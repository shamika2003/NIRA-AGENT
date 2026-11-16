using System.Text.Json;
using NIRAAgent.Goals;
using NIRAAgent.Branches;
using NIRAAgent.Mind;
using NIRAAgent.Runtime.Contracts;
using NIRAAgent.Runtime.General;
using NIRAAgent.Runtime.Runtime.AgentLoop;

namespace NIRAAgent.Runtime.HostAdapters;

// Goal proposals originate with NIRA during real work. Existing NIRAGoalService
// alone validates, deduplicates, and persists them. No parallel goal database.
public interface INIRASelfGoalExecutor
{
    bool HasOpenGoal(string objective);
    Task<NIRAGoalApplyResult> CreateAsync(NIRAMindEvent source, string objective,
        string criterion, CancellationToken ct);
    Task<NIRAGoalState?> ReadAsync(Guid id, CancellationToken ct);
}

// Extends the existing goal owner with existing authoritative branch services.
// A plan has no executable tool calls or background wake by itself.
public interface INIRASelfGoalStageExecutor
{
    Task<IReadOnlyList<NIRABranchState>> SaveStagesAsync(NIRAMindEvent source,
        Guid goalId, IReadOnlyList<NiraSelfGoalStage> stages, CancellationToken ct);
}

// Repair only an existing verified goal-only intention that has NO existing branches.
// Never auto-replay a partially committed stage plan or rewrite the goal.
public interface INIRASelfGoalGoalOnlyRepair
{
    NIRAGoalState? FindGoalWithoutStages(string objective);
}

public sealed class NIRASelfGoalExecutor : INIRASelfGoalExecutor, INIRASelfGoalStageExecutor, INIRASelfGoalGoalOnlyRepair
{
    private readonly NIRAGoalService _goals;
    private readonly NIRAGoalStore _store;
    private readonly NIRABranchService _branches;
    private readonly NIRABranchStore _branchStore;
    public NIRASelfGoalExecutor(NIRAGoalService goals, NIRAGoalStore store,
        NIRABranchService branches, NIRABranchStore branchStore)
    {
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _branchStore = branchStore ?? throw new ArgumentNullException(nameof(branchStore));
    }
    public bool HasOpenGoal(string objective) => _goals.CurrentGoals.Any(g =>
        g.IsOpen && string.Equals(g.Objective, objective, StringComparison.OrdinalIgnoreCase));
    public NIRAGoalState? FindGoalWithoutStages(string objective)
    {
        NIRAGoalState? goal = _goals.CurrentGoals.FirstOrDefault(g =>
            g.IsOpen && g.Status != NIRAGoalStatus.Blocked &&
            g.LastReason?.StartsWith("NIRA's internal work intention",
                StringComparison.Ordinal) == true &&
            g.Objective.Equals(objective, StringComparison.OrdinalIgnoreCase));
        return goal is not null && _branches.GetForGoal(goal.Id).Count == 0 ? goal : null;
    }
    public async Task<NIRAGoalState?> ReadAsync(Guid id, CancellationToken ct) =>
        (await _store.ReadGoalsAsync(ct)).SingleOrDefault(g => g.Id == id);
    public async Task<NIRAGoalApplyResult> CreateAsync(NIRAMindEvent source,
        string objective, string criterion, CancellationToken ct)
    {
        string quote = source.Content.Trim();
        quote = quote[..Math.Min(quote.Length, 220)];
        var proposal = new NIRAGoalProposal
        {
            Action = NIRAGoalProposalAction.Create,
            Objective = objective,
            CompletionCriteria = [criterion],
            Priority = 0.65,
            EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
            EvidenceQuote = quote,
            EvidenceSummary = "NIRA chose to keep a multi-step task from this current user event.",
            Reason = "NIRA's internal work intention, not a user-authored goal command " +
                "or permission to perform external actions.",
            Confidence = 0.91
            // No invented wake or independent runner; the existing scheduler
            // stays the only owner of scheduled work.
        };
        IReadOnlyList<NIRAGoalApplyResult> result = await _goals.ApplyProposalsAsync(
            source, string.Empty, string.Empty, [proposal], ct);
        if (result.Count != 1) throw new InvalidDataException("SELF_GOAL_OWNER_RESULT_COUNT");
        return result[0];
    }

    public async Task<IReadOnlyList<NIRABranchState>> SaveStagesAsync(
        NIRAMindEvent source, Guid goalId,
        IReadOnlyList<NiraSelfGoalStage> stages, CancellationToken ct)
    {
        if (goalId == Guid.Empty || stages.Count is < 2 or > 5 ||
            !_goals.TryGetGoal(goalId, out NIRAGoalState? goal) ||
            goal is null || !goal.IsOpen)
            throw new InvalidDataException("SELF_GOAL_PARENT_UNAVAILABLE");

        // Execution authority is not granted by a plan. Stage branches start
        // Waiting and require real authorized tool work before verification.
        NIRABranchProposal[] proposals = stages.Select(stage => new NIRABranchProposal
        {
            Action = NIRABranchProposalAction.Create,
            GoalId = goalId.ToString("D"),
            Objective = stage.Objective,
            CompletionCriteria = [stage.SuccessCriterion],
            JoinPolicy = NIRABranchJoinPolicy.Required,
            WaitingFor = "Awaiting authorized execution and independent result verification.",
            EvidenceSource = NIRABranchEvidenceSource.ExecutivePlan,
            EvidenceSummary = "NIRA stage proposal from an authorized current user task.",
            Reason = "Unexecuted stage in NIRA's persistent work plan.",
            Confidence = 0.90
        }).ToArray();
        IReadOnlyList<NIRABranchApplyResult> results = await _branches.ApplyProposalsAsync(
            source, string.Empty, string.Empty, proposals, ct);
        if (results.Count != stages.Count || results.Any(r =>
                r.Action is not (NIRABranchApplyAction.Created or NIRABranchApplyAction.Duplicate) ||
                r.Branch is null))
            throw new InvalidDataException("SELF_GOAL_STAGE_WRITE_INCOMPLETE");
        IReadOnlyList<NIRABranchState> disk = await _branchStore.ReadBranchesAsync(ct);
        var verified = new List<NIRABranchState>(stages.Count);
        for (int i = 0; i < stages.Count; i++)
        {
            NIRABranchState branch = results[i].Branch!;
            NIRABranchState? saved = disk.FirstOrDefault(x => x.Id == branch.Id);
            if (saved is null || saved.GoalId != goalId || saved.ParentBranchId is not null ||
                saved.IsResolved || saved.JoinPolicy != NIRABranchJoinPolicy.Required ||
                saved.EvidenceCount < 1 ||
                !saved.Objective.Equals(stages[i].Objective, StringComparison.Ordinal) ||
                !saved.CompletionCriteria.Contains(stages[i].SuccessCriterion,
                    StringComparer.Ordinal))
                throw new InvalidDataException("SELF_GOAL_STAGE_READBACK_MISMATCH");
            verified.Add(saved);
        }
        if (verified.Select(x => x.Id).Distinct().Count() != stages.Count)
            throw new InvalidDataException("SELF_GOAL_STAGE_DUPLICATE_ID");
        return verified;
    }
}

public sealed record NiraSelfGoalStage(string Objective, string SuccessCriterion);
public sealed record NiraSelfGoalPlan(string Objective, string SuccessCriterion,
    IReadOnlyList<NiraSelfGoalStage> Stages);

public static class NIRASelfGoalRegistration
{
    public const string ConsentValue = "ALLOW_NIRA_WORK_GOALS";

    public static bool IsEligible(string? consent, bool lifecycleConsented,
        string? request, NIRAMindEvent? source)
    {
        if (!string.Equals(consent, ConsentValue, StringComparison.Ordinal) ||
            !lifecycleConsented || source is null || (source.Source != NIRAMindEventSource.User ||
             source.Name != "UserMessage") ||
            string.IsNullOrWhiteSpace(request) || request.Length is < 35 or > 12000)
            return false;
        string original = source.Content.Trim();
        string task = request.Trim();
        if (!string.Equals(original, task, StringComparison.Ordinal) &&
            !(original.StartsWith("/v2 ", StringComparison.OrdinalIgnoreCase) &&
              string.Equals(original[4..].Trim(), task, StringComparison.Ordinal)))
            return false;
        if (original.IndexOf('\0') >= 0 ||
            NIRALifecycleContextPolicy.IsTaskContinuationRequest(task)) return false;
        // Explicit user-authored lifecycle commands already have their own
        // audited tools, and must never be intercepted by self-planning.
        string lower = task.ToLowerInvariant();
        return !lower.StartsWith("create goal ", StringComparison.Ordinal) &&
               !lower.StartsWith("complete goal ", StringComparison.Ordinal) &&
               !lower.StartsWith("create branch ", StringComparison.Ordinal) &&
               !lower.StartsWith("complete branch ", StringComparison.Ordinal) &&
               !lower.StartsWith("inspect saved ", StringComparison.Ordinal);
    }

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        INIRASelfGoalExecutor executor, string? consent, bool lifecycleConsented,
        string request, NIRAMindEvent source)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (!IsEligible(consent, lifecycleConsented, request, source)) return [];
        return [new NIRASelfGoalCapability(vault, executor, source, request)];
    }

    public static bool TryValidatePlan(JsonElement args, out NiraSelfGoalPlan? plan)
    {
        plan = null;
        if (args.ValueKind != JsonValueKind.Object ||
            args.EnumerateObject().Count() is < 2 or > 3 ||
            !args.TryGetProperty("objective", out JsonElement objective) ||
            !args.TryGetProperty("successCriterion", out JsonElement criterion) ||
            objective.ValueKind != JsonValueKind.String ||
            criterion.ValueKind != JsonValueKind.String)
            return false;
        string? obj = objective.GetString()?.Trim();
        string? crit = criterion.GetString()?.Trim();
        if (!IsSafeText(obj, 12, 180) || !IsSafeText(crit, 16, 220) ||
            obj!.Equals(crit, StringComparison.OrdinalIgnoreCase) ||
            crit!.Equals("done", StringComparison.OrdinalIgnoreCase) ||
            crit.Equals("task completed", StringComparison.OrdinalIgnoreCase)) return false;

        var stages = new List<NiraSelfGoalStage>();
        if (args.TryGetProperty("stages", out JsonElement steps))
        {
            if (steps.ValueKind != JsonValueKind.Array ||
                steps.GetArrayLength() is < 2 or > 5) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement step in steps.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object ||
                    step.EnumerateObject().Count() != 2 ||
                    !step.TryGetProperty("objective", out JsonElement title) ||
                    !step.TryGetProperty("successCriterion", out JsonElement proof) ||
                    title.ValueKind != JsonValueKind.String ||
                    proof.ValueKind != JsonValueKind.String) return false;
                string? titleText = title.GetString()?.Trim();
                string? proofText = proof.GetString()?.Trim();
                if (!IsSafeText(titleText, 12, 140) ||
                    !IsSafeText(proofText, 16, 220) ||
                    titleText!.Equals(proofText, StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(titleText)) return false;
                stages.Add(new NiraSelfGoalStage(titleText, proofText!));
            }
        }
        plan = new NiraSelfGoalPlan(obj, crit, stages);
        return true;
    }

    private static bool IsSafeText(string? value, int min, int max) =>
        value is not null && value.Length >= min && value.Length <= max &&
        !value.Any(char.IsControl);

}

public sealed class NIRASelfGoalCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly INIRASelfGoalExecutor _executor;
    private readonly NIRAMindEvent _source;

    public NIRASelfGoalCapability(AgentPayloadVault vault,
        INIRASelfGoalExecutor executor, NIRAMindEvent source, string request)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (!NIRASelfGoalRegistration.IsEligible(NIRASelfGoalRegistration.ConsentValue,
                true, request, source))
            throw new ArgumentException("Self-goal source must be a live authorized task.");
    }

    public AgentToolSpec Spec { get; } = new(
        "nira.lifecycle.goal.plan_self",
        "NIRA may choose to save ONE internal work goal for a substantial multi-step " +
        "task requiring future review or continuity. Use a specific objective and " +
        "testable success criterion grounded in the CURRENT user request. " +
        "Not for chat, a one-shot answer, or an already finished task. " +
        "Saving a goal does not complete or schedule the work.", AccessLevel.Modify);
    public string FunctionName => "nira_plan_persistent_work_goal";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            objective = new { type = "string", description = "NIRA task objective (12-180 chars)" },
            successCriterion = new { type = "string", description = "Verifiable final result (16-220 chars)" },
            stages = new { type = "array", minItems = 2, maxItems = 5,
                description = "Two to five concrete, unfinished, verifiable work stages. No invented completed evidence.",
                items = new { type = "object", properties = new {
                    objective = new { type = "string" },
                    successCriterion = new { type = "string" }
                }, required = new[] { "objective", "successCriterion" }, additionalProperties = false }
            }
        },
        required = new[] { "objective", "successCriterion", "stages" }, additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) =>
        NIRASelfGoalRegistration.TryValidatePlan(args, out NiraSelfGoalPlan? plan) &&
        plan is not null && plan.Stages.Count is >= 2 and <= 5;
    public bool IsSafeToPlan(JsonElement args) =>
        NIRASelfGoalRegistration.TryValidatePlan(args, out NiraSelfGoalPlan? plan) &&
        plan is not null && plan.Stages.Count is >= 2 and <= 5 &&
        (!_executor.HasOpenGoal(plan.Objective) ||
         (_executor is INIRASelfGoalGoalOnlyRepair repair &&
          repair.FindGoalWithoutStages(plan.Objective) is not null));

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        NiraSelfGoalPlan? plan;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!NIRASelfGoalRegistration.TryValidatePlan(args, out plan) ||
                plan is null || plan.Stages.Count is < 2 or > 5)
                return AgentToolReceipt.ConfirmedNoChange("SELF_GOAL_ARGS_INVALID");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return AgentToolReceipt.ConfirmedNoChange("SELF_GOAL_REFERENCE_INVALID"); }
        NIRAGoalState? reuse = (_executor as INIRASelfGoalGoalOnlyRepair)?
            .FindGoalWithoutStages(plan.Objective);
        if (_executor.HasOpenGoal(plan.Objective) && reuse is null)
            return AgentToolReceipt.ConfirmedNoChange("SELF_GOAL_ALREADY_HAS_STAGES");

        NIRAGoalApplyResult result;
        try
        {
            result = reuse is not null
                ? new NIRAGoalApplyResult { Action = NIRAGoalApplyAction.Duplicate,
                    Goal = reuse, Reason = "Complete a previously saved goal-only plan." }
                : await _executor.CreateAsync(_source, plan.Objective, plan.SuccessCriterion, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return AgentToolReceipt.Uncertain("SELF_GOAL_COMMIT_UNKNOWN"); }
        if (result.Action == NIRAGoalApplyAction.Rejected ||
            result.Action == NIRAGoalApplyAction.NoChange)
            return AgentToolReceipt.ConfirmedNoChange("SELF_GOAL_OWNER_REJECTED");
        if (result.Action is not (NIRAGoalApplyAction.Created or NIRAGoalApplyAction.Duplicate) ||
            result.Goal is null)
            return AgentToolReceipt.Uncertain("SELF_GOAL_RECEIPT_UNKNOWN");

        try
        {
            NIRAGoalState? stored = await _executor.ReadAsync(result.Goal.Id, ct);
            if (stored is null || stored.Id != result.Goal.Id || !stored.IsOpen ||
                stored.EvidenceCount < 1 ||
                !stored.Objective.Equals(plan.Objective, StringComparison.Ordinal) ||
                (result.Action == NIRAGoalApplyAction.Created &&
                 !stored.CompletionCriteria.Contains(plan.SuccessCriterion, StringComparer.Ordinal)) ||
                (result.Action == NIRAGoalApplyAction.Created &&
                 (stored.SourceEventId != _source.Id || stored.NextWakeAtUtc is not null ||
                  stored.Status != NIRAGoalStatus.Active)))
                return AgentToolReceipt.Uncertain("SELF_GOAL_READBACK_MISMATCH");
            IReadOnlyList<NIRABranchState> stages = Array.Empty<NIRABranchState>();
            if (plan.Stages.Count > 0)
            {
                if (_executor is not INIRASelfGoalStageExecutor stageExecutor)
                    return AgentToolReceipt.Uncertain("SELF_GOAL_STAGE_OWNER_UNAVAILABLE");
                // Failure after an authoritative goal/branch commit is UNKNOWN.
                // Never retry this plan automatically or claim it was rolled back.
                stages = await stageExecutor.SaveStagesAsync(_source, stored.Id, plan.Stages, ct);
                if (stages.Count != plan.Stages.Count ||
                    stages.Select(x => x.Id).Distinct().Count() != stages.Count ||
                    stages.Where((stage, i) => stage.Id == Guid.Empty ||
                        stage.GoalId != stored.Id || stage.IsResolved ||
                        stage.JoinPolicy != NIRABranchJoinPolicy.Required ||
                        stage.EvidenceCount < 1 ||
                        !stage.Objective.Equals(plan.Stages[i].Objective, StringComparison.Ordinal) ||
                        !stage.CompletionCriteria.Contains(plan.Stages[i].SuccessCriterion,
                            StringComparer.Ordinal)).Any())
                    return AgentToolReceipt.Uncertain("SELF_GOAL_STAGE_COUNT_OR_CONTENT_MISMATCH");
            }
            JsonElement receipt = JsonSerializer.SerializeToElement(new
            {
                outcome = "nira_owned_intent_verified", goalId = stored.Id.ToString("D"),
                objective = stored.Objective,
                successCriterion = stored.CompletionCriteria.FirstOrDefault() ?? plan.SuccessCriterion,
                reusedExistingGoal = reuse is not null,
                status = stored.Status.ToString(), completed = false,
                stages = stages.Select(x => new {
                    branchId = x.Id.ToString("D"), objective = x.Objective,
                    status = x.Status.ToString(), completed = false
                }).ToArray(),
                stageCount = stages.Count,
                backgroundExecutionScheduled = false, owner = "Existing NIRAGoalService and NIRABranchService",
                verifiedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, receipt, ct);
            return AgentToolReceipt.Verified(token);
        }
        catch (Exception) { return AgentToolReceipt.Uncertain("SELF_GOAL_READBACK_UNKNOWN"); }
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Authorization;
using NIRAAgent.Capabilities;
using NIRAAgent.Branches;
using NIRAAgent.Goals;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

/// <summary>Uses the existing branch service and database, not a V2 branch store.</summary>
public interface ILegacyBranchCreateExecutor
{
    bool CanCreate(Guid goalId, string objective);
    Task<NIRABranchApplyResult> CreateAsync(NIRAMindEvent source,
        Guid goalId, string objective, string criterion, CancellationToken ct);
    Task<NIRABranchState?> ReadPersistedAsync(Guid branchId, CancellationToken ct);
}

public sealed class NIRALegacyBranchCreateExecutor : ILegacyBranchCreateExecutor
{
    private readonly NIRABranchService _branches;
    private readonly NIRABranchStore _store;
    private readonly NIRAGoalService _goals;

    public NIRALegacyBranchCreateExecutor(NIRABranchService branches,
        NIRABranchStore store, NIRAGoalService goals)
    {
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
    }

    public bool CanCreate(Guid goalId, string objective) =>
        _goals.TryGetGoal(goalId, out NIRAGoalState? goal) &&
        goal is { IsOpen: true } && goal.Status != NIRAGoalStatus.Blocked &&
        !_branches.CurrentBranches.Any(b => b.IsOpen && b.GoalId == goalId &&
            b.ParentBranchId is null &&
            string.Equals(b.Objective, objective, StringComparison.OrdinalIgnoreCase));

    public async Task<NIRABranchState?> ReadPersistedAsync(Guid branchId, CancellationToken ct) =>
        (await _store.ReadBranchesAsync(ct)).SingleOrDefault(b => b.Id == branchId);

    public async Task<NIRABranchApplyResult> CreateAsync(NIRAMindEvent source,
        Guid goalId, string objective, string criterion, CancellationToken ct)
    {
        // The objective and criterion came from the actual direct user event.
        // WAITING means no delegated work, no automatic branch execution.
        var proposal = new NIRABranchProposal
        {
            Action = NIRABranchProposalAction.Create,
            GoalId = goalId.ToString("D"),
            Objective = objective,
            JoinPolicy = NIRABranchJoinPolicy.Required,
            DispatchMode = NIRABranchDispatchMode.Inline,
            CompletionCriteria = [criterion],
            WaitingFor = "Awaiting explicit owner activation; do not run branch work.",
            EvidenceSource = NIRABranchEvidenceSource.CurrentEvent,
            EvidenceQuote = objective,
            EvidenceSummary = "Exact user-requested goal-linked work branch.",
            Reason = "Recorded as Waiting. Future work is not authorized by creation.",
            Confidence = 1.0
        };
        var results = await _branches.ApplyProposalsAsync(source,
            string.Empty, string.Empty, [proposal], ct);
        if (results.Count != 1) throw new InvalidDataException("BRANCH_OWNER_RECEIPT_COUNT_INVALID");
        return results[0];
    }
}

public sealed record ExactBranchCreateRequest(Guid GoalId, string Objective, string Criterion);

public static class LegacyBranchCreateRegistration
{
    public const string ConsentValue = "ALLOW_EXPLICIT_BRANCH_CREATE";
    private static readonly Regex Pattern = new(
        "^\\s*Create\\s+(?:a\\s+)?branch\\s+for\\s+goal\\s+(?<id>[0-9a-fA-F-]{36})\\s+named\\s+\\\"(?<objective>[^\\\"\\r\\n]{1,180})\\\"\\s+with\\s+(?:success\\s+)?criterion\\s+\\\"(?<criterion>[^\\\"\\r\\n]{1,180})\\\"\\s*\\.?\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex Intent = new(
        "^\\s*Create\\s+(?:a\\s+)?branch\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    public static bool TryParse(string? text, out ExactBranchCreateRequest? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 650 ||
            text.Any(char.IsControl)) return false;
        var match = Pattern.Match(text);
        if (!match.Success || !Guid.TryParseExact(match.Groups["id"].Value, "D", out Guid id) ||
            id == Guid.Empty) return false;
        string objective = match.Groups["objective"].Value.Trim();
        string criterion = match.Groups["criterion"].Value.Trim();
        if (objective.Length is < 5 or > 180 || criterion.Length is < 5 or > 180 ||
            objective.Equals(criterion, StringComparison.OrdinalIgnoreCase) ||
            objective.StartsWith("do not ", StringComparison.OrdinalIgnoreCase)) return false;
        parsed = new ExactBranchCreateRequest(id, objective, criterion);
        return true;
    }

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyBranchCreateExecutor executor, string? consent, bool lifecycleConsented,
        string text, NIRAMindEvent source)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(source);
        if (source.Source != NIRAMindEventSource.User ||
            !string.Equals(source.Content.Trim(), text.Trim(), StringComparison.Ordinal) ||
            consent != ConsentValue || !lifecycleConsented ||
            !TryParse(text, out ExactBranchCreateRequest? parsed) || parsed is null ||
            !executor.CanCreate(parsed.GoalId, parsed.Objective)) return [];
        return [new LegacyBranchCreateCapability(vault, executor, source, parsed)];
    }

    public static string? ExplainUnavailable(string text, string? consent,
        bool lifecycleConsented, bool registered)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 650 || !Intent.IsMatch(text)) return null;
        if (registered) return null;
        if (consent != ConsentValue || !lifecycleConsented)
            return "Branch creation is disabled. Launch with -AllowPersonalContext " +
                "-AllowLifecycleContext -AllowBranchCreate. No branch was changed.";
        return "No branch was created. Use: Create branch for goal <GUID> named " +
            "\"objective\" with success criterion \"evidence of completion\". " +
            "The parent must be an open, unblocked goal and the branch must not exist already.";
    }
}

public sealed class LegacyBranchCreateCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyBranchCreateExecutor _executor;
    private readonly NIRAMindEvent _source;
    private readonly ExactBranchCreateRequest _request;
    public NIRACapabilityRequest ApprovalRequest { get; }

    public LegacyBranchCreateCapability(AgentPayloadVault vault,
        ILegacyBranchCreateExecutor executor, NIRAMindEvent source,
        ExactBranchCreateRequest request)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (source.Source != NIRAMindEventSource.User ||
            !LegacyBranchCreateRegistration.TryParse(source.Content, out var parsed) ||
            parsed != request) throw new ArgumentException("Untrusted branch creation event.");
        ApprovalRequest = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(),
            CapabilityId = "lifecycle.branch.create",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                operation = "create_goal_linked_branch",
                goalId = request.GoalId.ToString("D"),
                objective = request.Objective,
                successCriterion = request.Criterion,
                initialStatus = "Waiting",
                dispatch = "None",
                owner = "ExistingNIRABranchService"
            }),
            Reason = "Direct user requested this exact goal-linked branch."
        };
    }

    public AgentToolSpec Spec { get; } = new(
        "legacy.lifecycle.branch.create_exact",
        "Create exactly ONE branch in the existing NIRA branch store for the " +
        "user-supplied parent goal ID. WAITING; does not execute any work. " +
        "Fresh owner Allow Once required. Empty arguments only.", AccessLevel.Modify);
    public string FunctionName => "nira_create_exact_branch";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && !args.EnumerateObject().Any();
    public bool IsStillAllowed() => _executor.CanCreate(_request.GoalId, _request.Objective);

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!ValidateArguments(args)) return AgentToolReceipt.ConfirmedNoChange("BRANCH_ARGUMENTS_INVALID");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException)
        { return AgentToolReceipt.ConfirmedNoChange("BRANCH_REFERENCE_INVALID"); }
        if (!IsStillAllowed())
            return AgentToolReceipt.ConfirmedNoChange("BRANCH_ALREADY_EXISTS_OR_PARENT_CHANGED");

        NIRABranchApplyResult result;
        try
        {
            result = await _executor.CreateAsync(_source, _request.GoalId,
                _request.Objective, _request.Criterion, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return AgentToolReceipt.Uncertain("BRANCH_COMMIT_OUTCOME_UNKNOWN"); }

        if (result.Action is NIRABranchApplyAction.Rejected or NIRABranchApplyAction.NoChange)
            return AgentToolReceipt.ConfirmedNoChange("BRANCH_OWNER_REJECTED");
        if (result.Action != NIRABranchApplyAction.Created || result.Branch is null)
            return AgentToolReceipt.Uncertain("BRANCH_OWNER_RECEIPT_UNEXPECTED");

        try
        {
            var stored = await _executor.ReadPersistedAsync(result.Branch.Id, ct);
            if (stored is null || stored.Id == Guid.Empty ||
                stored.GoalId != _request.GoalId ||
                stored.Status != NIRABranchStatus.Waiting ||
                stored.ParentBranchId is not null ||
                stored.SourceEventId != _source.Id ||
                !string.Equals(stored.Objective, _request.Objective, StringComparison.Ordinal) ||
                !stored.CompletionCriteria.Contains(_request.Criterion, StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(stored.WaitingFor))
                return AgentToolReceipt.Uncertain("BRANCH_READBACK_MISMATCH");
            var receipt = JsonSerializer.SerializeToElement(new
            {
                outcome = "created_and_sqlite_readback_verified",
                branchId = stored.Id.ToString("D"),
                goalId = stored.GoalId.ToString("D"),
                objective = stored.Objective,
                completionCriterion = _request.Criterion,
                status = stored.Status.ToString(),
                changed = true,
                executionDispatched = false,
                owner = "ExistingNIRABranchService",
                verifiedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, receipt, ct);
            return AgentToolReceipt.Verified(token);
        }
        catch (Exception) { return AgentToolReceipt.Uncertain("BRANCH_READBACK_RECONCILIATION_REQUIRED"); }
    }
}

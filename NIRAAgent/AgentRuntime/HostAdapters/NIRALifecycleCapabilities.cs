using System.IO;
using System.Text.Json;
using NIRAAgent.Branches;
using NIRAAgent.Capabilities;
using NIRAAgent.Goals;
using NIRAAgent.Mind;
using NIRAAgent.Runtime.Contracts;
using NIRAAgent.Runtime.General;
using NIRAAgent.Runtime.Runtime.AgentLoop;

namespace NIRAAgent.Runtime.HostAdapters;

/// <summary>
/// Host-bound interface to existing durable goal, branch and work services.
/// This is NOT a second planner: model proposals pass through the original
/// authoritative stores, evidence gates, dependency checks and work queue.
/// </summary>
public static class NIRALifecycleCapabilities
{
    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        NIRAMindEvent source, NIRAGoalService goals, NIRABranchService branches,
        NIRABranchWorkService work, bool contextAllowed)
    {
        if (!contextAllowed ||
            (source.Source == NIRAMindEventSource.User &&
             (source.Name != "UserMessage" ||
              !(NIRALifecycleContextPolicy.IsRelevantRequest(source.Content) ||
                NIRALifecycleContextPolicy.IsTaskContinuationRequest(source.Content)))))
            return Array.Empty<IGeneralCapability>();
        var status = new Status(vault, source, goals, branches, work);
        if (!Change.CanBind(source, goals)) return [status];
        return [status, new Change(vault, source, goals, branches, work)];
    }

    public sealed class Status : IGeneralCapability
    {
        private readonly AgentPayloadVault _vault;
        private readonly NIRAMindEvent _event;
        private readonly NIRAGoalService _goals;
        private readonly NIRABranchService _branches;
        private readonly NIRABranchWorkService _work;
        public Status(AgentPayloadVault vault, NIRAMindEvent source,
            NIRAGoalService goals, NIRABranchService branches, NIRABranchWorkService work)
        { _vault = vault; _event = source; _goals = goals; _branches = branches; _work = work; }
        public AgentToolSpec Spec { get; } = new("nira.lifecycle.inspect",
            "Inspect current authoritative goal, branch and assigned-work status. A saved objective is not proof of completed work.", AccessLevel.Observe);
        public string FunctionName => "nira_inspect_lifecycle";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new {
            type = "object", properties = new {
                goalId = new { type = "string", description = "Optional exact goal ID. On internal events only the event-owned goal is readable." }
            }, additionalProperties = false
        });
        public bool ValidateArguments(JsonElement args) =>
            args.ValueKind == JsonValueKind.Object && args.EnumerateObject().All(p =>
                p.NameEquals("goalId") && p.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(p.Value.GetString(), out Guid id) && id != Guid.Empty);
        public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
        {
            Guid runId = _vault.RunIdFromArgument(reference);
            if (runId != _event.Id) return AgentToolReceipt.Uncertain("LIFECYCLE_RUN_MISMATCH");
            JsonElement args = await _vault.ReadArgumentAsync(runId, Spec.Id, reference, ct);
            if (!ValidateArguments(args)) return AgentToolReceipt.Uncertain("LIFECYCLE_INSPECT_ARGS_INVALID");
            Guid? selected = args.TryGetProperty("goalId", out JsonElement g) ? Guid.Parse(g.GetString()!) : null;
            if (Change.EventGoal(_event) is Guid owned && selected is Guid other && owned != other)
                return AgentToolReceipt.Uncertain("LIFECYCLE_SCOPE_MISMATCH");
            Guid? goalId = Change.EventGoal(_event) ?? selected;
            var goals = _goals.CurrentGoals.Where(g => !goalId.HasValue || g.Id == goalId)
                .Take(12).Select(g => new { id = g.Id, g.Objective, status = g.Status.ToString(),
                    g.CompletionCriteria, g.WaitingFor, g.Blocker, g.NextWakeAtUtc,
                    g.LastEvidenceSummary, g.EvidenceCount }).ToArray();
            var branches = _branches.CurrentBranches.Where(b => !goalId.HasValue || b.GoalId == goalId)
                .Take(20).Select(b => new { id = b.Id, goalId = b.GoalId, b.Objective,
                    status = b.Status.ToString(), b.CompletionCriteria, b.ResultSummary,
                    b.WaitingFor, b.Blocker, b.EvidenceCount }).ToArray();
            var work = _work.CurrentWork.Where(w => !goalId.HasValue || w.GoalId == goalId)
                .OrderByDescending(w => w.CreatedAtUtc).Take(20)
                .Select(w => new { id = w.Id, branchId = w.BranchId, goalId = w.GoalId,
                    status = w.Status.ToString(), capabilityId = w.CapabilityRequest?.CapabilityId,
                    w.ResultSummary }).ToArray();
            string token = await _vault.SaveResultAsync(runId, Spec.Id,
                JsonSerializer.SerializeToElement(new { goalId, goals, branches, assignedWork = work,
                    note = "Read from authoritative stores; a queued action has not yet succeeded." }), ct);
            return AgentToolReceipt.ReadOnlyResult(token);
        }
    }

    public sealed class Change : IGeneralCapability
    {
        private readonly AgentPayloadVault _vault;
        private readonly NIRAMindEvent _event;
        private readonly NIRAGoalService _goals;
        private readonly NIRABranchService _branches;
        private readonly NIRABranchWorkService _work;
        public Change(AgentPayloadVault vault, NIRAMindEvent source,
            NIRAGoalService goals, NIRABranchService branches, NIRABranchWorkService work)
        { _vault = vault; _event = source; _goals = goals; _branches = branches; _work = work; }
        public AgentToolSpec Spec { get; } = new("nira.lifecycle.update",
            "For an authorized direct-user goal continuation or a host-owned internal goal/branch event: propose a grounded goal/branch transition, create a required root branch, or QUEUE one branch-owned capability. The runtime validates dependencies and audits actual capability execution separately. Completing tasks requires authoritative completion evidence; a plan or wake is not completion evidence.", AccessLevel.Modify);
        public string FunctionName => "nira_update_lifecycle";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new {
            type = "object", properties = new {
                kind = new { type = "string", @enum = new[] { "goal", "branch", "work" } },
                action = new { type = "string", @enum = new[] { "create", "progress", "activate", "wait", "block", "complete", "fail", "assign" } },
                targetId = new { type = "string", description = "Existing goal/branch GUID. For branch create: owning goal ID." },
                evidenceQuote = new { type = "string", description = "Exact substring of this host-issued internal event; never invent evidence." },
                reason = new { type = "string" },
                objective = new { type = "string", description = "Required for branch creation" },
                successCriterion = new { type = "string", description = "Required for branch creation" },
                waitingFor = new { type = "string" }, blocker = new { type = "string" },
                nextWakeUtc = new { type = "string", description = "ISO-8601 UTC wake for goal wait" },
                resultSummary = new { type = "string", description = "Required for branch completion" },
                capabilityId = new { type = "string", description = "For work assign: registered capability ID" },
                arguments = new { type = "object", description = "For work assign: exact capability arguments" }
            }, required = new[] { "kind", "action", "targetId", "evidenceQuote", "reason" },
            additionalProperties = false
        });

        public static Guid? EventGoal(NIRAMindEvent evt, NIRAGoalService? goals = null)
        {
            if (evt.Metadata.TryGetValue("goalId", out string? raw) &&
                Guid.TryParse(raw, out Guid trusted) && trusted != Guid.Empty)
                return trusted;
            if (evt.Source != NIRAMindEventSource.User || evt.Name != "UserMessage" ||
                !NIRALifecycleContextPolicy.IsTaskContinuationRequest(evt.Content)) return null;
            var ids = System.Text.RegularExpressions.Regex.Matches(evt.Content,
                @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
            if (ids.Count == 1 && Guid.TryParse(ids[0].Value, out Guid explicitId))
                return explicitId;
            if (ids.Count != 0 || goals is null ||
                !System.Text.RegularExpressions.Regex.IsMatch(evt.Content,
                    @"\b(goal|project|task|work)\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100))) return null;
            NIRAGoalState[] open = goals.CurrentGoals.Where(g => g.IsOpen).Take(2).ToArray();
            return open.Length == 1 ? open[0].Id : null;
        }
        public static bool CanBind(NIRAMindEvent evt, NIRAGoalService goals) =>
            ((evt.Source == NIRAMindEventSource.Internal &&
              evt.Name is ("PersistentGoalWake" or "PersistentBranchResult" or
                  "PersistentBranchWorkResult" or "PersistentBranchWorkResultBatch" or
                  "PersistentBranchWorkReconsideration")) ||
             (evt.Source == NIRAMindEventSource.User && evt.Name == "UserMessage" &&
              NIRALifecycleContextPolicy.IsTaskContinuationRequest(evt.Content))) &&
            EventGoal(evt, goals) is Guid id && goals.TryGetGoal(id, out NIRAGoalState? g) &&
            g is { IsOpen: true };
        private static bool Text(JsonElement a, string name, int min, int max, bool optional = false)
        {
            if (!a.TryGetProperty(name, out JsonElement v)) return optional;
            if (v.ValueKind != JsonValueKind.String) return false;
            string? s = v.GetString();
            return s is not null && s.Length >= min && s.Length <= max &&
                !s.Any(char.IsControl);
        }
        private static string Get(JsonElement a, string name) =>
            a.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim() : string.Empty;
        private static readonly HashSet<string> FieldNames = new(StringComparer.Ordinal) {
            "kind", "action", "targetId", "evidenceQuote", "reason", "objective",
            "successCriterion", "waitingFor", "blocker", "nextWakeUtc", "resultSummary",
            "capabilityId", "arguments" };
        public bool ValidateArguments(JsonElement a)
        {
            if (a.ValueKind != JsonValueKind.Object ||
                a.EnumerateObject().Any(p => !FieldNames.Contains(p.Name)) ||
                !Text(a, "kind", 4, 6) || !Text(a, "action", 4, 10) ||
                !Text(a, "targetId", 36, 36) || !Text(a, "evidenceQuote", 8, 320) ||
                !Text(a, "reason", 8, 500) ||
                !Text(a, "objective", 12, 180, true) ||
                !Text(a, "successCriterion", 16, 240, true) ||
                !Text(a, "waitingFor", 6, 500, true) ||
                !Text(a, "blocker", 6, 500, true) ||
                !Text(a, "resultSummary", 12, 1000, true) ||
                !Text(a, "nextWakeUtc", 20, 50, true) ||
                !Text(a, "capabilityId", 4, 160, true) ||
                !Guid.TryParse(Get(a, "targetId"), out Guid id) || id == Guid.Empty ||
                !a.EnumerateObject().Any(p => p.NameEquals("evidenceQuote")) ||
                !_event.Content.Contains(Get(a, "evidenceQuote"), StringComparison.Ordinal)) return false;
            string kind = Get(a, "kind"), action = Get(a, "action");
            if (kind == "goal" && action is not ("progress" or "activate" or "wait" or "block" or "complete")) return false;
            if (kind == "branch" && action is not ("create" or "progress" or "activate" or "wait" or "block" or "complete" or "fail")) return false;
            if (kind == "work" && action != "assign") return false;
            if (kind is not ("goal" or "branch" or "work")) return false;
            if (action == "create" && (Get(a, "objective").Length < 12 ||
                Get(a, "successCriterion").Length < 16)) return false;
            if (action == "wait" && Get(a, "waitingFor").Length < 6) return false;
            if (action == "block" && Get(a, "blocker").Length < 6) return false;
            if (action == "complete" && kind == "branch" && Get(a, "resultSummary").Length < 12) return false;
            if (action == "assign" && (Get(a, "capabilityId").Length < 4 ||
                !a.TryGetProperty("arguments", out JsonElement arguments) ||
                arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 16000)) return false;
            if (Get(a, "nextWakeUtc").Length > 0 &&
                (!DateTimeOffset.TryParse(Get(a, "nextWakeUtc"), out DateTimeOffset wake) ||
                 wake.Offset != TimeSpan.Zero || wake < DateTimeOffset.UtcNow.AddSeconds(20) ||
                 wake > DateTimeOffset.UtcNow.AddDays(365))) return false;
            return true;
        }

        public bool IsBoundAndSafe(JsonElement args)
        {
            if (!CanBind(_event, _goals) || !ValidateArguments(args) ||
                EventGoal(_event, _goals) is not Guid goalId ||
                !_goals.TryGetGoal(goalId, out NIRAGoalState? goal) ||
                goal is not { IsOpen: true }) return false;
            string kind = Get(args, "kind"), action = Get(args, "action");
            Guid id = Guid.Parse(Get(args, "targetId"));
            if (kind == "goal")
            {
                if (goalId != id) return false;
                // A scheduled wake or a model-generated statement is NOT proof of completion.
                if (action == "complete")
                    return _event.Name == "PersistentBranchResult" &&
                        _event.Metadata.TryGetValue("branchStatus", out string? status) &&
                        status == "Completed" &&
                        _event.Content.Contains("Final branch status:\nCompleted", StringComparison.Ordinal);
                return true;
            }
            if (kind == "branch" && action == "create") return id == goalId;
            if (!_branches.TryGetBranch(id, out NIRABranchState? branch) ||
                branch is null || branch.GoalId != goalId || branch.IsResolved) return false;
            if (action == "complete")
            {
                if (_event.Name != "PersistentBranchWorkResult" ||
                    !_event.Metadata.TryGetValue("branchId", out string? sourceBranch) ||
                    !Guid.TryParse(sourceBranch, out Guid observedId) || observedId != id ||
                    !_event.Metadata.TryGetValue("workStatus", out string? status) ||
                    status != "Succeeded") return false;
                // A completed assigned work result is necessary, but not itself sufficient;
                // the authoritative branch service also enforces dependency/child proofs.
            }
            if (action == "fail" && kind == "branch" &&
                _event.Name != "PersistentBranchWorkResult") return false;
            if (kind == "work" && _work.CurrentWork.Any(w => w.BranchId == id && w.IsOpen))
                return false;
            return true;
        }

        public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
        {
            Guid runId = _vault.RunIdFromArgument(reference);
            if (runId != _event.Id) return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_RUN_MISMATCH");
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!IsBoundAndSafe(args)) return AgentToolReceipt.ConfirmedNoChange("LIFECYCLE_SOURCE_OR_STATE_REJECTED");
            string kind = Get(args, "kind"), action = Get(args, "action"), reason = Get(args, "reason");
            string quote = Get(args, "evidenceQuote");
            Guid id = Guid.Parse(Get(args, "targetId"));
            if (kind == "goal")
            {
                NIRAGoalProposalAction transition = action switch {
                    "progress" => NIRAGoalProposalAction.RecordProgress,
                    "activate" => NIRAGoalProposalAction.Activate,
                    "wait" => NIRAGoalProposalAction.SetWaiting,
                    "block" => NIRAGoalProposalAction.SetBlocked,
                    "complete" => NIRAGoalProposalAction.Complete,
                    _ => throw new InvalidDataException("Unknown goal transition") };
                var proposal = new NIRAGoalProposal { Action = transition, GoalId = id.ToString("D"),
                    EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent, EvidenceQuote = quote,
                    EvidenceSummary = reason, Reason = reason, Confidence = 0.96,
                    WaitingFor = Get(args, "waitingFor"), Blocker = Get(args, "blocker"),
                    NextWakeAtUtc = Get(args, "nextWakeUtc") is string wake && wake.Length > 0
                        ? DateTimeOffset.Parse(wake) : null };
                NIRAGoalApplyResult result = (await _goals.ApplyProposalsAsync(_event, "", "",
                    [proposal], ct)).Single();
                if (result.Action == NIRAGoalApplyAction.Rejected)
                    return AgentToolReceipt.ConfirmedNoChange("GOAL_TRANSITION_REJECTED");
                string receipt = await _vault.SaveResultAsync(runId, Spec.Id,
                    JsonSerializer.SerializeToElement(new { kind, action, result = result.Action.ToString(),
                        goalId = result.Goal?.Id, status = result.Goal?.Status.ToString(), result.Reason }), ct);
                return AgentToolReceipt.Verified(receipt);
            }
            if (kind == "branch")
            {
                NIRABranchProposalAction transition = action switch {
                    "create" => NIRABranchProposalAction.Create,
                    "progress" => NIRABranchProposalAction.RecordProgress,
                    "activate" => NIRABranchProposalAction.Activate,
                    "wait" => NIRABranchProposalAction.SetWaiting,
                    "block" => NIRABranchProposalAction.SetBlocked,
                    "complete" => NIRABranchProposalAction.Complete,
                    "fail" => NIRABranchProposalAction.Fail,
                    _ => throw new InvalidDataException("Unknown branch transition") };
                var proposal = new NIRABranchProposal { Action = transition,
                    GoalId = EventGoal(_event, _goals)!.Value.ToString("D"),
                    BranchId = action == "create" ? null : id.ToString("D"),
                    Objective = Get(args, "objective"),
                    CompletionCriteria = action == "create" ? [Get(args, "successCriterion")] : [],
                    JoinPolicy = action == "create" ? NIRABranchJoinPolicy.Required : null,
                    EvidenceSource = NIRABranchEvidenceSource.CurrentEvent, EvidenceQuote = quote,
                    EvidenceSummary = reason, Reason = reason, Confidence = 0.96,
                    WaitingFor = Get(args, "waitingFor"), Blocker = Get(args, "blocker"),
                    FailureReason = action == "fail" ? reason : null,
                    ResultSummary = Get(args, "resultSummary") };
                NIRABranchApplyResult result = (await _branches.ApplyProposalsAsync(_event,
                    "", "", [proposal], ct)).Single();
                if (result.Action == NIRABranchApplyAction.Rejected)
                    return AgentToolReceipt.ConfirmedNoChange("BRANCH_TRANSITION_REJECTED");
                string receipt = await _vault.SaveResultAsync(runId, Spec.Id,
                    JsonSerializer.SerializeToElement(new { kind, action, result = result.Action.ToString(),
                        branchId = result.Branch?.Id, goalId = result.Branch?.GoalId,
                        status = result.Branch?.Status.ToString(), result.Reason }), ct);
                return AgentToolReceipt.Verified(receipt);
            }
            var request = new NIRACapabilityRequest { CapabilityId = Get(args, "capabilityId"),
                Arguments = args.GetProperty("arguments").Clone(), Reason = reason };
            var workProposal = new NIRABranchWorkProposal { BranchId = id.ToString("D"),
                Kind = NIRABranchWorkKind.Capability, CapabilityRequest = request,
                Reason = reason, Confidence = 0.96 };
            NIRABranchWorkApplyResult assignment = (await _work.ApplyProposalsAsync(_event,
                [workProposal], ct)).Single();
            if (assignment.Action == NIRABranchWorkApplyAction.Rejected)
                return AgentToolReceipt.ConfirmedNoChange("BRANCH_WORK_REJECTED");
            string saved = await _vault.SaveResultAsync(runId, Spec.Id,
                JsonSerializer.SerializeToElement(new { kind, action, result = assignment.Action.ToString(),
                    workId = assignment.Work?.Id, branchId = assignment.Work?.BranchId,
                    status = assignment.Work?.Status.ToString(),
                    note = "Assigned to existing branch runner; QUEUED is not executed or completed." }), ct);
            return AgentToolReceipt.Verified(saved);
        }
    }
}

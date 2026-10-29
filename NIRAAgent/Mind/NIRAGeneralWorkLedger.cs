using System.Text;
using NIRAAgent.AI.Cognition;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

// R53: One run's original objective, model-proposed milestones and trusted
// tool OUTCOMES. No keyword router, no authority to execute and no inferred
// fulfillment. Every individual criterion remains unverified until assessed
// against the original user request by the independent completion reviewer.
internal sealed class NIRAGeneralWorkLedger
{
    private string _objective = string.Empty;
    private string[] _criteria = Array.Empty<string>();
    private string _nextMilestone = string.Empty;
    private bool _requiresToolExecution;
    private bool _requiresStateChange;
    private bool _hasSuccessfulExecution;
    private bool _hasVerifiedStateChange;
    private int _failedOrUncertain;
    private readonly List<(string Capability, string Status, string Risk)> _observations = new();

    internal bool RequiresGoalLevelReview => _requiresToolExecution ||
        _requiresStateChange || _criteria.Length > 1;
    internal bool HasSuccessfulExecution => _hasSuccessfulExecution;
    internal bool HasVerifiedStateChange => _hasVerifiedStateChange;
    // A read-only source or a goal-store mutation does not satisfy an output
    // whose original work plan requires a file/application state change.
    internal bool HasUnfulfilledStateChange => _objective.Length > 0 &&
        _requiresStateChange && !_hasVerifiedStateChange;

    // An internal drafting promise is not an executable capability request.
    // This is only a bounded prompt/schema recovery condition: never execute
    // a tool without a fresh model proposal and normal authorization.
    internal bool ShouldRecoverActionlessContinuation(
        NIRACognitionDecision decision, bool alreadyRecovered) =>
        !alreadyRecovered && HasUnfulfilledStateChange &&
        decision.State == NIRACognitionState.Continue &&
        decision.CapabilityRequests.Count == 0 &&
        decision.ContextRequests.Count == 0 &&
        decision.CapabilityIds.Count == 0 &&
        decision.MemorySearches.Count == 0 &&
        decision.ConversationSearches.Count == 0 &&
        decision.BranchWorkProposals.Count == 0 &&
        decision.DynamicToolInvocations.Count == 0 &&
        decision.DynamicToolProposals.Count == 0 &&
        decision.ControlRequests.Count == 0;

    internal string UnfulfilledOutputReply() =>
        "I gathered some preparatory information, but I did not create or " +
        "verify the requested output. The original objective is still " +
        "unfinished: " + _objective + ". I stopped after the model " +
        "failed to propose a usable next action, rather than claiming " +
        "that unrelated filesystem results satisfied the request.";

    private static string Bound(string? text, int max)
    {
        string clean = string.Join(" ", (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= max ? clean : clean[..max];
    }

    internal void Propose(NIRAAutonomousWorkPlan? plan)
    {
        if (plan is null) return;
        string objective = Bound(plan.Objective, 220);
        if (objective.Length == 0) return;
        if (_objective.Length == 0)
        {
            // Never permit a later model cycle or untrusted search extract
            // to replace the already recorded objective/requirements.
            _objective = objective;
            _criteria = (plan.CompletionCriteria ?? Array.Empty<string>())
                .Select(item => Bound(item, 150))
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5).ToArray();
        }
        // Hints can become more conservative, but not less: a model cannot
        // remove a prerequisite by silently changing a later plan.
        _requiresToolExecution |= plan.RequiresToolExecution;
        _requiresStateChange |= plan.RequiresStateChange;
        _nextMilestone = Bound(plan.NextMilestone, 220);
    }

    internal void Observe(NIRACapabilityRequest request, NIRACapabilityResult result)
    {
        // Receipts may arrive before cognition supplies its first workPlan.
        // Keep them, because later planning must not erase accomplished work.
        bool success = result.Status == NIRACapabilityResultStatus.Succeeded &&
            !result.OutcomeUncertain;
        _hasSuccessfulExecution |= success;
        _hasVerifiedStateChange |= success && result.ChangedSystemState;
        if (!success) _failedOrUncertain++;
        _observations.Add((Bound(request.CapabilityId, 110),
            result.OutcomeUncertain ? "Uncertain" : result.Status.ToString(),
            result.Risk.ToString()));
        if (_observations.Count > 12) _observations.RemoveAt(0);
    }

    // This conservative gate is only a ONE-TIME extra cognition step. It
    // catches an unsupported final success before any action, but never blocks
    // an explicit truthful NeedUser/Blocked or overrides safety/permissions.
    internal bool ShouldReconsiderTerminal(NIRACognitionDecision decision)
    {
        if (_objective.Length == 0 || decision.State != NIRACognitionState.Complete ||
            !decision.EmitReply || string.IsNullOrWhiteSpace(decision.Reply)) return false;
        return (_requiresToolExecution && !_hasSuccessfulExecution) ||
               (_requiresStateChange && !_hasVerifiedStateChange);
    }

    internal string ReconsiderationEvidence() =>
        "GENERAL GOAL-EXECUTION CHECK (one bounded reconsideration): " +
        "Your last draft declared the ORIGINAL objective complete but its own " +
        "workPlan requires an actual capability result or state change that has " +
        "NOT been observed in this run. An intent, draft, image candidate, " +
        "unexecuted proposed tool or search result cannot prove a saved or " +
        "changed deliverable. Use the registered tools to complete the next " +
        "dependency, or state the precise blocker. Do not repeat uncertain " +
        "writes, bypass authorization, or invent a destination.";

    internal string ReviewerEvidence()
    {
        if (_objective.Length == 0) return string.Empty;
        StringBuilder b = new();
        b.AppendLine("GENERAL OUTCOME CONTRACT — ADVISORY PLAN, NEVER PROOF:");
        b.AppendLine("Model proposed original objective: " + _objective);
        foreach (string criterion in _criteria)
            b.AppendLine("Requested criterion (NOT independently certified): " + criterion);
        b.AppendLine("Needs registered tool execution (model-declared): " + _requiresToolExecution);
        b.AppendLine("Needs real state change (model-declared): " + _requiresStateChange);
        b.AppendLine("Trusted successful tool observed: " + _hasSuccessfulExecution);
        b.AppendLine("Trusted successful changed-state receipt observed: " + _hasVerifiedStateChange);
        b.AppendLine("Failures/uncertain outcomes observed: " + _failedOrUncertain);
        b.AppendLine("The model's workPlan and checkmarks are NEVER evidence of fulfillment. " +
            "Judge EACH criterion against the ORIGINAL USER REQUEST and full " +
            "actual receipts. A successful write proves a write, NOT research " +
            "accuracy, content completeness or visual quality. Do not demand " +
            "unnecessary further tools if the outcome is already supported.");
        return b.ToString();
    }

    internal string Snapshot()
    {
        if (_objective.Length == 0) return string.Empty;
        StringBuilder b = new();
        b.AppendLine("R53 USER-GOAL CONTINUITY (model plan; NOT proof)");
        b.AppendLine("Objective: " + _objective);
        foreach (string criterion in _criteria)
            b.AppendLine("Completion criterion (pending independent review): " + criterion);
        if (_nextMilestone.Length > 0)
            b.AppendLine("Next model-proposed milestone: " + _nextMilestone);
        b.AppendLine("Tool execution observed: " + _hasSuccessfulExecution +
            "; verified state change observed: " + _hasVerifiedStateChange);
        b.AppendLine("Recent trusted tool outcomes (statuses only; original receipts have details):");
        if (_observations.Count == 0) b.AppendLine("None yet.");
        foreach (var observation in _observations.TakeLast(8))
            b.AppendLine("- " + observation.Capability + " | " +
                observation.Status + " | " + observation.Risk);
        b.AppendLine("A successful subtask is not full goal completion. Reassess " +
            "remaining dependencies, evidence, content quality and output " +
            "verification before delivering a final answer. Do not bypass " +
            "permissions or replay uncertain mutations.");
        return b.ToString().Trim();
    }
}

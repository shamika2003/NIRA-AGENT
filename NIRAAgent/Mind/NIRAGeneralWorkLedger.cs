using System.Text;
using NIRAAgent.AI.Cognition;
using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

// R49: Run-local cognitive continuity, not a new tool router or a permission layer.
// The model proposes milestones; ONLY trusted capability receipts describe execution.
// No arbitrary user/source text can mark a goal complete or authorize a mutation.
internal sealed class NIRAGeneralWorkLedger
{
    private string _objective = string.Empty;
    private string[] _criteria = Array.Empty<string>();
    private string _nextMilestone = string.Empty;
    private readonly List<(string Capability, string Status, string Risk)> _observations = new();

    private static string Bound(string? text, int maximum)
    {
        string clean = string.Join(" ", (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length > maximum ? clean[..maximum] : clean;
    }

    internal void Propose(NIRAAutonomousWorkPlan? plan)
    {
        if (plan is null) return;
        string objective = Bound(plan.Objective, 220);
        if (objective.Length == 0) return;
        // First meaningful objective is stable across later replanning. Neither
        // a webpage nor a later model turn can quietly redefine the user goal.
        if (_objective.Length == 0)
        {
            _objective = objective;
            _criteria = (plan.CompletionCriteria ?? Array.Empty<string>())
                .Select(item => Bound(item, 150))
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5).ToArray();
        }
        _nextMilestone = Bound(plan.NextMilestone, 220);
    }

    internal void Observe(NIRACapabilityRequest request, NIRACapabilityResult result)
    {
        if (_objective.Length == 0) return;
        // Never turn a successful *tool* call into proof of a complete goal.
        string status = result.OutcomeUncertain ? "Uncertain" : result.Status.ToString();
        _observations.Add((Bound(request.CapabilityId, 110), status,
            result.Risk.ToString()));
        if (_observations.Count > 12) _observations.RemoveAt(0);
    }

    internal string Snapshot()
    {
        if (_objective.Length == 0) return string.Empty;
        StringBuilder b = new();
        b.AppendLine("R49 USER-GOAL WORK CONTINUITY (model plan; NOT proof)");
        b.AppendLine("Objective: " + _objective);
        foreach (string criterion in _criteria)
            b.AppendLine("Required result: " + criterion);
        if (_nextMilestone.Length > 0)
            b.AppendLine("Model's next milestone: " + _nextMilestone);
        b.AppendLine("Actual authorized tool observations (status only; inspect original receipts for details):");
        if (_observations.Count == 0) b.AppendLine("None yet.");
        foreach (var observation in _observations.TakeLast(8))
            b.AppendLine("- " + observation.Capability + " | " +
                observation.Status + " | " + observation.Risk);
        b.AppendLine("A proposal, successful search, successful edit, or created file is not " +
            "itself evidence that every requested result is complete. " +
            "Replan from trusted observations; verify final deliverables and dependencies. " +
            "Never repeat uncertain writes or evade rejected permissions.");
        return b.ToString().Trim();
    }
}

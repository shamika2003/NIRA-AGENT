using System.Text.RegularExpressions;

namespace NIRAAgent.Runtime.Contracts;

/// <summary>
/// A current-run task contract, NOT a long-term NIRA goal. The original request
/// is still the authority. Model-produced milestones are advisory; host-derived
/// artifact obligations cannot be removed by later model output.
/// Backward compatible: older run checkpoints may have TaskContract == null.
/// </summary>
public sealed class AgentTaskContract
{
    public int SchemaVersion { get; init; } = 1;
    public bool RequiresVerifiedAction { get; init; }
    public bool RequiresChangedState { get; init; }
    public bool RequiresSavedArtifact { get; init; }
    public bool RequiresReadBack { get; init; }
    public List<string> RequestedFileNames { get; init; } = new();
    public List<string> Milestones { get; init; } = new();
    public int RejectedPrematureAnswers { get; set; }
    public string LastUnfinishedReason { get; set; } = string.Empty;
}

/// <summary>
/// An exact-filename request is an independent host-side obligation, even when
/// a model plan is incomplete. No fixed research quota or mandatory tool route.
/// </summary>
public static class AgentTaskContractPolicy
{
    private static readonly Regex FileName = new(
        @"(?<![\w.])([A-Za-z0-9][A-Za-z0-9_.-]{1,100}\.(?:docx|xlsx|xls|pptx|pdf|csv|txt|md|html|json|xml|png|jpg|jpeg|zip|py|cs|js|ts|sql))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex ActionIntent = new(
        @"\b(?:create|make|build|write|generate|produce|save|export|publish|prepare|deliver|edit|update|modify|send|install|schedule)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex InspectionIntent = new(
        @"\b(?:check|verify|inspect|read.back|review.the.saved|confirm.the.saved|open.the.saved)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex ExplanationOnly = new(
        @"\b(?:how (?:do|can|would|should|to)|explain how|what (?:is|are|would)|tell me how)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    public static bool ShouldPlan(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return false;
        return request.Length > 320 ||
            (ActionIntent.IsMatch(request) && !ExplanationOnly.IsMatch(request));
    }

    public static AgentTaskContract Create(string request, bool modelRequiresAction = false,
        bool modelRequiresChange = false, bool modelRequiresArtifact = false,
        bool modelRequiresReadBack = false, IEnumerable<string>? milestones = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        var named = FileName.Matches(request).Cast<Match>().Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        // A literal requested file name coupled to a direct action is stronger
        // than a model summary. Generic how-to questions are not file writes.
        bool explicitArtifact = named.Count > 0 && ActionIntent.IsMatch(request) &&
            !(request.Length < 280 && ExplanationOnly.IsMatch(request));
        bool artifact = explicitArtifact || modelRequiresArtifact;
        bool change = artifact || modelRequiresChange;
        bool action = change || modelRequiresAction;
        bool inspect = artifact && (InspectionIntent.IsMatch(request) || modelRequiresReadBack);
        var steps = (milestones ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => string.Join(' ', s.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)))
            .Where(s => s.Length is > 3 and <= 180)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(7).ToList();
        return new AgentTaskContract {
            RequiresVerifiedAction = action, RequiresChangedState = change,
            RequiresSavedArtifact = artifact, RequiresReadBack = inspect,
            RequestedFileNames = explicitArtifact ? named : new List<string>(),
            Milestones = steps
        };
    }

    public static int RunSelfCheck()
    {
        string request = "Research Tesla and create Tesla_Report.docx in Documents. " +
            "Inspect the saved report afterward and confirm it contains the images.";
        AgentTaskContract c = Create(request, milestones: new[] {
            "Research trustworthy sources", "Write report", "Inspect saved report" });
        if (!c.RequiresSavedArtifact || !c.RequiresReadBack ||
            !c.RequiresChangedState || !c.RequiresVerifiedAction ||
            c.RequestedFileNames.Single() != "Tesla_Report.docx" ||
            c.Milestones.Count != 3) throw new InvalidOperationException("Task contract missed requested deliverable.");
        AgentTaskContract conversation = Create("Tell me how to make a Tesla_Report.docx file.");
        if (conversation.RequiresSavedArtifact || conversation.RequiresChangedState)
            throw new InvalidOperationException("Explanation misclassified as a file mutation.");
        AgentTaskContract model = Create("Please organize my workshop in several steps.",
            modelRequiresAction: true, modelRequiresChange: true,
            milestones: new[] { "Gather requirements", "Complete authorized actions" });
        if (!model.RequiresChangedState || model.Milestones.Count != 2)
            throw new InvalidOperationException("Model-authored work contract ignored.");
        return 3;
    }
}

public sealed record AgentTaskCompletionAssessment(bool Accept, string Reason);

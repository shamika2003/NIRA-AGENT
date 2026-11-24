using System.Text.Json;
using System.Text.RegularExpressions;
using Nira.Core;

namespace NIRAAgent.UI.Presentation;

/// <summary>Presentation boundary, not an execution or authorization policy. Raw diagnostics stay in the journal.</summary>
public static class NIRAPublicDelivery
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Internal = new(@"(?<![\w.])(?:nira_(?:select_tools|plan(?:_status|_update)?|finish|ask_user|decide_workflow)|(?:browser\.follow_public_link|browser\.inspect_public|browser\.session_click_button|browser\.session_close|browser\.session_fill_text|browser\.session_follow|browser\.session_follow_named_link|browser\.session_inspect|browser\.session_inspect_controls|browser\.session_inspect_links|browser\.session_inspect_text|browser\.session_open|browser\.session_select_option|browser\.session_semantic_action|browser\.session_set_checked|desktop\.control_invoke|desktop\.control_set_text|desktop\.control_type_text|desktop\.window_activate|desktop\.window_inspect|desktop\.windows_list|filesystem\.edit_text|filesystem\.find|filesystem\.list|filesystem\.mkdir|filesystem\.read_text|filesystem\.sha256|filesystem\.write_text|image\.retrieve|image\.generate|image\.search|memory\.remember|memory\.search|process\.run|productivity\.create_document|productivity\.create_workbook|productivity\.inspect|productivity\.ocr_read|productivity\.pdf_export_text|productivity\.pdf_search|productivity\.read|productivity\.revise_document|productivity\.revise_workbook|vision\.analyze|web\.search|workspace\.info)|[a-f0-9]{32}-[a-f0-9]{24}|call_[a-z0-9]{5,}|(?:receipt|operation)[\s\u202f\u00a0]+ID\b|ArgumentsJson|ResultJson|ToolCalls|CandidateId|MediaUrl|ThumbnailUrl|SourceUrl|active capability schemas|model round\s+\d+|nira_decide_workflow|tool_calls|ToolCallId|EvidenceId|ResultReference|ArgumentReference|AutomaticPlanningOutcome|RetryBlockCode|pixelsVerified|visualIdentityVerified|(?:internal|private)\s+(?:tool|execution)\s+(?:log|trace|journal)|\b(?:receipt|operation)\s+identifier)(?![\w.])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);
    private static readonly Regex Heading = new(@"^\s*(?:#{1,6}\s+|\*\*|__)", RegexOptions.CultureInvariant, Limit);
    private static readonly Regex Diary = new(@"^\s*(?:#{1,6}\s+|\*\*|__)?(?:how (?:the (?:list|results|images|information) (?:was|were) (?:obtained|found)|i (?:searched|found|used (?:the )?tools))|(?:internal |tool )?(?:execution|tool call|capability) (?:details|log|trace|history)|recorded (?:verification )?observations|host-recorded execution evidence)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);

    internal static bool ContainsInternal(string text)
    { try { return Internal.IsMatch(text); } catch (RegexMatchTimeoutException) { return true; } }

    public static string Answer(string raw)
    {
        if (raw.StartsWith("The model repeatedly printed a command as text instead of", StringComparison.Ordinal))
            return "I couldn't finish the requested action. No unconfirmed action was performed. You can retry the saved task.";
        try
        {
            var output = new List<string>(); bool skip = false; string? fence = null;
            foreach (string line in raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Not executed: you did not approve", StringComparison.OrdinalIgnoreCase))
                { output.Add("I did not perform the requested action because you declined approval. Your task is saved."); continue; }
                if (trimmed.StartsWith("Host-recorded execution evidence:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Recorded observations (tool/provider output):", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Recorded verification observations:", StringComparison.OrdinalIgnoreCase)) break;
                if (skip && !Heading.IsMatch(line)) continue;
                if (Diary.IsMatch(line)) { skip = true; continue; }
                if (skip) skip = false;
                string clean = line.Replace("**Source check (host):**", "**Source note:**", StringComparison.Ordinal)
                    .Replace("The recorded work and verification steps are complete. Here is the host-generated report from saved execution evidence. No file operation was repeated to produce this report.", "The requested work and verification are complete. Your saved results are available.", StringComparison.Ordinal)
                    .Replace("No additional model synthesis was produced; provider interpretations are not independently verified.", "The results have not been independently interpreted or verified beyond the completed checks.", StringComparison.Ordinal)
                    .Replace("The host is delivering the saved results without repeating operations.", "Your saved results are available; no actions were repeated.", StringComparison.Ordinal)
                    .Replace("model-supplied URL(s) were not found in successful tool results or current user input. Treat them as unverified, not established sources.", "link(s) have not been independently checked. Treat them as unverified.", StringComparison.Ordinal)
                    .Replace("user-supplied URL(s) were not observed in successful tool results.", "link(s) you supplied have not been independently checked.", StringComparison.Ordinal);
                // Preserve code examples, but not a raw invocation envelope or private receipt IDs.
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    if (fence is null) fence = trimmed[..3]; else if (trimmed.StartsWith(fence, StringComparison.Ordinal)) fence = null;
                    output.Add(clean); continue;
                }
                if (fence is null && clean.TrimStart().StartsWith('|'))
                    clean = clean.Replace("(MediaUrl)", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("(SourceUrl)", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("(ThumbnailUrl)", "", StringComparison.OrdinalIgnoreCase);
                if (fence is null && Internal.IsMatch(clean)) continue;
                if (fence is not null && Regex.IsMatch(clean, @"\b(?:call_[a-z0-9]{5,}|[a-f0-9]{32}-[a-f0-9]{24}|ArgumentsJson|ResultJson|nira_select_tools|image\.search|image\.retrieve|image\.generate|browser\.session_[a-z_]+|filesystem\.[a-z_]+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit)) continue;
                output.Add(clean);
            }
            string result = string.Join("\n", output).Trim();
            return result.Length == 0 ? "I couldn't prepare a clear response. Please try again." : result;
        }
        catch (RegexMatchTimeoutException) { return "I couldn't prepare a clear response. Please try again."; }
    }

    public static string Status(ConversationStatus status) => status switch
    {
        ConversationStatus.Completed => "Finished",
        ConversationStatus.Running => "Working on your request",
        ConversationStatus.AwaitingApproval => "Waiting for your approval",
        ConversationStatus.AwaitingUser => "Waiting for your reply",
        ConversationStatus.Paused => "Paused",
        ConversationStatus.NeedsReconciliation => "Please review the last action before retrying",
        ConversationStatus.Failed => "Could not finish",
        _ => "Ready"
    };

    public static string? Activity(AgentProgress progress) => progress.Kind switch
    {
        "reasoning" => null,
        "permission" => "Waiting for your approval",
        "tool" => ActionTitle(progress.ToolId),
        "observed" or "succeeded" => "Result received",
        "completed" => "Finished",
        "paused" => "Paused",
        "reconciliation" or "unknown" => "An earlier action needs verification",
        "error" or "failed" => "Something went wrong",
        "rejected" => "Action not performed",
        _ => null
    };

    private static string ActionTitle(string? id) => id switch
    {
        "image.retrieve" => "Loading images",
        "image.generate" => "Creating your artwork",
        "filesystem.write_text" => "Creating a text file",
        "filesystem.edit_text" => "Editing a text file",
        "filesystem.mkdir" => "Creating a folder",
        "process.run" => "Running a program",
        "desktop.control_type_text" or "desktop.control_set_text" => "Entering text",
        "desktop.control_invoke" => "Interacting with an application",
        "desktop.window_activate" => "Opening the selected window",
        "browser.session_fill_text" => "Filling a web form",
        "browser.session_click_button" => "Clicking a web button",
        "productivity.create_document" => "Creating a Word document",
        "productivity.create_workbook" => "Creating an Excel workbook",
        "productivity.revise_document" => "Editing a Word document",
        "productivity.revise_workbook" => "Editing an Excel workbook",
        _ => NIRAActivityPresentationPolicy.ToolTitle(id) is "Using an available capability" ? "Working on your request" : NIRAActivityPresentationPolicy.ToolTitle(id)
    };

    public static string Approval(ApprovalRequest request)
    {
        string risk = request.Risk switch
        {
            ToolRisk.ReadExternal => "Connect to an external service",
            ToolRisk.ReadLocal => "Read local information",
            ToolRisk.WriteWorkspace => "Change files in your workspace",
            ToolRisk.WriteOutsideWorkspace => "Change files outside your workspace",
            _ => "Perform a potentially sensitive action"
        };
        var lines = new List<string> { ActionTitle(request.ToolName), risk, "", "Destination:", request.Target, "", "Requested details:" };
        try
        {
            using var doc = JsonDocument.Parse(request.ArgumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            foreach (var property in doc.RootElement.EnumerateObject())
                lines.Add(property.Name.Replace('_', ' ') + ": " + (property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText()));
        }
        catch (JsonException) { lines.Add(request.ArgumentsJson); }
        return string.Join("\n", lines);
    }
}

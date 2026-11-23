using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>Uncertain effects must reach NiraEngine's reconciliation barrier.</summary>
public sealed class BrowserInteractionUncertainException(string message, Exception innerException)
    : Exception(message, innerException)
{
}

/// <summary>
/// Explicitly approved entry of non-secret, non-password text in one labeled
/// text/search field on a previously inspected public HTTPS page. Never submits.
/// The page can still execute client-side handlers. This is NOT a sandbox.
/// </summary>
public sealed class BrowserSessionFillTextTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_fill_text",
        "Enter user-approved non-secret text into an existing labeled public-page text/search/textarea control. " +
        "Use the exact page URL, DomIndex, tag and label from browser.session_inspect. " +
        "This can trigger website scripts: requires Dangerous per-action approval. " +
        "Never use for passwords, tokens, personal or financial information. " +
        "Does not submit forms, click buttons or permit navigation after entry. " +
        "Further same-page interactions require separate host approvals and fresh checks. " +
        "Network requests remain blocked after the first interaction. No OS/network sandbox.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sessionId":{"type":"string","minLength":32,"maxLength":32},
          "expectedPageUrl":{"type":"string","minLength":12,"maxLength":2048},
          "domIndex":{"type":"integer"},
          "expectedTag":{"type":"string","enum":["input","textarea"]},
          "expectedLabel":{"type":"string","minLength":1,"maxLength":150},
          "text":{"type":"string","maxLength":500}},
         "required":["sessionId","expectedPageUrl","domIndex","expectedTag","expectedLabel","text"],
         "additionalProperties":false}
        """), HasSideEffects: true);

    public static bool IsAllowedControl(BrowserElement element) =>
        !element.Disabled && !string.IsNullOrWhiteSpace(element.Label) &&
        ((element.Tag == "input" && element.Type is "text" or "search" or "") ||
         (element.Tag == "textarea" && element.Type == ""));

    private static Entry Parse(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        Uri url = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedPageUrl").GetString() ?? "");
        int domIndex = args.GetProperty("domIndex").GetInt32();
        if (domIndex is < 0 or >= 24)
            throw new ArgumentException("Control DOM index must be between 0 and 23 from the current page inspection.");
        string tag = args.GetProperty("expectedTag").GetString() ?? "";
        string label = args.GetProperty("expectedLabel").GetString() ?? "";
        if (tag is not ("input" or "textarea") ||
            string.IsNullOrWhiteSpace(label) || label != label.Trim())
            throw new ArgumentException("Only labeled input/textarea controls from an observed page are allowed.");
        string value = args.GetProperty("text").GetString() ?? "";
        if (value.Length > 500 || value.Any(ch => ch == '\0' || ch == '\r' || (char.IsControl(ch) && ch != '\n' && ch != '\t')))
            throw new ArgumentException("Text exceeds 500 characters or contains unsupported controls.");
        if (tag == "input" && (value.Contains('\n') || value.Contains('\t')))
            throw new ArgumentException("Single-line input cannot contain tab or newline.");
        return new Entry(id, url, domIndex, tag, label, value);
    }

    public string GetTarget(JsonElement args)
    {
        Entry e = Parse(args);
        // The host must display the EXACT data that will be entered before approval.
        return "session " + e.SessionId + " on " + e.PageUrl.AbsoluteUri +
            " [DOM index " + e.DomIndex + ", " + e.Tag + ", label '" + e.Label +
            "', text " + JsonSerializer.Serialize(e.Text) + "]" +
            " WARNING: page scripts may react; no form submit/click is performed.";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        Entry e = Parse(args);
        try
        {
            BrowserSessionTextObservation observed = await sessions.FillTextAsync(
                e.SessionId, e.PageUrl, e.DomIndex, e.Tag, e.Label, e.Text, ct);
            return ToolReceipt.Ok("Entered approved text into a public browser field and verified the value", observed,
                changedState: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            return ToolReceipt.Fail("BROWSER_TEXT_CONTROL_REFUSED", ex.Message);
        }
        // Deliberately do not catch BrowserInteractionUncertainException:
        // the kernel records an unknown effect and requires reconciliation.
    }

    private sealed record Entry(string SessionId, Uri PageUrl, int DomIndex,
        string Tag, string Label, string Text);
}

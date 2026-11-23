using System.Text.Json;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// One user-approved click on an explicitly typed native HTML button on a public
/// HTTPS page. Clicks can invoke site JavaScript, so every call is Dangerous.
/// Form submit/reset buttons, input buttons, links and arbitrary selectors are
/// deliberately excluded. Browser network traffic is blocked before the click,
/// but already-open connections or scripts can have other effects. NOT a sandbox.
/// </summary>
public sealed class BrowserSessionClickButtonTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_click_button",
        "Click ONE user-approved native HTML <button type=button> on a previously inspected " +
        "public HTTPS page. Requires the exact page URL, DOM index, label and visible button text " +
        "from session inspection. Host requests Dangerous approval for the exact target. " +
        "No submit/reset buttons, links, forms or arbitrary selectors. Subsequent same-page " +
        "actions require fresh independent approval and inspection. Blocks HTTP requests " +
        "after the first interaction but site scripts or already-open " +
        "connections may still cause effects. NOT a sandbox. Never use for purchases, deletion, " +
        "account operations, credentials or other consequential actions. " +
        "Only explicit before/after accessibility-state fields in the receipt establish " +
        "a verified UI state change; successful click dispatch alone does not.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sessionId":{"type":"string","minLength":32,"maxLength":32},
          "expectedPageUrl":{"type":"string","minLength":12,"maxLength":2048},
          "domIndex":{"type":"integer"},
          "expectedTag":{"type":"string","enum":["button"]},
          "expectedType":{"type":"string","enum":["button"]},
          "expectedLabel":{"type":"string","maxLength":150},
          "expectedText":{"type":"string","maxLength":160}},
         "required":["sessionId","expectedPageUrl","domIndex","expectedTag",
             "expectedType","expectedLabel","expectedText"],
         "additionalProperties":false}
        """), HasSideEffects: true);

    public static bool IsAllowedControl(BrowserElement element) =>
        !element.Disabled && element.Tag == "button" && element.Type == "button" &&
        (!string.IsNullOrWhiteSpace(element.Label) ||
         !string.IsNullOrWhiteSpace(element.Text));

    private static Target Parse(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        Uri url = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedPageUrl").GetString() ?? "");
        int index = args.GetProperty("domIndex").GetInt32();
        if (index is < 0 or >= 24)
            throw new ArgumentException("Button index must be 0–23 from the observed control DOM index.");
        string tag = args.GetProperty("expectedTag").GetString() ?? "";
        string type = args.GetProperty("expectedType").GetString() ?? "";
        string label = args.GetProperty("expectedLabel").GetString() ?? "";
        string text = args.GetProperty("expectedText").GetString() ?? "";
        if (tag != "button" || type != "button" ||
            (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(text)) ||
            label.Any(char.IsControl) || text.Any(char.IsControl) ||
            label != label.Trim() || text != text.Trim())
            throw new ArgumentException(
                "Select an explicitly typed native button with exact observed label/text and no controls.");
        return new Target(id, url, index, label, text);
    }

    public string GetTarget(JsonElement args)
    {
        Target e = Parse(args);
        return "session " + e.SessionId + " on " + e.PageUrl.AbsoluteUri +
            " [DOM index " + e.DomIndex + ", native <button type=button>, label " +
            JsonSerializer.Serialize(e.Label) + ", text " +
            JsonSerializer.Serialize(e.Text) + "]" +
            " WARNING: Clicking triggers site scripts; network routing is blocked " +
            "but other effects may occur. Every click needs separate approval; " +
            "no form submission or navigation approved.";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        Target e = Parse(args);
        try
        {
            BrowserSessionButtonObservation observed = await sessions.ClickButtonAsync(
                e.SessionId, e.PageUrl, e.DomIndex, e.Label, e.Text, ct);
            string evidence = observed.AccessibleState?.VerifiedStateChange == true
                ? "Observed an accessible-state transition for matching control metadata"
                : "No accessible state change was independently verified";
            return ToolReceipt.Ok(
                "Dispatched approved native button click without navigation. " + evidence + ".",
                observed, changedState: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        {
            return ToolReceipt.Fail("BROWSER_BUTTON_CLICK_REFUSED", ex.Message);
        }
        // Uncertain post-dispatch effects are intentionally not caught; NiraEngine
        // marks the operation Unknown and pauses for manual reconciliation.
    }

    private sealed record Target(string SessionId, Uri PageUrl, int DomIndex,
        string Label, string Text);
}

using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// Read-only follow of an observed anchor by index. Each visit is fresh/incognito,
/// GET-only, HTTPS-only and restricted to the exact approved hostname. This is
/// navigation to an observed href, NOT a DOM click, login or browser session.
/// </summary>
public sealed class PublicBrowserLinkTool(IBrowserPageReader? reader = null) : IAgentTool
{
    private readonly IBrowserPageReader _reader = reader ?? new EdgePublicPageReader();

    public ToolDescriptor Descriptor { get; } = new(
        "browser.follow_public_link",
        "Follow one observed public HTTPS page link by its zero-based index in the " +
        "browser.inspect_public links list, supplying expectedLinkUrl to prevent " +
        "navigating to a changed link. Inspects source and destination in fresh " +
        "Edge contexts; only the original hostname is allowed. No DOM clicks, " +
        "login sessions, POST requests, downloads or stored cookies. Requires approval.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sourceUrl":{"type":"string","minLength":12,"maxLength":2048},
          "expectedLinkUrl":{"type":"string","minLength":12,"maxLength":2048},
          "linkIndex":{"type":"integer"}},
         "required":["sourceUrl","expectedLinkUrl","linkIndex"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement args)
    {
        Uri source = PublicBrowserUrlPolicy.Parse(args.GetProperty("sourceUrl").GetString() ?? "");
        int index = args.GetProperty("linkIndex").GetInt32();
        if (index is < 0 or >= 24)
            throw new ArgumentException("Link index must be 0–23 from the observed browser links list.");
        Uri expected = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedLinkUrl").GetString() ?? "");
        if (!source.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source and intended link must use the same public HTTPS hostname.");
        return source.AbsoluteUri + " [observed link index: " + index +
            ", approved destination: " + expected.AbsoluteUri + "]";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        _ = GetTarget(args);
        Uri source = PublicBrowserUrlPolicy.Parse(args.GetProperty("sourceUrl").GetString() ?? "");
        int index = args.GetProperty("linkIndex").GetInt32();
        Uri expected = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedLinkUrl").GetString() ?? "");
        try
        {
            BrowserSnapshot first = await _reader.ReadAsync(source, ct);
            if (!PublicBrowserUrlPolicy.AllowsSameHostGet(first.FinalUrl, source.Host, "GET"))
                return ToolReceipt.Fail("BROWSER_SOURCE_CHANGED_ORIGIN", "Source navigation escaped the approved hostname.");
            if (index >= first.Links.Count)
                return ToolReceipt.Fail("BROWSER_LINK_MISSING", "Source no longer has the requested link index.",
                    new { source = source.AbsoluteUri, linkIndex = index, links = first.Links.Count });
            BrowserLink link = first.Links[index];
            Uri destination;
            try { destination = PublicBrowserUrlPolicy.Parse(link.Url); }
            catch (ArgumentException)
            {
                return ToolReceipt.Fail("BROWSER_LINK_UNSAFE", "Observed link is not an allowed HTTPS destination.");
            }
            if (!destination.AbsoluteUri.Equals(expected.AbsoluteUri, StringComparison.Ordinal))
                return ToolReceipt.Fail("BROWSER_LINK_CHANGED", "Observed link differs from the exact URL approved by the user. Inspect the page again before following a link.",
                    new { source = source.AbsoluteUri, linkIndex = index });
            BrowserSnapshot target = await _reader.ReadAsync(destination, ct);
            if (!PublicBrowserUrlPolicy.AllowsSameHostGet(target.FinalUrl, source.Host, "GET"))
                return ToolReceipt.Fail("BROWSER_TARGET_CHANGED_ORIGIN", "Destination navigation escaped the approved hostname.");
            return ToolReceipt.Ok("Followed and inspected same-host public link", new {
                sourceUrl = first.FinalUrl, sourceTitle = first.Title,
                linkIndex = index, linkText = link.Text, linkUrl = destination.AbsoluteUri,
                destination = target
            });
        }
        catch (Exception error) when (error is PlaywrightException or SocketException or
            TimeoutException or IOException or InvalidOperationException)
        {
            return ToolReceipt.Fail("BROWSER_LINK_VISIT_FAILED", "Read-only browser link visit failed: " + error.Message);
        }
    }
}

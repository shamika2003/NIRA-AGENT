using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// One explicitly approved, public HTTPS, read-oriented page visit. No persistent
/// browser profile, cookies, clicks, form submission, file downloads, or logins.
/// The browser is not an operating-system or network sandbox. Do not use it for
/// private services or authenticated accounts.
/// </summary>
public sealed class PublicBrowserInspectionTool(IBrowserPageReader? reader = null) : IAgentTool
{
    private readonly IBrowserPageReader _reader = reader ?? new EdgePublicPageReader();

    public ToolDescriptor Descriptor { get; } = new(
        "browser.inspect_public",
        "Visit a public HTTPS page in a fresh Microsoft Edge browser context and report its " +
        "observed title, text, links and visible control metadata. No clicks, form submissions, downloads, login " +
        "sessions or saved cookies. For research or reading, set textOnly=true to skip link/control enumeration; omit it for interactive inspection or link discovery. Each visit requires host approval. Does not grant " +
        "access to localhost, intranet or private IPs. Other origins are blocked.",
        ToolRisk.ReadExternal,
        JsonToolSchema.Schema("""
        {"type":"object","properties":{"url":{"type":"string","minLength":12,"maxLength":2048},"textOnly":{"type":"boolean","description":"Read page title and visible text only; no links or control metadata."}},
         "required":["url"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement arguments) =>
        PublicBrowserUrlPolicy.Parse(arguments.GetProperty("url").GetString() ?? "").AbsoluteUri;

    public async Task<ToolReceipt> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        Uri uri = PublicBrowserUrlPolicy.Parse(arguments.GetProperty("url").GetString() ?? "");
        try
        {
            bool textOnly = arguments.TryGetProperty("textOnly", out var mode) && mode.ValueKind == JsonValueKind.True;
            BrowserSnapshot observed = textOnly ? await _reader.ReadTextAsync(uri, ct) : await _reader.ReadAsync(uri, ct);
            if (!PublicBrowserUrlPolicy.AllowsSameHostGet(observed.FinalUrl, uri.Host, "GET"))
                return ToolReceipt.Fail("BROWSER_REDIRECT_BLOCKED",
                    "The observed final page was not the approved public HTTPS host.");
            return textOnly
                ? ToolReceipt.Ok("Public browser text inspected (links and controls not collected)", new {
                    observed.RequestedUrl, observed.FinalUrl, observed.Title, observed.HttpStatus,
                    observed.VisibleText, observed.TextTruncated, Links = Array.Empty<BrowserLink>(),
                    Elements = Array.Empty<BrowserElement>(), inspectionMode = "text_only" })
                : ToolReceipt.Ok("Public browser page inspected", observed);
        }
        catch (Exception error) when (error is PlaywrightException or SocketException or
            TimeoutException or IOException or InvalidOperationException)
        {
            return ToolReceipt.Fail("BROWSER_INSPECTION_FAILED", "Public browser inspection failed: " +
                (error is PlaywrightException ? "Edge could not complete this visit. " : "") +
                error.Message);
        }
    }
}

public sealed record BrowserLink(string Text, string Url, string AccessibleName = "");
public sealed record BrowserSnapshot(string RequestedUrl, string FinalUrl,
    string Title, int? HttpStatus, string VisibleText, bool TextTruncated,
    IReadOnlyList<BrowserLink> Links, IReadOnlyList<BrowserElement>? Elements = null);

/// <summary>Read-only element metadata; never includes input values or password contents.</summary>
public sealed record BrowserElement(string Tag, string Role, string Label, string Type,
    string Text, bool Disabled, int DomIndex = -1,
    bool? AriaExpanded = null, bool? AriaPressed = null, bool? AriaChecked = null,
    string AccessibleName = "", string SemanticRole = "",
    IReadOnlyList<string>? OptionLabels = null, bool? NativeChecked = null,
    string HtmlId = "");

/// <summary>Injectable page reader for deterministic agent tests without launching a browser.</summary>
public interface IBrowserPageReader
{
    Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct);
    // Older injected readers remain compatible. The production reader avoids the enumeration itself.
    async Task<BrowserSnapshot> ReadTextAsync(Uri target, CancellationToken ct) =>
        (await ReadAsync(target, ct)) with { Links = [], Elements = [] };
}

/// <summary>
/// Conservative public-website URL policy, not a DNS-pinning/network sandbox.
/// Browser navigation must also pass the network-routing restriction below.
/// </summary>
public static class PublicBrowserUrlPolicy
{
    public static Uri Parse(string raw)
    {
        if (raw.Length is < 12 or > 2048 || raw != raw.Trim() ||
            raw.Any(char.IsControl) || !Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.HostNameType != UriHostNameType.Dns ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Supply a public HTTPS URL with no credentials, fragment or custom port.");
        string host = uri.Host;
        if (host.Length > 253 || host.EndsWith(".", StringComparison.Ordinal) ||
            host.Contains('_') || host.IndexOf('.') <= 0 ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".example", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The destination must be a public DNS hostname.");
        return uri;
    }

    public static bool AllowsSameHostGet(string raw, string approvedHost, string method)
    {
        if (method is not ("GET" or "HEAD")) return false;
        try { return Parse(raw).Host.Equals(approvedHost, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    public static void RequirePublicDns(IEnumerable<IPAddress> resolved)
    {
        IPAddress[] addresses = resolved.ToArray();
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new InvalidOperationException("Destination DNS returned no addresses or included a non-public address.");
    }

    public static bool IsPublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        byte[] b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            int a = b[0], c = b[1], d = b[2];
            return a is > 0 and < 224 && a != 10 && a != 127 &&
                !(a == 100 && c is >= 64 and <= 127) &&
                !(a == 169 && c == 254) &&
                !(a == 172 && c is >= 16 and <= 31) &&
                !(a == 192 && (c == 168 || (c == 0 && (d == 0 || d == 2)) ||
                   (c == 88 && d == 99))) &&
                !(a == 198 && (c == 18 || c == 19 || (c == 51 && d == 100))) &&
                !(a == 203 && c == 0 && d == 113);
        }
        if (ip.AddressFamily != AddressFamily.InterNetworkV6 ||
            ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal ||
            IPAddress.IsLoopback(ip)) return false;
        // Globally routable IPv6 is 2000::/3; reject test and tunneling ranges.
        return (b[0] & 0xE0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 &&
              ((b[2] == 0x0d && b[3] == 0xb8) || // 2001:db8::/32 documentation
               (b[2] == 0 && b[3] == 0))) &&   // 2001:0000::/32 Teredo
            !(b[0] == 0x20 && b[1] == 0x02);    // 6to4 encapsulation
    }
}

internal sealed class EdgePublicPageReader : IBrowserPageReader
{
    public Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct) => ReadCoreAsync(target, false, ct);
    public Task<BrowserSnapshot> ReadTextAsync(Uri target, CancellationToken ct) => ReadCoreAsync(target, true, ct);

    private static async Task<BrowserSnapshot> ReadCoreAsync(Uri target, bool textOnly, CancellationToken ct)
    {
        // This is best-effort public-network filtering, not DNS pinning or a sandbox.
        IPAddress[] dns = await Dns.GetHostAddressesAsync(target.DnsSafeHost, ct);
        PublicBrowserUrlPolicy.RequirePublicDns(dns);
        using IPlaywright playwright = await Playwright.CreateAsync().WaitAsync(ct);
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() {
            Channel = "msedge", Headless = true, Timeout = 15000
        }).WaitAsync(ct);
        IBrowserContext context = await browser.NewContextAsync(new() {
            AcceptDownloads = false, IgnoreHTTPSErrors = false,
            ServiceWorkers = ServiceWorkerPolicy.Block
        }).WaitAsync(ct);
        try
        {
            int networkRequests = 0;
            await context.RouteAsync("**/*", async route => {
                string url = route.Request.Url;
                string method = route.Request.Method;
                if (++networkRequests > 60 ||
                    !PublicBrowserUrlPolicy.AllowsSameHostGet(url, target.Host, method))
                {
                    await route.AbortAsync();
                    return;
                }
                // Fetch without implicit redirects. Browser-triggered follow-up
                // navigations must be routed and checked against the approved host.
                IAPIResponse response = await route.FetchAsync(new() {
                    MaxRedirects = 0, Timeout = 15000
                });
                await route.FulfillAsync(new() { Response = response });
            });
            IPage page = await context.NewPageAsync().WaitAsync(ct);
            IResponse? initial = await page.GotoAsync(target.AbsoluteUri, new() {
                WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000
            }).WaitAsync(ct);
            if (!PublicBrowserUrlPolicy.AllowsSameHostGet(page.Url, target.Host, "GET"))
                throw new InvalidOperationException("Navigation escaped the approved hostname.");
            return await SnapshotAsync(page, target, initial?.Status, ct, textOnly: textOnly);
        }
        finally { await context.CloseAsync(); }
    }

    // Standard ARIA tristate: null means absent/invalid/unknown, never an
    // inferred false. Values such as aria-pressed="mixed" remain unknown.
    internal static bool? ParseAriaBool(string? value) => bool.TryParse(value, out bool state)
        ? state : null;


    // Paged anchor metadata deliberately excludes form input values and arbitrary markup.
    // Pages refer to the original a[href] DOM order; hidden anchors are skipped,
    // never backfilled from later pages. A later named action rechecks live identity.
    internal static async Task<(int TotalLinkCount, IReadOnlyList<BrowserLink> Links)> ReadLinkPageAsync(
        IPage page, int pageNumber, CancellationToken ct)
    {
        int offset = BrowserLinkPagination.OffsetForPage(pageNumber);
        ILocator anchors = page.Locator("a[href]");
        int total = await anchors.CountAsync().WaitAsync(ct);
        var links = new List<BrowserLink>();
        for (int i = offset; i < Math.Min(total, offset + BrowserLinkPagination.PageSize); i++)
        {
            try
            {
                ILocator one = anchors.Nth(i);
                if (!await one.IsVisibleAsync().WaitAsync(ct)) continue;
                // Visible DOM nodes can be intentionally excluded from accessibility.
                // Only native link-role anchors are admissible for paged selection.
                string? explicitRole = await one.GetAttributeAsync("role").WaitAsync(ct);
                if (!string.IsNullOrWhiteSpace(explicitRole) &&
                    !string.Equals(explicitRole, "link", StringComparison.OrdinalIgnoreCase)) continue;
                if (!await one.EvaluateAsync<bool>("""
                    el => !el.closest('[aria-hidden="true"], [inert]')
                    """).WaitAsync(ct)) continue;
                string? href = await one.GetAttributeAsync("href").WaitAsync(ct);
                if (string.IsNullOrWhiteSpace(href) ||
                    !Uri.TryCreate(new Uri(page.Url), href, out Uri? url)) continue;
                string? rawText = await one.TextContentAsync().WaitAsync(ct);
                string name = await one.EvaluateAsync<string>("""
                    el => {
                        const trim = x => (x || '').replace(/\s+/g, ' ').trim();
                        const aria = trim(el.getAttribute('aria-label'));
                        if (aria) return aria;
                        const ids = trim(el.getAttribute('aria-labelledby')).split(/\s+/).filter(Boolean);
                        if (ids.length) {
                            const value = ids.map(id => trim(document.getElementById(id)?.textContent))
                              .filter(Boolean).join(' ');
                            if (value) return value;
                        }
                        return trim(el.innerText) ||
                          trim(el.querySelector('img[alt]')?.getAttribute('alt')) ||
                          trim(el.getAttribute('title'));
                    }
                    """).WaitAsync(ct);
                string text = (rawText ?? "").Trim();
                links.Add(new BrowserLink(
                    text[..Math.Min(text.Length, 120)],
                    url.AbsoluteUri[..Math.Min(url.AbsoluteUri.Length, 700)],
                    name[..Math.Min(name.Length, 150)]));
            }
            catch (PlaywrightException)
            {
                // A dynamically removed anchor is not a reliable navigation target.
            }
        }
        return (total, links);
    }

    // Shared, bounded DOM capture for one-shot reads and reused browser contexts.
    internal static async Task<BrowserSnapshot> SnapshotAsync(
        IPage page, Uri target, int? status, CancellationToken ct,
        int controlOffset = 0, int controlLimit = 24, bool textOnly = false)
    {
            string title = await page.TitleAsync().WaitAsync(ct);
            string visible;
            try { visible = await page.Locator("body").InnerTextAsync(new() { Timeout = 6000 }).WaitAsync(ct); }
            catch (PlaywrightException) { visible = ""; }
            bool truncated = visible.Length > 10000;
            if (truncated) visible = visible[..10000];
            if (textOnly)
                return new BrowserSnapshot(target.AbsoluteUri, page.Url,
                    title[..Math.Min(title.Length, 300)], status, visible, truncated, [], []);
            var links = new List<BrowserLink>();
            ILocator anchor = page.Locator("a[href]");
            int count = Math.Min(await anchor.CountAsync().WaitAsync(ct), 72);
            for (int i = 0; i < count && links.Count < 24; i++)
            {
                try
                {
                    ILocator one = anchor.Nth(i);
                    if (!await one.IsVisibleAsync().WaitAsync(ct)) continue;
                    string? href = await one.GetAttributeAsync("href").WaitAsync(ct);
                    string? text = await one.TextContentAsync().WaitAsync(ct);
                    if (string.IsNullOrWhiteSpace(href) ||
                        !Uri.TryCreate(new Uri(page.Url), href, out Uri? link)) continue;
                    // A bounded, read-only approximation of the accessible link name.
                    // Execution also checks Playwright's exact accessible-role locator.
                    string accessible = await one.EvaluateAsync<string>("""
                        el => {
                            const trim = x => (x || '').replace(/\s+/g, ' ').trim();
                            const direct = trim(el.getAttribute('aria-label'));
                            if (direct) return direct;
                            const ids = trim(el.getAttribute('aria-labelledby')).split(/\s+/).filter(Boolean);
                            if (ids.length) {
                                const joined = ids.map(id => trim(document.getElementById(id)?.textContent))
                                  .filter(Boolean).join(' ');
                                if (joined) return joined;
                            }
                            const visible = trim(el.innerText);
                            if (visible) return visible;
                            return trim(el.querySelector('img[alt]')?.getAttribute('alt')) ||
                              trim(el.getAttribute('title'));
                        }
                        """).WaitAsync(ct);
                    string concise = (text ?? "").Trim();
                    links.Add(new BrowserLink(
                        concise[..Math.Min(concise.Length, 120)],
                        link.AbsoluteUri[..Math.Min(link.AbsoluteUri.Length, 700)],
                        accessible[..Math.Min(accessible.Length, 150)]));
                }
                catch (PlaywrightException)
                {
                    // A dynamically removed anchor is not grounds for trusting a stale target.
                }
            }
            // Metadata only; do not read input values, credentials, or HTML markup.
            // Controls are identified for later, separately authorized interactions.
            var elements = new List<BrowserElement>();
            ILocator controls = page.Locator("button, input, select, textarea, [role='button']");
            // Ordinary snapshots inspect the first 24 controls. Approved paged reads
            // can inspect later controls without exposing input values or the whole DOM.
            if (controlOffset < 0 || controlLimit is < 1 or > 24 ||
                controlOffset > BrowserControlPagination.MaxPageIndex * BrowserControlPagination.PageSize)
                throw new ArgumentOutOfRangeException(nameof(controlOffset));
            int totalControlCount = await controls.CountAsync().WaitAsync(ct);
            int controlEnd = Math.Min(totalControlCount, controlOffset + controlLimit);
            for (int i = controlOffset; i < controlEnd; i++)
            {
                try
                {
                    ILocator control = controls.Nth(i);
                    if (!await control.IsVisibleAsync().WaitAsync(ct)) continue;
                    string tag = await control.EvaluateAsync<string>("el => el.tagName.toLowerCase()").WaitAsync(ct);
                    string type = (await control.GetAttributeAsync("type").WaitAsync(ct) ?? "").ToLowerInvariant();
                    if (type is "password" or "hidden") continue;
                    string role = await control.GetAttributeAsync("role").WaitAsync(ct) ?? "";
                    string label = await control.GetAttributeAsync("aria-label").WaitAsync(ct) ??
                        await control.GetAttributeAsync("name").WaitAsync(ct) ??
                        await control.GetAttributeAsync("placeholder").WaitAsync(ct) ?? "";
                    // Input/textarea textContent can contain user text. Never include it.
                    string content = tag is "input" or "textarea" or "select" ? "" :
                        await control.TextContentAsync().WaitAsync(ct) ?? "";
                    string semanticRole = BrowserSemanticControl.RoleFor(tag, type, role);
                    // Used only for identity/locating; never read an element's value.
                    string accessibleName = await control.EvaluateAsync<string>("""
                        el => {
                            const trim = x => (x || '').trim();
                            const direct = trim(el.getAttribute('aria-label'));
                            if (direct) return direct;
                            const ids = trim(el.getAttribute('aria-labelledby')).split(/\s+/).filter(Boolean);
                            if (ids.length) {
                                const joined = ids.map(id => trim(document.getElementById(id)?.textContent)).filter(Boolean).join(' ');
                                if (joined) return joined;
                            }
                            const labels = el.labels ? Array.from(el.labels).map(x => trim(x.textContent)).filter(Boolean) : [];
                            if (labels.length) return labels.join(' ');
                            if (el.tagName.toLowerCase() === 'button') return trim(el.innerText);
                            return trim(el.getAttribute('title')) || trim(el.getAttribute('placeholder'));
                        }
                        """).WaitAsync(ct);
                    // Option labels are public UI metadata, NOT the currently selected value.
                    // Bound the list to avoid exposing a page's entire DOM or form contents.
                    IReadOnlyList<string>? options = null;
                    if (tag == "select" && semanticRole == "combobox")
                    {
                        string[] labels = await control.EvaluateAsync<string[]>("""
                            el => Array.from(el.options || []).slice(0, 24)
                              .map(x => (x.label || x.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 100))
                            """).WaitAsync(ct);
                        options = labels;
                    }
                    bool disabled = await control.IsDisabledAsync().WaitAsync(ct);
                    string concise = content.Trim();
                    // Standard accessibility state only: do not capture input values,
                    // arbitrary DOM attributes, credentials, or page source markup.
                    bool? expanded = ParseAriaBool(await control.GetAttributeAsync("aria-expanded").WaitAsync(ct));
                    bool? pressed = ParseAriaBool(await control.GetAttributeAsync("aria-pressed").WaitAsync(ct));
                    bool? checkedState = ParseAriaBool(await control.GetAttributeAsync("aria-checked").WaitAsync(ct));
                    // A restricted public HTML ID can distinguish controls sharing an accessible name.
                    // Do not read current input values or unrelated/private DOM attributes.
                    string htmlId = "";
                    if (tag == "input" && (type is "checkbox" or "radio"))
                    {
                        string candidateId = await control.GetAttributeAsync("id").WaitAsync(ct) ?? "";
                        if (BrowserNativeCheckedControl.IsSafeHtmlId(candidateId)) htmlId = candidateId;
                    }
                    // Native checked state is accessible UI metadata, never an input value.
                    bool? nativeChecked = tag == "input" && (type is "checkbox" or "radio")
                        ? await control.IsCheckedAsync().WaitAsync(ct) : null;
                    elements.Add(new BrowserElement(
                        tag[..Math.Min(tag.Length, 40)], role[..Math.Min(role.Length, 60)],
                        label[..Math.Min(label.Length, 150)], type[..Math.Min(type.Length, 50)],
                        concise[..Math.Min(concise.Length, 160)], disabled, i,
                        expanded, pressed, checkedState,
                        accessibleName[..Math.Min(accessibleName.Length, 150)], semanticRole, options,
                        nativeChecked, htmlId));
                }
                catch (PlaywrightException)
                {
                    // An element disappearing during inspection is not a page failure.
                    // Ignore it instead of converting one dynamic control into a failed visit.
                }
            }
            return new BrowserSnapshot(target.AbsoluteUri, page.Url,
                title[..Math.Min(title.Length, 300)], status, visible,
                truncated, links, elements);
    }
}

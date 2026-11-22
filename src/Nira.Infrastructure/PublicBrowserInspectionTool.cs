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
        "sessions or saved cookies. Each visit requires host approval. Does not grant " +
        "access to localhost, intranet or private IPs. Other origins are blocked.",
        ToolRisk.ReadExternal,
        JsonToolSchema.Schema("""
        {"type":"object","properties":{"url":{"type":"string","minLength":12,"maxLength":2048}},
         "required":["url"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement arguments) =>
        PublicBrowserUrlPolicy.Parse(arguments.GetProperty("url").GetString() ?? "").AbsoluteUri;

    public async Task<ToolReceipt> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        Uri uri = PublicBrowserUrlPolicy.Parse(arguments.GetProperty("url").GetString() ?? "");
        try
        {
            BrowserSnapshot observed = await _reader.ReadAsync(uri, ct);
            if (!PublicBrowserUrlPolicy.AllowsSameHostGet(observed.FinalUrl, uri.Host, "GET"))
                return ToolReceipt.Fail("BROWSER_REDIRECT_BLOCKED",
                    "The observed final page was not the approved public HTTPS host.");
            return ToolReceipt.Ok("Public browser page inspected", observed);
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

public sealed record BrowserLink(string Text, string Url);
public sealed record BrowserSnapshot(string RequestedUrl, string FinalUrl,
    string Title, int? HttpStatus, string VisibleText, bool TextTruncated,
    IReadOnlyList<BrowserLink> Links, IReadOnlyList<BrowserElement>? Elements = null);

/// <summary>Read-only element metadata; never includes input values or password contents.</summary>
public sealed record BrowserElement(string Tag, string Role, string Label, string Type, string Text, bool Disabled, int DomIndex = -1);

/// <summary>Injectable page reader for deterministic agent tests without launching a browser.</summary>
public interface IBrowserPageReader
{
    Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct);
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
    public async Task<BrowserSnapshot> ReadAsync(Uri target, CancellationToken ct)
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
            return await SnapshotAsync(page, target, initial?.Status, ct);
        }
        finally { await context.CloseAsync(); }
    }

    // Shared, bounded DOM capture for one-shot reads and reused browser contexts.
    internal static async Task<BrowserSnapshot> SnapshotAsync(
        IPage page, Uri target, int? status, CancellationToken ct)
    {
            string title = await page.TitleAsync().WaitAsync(ct);
            string visible;
            try { visible = await page.Locator("body").InnerTextAsync(new() { Timeout = 6000 }).WaitAsync(ct); }
            catch (PlaywrightException) { visible = ""; }
            bool truncated = visible.Length > 10000;
            if (truncated) visible = visible[..10000];
            var links = new List<BrowserLink>();
            ILocator anchor = page.Locator("a[href]");
            int count = Math.Min(await anchor.CountAsync().WaitAsync(ct), 24);
            for (int i = 0; i < count; i++)
            {
                ILocator one = anchor.Nth(i);
                string? href = await one.GetAttributeAsync("href").WaitAsync(ct);
                string? name = await one.TextContentAsync().WaitAsync(ct);
                if (string.IsNullOrWhiteSpace(href) ||
                    !Uri.TryCreate(new Uri(page.Url), href, out Uri? link)) continue;
                links.Add(new BrowserLink((name ?? "").Trim()[..Math.Min((name ?? "").Trim().Length, 120)],
                    link.AbsoluteUri[..Math.Min(link.AbsoluteUri.Length, 700)]));
            }
            // Metadata only; do not read input values, credentials, or HTML markup.
            // Controls are identified for later, separately authorized interactions.
            var elements = new List<BrowserElement>();
            ILocator controls = page.Locator("button, input, select, textarea, [role='button']");
            int controlCount = Math.Min(await controls.CountAsync().WaitAsync(ct), 24);
            for (int i = 0; i < controlCount; i++)
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
                    string content = await control.TextContentAsync().WaitAsync(ct) ?? "";
                    bool disabled = await control.IsDisabledAsync().WaitAsync(ct);
                    string concise = content.Trim();
                    elements.Add(new BrowserElement(
                        tag[..Math.Min(tag.Length, 40)], role[..Math.Min(role.Length, 60)],
                        label[..Math.Min(label.Length, 150)], type[..Math.Min(type.Length, 50)],
                        concise[..Math.Min(concise.Length, 160)], disabled, i));
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

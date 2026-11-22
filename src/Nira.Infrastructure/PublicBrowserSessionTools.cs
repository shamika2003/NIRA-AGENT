using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Playwright;
using Nira.Core;

namespace Nira.Infrastructure;

/// <summary>
/// A short-lived, unauthenticated Edge session shared by tool calls in one NIRA host.
/// Browser-session IDs do not survive a CLI/desktop restart. This is not a sandbox.
/// </summary>
public sealed record BrowserSessionObservation(string SessionId, BrowserSnapshot Page);
public sealed record BrowserSessionLinkObservation(string SessionId, string SourceUrl,
    string LinkText, string LinkUrl, BrowserSnapshot Page);

// Never echo filled input text back to the model or into execution receipts.
public sealed record BrowserSessionTextObservation(string SessionId, string PageUrl,
    int DomIndex, string ElementTag, string ElementLabel, int CharactersEntered,
    BrowserSnapshot Page);

// Records the observed button identity and post-click page, never user input values.
public sealed record BrowserSessionButtonObservation(string SessionId, string PageUrl,
    int DomIndex, string ElementLabel, string ElementText, BrowserSnapshot Page);

public interface IPublicBrowserSessions
{
    Task<BrowserSessionObservation> OpenAsync(Uri url, CancellationToken ct);
    Task<BrowserSnapshot> InspectAsync(string sessionId, CancellationToken ct);
    Task<BrowserSessionLinkObservation> FollowAsync(string sessionId, Uri expectedSource,
        int linkIndex, Uri expectedDestination, CancellationToken ct);
    Task<bool> CloseAsync(string sessionId, CancellationToken ct);
    Task<BrowserSessionTextObservation> FillTextAsync(string sessionId, Uri expectedUrl,
        int domIndex, string expectedTag, string expectedLabel, string text, CancellationToken ct);
    Task<BrowserSessionButtonObservation> ClickButtonAsync(string sessionId, Uri expectedUrl,
        int domIndex, string expectedLabel, string expectedText, CancellationToken ct);
}

internal static class BrowserSessionArguments
{
    public static string Id(JsonElement args)
    {
        string id = args.GetProperty("sessionId").GetString() ?? "";
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Use the 32-character sessionId from browser.session_open; sessions expire when NIRA exits.");
        return id;
    }

    public static (Uri Source, Uri Destination, int Index) Link(JsonElement args)
    {
        Uri source = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedSourceUrl").GetString() ?? "");
        Uri destination = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedLinkUrl").GetString() ?? "");
        int index = args.GetProperty("linkIndex").GetInt32();
        if (index is < 0 or >= 24 ||
            !string.Equals(source.Host, destination.Host, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select a link index from 0 to 23 and a destination on the approved HTTPS hostname.");
        return (source, destination, index);
    }
}

public sealed class BrowserSessionOpenTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_open",
        "Create an isolated, temporary Microsoft Edge browser session on an approved public HTTPS website. " +
        "Keep that same page context for later read-only inspection and host-approved link navigation. " +
        "No saved profile, login, DOM clicks, form submission or downloads. Session ends when NIRA exits.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"url":{"type":"string","minLength":12,"maxLength":2048}},
         "required":["url"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args) =>
        PublicBrowserUrlPolicy.Parse(args.GetProperty("url").GetString() ?? "").AbsoluteUri;

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        Uri url = PublicBrowserUrlPolicy.Parse(args.GetProperty("url").GetString() ?? "");
        try
        {
            BrowserSessionObservation result = await sessions.OpenAsync(url, ct);
            return ToolReceipt.Ok("Opened and inspected temporary public browser session", result, changedState: true);
        }
        catch (Exception ex) when (ex is PlaywrightException or SocketException or
            IOException or TimeoutException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_SESSION_OPEN_FAILED", "Could not open public browser session: " + ex.Message); }
    }
}

public sealed class BrowserSessionInspectTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_inspect",
        "Inspect the current visible page in an existing temporary public browser session. " +
        "Returns bounded title, links, text and control metadata; never reads input values. " +
        "Cannot navigate; host approval is still required for each external inspection.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"sessionId":{"type":"string","minLength":32,"maxLength":32}},
         "required":["sessionId"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement args) => "public browser session " + BrowserSessionArguments.Id(args);

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string id = BrowserSessionArguments.Id(args);
        try { return ToolReceipt.Ok("Inspected current browser session", await sessions.InspectAsync(id, ct)); }
        catch (Exception ex) when (ex is PlaywrightException or IOException or
            TimeoutException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_SESSION_INSPECT_FAILED", ex.Message); }
    }
}

public sealed class BrowserSessionFollowTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_follow",
        "Navigate within an existing temporary public Edge session to a specifically approved, " +
        "same-host HTTPS URL already observed in its links. Pass the exact current source URL, " +
        "zero-based link index, and expected destination URL. Rechecks live links before navigating. " +
        "No DOM click, login, POST, downloads or cross-origin navigation; requires approval each time.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sessionId":{"type":"string","minLength":32,"maxLength":32},
          "expectedSourceUrl":{"type":"string","minLength":12,"maxLength":2048},
          "linkIndex":{"type":"integer"},
          "expectedLinkUrl":{"type":"string","minLength":12,"maxLength":2048}},
         "required":["sessionId","expectedSourceUrl","linkIndex","expectedLinkUrl"],
         "additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        var (source, target, index) = BrowserSessionArguments.Link(args);
        return "session " + id + ": " + source.AbsoluteUri +
            " [link index " + index + "] -> " + target.AbsoluteUri;
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string id = BrowserSessionArguments.Id(args);
        var (source, destination, index) = BrowserSessionArguments.Link(args);
        try
        {
            BrowserSessionLinkObservation result = await sessions.FollowAsync(id, source, index, destination, ct);
            return ToolReceipt.Ok("Navigated approved public browser session link", result, changedState: true);
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or
            TimeoutException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_SESSION_FOLLOW_FAILED", ex.Message); }
    }
}

public sealed class BrowserSessionCloseTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_close", "Close a temporary browser context and discard its cookies and page state.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"sessionId":{"type":"string","minLength":32,"maxLength":32}},
         "required":["sessionId"],"additionalProperties":false}
        """), HasSideEffects: true);

    public string GetTarget(JsonElement args)
    {
        _ = BrowserSessionArguments.Id(args);
        // Closing an ephemeral local context does not access a new external resource.
        return "workspace";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string id = BrowserSessionArguments.Id(args);
        try
        {
            bool closed = await sessions.CloseAsync(id, ct);
            return closed
                ? ToolReceipt.Ok("Closed and discarded public browser session", new { sessionId = id }, changedState: true)
                : ToolReceipt.Fail("BROWSER_SESSION_NOT_FOUND", "Session is already closed or the NIRA host restarted.");
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_SESSION_CLOSE_FAILED", ex.Message); }
    }
}

/// <summary>
/// Long-lived only within this NIRA process. A per-session mutex serializes reads and
/// approved navigation, and a navigation gate blocks unapproved page transitions.
/// Per-host DNS checks are best effort, NOT network isolation/DNS pinning.
/// </summary>
public sealed class EdgePublicBrowserSessions : IPublicBrowserSessions, IDisposable
{
    private const int MaximumSessions = 2;
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, ActiveSession> _sessions = new(StringComparer.Ordinal);
    private bool _disposed;

    private sealed class NavigationGate(string permittedUrl)
    {
        public string PermittedUrl = permittedUrl;
        public int Requests;
        public volatile bool BlockNetworkAfterInteraction;
    }

    private sealed class ActiveSession(IPlaywright playwright, IBrowser browser, IBrowserContext context,
        IPage page, Uri host, NavigationGate gate)
    {
        public IPlaywright Driver { get; } = playwright;
        public IBrowser Browser { get; } = browser;
        public IBrowserContext Context { get; } = context;
        public IPage Page { get; } = page;
        public Uri Host { get; } = host;
        public NavigationGate Gate { get; } = gate;
        public SemaphoreSlim Mutex { get; } = new(1, 1);
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;

        public async Task DisposeAsync()
        {
            try { await Context.CloseAsync(); }
            finally
            {
                try { await Browser.CloseAsync(); }
                finally { Driver.Dispose(); }
            }
        }
    }

    private ActiveSession Find(string id)
    {
        if (_disposed || !_sessions.TryGetValue(id, out ActiveSession? session))
            throw new InvalidOperationException("Browser session not found. Open a new session; sessions do not survive NIRA restarts.");
        if (DateTimeOffset.UtcNow - session.LastActivity > IdleLimit)
            throw new InvalidOperationException("Browser session expired after 15 minutes of inactivity. Close it and open a new one.");
        return session;
    }

    public async Task<BrowserSessionObservation> OpenAsync(Uri url, CancellationToken ct)
    {
        if (_disposed) throw new InvalidOperationException("Browser host has shut down.");
        // Reclaim expired sessions before refusing a new one. This is a lazy idle
        // eviction, not a background timer; the session is also closed on host exit.
        if (_sessions.Count >= MaximumSessions)
        {
            foreach (var entry in _sessions.ToArray())
                if (DateTimeOffset.UtcNow - entry.Value.LastActivity > IdleLimit)
                    await CloseAsync(entry.Key, ct);
        }
        if (_sessions.Count >= MaximumSessions)
            throw new InvalidOperationException("At most two live browser sessions are allowed. Close one before opening another.");
        Uri target = PublicBrowserUrlPolicy.Parse(url.AbsoluteUri);
        PublicBrowserUrlPolicy.RequirePublicDns(await Dns.GetHostAddressesAsync(target.DnsSafeHost, ct));
        IPlaywright? driver = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        try
        {
            driver = await Playwright.CreateAsync().WaitAsync(ct);
            browser = await driver.Chromium.LaunchAsync(new() {
                Channel = "msedge", Headless = true, Timeout = 15000
            }).WaitAsync(ct);
            context = await browser.NewContextAsync(new() {
                AcceptDownloads = false, IgnoreHTTPSErrors = false,
                ServiceWorkers = ServiceWorkerPolicy.Block
            }).WaitAsync(ct);
            var gate = new NavigationGate(target.AbsoluteUri);
            await context.RouteAsync("**/*", async route => {
                string requested = route.Request.Url;
                if (gate.BlockNetworkAfterInteraction ||
                    Interlocked.Increment(ref gate.Requests) > 100 ||
                    !PublicBrowserUrlPolicy.AllowsSameHostGet(requested, target.Host, route.Request.Method) ||
                    (route.Request.IsNavigationRequest &&
                     !string.Equals(requested, gate.PermittedUrl, StringComparison.Ordinal)))
                {
                    await route.AbortAsync();
                    return;
                }
                IAPIResponse response = await route.FetchAsync(new() {
                    MaxRedirects = 0, Timeout = 15000
                });
                if (response.Status is >= 300 and < 400)
                {
                    await route.AbortAsync(); // Never silently follow a redirect.
                    return;
                }
                await route.FulfillAsync(new() { Response = response });
            });
            IPage page = await context.NewPageAsync().WaitAsync(ct);
            IResponse? response = await page.GotoAsync(target.AbsoluteUri, new() {
                WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000
            }).WaitAsync(ct);
            if (!string.Equals(page.Url, target.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("The opened page did not remain at the exact approved URL.");
            BrowserSnapshot snapshot = await EdgePublicPageReader.SnapshotAsync(page, target, response?.Status, ct);
            var active = new ActiveSession(driver, browser, context, page, target, gate);
            string id = Guid.NewGuid().ToString("N");
            if (!_sessions.TryAdd(id, active))
                throw new InvalidOperationException("Browser session registration failed.");
            // The stored session owns all handles after registration.
            driver = null;
            browser = null;
            context = null;
            return new BrowserSessionObservation(id, snapshot);
        }
        finally
        {
            if (context is not null) await context.CloseAsync();
            if (browser is not null) await browser.CloseAsync();
            driver?.Dispose();
        }
    }

    public async Task<BrowserSnapshot> InspectAsync(string sessionId, CancellationToken ct)
    {
        ActiveSession s = Find(sessionId);
        await s.Mutex.WaitAsync(ct);
        try
        {
            if (!string.Equals(s.Page.Url, s.Gate.PermittedUrl, StringComparison.Ordinal))
                throw new InvalidOperationException("Page changed without a host-approved navigation; close this session.");
            BrowserSnapshot snapshot = await EdgePublicPageReader.SnapshotAsync(
                s.Page, new Uri(s.Gate.PermittedUrl), null, ct);
            s.LastActivity = DateTimeOffset.UtcNow;
            return snapshot;
        }
        finally { s.Mutex.Release(); }
    }

    public async Task<BrowserSessionLinkObservation> FollowAsync(string sessionId, Uri expectedSource,
        int linkIndex, Uri expectedDestination, CancellationToken ct)
    {
        ActiveSession s = Find(sessionId);
        await s.Mutex.WaitAsync(ct);
        try
        {
            if (s.Gate.BlockNetworkAfterInteraction)
                throw new InvalidOperationException("A browser interaction occurred in this session. Close and reopen it before navigating.");
            if (!string.Equals(s.Page.Url, expectedSource.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(s.Gate.PermittedUrl, expectedSource.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("Session is no longer at the exact observed source URL.");
            if (!string.Equals(expectedSource.Host, s.Host.Host, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(expectedDestination.Host, s.Host.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Session hostname differs from the approved destination.");
            BrowserSnapshot current = await EdgePublicPageReader.SnapshotAsync(s.Page, expectedSource, null, ct);
            if (linkIndex < 0 || linkIndex >= current.Links.Count)
                throw new InvalidOperationException("The observed link index is no longer present.");
            BrowserLink link = current.Links[linkIndex];
            if (!string.Equals(link.Url, expectedDestination.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("The link changed after inspection; re-inspect before approving navigation.");
            PublicBrowserUrlPolicy.RequirePublicDns(
                await Dns.GetHostAddressesAsync(expectedDestination.DnsSafeHost, ct));
            // The route handler enforces this exact destination on all page navigations.
            s.Gate.PermittedUrl = expectedDestination.AbsoluteUri;
            IResponse? response = await s.Page.GotoAsync(expectedDestination.AbsoluteUri, new() {
                WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000
            }).WaitAsync(ct);
            if (!string.Equals(s.Page.Url, expectedDestination.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException("Destination moved away from exact approved URL; close session.");
            BrowserSnapshot snapshot = await EdgePublicPageReader.SnapshotAsync(
                s.Page, expectedDestination, response?.Status, ct);
            s.LastActivity = DateTimeOffset.UtcNow;
            return new BrowserSessionLinkObservation(sessionId, current.FinalUrl, link.Text,
                expectedDestination.AbsoluteUri, snapshot);
        }
        finally { s.Mutex.Release(); }
    }

    /// <summary>
    /// Fill a verified, labeled text/search field. Entry is deliberately terminal
    /// for browsing in this session: network requests and later navigation are
    /// blocked until the session is closed. It is not a complete browser sandbox;
    /// sites may still react to DOM/input events or already-open connections.
    /// </summary>
    public async Task<BrowserSessionTextObservation> FillTextAsync(string sessionId, Uri expectedUrl,
        int domIndex, string expectedTag, string expectedLabel, string text, CancellationToken ct)
    {
        ActiveSession s = Find(sessionId);
        await s.Mutex.WaitAsync(ct);
        try
        {
            if (s.Gate.BlockNetworkAfterInteraction)
                throw new InvalidOperationException("A browser interaction already occurred. Close and reopen the session before entering text.");
            if (!string.Equals(s.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(s.Gate.PermittedUrl, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(s.Host.Host, expectedUrl.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Session is not at the exact approved page URL.");

            BrowserSnapshot before = await EdgePublicPageReader.SnapshotAsync(s.Page, expectedUrl, null, ct);
            BrowserElement? selected = before.Elements?.FirstOrDefault(x => x.DomIndex == domIndex);
            if (selected is null || selected.Disabled ||
                !string.Equals(selected.Tag, expectedTag, StringComparison.Ordinal) ||
                !string.Equals(selected.Label, expectedLabel, StringComparison.Ordinal) ||
                !BrowserSessionFillTextTool.IsAllowedControl(selected))
                throw new InvalidOperationException("Text control changed, disappeared, or is not an allowed labeled text/search field.");

            ILocator control = s.Page.Locator("button, input, select, textarea, [role='button']").Nth(domIndex);
            if (!await control.IsVisibleAsync().WaitAsync(ct) ||
                !await control.IsEditableAsync().WaitAsync(ct))
                throw new InvalidOperationException("The approved field is no longer visible and editable.");

            // From this boundary onward, the input event may already have occurred.
            // Fail with uncertain-effect reconciliation on any inability to verify.
            s.Gate.BlockNetworkAfterInteraction = true;
            try
            {
                await control.FillAsync(text, new() { Timeout = 6000 }).WaitAsync(ct);
                string actual = await control.InputValueAsync().WaitAsync(ct);
                if (!string.Equals(actual, text, StringComparison.Ordinal) ||
                    !string.Equals(s.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal))
                    throw new InvalidOperationException("Input value or page URL could not be verified after the event.");
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(s.Page, expectedUrl, null, ct);
                s.LastActivity = DateTimeOffset.UtcNow;
                return new BrowserSessionTextObservation(sessionId, expectedUrl.AbsoluteUri,
                    domIndex, selected.Tag, selected.Label, text.Length, after);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException)
            {
                throw new BrowserInteractionUncertainException(
                    "Text entry may have taken effect, but verification failed. " +
                    "NIRA must reconcile before retrying this interaction.", ex);
            }
        }
        finally { s.Mutex.Release(); }
    }

    /// <summary>
    /// Dispatch one explicitly approved click to a native type=button control only.
    /// Network requests are aborted before click dispatch; form ancestors and
    /// submit/reset controls are refused. Already-active scripts/connections can
    /// still cause effects; this is NOT a browser sandbox.
    /// </summary>
    public async Task<BrowserSessionButtonObservation> ClickButtonAsync(string sessionId,
        Uri expectedUrl, int domIndex, string expectedLabel, string expectedText, CancellationToken ct)
    {
        ActiveSession s = Find(sessionId);
        await s.Mutex.WaitAsync(ct);
        try
        {
            if (s.Gate.BlockNetworkAfterInteraction)
                throw new InvalidOperationException("A browser interaction already occurred in this session. Close it before another click.");
            if (!string.Equals(s.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(s.Gate.PermittedUrl, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(s.Host.Host, expectedUrl.Host, StringComparison.OrdinalIgnoreCase) ||
                s.Context.Pages.Count != 1)
                throw new InvalidOperationException("Session is not at the exact approved page or has an unexpected extra tab.");

            BrowserSnapshot before = await EdgePublicPageReader.SnapshotAsync(s.Page, expectedUrl, null, ct);
            BrowserElement? selected = before.Elements?.FirstOrDefault(x => x.DomIndex == domIndex);
            if (selected is null || selected.Disabled ||
                !string.Equals(selected.Label, expectedLabel, StringComparison.Ordinal) ||
                !string.Equals(selected.Text, expectedText, StringComparison.Ordinal) ||
                !BrowserSessionClickButtonTool.IsAllowedControl(selected))
                throw new InvalidOperationException(
                    "Button changed, disappeared, is disabled or is not an explicit type=button control.");

            ILocator button = s.Page.Locator("button, input, select, textarea, [role='button']").Nth(domIndex);
            if (!await button.IsVisibleAsync().WaitAsync(ct) ||
                !await button.IsEnabledAsync().WaitAsync(ct) ||
                !string.Equals(await button.GetAttributeAsync("type").WaitAsync(ct),
                    "button", StringComparison.OrdinalIgnoreCase) ||
                await button.EvaluateAsync<bool>("el => !!el.closest('form')").WaitAsync(ct))
                throw new InvalidOperationException(
                    "The target is no longer a visible enabled native type=button outside forms.");

            // From this boundary the click may have occurred. Unknown outcomes
            // must be escalated to the kernel's reconciliation barrier.
            s.Gate.BlockNetworkAfterInteraction = true;
            try
            {
                await button.ClickAsync(new() { Timeout = 6000 }).WaitAsync(ct);
                if (!string.Equals(s.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                    s.Context.Pages.Count != 1)
                    throw new InvalidOperationException(
                        "Click caused an unexpected navigation or additional browser page.");
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(
                    s.Page, expectedUrl, null, ct);
                s.LastActivity = DateTimeOffset.UtcNow;
                return new BrowserSessionButtonObservation(
                    sessionId, expectedUrl.AbsoluteUri, domIndex, selected.Label, selected.Text, after);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException)
            {
                throw new BrowserInteractionUncertainException(
                    "The approved button click may have taken effect but could not be fully verified. " +
                    "NIRA must reconcile before another interaction.", ex);
            }
        }
        finally { s.Mutex.Release(); }
    }

    public async Task<bool> CloseAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_sessions.TryRemove(sessionId, out ActiveSession? s)) return false;
        await s.Mutex.WaitAsync(ct);
        try { await s.DisposeAsync(); }
        finally { s.Mutex.Release(); }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _sessions.Keys.ToArray())
        {
            if (_sessions.TryRemove(id, out ActiveSession? session))
            {
                // Host shutdown is synchronous in the CLI and WPF app.
                session.DisposeAsync().GetAwaiter().GetResult();
            }
        }
    }
}

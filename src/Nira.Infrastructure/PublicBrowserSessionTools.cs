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

// Paged, read-only control metadata. Never captures field values or arbitrary DOM markup.
public sealed record BrowserSessionControlPageObservation(string SessionId, string PageUrl,
    int PageNumber, int TotalControlCount, IReadOnlyList<BrowserElement> Elements);

public sealed record BrowserSessionLinkPageObservation(string SessionId, string PageUrl,
    int PageNumber, int TotalLinkCount, IReadOnlyList<BrowserLink> Links);

// A bounded, separately approved segment of public rendered page text. This is
// body.innerText (never input.value, outerHTML, cookies or page scripts).
public sealed record BrowserSessionVisibleTextPageObservation(
    string SessionId, string PageUrl, string PageTitle, int PageNumber,
    int TotalVisibleCharacters, string Text, bool HasMore, bool ReadLimitReached);

public static class BrowserVisibleTextPagination
{
    public const int PageSize = 4000;
    public const int MaxPageIndex = 15;
    public const int MaximumReadableCharacters = PageSize * (MaxPageIndex + 1);

    public static int OffsetForPage(int pageNumber)
    {
        if (pageNumber is < 0 or > MaxPageIndex)
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                "Visible-text page must be 0–15.");
        return checked(pageNumber * PageSize);
    }

    public static BrowserSessionVisibleTextPageObservation Slice(
        string sessionId, string pageUrl, string pageTitle, string visibleText, int pageNumber)
    {
        int offset = OffsetForPage(pageNumber);
        int length = offset >= visibleText.Length
            ? 0 : Math.Min(PageSize, visibleText.Length - offset);
        string chunk = length == 0 ? "" : visibleText.Substring(offset, length);
        bool remaining = offset + length < visibleText.Length;
        bool bounded = remaining && pageNumber == MaxPageIndex;
        return new BrowserSessionVisibleTextPageObservation(sessionId, pageUrl,
            pageTitle.Length <= 300 ? pageTitle : pageTitle[..300], pageNumber,
            visibleText.Length, chunk, remaining && !bounded, bounded);
    }
}

public static class BrowserLinkPagination
{
    public const int PageSize = 24;
    public const int MaxPageIndex = 63;
    public static int OffsetForPage(int page)
    {
        if (page is < 0 or > MaxPageIndex)
            throw new ArgumentOutOfRangeException(nameof(page), "Link page must be 0–63.");
        return checked(page * PageSize);
    }
}

public static class BrowserControlPagination
{
    public const int PageSize = 24;
    public const int MaxPageIndex = 63;

    public static int OffsetForPage(int page)
    {
        if (page is < 0 or > MaxPageIndex)
            throw new ArgumentOutOfRangeException(nameof(page), "Control page must be 0–63.");
        return checked(page * PageSize);
    }

    public static int PageForIndex(int index)
    {
        if (index < 0 || index >= (MaxPageIndex + 1) * PageSize)
            throw new InvalidOperationException("Target is outside the bounded control inspection range.");
        return index / PageSize;
    }
}

public sealed record BrowserSessionLinkObservation(string SessionId, string SourceUrl,
    string LinkText, string LinkUrl, BrowserSnapshot Page, string NavigationUrl = "");

// Never echo filled input text back to the model or into execution receipts.
public sealed record BrowserSessionTextObservation(string SessionId, string PageUrl,
    int DomIndex, string ElementTag, string ElementLabel, int CharactersEntered,
    BrowserSnapshot Page);

// Records the observed button identity and post-click page, never user input values.
public sealed record BrowserSessionButtonObservation(string SessionId, string PageUrl,
    int DomIndex, string ElementLabel, string ElementText, BrowserSnapshot Page,
    BrowserButtonStateEvidence? AccessibleState = null);

// The selected option is an explicitly approved public label. Previous field values
// and underlying option values are never included in a receipt or page snapshot.
public sealed record BrowserSessionSelectObservation(string SessionId, string PageUrl,
    string AccessibleName, string OptionLabel, bool SelectionVerified, BrowserSnapshot Page);

// Native input checked state is not a form value. A no-op does not dispatch a click.
public sealed record BrowserSessionCheckedObservation(string SessionId, string PageUrl,
    string Role, string AccessibleName, bool RequestedChecked, bool CheckedBefore,
    bool CheckedAfter, bool StateChangeObserved, BrowserSnapshot Page, string HtmlId = "");

/// <summary>
/// Generic accessibility-state evidence for one matching observed control. A dispatched
/// click is not proof that a requested UI change occurred. The before/after pair
/// is comparable only when the observed target metadata is unchanged. This does
/// not prove the underlying DOM node was not replaced.
/// </summary>
public sealed record BrowserButtonStateEvidence(
    bool MatchingControlObserved,
    bool? ExpandedBefore, bool? ExpandedAfter,
    bool? PressedBefore, bool? PressedAfter,
    bool? CheckedBefore, bool? CheckedAfter,
    bool VerifiedStateChange)
{
    public static BrowserButtonStateEvidence Compare(BrowserElement before, BrowserSnapshot after)
    {
        BrowserElement? next = after.Elements?.FirstOrDefault(e => e.DomIndex == before.DomIndex);
        bool matched = next is not null &&
            string.Equals(next.Tag, before.Tag, StringComparison.Ordinal) &&
            string.Equals(next.Type, before.Type, StringComparison.Ordinal) &&
            string.Equals(next.Role, before.Role, StringComparison.Ordinal) &&
            string.Equals(next.Label, before.Label, StringComparison.Ordinal) &&
            string.Equals(next.Text, before.Text, StringComparison.Ordinal);
        bool changed = matched &&
            (Changed(before.AriaExpanded, next!.AriaExpanded) ||
             Changed(before.AriaPressed, next!.AriaPressed) ||
             Changed(before.AriaChecked, next!.AriaChecked));
        return new BrowserButtonStateEvidence(
            matched,
            before.AriaExpanded, matched ? next!.AriaExpanded : null,
            before.AriaPressed, matched ? next!.AriaPressed : null,
            before.AriaChecked, matched ? next!.AriaChecked : null,
            changed);
    }

    private static bool Changed(bool? before, bool? after) =>
        before.HasValue && after.HasValue && before.Value != after.Value;
}

public interface IPublicBrowserSessions
{
    Task<BrowserSessionObservation> OpenAsync(Uri url, CancellationToken ct);
    Task<BrowserSnapshot> InspectAsync(string sessionId, CancellationToken ct);
    Task<BrowserSessionControlPageObservation> InspectControlsAsync(string sessionId,
        int pageNumber, CancellationToken ct);
    Task<BrowserSessionLinkPageObservation> InspectLinksAsync(string sessionId,
        int pageNumber, CancellationToken ct);
    Task<BrowserSessionVisibleTextPageObservation> InspectVisibleTextAsync(
        string sessionId, int pageNumber, CancellationToken ct);
    Task<BrowserSessionLinkObservation> FollowAsync(string sessionId, Uri expectedSource,
        int linkIndex, Uri expectedDestination, CancellationToken ct);
    Task<BrowserSessionLinkObservation> FollowByNameAsync(string sessionId, Uri expectedSource,
        string expectedName, Uri expectedDestination, CancellationToken ct,
        int? inspectedLinkPageNumber = null, bool preferCanonicalTrailingSlash = false);
    Task<bool> CloseAsync(string sessionId, CancellationToken ct);
    Task<BrowserSessionTextObservation> FillTextAsync(string sessionId, Uri expectedUrl,
        int domIndex, string expectedTag, string expectedLabel, string text, CancellationToken ct);
    Task<BrowserSessionButtonObservation> ClickButtonAsync(string sessionId, Uri expectedUrl,
        int domIndex, string expectedLabel, string expectedText, CancellationToken ct);
    Task<BrowserSemanticActionObservation> InteractByRoleAsync(string sessionId, Uri expectedUrl,
        string action, string role, string accessibleName, string? text, CancellationToken ct);
    Task<BrowserSessionSelectObservation> SelectOptionByNameAsync(string sessionId, Uri expectedUrl,
        string accessibleName, string optionLabel, CancellationToken ct);
    Task<BrowserSessionCheckedObservation> SetCheckedByNameAsync(string sessionId, Uri expectedUrl,
        string role, string accessibleName, bool isChecked, CancellationToken ct,
        string? expectedHtmlId = null);
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
        "Keep the same page context for inspection, approved link traversal and separately " +
        "authorized non-submitting text/button interactions. Each action requires a new host approval. " +
        "No saved profile, login, form submission or downloads. Session ends when NIRA exits.",
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

/// <summary>
/// Inspect later controls of a long page without enlarging the normal snapshot.
/// The host authorizes each bounded, read-only page of metadata.
/// </summary>
public sealed class BrowserSessionInspectControlsTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_inspect_controls",
        "Read ONE page of 24 visible control metadata entries in an existing public Edge session. " +
        "Use pageNumber=0 for the first 24 DOM controls, pageNumber=1 for the next 24, etc. " +
        "Returns original DOM indices, accessible role/name, safe HTML IDs, native checked state, " +
        "and total number of controls, but NEVER text field values or credentials. " +
        "Each read requires host approval and never navigates or interacts.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sessionId":{"type":"string","minLength":32,"maxLength":32},
          "pageNumber":{"type":"integer"}},
         "required":["sessionId","pageNumber"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        int page = args.GetProperty("pageNumber").GetInt32();
        _ = BrowserControlPagination.OffsetForPage(page);
        return "public browser session " + id + ", control page " + page;
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string id = BrowserSessionArguments.Id(args);
        int page = args.GetProperty("pageNumber").GetInt32();
        _ = BrowserControlPagination.OffsetForPage(page);
        try
        {
            BrowserSessionControlPageObservation observed =
                await sessions.InspectControlsAsync(id, page, ct);
            return ToolReceipt.Ok("Inspected bounded browser control metadata page", observed);
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or
            TimeoutException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_CONTROL_PAGE_FAILED", ex.Message); }
    }
}

public sealed class BrowserSessionInspectLinksTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_inspect_links",
        "Inspect one bounded page of up to 24 HTML anchors in an approved public browser session, " +
        "using pageNumber 0–63. Reports total anchor count, visible link names and exact URLs " +
        "without reading form values or changing page state. Each inspection requires host approval. " +
        "For later links, supply the observed pageNumber as linkPageNumber to " +
        "browser.session_follow_named_link; navigation requires a separate approval.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sessionId":{"type":"string","minLength":32,"maxLength":32},
          "pageNumber":{"type":"integer"}},
         "required":["sessionId","pageNumber"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        int page = args.GetProperty("pageNumber").GetInt32();
        _ = BrowserLinkPagination.OffsetForPage(page);
        return "public browser session " + id + ", link page " + page;
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string id = BrowserSessionArguments.Id(args);
        int page = args.GetProperty("pageNumber").GetInt32();
        _ = BrowserLinkPagination.OffsetForPage(page);
        try
        {
            BrowserSessionLinkPageObservation observed =
                await sessions.InspectLinksAsync(id, page, ct);
            return ToolReceipt.Ok("Inspected bounded browser link metadata page", observed);
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or
            TimeoutException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_LINK_PAGE_FAILED", ex.Message); }
    }
}

/// <summary>
/// Read exactly one bounded segment of rendered public text in an existing session.
/// No JavaScript evaluation, arbitrary selectors, form values, or navigation.
/// Every page read is independently approved by the host.
/// </summary>
public sealed class BrowserSessionInspectVisibleTextTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_inspect_text",
        "Read one numbered page of up to 4,000 characters of visible rendered text " +
        "from an already approved public Edge session. pageNumber 0–15 (maximum 64,000 " +
        "characters total); includes exact page URL, title, total length and whether more " +
        "pages exist. Do not read input values, passwords, page source, or DOM markup. " +
        "Does not click, navigate, or submit; host approval is required for every page.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sessionId":{"type":"string","minLength":32,"maxLength":32},
          "pageNumber":{"type":"integer"}},
         "required":["sessionId","pageNumber"],"additionalProperties":false}
        """));

    public string GetTarget(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        int page = args.GetProperty("pageNumber").GetInt32();
        _ = BrowserVisibleTextPagination.OffsetForPage(page);
        return "public browser session " + id + ", visible text page " + page;
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string id = BrowserSessionArguments.Id(args);
        int page = args.GetProperty("pageNumber").GetInt32();
        _ = BrowserVisibleTextPagination.OffsetForPage(page);
        try
        {
            BrowserSessionVisibleTextPageObservation result =
                await sessions.InspectVisibleTextAsync(id, page, ct);
            return ToolReceipt.Ok("Inspected bounded visible browser text page", result);
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or
            TimeoutException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_VISIBLE_TEXT_PAGE_FAILED", ex.Message); }
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

/// <summary>
/// Follow a uniquely named anchor without a DOM click. The destination is the
/// exact separately approved URL, never an href chosen after approval.
/// </summary>
public static class BrowserSemanticLink
{
    public static BrowserLink RequireUnique(IReadOnlyList<BrowserLink> links,
        string expectedName, Uri expectedDestination)
    {
        var matches = links.Where(link => string.Equals(link.AccessibleName,
            expectedName, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Expected exactly one observed link with that accessible name. Re-inspect the page.");
        if (!string.Equals(matches[0].Url, expectedDestination.AbsoluteUri,
            StringComparison.Ordinal))
            throw new InvalidOperationException("Observed link target differs from the exact approved URL. Re-inspect before navigating.");
        return matches[0];
    }
    // For inspected paged anchors, name AND exact href identify the target inside
    // the approved 24-anchor slice. Repeated names elsewhere on the page must not
    // invalidate an otherwise unique observed anchor. Duplicate name+href is unsafe.
    public static BrowserLink RequireExactInPage(IReadOnlyList<BrowserLink> links,
        string expectedName, Uri expectedDestination)
    {
        var matches = links.Where(link =>
            string.Equals(link.AccessibleName, expectedName, StringComparison.Ordinal) &&
            string.Equals(link.Url, expectedDestination.AbsoluteUri, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Expected exactly one visible anchor with this name AND exact URL in the inspected link page. Re-inspect.");
        return matches[0];
    }
}

/// <summary>
/// Optional direct navigation to the explicitly approved trailing-slash form of
/// a link's observed URL. Not a redirect follower: other locations stay blocked.
/// This is for directory-style public documentation links (e.g. /docs/images -> /docs/images/).
/// </summary>
public static class BrowserNamedLinkDestination
{
    public static Uri Resolve(Uri observedLinkUrl, bool preferCanonicalTrailingSlash)
    {
        if (!preferCanonicalTrailingSlash) return observedLinkUrl;
        string path = observedLinkUrl.AbsolutePath;
        string tail = path[(path.LastIndexOf('/') + 1)..];
        if (path == "/" || path.EndsWith("/", StringComparison.Ordinal) ||
            tail.Length == 0 || tail.Contains('.') ||
            observedLinkUrl.Query.Length != 0 || observedLinkUrl.Fragment.Length != 0)
            throw new ArgumentException(
                "The canonical trailing-slash option is only valid for a public directory-style URL without a query or file extension.");
        // Do not mutate host, scheme, query or path except adding one terminal slash.
        return PublicBrowserUrlPolicy.Parse(observedLinkUrl.AbsoluteUri + "/");
    }
}

public sealed class BrowserSessionSemanticLinkTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_follow_named_link",
        "Navigate in an isolated public Edge session to ONE uniquely named visible link. " +
        "Use the exact AccessibleName and URL from the observed Links list. " +
        "Rechecks accessible role/name, destination href and same-host origin. " +
        "Requires host approval displaying the exact URL. Never clicks the DOM link, " +
        "submits a form, opens tabs, or navigates after an interactive action. " +
        "If the target was found with browser.session_inspect_links, supply its " +
        "linkPageNumber so the host rechecks that exact bounded anchor page. " +
        "Set preferCanonicalTrailingSlash=true only for a directory-style href " +
        "when you want to approve direct navigation to its trailing-slash URL. " +
        "The approval text discloses both the observed href and actual navigation URL; " +
        "all other redirects remain blocked.",
        ToolRisk.ReadExternal, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "sessionId":{"type":"string","minLength":32,"maxLength":32},
          "expectedSourceUrl":{"type":"string","minLength":12,"maxLength":2048},
          "expectedLinkName":{"type":"string","minLength":1,"maxLength":150},
          "expectedLinkUrl":{"type":"string","minLength":12,"maxLength":2048},
          "linkPageNumber":{"type":"integer"},
          "preferCanonicalTrailingSlash":{"type":"boolean"}},
         "required":["sessionId","expectedSourceUrl","expectedLinkName","expectedLinkUrl"],
         "additionalProperties":false}
        """), HasSideEffects: true);

    private static (string Id, Uri Source, string Name, Uri Target, int? LinkPage,
        bool CanonicalSlash, Uri NavigationUrl) Parse(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        Uri source = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedSourceUrl").GetString() ?? "");
        Uri target = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedLinkUrl").GetString() ?? "");
        string name = args.GetProperty("expectedLinkName").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
            name.Length > 150 || name.Any(char.IsControl))
            throw new ArgumentException("Supply the exact nonblank accessible link name from a browser snapshot.");
        if (!string.Equals(source.Host, target.Host, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Named-link navigation must remain on the exact approved hostname.");
        int? linkPage = args.TryGetProperty("linkPageNumber", out JsonElement page)
            ? page.GetInt32() : null;
        if (linkPage.HasValue) _ = BrowserLinkPagination.OffsetForPage(linkPage.Value);
        bool canonical = args.TryGetProperty("preferCanonicalTrailingSlash", out JsonElement option) &&
            option.GetBoolean();
        Uri navigation = BrowserNamedLinkDestination.Resolve(target, canonical);
        return (id, source, name, target, linkPage, canonical, navigation);
    }

    public string GetTarget(JsonElement args)
    {
        var target = Parse(args);
        return "session " + target.Id + " from " + target.Source.AbsoluteUri +
            " [unique link role/name " + JsonSerializer.Serialize(target.Name) +
            "]" + (target.LinkPage.HasValue ? " [inspected link page " + target.LinkPage.Value + "]" : "") +
            " [observed href " + target.Target.AbsoluteUri + "]" +
            " -> approved navigation " + target.NavigationUrl.AbsoluteUri +
            " [all other redirects blocked]";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var target = Parse(args);
        try
        {
            BrowserSessionLinkObservation observed = await sessions.FollowByNameAsync(
                target.Id, target.Source, target.Name, target.Target, ct, target.LinkPage,
                target.CanonicalSlash);
            return ToolReceipt.Ok("Navigated approved uniquely named public link", observed,
                changedState: true);
        }
        catch (Exception error) when (error is PlaywrightException or IOException or
            TimeoutException or InvalidOperationException)
        { return ToolReceipt.Fail("BROWSER_NAMED_LINK_REFUSED", error.Message); }
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

/// <summary>Generic, exact and unique accessible-role/name targeting.</summary>
public static class BrowserSemanticControl
{
    public static string RoleFor(string tag, string type, string explicitRole)
    {
        if (!string.IsNullOrEmpty(explicitRole)) return explicitRole;
        return tag switch
        {
            "button" => "button",
            "textarea" => "textbox",
            "input" when type == "search" => "searchbox",
            "input" when type == "checkbox" => "checkbox",
            "input" when type == "radio" => "radio",
            "input" when type is "text" or "" => "textbox",
            "select" => "combobox",
            _ => ""
        };
    }

    public static BrowserElement RequireUnique(IEnumerable<BrowserElement> elements,
        string action, string role, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Semantic element name must be non-empty.");
        BrowserElement[] matches = elements.Where(e =>
            string.Equals(e.SemanticRole, role, StringComparison.Ordinal) &&
            string.Equals(e.AccessibleName, name, StringComparison.Ordinal) &&
            IsAllowed(action, role, e)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Expected exactly one enabled, supported {role} named '{name}', found {matches.Length}. " +
                "Re-inspect the page and choose an unambiguous control.");
        return matches[0];
    }

    public static bool IsAllowed(string action, string role, BrowserElement element) =>
        !element.Disabled && !string.IsNullOrWhiteSpace(element.AccessibleName) &&
        (action switch
        {
            "click" => role == "button" && element.Tag == "button" && element.Type == "button",
            "fill" => (role is "textbox" or "searchbox") &&
                ((element.Tag == "input" && element.Type is "text" or "search" or "") ||
                 (element.Tag == "textarea" && element.Type == "")),
            _ => false
        });
}

public sealed record BrowserSemanticActionObservation(string SessionId, string PageUrl,
    string Action, string Role, string AccessibleName, int? CharactersEntered,
    BrowserSnapshot Page, BrowserButtonStateEvidence? AccessibleState = null);

/// <summary>Uniquely match a visible native single-select by accessible identity and option label.</summary>
public static class BrowserSemanticSelect
{
    public static BrowserElement RequireUnique(IEnumerable<BrowserElement> elements,
        string accessibleName, string optionLabel)
    {
        if (string.IsNullOrWhiteSpace(accessibleName) || string.IsNullOrWhiteSpace(optionLabel))
            throw new ArgumentException("Supply the exact observed control name and option label.");
        BrowserElement[] found = elements.Where(e => e.Tag == "select" &&
            e.SemanticRole == "combobox" && !e.Disabled &&
            string.Equals(e.AccessibleName, accessibleName, StringComparison.Ordinal)).ToArray();
        if (found.Length != 1)
            throw new InvalidOperationException("A unique enabled named native dropdown was not observed. Reinspect before selecting.");
        IReadOnlyList<string>? optionLabels = found[0].OptionLabels;
        if (optionLabels is null ||
            optionLabels.Count(label => string.Equals(label, optionLabel, StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException("The selected option label is absent or ambiguous. Reinspect before selecting.");
        return found[0];
    }
}

/// <summary>Approval is per exact dropdown/option. Selecting may trigger site JavaScript.</summary>
public sealed class BrowserSessionSelectOptionTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_select_option",
        "Select ONE enabled option by its exact visible label in ONE uniquely named native single-select combobox. " +
        "First call browser.session_inspect and use Elements.SemanticRole=combobox, AccessibleName and OptionLabels. " +
        "The host requires Dangerous approval of the exact page, dropdown and option. " +
        "The browser rechecks name, tag and option uniqueness before dispatch and verifies selected text afterward. " +
        "No form submission, credentials, multi-select, navigation, arbitrary selectors or option values. " +
        "Network requests stay blocked after first interaction; browser is not an OS/network sandbox.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
           "sessionId":{"type":"string","minLength":32,"maxLength":32},
           "expectedPageUrl":{"type":"string","minLength":12,"maxLength":2048},
           "accessibleName":{"type":"string","minLength":1,"maxLength":150},
           "optionLabel":{"type":"string","minLength":1,"maxLength":100}},
          "required":["sessionId","expectedPageUrl","accessibleName","optionLabel"],
          "additionalProperties":false}
        """), HasSideEffects: true);

    private static (string Id, Uri Page, string Name, string Label) Parse(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        Uri page = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedPageUrl").GetString() ?? "");
        string name = args.GetProperty("accessibleName").GetString() ?? "";
        string label = args.GetProperty("optionLabel").GetString() ?? "";
        if (name.Length is < 1 or > 150 || label.Length is < 1 or > 100 ||
            name != name.Trim() || label != label.Trim() ||
            name.Any(char.IsControl) || label.Any(char.IsControl))
            throw new ArgumentException("Dropdown and option must use exact, non-empty, one-line observed labels.");
        return (id, page, name, label);
    }

    public string GetTarget(JsonElement args)
    {
        var target = Parse(args);
        return "session " + target.Id + " on " + target.Page.AbsoluteUri +
            " [native combobox " + JsonSerializer.Serialize(target.Name) +
            ", select option " + JsonSerializer.Serialize(target.Label) +
            "] WARNING: change events can run scripts; no form submission or navigation authorized.";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var target = Parse(args);
        try
        {
            BrowserSessionSelectObservation result = await sessions.SelectOptionByNameAsync(
                target.Id, target.Page, target.Name, target.Label, ct);
            return ToolReceipt.Ok("Selected the approved native dropdown option and verified its label", result,
                changedState: true);
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or TimeoutException or InvalidOperationException)
        {
            return ToolReceipt.Fail("BROWSER_SELECT_OPTION_REFUSED", ex.Message);
        }
        // Post-dispatch uncertainty is NOT swallowed. The kernel requires reconciliation.
    }
}

/// <summary>
/// Require one enabled, native input whose exact accessible role/name and
/// checked state were observed. Never fall back to the first matching element.
/// </summary>
public static class BrowserNativeCheckedControl
{
    public static bool IsSafeHtmlId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 80 || !AsciiLetter(id[0])) return false;
        return id.Skip(1).All(c => AsciiLetter(c) || (c >= '0' && c <= '9') ||
            c == '_' || c == '-' || c == ':' || c == '.');
    }

    private static bool AsciiLetter(char value) =>
        (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');

    public static BrowserElement RequireUnique(IEnumerable<BrowserElement> elements,
        string role, string accessibleName, string? expectedHtmlId = null)
    {
        if (role is not ("checkbox" or "radio") || string.IsNullOrWhiteSpace(accessibleName) ||
            (expectedHtmlId is not null && !IsSafeHtmlId(expectedHtmlId)))
            throw new ArgumentException("Use an observed native role, exact name and optional safe HTML ID.");
        BrowserElement[] matches = elements.Where(e => e.Tag == "input" && e.Type == role &&
            e.SemanticRole == role && !e.Disabled && e.NativeChecked.HasValue &&
            string.Equals(e.AccessibleName, accessibleName, StringComparison.Ordinal) &&
            (expectedHtmlId is null || string.Equals(e.HtmlId, expectedHtmlId, StringComparison.Ordinal))).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Expected exactly one enabled native control matching its observed role, name and HTML ID. " +
                "Reinspect or supply a unique observed HtmlId; do not guess or use a DOM index.");
        return matches[0];
    }
}

/// <summary>Set native checked state without form submission; approval is per action.</summary>
public sealed class BrowserSessionSetCheckedTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_set_checked",
        "Set an explicitly approved native checkbox to checked/unchecked or a native radio to checked. " +
        "Inspect Elements first for SemanticRole, AccessibleName, NativeChecked and HtmlId. " +
        "If the same role/name occurs more than once, supply expectedHtmlId from observed HtmlId; " +
        "never invent an ID or use a DOM index. Without an ID, duplicate names remain refused. " +
        "The host requires Dangerous approval for the exact page, role, name, optional ID and desired state. " +
        "The browser rechecks unique identity, type and current state, then verifies native checked state. " +
        "Already-satisfied state returns a read-only no-op receipt; radio uncheck is prohibited. " +
        "Never deliberately submits a form, uses hidden controls, enters credentials, or navigates. " +
        "Network requests stay blocked after an actual interaction. JavaScript may still cause effects; NOT sandboxed.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
           "sessionId":{"type":"string","minLength":32,"maxLength":32},
           "expectedPageUrl":{"type":"string","minLength":12,"maxLength":2048},
           "role":{"type":"string","enum":["checkbox","radio"]},
           "accessibleName":{"type":"string","minLength":1,"maxLength":150},
           "expectedHtmlId":{"type":"string","minLength":1,"maxLength":80},
           "checked":{"type":"boolean"}},
          "required":["sessionId","expectedPageUrl","role","accessibleName","checked"],
          "additionalProperties":false}
        """), HasSideEffects: true);

    private static (string Id, Uri Page, string Role, string Name, string? HtmlId, bool Checked) Parse(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        Uri page = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedPageUrl").GetString() ?? "");
        string role = args.GetProperty("role").GetString() ?? "";
        string name = args.GetProperty("accessibleName").GetString() ?? "";
        bool state = args.GetProperty("checked").GetBoolean();
        string? htmlId = args.TryGetProperty("expectedHtmlId", out JsonElement suppliedId)
            ? suppliedId.GetString() : null;
        if ((htmlId is not null && !BrowserNativeCheckedControl.IsSafeHtmlId(htmlId)) ||
            role is not ("checkbox" or "radio") || (role == "radio" && !state) ||
            name.Length is < 1 or > 150 || name != name.Trim() || name.Any(char.IsControl))
            throw new ArgumentException("Only named native checkbox states or checked radio states are supported.");
        return (id, page, role, name, htmlId, state);
    }

    public string GetTarget(JsonElement args)
    {
        var e = Parse(args);
        return "session " + e.Id + " on " + e.Page.AbsoluteUri + " [native " + e.Role + " " +
            JsonSerializer.Serialize(e.Name) +
            (e.HtmlId is null ? "" : ", HTML id " + JsonSerializer.Serialize(e.HtmlId)) +
            ", checked=" + e.Checked.ToString().ToLowerInvariant() +
            "] WARNING: change events may run scripts; no form submission/navigation approved.";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var e = Parse(args);
        try
        {
            BrowserSessionCheckedObservation observation = await sessions.SetCheckedByNameAsync(
                e.Id, e.Page, e.Role, e.Name, e.Checked, ct, e.HtmlId);
            return ToolReceipt.Ok(observation.StateChangeObserved
                    ? "Set approved native checked state and verified the result"
                    : "The native control was already in the requested state; no change dispatched",
                observation, changedState: observation.StateChangeObserved);
        }
        catch (Exception ex) when (ex is PlaywrightException or IOException or TimeoutException or InvalidOperationException)
        {
            return ToolReceipt.Fail("BROWSER_SET_CHECKED_REFUSED", ex.Message);
        }
        // Uncertain post-dispatch effects are escalated to the durable kernel.
    }
}

/// <summary>
/// One explicitly approved semantic operation. The host decides authorization,
/// while the Edge session independently re-resolves a unique native control.
/// </summary>
public sealed class BrowserSessionSemanticActionTool(IPublicBrowserSessions sessions) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new(
        "browser.session_semantic_action",
        "Operate ONE uniquely named, visible, native public-page control using its accessible role/name. " +
        "First inspect browser session Elements for SemanticRole and AccessibleName. " +
        "Action click supports only native type=button outside forms; action fill supports labeled " +
        "ordinary textbox/searchbox fields. Requires explicit Dangerous approval for EVERY action. " +
        "Exact approved page URL, no submit, download, credential, cross-origin network or hidden field. " +
        "After interaction, outbound network requests remain blocked. No network sandbox.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
           "sessionId":{"type":"string","minLength":32,"maxLength":32},
           "expectedPageUrl":{"type":"string","minLength":12,"maxLength":2048},
           "action":{"type":"string","enum":["click","fill"]},
           "role":{"type":"string","enum":["button","textbox","searchbox"]},
           "accessibleName":{"type":"string","minLength":1,"maxLength":150},
           "text":{"type":"string","maxLength":500}},
          "required":["sessionId","expectedPageUrl","action","role","accessibleName"],
          "additionalProperties":false}
        """), HasSideEffects: true);

    private sealed record Target(string Id, Uri Url, string Action, string Role, string Name, string? Text);

    private static Target Parse(JsonElement args)
    {
        string id = BrowserSessionArguments.Id(args);
        Uri url = PublicBrowserUrlPolicy.Parse(args.GetProperty("expectedPageUrl").GetString() ?? "");
        string action = args.GetProperty("action").GetString() ?? "";
        string role = args.GetProperty("role").GetString() ?? "";
        string name = args.GetProperty("accessibleName").GetString() ?? "";
        string? value = args.TryGetProperty("text", out var textArg) ? textArg.GetString() : null;
        if (name.Length is < 1 or > 150 || name != name.Trim() || name.Any(char.IsControl) ||
            (action == "click" && (role != "button" || value is not null)) ||
            (action == "fill" && (role is not ("textbox" or "searchbox") || value is null)) ||
            action is not ("click" or "fill"))
            throw new ArgumentException("Expected a unique accessible button name for click or textbox/searchbox name and text for fill.");
        if (value is not null && (value.Length > 500 || value.Any(ch =>
            ch == '\0' || ch == '\r' || ch == '\n' || ch == '\t' || char.IsControl(ch))))
            throw new ArgumentException("Only single-line non-secret text (up to 500 characters) can be entered.");
        return new Target(id, url, action, role, name, value);
    }

    public string GetTarget(JsonElement args)
    {
        Target t = Parse(args);
        return "session " + t.Id + " on " + t.Url.AbsoluteUri + " [" + t.Action +
            " role " + t.Role + " with exact accessible name " +
            JsonSerializer.Serialize(t.Name) + (t.Text is null ? "" :
            ", approved text " + JsonSerializer.Serialize(t.Text)) +
            "] WARNING: Website scripts can react; requires a new approval per action.";
    }

    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        Target t = Parse(args);
        try
        {
            BrowserSemanticActionObservation observed = await sessions.InteractByRoleAsync(
                t.Id, t.Url, t.Action, t.Role, t.Name, t.Text, ct);
            string result = t.Action == "click" && observed.AccessibleState?.VerifiedStateChange != true
                ? "Click dispatched, but accessibility-state change not verified"
                : "Semantic browser interaction executed and observed";
            return ToolReceipt.Ok(result, observed, changedState: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
        { return ToolReceipt.Fail("BROWSER_SEMANTIC_TARGET_REFUSED", ex.Message); }
        // BrowserInteractionUncertainException flows to the agent's Unknown barrier.
    }
}

/// <summary>
/// A bounded action sequence. An action enters the uncertain state before browser
/// dispatch and is unlocked ONLY after a verified observation. A successful action
/// does not authorize any later action; the host approval broker always decides.
/// </summary>
public sealed class BrowserInteractionSequence
{
    public const int MaximumVerifiedActions = 8;
    public int VerifiedActions { get; private set; }
    public bool RequiresReconciliation { get; private set; }

    public void EnsureReady()
    {
        if (RequiresReconciliation)
            throw new InvalidOperationException(
                "An earlier browser action may have taken effect without verification. " +
                "Inspect and close this session; do not send another action.");
        if (VerifiedActions >= MaximumVerifiedActions)
            throw new InvalidOperationException(
                "Browser interaction budget exhausted. Close and open a new session.");
    }

    public void Begin()
    {
        EnsureReady();
        RequiresReconciliation = true;
    }

    public void Verify()
    {
        if (!RequiresReconciliation)
            throw new InvalidOperationException("A browser interaction cannot be verified before dispatch.");
        VerifiedActions++;
        RequiresReconciliation = false;
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
    // This is a bounded interactive inspection session, not unconstrained browsing.
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, ActiveSession> _sessions = new(StringComparer.Ordinal);
    private bool _disposed;

    private sealed class NavigationGate(string permittedUrl)
    {
        public string PermittedUrl = permittedUrl;
        public int Requests;
        // This flag is intentionally PERMANENT for the lifetime of this session.
        // It blocks further network requests after the first interactive action.
        public volatile bool BlockNetworkAfterInteraction;
        public BrowserInteractionSequence Actions { get; } = new();
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

    public async Task<BrowserSessionControlPageObservation> InspectControlsAsync(string sessionId,
        int pageNumber, CancellationToken ct)
    {
        int offset = BrowserControlPagination.OffsetForPage(pageNumber);
        ActiveSession s = Find(sessionId);
        await s.Mutex.WaitAsync(ct);
        try
        {
            if (!string.Equals(s.Page.Url, s.Gate.PermittedUrl, StringComparison.Ordinal) ||
                s.Context.Pages.Count != 1)
                throw new InvalidOperationException("Page changed without an approved navigation.");
            int count = await s.Page.Locator(
                "button, input, select, textarea, [role='button']").CountAsync().WaitAsync(ct);
            BrowserSnapshot snapshot = await EdgePublicPageReader.SnapshotAsync(
                s.Page, new Uri(s.Gate.PermittedUrl), null, ct, offset);
            if (!string.Equals(s.Page.Url, s.Gate.PermittedUrl, StringComparison.Ordinal) ||
                s.Context.Pages.Count != 1)
                throw new InvalidOperationException("Page changed during control inspection.");
            s.LastActivity = DateTimeOffset.UtcNow;
            return new BrowserSessionControlPageObservation(sessionId, s.Page.Url,
                pageNumber, count, snapshot.Elements ?? []);
        }
        finally { s.Mutex.Release(); }
    }

    public async Task<BrowserSessionLinkPageObservation> InspectLinksAsync(
        string sessionId, int pageNumber, CancellationToken ct)
    {
        _ = BrowserLinkPagination.OffsetForPage(pageNumber);
        ActiveSession s = Find(sessionId);
        await s.Mutex.WaitAsync(ct);
        try
        {
            if (!string.Equals(s.Page.Url, s.Gate.PermittedUrl, StringComparison.Ordinal) ||
                s.Context.Pages.Count != 1)
                throw new InvalidOperationException("Page changed without an approved navigation.");
            var (total, links) = await EdgePublicPageReader.ReadLinkPageAsync(
                s.Page, pageNumber, ct);
            s.LastActivity = DateTimeOffset.UtcNow;
            return new BrowserSessionLinkPageObservation(sessionId, s.Page.Url,
                pageNumber, total, links);
        }
        finally { s.Mutex.Release(); }
    }

    public async Task<BrowserSessionVisibleTextPageObservation> InspectVisibleTextAsync(
        string sessionId, int pageNumber, CancellationToken ct)
    {
        _ = BrowserVisibleTextPagination.OffsetForPage(pageNumber);
        ActiveSession session = Find(sessionId);
        await session.Mutex.WaitAsync(ct);
        try
        {
            if (!string.Equals(session.Page.Url, session.Gate.PermittedUrl, StringComparison.Ordinal) ||
                session.Context.Pages.Count != 1)
                throw new InvalidOperationException("Page changed without an approved navigation.");
            // Same rendered-text API as the original 10,000-character snapshot;
            // read-only and does not expose <input> or <textarea> field values.
            string text = await session.Page.Locator("body").InnerTextAsync(
                new() { Timeout = 6000 }).WaitAsync(ct);
            string title = await session.Page.TitleAsync().WaitAsync(ct);
            if (!string.Equals(session.Page.Url, session.Gate.PermittedUrl, StringComparison.Ordinal) ||
                session.Context.Pages.Count != 1)
                throw new InvalidOperationException("Page changed during visible-text inspection.");
            session.LastActivity = DateTimeOffset.UtcNow;
            return BrowserVisibleTextPagination.Slice(sessionId, session.Page.Url,
                title, text, pageNumber);
        }
        finally { session.Mutex.Release(); }
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
    /// Navigate to the preapproved URL after checking a unique visible link's
    /// accessible name and href. This is GET navigation, not a DOM click.
    /// </summary>
    public async Task<BrowserSessionLinkObservation> FollowByNameAsync(
        string sessionId, Uri expectedSource, string expectedName,
        Uri expectedDestination, CancellationToken ct, int? inspectedLinkPageNumber = null,
        bool preferCanonicalTrailingSlash = false)
    {
        // Validate URL selection before obtaining the session mutex or dispatching traffic.
        Uri navigationUrl = BrowserNamedLinkDestination.Resolve(expectedDestination,
            preferCanonicalTrailingSlash);
        ActiveSession s = Find(sessionId);
        await s.Mutex.WaitAsync(ct);
        try
        {
            if (s.Gate.BlockNetworkAfterInteraction || s.Gate.Actions.RequiresReconciliation)
                throw new InvalidOperationException("Interactive or uncertain action prevents further navigation in this session.");
            if (!string.Equals(s.Page.Url, expectedSource.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(s.Gate.PermittedUrl, expectedSource.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(s.Host.Host, expectedSource.Host, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(s.Host.Host, expectedDestination.Host, StringComparison.OrdinalIgnoreCase) ||
                s.Context.Pages.Count != 1)
                throw new InvalidOperationException("Session is not at the exact authorized source URL, or opened another tab.");
            BrowserSnapshot snapshot = await EdgePublicPageReader.SnapshotAsync(
                s.Page, expectedSource, null, ct);
            IReadOnlyList<BrowserLink> candidateLinks = inspectedLinkPageNumber.HasValue
                ? (await EdgePublicPageReader.ReadLinkPageAsync(
                    s.Page, inspectedLinkPageNumber.Value, ct)).Links : snapshot.Links;
            BrowserLink link;
            if (inspectedLinkPageNumber.HasValue)
            {
                // ReadLinkPageAsync already selects only visible native a[href]
                // anchors, excluding aria-hidden/inert/overridden-role elements.
                // Requiring GetByRole(...).Count==1 globally incorrectly rejects
                // named links that recur in a separate sidebar or footer.
                link = BrowserSemanticLink.RequireExactInPage(
                    candidateLinks, expectedName, expectedDestination);
            }
            else
            {
                link = BrowserSemanticLink.RequireUnique(
                    candidateLinks, expectedName, expectedDestination);
                ILocator named = s.Page.GetByRole(AriaRole.Link, new() {
                    Name = expectedName, Exact = true
                });
                if (await named.CountAsync().WaitAsync(ct) != 1 ||
                    !await named.IsVisibleAsync().WaitAsync(ct))
                    throw new InvalidOperationException(
                        "Accessible link role/name is missing, hidden or ambiguous. Re-inspect.");
                string? href = await named.GetAttributeAsync("href").WaitAsync(ct);
                if (string.IsNullOrWhiteSpace(href) ||
                    !Uri.TryCreate(expectedSource, href, out Uri? destination) ||
                    !string.Equals(destination.AbsoluteUri, expectedDestination.AbsoluteUri,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The named link href changed after inspection; no navigation was performed.");
            }
            if (!string.Equals(s.Page.Url, expectedSource.AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Source page changed while checking the link; no navigation was performed.");
            PublicBrowserUrlPolicy.RequirePublicDns(
                await Dns.GetHostAddressesAsync(navigationUrl.DnsSafeHost, ct));

            // Request exactly the approved URL. When the opt-in is set, request
            // the trailing-slash variant directly, avoiding an unapproved 301/302.
            // All responses redirecting elsewhere remain forbidden by RouteAsync.
            s.Gate.PermittedUrl = navigationUrl.AbsoluteUri;
            IResponse? response;
            try
            {
                response = await s.Page.GotoAsync(navigationUrl.AbsoluteUri, new() {
                    WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000
                }).WaitAsync(ct);
                if (!string.Equals(s.Page.Url, navigationUrl.AbsoluteUri, StringComparison.Ordinal) ||
                    s.Context.Pages.Count != 1)
                    throw new InvalidOperationException("Approved browser navigation did not remain at its exact destination.");
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(
                    s.Page, navigationUrl, response?.Status, ct);
                s.LastActivity = DateTimeOffset.UtcNow;
                return new BrowserSessionLinkObservation(sessionId, snapshot.FinalUrl,
                    link.AccessibleName, expectedDestination.AbsoluteUri, after,
                    navigationUrl.AbsoluteUri);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException)
            {
                // The navigation might have reached its destination before the
                // failure. Do not label the effect "no change" or permit blind retry.
                s.Gate.Actions.Begin();
                throw new BrowserInteractionUncertainException(
                    "Browser navigation may have taken effect without verification. " +
                    "Close this session and reconcile before retrying.", ex);
            }
        }
        finally { s.Mutex.Release(); }
    }

    /// <summary>
    /// Shared per-action preparation. Called with the session mutex held. Each action
    /// must be independently host-approved by NiraEngine before getting here.
    /// Never unlock network access after interactions, even when a later action is
    /// approved. Do not dispatch stale, ambiguous or changed DOM targets.
    /// </summary>
    private static async Task<(BrowserElement Element, ILocator Locator)> PrepareInteractionAsync(
        ActiveSession session, Uri expectedUrl, int domIndex,
        Func<BrowserElement, bool> expectedControl, CancellationToken ct)
    {
        session.Gate.Actions.EnsureReady();
        if (!string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
            !string.Equals(session.Gate.PermittedUrl, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
            !string.Equals(session.Host.Host, expectedUrl.Host, StringComparison.OrdinalIgnoreCase) ||
            session.Context.Pages.Count != 1)
            throw new InvalidOperationException(
                "The browser is no longer at the exact approved page or opened another tab.");

        BrowserSnapshot before = await EdgePublicPageReader.SnapshotAsync(
            session.Page, expectedUrl, null, ct);
        BrowserElement? selected = before.Elements?.FirstOrDefault(e => e.DomIndex == domIndex);
        if (selected is null || selected.Disabled || !expectedControl(selected))
            throw new InvalidOperationException(
                "The observed control changed, disappeared or is not allowed. Inspect again before proposing another action.");
        ILocator locator = session.Page.Locator(
            "button, input, select, textarea, [role='button']").Nth(domIndex);
        if (!await locator.IsVisibleAsync().WaitAsync(ct) ||
            !await locator.IsEnabledAsync().WaitAsync(ct))
            throw new InvalidOperationException(
                "The approved control is no longer visible and enabled.");
        return (selected, locator);
    }

    // The uncertain flag is set BEFORE the browser operation. Only a fully
    // verified post-action snapshot permits another approved interaction.
    private static void BeginInteractiveDispatch(ActiveSession session)
    {
        session.Gate.BlockNetworkAfterInteraction = true;
        session.Gate.Actions.Begin();
    }

    private static void MarkVerifiedInteraction(ActiveSession session)
    {
        session.Gate.Actions.Verify();
        session.LastActivity = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Fill a verified, labeled text/search field. Entry blocks subsequent network
    /// access and navigation, but additional specifically approved same-page
    /// interactions remain possible after this action is fully verified. It is not a complete browser sandbox;
    /// sites may still react to DOM/input events or already-open connections.
    /// </summary>
    public async Task<BrowserSessionTextObservation> FillTextAsync(string sessionId, Uri expectedUrl,
        int domIndex, string expectedTag, string expectedLabel, string text, CancellationToken ct)
    {
        ActiveSession session = Find(sessionId);
        await session.Mutex.WaitAsync(ct);
        try
        {
            var (element, control) = await PrepareInteractionAsync(session, expectedUrl, domIndex,
                observed => string.Equals(observed.Tag, expectedTag, StringComparison.Ordinal) &&
                    string.Equals(observed.Label, expectedLabel, StringComparison.Ordinal) &&
                    BrowserSessionFillTextTool.IsAllowedControl(observed), ct);
            if (!await control.IsEditableAsync().WaitAsync(ct))
                throw new InvalidOperationException("The approved text field is no longer editable.");
            if (element.Tag == "input" &&
                !string.Equals(await control.GetAttributeAsync("type").WaitAsync(ct) ?? "",
                    element.Type, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Text input type changed after inspection.");

            BeginInteractiveDispatch(session);
            try
            {
                await control.FillAsync(text, new() { Timeout = 6000 }).WaitAsync(ct);
                string actual = await control.InputValueAsync().WaitAsync(ct);
                if (!string.Equals(actual, text, StringComparison.Ordinal) ||
                    !string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                    session.Context.Pages.Count != 1)
                    throw new InvalidOperationException("Entered text, URL or tab count could not be verified.");
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(
                    session.Page, expectedUrl, null, ct);
                MarkVerifiedInteraction(session);
                return new BrowserSessionTextObservation(sessionId, expectedUrl.AbsoluteUri,
                    domIndex, element.Tag, element.Label, text.Length, after);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException or OperationCanceledException)
            {
                // Sequence remains uncertain. Never silently repeat.
                throw new BrowserInteractionUncertainException(
                    "Text entry may have taken effect but was not fully verified. " +
                    "Close the session and reconcile before repeating it.", ex);
            }
        }
        finally { session.Mutex.Release(); }
    }

    /// <summary>
    /// Dispatch one independently approved click on a native type=button only.
    /// Network requests remain blocked; form ancestors, submit/reset buttons
    /// and any further interactions after an uncertain action are refused. Already-active scripts/connections can
    /// still cause effects; this is NOT a browser sandbox.
    /// </summary>
    public async Task<BrowserSessionButtonObservation> ClickButtonAsync(string sessionId,
        Uri expectedUrl, int domIndex, string expectedLabel, string expectedText, CancellationToken ct)
    {
        ActiveSession session = Find(sessionId);
        await session.Mutex.WaitAsync(ct);
        try
        {
            var (element, button) = await PrepareInteractionAsync(session, expectedUrl, domIndex,
                observed => string.Equals(observed.Label, expectedLabel, StringComparison.Ordinal) &&
                    string.Equals(observed.Text, expectedText, StringComparison.Ordinal) &&
                    BrowserSessionClickButtonTool.IsAllowedControl(observed), ct);
            if (!string.Equals(await button.GetAttributeAsync("type").WaitAsync(ct),
                    "button", StringComparison.OrdinalIgnoreCase) ||
                await button.EvaluateAsync<bool>("el => !!el.closest('form')").WaitAsync(ct))
                throw new InvalidOperationException(
                    "Button is no longer an explicitly typed native type=button outside forms.");

            BeginInteractiveDispatch(session);
            try
            {
                await button.ClickAsync(new() { Timeout = 6000 }).WaitAsync(ct);
                if (!string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                    session.Context.Pages.Count != 1)
                    throw new InvalidOperationException("Click navigated or opened another page.");
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(
                    session.Page, expectedUrl, null, ct);
                BrowserButtonStateEvidence accessible = BrowserButtonStateEvidence.Compare(element, after);
                // A click may succeed without a visible accessible state transition.
                // Preserve that uncertainty in the receipt rather than guessing.
                MarkVerifiedInteraction(session);
                return new BrowserSessionButtonObservation(
                    sessionId, expectedUrl.AbsoluteUri, domIndex, element.Label, element.Text, after,
                    accessible);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException or OperationCanceledException)
            {
                // The shared sequence stays locked after any uncertain click.
                throw new BrowserInteractionUncertainException(
                    "The button click may have happened but verification failed. " +
                    "Close the session and reconcile before repeating it.", ex);
            }
        }
        finally { session.Mutex.Release(); }
    }

    public async Task<BrowserSemanticActionObservation> InteractByRoleAsync(string sessionId,
        Uri expectedUrl, string action, string role, string accessibleName, string? text,
        CancellationToken ct)
    {
        ActiveSession session = Find(sessionId);
        await session.Mutex.WaitAsync(ct);
        try
        {
            session.Gate.Actions.EnsureReady();
            if (!string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(session.Gate.PermittedUrl, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(session.Host.Host, expectedUrl.Host, StringComparison.OrdinalIgnoreCase) ||
                session.Context.Pages.Count != 1)
                throw new InvalidOperationException("Session is no longer at the exact approved public page.");
            BrowserSnapshot before = await EdgePublicPageReader.SnapshotAsync(
                session.Page, expectedUrl, null, ct);
            BrowserElement observed = BrowserSemanticControl.RequireUnique(
                before.Elements ?? [], action, role, accessibleName);
            AriaRole nativeRole = role switch
            {
                "button" => AriaRole.Button,
                "searchbox" => AriaRole.Searchbox,
                "textbox" => AriaRole.Textbox,
                _ => throw new ArgumentException("Unsupported accessible role")
            };
            // Playwright uses the actual accessibility tree, not the order of DOM nodes.
            // A duplicate or newly changed target is a hard refusal, never .First().
            ILocator target = session.Page.GetByRole(nativeRole,
                new() { Name = accessibleName, Exact = true });
            if (await target.CountAsync().WaitAsync(ct) != 1 ||
                !await target.IsVisibleAsync().WaitAsync(ct) ||
                !await target.IsEnabledAsync().WaitAsync(ct))
                throw new InvalidOperationException("The accessible target is missing, hidden or ambiguous.");
            string realTag = await target.EvaluateAsync<string>(
                "el => el.tagName.toLowerCase()").WaitAsync(ct);
            string realType = (await target.GetAttributeAsync("type").WaitAsync(ct) ?? "").ToLowerInvariant();
            if (!string.Equals(realTag, observed.Tag, StringComparison.Ordinal) ||
                !string.Equals(realType, observed.Type, StringComparison.Ordinal))
                throw new InvalidOperationException("Accessible target changed tag or input type since inspection.");
            if (action == "click" &&
                (realTag != "button" || realType != "button" ||
                 await target.EvaluateAsync<bool>("el => !!el.closest('form')").WaitAsync(ct)))
                throw new InvalidOperationException("Only native type=button controls outside forms are allowed.");
            if (action == "fill" && !await target.IsEditableAsync().WaitAsync(ct))
                throw new InvalidOperationException("The named input is no longer editable.");

            BeginInteractiveDispatch(session);
            try
            {
                if (action == "click")
                    await target.ClickAsync(new() { Timeout = 6000 }).WaitAsync(ct);
                else
                {
                    await target.FillAsync(text!, new() { Timeout = 6000 }).WaitAsync(ct);
                    string observedText = await target.InputValueAsync().WaitAsync(ct);
                    if (!string.Equals(observedText, text, StringComparison.Ordinal))
                        throw new InvalidOperationException("Entered text could not be verified.");
                }
                if (!string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                    session.Context.Pages.Count != 1)
                    throw new InvalidOperationException("An unapproved page or tab navigation was detected.");
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(
                    session.Page, expectedUrl, null, ct);
                BrowserButtonStateEvidence? state = null;
                if (action == "click")
                {
                    BrowserElement[] matchingAfter = (after.Elements ?? []).Where(e =>
                        e.SemanticRole == observed.SemanticRole &&
                        e.AccessibleName == observed.AccessibleName).ToArray();
                    if (matchingAfter.Length == 1)
                        state = BrowserButtonStateEvidence.Compare(
                            observed with { DomIndex = matchingAfter[0].DomIndex }, after);
                    else
                        state = BrowserButtonStateEvidence.Compare(observed,
                            after with { Elements = Array.Empty<BrowserElement>() });
                }
                MarkVerifiedInteraction(session);
                return new BrowserSemanticActionObservation(sessionId, expectedUrl.AbsoluteUri,
                    action, role, accessibleName, action == "fill" ? text!.Length : null,
                    after, state);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException or OperationCanceledException)
            {
                throw new BrowserInteractionUncertainException(
                    "The semantic browser action may have occurred without complete verification. " +
                    "Close and reconcile before retrying.", ex);
            }
        }
        finally { session.Mutex.Release(); }
    }

    public async Task<BrowserSessionSelectObservation> SelectOptionByNameAsync(string sessionId,
        Uri expectedUrl, string accessibleName, string optionLabel, CancellationToken ct)
    {
        ActiveSession session = Find(sessionId);
        await session.Mutex.WaitAsync(ct);
        try
        {
            session.Gate.Actions.EnsureReady();
            if (!string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(session.Gate.PermittedUrl, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(session.Host.Host, expectedUrl.Host, StringComparison.OrdinalIgnoreCase) ||
                session.Context.Pages.Count != 1)
                throw new InvalidOperationException("Session is not on the exact approved page.");
            BrowserSnapshot before = await EdgePublicPageReader.SnapshotAsync(session.Page, expectedUrl, null, ct);
            _ = BrowserSemanticSelect.RequireUnique(before.Elements ?? [], accessibleName, optionLabel);
            ILocator target = session.Page.GetByRole(AriaRole.Combobox,
                new() { Name = accessibleName, Exact = true });
            if (await target.CountAsync().WaitAsync(ct) != 1 ||
                !await target.IsVisibleAsync().WaitAsync(ct) ||
                !await target.IsEnabledAsync().WaitAsync(ct))
                throw new InvalidOperationException("Native dropdown is absent, disabled or ambiguous.");
            if (!string.Equals(await target.EvaluateAsync<string>(
                    "el => el.tagName.toLowerCase()").WaitAsync(ct), "select", StringComparison.Ordinal) ||
                await target.EvaluateAsync<bool>("el => el.multiple || el.size > 1").WaitAsync(ct))
                throw new InvalidOperationException("Only native single-select comboboxes are permitted.");
            // Re-verify the exact label uniquely exists and is enabled immediately before dispatch.
            int optionMatches = await target.EvaluateAsync<int>("""
                (el, label) => {
                    const matches = Array.from(el.options).filter(x =>
                        (x.label || x.textContent || '').replace(/\s+/g, ' ').trim() === label);
                    return matches.length === 1 && !matches[0].disabled &&
                      !matches[0].closest('optgroup[disabled]') ? 1 : 0;
                }
                """, optionLabel).WaitAsync(ct);
            if (optionMatches != 1)
                throw new InvalidOperationException("The requested native option is no longer unique and enabled.");

            BeginInteractiveDispatch(session);
            try
            {
                await target.SelectOptionAsync(new SelectOptionValue { Label = optionLabel },
                    new() { Timeout = 6000 }).WaitAsync(ct);
                string selected = await target.EvaluateAsync<string>("""
                    el => Array.from(el.selectedOptions).map(x =>
                      (x.label || x.textContent || '').replace(/\s+/g, ' ').trim()).join('')
                    """).WaitAsync(ct);
                if (!string.Equals(selected, optionLabel, StringComparison.Ordinal) ||
                    !string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                    session.Context.Pages.Count != 1)
                    throw new InvalidOperationException("The selected option or approved page could not be verified.");
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(
                    session.Page, expectedUrl, null, ct);
                // The dropdown itself must still have the same unique accessible identity.
                _ = BrowserSemanticSelect.RequireUnique(after.Elements ?? [], accessibleName, optionLabel);
                MarkVerifiedInteraction(session);
                return new BrowserSessionSelectObservation(sessionId, expectedUrl.AbsoluteUri,
                    accessibleName, optionLabel, true, after);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException or OperationCanceledException)
            {
                throw new BrowserInteractionUncertainException(
                    "Dropdown selection may have happened but was not fully verified. " +
                    "Stop, close the session, and reconcile before repeating it.", ex);
            }
        }
        finally { session.Mutex.Release(); }
    }

    public async Task<BrowserSessionCheckedObservation> SetCheckedByNameAsync(string sessionId,
        Uri expectedUrl, string role, string accessibleName, bool isChecked, CancellationToken ct,
        string? expectedHtmlId = null)
    {
        ActiveSession session = Find(sessionId);
        await session.Mutex.WaitAsync(ct);
        try
        {
            session.Gate.Actions.EnsureReady();
            if (role is not ("checkbox" or "radio") || (role == "radio" && !isChecked) ||
                (expectedHtmlId is not null && !BrowserNativeCheckedControl.IsSafeHtmlId(expectedHtmlId)))
                throw new InvalidOperationException("Invalid native control role, state, or HTML ID.");
            if (!string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(session.Gate.PermittedUrl, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                !string.Equals(session.Host.Host, expectedUrl.Host, StringComparison.OrdinalIgnoreCase) ||
                session.Context.Pages.Count != 1)
                throw new InvalidOperationException("Session is no longer on the exact approved page.");
            // Exact accessible role/name is checked against the WHOLE live page,
            // not the truncated first-24-controls snapshot.
            ILocator byRole = session.Page.GetByRole(role == "checkbox" ? AriaRole.Checkbox : AriaRole.Radio,
                new() { Name = accessibleName, Exact = true });
            int candidates = await byRole.CountAsync().WaitAsync(ct);
            if (candidates < 1 || candidates > 96 || (expectedHtmlId is null && candidates != 1))
                throw new InvalidOperationException("The checked control is missing or ambiguously named.");
            ILocator? target = null;
            if (expectedHtmlId is null)
            {
                target = byRole;
            }
            else
            {
                // The ID is constrained to a safe ASCII grammar, not an arbitrary CSS selector.
                // A duplicate ID anywhere among native inputs is rejected, even if another input
                // with that ID has a different accessible name.
                if (await session.Page.Locator($"input[id=\"{expectedHtmlId}\"]")
                    .CountAsync().WaitAsync(ct) != 1)
                    throw new InvalidOperationException("HTML control ID is not unique on this page.");
                for (int i = 0; i < candidates; i++)
                {
                    ILocator current = byRole.Nth(i);
                    if (string.Equals(await current.GetAttributeAsync("id").WaitAsync(ct),
                        expectedHtmlId, StringComparison.Ordinal))
                    {
                        if (target is not null)
                            throw new InvalidOperationException("More than one named control shares that HTML ID.");
                        target = current;
                    }
                }
            }
            if (target is null ||
                !await target.IsVisibleAsync().WaitAsync(ct) ||
                !await target.IsEnabledAsync().WaitAsync(ct) ||
                !await target.EvaluateAsync<bool>("""
                    (el, expectedType) => el.tagName.toLowerCase() === 'input' &&
                        el.type.toLowerCase() === expectedType && !el.disabled
                    """, role).WaitAsync(ct))
                throw new InvalidOperationException("The approved native checked control is stale or ambiguous.");
            // Locate the approved control's actual position in the ordered control
            // collection. This lets the ordinary 24-entry snapshot verify radios
            // and checkboxes farther down long forms without reading any input value.
            int controlIndex = await target.EvaluateAsync<int>("""
                el => Array.from(document.querySelectorAll(
                    'button, input, select, textarea, [role="button"]')).indexOf(el)
                """).WaitAsync(ct);
            int controlPage = BrowserControlPagination.PageForIndex(controlIndex);
            BrowserSnapshot before = await EdgePublicPageReader.SnapshotAsync(
                session.Page, expectedUrl, null, ct,
                BrowserControlPagination.OffsetForPage(controlPage));
            BrowserElement observed = BrowserNativeCheckedControl.RequireUnique(
                before.Elements ?? [], role, accessibleName, expectedHtmlId);
            if (observed.DomIndex != controlIndex)
                throw new InvalidOperationException("Target control index changed during verification.");
            bool beforeChecked = await target.IsCheckedAsync().WaitAsync(ct);
            if (beforeChecked != observed.NativeChecked!.Value)
                throw new InvalidOperationException("Checked state changed since inspection. Reinspect before approval.");
            if (beforeChecked == isChecked)
            {
                // No click, no network lock, no action budget consumed.
                return new BrowserSessionCheckedObservation(sessionId, expectedUrl.AbsoluteUri,
                    role, accessibleName, isChecked, beforeChecked, beforeChecked, false,
                    before, observed.HtmlId);
            }
            BeginInteractiveDispatch(session);
            try
            {
                if (isChecked)
                    await target.CheckAsync(new() { Timeout = 6000 }).WaitAsync(ct);
                else
                    await target.UncheckAsync(new() { Timeout = 6000 }).WaitAsync(ct);
                bool checkedAfter = await target.IsCheckedAsync().WaitAsync(ct);
                if (checkedAfter != isChecked ||
                    !string.Equals(session.Page.Url, expectedUrl.AbsoluteUri, StringComparison.Ordinal) ||
                    session.Context.Pages.Count != 1)
                    throw new InvalidOperationException("The requested native checked state/page was not verified.");
                int afterIndex = await target.EvaluateAsync<int>("""
                    el => Array.from(document.querySelectorAll(
                        'button, input, select, textarea, [role="button"]')).indexOf(el)
                    """).WaitAsync(ct);
                BrowserSnapshot after = await EdgePublicPageReader.SnapshotAsync(
                    session.Page, expectedUrl, null, ct,
                    BrowserControlPagination.OffsetForPage(
                        BrowserControlPagination.PageForIndex(afterIndex)));
                BrowserElement current = BrowserNativeCheckedControl.RequireUnique(
                    after.Elements ?? [], role, accessibleName, expectedHtmlId);
                if (current.NativeChecked != isChecked)
                    throw new InvalidOperationException("Post-action snapshot disagreed with the checked control.");
                MarkVerifiedInteraction(session);
                return new BrowserSessionCheckedObservation(sessionId, expectedUrl.AbsoluteUri,
                    role, accessibleName, isChecked, beforeChecked, checkedAfter, true,
                    after, observed.HtmlId);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or
                InvalidOperationException or OperationCanceledException)
            {
                throw new BrowserInteractionUncertainException(
                    "Native checked state may have changed without complete verification. " +
                    "Stop, close the session and reconcile before retrying.", ex);
            }
        }
        finally { session.Mutex.Release(); }
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

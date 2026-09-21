/*
 * filename: NIRABrowserService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Playwright;
using NIRAAgent.Authorization;
using NIRAAgent.Goals;

namespace NIRAAgent.Browser;

// =============================================================
// NIRA-MANAGED BROWSER RUNTIME
//
// This is intentionally a separate browser profile from the user's normal
// Chrome/Edge profile. Playwright itself recommends a dedicated automation
// profile, and this also gives NIRA a durable authenticated session without
// taking ownership of the user's ordinary browser state.
//
// The service never exposes cookies, local-storage tokens, password values,
// or other raw authentication material to cognition. Authenticated API calls
// use BrowserContext.APIRequest so session cookies stay inside Playwright.
// =============================================================

public sealed class NIRABrowserService : IAsyncDisposable
{
    private const int DefaultViewportWidth = 1440;
    private const int DefaultViewportHeight = 900;
    private const int MaximumInspectionTextCharacters = 16000;
    private const int MaximumInteractiveElements = 180;
    private const int MaximumElementTextCharacters = 220;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateSync = new();
    private readonly Dictionary<Guid, IPage> _pages = new();
    private readonly Dictionary<Guid, PageOwnership> _ownership = new();
    private readonly Dictionary<string, Guid> _activeByOwner = new(StringComparer.Ordinal);
    private readonly NIRAAuthorityExecutionContextAccessor _authority;
    private readonly NIRAGoalService _goals;

    // A page's owner comes only from the runtime execution context or from
    // its Playwright opener. Browser/page text cannot select an owner.
    private sealed record PageOwnership(
        string OwnerKey,
        Guid? GoalId,
        Guid? BranchId,
        Guid? OpenerPageId,
        bool Recovered,
        Guid? CreatedRunId);

    private readonly Dictionary<IPage, Guid> _pageIds = new();
    private readonly Dictionary<Guid, string> _pageOrigins = new();
    private readonly Dictionary<Guid, HashSet<string>> _latestElementRefs = new();
    private readonly Dictionary<Guid, Dictionary<string, GroundedElementState>> _latestGroundedElements = new();
    // These are per-session, per-page facts; never persisted as auth state.
    private readonly Dictionary<Guid, NIRABrowserNavigationEvidence> _navigation = new();
    private readonly HashSet<Guid> _crashedPages = new();
    private readonly Dictionary<Guid, string> _uncertainActions = new();
    // Durable write-ahead dispatch receipts, separate from auth and recovery.
    private readonly NIRABrowserActionJournal _actionJournal;
    private readonly Dictionary<Guid, NIRABrowserActionCheckpoint> _latestActions = new();
    // Never persisted or shared across NIRA browser sessions. A checkpoint
    // belongs to its exact PageId and thus to its authoritative owner.
    private readonly Dictionary<Guid, NIRABrowserRecoveryCheckpoint> _recoveries = new();
    // Bound retries across tabs belonging to the same goal and origin.
    private readonly Dictionary<string, Guid> _credentialAttemptRunsByScope =
        new(StringComparer.OrdinalIgnoreCase);
    // Auth attempts are scoped to the actual task+origin and exact live page.
    // Only a dispatched submit followed by an observed website rejection can
    // authorize one secure replacement. A failed capability is NOT a failed login.
    private sealed record CredentialSubmission
    {
        public Guid PageId { get; init; }
        public DateTimeOffset SubmittedAtUtc { get; init; }
        public DateTimeOffset? LastInspectedAtUtc { get; init; }
        public string LastInspectedUrl { get; init; } = string.Empty;
        public string State { get; init; } = "SubmittedAwaitingObservation";
        public bool WebsiteRejectionObserved { get; init; }
    }
    private readonly Dictionary<string, CredentialSubmission> _credentialSubmissions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, DateTimeOffset> _lastPageInspection = new();
    private readonly HashSet<string> _trustedRefreshUsed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _queryBearingNavigation = new();

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private Guid _sessionId;
    private string _profile = "default";
    private bool _headed;
    private bool _disposed;

    private readonly string _browserRoot;
    private readonly string _profilesRoot;
    private readonly string _artifactsRoot;
    private readonly string _downloadsRoot;

    private sealed record GroundedElementState
    {
        public string Ref { get; init; } = string.Empty;
        public string RawHref { get; init; } = string.Empty;
        public string ObservedPageUrl { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string Tag { get; init; } = string.Empty;
        public string InputType { get; init; } = string.Empty;
        public bool SensitiveEntry { get; init; }
        public bool Disabled { get; init; }
    }

    public NIRABrowserService(
        NIRAAuthorityExecutionContextAccessor authority,
        NIRAGoalService goals)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
        string local =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(local))
        {
            local =
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".NIRA");
        }

        _browserRoot =
            Path.Combine(local, "NIRAAgent", "browser");

        _profilesRoot =
            Path.Combine(_browserRoot, "profiles");

        _artifactsRoot =
            Path.Combine(_browserRoot, "artifacts");

        _downloadsRoot =
            Path.Combine(_browserRoot, "downloads");

        Directory.CreateDirectory(_profilesRoot);
        Directory.CreateDirectory(_artifactsRoot);
        Directory.CreateDirectory(_downloadsRoot);
        _actionJournal = new NIRABrowserActionJournal(
            Path.Combine(_browserRoot, "dispatch", "browser-dispatch.db"));
    }

    public async Task<NIRABrowserSessionSnapshot> OpenAsync(
        string? profile,
        bool headed,
        string? initialUrl,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string normalizedProfile = NormalizeProfile(profile);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_context?.IsClosed == true)
            {
                await CloseCoreAsync();
            }

            if (_context != null &&
                !string.Equals(_profile, normalizedProfile, StringComparison.OrdinalIgnoreCase))
            {
                EnsureSessionOwnedForClose();
                await CloseCoreAsync();
            }

            if (_context == null)
            {
                _playwright ??=
                    await Playwright.CreateAsync();

                string userDataDir =
                    Path.Combine(_profilesRoot, normalizedProfile);

                Directory.CreateDirectory(userDataDir);

                try
                {
                    _context =
                        await _playwright.Chromium.LaunchPersistentContextAsync(
                            userDataDir,
                            new BrowserTypeLaunchPersistentContextOptions
                            {
                                Headless = !headed,
                                AcceptDownloads = true,
                                DownloadsPath = _downloadsRoot,
                                ArtifactsDir = _artifactsRoot,
                                ViewportSize = new ViewportSize
                                {
                                    Width = DefaultViewportWidth,
                                    Height = DefaultViewportHeight
                                }
                            });
                }
                catch (PlaywrightException ex) when (
                    ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("browserType.launchPersistentContext", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Playwright Chromium is not installed for this NIRA build. Build the project, then run the generated playwright.ps1 install chromium command once.",
                        ex);
                }

                _sessionId = Guid.NewGuid();
                _profile = normalizedProfile;
                _headed = headed;
                lock (_stateSync)
                {
                    _pages.Clear();
                    _ownership.Clear();
                    _activeByOwner.Clear();
                    _pageIds.Clear();
                    _pageOrigins.Clear();
                    _latestElementRefs.Clear();
                    _latestGroundedElements.Clear();
                    _navigation.Clear();
                    _crashedPages.Clear();
                    _uncertainActions.Clear();
                    _latestActions.Clear();
                    _recoveries.Clear();
                    _credentialAttemptRunsByScope.Clear();
                    _credentialSubmissions.Clear();
                    _lastPageInspection.Clear();
                    _trustedRefreshUsed.Clear();
                    _queryBearingNavigation.Clear();
                }

                // A new tab is NEVER globally activated by an event. Popup
                // ownership is resolved from its Playwright opener in refresh.
                _context.Page += (_, page) => RegisterPage(page, recovered: false);

                // Chromium may restore some/all tabs from the persistent
                // profile. Old PageIds are never reused across sessions.
                foreach (IPage page in _context.Pages)
                    RegisterPage(page, recovered: true);

                Debug.WriteLine(
                    $"[Browser] OPEN | Session={_sessionId:D} | Profile='{_profile}' | Headed={_headed}");
            }

            if (!string.IsNullOrWhiteSpace(initialUrl))
            {
                IPage page = FindSoleOwnedPage() ?? await CreateOwnedPageAsync();
                string requestedUrl = ParseHttpUri(initialUrl).AbsoluteUri;
                // Repeating browser.session.open with the same initial URL is
                // not a reason to discard a live session or reload its page.
                bool navigationRequired = !string.Equals(
                    page.Url, requestedUrl, StringComparison.OrdinalIgnoreCase);
                if (navigationRequired)
                    await NavigateCoreAsync(page, requestedUrl, timeoutSeconds, cancellationToken);
                Guid navigatedId = EnsurePageId(page);
                // A repeated session.open for an unchanged page is an
                // observation, not a document transition. Invalidating its
                // inspected element refs made the next grounded follow fail.
                if (navigationRequired)
                    ClearElementRefs(navigatedId);
                SetActivePage(navigatedId);
                UpdatePageOrigin(navigatedId, page.Url);
            }

            return await SnapshotCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserSessionSnapshot> CloseAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await RefreshPagesAsync();
            EnsureSessionOwnedForClose();
            await CloseCoreAsync();
            return new NIRABrowserSessionSnapshot
            {
                SessionId = Guid.Empty,
                Profile = _profile,
                Headed = _headed,
                IsOpen = false
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserSessionSnapshot> CurrentAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // An unopened browser is an ordinary observable state, not a
            // fatal inspect/recovery error. It must never be mistaken for an
            // authenticated session or used to borrow a foreign page.
            if (_context == null || _context.IsClosed)
                return new NIRABrowserSessionSnapshot
                {
                    IsOpen = false,
                    Profile = _profile,
                    Headed = _headed,
                    RecoveryNotice = "No live managed browser session. Use browser.accounts for saved account metadata, then browser.session.open (headless) with a grounded route before browser.inspect. Do not ask the user for internal PageIds.",
                    PendingActionReviews = _actionJournal.ReadUncertain(CurrentDispatchOwnerKey())
                };
            return await SnapshotCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserPageSnapshot> NavigateAsync(
        Guid? pageId,
        string url,
        bool newPage,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Uri uri = ParseHttpUri(url);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();

            IPage page;
            if (newPage)
            {
                page = await CreateOwnedPageAsync();
            }
            else
            {
                // First navigation in a fresh/reopened profile gets its OWN tab.
                // Never take over an unclaimed restored or foreign task tab.
                page = pageId == null && !HasOwnedPages()
                    ? await CreateOwnedPageAsync()
                    : ResolvePage(pageId);
            }

            await NavigateCoreAsync(
                page,
                uri.AbsoluteUri,
                timeoutSeconds,
                cancellationToken);

            Guid id = EnsurePageId(page);
            ClearElementRefs(id);
            SetActivePage(id);
            UpdatePageOrigin(id, page.Url);

            return await BuildPageSnapshotAsync(id, page);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserPageSnapshot> FollowAsync(
        Guid pageId,
        string elementRef,
        bool newPage,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string cleanRef = NormalizeElementRef(elementRef);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage sourcePage = ResolvePage(pageId);
            Guid sourceId = EnsurePageId(sourcePage);
            GroundedElementState grounded = ResolveGroundedElement(sourceId, cleanRef);
            ILocator sourceLocator = await ValidateGroundedActionTargetAsync(sourceId, sourcePage, cleanRef);

            if (grounded.Disabled)
            {
                throw new InvalidOperationException(
                    "The grounded browser element is disabled. Re-inspect the page before choosing another target.");
            }

            if (string.IsNullOrWhiteSpace(grounded.RawHref))
            {
                throw new InvalidOperationException(
                    "browser.follow requires a grounded link with an HTTP/HTTPS href from the latest browser.inspect result. Use browser.click only when the site genuinely requires an interactive click rather than ordinary read-only link navigation.");
            }

            string? currentRawHref = await sourceLocator.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(currentRawHref))
            {
                throw new InvalidOperationException(
                    "The grounded link no longer exposes its inspected href. Re-inspect the current page before following it.");
            }

            Uri currentHref =
                Uri.TryCreate(currentRawHref, UriKind.Absolute, out Uri? absoluteHref)
                    ? ParseHttpUri(absoluteHref.AbsoluteUri)
                    : ParseHttpUri(new Uri(new Uri(sourcePage.Url), currentRawHref).AbsoluteUri);

            Uri destination = ParseHttpUri(grounded.RawHref);
            if (!string.Equals(currentHref.AbsoluteUri, destination.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The grounded link target changed after the latest browser.inspect result. Re-inspect the page before following it.");
            }

            IPage targetPage;

            if (newPage)
            {
                targetPage = await CreateOwnedPageAsync();
            }
            else
            {
                targetPage = sourcePage;
                ClearElementRefs(sourceId);
            }

            await NavigateCoreAsync(
                targetPage,
                destination.AbsoluteUri,
                timeoutSeconds,
                cancellationToken);

            Guid targetId = EnsurePageId(targetPage);
            ClearElementRefs(targetId);
            SetActivePage(targetId);
            UpdatePageOrigin(targetId, targetPage.Url);

            Debug.WriteLine(
                $"[Browser] FOLLOW | Session={_sessionId:D} | SourcePage={sourceId:D} | Ref='{cleanRef}' | TargetPage={targetId:D} | Url='{TrimLog(targetPage.Url)}'");

            return await BuildPageSnapshotAsync(targetId, targetPage);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserInspection> InspectAsync(
        Guid? pageId,
        int maxElements,
        int maxTextCharacters,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        maxElements = Math.Clamp(maxElements, 1, MaximumInteractiveElements);
        maxTextCharacters = Math.Clamp(maxTextCharacters, 1000, MaximumInspectionTextCharacters);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);
            SetActivePage(id);
            UpdatePageOrigin(id, page.Url);

            string token =
                $"s{id:N}"[..10] + "-" + Guid.NewGuid().ToString("N")[..8];

            JsonElement raw =
                await page.EvaluateAsync<JsonElement>(
                    InspectionScript,
                    new
                    {
                        token,
                        maxElements,
                        maxTextCharacters,
                        maxElementTextCharacters = MaximumElementTextCharacters
                    });

            string bodyText = ReadString(raw, "text");
            string textSource = ReadString(raw, "textSource");
            string canonicalUrl = RedactUrlForCognition(ReadString(raw, "canonicalUrl"));
            string siteName = ReadString(raw, "siteName");
            string language = ReadString(raw, "language");
            string publishedAt = ReadString(raw, "publishedAt");
            string modifiedAt = ReadString(raw, "modifiedAt");
            List<string> structuredDataTypes = ReadStringArray(raw, "structuredDataTypes", 24, 120);

            List<NIRABrowserInteractiveElement> elements = [];
            Dictionary<string, GroundedElementState> grounded = new(StringComparer.Ordinal);

            if (raw.TryGetProperty("elements", out JsonElement array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in array.EnumerateArray())
                {
                    List<string> options = [];
                    if (item.TryGetProperty("options", out JsonElement optionArray) &&
                        optionArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement option in optionArray.EnumerateArray().Take(50))
                        {
                            if (option.ValueKind == JsonValueKind.String)
                            {
                                string? value = option.GetString();
                                if (!string.IsNullOrWhiteSpace(value))
                                    options.Add(value.Trim());
                            }
                        }
                    }

                    string elementRef = ReadString(item, "ref");
                    string rawHref = ReadString(item, "href");
                    string role = ReadString(item, "role");
                    string tag = ReadString(item, "tag");
                    string inputType = ReadString(item, "inputType");
                    bool sensitive = ReadBoolean(item, "sensitiveEntry");
                    bool disabled = ReadBoolean(item, "disabled");

                    NIRABrowserInteractiveElement publicElement =
                        new()
                        {
                            Ref = elementRef,
                            Role = role,
                            Name = ReadString(item, "name"),
                            Tag = tag,
                            InputType = inputType,
                            Placeholder = ReadString(item, "placeholder"),
                            Href = RedactUrlForCognition(rawHref),
                            ValuePreview = ReadString(item, "valuePreview"),
                            SensitiveEntry = sensitive,
                            Disabled = disabled,
                            Checked = ReadBoolean(item, "checked"),
                            Options = options
                        };

                    elements.Add(publicElement);

                    if (!string.IsNullOrWhiteSpace(elementRef))
                    {
                        grounded[elementRef] =
                            new GroundedElementState
                            {
                                Ref = elementRef,
                                RawHref = rawHref,
                                ObservedPageUrl = page.Url,
                                Role = role,
                                Tag = tag,
                                InputType = inputType,
                                SensitiveEntry = sensitive,
                                Disabled = disabled
                            };
                    }
                }
            }

            // DOM structure is parsed as external evidence, without model-owned
            // selectors, values from secret inputs or unbounded table dumps.
            List<NIRABrowserFormSnapshot> forms = [];
            if (raw.TryGetProperty("forms", out JsonElement rawForms) &&
                rawForms.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement rawForm in rawForms.EnumerateArray().Take(6))
                {
                    List<NIRABrowserFormField> fields = [];
                    if (rawForm.TryGetProperty("fields", out JsonElement rawFields) &&
                        rawFields.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement field in rawFields.EnumerateArray().Take(16))
                        {
                            fields.Add(new NIRABrowserFormField
                            {
                                ElementRef = ReadString(field, "ref"),
                                Label = ReadString(field, "label"),
                                Name = ReadString(field, "name"),
                                Kind = ReadString(field, "kind"),
                                Required = ReadBoolean(field, "required"),
                                Disabled = ReadBoolean(field, "disabled"),
                                SensitiveEntry = ReadBoolean(field, "sensitiveEntry"),
                                Options = ReadStringArray(field, "options", 12, 100)
                            });
                        }
                    }
                    forms.Add(new NIRABrowserFormSnapshot
                    {
                        Name = ReadString(rawForm, "name"),
                        Method = ReadString(rawForm, "method"),
                        Action = RedactUrlForCognition(ReadString(rawForm, "action")),
                        Fields = fields,
                        Truncated = ReadBoolean(rawForm, "truncated")
                    });
                }
            }
            List<NIRABrowserTableSnapshot> tables = [];
            if (raw.TryGetProperty("tables", out JsonElement rawTables) &&
                rawTables.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement rawTable in rawTables.EnumerateArray().Take(6))
                {
                    List<IReadOnlyList<string>> rows = [];
                    if (rawTable.TryGetProperty("rows", out JsonElement rawRows) &&
                        rawRows.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement row in rawRows.EnumerateArray().Take(25))
                        {
                            if (row.ValueKind != JsonValueKind.Array) continue;
                            rows.Add(row.EnumerateArray().Take(10)
                                .Where(cell => cell.ValueKind == JsonValueKind.String)
                                .Select(cell => cell.GetString() ?? string.Empty).ToArray());
                        }
                    }
                    tables.Add(new NIRABrowserTableSnapshot
                    {
                        Caption = ReadString(rawTable, "caption"),
                        Headers = rawTable.TryGetProperty("headers", out JsonElement rawHeaders) &&
                            rawHeaders.ValueKind == JsonValueKind.Array
                            ? rawHeaders.EnumerateArray().Take(10)
                                .Where(cell => cell.ValueKind == JsonValueKind.String)
                                .Select(cell => cell.GetString() ?? string.Empty).ToArray()
                            : Array.Empty<string>(),
                        Rows = rows,
                        Truncated = ReadBoolean(rawTable, "truncated")
                    });
                }
            }

            SetLatestElements(id, grounded);

            string title = await SafeTitleAsync(page);
            string visibleUrl = RedactUrlForCognition(page.Url);
            string contentHash = ComputeSha256Hex(
                string.Join(
                    "\n",
                    visibleUrl,
                    canonicalUrl,
                    title,
                    siteName,
                    language,
                    textSource,
                    publishedAt,
                    modifiedAt,
                    string.Join("|", structuredDataTypes),
                    bodyText));

            NIRABrowserInspection inspection =
                new()
                {
                    SessionId = _sessionId,
                    PageId = id,
                    InspectionId = Guid.NewGuid(),
                    Url = visibleUrl,
                    CanonicalUrl = canonicalUrl,
                    Title = title,
                    SiteName = siteName,
                    Language = language,
                    TextSource = string.IsNullOrWhiteSpace(textSource) ? "body" : textSource,
                    PublishedAt = publishedAt,
                    ModifiedAt = modifiedAt,
                    StructuredDataTypes = structuredDataTypes,
                    ContentSha256 = contentHash,
                    Text = bodyText,
                    Elements = elements,
                    Forms = forms,
                    Tables = tables,
                    StructuredContentTruncated = ReadBoolean(raw, "structuredContentTruncated"),
                    Navigation = NavigationForPage(id),
                    Recovery = RecoveryForPage(id),
                    PasswordControlObserved = elements.Any(e =>
                        e.SensitiveEntry && string.Equals(e.InputType, "password", StringComparison.OrdinalIgnoreCase)) ||
                        forms.Any(f => f.Fields.Any(field =>
                            field.SensitiveEntry && string.Equals(field.Kind, "password", StringComparison.OrdinalIgnoreCase))),
                    ObservedAtUtc = DateTimeOffset.UtcNow
                };

            // A redirect plus a sensitive password control is an authentication
            // recovery opportunity, NOT a conclusion that a session expired.
            // The checkpoint preserves the original route while cognition uses
            // the site evidence to decide if secure authentication is required.
            ObserveRecoveryOpportunity(inspection);
            inspection = inspection with { Recovery = RecoveryForPage(id) };
            inspection = inspection with
            {
                AuthenticationAttemptState = ObserveCredentialOutcome(inspection)
            };
            inspection = StampPostActionInspection(id, inspection);
            lock (_stateSync) _lastPageInspection[id] = inspection.ObservedAtUtc;

            Debug.WriteLine(
                $"[Browser] INSPECT | Session={_sessionId:D} | Page={id:D} | Inspection={inspection.InspectionId:D} | Elements={elements.Count} | TextSource={inspection.TextSource} | Url='{TrimLog(page.Url)}'");

            return inspection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserPageSnapshot> ClickAsync(
        Guid? pageId,
        string elementRef,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string cleanRef = NormalizeElementRef(elementRef);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);
            EnsureNoUncertainAction(id, page);
            ILocator locator = await ValidateGroundedActionTargetAsync(id, page, cleanRef);
            string beforeUrl = page.Url;
            HashSet<Guid> beforePageIds;
            lock (_stateSync) beforePageIds = _pages.Keys.ToHashSet();

            cancellationToken.ThrowIfCancellationRequested();
            // Transactionally recorded BEFORE dispatch: a process crash leaves
            // an in-flight receipt, never a reason to repeat the click.
            NIRABrowserActionCheckpoint action = BeginDispatch(id, page, "click", cleanRef);
            try
            {
                await locator.ClickAsync(
                    new LocatorClickOptions { Timeout = timeoutSeconds * 1000 });
            }
            catch (Exception ex) when (ex is (PlaywrightException or OperationCanceledException))
            {
                FinishDispatch(action, "OutcomeUncertain", page);
                ClearElementRefs(id);
                if (ex is OperationCanceledException) throw;
                throw new InvalidOperationException(
                    $"BrowserActionOutcomeUncertain ActionId={action.ActionId:D}: the click might already have reached the site. Inspect first; automatic retry is blocked for this task and origin.", ex);
            }
            FinishDispatch(action, "MechanicallyReturned", page);
            cancellationToken.ThrowIfCancellationRequested();
            ClearElementRefs(id);
            await RefreshPagesAsync();
            if (!page.IsClosed)
            {
                UpdatePageOrigin(id, page.Url);
                SetActivePage(id);
            }

            Debug.WriteLine(
                $"[Browser] CLICK | Session={_sessionId:D} | Page={id:D} | Ref='{cleanRef}'");

            Guid[] popups;
            lock (_stateSync)
                popups = _ownership
                    .Where(pair => !beforePageIds.Contains(pair.Key) &&
                        pair.Value.OpenerPageId == id &&
                        _pages.TryGetValue(pair.Key, out IPage? candidate) && !candidate.IsClosed)
                    .Select(pair => pair.Key).ToArray();

            return await ActionSnapshotAsync(id, page, "click", cleanRef,
                beforeUrl, "Playwright click returned; external/site result NOT verified. Inspect each popup and the source page before claiming completion.", popups);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserPageSnapshot> FillAsync(
        Guid? pageId,
        string elementRef,
        string value,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string cleanRef = NormalizeElementRef(elementRef);

        if (value.Length > 16000)
        {
            throw new InvalidOperationException(
                "Browser fill text is too large.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);
            EnsureNoUncertainAction(id, page);
            GroundedElementState grounded = ResolveGroundedElement(id, cleanRef);
            ILocator locator = await ValidateGroundedActionTargetAsync(id, page, cleanRef);
            string beforeUrl = page.Url;

            if (grounded.Disabled)
            {
                throw new InvalidOperationException(
                    "The grounded browser field is disabled. Re-inspect the page before choosing another target.");
            }

            string inputType =
                (await locator.GetAttributeAsync("type") ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();

            string autocomplete =
                (await locator.GetAttributeAsync("autocomplete") ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();

            string semanticHints =
                string.Join(
                    " ",
                    await locator.GetAttributeAsync("id") ?? string.Empty,
                    await locator.GetAttributeAsync("name") ?? string.Empty,
                    await locator.GetAttributeAsync("placeholder") ?? string.Empty,
                    await locator.GetAttributeAsync("aria-label") ?? string.Empty);

            if (grounded.SensitiveEntry || IsSensitiveEntry(inputType, autocomplete, semanticHints))
            {
                throw new InvalidOperationException(
                    "NIRA does not place passwords, one-time codes, PINs, payment secrets, API keys/tokens, or other credential-like values from model-visible browser.fill arguments into web pages. Use browser.authenticate for approved website login credentials; MFA/OTP or unsupported secret flows require a trusted/human step.");
            }

            await locator.FillAsync(
                value,
                new LocatorFillOptions
                {
                    Timeout = timeoutSeconds * 1000
                });

            cancellationToken.ThrowIfCancellationRequested();
            string localVerification;
            if (!page.IsClosed && string.Equals(page.Url, beforeUrl, StringComparison.Ordinal))
            {
                string observed = await locator.EvaluateAsync<string>(
                    "el => 'value' in el ? String(el.value) : String(el.textContent || '')");
                if (!string.Equals(observed, value, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The fill was dispatched but the field value differed on re-observation. Inspect the current state before retrying; never repeat blindly.");
                localVerification = "The same DOM field contains the requested non-secret text; saved/server state NOT verified.";
            }
            else
            {
                localVerification = "The page navigated/closed after fill; inspect the resulting state before retrying or claiming completion.";
            }
            SetActivePage(id);
            UpdatePageOrigin(id, page.Url);

            Debug.WriteLine(
                $"[Browser] FILL | Session={_sessionId:D} | Page={id:D} | Ref='{cleanRef}' | Chars={value.Length}");

            return await ActionSnapshotAsync(id, page, "fill", cleanRef,
                beforeUrl, localVerification);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserPageSnapshot> FillCredentialAsync(
        Guid pageId,
        string? usernameRef,
        string? passwordRef,
        string username,
        string secret,
        string? submitRef,
        string expectedOrigin,
        int timeoutSeconds,
        CancellationToken cancellationToken = default,
        bool freshTrustedCredential = false)
    {
        ThrowIfDisposed();

        string? cleanUsernameRef =
            string.IsNullOrWhiteSpace(usernameRef)
                ? null
                : NormalizeElementRef(usernameRef);

        string? cleanPasswordRef =
            string.IsNullOrWhiteSpace(passwordRef)
                ? null
                : NormalizeElementRef(passwordRef);

        string? cleanSubmitRef =
            string.IsNullOrWhiteSpace(submitRef)
                ? null
                : NormalizeElementRef(submitRef);

        if (cleanUsernameRef == null && cleanPasswordRef == null)
        {
            throw new InvalidOperationException(
                "Secure browser authentication requires a username field, password field, or both.");
        }

        if (username.Length > 4096 || secret.Length > 16384)
        {
            throw new InvalidOperationException(
                "Secure browser credential material is unexpectedly large.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);

            if (id != pageId)
            {
                throw new InvalidOperationException(
                    "The requested browser page is no longer authoritative.");
            }

            // The secure UI can remain open while the page navigates. Validate
            // the exact authorized origin AFTER the credential is resolved and
            // BEFORE any username or secret is dispatched to the DOM.
            AssertCurrentOrigin(page, expectedOrigin);
            EnsureNoUncertainAction(id, page);
            if (freshTrustedCredential)
            {
                EnsureTrustedCredentialRefreshAllowed(id, expectedOrigin);
                // One refresh per task+origin; no repeat with stale credentials.
                lock (_stateSync) _trustedRefreshUsed.Add(CredentialScopeKey(expectedOrigin));
            }
            else
                EnsureCredentialRetryAllowed(id, expectedOrigin);
            if (cleanUsernameRef != null)
            {
                GroundedElementState groundedUsername =
                    ResolveGroundedElement(id, cleanUsernameRef);

                if (groundedUsername.Disabled)
                {
                    throw new InvalidOperationException(
                        "The grounded username/account field is disabled. Re-inspect the page.");
                }

                ILocator usernameLocator =
                    await ValidateGroundedActionTargetAsync(id, page, cleanUsernameRef);

                MarkCredentialInteraction(id, expectedOrigin, includesSubmit: false);
                await usernameLocator.FillAsync(
                    username,
                    new LocatorFillOptions
                    {
                        Timeout = timeoutSeconds * 1000
                    });
            }

            if (cleanPasswordRef != null)
            {
                GroundedElementState groundedPassword =
                    ResolveGroundedElement(id, cleanPasswordRef);

                if (groundedPassword.Disabled)
                {
                    throw new InvalidOperationException(
                        "The grounded credential field is disabled. Re-inspect the page.");
                }

                ILocator passwordLocator =
                    await ValidateGroundedActionTargetAsync(id, page, cleanPasswordRef);

                string inputType =
                    (await passwordLocator.GetAttributeAsync("type") ?? string.Empty)
                        .Trim()
                        .ToLowerInvariant();

                string autocomplete =
                    (await passwordLocator.GetAttributeAsync("autocomplete") ?? string.Empty)
                        .Trim()
                        .ToLowerInvariant();

                string semanticHints =
                    string.Join(
                        " ",
                        await passwordLocator.GetAttributeAsync("id") ?? string.Empty,
                        await passwordLocator.GetAttributeAsync("name") ?? string.Empty,
                        await passwordLocator.GetAttributeAsync("placeholder") ?? string.Empty,
                        await passwordLocator.GetAttributeAsync("aria-label") ?? string.Empty);

                if (!groundedPassword.SensitiveEntry &&
                    !IsSensitiveEntry(inputType, autocomplete, semanticHints))
                {
                    throw new InvalidOperationException(
                        "The requested password/secret target is not grounded as a sensitive credential field. NIRA will not place a stored secret into an ordinary text field.");
                }

                MarkCredentialInteraction(id, expectedOrigin, includesSubmit: false);
                await passwordLocator.FillAsync(
                    secret,
                    new LocatorFillOptions
                    {
                        Timeout = timeoutSeconds * 1000
                    });
            }

            if (cleanSubmitRef != null)
            {
                GroundedElementState groundedSubmit =
                    ResolveGroundedElement(id, cleanSubmitRef);

                if (groundedSubmit.Disabled)
                {
                    throw new InvalidOperationException(
                        "The grounded sign-in/continue control is disabled. Re-inspect the page.");
                }

                ILocator submitLocator =
                    await ValidateGroundedActionTargetAsync(id, page, cleanSubmitRef);

                // Record the potential submission BEFORE dispatch: if Playwright
                // throws after the click was sent, another automatic click must
                // never silently duplicate the login attempt.
                MarkCredentialInteraction(id, expectedOrigin, includesSubmit: true);
                lock (_stateSync)
                    _credentialSubmissions[CredentialScopeKey(expectedOrigin)] =
                        new CredentialSubmission
                        {
                            PageId = id,
                            SubmittedAtUtc = DateTimeOffset.UtcNow
                        };
                string beforeSubmitUrl = page.Url;
                await submitLocator.ClickAsync(
                    new LocatorClickOptions
                    {
                        Timeout = timeoutSeconds * 1000
                    });

                // A real site may redirect asynchronously AFTER Playwright's
                // click resolves. The immediately observed old login DOM is not
                // an authentication failure (or permission to resubmit).
                // Wait inside the SAME credential capability, not in another
                // expensive model round. Neither navigation nor a missing form
                // is by itself proof that the account is authenticated.
                await AwaitCredentialPageTransitionAsync(
                    page, beforeSubmitUrl, timeoutSeconds, cancellationToken);

                ClearElementRefs(id);
                await RefreshPagesAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            SetActivePage(id);
            UpdatePageOrigin(id, page.Url);

            Debug.WriteLine(
                $"[Browser] SECURE AUTH FILL | Session={_sessionId:D} | Page={id:D} | " +
                $"UsernameField={cleanUsernameRef != null} | SecretField={cleanPasswordRef != null} | Submit={cleanSubmitRef != null}");

            return await BuildPageSnapshotAsync(id, page);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Site-independent bounded post-submit observation. It never replays a
    // credential, follows links, or assumes a particular portal URL.
    private static async Task AwaitCredentialPageTransitionAsync(
        IPage page, string originalUrl, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        int waitMilliseconds = Math.Min(12000,
            Math.Max(1500, timeoutSeconds * 1000));
        Stopwatch clock = Stopwatch.StartNew();
        bool transitioned = false;
        while (clock.ElapsedMilliseconds < waitMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (page.IsClosed) break;
            try
            {
                transitioned = await page.EvaluateAsync<bool>(
                    "original => location.href !== original || " +
                    "document.querySelector('input[type=password]') === null",
                    originalUrl);
                if (transitioned) break;
            }
            catch (PlaywrightException)
            {
                // The document can be replaced mid-observation. A thrown
                // evaluation does not mean the login failed.
            }
            await Task.Delay(200, cancellationToken);
        }
        Debug.WriteLine(
            $"[Browser] AUTH POST-SUBMIT | TransitionObserved={transitioned} | " +
            $"WaitMs={clock.ElapsedMilliseconds} | " +
            $"SameRoute={string.Equals(page.Url, originalUrl, StringComparison.OrdinalIgnoreCase)}");
    }

    // Preflight before showing a trusted credential prompt, so a retry loop
    // does not repeatedly ask for the same secret. Checked again after the
    // prompt inside FillCredentialAsync to cover page/origin changes.
    public string? TryGetObservedPageRoute(Guid pageId, string expectedOrigin)
    {
        lock (_stateSync)
        {
            if (!_pages.TryGetValue(pageId, out IPage? page) || page.IsClosed ||
                !Uri.TryCreate(page.Url, UriKind.Absolute, out Uri? uri) ||
                !string.Equals(uri.GetLeftPart(UriPartial.Authority), expectedOrigin,
                    StringComparison.OrdinalIgnoreCase))
                return null;
            return NavigationUrlForCognition(page.Url);
        }
    }

    public async Task EnsureAuthenticationMayProceedAsync(
        Guid pageId, string expectedOrigin, CancellationToken cancellationToken = default,
        bool forceReplacement = false,
        string? usernameRef = null, string? passwordRef = null, string? submitRef = null)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            AssertCurrentOrigin(page, expectedOrigin);
            EnsureNoUncertainAction(pageId, page);
            if (forceReplacement)
                EnsureTrustedCredentialRefreshAllowed(pageId, expectedOrigin);
            else
                EnsureCredentialRetryAllowed(pageId, expectedOrigin);

            // Do this before opening a credential prompt. Reject missing or
            // stale refs and non-secret password targets without soliciting or
            // retrieving the user's password.
            if (string.IsNullOrWhiteSpace(usernameRef) &&
                string.IsNullOrWhiteSpace(passwordRef))
                throw new InvalidOperationException(
                    "Login has no grounded username or password field. Inspect the current login document and use its exact refs.");
            foreach (string? elementRef in new[] { usernameRef, passwordRef, submitRef })
            {
                if (string.IsNullOrWhiteSpace(elementRef)) continue;
                GroundedElementState target = ResolveGroundedElement(
                    pageId, NormalizeElementRef(elementRef));
                if (target.Disabled)
                    throw new InvalidOperationException(
                        "A login control is disabled; inspect the current page before requesting credentials.");
                await ValidateGroundedActionTargetAsync(
                    pageId, page, NormalizeElementRef(elementRef));
                if (elementRef == passwordRef &&
                    !target.SensitiveEntry &&
                    !string.Equals(target.InputType, "password", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Password target is not a grounded sensitive input. No credential was retrieved or entered.");
            }
        }
        finally { _gate.Release(); }
    }

    // This re-opens ONLY a previously observed, queryless HTTP GET document
    // route on the same task-owned tab. It does not replay a form submission,
    // click, upload or URL containing an opaque token/query parameter.
    // The caller must inspect the result to determine whether access works.
    public async Task<NIRABrowserPageSnapshot> ResumeInterruptedDocumentAsync(
        Guid pageId, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            NIRABrowserRecoveryCheckpoint checkpoint;
            lock (_stateSync)
            {
                if (!_recoveries.TryGetValue(pageId, out NIRABrowserRecoveryCheckpoint? candidate) || candidate == null)
                    throw new InvalidOperationException(
                        "No observed, task-owned authentication recovery checkpoint exists for this page. Inspect the actual page first.");
                checkpoint = candidate;
                if (!checkpoint.CredentialSubmitAttempted)
                    throw new InvalidOperationException(
                        "A credential submission has not been observed in this checkpoint. Inspect the login page and authenticate through the approved broker if the task requires it.");
                if (!checkpoint.PostCredentialInspectionObserved)
                    throw new InvalidOperationException(
                        "Inspect the page after credential submission before revisiting the interrupted document. A submitted login is not proof of successful authentication.");
                if (checkpoint.State == "OriginalDocumentRevisited")
                    throw new InvalidOperationException(
                        "The interrupted route was already revisited. Inspect its current state; do not repeat a possibly consequential GET blindly.");
                if (_uncertainActions.ContainsKey(pageId))
                    throw new InvalidOperationException(
                        "The previous browser action has an uncertain outcome. Inspect and reconcile it before another navigation.");
            }
            Uri route = ParseHttpUri(checkpoint.InterruptedRoute);
            if (!string.IsNullOrEmpty(route.Query) || !string.IsNullOrEmpty(route.Fragment) ||
                !string.IsNullOrEmpty(route.UserInfo))
                throw new InvalidOperationException("Recovery cannot replay a token-bearing URL.");
            // Mark BEFORE dispatch; a timeout does not make it safe to repeat.
            lock (_stateSync)
                _recoveries[pageId] = checkpoint with { State = "OriginalDocumentRevisited", ObservedAtUtc = DateTimeOffset.UtcNow };
            await NavigateCoreAsync(page, route.AbsoluteUri, timeoutSeconds, cancellationToken);
            ClearElementRefs(pageId);
            SetActivePage(pageId);
            UpdatePageOrigin(pageId, page.Url);
            return await BuildPageSnapshotAsync(pageId, page);
        }
        finally { _gate.Release(); }
    }

    private void ObserveRecoveryOpportunity(NIRABrowserInspection inspection)
    {
        lock (_stateSync)
        {
            if (_recoveries.TryGetValue(inspection.PageId, out NIRABrowserRecoveryCheckpoint? existing) &&
                existing.CredentialSubmitAttempted &&
                !existing.PostCredentialInspectionObserved &&
                inspection.ObservedAtUtc >= existing.ObservedAtUtc)
            {
                _recoveries[inspection.PageId] = existing with
                {
                    PostCredentialInspectionObserved = true,
                    State = "PostCredentialPageInspected",
                    ObservedAtUtc = inspection.ObservedAtUtc
                };
            }
        }
        NIRABrowserNavigationEvidence? navigation = inspection.Navigation;
        if (navigation == null || !inspection.PasswordControlObserved ||
            string.IsNullOrWhiteSpace(navigation.RequestedUrl) ||
            string.IsNullOrWhiteSpace(navigation.FinalUrl) ||
            string.Equals(navigation.RequestedUrl, navigation.FinalUrl, StringComparison.OrdinalIgnoreCase))
            return;
        lock (_stateSync)
            if (_queryBearingNavigation.Contains(inspection.PageId)) return;
        if (!Uri.TryCreate(navigation.RequestedUrl, UriKind.Absolute, out Uri? route) ||
            route.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(route.Query) || !string.IsNullOrEmpty(route.Fragment))
            return;
        lock (_stateSync)
        {
            // Never replace a checkpoint in progress merely because a later
            // navigation or inspection resembles a login form again.
            _recoveries.TryAdd(inspection.PageId, new NIRABrowserRecoveryCheckpoint
            {
                PageId = inspection.PageId,
                InterruptedRoute = navigation.RequestedUrl,
                ObservedPageUrl = navigation.FinalUrl,
                State = "LoginFormObservedAfterNavigation",
                ObservedAtUtc = inspection.ObservedAtUtc
            });
        }
    }

    private NIRABrowserRecoveryCheckpoint? RecoveryForPage(Guid id)
    {
        lock (_stateSync)
            return _recoveries.TryGetValue(id, out NIRABrowserRecoveryCheckpoint? result) ? result : null;
    }

    private static void AssertCurrentOrigin(IPage page, string expectedOrigin)
    {
        if (page.IsClosed || !Uri.TryCreate(page.Url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.Equals(uri.GetLeftPart(UriPartial.Authority), expectedOrigin,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The page's live origin no longer matches the credential's authorized website origin. No credential material was entered.");
    }

    private string CredentialScopeKey(string origin)
    {
        NIRAAuthorityExecutionContext context = _authority.Current;
        string owner = context.GoalId is Guid goal && goal != Guid.Empty
            ? $"goal:{goal:D}"
            : context.RunId != Guid.Empty ? $"run:{context.RunId:D}" : $"session:{_sessionId:D}";
        return owner + "|" + origin;
    }

    // This state is deliberately mechanics/evidence only. Navigating away from
    // a login page does NOT prove authentication. Merely seeing the login form
    // again does NOT prove a bad password.
    private string ObserveCredentialOutcome(NIRABrowserInspection inspection)
    {
        lock (_stateSync)
        {
            if (!_pages.TryGetValue(inspection.PageId, out IPage? livePage) ||
                livePage.IsClosed ||
                !Uri.TryCreate(livePage.Url, UriKind.Absolute, out Uri? liveUri))
                return "NoSubmissionRecorded";
            string key = CredentialScopeKey(
                liveUri.GetLeftPart(UriPartial.Authority));
            if (!_credentialSubmissions.TryGetValue(key, out CredentialSubmission? prior) ||
                prior.PageId != inspection.PageId)
                return "NoSubmissionRecorded";
            if (inspection.ObservedAtUtc < prior.SubmittedAtUtc)
                return prior.State;

            bool rejection = inspection.PasswordControlObserved &&
                inspection.Navigation?.OutcomeUncertain != true &&
                ExplicitLoginRejectionObserved(inspection.Text);
            string state = rejection
                ? "WebsiteRejectedCredentials"
                : inspection.PasswordControlObserved
                    ? "LoginFormPresentOutcomeUnverified"
                    : "LoginFormAbsentOutcomeUnverified";
            _credentialSubmissions[key] = prior with
            {
                LastInspectedAtUtc = inspection.ObservedAtUtc,
                LastInspectedUrl = inspection.Url,
                WebsiteRejectionObserved = rejection,
                State = state
            };
            return state;
        }
    }

    private static bool ExplicitLoginRejectionObserved(string visibleText) =>
        !string.IsNullOrWhiteSpace(visibleText) &&
        Regex.IsMatch(visibleText,
            @"(?ix)\b(?:invalid|incorrect|wrong|expired|unrecognized|unrecognised)\s+(?:user(?:name)?|email|account|password|credentials|login|sign[ -]?in)\b|\b(?:login|log[ -]?in|sign[ -]?in|authentication)\s+(?:failed|unsuccessful|denied|rejected)\b|\b(?:password|credentials)\s+(?:is|are|was|were)\s+(?:invalid|incorrect|wrong|expired)\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // Return only the stage, never credentials, for a precise failure envelope.
    public string CredentialSubmissionState(Guid pageId, string origin)
    {
        lock (_stateSync)
            return _credentialSubmissions.TryGetValue(CredentialScopeKey(origin),
                out CredentialSubmission? attempt) && attempt.PageId == pageId
                ? attempt.State : "NoSubmissionRecorded";
    }

    private void EnsureTrustedCredentialRefreshAllowed(Guid pageId, string origin)
    {
        lock (_stateSync)
        {
            string key = CredentialScopeKey(origin);
            if (_trustedRefreshUsed.Contains(key))
                throw new InvalidOperationException(
                    "One secure credential replacement has already been used for this task and origin. Do not prompt or submit again.");
            if (!_credentialSubmissions.TryGetValue(key, out CredentialSubmission? submission) ||
                submission.PageId != pageId)
                throw new InvalidOperationException(
                    "No login submission was recorded on this task-owned page. The failed capability is NOT evidence of incorrect credentials. Correct the original problem; do not request a credential refresh.");
            if (!submission.WebsiteRejectionObserved ||
                submission.LastInspectedAtUtc is not DateTimeOffset inspected ||
                inspected < submission.SubmittedAtUtc ||
                DateTimeOffset.UtcNow - inspected > TimeSpan.FromMinutes(10) ||
                !_pages.TryGetValue(pageId, out IPage? livePage) || livePage.IsClosed ||
                !string.Equals(RedactUrlForCognition(livePage.Url),
                    submission.LastInspectedUrl, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The site has not shown a fresh, explicit rejection of submitted credentials on this same page. Inspect the actual login result and fix that blocker; do not automatically refresh or resubmit a password.");
        }
    }

    private void EnsureCredentialRetryAllowed(Guid pageId, string origin)
    {
        lock (_stateSync)
        {
            NIRAAuthorityExecutionContext context = _authority.Current;
            string key = CredentialScopeKey(origin);
            if (!_credentialAttemptRunsByScope.TryGetValue(key, out Guid previousRun)) return;
            // A new direct user request can explicitly allow another attempt;
            // a new tab, background wake or model proposal cannot bypass this.
            if (!context.UserInitiated || context.RunId == Guid.Empty || context.RunId == previousRun)
                throw new InvalidOperationException(
                    "Website authentication has already been attempted for this task and origin. Inspect the result; another automatic credential submission is blocked until a fresh explicit user request.");
        }
    }

    private void MarkCredentialInteraction(Guid pageId, string origin, bool includesSubmit)
    {
        lock (_stateSync)
        {
            NIRAAuthorityExecutionContext context = _authority.Current;
            string key = CredentialScopeKey(origin);
            bool isNewAttempt = includesSubmit &&
                (!_credentialAttemptRunsByScope.TryGetValue(key, out Guid previousRun) ||
                 previousRun != context.RunId);
            // Filling a username/password is not a website login attempt.
            // Failed pre-submit fills must not poison the retry budget.
            if (includesSubmit)
                _credentialAttemptRunsByScope[key] = context.RunId;
            if (_recoveries.TryGetValue(pageId, out NIRABrowserRecoveryCheckpoint? checkpoint))
            {
                _recoveries[pageId] = checkpoint with
                {
                    CredentialInteractions = checkpoint.CredentialInteractions + (isNewAttempt ? 1 : 0),
                    CredentialSubmitAttempted = checkpoint.CredentialSubmitAttempted || includesSubmit,
                    State = includesSubmit ? "CredentialSubmissionAttempted" : "CredentialFieldsEntered",
                    ObservedAtUtc = DateTimeOffset.UtcNow
                };
            }
        }
    }

    public async Task<NIRABrowserPageSnapshot> SelectAsync(
        Guid? pageId,
        string elementRef,
        string option,
        bool byLabel,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string cleanRef = NormalizeElementRef(elementRef);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);
            EnsureNoUncertainAction(id, page);
            ILocator locator = await ValidateGroundedActionTargetAsync(id, page, cleanRef);
            string beforeUrl = page.Url;

            if (byLabel)
            {
                await locator.SelectOptionAsync(
                    new SelectOptionValue { Label = option },
                    new LocatorSelectOptionOptions
                    {
                        Timeout = timeoutSeconds * 1000
                    });
            }
            else
            {
                await locator.SelectOptionAsync(
                    new SelectOptionValue { Value = option },
                    new LocatorSelectOptionOptions
                    {
                        Timeout = timeoutSeconds * 1000
                    });
            }

            cancellationToken.ThrowIfCancellationRequested();
            string localVerification;
            if (!page.IsClosed && string.Equals(page.Url, beforeUrl, StringComparison.Ordinal))
            {
                string selected = byLabel
                    ? await locator.EvaluateAsync<string>("el => el.selectedOptions?.[0]?.label || ''")
                    : await locator.EvaluateAsync<string>("el => String(el.value)");
                if (!string.Equals(selected, option, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The selection was dispatched but the selected option differed on re-observation. Inspect before retrying.");
                localVerification = "The DOM select contains the requested option; submitted/server state NOT verified.";
            }
            else
            {
                localVerification = "The page navigated/closed after selection; inspect the resulting state before retrying or claiming completion.";
            }
            SetActivePage(id);
            UpdatePageOrigin(id, page.Url);

            Debug.WriteLine(
                $"[Browser] SELECT | Session={_sessionId:D} | Page={id:D} | Ref='{cleanRef}'");

            return await ActionSnapshotAsync(id, page, "select", cleanRef,
                beforeUrl, localVerification);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserPageSnapshot> WaitAsync(
        Guid? pageId,
        int milliseconds,
        string? urlContains,
        string? elementRef,
        string? elementState,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        milliseconds = Math.Clamp(milliseconds, 0, 120000);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);

            if (milliseconds > 0)
            {
                await page.WaitForTimeoutAsync(milliseconds);
            }

            if (!string.IsNullOrWhiteSpace(urlContains))
            {
                DateTimeOffset deadline =
                    DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);

                while (!page.Url.Contains(urlContains.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (DateTimeOffset.UtcNow >= deadline)
                    {
                        throw new TimeoutException(
                            $"Browser URL did not contain '{urlContains.Trim()}' before timeout.");
                    }

                    await Task.Delay(100, cancellationToken);
                }
            }

            if (!string.IsNullOrWhiteSpace(elementRef))
            {
                ILocator locator =
                    ResolveRefLocator(
                        id,
                        page,
                        NormalizeElementRef(elementRef));

                await locator.WaitForAsync(
                    new LocatorWaitForOptions
                    {
                        State = ParseWaitState(elementState),
                        Timeout = timeoutSeconds * 1000
                    });
            }

            if (milliseconds == 0 &&
                string.IsNullOrWhiteSpace(urlContains) &&
                string.IsNullOrWhiteSpace(elementRef))
            {
                throw new InvalidOperationException(
                    "browser.wait requires milliseconds, urlContains, or ref.");
            }

            SetActivePage(id);
            UpdatePageOrigin(id, page.Url);
            return await BuildPageSnapshotAsync(id, page);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserScreenshotResult> ScreenshotAsync(
        Guid? pageId,
        bool fullPage,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);

            string path =
                Path.Combine(
                    _artifactsRoot,
                    $"browser-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");

            await page.ScreenshotAsync(
                new PageScreenshotOptions
                {
                    Path = path,
                    FullPage = fullPage,
                    Type = ScreenshotType.Png
                });

            cancellationToken.ThrowIfCancellationRequested();
            SetActivePage(id);
            UpdatePageOrigin(id, page.Url);

            FileInfo screenshotFile = new(path);
            string screenshotSha256 = await ComputeFileSha256Async(path, cancellationToken);

            NIRABrowserScreenshotResult result =
                new()
                {
                    SessionId = _sessionId,
                    PageId = id,
                    Url = RedactUrlForCognition(page.Url),
                    Title = await SafeTitleAsync(page),
                    LocalPath = path,
                    FullPage = fullPage,
                    SizeBytes = screenshotFile.Exists ? screenshotFile.Length : 0,
                    Sha256 = screenshotSha256,
                    CapturedAtUtc = DateTimeOffset.UtcNow
                };

            Debug.WriteLine(
                $"[Browser] SCREENSHOT | Session={_sessionId:D} | Page={id:D} | FullPage={fullPage} | Path='{path}'");

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserRequestResult> RequestAsync(
        string method,
        string url,
        IReadOnlyDictionary<string, string>? headers,
        string? body,
        int timeoutSeconds,
        int maxResponseCharacters,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Uri uri = ParseHttpUri(url);
        string normalizedMethod = NormalizeHttpMethod(method);
        maxResponseCharacters = Math.Clamp(maxResponseCharacters, 1000, 48000);

        if (ContainsCredentialLikePayload(body))
        {
            throw new InvalidOperationException(
                "browser.request rejected a credential-like request body. Authenticated browser requests must reuse the browser context's session rather than place passwords, tokens, API keys, PINs, one-time codes, or payment secrets in model-visible request arguments.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();

            Dictionary<string, string>? cleanHeaders = null;
            if (headers != null && headers.Count > 0)
            {
                cleanHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach ((string name, string value) in headers)
                {
                    if (IsCredentialHeader(name))
                    {
                        throw new InvalidOperationException(
                            $"Header '{name}' is credential-sensitive. browser.request deliberately reuses the browser context's authenticated cookie jar without exposing raw credentials to cognition.");
                    }

                    if (name.IndexOfAny(['\r', '\n']) >= 0 ||
                        value.IndexOfAny(['\r', '\n']) >= 0)
                    {
                        throw new InvalidOperationException(
                            "Browser request headers cannot contain newlines.");
                    }

                    cleanHeaders[name] = value;
                }
            }

            bool stateChangingRequest = normalizedMethod is not ("GET" or "HEAD");
            NIRABrowserActionCheckpoint? requestAction = null;
            if (stateChangingRequest)
            {
                EnsureNoUncertainOrigin(uri.GetLeftPart(UriPartial.Authority));
                requestAction = BeginHttpDispatch(uri, normalizedMethod);
            }
            IAPIResponse response;
            try
            {
                response = await _context!.APIRequest.FetchAsync(
                    uri.AbsoluteUri,
                    new()
                    {
                        Method = normalizedMethod,
                        Headers = cleanHeaders,
                        Data = body,
                        Timeout = timeoutSeconds * 1000,
                        FailOnStatusCode = false,
                        MaxRedirects = normalizedMethod is "GET" or "HEAD" ? 10 : 0
                    });
            }
            catch (Exception ex) when (requestAction != null &&
                ex is (PlaywrightException or OperationCanceledException))
            {
                FinishHttpDispatch(requestAction!, "OutcomeUncertain", uri.AbsoluteUri);
                if (ex is OperationCanceledException) throw;
                throw new InvalidOperationException(
                    $"BrowserRequestOutcomeUncertain ActionId={requestAction!.ActionId:D}: site may have applied the request. Read/inspect independent state before any retry.", ex);
            }
            // Receiving a response is only transport evidence; its status/body
            // are NOT proof that the requested business effect is complete.
            if (requestAction != null)
                FinishHttpDispatch(requestAction, "MechanicallyReturned", response.Url);

            cancellationToken.ThrowIfCancellationRequested();

            string responseText =
                RedactStructuredSecretsForCognition(
                    await response.TextAsync());

            if (responseText.Length > maxResponseCharacters)
            {
                responseText =
                    responseText[..maxResponseCharacters] +
                    "\n...[truncated by NIRA browser runtime]";
            }

            Debug.WriteLine(
                $"[Browser] REQUEST | Session={_sessionId:D} | Method={normalizedMethod} | Status={response.Status} | Url='{TrimLog(response.Url)}'");

            NIRABrowserRequestResult result = new()
            {
                SessionId = _sessionId,
                Method = normalizedMethod,
                Url = RedactUrlForCognition(response.Url),
                Status = response.Status,
                Ok = response.Ok,
                Body = responseText
            };

            await response.DisposeAsync();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Upload selection is one bounded operation. It does not click Submit and
    // site scripts MAY upload immediately on selection; do not claim acceptance.
    public async Task<NIRABrowserUploadResult> UploadAsync(
        Guid pageId, string elementRef, string path, string approvedSha256,
        long approvedBytes, int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string cleanRef = NormalizeElementRef(elementRef);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);
            EnsureNoUncertainAction(id, page);
            GroundedElementState grounded = ResolveGroundedElement(id, cleanRef);
            ILocator locator = ResolveRefLocator(id, page, cleanRef);
            if (await locator.CountAsync() != 1 || grounded.Disabled ||
                !await locator.IsEnabledAsync())
                throw new InvalidOperationException("Upload target was lost or disabled. Inspect again; do not retry blindly.");
            string tag = await locator.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
            string type = (await locator.GetAttributeAsync("type") ?? string.Empty).Trim().ToLowerInvariant();
            if (tag != "input" || type != "file" || grounded.Tag != "input" ||
                !string.Equals(grounded.InputType, "file", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Upload ref must be the inspected file input; no generic click fallback.");
            // Upload is not allowed to follow a redirect to another origin.
            string currentOrigin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
            if (!string.Equals(currentOrigin, new Uri(grounded.ObservedPageUrl).GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Upload destination changed after inspection.");
            _ = ResolveGroundedElement(id, cleanRef);
            string beforeUrl = page.Url;
            cancellationToken.ThrowIfCancellationRequested();

            // Snapshot the approved bytes before any browser action. The browser
            // never reads a mutable path after we verify the snapshot.
            byte[] bytes;
            await using (FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (source.Length != approvedBytes || source.Length > 20L * 1024 * 1024)
                    throw new InvalidOperationException("Approved file size changed; request a new upload approval.");
                using MemoryStream buffer = new();
                await source.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (bytes.LongLength != approvedBytes ||
                !string.Equals(sha256, approvedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Approved file contents changed; request a new upload approval.");
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            NIRABrowserActionCheckpoint upload = BeginDispatch(id, page, "upload", cleanRef);
            try
            {
                await locator.SetInputFilesAsync(
                    new[] { new FilePayload { Name = name, MimeType = "application/octet-stream", Buffer = bytes } },
                    new LocatorSetInputFilesOptions { Timeout = timeoutSeconds * 1000 });
            }
            catch (Exception ex) when (ex is (PlaywrightException or OperationCanceledException))
            {
                FinishDispatch(upload, "OutcomeUncertain", page);
                ClearElementRefs(id);
                if (ex is OperationCanceledException) throw;
                throw new InvalidOperationException(
                    $"BrowserUploadOutcomeUncertain ActionId={upload.ActionId:D}: file selection may have triggered an external upload. Inspect first; automatic repeat is blocked.", ex);
            }
            FinishDispatch(upload, "MechanicallyReturned", page);

            // Only verify the local file chooser state. Site scripts can already
            // transmit on selection; their outcome needs separate evidence.
            bool staged;
            try
            {
                staged = !page.IsClosed && string.Equals(page.Url, beforeUrl, StringComparison.Ordinal) &&
                    await locator.EvaluateAsync<bool>("el => el.files?.length === 1");
            }
            catch (Exception ex) when (ex is (PlaywrightException or OperationCanceledException))
            {
                FinishDispatch(upload, "OutcomeUncertain", page);
                if (ex is OperationCanceledException) throw;
                throw new InvalidOperationException(
                    $"BrowserUploadOutcomeUncertain ActionId={upload.ActionId:D}: file selection completed but DOM verification failed. Inspect before retrying.", ex);
            }
            if (!staged)
            {
                FinishDispatch(upload, "OutcomeUncertain", page);
                throw new IOException("File selection may have occurred but could not be verified; inspect the page before any retry or submission.");
            }
            ClearElementRefs(id);
            UpdatePageOrigin(id, page.Url);
            return new NIRABrowserUploadResult
            {
                SessionId = _sessionId, PageId = id,
                Origin = currentOrigin, LocalPath = path, FileName = name,
                SizeBytes = approvedBytes, Sha256 = sha256,
                StagedAtUtc = DateTimeOffset.UtcNow, FileStagedInDom = true
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NIRABrowserDownloadResult> DownloadAsync(
        Guid? pageId,
        string elementRef,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string cleanRef = NormalizeElementRef(elementRef);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            IPage page = ResolvePage(pageId);
            Guid id = EnsurePageId(page);
            EnsureNoUncertainAction(id, page);
            ILocator locator = await ValidateGroundedActionTargetAsync(id, page, cleanRef);

            IDownload download =
                await page.RunAndWaitForDownloadAsync(
                    async () =>
                    {
                        await locator.ClickAsync(
                            new LocatorClickOptions
                            {
                                Timeout = timeoutSeconds * 1000
                            });
                    },
                    new PageRunAndWaitForDownloadOptions
                    {
                        Timeout = timeoutSeconds * 1000
                    });

            string suggested =
                SanitizeFileName(download.SuggestedFilename);

            string destination =
                Path.Combine(
                    _downloadsRoot,
                    $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{suggested}");

            // A download event proves only that a transfer began. Its saved
            // file, Playwright failure state, size and hash are checked before
            // success is returned. A zero-byte file may be legitimate.
            try
            {
                await download.SaveAsAsync(destination);
                cancellationToken.ThrowIfCancellationRequested();
                string? failure = await download.FailureAsync();
                if (!string.IsNullOrWhiteSpace(failure))
                    throw new IOException("Browser download failed: " + failure);

                FileInfo downloadedFile = new(destination);
                if (!downloadedFile.Exists)
                    throw new IOException("Browser download reported completion but its saved file is missing.");
                long verifiedBytes = downloadedFile.Length;
                string downloadSha256 = await ComputeFileSha256Async(destination, cancellationToken);
                downloadedFile.Refresh();
                if (!downloadedFile.Exists || downloadedFile.Length != verifiedBytes)
                    throw new IOException("Saved download changed during verification; do not treat it as complete.");
                DateTimeOffset verifiedAtUtc = DateTimeOffset.UtcNow;
                if (!page.IsClosed) UpdatePageOrigin(id, page.Url);
                ClearElementRefs(id);

                Debug.WriteLine(
                    $"[Browser] DOWNLOAD VERIFIED | Session={_sessionId:D} | Page={id:D} | Bytes={verifiedBytes} | Sha256={downloadSha256}");

                return new NIRABrowserDownloadResult
                {
                    SessionId = _sessionId,
                    PageId = id,
                    Url = RedactUrlForCognition(download.Url),
                    SuggestedFileName = suggested,
                    LocalPath = destination,
                    SizeBytes = verifiedBytes,
                    Sha256 = downloadSha256,
                    VerifiedAtUtc = verifiedAtUtc
                };
            }
            catch
            {
                // Never leave an unverified file in the normal downloads area.
                // The click may have reached the site; do not automatically retry.
                try { if (File.Exists(destination)) File.Delete(destination); }
                catch (IOException) { /* Diagnostic remains the thrown original. */ }
                catch (UnauthorizedAccessException) { /* Original failure stays authoritative. */ }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task NavigateCoreAsync(
        IPage page,
        string url,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Guid id = EnsurePageId(page);
        lock (_stateSync)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? inputUri) &&
                (!string.IsNullOrEmpty(inputUri.Query) || !string.IsNullOrEmpty(inputUri.Fragment)))
                _queryBearingNavigation.Add(id);
            else
                _queryBearingNavigation.Remove(id);
        }
        SetNavigation(id, new NIRABrowserNavigationEvidence
        {
            RequestedUrl = NavigationUrlForCognition(url),
            FinalUrl = NavigationUrlForCognition(page.Url),
            FailureKind = "NavigationInProgress",
            OutcomeUncertain = true
        });
        ClearElementRefs(id);
        try
        {
            IResponse? response = await page.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = timeoutSeconds * 1000
                });
            cancellationToken.ThrowIfCancellationRequested();
            SetNavigation(id, new NIRABrowserNavigationEvidence
            {
                RequestedUrl = NavigationUrlForCognition(url),
                FinalUrl = NavigationUrlForCognition(page.Url),
                RedirectChain = response == null ? Array.Empty<string>() : BuildRedirectChain(response.Request),
                MainDocumentHttpStatus = response?.Status,
                FailureKind = string.Empty,
                OutcomeUncertain = false
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetNavigationFailure(id, page, "NavigationInterrupted");
            throw;
        }
        catch (PlaywrightException ex)
        {
            SetNavigationFailure(id, page, "NavigationFailedOrTimedOut");
            throw new InvalidOperationException(
                "BrowserNavigationUncertain: navigation failed or timed out. Use browser.current and inspect the fresh page state; do not automatically repeat any possibly completed workflow action.", ex);
        }
    }

    private async Task RefreshPagesAsync()
    {
        if (_context == null)
            return;

        foreach (IPage page in _context.Pages)
            RegisterPage(page, recovered: false);

        // Playwright's opener relationship is evidence for a popup's owner;
        // event callbacks alone must not infer owner from ambient AsyncLocal.
        KeyValuePair<Guid, IPage>[] unclaimed;
        lock (_stateSync)
            unclaimed = _pages.Where(pair =>
                _ownership.TryGetValue(pair.Key, out PageOwnership? own) &&
                own.OwnerKey.Length == 0 && !own.Recovered && !pair.Value.IsClosed).ToArray();
        foreach ((Guid id, IPage page) in unclaimed)
        {
            IPage? opener;
            try { opener = await page.OpenerAsync(); }
            catch (PlaywrightException) { continue; }
            if (opener == null) continue;
            lock (_stateSync)
            {
                if (_pageIds.TryGetValue(opener, out Guid openerId) &&
                    _ownership.TryGetValue(openerId, out PageOwnership? parent) &&
                    parent.OwnerKey.Length > 0 &&
                    _ownership.TryGetValue(id, out PageOwnership? existing) &&
                    existing.OwnerKey.Length == 0)
                    _ownership[id] = parent with { OpenerPageId = openerId, Recovered = false };
            }
        }

        lock (_stateSync)
        {
            foreach (Guid stale in
                     _pages
                         .Where(pair => pair.Value.IsClosed)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                IPage page = _pages[stale];
                _pages.Remove(stale);
                _pageIds.Remove(page);
                _pageOrigins.Remove(stale);
                _ownership.Remove(stale);
                foreach (string key in _activeByOwner.Where(pair => pair.Value == stale).Select(pair => pair.Key).ToArray())
                    _activeByOwner.Remove(key);
                _latestElementRefs.Remove(stale);
                _latestGroundedElements.Remove(stale);
                _navigation.Remove(stale);
                _crashedPages.Remove(stale);
                _uncertainActions.Remove(stale);

            }

            // Never silently replace a closed active tab with an unrelated tab.
            // The next action must select an exact surviving PageId.
        }

        await Task.CompletedTask;
    }

    private async Task<NIRABrowserSessionSnapshot> SnapshotCoreAsync()
    {
        EnsureOpen();
        await RefreshPagesAsync();

        KeyValuePair<Guid, IPage>[] pagePairs;
        Guid? active;
        lock (_stateSync)
        {
            pagePairs = _pages.ToArray();
            active = _activeByOwner.TryGetValue(CurrentOwnerKey(), out Guid ownedActive) &&
                _pages.TryGetValue(ownedActive, out IPage? live) && !live.IsClosed
                ? ownedActive : null;
        }

        List<NIRABrowserPageSnapshot> pages = [];
        foreach ((Guid id, IPage page) in pagePairs)
        {
            pages.Add(await BuildPageSnapshotAsync(id, page));
        }

        return new NIRABrowserSessionSnapshot
        {
            SessionId = _sessionId,
            Profile = _profile,
            Headed = _headed,
            IsOpen = true,
            ActivePageId = active,
            RecoveryNotice = "Browser storage may persist, but authentication is unverified. Restored tabs receive NEW IDs. PendingActionReviews are durable dispatch receipts, not proof of site success; inspect and reconcile before any repeat. No automatic retry.",
            Pages = pages,
            PendingActionReviews = _actionJournal.ReadUncertain(CurrentDispatchOwnerKey())
        };
    }

    private static string OwnerKey(NIRAAuthorityExecutionContext context) =>
        context.BranchId is Guid branch && branch != Guid.Empty
            ? "branch:" + branch.ToString("D")
            : context.GoalId is Guid goal && goal != Guid.Empty
                ? "goal:" + goal.ToString("D")
                : "interactive";

    private string CurrentOwnerKey() => OwnerKey(_authority.Current);

    // A completed goal no longer reserves tabs against explicitly selecting
    // them or closing the session. An unknown goal is treated conservatively.
    private bool OwnerStillActive(PageOwnership ownership) =>
        ownership.GoalId is not Guid goalId ||
        !_goals.TryGetGoal(goalId, out NIRAGoalState? goal) ||
        goal == null || goal.IsOpen;

    private void RegisterPage(IPage page, bool recovered)
    {
        lock (_stateSync)
        {
            if (_pageIds.TryGetValue(page, out Guid existingId))
            {
                if (recovered && _ownership.TryGetValue(existingId, out PageOwnership? existing) &&
                    existing.OwnerKey.Length == 0)
                    _ownership[existingId] = existing with { Recovered = true };
                return;
            }
            Guid id = Guid.NewGuid();
            _pageIds[page] = id;
            _pages[id] = page;
            _ownership[id] = new PageOwnership(string.Empty, null, null, null, recovered, null);
        }

        // Generic transport/runtime events only; never interpret URLs as login
        // success, infer ownership from a background callback, or retain secrets.
        page.Response += (_, response) => ObserveDocumentResponse(page, response);
        page.RequestFailed += (_, request) => ObserveDocumentFailure(page, request);
        page.FrameNavigated += (_, frame) => ObserveMainFrameNavigation(page, frame);
        page.Crash += (_, crashedPage) => ObservePageCrash(crashedPage);
    }

    private static bool IsMainDocumentRequest(IPage page, IRequest request)
    {
        try { return request.IsNavigationRequest && request.Frame == page.MainFrame; }
        catch (PlaywrightException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // Navigation diagnostics do not need query strings, fragments, or HTTP
    // userinfo; these can contain transient tokens. Keep only the page route.
    private static string NavigationUrlForCognition(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme is not ("http" or "https"))
            return string.Empty;
        UriBuilder builder = new(parsed)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri;
    }

    private static IReadOnlyList<string> BuildRedirectChain(IRequest finalRequest)
    {
        List<string> chain = [];
        IRequest? current = finalRequest;
        for (int i = 0; current != null && i < 12; i++, current = current.RedirectedFrom)
            chain.Add(NavigationUrlForCognition(current.Url));
        chain.Reverse();
        return chain;
    }

    private void ObserveDocumentResponse(IPage page, IResponse response)
    {
        if (!IsMainDocumentRequest(page, response.Request)) return;
        lock (_stateSync)
        {
            if (!_pageIds.TryGetValue(page, out Guid id)) return;
            _navigation.TryGetValue(id, out NIRABrowserNavigationEvidence? previous);
            IRequest first = response.Request;
            for (int i = 0; first.RedirectedFrom != null && i < 12; i++)
                first = first.RedirectedFrom;
            if (Uri.TryCreate(first.Url, UriKind.Absolute, out Uri? initialUri) &&
                (!string.IsNullOrEmpty(initialUri.Query) || !string.IsNullOrEmpty(initialUri.Fragment)))
                _queryBearingNavigation.Add(id);
            _navigation[id] = new NIRABrowserNavigationEvidence
            {
                RequestedUrl = previous is { FailureKind: "NavigationInProgress" }
                    ? previous.RequestedUrl : BuildRedirectChain(response.Request).FirstOrDefault() ?? NavigationUrlForCognition(response.Request.Url),
                FinalUrl = NavigationUrlForCognition(response.Url),
                RedirectChain = BuildRedirectChain(response.Request),
                MainDocumentHttpStatus = response.Status,
                OutcomeUncertain = false
            };
            _latestElementRefs.Remove(id);
            _latestGroundedElements.Remove(id);
        }
    }

    private void ObserveDocumentFailure(IPage page, IRequest request)
    {
        if (!IsMainDocumentRequest(page, request)) return;
        lock (_stateSync)
        {
            if (!_pageIds.TryGetValue(page, out Guid id)) return;
            _navigation.TryGetValue(id, out NIRABrowserNavigationEvidence? prior);
            _navigation[id] = new NIRABrowserNavigationEvidence
            {
                RequestedUrl = prior is { FailureKind: "NavigationInProgress" }
                    ? prior.RequestedUrl : BuildRedirectChain(request).FirstOrDefault() ?? NavigationUrlForCognition(request.Url),
                FinalUrl = NavigationUrlForCognition(page.Url),
                RedirectChain = BuildRedirectChain(request),
                FailureKind = "MainDocumentNetworkFailure",
                OutcomeUncertain = true
            };
            _latestElementRefs.Remove(id);
            _latestGroundedElements.Remove(id);
        }
    }

    private void ObserveMainFrameNavigation(IPage page, IFrame frame)
    {
        if (frame != page.MainFrame) return;
        lock (_stateSync)
        {
            if (!_pageIds.TryGetValue(page, out Guid id)) return;
            _navigation.TryGetValue(id, out NIRABrowserNavigationEvidence? prior);
            string newUrl = NavigationUrlForCognition(frame.Url);
            // Same-document/status evidence may be retained only for the same
            // observed document. A new document without a network response
            // has UNKNOWN status, not the previous page's 200/401/404.
            _navigation[id] = prior != null && string.Equals(prior.FinalUrl, newUrl, StringComparison.Ordinal)
                ? prior with { ObservedAtUtc = DateTimeOffset.UtcNow }
                : new NIRABrowserNavigationEvidence
                {
                    RequestedUrl = newUrl, FinalUrl = newUrl,
                    ObservedAtUtc = DateTimeOffset.UtcNow
                };
            _latestElementRefs.Remove(id);
            _latestGroundedElements.Remove(id);
        }
    }

    private void ObservePageCrash(IPage page)
    {
        lock (_stateSync)
        {
            if (!_pageIds.TryGetValue(page, out Guid id)) return;
            _crashedPages.Add(id);
            _uncertainActions[id] = "PageCrash; last action outcome may be uncertain";
            _latestElementRefs.Remove(id);
            _latestGroundedElements.Remove(id);
            _navigation.TryGetValue(id, out NIRABrowserNavigationEvidence? prior);
            _navigation[id] = (prior ?? new NIRABrowserNavigationEvidence()) with
            {
                FailureKind = "PageCrash", OutcomeUncertain = true,
                ObservedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private NIRABrowserNavigationEvidence? NavigationForPage(Guid pageId)
    {
        lock (_stateSync)
            return _navigation.TryGetValue(pageId, out NIRABrowserNavigationEvidence? value) ? value : null;
    }

    private void SetNavigation(Guid pageId, NIRABrowserNavigationEvidence evidence)
    {
        lock (_stateSync) _navigation[pageId] = evidence;
    }

    private void SetNavigationFailure(Guid pageId, IPage page, string kind)
    {
        lock (_stateSync)
        {
            _navigation.TryGetValue(pageId, out NIRABrowserNavigationEvidence? previous);
            _navigation[pageId] = (previous ?? new NIRABrowserNavigationEvidence()) with
            {
                FinalUrl = page.IsClosed ? previous?.FinalUrl ?? string.Empty : NavigationUrlForCognition(page.Url),
                FailureKind = kind,
                OutcomeUncertain = true,
                ObservedAtUtc = DateTimeOffset.UtcNow
            };
            _latestElementRefs.Remove(pageId);
            _latestGroundedElements.Remove(pageId);
        }
    }

    private Guid EnsurePageId(IPage page)
    {
        RegisterPage(page, recovered: false);
        lock (_stateSync) return _pageIds[page];
    }

    private void ClaimPage(Guid id, bool allowParentTransfer)
    {
        NIRAAuthorityExecutionContext execution = _authority.Current;
        string key = OwnerKey(execution);
        lock (_stateSync)
        {
            if (!_ownership.TryGetValue(id, out PageOwnership? existing))
                throw new InvalidOperationException("Browser page is no longer registered. Call browser.current.");
            if (existing.OwnerKey.Length > 0 && existing.OwnerKey != key &&
                OwnerStillActive(existing))
            {
                bool parentTransfer = allowParentTransfer &&
                    execution.GoalId is Guid goal && goal != Guid.Empty &&
                    existing.GoalId == goal && execution.BranchId == null;
                // A main-goal observation may be delegated to a child branch.
                // This is an exact, same-goal parent -> child handoff, never
                // permission to take an unrelated goal's or sibling branch's tab.
                bool delegatedChildTransfer =
                    execution.GoalId is Guid childGoal && childGoal != Guid.Empty &&
                    execution.BranchId is Guid childBranch && childBranch != Guid.Empty &&
                    existing.GoalId == childGoal && existing.BranchId == null;
                if (!parentTransfer && !delegatedChildTransfer)
                    throw new InvalidOperationException(
                        "PageOwnedByAnotherTask: this tab belongs to another active browser responsibility. Use browser.navigate newPage=true; never select a foreign task's page by guessing.");
            }
            if (existing.OwnerKey != key &&
                _activeByOwner.TryGetValue(existing.OwnerKey, out Guid previousActive) &&
                previousActive == id)
                _activeByOwner.Remove(existing.OwnerKey);
            _ownership[id] = new PageOwnership(
                key, execution.GoalId, execution.BranchId,
                existing.OpenerPageId, existing.Recovered,
                existing.CreatedRunId ?? (execution.RunId == Guid.Empty ? null : execution.RunId));
            _activeByOwner[key] = id;
        }
    }

    // Trusted, exact-run handoff: a direct user request can open + inspect a
    // page before the model creates its persistent goal/branch in a later cycle.
    // The same user-event run may adopt that ONE page; arbitrary old/foreign
    // tabs, other runs and persisted profile tabs can never be adopted.
    public bool PromoteInteractivePageOpenedByRun(
        Guid runId, Guid goalId, Guid branchId)
    {
        if (runId == Guid.Empty || goalId == Guid.Empty || branchId == Guid.Empty)
            return false;
        lock (_stateSync)
        {
            Guid[] candidates = _pages.Where(pair => !pair.Value.IsClosed &&
                _ownership.TryGetValue(pair.Key, out PageOwnership? owner) &&
                owner.OwnerKey == "interactive" && !owner.Recovered &&
                owner.CreatedRunId == runId && owner.GoalId == null &&
                owner.BranchId == null).Select(pair => pair.Key).ToArray();
            if (candidates.Length != 1) return false;
            Guid pageId = candidates[0];
            string branchKey = "branch:" + branchId.ToString("D");
            if (_activeByOwner.TryGetValue(branchKey, out Guid branchActive) &&
                branchActive != pageId) return false;
            PageOwnership previous = _ownership[pageId];
            _ownership[pageId] = previous with
            {
                OwnerKey = branchKey, GoalId = goalId, BranchId = branchId
            };
            if (_activeByOwner.TryGetValue("interactive", out Guid active) &&
                active == pageId) _activeByOwner.Remove("interactive");
            _activeByOwner[branchKey] = pageId;
            Debug.WriteLine($"[BrowserOwnership] PROMOTED | Page={pageId:D} | " +
                $"Run={runId:D} | Goal={goalId:D} | Branch={branchId:D} | " +
                "Reason=SameDirectUserRun");
            return true;
        }
    }

    private async Task<IPage> CreateOwnedPageAsync()
    {
        IPage page = await _context!.NewPageAsync();
        Guid id = EnsurePageId(page);
        ClaimPage(id, allowParentTransfer: false);
        return page;
    }

    private bool HasOwnedPages()
    {
        lock (_stateSync)
            return _pages.Any(pair => !pair.Value.IsClosed &&
                _ownership.TryGetValue(pair.Key, out PageOwnership? ownership) &&
                ownership.OwnerKey == CurrentOwnerKey());
    }

    private IPage? FindSoleOwnedPage()
    {
        lock (_stateSync)
        {
            string owner = CurrentOwnerKey();
            IPage[] owned = _pages.Where(pair => !pair.Value.IsClosed &&
                _ownership.TryGetValue(pair.Key, out PageOwnership? info) &&
                info.OwnerKey == owner).Select(pair => pair.Value).ToArray();
            return owned.Length == 1 ? owned[0] : null;
        }
    }

    private void EnsureSessionOwnedForClose()
    {
        lock (_stateSync)
        {
            string owner = CurrentOwnerKey();
            if (_pages.Any(pair => !pair.Value.IsClosed &&
                _ownership.TryGetValue(pair.Key, out PageOwnership? info) &&
                info.OwnerKey.Length > 0 && info.OwnerKey != owner &&
                OwnerStillActive(info)))
                throw new InvalidOperationException(
                    "Another task owns an open page. Closing or changing this shared browser session would interrupt it. Do not close the whole session.");
        }
    }

    // Explicit selection is a deliberate handoff. Recovered tabs are never
    // silently claimed by the first unrelated background branch after restart.
    public async Task<NIRABrowserPageSnapshot> SelectPageAsync(
        Guid pageId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureOpen();
            await RefreshPagesAsync();
            if (!_pages.TryGetValue(pageId, out IPage? page) || page.IsClosed)
                throw new InvalidOperationException(
                    "The requested page closed. Use browser.current; old PageIds cannot be reused after browser restart.");
            // If page ownership was not yet reconciled by the context event,
            // do NOT let another task claim a foreign popup as an unowned tab.
            IPage? opener;
            try { opener = await page.OpenerAsync(); }
            catch (PlaywrightException) { opener = null; }
            if (opener != null)
            {
                lock (_stateSync)
                {
                    if (_pageIds.TryGetValue(opener, out Guid openerId) &&
                        _ownership.TryGetValue(openerId, out PageOwnership? parent) &&
                        parent.OwnerKey.Length > 0 && parent.OwnerKey != CurrentOwnerKey() &&
                        OwnerStillActive(parent))
                        throw new InvalidOperationException(
                            "The requested popup belongs to another task. Select one of your own pages or open a new page.");
                }
            }
            ClaimPage(pageId, allowParentTransfer: true);
            ClearElementRefs(pageId);
            return await BuildPageSnapshotAsync(pageId, page);
        }
        finally { _gate.Release(); }
    }

    private IPage ResolvePage(Guid? pageId)
    {
        EnsureOpen();
        lock (_stateSync)
        {
            string owner = CurrentOwnerKey();
            if (pageId is Guid id)
            {
                if (_pages.TryGetValue(id, out IPage? exact) && !exact.IsClosed)
                {
                    if (!_ownership.TryGetValue(id, out PageOwnership? state))
                        throw new InvalidOperationException(
                            "PageNotSelectedForTask: this page has no valid owner. Use browser.current and a fresh task-owned page.");
                    if (state.OwnerKey != owner)
                    {
                        NIRAAuthorityExecutionContext execution = _authority.Current;
                        if (execution.GoalId is not Guid goal || goal == Guid.Empty ||
                            execution.BranchId is not Guid branch || branch == Guid.Empty ||
                            state.GoalId != goal || state.BranchId != null ||
                            !OwnerStillActive(state))
                            throw new InvalidOperationException(
                                "PageNotSelectedForTask: use browser.page.select with the exact PageId, or open a new task-owned page. A foreign task's page cannot be used.");
                        // Keep the CURRENT inspected link refs: no page reload,
                        // second inspection, or credential prompt is necessary.
                        ClaimPage(id, allowParentTransfer: false);
                    }
                    return exact;
                }
                throw new InvalidOperationException(
                    $"Browser page '{id:D}' is closed or from a previous session. Call browser.current; never replay a prior action.");
            }
            IPage[] owned = _pages.Where(pair => !pair.Value.IsClosed &&
                _ownership.TryGetValue(pair.Key, out PageOwnership? state) &&
                state.OwnerKey == owner).Select(pair => pair.Value).ToArray();
            if (owned.Length == 1) return owned[0];
            if (owned.Length == 0)
                throw new InvalidOperationException(
                    "This task has no selected page. Call browser.current then browser.page.select, or open a new page with browser.navigate newPage=true.");
            throw new InvalidOperationException(
                "AmbiguousBrowserPage: multiple pages belong to this task. Supply an exact PageId from browser.current/inspect. Never guess the active tab.");
        }
    }

    // Missing pageId is safe only with ONE open page owned by this task.
    // Never select recovered/unclaimed or another task's tab automatically.
    public Guid? TryGetSoleOwnedPageId()
    {
        lock (_stateSync)
        {
            string owner = CurrentOwnerKey();
            Guid[] owned = _pages.Where(pair => !pair.Value.IsClosed &&
                _ownership.TryGetValue(pair.Key, out PageOwnership? state) &&
                state.OwnerKey == owner).Select(pair => pair.Key).ToArray();
            return owned.Length == 1 ? owned[0] : null;
        }
    }

    public bool TryGetAuthorizationOrigin(
        Guid pageId,
        out string origin)
    {
        lock (_stateSync)
        {
            if (_pages.TryGetValue(pageId, out IPage? page) && !page.IsClosed &&
                _ownership.TryGetValue(pageId, out PageOwnership? state) &&
                state.OwnerKey == CurrentOwnerKey())
            {
                origin = TryGetHttpOrigin(page.Url);
                return origin.Length > 0;
            }
        }

        origin = string.Empty;
        return false;
    }

    private void SetActivePage(Guid id)
    {
        ClaimPage(id, allowParentTransfer: false);
    }

    private void UpdatePageOrigin(Guid id, string? url)
    {
        string origin = TryGetHttpOrigin(url);
        lock (_stateSync)
        {
            if (origin.Length == 0)
                _pageOrigins.Remove(id);
            else
                _pageOrigins[id] = origin;
        }
    }

    private static string TryGetHttpOrigin(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return string.Empty;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private ILocator ResolveRefLocator(
        Guid pageId,
        IPage page,
        string elementRef)
    {
        _ = ResolveGroundedElement(pageId, elementRef);

        return page.Locator(
            $"[data-NIRA-ref=\"{elementRef}\"]");
    }

    // Recheck a ref immediately before a state-changing interaction. Refs are
    // grounded to one observed page and DOM target; never guess/retry a mutation.
    // A rejected ref must be refreshed with browser.inspect and re-authorized
    // through the normal request path when that changes the permitted origin.
    private async Task<ILocator> ValidateGroundedActionTargetAsync(
        Guid pageId, IPage page, string elementRef)
    {
        GroundedElementState grounded = ResolveGroundedElement(pageId, elementRef);
        ILocator locator = ResolveRefLocator(pageId, page, elementRef);
        if (await locator.CountAsync() != 1)
            throw new InvalidOperationException(
                "StaleTarget: the inspected element is missing or ambiguous. Re-inspect the page; never guess or replay the action.");

        string tag = await locator.EvaluateAsync<string>(
            "el => el.tagName.toLowerCase()");
        string type = tag == "input"
            ? (await locator.GetAttributeAsync("type") ?? "text")
                .Trim().ToLowerInvariant()
            : string.Empty;
        if (!string.Equals(tag, grounded.Tag, StringComparison.Ordinal) ||
            !string.Equals(type, grounded.InputType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "StaleTarget: the element's tag or input type changed. Re-inspect before acting.");

        if (grounded.Disabled || !await locator.IsEnabledAsync() ||
            !await locator.IsVisibleAsync())
            throw new InvalidOperationException(
                "StaleTarget: the inspected element is disabled or no longer visible. Re-inspect before acting.");

        // Async checks may cross a navigation boundary. Reject a ref from a
        // previous document even if its temporary marker survived in the UI.
        _ = ResolveGroundedElement(pageId, elementRef);
        return locator;
    }

    private async Task<NIRABrowserPageSnapshot> ActionSnapshotAsync(
        Guid pageId, IPage page, string operation, string elementRef,
        string beforeUrl, string localVerification,
        IReadOnlyList<Guid>? newPageIds = null)
    {
        NIRABrowserPageSnapshot after = await BuildPageSnapshotAsync(pageId, page);
        return after with
        {
            ActionEvidence = new NIRABrowserActionEvidence
            {
                Operation = operation,
                ElementRef = elementRef,
                BeforeUrl = RedactUrlForCognition(beforeUrl),
                AfterUrl = after.Url,
                ActionApplied = true,
                NewPageIds = newPageIds ?? Array.Empty<Guid>(),
                LocalVerification = localVerification,
                ObservedAtUtc = DateTimeOffset.UtcNow
            }
        };
    }

    public bool IsGroundedElementRef(
        Guid pageId,
        string elementRef)
    {
        string clean;

        try
        {
            clean = NormalizeElementRef(elementRef);
        }
        catch
        {
            return false;
        }

        lock (_stateSync)
        {
            return _pages.TryGetValue(pageId, out IPage? page) &&
                   !page.IsClosed &&
                   _ownership.TryGetValue(pageId, out PageOwnership? state) &&
                   state.OwnerKey == CurrentOwnerKey() &&
                   _latestElementRefs.TryGetValue(pageId, out HashSet<string>? refs) &&
                   refs.Contains(clean) &&
                   _latestGroundedElements.TryGetValue(pageId, out Dictionary<string, GroundedElementState>? elements) &&
                   elements.TryGetValue(clean, out GroundedElementState? grounded) &&
                   string.Equals(page.Url, grounded.ObservedPageUrl, StringComparison.Ordinal);
        }
    }

    private void SetLatestElements(
        Guid pageId,
        IReadOnlyDictionary<string, GroundedElementState> elements)
    {
        lock (_stateSync)
        {
            _latestElementRefs[pageId] =
                elements.Keys
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.Ordinal);

            _latestGroundedElements[pageId] =
                elements.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
        }
    }

    private GroundedElementState ResolveGroundedElement(
        Guid pageId,
        string elementRef)
    {
        lock (_stateSync)
        {
            if (_latestGroundedElements.TryGetValue(pageId, out Dictionary<string, GroundedElementState>? elements) &&
                elements.TryGetValue(elementRef, out GroundedElementState? grounded) &&
                _pages.TryGetValue(pageId, out IPage? page) &&
                !page.IsClosed)
            {
                if (!string.Equals(page.Url, grounded.ObservedPageUrl, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The browser page URL changed after the latest browser.inspect result. Re-inspect the current page before using old element refs.");
                }

                return grounded;
            }
        }

        throw new InvalidOperationException(
            "The browser element ref is not grounded in the latest browser.inspect result for this page. Re-inspect the page and use an exact returned ref.");
    }

    private string CurrentDispatchOwnerKey()
    {
        NIRAAuthorityExecutionContext context = _authority.Current;
        return context.GoalId is Guid goal && goal != Guid.Empty
            ? $"goal:{goal:D}"
            : $"run:{(context.RunId == Guid.Empty ? _sessionId : context.RunId):D}";
    }

    private void EnsureNoUncertainAction(Guid pageId, IPage page)
    {
        string origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
        EnsureNoUncertainOrigin(origin);
        lock (_stateSync)
            if (_crashedPages.Contains(pageId))
                throw new InvalidOperationException(
                    "BrowserPageCrashed: create/recover a fresh page and inspect before any new mutation.");
    }

    private void EnsureNoUncertainOrigin(string origin)
    {
        // Database check survives a process crash, unlike volatile page flags.
        if (_actionJournal.HasUncertainForOrigin(CurrentDispatchOwnerKey(), origin))
            throw new InvalidOperationException(
                "BrowserUncertainActionBlocked: a previous browser mutation on this task and origin has an unknown outcome. Investigate with read-only evidence and do not retry through another tool or tab. A future trusted reconciliation decision is required before further mutations in this scope.");
    }

    private NIRABrowserActionCheckpoint BeginHttpDispatch(Uri uri, string method)
    {
        NIRAAuthorityExecutionContext context = _authority.Current;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        NIRABrowserActionCheckpoint action = new()
        {
            SessionId = _sessionId, PageId = Guid.Empty,
            GoalId = context.GoalId, BranchId = context.BranchId,
            RunId = context.RunId == Guid.Empty ? _sessionId : context.RunId,
            Operation = "request." + method,
            BeforeRoute = NavigationUrlForCognition(uri.AbsoluteUri),
            BeforeOrigin = uri.GetLeftPart(UriPartial.Authority),
            DispatchedAtUtc = now, UpdatedAtUtc = now
        };
        _actionJournal.Save(action);
        return action;
    }

    private void FinishHttpDispatch(NIRABrowserActionCheckpoint action,
        string state, string afterUrl)
    {
        _actionJournal.Save(action with
        {
            State = state,
            AfterRoute = NavigationUrlForCognition(afterUrl),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private NIRABrowserActionCheckpoint BeginDispatch(Guid pageId, IPage page,
        string operation, string elementRef)
    {
        NIRAAuthorityExecutionContext context = _authority.Current;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        NIRABrowserActionCheckpoint action = new()
        {
            SessionId = _sessionId, PageId = pageId,
            GoalId = context.GoalId, BranchId = context.BranchId,
            RunId = context.RunId == Guid.Empty ? _sessionId : context.RunId,
            Operation = operation, ElementRef = elementRef,
            BeforeRoute = NavigationUrlForCognition(page.Url),
            BeforeOrigin = new Uri(page.Url).GetLeftPart(UriPartial.Authority),
            DispatchedAtUtc = now, UpdatedAtUtc = now
        };
        // Fail closed if the durable receipt cannot be written.
        _actionJournal.Save(action);
        lock (_stateSync) _latestActions[pageId] = action;
        return action;
    }

    private void FinishDispatch(NIRABrowserActionCheckpoint action,
        string state, IPage page)
    {
        NIRABrowserActionCheckpoint changed = action with
        {
            State = state,
            AfterRoute = page.IsClosed ? string.Empty : NavigationUrlForCognition(page.Url),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        // If persistence fails, the prior Dispatching row remains fail-closed.
        _actionJournal.Save(changed);
        lock (_stateSync)
        {
            _latestActions[action.PageId] = changed;
            if (state == "OutcomeUncertain")
                _uncertainActions[action.PageId] =
                    $"ActionId={action.ActionId:D}; {action.Operation} outcome uncertain; inspect before any retry";
            else
                _uncertainActions.Remove(action.PageId);
        }
    }

    private NIRABrowserActionCheckpoint? LatestAction(Guid pageId)
    {
        lock (_stateSync)
            return _latestActions.TryGetValue(pageId, out NIRABrowserActionCheckpoint? action)
                ? action : null;
    }

    private NIRABrowserInspection StampPostActionInspection(Guid pageId,
        NIRABrowserInspection inspection)
    {
        NIRABrowserActionCheckpoint? action = LatestAction(pageId);
        if (action is not { State: "OutcomeUncertain" })
            return inspection with { DispatchCheckpoint = action };
        NIRABrowserActionCheckpoint observed = action with
        {
            PostActionInspectionId = inspection.InspectionId,
            PostActionInspectedAtUtc = inspection.ObservedAtUtc,
            PostActionContentSha256 = inspection.ContentSha256,
            AfterRoute = NavigationUrlForCognition(inspection.Url),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _actionJournal.Save(observed);
        lock (_stateSync) _latestActions[pageId] = observed;
        // Post-action evidence is useful but never automatically clears an
        // uncertain external side effect or claims the target was unchanged.
        return inspection with { DispatchCheckpoint = observed };
    }

    private void ClearElementRefs(Guid pageId)
    {
        lock (_stateSync)
        {
            _latestElementRefs.Remove(pageId);
            _latestGroundedElements.Remove(pageId);
        }
    }

    private async Task<NIRABrowserPageSnapshot> BuildPageSnapshotAsync(
        Guid pageId,
        IPage page)
    {
        PageOwnership state;
        bool crashed;
        string uncertainAction;
        NIRABrowserNavigationEvidence? navigation;
        lock (_stateSync)
        {
            crashed = _crashedPages.Contains(pageId);
            uncertainAction = _uncertainActions.TryGetValue(pageId, out string? uncertain) ? uncertain : string.Empty;
            navigation = _navigation.TryGetValue(pageId, out NIRABrowserNavigationEvidence? observed) ? observed : null;
            // Click/navigation may close its source page (e.g. popup handoff).
            // Return closed-page evidence rather than converting a performed
            // action into a misleading failed/unknown outcome.
            state = _ownership.TryGetValue(pageId, out PageOwnership? liveOwner)
                ? liveOwner
                : new PageOwnership(string.Empty, null, null, null, false, null);
        }
        if (!page.IsClosed) UpdatePageOrigin(pageId, page.Url);
        return new NIRABrowserPageSnapshot
        {
            PageId = pageId,
            OwnerGoalId = state.GoalId,
            OwnerBranchId = state.BranchId,
            OpenerPageId = state.OpenerPageId,
            RecoveredFromProfile = state.Recovered,
            RequiresExplicitSelection = state.OwnerKey.Length == 0,
            Url = RedactUrlForCognition(page.Url),
            Title = await SafeTitleAsync(page),
            IsClosed = page.IsClosed,
            IsCrashed = crashed,
            UncertainAction = uncertainAction,
            Navigation = navigation,
            Recovery = RecoveryForPage(pageId),
            DispatchCheckpoint = LatestAction(pageId),
            ObservedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task<string> SafeTitleAsync(IPage page)
    {
        try
        {
            return page.IsClosed
                ? string.Empty
                : await page.TitleAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task CloseCoreAsync()
    {
        if (_context != null)
        {
            try
            {
                await _context.CloseAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Browser] CLOSE WARNING | {ex.GetType().Name}: {ex.Message}");
            }
        }

        _context = null;
        _sessionId = Guid.Empty;
        lock (_stateSync)
        {
            _pages.Clear();
            _ownership.Clear();
            _activeByOwner.Clear();
            _pageIds.Clear();
            _pageOrigins.Clear();
            _latestElementRefs.Clear();
            _latestGroundedElements.Clear();
            _navigation.Clear();
            _crashedPages.Clear();
            _uncertainActions.Clear();
            _latestActions.Clear();
            _recoveries.Clear();
            _credentialAttemptRunsByScope.Clear();
            _credentialSubmissions.Clear();
            _lastPageInspection.Clear();
            _trustedRefreshUsed.Clear();
            _queryBearingNavigation.Clear();
        }

        Debug.WriteLine("[Browser] CLOSED");
    }

    private void EnsureOpen()
    {
        if (_context == null || _context.IsClosed)
        {
            throw new InvalidOperationException(
                "No live NIRA browser session is open. Call browser.session.open first.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await _gate.WaitAsync();
        try
        {
            await CloseCoreAsync();
            _playwright?.Dispose();
            _playwright = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    public static Uri ParseHttpUri(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "Browser URLs must be absolute HTTP/HTTPS URLs without embedded credentials.");
        }

        return uri;
    }

    public static string NormalizeHttpMethod(string? value)
    {
        string method =
            string.IsNullOrWhiteSpace(value)
                ? "GET"
                : value.Trim().ToUpperInvariant();

        if (method is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE"))
        {
            throw new InvalidOperationException(
                "Unsupported browser HTTP method.");
        }

        return method;
    }

    private static string NormalizeProfile(string? value)
    {
        string profile =
            string.IsNullOrWhiteSpace(value)
                ? "default"
                : value.Trim();

        if (profile.Length > 40 ||
            !Regex.IsMatch(profile, "^[A-Za-z0-9_-]+$"))
        {
            throw new InvalidOperationException(
                "Browser profile names may contain only letters, numbers, underscore, and hyphen (maximum 40 characters).");
        }

        return profile;
    }

    private static string NormalizeElementRef(string value)
    {
        string clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 80 ||
            !Regex.IsMatch(clean, "^[A-Za-z0-9_-]+$"))
        {
            throw new InvalidOperationException(
                "Use an exact browser element ref returned by the latest browser.inspect result.");
        }

        return clean;
    }

    private static WaitForSelectorState ParseWaitState(string? value)
    {
        return (value?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "visible" => WaitForSelectorState.Visible,
            "hidden" => WaitForSelectorState.Hidden,
            "attached" => WaitForSelectorState.Attached,
            "detached" => WaitForSelectorState.Detached,
            _ => throw new InvalidOperationException(
                "browser.wait state must be visible, hidden, attached, or detached.")
        };
    }

    private static bool IsCredentialHeader(string name)
    {
        return name.Trim().ToLowerInvariant() is
            "authorization" or
            "proxy-authorization" or
            "cookie" or
            "set-cookie" or
            "x-api-key" or
            "api-key" or
            "x-auth-token";
    }

    private static bool IsSensitiveEntry(
        string inputType,
        string autocomplete,
        string? semanticHints = null)
    {
        if (string.Equals(inputType, "password", StringComparison.OrdinalIgnoreCase))
            return true;

        bool sensitiveAutocomplete =
            autocomplete
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(token => token is
                    "current-password" or
                    "new-password" or
                    "one-time-code" or
                    "webauthn" or
                    "cc-number" or
                    "cc-csc" or
                    "cc-exp" or
                    "cc-exp-month" or
                    "cc-exp-year");

        if (sensitiveAutocomplete)
            return true;

        if (string.IsNullOrWhiteSpace(semanticHints))
            return false;

        return Regex.IsMatch(
            semanticHints,
            @"(?ix)\b(password|passwd|passcode|otp|one[-_ ]?time(?:[-_ ]?code)?|verification[-_ ]?code|security[-_ ]?code|pin|cvv|cvc|card[-_ ]?number|credit[-_ ]?card|api[-_ ]?key|access[-_ ]?token|refresh[-_ ]?token|private[-_ ]?key|client[-_ ]?secret)\b");
    }

    public static string RedactUrlForCognition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return value?.Trim() ?? string.Empty;
        }

        UriBuilder builder = new(uri)
        {
            Fragment = string.Empty
        };

        string rawQuery = uri.Query.TrimStart('?');
        if (rawQuery.Length > 0)
        {
            string[] parts = rawQuery
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(
                    part =>
                    {
                        int equals = part.IndexOf('=');
                        string rawName = equals >= 0 ? part[..equals] : part;
                        string decodedName;

                        try
                        {
                            decodedName = Uri.UnescapeDataString(rawName).Trim().ToLowerInvariant();
                        }
                        catch
                        {
                            decodedName = rawName.Trim().ToLowerInvariant();
                        }

                        if (!IsSensitiveQueryName(decodedName))
                            return part;

                        return rawName + "=%5Bredacted%5D";
                    })
                .ToArray();

            builder.Query = string.Join("&", parts);
        }

        return builder.Uri.AbsoluteUri;
    }

    private static bool IsSensitiveQueryName(string name) =>
        name is
            "token" or
            "access_token" or
            "refresh_token" or
            "id_token" or
            "api_key" or
            "apikey" or
            "key" or
            "password" or
            "secret" or
            "signature" or
            "sig" or
            "code" or
            "auth" or
            "authorization";

    private static string SanitizeFileName(string? value)
    {
        string name =
            string.IsNullOrWhiteSpace(value)
                ? "download.bin"
                : value.Trim();

        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        if (name.Length > 180)
        {
            string extension = Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name);
            int available = Math.Max(16, 180 - extension.Length);
            name = stem[..Math.Min(stem.Length, available)] + extension;
        }

        return name;
    }

    public static bool ContainsCredentialLikePayload(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        string trimmed = body.Trim();

        try
        {
            JsonNode? node = JsonNode.Parse(trimmed);
            if (node != null && ContainsSensitiveJsonProperty(node))
                return true;
        }
        catch
        {
            // Non-JSON bodies are checked below as form/query-like payloads.
        }

        return Regex.IsMatch(
            trimmed,
            @"(?ix)(?:^|[&;\s{,])(?:password|passwd|passcode|otp|one[_-]?time[_-]?code|verification[_-]?code|pin|cvv|cvc|card[_-]?number|credit[_-]?card|api[_-]?key|apikey|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|private[_-]?key|csrf[_-]?token|xsrf[_-]?token|session[_-]?id|secret)\s*(?:=|:)");
    }

    private static bool ContainsSensitiveJsonProperty(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                if (IsSensitiveDataKey(pair.Key) && pair.Value != null)
                    return true;

                if (pair.Value != null && ContainsSensitiveJsonProperty(pair.Value))
                    return true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item != null && ContainsSensitiveJsonProperty(item))
                    return true;
            }
        }

        return false;
    }

    private static string RedactStructuredSecretsForCognition(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            JsonNode? node = JsonNode.Parse(text);
            if (node == null)
                return text;

            RedactSensitiveJsonProperties(node);
            return node.ToJsonString();
        }
        catch
        {
            return text;
        }
    }

    private static void RedactSensitiveJsonProperties(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(pair => pair.Key).ToArray())
            {
                JsonNode? value = obj[key];
                if (IsSensitiveDataKey(key))
                {
                    obj[key] = "[redacted]";
                    continue;
                }

                if (value != null)
                    RedactSensitiveJsonProperties(value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (item != null)
                    RedactSensitiveJsonProperties(item);
            }
        }
    }

    private static bool IsSensitiveDataKey(string key)
    {
        string normalized =
            Regex.Replace(key.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_")
                .Trim('_');

        return normalized is
            "password" or "passwd" or "passcode" or "otp" or "one_time_code" or
            "verification_code" or "pin" or "cvv" or "cvc" or "card_number" or
            "credit_card" or "api_key" or "apikey" or "access_token" or
            "refresh_token" or "id_token" or "client_secret" or "private_key" or
            "authorization" or "cookie" or "set_cookie" or "csrf" or "csrf_token" or
            "xsrf" or "xsrf_token" or "sessionid" or "session_id" or "secret" ||
            normalized.EndsWith("_password", StringComparison.Ordinal) ||
            normalized.EndsWith("_secret", StringComparison.Ordinal) ||
            normalized.EndsWith("_token", StringComparison.Ordinal) ||
            normalized.EndsWith("_api_key", StringComparison.Ordinal);
    }

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using SHA256 sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256Hex(string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value ?? string.Empty)))
            .ToLowerInvariant();

    private static List<string> ReadStringArray(
        JsonElement value,
        string property,
        int maximumItems,
        int maximumCharacters)
    {
        List<string> result = [];

        if (!value.TryGetProperty(property, out JsonElement array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (JsonElement item in array.EnumerateArray().Take(Math.Max(0, maximumItems)))
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            string clean = (item.GetString() ?? string.Empty).Trim();
            if (clean.Length == 0)
                continue;

            if (clean.Length > maximumCharacters)
                clean = clean[..maximumCharacters];

            if (!result.Contains(clean, StringComparer.OrdinalIgnoreCase))
                result.Add(clean);
        }

        return result;
    }

    private static string ReadString(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return element.GetString() ?? string.Empty;
    }

    private static bool ReadBoolean(JsonElement value, string property)
    {
        return value.TryGetProperty(property, out JsonElement element) &&
               element.ValueKind == JsonValueKind.True;
    }

    private static string TrimLog(string value)
    {
        string clean =
            RedactUrlForCognition(value)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

        return clean.Length <= 160
            ? clean
            : clean[..157] + "...";
    }

    private const string InspectionScript =
        """
        ({ token, maxElements, maxTextCharacters, maxElementTextCharacters }) => {
          const clean = (value, max) => {
            if (value == null) return '';
            const text = String(value).replace(/\s+/g, ' ').trim();
            return text.length <= max ? text : text.slice(0, Math.max(0, max - 3)) + '...';
          };

          const visible = (el) => {
            if (!(el instanceof Element)) return false;
            const style = getComputedStyle(el);
            if (style.visibility === 'hidden' || style.display === 'none') return false;
            const rect = el.getBoundingClientRect();
            return rect.width > 0 && rect.height > 0;
          };

          const meta = (...selectors) => {
            for (const selector of selectors) {
              const el = document.querySelector(selector);
              const value = el?.getAttribute('content') || el?.getAttribute('href') || '';
              if (value.trim()) return clean(value, 1000);
            }
            return '';
          };

          const roleOf = (el) => {
            const explicit = el.getAttribute('role');
            if (explicit) return explicit;
            const tag = el.tagName.toLowerCase();
            if (tag === 'button') return 'button';
            if (tag === 'a' && el.hasAttribute('href')) return 'link';
            if (tag === 'select') return 'combobox';
            if (tag === 'textarea') return 'textbox';
            if (tag === 'input') {
              const type = (el.getAttribute('type') || 'text').toLowerCase();
              if (type === 'checkbox') return 'checkbox';
              if (type === 'radio') return 'radio';
              if (type === 'button' || type === 'submit' || type === 'reset') return 'button';
              return 'textbox';
            }
            if (el.getAttribute('contenteditable') === 'true') return 'textbox';
            return '';
          };

          const nameOf = (el) => {
            const aria = el.getAttribute('aria-label');
            if (aria) return clean(aria, maxElementTextCharacters);

            const labelledBy = el.getAttribute('aria-labelledby');
            if (labelledBy) {
              const text = labelledBy.split(/\s+/)
                .map(id => document.getElementById(id)?.textContent || '')
                .join(' ');
              if (text.trim()) return clean(text, maxElementTextCharacters);
            }

            if (el.labels && el.labels.length) {
              const text = Array.from(el.labels).map(x => x.textContent || '').join(' ');
              if (text.trim()) return clean(text, maxElementTextCharacters);
            }

            const title = el.getAttribute('title');
            if (title) return clean(title, maxElementTextCharacters);

            const alt = el.getAttribute('alt');
            if (alt) return clean(alt, maxElementTextCharacters);

            const placeholder = el.getAttribute('placeholder');
            if (placeholder) return clean(placeholder, maxElementTextCharacters);

            const text = el.innerText || el.textContent || '';
            if (text.trim()) return clean(text, maxElementTextCharacters);

            const name = el.getAttribute('name');
            if (name) return clean(name, maxElementTextCharacters);

            return '';
          };

          const isSensitiveField = (el, inputType, autocomplete) => {
            const sensitiveAutocomplete = new Set([
              'current-password',
              'new-password',
              'one-time-code',
              'webauthn',
              'cc-number',
              'cc-csc',
              'cc-exp',
              'cc-exp-month',
              'cc-exp-year'
            ]);

            if (inputType === 'password') return true;
            if (autocomplete.split(/\s+/).some(x => sensitiveAutocomplete.has(x))) return true;

            const hints = [
              el.getAttribute('id') || '',
              el.getAttribute('name') || '',
              el.getAttribute('placeholder') || '',
              el.getAttribute('aria-label') || '',
              el.getAttribute('title') || ''
            ].join(' ').toLowerCase();

            return /\b(password|passwd|passcode|otp|one[-_ ]?time(?:[-_ ]?code)?|verification[-_ ]?code|security[-_ ]?code|pin|cvv|cvc|card[-_ ]?number|credit[-_ ]?card|api[-_ ]?key|access[-_ ]?token|refresh[-_ ]?token|private[-_ ]?key|client[-_ ]?secret)\b/i.test(hints);
          };

          const structuredDataTypes = [];
          let structuredPublishedAt = '';
          let structuredModifiedAt = '';
          const addType = (value) => {
            const values = Array.isArray(value) ? value : [value];
            for (const item of values) {
              if (typeof item !== 'string') continue;
              const normalized = clean(item, 120);
              if (normalized && !structuredDataTypes.includes(normalized) && structuredDataTypes.length < 24)
                structuredDataTypes.push(normalized);
            }
          };

          const walkStructured = (node, depth = 0) => {
            if (depth > 8 || node == null) return;
            if (Array.isArray(node)) {
              for (const child of node.slice(0, 60)) walkStructured(child, depth + 1);
              return;
            }
            if (typeof node !== 'object') return;
            if (node['@type']) addType(node['@type']);
            if (!structuredPublishedAt && typeof node.datePublished === 'string')
              structuredPublishedAt = clean(node.datePublished, 200);
            if (!structuredModifiedAt && typeof node.dateModified === 'string')
              structuredModifiedAt = clean(node.dateModified, 200);
            if (node['@graph']) walkStructured(node['@graph'], depth + 1);
          };

          for (const script of Array.from(document.querySelectorAll('script[type="application/ld+json"]')).slice(0, 20)) {
            try {
              walkStructured(JSON.parse(script.textContent || ''));
            } catch {
              // Invalid third-party JSON-LD is ignored; visible DOM remains usable evidence.
            }
          }

          document.querySelectorAll('[data-NIRA-ref]').forEach(el => el.removeAttribute('data-NIRA-ref'));

          const selector = [
            'a[href]',
            'button',
            'input',
            'textarea',
            'select',
            '[role="button"]',
            '[role="link"]',
            '[role="checkbox"]',
            '[role="radio"]',
            '[role="combobox"]',
            '[role="textbox"]',
            '[contenteditable="true"]'
          ].join(',');

          const elements = [];
          let index = 0;
          for (const el of document.querySelectorAll(selector)) {
            if (!visible(el) && !(el.tagName.toLowerCase() === 'input' && (el.getAttribute('type') || '').toLowerCase() === 'file')) continue;
            if (elements.length >= maxElements) break;

            index += 1;
            const ref = `${token}-${index}`;
            el.setAttribute('data-NIRA-ref', ref);

            const tag = el.tagName.toLowerCase();
            const inputType = tag === 'input'
              ? (el.getAttribute('type') || 'text').toLowerCase()
              : '';
            const autocomplete = (el.getAttribute('autocomplete') || '').toLowerCase();
            const sensitiveEntry = isSensitiveField(el, inputType, autocomplete);

            let valuePreview = '';
            if ((tag === 'input' || tag === 'textarea' || tag === 'select') && !sensitiveEntry) {
              valuePreview = clean(el.value || '', maxElementTextCharacters);
            }

            const options = tag === 'select'
              ? Array.from(el.options || []).slice(0, 50).map(o => clean(o.textContent || o.value || '', 120))
              : [];

            elements.push({
              ref,
              role: clean(roleOf(el), 60),
              name: clean(nameOf(el), maxElementTextCharacters),
              tag,
              inputType,
              placeholder: clean(el.getAttribute('placeholder') || '', maxElementTextCharacters),
              href: tag === 'a' ? clean(el.href || '', 1000) : '',
              valuePreview,
              sensitiveEntry,
              disabled: !!el.disabled || el.getAttribute('aria-disabled') === 'true',
              checked: !!el.checked || el.getAttribute('aria-checked') === 'true',
              options
            });
          }

          // Forms and tables are bounded evidence, not website-specific rules.
          // No input values or credential contents are exported by this path.
          const structuredBudget = 9000;
          let structuredUsed = 0;
          let structuredContentTruncated = false;
          const bounded = (value, max) => {
            const text = clean(value, max);
            if (structuredUsed + text.length > structuredBudget) {
              structuredContentTruncated = true;
              return '';
            }
            structuredUsed += text.length;
            return text;
          };
          const forms = [];
          const allForms = Array.from(document.querySelectorAll('form')).filter(visible);
          if (allForms.length > 6) structuredContentTruncated = true;
          for (const form of allForms.slice(0, 6)) {
            const allFields = Array.from(form.querySelectorAll('input,textarea,select'))
              .filter(visible);
            const fields = [];
            if (allFields.length > 16) structuredContentTruncated = true;
            for (const field of allFields.slice(0, 16)) {
              const tag = field.tagName.toLowerCase();
              const kind = tag === 'input'
                ? (field.getAttribute('type') || 'text').toLowerCase() : tag;
              const auto = (field.getAttribute('autocomplete') || '').toLowerCase();
              const sensitiveEntry = isSensitiveField(field, kind, auto);
              fields.push({
                ref: field.getAttribute('data-NIRA-ref') || '',
                label: bounded(nameOf(field), 140),
                name: bounded(field.getAttribute('name') || '', 100),
                kind,
                required: !!field.required,
                disabled: !!field.disabled,
                sensitiveEntry,
                options: tag === 'select' ? Array.from(field.options || [])
                  .slice(0, 12).map(x => bounded(x.textContent || '', 100)) : []
              });
            }
            forms.push({
              name: bounded(form.getAttribute('aria-label') || form.getAttribute('name') || '', 140),
              method: bounded(form.getAttribute('method') || 'GET', 15),
              action: bounded(form.action || '', 500),
              fields,
              truncated: allFields.length > fields.length
            });
          }

          const tables = [];
          const allTables = Array.from(document.querySelectorAll('table')).filter(visible);
          if (allTables.length > 6) structuredContentTruncated = true;
          for (const table of allTables.slice(0, 6)) {
            const allRows = Array.from(table.querySelectorAll('tr')).filter(visible);
            const rows = [];
            const first = allRows[0];
            const headerRow = Array.from(table.querySelectorAll('thead tr')).find(visible) ||
              (first && first.querySelector('th') ? first : null);
            const headers = headerRow ? Array.from(headerRow.children)
              .filter(cell => cell.matches('th,td')).slice(0, 10)
              .map(cell => bounded(cell.innerText || cell.textContent || '', 120)) : [];
            const dataRows = headerRow ? allRows.filter(row => row !== headerRow) : allRows;
            if (dataRows.length > 25) structuredContentTruncated = true;
            for (const row of dataRows.slice(0, 25)) {
              const cells = Array.from(row.children).filter(cell => cell.matches('th,td'));
              if (cells.length > 10) structuredContentTruncated = true;
              rows.push(cells.slice(0, 10).map(cell =>
                bounded(cell.innerText || cell.textContent || '', 120)));
            }
            tables.push({
              caption: bounded(table.caption?.innerText || table.getAttribute('aria-label') || '', 160),
              headers,
              rows,
              truncated: dataRows.length > rows.length
            });
          }

          const article = document.querySelector('article');
          const main = document.querySelector('main, [role="main"]');
          const articleText = clean(article?.innerText || '', maxTextCharacters);
          const mainText = clean(main?.innerText || '', maxTextCharacters);
          const bodyText = clean(document.body?.innerText || '', maxTextCharacters);

          let text = bodyText;
          let textSource = 'body';
          if (articleText.length >= 240) {
            text = articleText;
            textSource = 'article';
          } else if (mainText.length >= 240) {
            text = mainText;
            textSource = 'main';
          }

          const canonicalUrl = document.querySelector('link[rel="canonical"]')?.href || '';
          const siteName = meta('meta[property="og:site_name"]', 'meta[name="application-name"]');
          const language = clean(document.documentElement?.lang || '', 80);
          const publishedAt = meta(
            'meta[property="article:published_time"]',
            'meta[name="article:published_time"]',
            'meta[name="date"]',
            'meta[name="datePublished"]',
            'meta[itemprop="datePublished"]') || structuredPublishedAt;
          const modifiedAt = meta(
            'meta[property="article:modified_time"]',
            'meta[name="article:modified_time"]',
            'meta[name="last-modified"]',
            'meta[name="dateModified"]',
            'meta[itemprop="dateModified"]') || structuredModifiedAt;

          return {
            text,
            textSource,
            canonicalUrl: clean(canonicalUrl, 1200),
            siteName,
            language,
            publishedAt,
            modifiedAt,
            structuredDataTypes,
            elements,
            forms,
            tables,
            structuredContentTruncated
          };
        }
        """;
}


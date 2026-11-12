using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

/// <summary>
/// Phase 4G single-batch capability bridge. V2 owns decisions and run checkpoints;
/// existing NIRA owns each service/authorization/audit. Every target is fixed by
/// the CURRENT user message, not selected from history or arbitrary model args.
/// </summary>
public interface ILegacyPhase4GExecutor
{
    Task<NIRACapabilityResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken ct);
}

public sealed class NIRALegacyPhase4GExecutor(NIRACapabilityService service) : ILegacyPhase4GExecutor
{
    private readonly NIRACapabilityService _service = service ?? throw new ArgumentNullException(nameof(service));
    public async Task<NIRACapabilityResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        IReadOnlyList<NIRACapabilityResult> results = await _service.ExecuteAsync([request], ct);
        if (results.Count != 1) throw new InvalidDataException("Unexpected legacy action receipt count.");
        return results[0];
    }
}

public static class LegacyPhase4GRegistration
{
    public const string BrowserConsent = "ALLOW_EXPLICIT_BROWSER_OBSERVATION";
    public const string DocxInspectConsent = "ALLOW_EXPLICIT_DOCX_INSPECTION";
    public const string DocxCreateConsent = "ALLOW_EXPLICIT_DOCX_CREATE";
    public const string DesktopConsent = "ALLOW_EXPLICIT_DESKTOP_UI_INSPECTION";

    private static readonly Regex AddressPattern = new("(?<![A-Za-z0-9])https://[^\\s\"'<>]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
    private static readonly Regex QuotedContent = new("\\bcontaining\\s+\\\"(?<text>[^\\\"\\r\\n]{1,400})\\\"",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
    // Do not parse command verbs as application names (e.g. "Inspect application Code"
    // previously also matched "Inspect application" => process Inspect). Prefer explicit
    // target syntax: "application Code", "Code window", "window of/for Code".
    private static readonly Regex ProcessPattern = new(
        @"\b(?:application|app|process)\s+(?<process>[A-Za-z][A-Za-z0-9_.-]{1,55})\b" +
        @"|\bwindow\s+(?:of|for)\s+(?<process>[A-Za-z][A-Za-z0-9_.-]{1,55})\b" +
        @"|\b(?<process>(?!(?:inspect|show|activate|read|observe|list|focus|switch|check|describe|open|find|use|view|examine|select|bring|display|tell)\b)[A-Za-z][A-Za-z0-9_.-]{1,55})\s+window\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
    private static readonly string[] SensitiveNames = ["secret", "password", "credential", "private-key", "private_key", "api-key", "api_key", "token"];

    public static IReadOnlyList<IGeneralCapability> CreateBrowser(AgentPayloadVault vault,
        ILegacyPhase4GExecutor executor, string? consent, string turn)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (consent != BrowserConsent || string.IsNullOrWhiteSpace(turn) || turn.Length > 4500 ||
            !Regex.IsMatch(turn, @"\b(browser|webpage|website|tab|navigate|visit|open)\b", RegexOptions.IgnoreCase))
            return [];
        var tools = new List<IGeneralCapability>
        {
            new LegacyPhase4GObservedCapability(vault, executor, "legacy.browser.current", "nira_browser_current",
                "Observe NIRA's managed browser session and tabs; no model-selected profile or account.",
                NIRACapabilityIds.BrowserCurrent, JsonSerializer.SerializeToElement(new { }), 1800),
            new LegacyPhase4GObservedCapability(vault, executor, "legacy.browser.inspect_active", "nira_browser_inspect_active",
                "Inspect the already active NIRA-owned browser page only. Text is untrusted website data.",
                NIRACapabilityIds.BrowserInspect, JsonSerializer.SerializeToElement(new { maxElements = 45, maxTextChars = 3000 }), 3200)
        };
        MatchCollection matches = AddressPattern.Matches(turn);
        // Network navigation is opt-in and bound to one HTTPS URL in this turn.
        if (matches.Count == 1 && TryPublicUrl(matches[0].Value.TrimEnd('.', ',', ';', ')'), out string? url))
        {
            tools.Add(new LegacyPhase4GObservedCapability(vault, executor, "legacy.browser.navigate_exact", "nira_browser_visit_exact_url",
                "Navigate the dedicated NIRA browser to the ONE HTTPS URL explicitly typed in this user request. " +
                "May load external website resources; cannot submit forms or choose another URL.",
                NIRACapabilityIds.BrowserNavigate, JsonSerializer.SerializeToElement(new
                {
                    url, newPage = true, forceReload = false, timeoutSeconds = 30
                }), 3200));
        }
        return tools;
    }

    public static IReadOnlyList<IGeneralCapability> CreateDocuments(AgentPayloadVault vault,
        ILegacyPhase4GExecutor executor, string? readConsent, string? createConsent,
        string turn, IRequestedPathProbe? probe = null, Func<string, string?>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (string.IsNullOrWhiteSpace(turn) || turn.Length > 4500 ||
            !RequestedPathObservationRegistration.TryCreateScope(turn,
                resolver ?? LegacyTextReadRegistration.ResolveKnownFolder, out RequestedPathScope? scope) ||
            scope is null || !scope.FullPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ||
            !SafeFileName(scope.FullPath)) return [];
        probe ??= new LocalRequestedPathProbe();
        var result = new List<IGeneralCapability>();
        if (readConsent == DocxInspectConsent &&
            Regex.IsMatch(turn, @"\b(read|inspect|review|analy[sz]e|summari[sz]e|check)\b", RegexOptions.IgnoreCase) &&
            probe.Observe(scope, CancellationToken.None) == RequestedPathStatus.File)
        {
            try
            {
                if (new FileInfo(scope.FullPath).Length is > 0 and <= 16 * 1024 * 1024)
                    result.Add(new LegacyPhase4GObservedCapability(vault, executor,
                        "legacy.docx.inspect_exact", "nira_inspect_exact_docx",
                        "Inspect the ONE Word document named in this current request (bounded preview). " +
                        "Private document preview may be sent to the selected cloud model provider only under independent consent.",
                        NIRACapabilityIds.DocxInspect, JsonSerializer.SerializeToElement(new { path = scope.FullPath }),
                        3200, () => probe.Observe(scope, CancellationToken.None) == RequestedPathStatus.File,
                        scope.FullPath, scope.UserLabel));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        if (createConsent == DocxCreateConsent &&
            Regex.IsMatch(turn, @"\b(create|make)\b", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(turn, @"\b(?:do not|don't|never)\s+(?:create|make)\b", RegexOptions.IgnoreCase))
        {
            MatchCollection contents = QuotedContent.Matches(turn);
            if (contents.Count == 1 && contents[0].Groups["text"].Value is string text &&
                !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl))
            {
                var capability = new LegacyPhase4GDocxCreateCapability(vault, executor, scope, text, probe);
                if (capability.IsSafeToCreate(CancellationToken.None)) result.Add(capability);
            }
        }
        return result;
    }

    public static IReadOnlyList<IGeneralCapability> CreateDesktopUi(AgentPayloadVault vault,
        ILegacyPhase4GExecutor executor, string? consent, string turn)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (consent != DesktopConsent || string.IsNullOrWhiteSpace(turn) || turn.Length > 2500 ||
            !Regex.IsMatch(turn, @"\b(inspect|show|read|observe|controls|accessibility|window|activate|focus|switch)\b", RegexOptions.IgnoreCase))
            return [];
        MatchCollection matches = ProcessPattern.Matches(turn);
        var names = matches.Cast<Match>().Select(m => m.Groups["process"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length != 1 || SensitiveNames.Any(x => names[0].Contains(x, StringComparison.OrdinalIgnoreCase)))
            return [];
        string process = names[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? names[0][..^4] : names[0];
        if (process.Length is < 2 or > 56) return [];
        var shared = new LegacyPhase4GDesktopWindowScope(process);
        var tools = new List<IGeneralCapability>
        {
            new LegacyPhase4GObservedCapability(vault, executor, "legacy.desktop.inspect_exact", "nira_desktop_inspect_named_app",
                "Observe UI Automation control names of the ONE application process named in this user request. " +
                "Window/control text may be shared with the selected cloud model provider, with explicit desktop UI consent.",
                NIRACapabilityIds.DesktopInspect, JsonSerializer.SerializeToElement(new { processName = process }),
                3200, onVerifiedOutput: shared.AcceptObservation),
            new LegacyPhase4GObservedCapability(vault, executor, "legacy.vision.accessibility_exact", "nira_accessibility_inspect_named_app",
                "Read the accessibility tree for the ONE user-named application. No screenshots, clicks or typing.",
                NIRACapabilityIds.VisionAccessibility, JsonSerializer.SerializeToElement(new { processName = process }), 3200)
        };
        if (Regex.IsMatch(turn, @"\b(activate|focus|switch to)\b", RegexOptions.IgnoreCase))
            tools.Add(new LegacyPhase4GDesktopActivateCapability(vault, executor, shared));
        return tools;
    }

    // Host-owned admission feedback. This only checks explicit CURRENT-turn intent
    // and the actual registered tools. It never invents access or adds a grant.
    public static string? ExplainUnavailableFileAction(string turn, bool textCreateRegistered,
        bool docxInspectionRegistered, bool docxCreateRegistered)
    {
        if (string.IsNullOrWhiteSpace(turn) || turn.Length > 4500) return null;
        bool create = Regex.IsMatch(turn, @"\b(create|make)\b", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(turn, @"\b(?:do not|don't|never)\s+(?:create|make)\b", RegexOptions.IgnoreCase);
        bool inspect = Regex.IsMatch(turn, @"\b(read|inspect|review|summari[sz]e|analy[sz]e|check)\b",
            RegexOptions.IgnoreCase);
        bool docxPath = Regex.IsMatch(turn,
            @"\b(?:Documents|Desktop|Downloads|Pictures|Music|Videos)[\\/][A-Za-z0-9_+./\\-]+\.docx\b",
            RegexOptions.IgnoreCase);
        bool txtPath = Regex.IsMatch(turn,
            @"\b(?:Documents|Desktop|Downloads|Pictures|Music|Videos)[\\/][A-Za-z0-9_+./\\-]+\.txt\b",
            RegexOptions.IgnoreCase);
        if (create && docxPath && !docxCreateRegistered)
            return "I did not create or overwrite that Word document. The exact new-file " +
                "capability was not available for this request. Check whether the destination " +
                "already exists, the parent folder is accessible, and -AllowDocxCreate is enabled.";
        if (create && txtPath && !textCreateRegistered)
            return "I did not create that text file. NIRA could not register a safe " +
                "create-only action for this request. Check whether the destination already " +
                "exists, its parent folder is accessible, and -AllowTextCreate is enabled.";
        if (inspect && docxPath && !docxInspectionRegistered)
            return "I could not inspect the named Word document: the exact-file read " +
                "capability was not available. The file may not exist yet or may be " +
                "inaccessible; also check -AllowDocxInspection. I did not open the file.";
        return null;
    }

    internal static bool TryPublicUrl(string candidate, out string? normalized)
    {
        normalized = null;
        if (candidate.Length > 2048 || !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrWhiteSpace(uri.UserInfo) ||
            !string.IsNullOrWhiteSpace(uri.Query) || !string.IsNullOrWhiteSpace(uri.Fragment) ||
            uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.') ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        normalized = uri.AbsoluteUri;
        return true;
    }
    private static bool SafeFileName(string path)
    {
        string name = Path.GetFileName(path);
        return name.Length is >= 6 and <= 80 && !name.StartsWith('.') &&
            !SensitiveNames.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Only supports an EMPTY model argument object. The host chooses all real arguments.</summary>
public sealed class LegacyPhase4GObservedCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyPhase4GExecutor _executor;
    private readonly NIRACapabilityRequest _request;
    private readonly int _maxOutput;
    private readonly Func<bool>? _preflight;
    private readonly string? _privatePath;
    private readonly string? _safePath;
    private readonly Action<string>? _onVerifiedOutput;
    private int _navigationDispatched;

    public LegacyPhase4GObservedCapability(AgentPayloadVault vault, ILegacyPhase4GExecutor executor,
        string id, string function, string description, string legacyId, JsonElement hostArguments,
        int maxOutput, Func<bool>? preflight = null, string? privatePath = null, string? safePath = null,
        Action<string>? onVerifiedOutput = null)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _request = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(), CapabilityId = legacyId, Arguments = hostArguments,
            Reason = "NIRA V2 Phase 4G exact host-selected observation"
        };
        _maxOutput = Math.Clamp(maxOutput, 256, 4000);
        _preflight = preflight;
        _privatePath = privatePath;
        _safePath = safePath;
        _onVerifiedOutput = onVerifiedOutput;
        Spec = new(id, description, AccessLevel.Observe);
        FunctionName = function;
    }
    public AgentToolSpec Spec { get; }
    public string FunctionName { get; }
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) => args.ValueKind == JsonValueKind.Object &&
        !args.EnumerateObject().Any();
    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!ValidateArguments(args)) return AgentToolReceipt.Uncertain("G4_MODEL_ARGUMENTS_NOT_ALLOWED");
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        { return AgentToolReceipt.Uncertain("G4_INVALID_ARGUMENT_REFERENCE"); }
        if (_preflight is not null && !_preflight())
            return AgentToolReceipt.Uncertain("G4_SCOPE_NOT_SAFE");
        // A network navigation is not replayed blindly even if the response fails.
        if (_request.CapabilityId == NIRACapabilityIds.BrowserNavigate &&
            Interlocked.Exchange(ref _navigationDispatched, 1) != 0)
            return AgentToolReceipt.Uncertain("G4_BROWSER_NAVIGATION_ALREADY_ATTEMPTED");
        NIRACapabilityResult receipt;
        try { receipt = await _executor.ExecuteAsync(_request, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or IOException or
            UnauthorizedAccessException or ArgumentException)
        { return AgentToolReceipt.Uncertain("G4_LEGACY_EXECUTION_FAILED"); }
        if (receipt is null || receipt.RequestId != _request.RequestId ||
            receipt.CapabilityId != _request.CapabilityId || receipt.Risk != NIRACapabilityRisk.Observe ||
            receipt.ChangedSystemState || receipt.OutcomeUncertain || !receipt.AuditRecorded)
            return AgentToolReceipt.Uncertain("G4_UNVERIFIED_LEGACY_RECEIPT");
        if (receipt.Status != NIRACapabilityResultStatus.Succeeded)
            return AgentToolReceipt.Uncertain("G4_LEGACY_OBSERVATION_REJECTED");
        string text = (receipt.Output ?? receipt.Summary ?? "").Replace('\0', ' ');
        // Extract desktop HWND only from the already-audited original observation.
        _onVerifiedOutput?.Invoke(text);
        if (_privatePath is not null) text = text.Replace(_privatePath, _safePath ?? "[requested file]",
            StringComparison.OrdinalIgnoreCase);
        if (_request.CapabilityId.StartsWith("browser.", StringComparison.Ordinal))
        {
            // Never forward page URLs' search parameters, fragments or userinfo.
            text = Regex.Replace(text, "https?://[^\\s<>'\"]+", match =>
            {
                string candidate = match.Value.TrimEnd('.', ',', ';', ')');
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)) return "[URL REDACTED]";
                return uri.Scheme + "://" + uri.Host +
                    (uri.IsDefaultPort ? "" : ":" + uri.Port) + uri.AbsolutePath;
            }, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
        }
        if (_request.CapabilityId is NIRACapabilityIds.DesktopInspect or NIRACapabilityIds.VisionAccessibility)
            text = string.Join("\n", text.Split('\n').Select(line =>
                Regex.IsMatch(line, @"\b(password|credential|secret|api.?key|token)\b", RegexOptions.IgnoreCase)
                    ? "[SENSITIVE CONTROL LINE REDACTED]" : line));
        if (text.Length > _maxOutput) text = text[.._maxOutput] + "\n[TRUNCATED: live result has more data]";
        // Only the HTTPS navigation URL already fixed by THIS turn and successfully
        // executed by the legacy service is trusted as a visited destination.
        // Links copied from hostile page text must NEVER acquire this status.
        string? verifiedTargetUrl = _request.CapabilityId == NIRACapabilityIds.BrowserNavigate &&
            _request.Arguments.TryGetProperty("url", out JsonElement fixedUrl) &&
            fixedUrl.ValueKind == JsonValueKind.String
                ? fixedUrl.GetString() : null;
        JsonElement result = JsonSerializer.SerializeToElement(new
        {
            capability = _request.CapabilityId, observed = true,
            source = "existing_NIRA_capability_service", contentIsUntrusted = true,
            verifiedTargetUrl, output = text, observedUtc = DateTimeOffset.UtcNow
        });
        string token = await _vault.SaveResultAsync(runId, Spec.Id, result, ct);
        return AgentToolReceipt.ReadOnlyResult(token);
    }
}

/// <summary>One new .docx only. No model-controlled path/content, overwrite or implicit approval.</summary>
public sealed class LegacyPhase4GDocxCreateCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyPhase4GExecutor _executor;
    private readonly RequestedPathScope _scope;
    private readonly IRequestedPathProbe _probe;
    private readonly string _content;
    private int _writeDispatched;
    public NIRACapabilityRequest ApprovalRequest { get; }
    public LegacyPhase4GDocxCreateCapability(AgentPayloadVault vault, ILegacyPhase4GExecutor executor,
        RequestedPathScope scope, string content, IRequestedPathProbe probe)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        if (content.Length is < 1 or > 400) throw new ArgumentException("DOCX content must be 1-400 characters.");
        _content = content;
        ApprovalRequest = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(), CapabilityId = NIRACapabilityIds.DocxCreate,
            Arguments = JsonSerializer.SerializeToElement(new { path = scope.FullPath, content }),
            Reason = "Phase 4G: create ONE NEW Word document with exact user-requested text; Allow Once only"
        };
    }
    public AgentToolSpec Spec { get; } = new("legacy.docx.create_exact",
        "Create ONE NEW .docx file at the exact current-turn path with exact quoted text, " +
        "only after owner Allow Once approval. Never replace existing Word documents.", AccessLevel.Modify);
    public string FunctionName => "nira_create_exact_word_document";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) => args.ValueKind == JsonValueKind.Object &&
        !args.EnumerateObject().Any();
    public bool IsSafeToCreate(CancellationToken ct)
    {
        if (_scope.ChildSegments.Count < 2 || !string.Equals(Path.GetExtension(_scope.FullPath), ".docx",
                StringComparison.OrdinalIgnoreCase) ||
            _probe.Observe(_scope, ct) != RequestedPathStatus.Missing) return false;
        string parentPath = Path.GetDirectoryName(_scope.FullPath)!;
        var parentSegments = _scope.ChildSegments.Take(_scope.ChildSegments.Count - 1).ToArray();
        var parent = new RequestedPathScope(_scope.UserLabel[.._scope.UserLabel.LastIndexOf('/')],
            _scope.KnownFolderRoot, parentPath, parentSegments);
        return _probe.Observe(parent, ct) == RequestedPathStatus.Directory;
    }
    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            if (!ValidateArguments(await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct)))
                return AgentToolReceipt.Uncertain("G4_MODEL_DOCX_ARGUMENT_REJECTED");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { return AgentToolReceipt.Uncertain("G4_INVALID_DOCX_REFERENCE"); }
        if (!IsSafeToCreate(ct)) return AgentToolReceipt.Uncertain("G4_DOCX_DESTINATION_EXISTS_OR_UNSAFE");
        if (Interlocked.Exchange(ref _writeDispatched, 1) != 0)
            return AgentToolReceipt.Uncertain("G4_DOCX_ALREADY_DISPATCHED");
        NIRACapabilityResult receipt;
        try { receipt = await _executor.ExecuteAsync(ApprovalRequest, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException or UnauthorizedAccessException)
        { return AgentToolReceipt.Uncertain("G4_DOCX_WRITE_REQUIRES_RECONCILIATION"); }
        if (receipt is null || receipt.RequestId != ApprovalRequest.RequestId ||
            receipt.CapabilityId != NIRACapabilityIds.DocxCreate || receipt.Risk != NIRACapabilityRisk.Modify ||
            receipt.Status != NIRACapabilityResultStatus.Succeeded || !receipt.AuditRecorded ||
            !receipt.ChangedSystemState || receipt.OutcomeUncertain)
            return AgentToolReceipt.Uncertain("G4_DOCX_RECEIPT_UNCONFIRMED");
        try
        {
            if (_probe.Observe(_scope, ct) != RequestedPathStatus.File)
                return AgentToolReceipt.Uncertain("G4_DOCX_DESTINATION_NOT_SAFE");
            byte[] bytes = await File.ReadAllBytesAsync(_scope.FullPath, ct);
            if (bytes.Length is < 200 or > 16 * 1024 * 1024)
                return AgentToolReceipt.Uncertain("G4_DOCX_OUTPUT_SIZE_INVALID");
            using var stream = new MemoryStream(bytes, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null || entry.Length is <= 0 or > 2 * 1024 * 1024)
                return AgentToolReceipt.Uncertain("G4_DOCX_DOCUMENT_XML_MISSING");
            using var source = entry.Open();
            using var reader = System.Xml.XmlReader.Create(source, new System.Xml.XmlReaderSettings
            {
                DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024
            });
            XDocument document = XDocument.Load(reader);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            string readBack = string.Concat(document.Descendants(w + "t").Select(e => e.Value));
            if (readBack != _content)
                return AgentToolReceipt.Uncertain("G4_DOCX_CONTENT_MISMATCH");
            JsonElement result = JsonSerializer.SerializeToElement(new
            {
                action = "docx_create_exact_verified", path = _scope.UserLabel,
                sizeBytes = bytes.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                independentReadBack = true, observedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, result, ct);
            return AgentToolReceipt.Verified(token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or
            System.Xml.XmlException or ArgumentException)
        { return AgentToolReceipt.Uncertain("G4_DOCX_POSTWRITE_RECONCILIATION_REQUIRED"); }
    }
}


/// <summary>One run, one process, one HWND observed from an audited desktop.inspect receipt.</summary>
public sealed class LegacyPhase4GDesktopWindowScope(string processName)
{
    public string ProcessName { get; } = processName;
    public string? ObservedHandle { get; private set; }
    public void AcceptObservation(string output)
    {
        MatchCollection matches = Regex.Matches(output,
            @"(?im)^WindowHandle:\s*(0x[0-9A-F]{1,16})\s*$", RegexOptions.IgnoreCase);
        // Never guess when a snapshot has ambiguous or multiple window identities.
        if (matches.Count == 1) ObservedHandle = matches[0].Groups[1].Value;
    }
}

/// <summary>Activates only the HWND observed for the exact user-named process.
/// No model-proposed HWND, key, click, input text or desktop action is accepted.</summary>
public sealed class LegacyPhase4GDesktopActivateCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly ILegacyPhase4GExecutor _executor;
    private readonly LegacyPhase4GDesktopWindowScope _scope;
    private NIRACapabilityRequest? _prepared;
    private int _activationDispatched;
    public LegacyPhase4GDesktopActivateCapability(AgentPayloadVault vault,
        ILegacyPhase4GExecutor executor, LegacyPhase4GDesktopWindowScope scope)
    { _vault = vault; _executor = executor; _scope = scope; }
    public AgentToolSpec Spec { get; } = new("legacy.desktop.activate_exact",
        "Activate only the specific existing app window verified by desktop.inspect in THIS run. " +
        "Requires an independent Allow Once UI approval. No clicking, text entry, closing or shell.", AccessLevel.Modify);
    public string FunctionName => "nira_activate_observed_window";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false });
    public bool ValidateArguments(JsonElement args) => args.ValueKind == JsonValueKind.Object &&
        !args.EnumerateObject().Any();
    public NIRACapabilityRequest? ApprovalRequest
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_scope.ObservedHandle)) return null;
            if (_prepared is not null) return _prepared;
            _prepared = new NIRACapabilityRequest
            {
                RequestId = Guid.NewGuid(), CapabilityId = NIRACapabilityIds.DesktopAct,
                Arguments = JsonSerializer.SerializeToElement(new
                {
                    windowHandle = _scope.ObservedHandle,
                    processName = _scope.ProcessName,
                    action = "activate"
                }),
                Reason = "Phase 4G exact HWND activation after UIA observation; allow once only"
            };
            return _prepared;
        }
    }
    public bool IsSafeToExecute()
    {
        NIRACapabilityRequest? request = _prepared;
        return request is not null &&
            string.Equals(_scope.ObservedHandle,
                request.Arguments.GetProperty("windowHandle").GetString(), StringComparison.OrdinalIgnoreCase);
    }
    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            if (!ValidateArguments(await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct)))
                return AgentToolReceipt.Uncertain("G4_MODEL_DESKTOP_ARGUMENT_REJECTED");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or UnauthorizedAccessException)
        { return AgentToolReceipt.Uncertain("G4_INVALID_DESKTOP_REFERENCE"); }
        if (!IsSafeToExecute()) return AgentToolReceipt.Uncertain("G4_DESKTOP_WINDOW_NOT_OBSERVED");
        if (Interlocked.Exchange(ref _activationDispatched, 1) != 0)
            return AgentToolReceipt.Uncertain("G4_DESKTOP_ACTIVATION_ALREADY_DISPATCHED");
        NIRACapabilityResult result;
        try { result = await _executor.ExecuteAsync(_prepared!, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or UnauthorizedAccessException)
        { return AgentToolReceipt.Uncertain("G4_DESKTOP_ACTION_REQUIRES_RECONCILIATION"); }
        if (result is not null && result.RequestId == _prepared!.RequestId &&
            result.CapabilityId == NIRACapabilityIds.DesktopAct &&
            result.Risk == NIRACapabilityRisk.Modify && result.AuditRecorded &&
            result.Status == NIRACapabilityResultStatus.Failed &&
            !result.ChangedSystemState && !result.OutcomeUncertain)
            return AgentToolReceipt.ConfirmedNoChange("G4_DESKTOP_ACTIVATION_NO_EFFECT");
        if (result is null || result.RequestId != _prepared!.RequestId ||
            result.CapabilityId != NIRACapabilityIds.DesktopAct ||
            result.Risk != NIRACapabilityRisk.Modify ||
            result.Status != NIRACapabilityResultStatus.Succeeded ||
            !result.ChangedSystemState || !result.AuditRecorded || result.OutcomeUncertain ||
            !result.Output.Contains("ActionPostconditionVerified: True", StringComparison.OrdinalIgnoreCase))
            return AgentToolReceipt.Uncertain("G4_DESKTOP_ACTIVATION_UNCONFIRMED");
        var evidence = JsonSerializer.SerializeToElement(new
        {
            action = "activate_existing_window", process = _scope.ProcessName,
            observedWindowVerified = true, userApprovalRequired = true,
            changedSystemState = true, observedUtc = DateTimeOffset.UtcNow
        });
        string resultToken = await _vault.SaveResultAsync(runId, Spec.Id, evidence, ct);
        return AgentToolReceipt.Verified(resultToken);
    }
}

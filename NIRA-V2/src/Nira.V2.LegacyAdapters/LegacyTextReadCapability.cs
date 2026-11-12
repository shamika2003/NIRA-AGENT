using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

/// <summary>
/// Temporary anti-corruption boundary: V2 owns model decisions/checkpoints,
/// legacy NIRA remains the authoritative executor, scope authorizer and auditor.
/// THIS adapter never creates its own file reader or independent permission store.
/// </summary>
public interface ILegacyTextReadExecutor
{
    Task<NIRACapabilityResult> ExecuteReadAsync(NIRACapabilityRequest request, CancellationToken ct);
}

public sealed class NIRALegacyTextReadExecutor(NIRACapabilityService service) : ILegacyTextReadExecutor
{
    private readonly NIRACapabilityService _service = service ?? throw new ArgumentNullException(nameof(service));

    public async Task<NIRACapabilityResult> ExecuteReadAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        IReadOnlyList<NIRACapabilityResult> results = await _service.ExecuteAsync([request], ct);
        if (results.Count != 1)
            throw new InvalidDataException("Legacy capability returned an unexpected result count.");
        return results[0];
    }
}

public static class LegacyTextReadRegistration
{
    // Separate explicit cloud-content consent; does NOT imply authorization to open anything.
    public const string ConsentValue = "ALLOW_EXPLICIT_TEXT_READ";
    private const long MaxFileBytes = 512 * 1024;
    // This is a grant condition, not a model router. The model still chooses whether
    // any registered capability is helpful. Merely naming a path is not read consent.
    private static readonly Regex ExplicitReadIntent = new(
        @"\b(read|summari[sz]e|review|analy[sz]e|show|explain|inspect|contents|content)\b|\bwhat(?:'s| is) inside\b|\bwhat does\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(150));
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".cs", ".csproj", ".xml", ".json", ".yaml", ".yml",
        ".py", ".java", ".js", ".ts", ".css", ".html", ".log", ".csv", ".ps1"
    };
    private static readonly string[] SensitiveMarkers =
    [
        "password", "secret", "credential", "private-key", "private_key", "api-key", "api_key", "token"
    ];

    public static bool IsConsented(string? consent) =>
        string.Equals(consent, ConsentValue, StringComparison.Ordinal);

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyTextReadExecutor executor, string? consent, string currentUserTurn,
        IRequestedPathProbe? probe = null, Func<string, string?>? rootResolver = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (!IsConsented(consent) || string.IsNullOrWhiteSpace(currentUserTurn) ||
            currentUserTurn.Length > 5000 || !ExplicitReadIntent.IsMatch(currentUserTurn))
            return [];
        // Exact current-user-turn path is host-selected; memory/model/tool text never grants scope.
        if (!RequestedPathObservationRegistration.TryCreateScope(currentUserTurn,
                rootResolver ?? ResolveKnownFolder, out RequestedPathScope? scope) || scope is null)
            return [];
        if (!IsAllowlistedTextName(scope.FullPath)) return [];
        return [new LegacyTextReadCapability(vault, scope, executor,
            probe ?? new LocalRequestedPathProbe(), MaxFileBytes)];
    }

    // Use the exact same Windows known-folder resolver used by scoped metadata;
    // never build paths from the hard-coded C:\Users\user string.
    internal static string? ResolveKnownFolder(string alias)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // Public lookup in the shared host policy; no filesystem probing performed here.
        if (alias.Equals("Documents", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (alias.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (alias.Equals("Pictures", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (alias.Equals("Music", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (alias.Equals("Videos", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (!alias.Equals("Downloads", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            string? entry = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
            if (string.IsNullOrWhiteSpace(entry)) return null;
            string expanded = Environment.ExpandEnvironmentVariables(entry);
            return Path.IsPathFullyQualified(expanded) ? Path.GetFullPath(expanded) : null;
        }
        catch (System.Security.SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public static bool IsAllowlistedTextName(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Extensions.Contains(Path.GetExtension(path)))
            return false;
        string name = Path.GetFileName(path);
        if (name.StartsWith(".", StringComparison.Ordinal) ||
            SensitiveMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }
}

public sealed class LegacyTextReadCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly RequestedPathScope _scope;
    private readonly ILegacyTextReadExecutor _executor;
    private readonly IRequestedPathProbe _probe;
    private readonly long _maxFileBytes;

    public LegacyTextReadCapability(AgentPayloadVault vault, RequestedPathScope scope,
        ILegacyTextReadExecutor executor, IRequestedPathProbe probe, long maxFileBytes = 512 * 1024)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _maxFileBytes = maxFileBytes;
    }

    public AgentToolSpec Spec { get; } = new("legacy.filesystem.read_exact",
        "Read the beginning of exactly ONE allowlisted text file explicitly named in the CURRENT " +
        "user message. The host fixes the path and NIRA's existing permission/audit service " +
        "authorizes and performs the read. NO input arguments, file writes, directory listing " +
        "or model-selected paths. Content may be sent to the selected cloud model provider only under separate consent.",
        AccessLevel.Observe);
    public string FunctionName => "nira_read_exact_user_text_file";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) =>
        args.ValueKind == JsonValueKind.Object && !args.EnumerateObject().Any();

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        JsonElement arguments;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            arguments = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or UnauthorizedAccessException)
        {
            return AgentToolReceipt.Uncertain("INVALID_SCOPED_ARGUMENT");
        }
        if (!ValidateArguments(arguments))
            return AgentToolReceipt.Uncertain("PATH_ARGUMENTS_NOT_ALLOWED");
        if (!LegacyTextReadRegistration.IsAllowlistedTextName(_scope.FullPath))
            return AgentToolReceipt.Uncertain("FILE_TYPE_NOT_ALLOWED");
        if (_probe.Observe(_scope, ct) != RequestedPathStatus.File)
            return AgentToolReceipt.Uncertain("FILE_NOT_SAFE_OR_MISSING");
        try
        {
            var info = new FileInfo(_scope.FullPath);
            if (info.Length > _maxFileBytes || info.Length < 0)
                return AgentToolReceipt.Uncertain("TEXT_FILE_TOO_LARGE");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return AgentToolReceipt.Uncertain("TEXT_FILE_UNAVAILABLE");
        }
        // The model never supplies the absolute path, the capability ID, or the read bounds.
        // Existing NIRA performs the canonical request validation, authority check and audit.
        var legacyRequest = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(),
            CapabilityId = NIRACapabilityIds.FileRead,
            Arguments = JsonSerializer.SerializeToElement(new
            {
                path = _scope.FullPath, startLine = 1, maxLines = 75, maxChars = 3600
            }),
            Reason = "V2 read of the exact text-file path expressly requested in this user turn"
        };
        NIRACapabilityResult result;
        try { result = await _executor.ExecuteReadAsync(legacyRequest, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            return AgentToolReceipt.Uncertain("LEGACY_READ_EXECUTION_FAILED");
        }
        if (result.CapabilityId != NIRACapabilityIds.FileRead ||
            result.Risk != NIRACapabilityRisk.Observe || result.ChangedSystemState || result.OutcomeUncertain ||
            !result.AuditRecorded || result.RequestId != legacyRequest.RequestId)
            return AgentToolReceipt.Uncertain("LEGACY_RECEIPT_POLICY_REJECTED");
        if (result.Status != NIRACapabilityResultStatus.Succeeded)
            return AgentToolReceipt.Uncertain(result.Status is NIRACapabilityResultStatus.AuthorizationRequired
                ? "LEGACY_PERMISSION_REQUIRED" : "LEGACY_READ_NOT_COMPLETED");
        // No absolute path or authority tokens in the model-visible result.
        // Restrict material to the beginning of the exact file, not an unbounded document dump.
        string text = result.Output.Length > 3600 ? result.Output[..3600] : result.Output;
        var observation = JsonSerializer.SerializeToElement(new
        {
            source = "authoritative_nira_legacy_capability_service",
            capability = NIRACapabilityIds.FileRead,
            requestedPath = _scope.UserLabel,
            result = "observed_text_excerpt",
            excerpt = text,
            partialRead = true,
            audited = result.AuditRecorded,
            observedUtc = DateTimeOffset.UtcNow
        });
        try
        {
            string token = await _vault.SaveResultAsync(runId, Spec.Id, observation, ct);
            return AgentToolReceipt.ReadOnlyResult(token);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return AgentToolReceipt.Uncertain("OBSERVATION_STORE_FAILED");
        }
    }
}

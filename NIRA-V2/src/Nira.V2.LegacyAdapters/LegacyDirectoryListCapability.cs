using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

/// <summary>
/// Authorization and enumeration stay in the EXISTING NIRA capability service.
/// This adapter contributes only model-independent scope, receipt and output validation.
/// </summary>
public interface ILegacyDirectoryListExecutor
{
    Task<NIRACapabilityResult> ExecuteListAsync(NIRACapabilityRequest request, CancellationToken ct);
}

public sealed class NIRALegacyDirectoryListExecutor(NIRACapabilityService service) : ILegacyDirectoryListExecutor
{
    private readonly NIRACapabilityService _service = service ?? throw new ArgumentNullException(nameof(service));

    public async Task<NIRACapabilityResult> ExecuteListAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        IReadOnlyList<NIRACapabilityResult> results = await _service.ExecuteAsync([request], ct);
        if (results.Count != 1)
            throw new InvalidDataException("Legacy listing returned an unexpected result count.");
        return results[0];
    }
}

public static class LegacyDirectoryListRegistration
{
    public const string ConsentValue = "ALLOW_EXPLICIT_DIRECTORY_LIST";
    private static readonly Regex DirectoryIntent = new(
        @"\b(list|enumerate|browse)\b|\b(show|what|which)\b.{0,45}\b(files|folders|directories|items)\b|\b(folder|directory)\s+(contents|listing)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(150));

    public static bool IsConsented(string? consent) =>
        string.Equals(consent, ConsentValue, StringComparison.Ordinal);

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyDirectoryListExecutor executor, string? consent, string currentUserTurn,
        IRequestedPathProbe? probe = null, Func<string, string?>? rootResolver = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (!IsConsented(consent) || string.IsNullOrWhiteSpace(currentUserTurn) ||
            currentUserTurn.Length > 5000 || !DirectoryIntent.IsMatch(currentUserTurn))
            return [];
        if (!RequestedPathObservationRegistration.TryCreateScope(currentUserTurn,
                rootResolver ?? LegacyTextReadRegistration.ResolveKnownFolder,
                out RequestedPathScope? scope) || scope is null)
            return [];
        return [new LegacyDirectoryListCapability(vault, scope, executor,
            probe ?? new LocalRequestedPathProbe())];
    }

    /// <summary>Hidden, credential-like and invalid entry names are never sent to the model.</summary>
    public static bool IsSafeEntryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
            name[0] == '.' || name.EndsWith('.') || name.EndsWith(' ') ||
            name is "." or ".." ||
            name.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|') ||
            name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("private_key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("private-key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("api_key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("token", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}

public sealed class LegacyDirectoryListCapability : IGeneralCapability
{
    private const int PageSize = 30;
    private readonly AgentPayloadVault _vault;
    private readonly RequestedPathScope _scope;
    private readonly ILegacyDirectoryListExecutor _executor;
    private readonly IRequestedPathProbe _probe;

    public LegacyDirectoryListCapability(AgentPayloadVault vault, RequestedPathScope scope,
        ILegacyDirectoryListExecutor executor, IRequestedPathProbe probe)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public AgentToolSpec Spec { get; } = new("legacy.filesystem.list_exact",
        "List up to 30 immediate files/folders inside exactly ONE directory explicitly named in " +
        "this current user message. The path and listing limits are fixed by the host. " +
        "The existing NIRA authorization/audit service executes the request. " +
        "This tool does not open file contents, recurse, select another path, or modify anything. " +
        "Names may be sent to Ollama Cloud only with independent owner consent.",
        AccessLevel.Observe);
    public string FunctionName => "nira_list_exact_user_directory";
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
            return AgentToolReceipt.Uncertain("DIRECTORY_ARGUMENTS_NOT_ALLOWED");
        if (_probe.Observe(_scope, ct) != RequestedPathStatus.Directory)
            return AgentToolReceipt.Uncertain("DIRECTORY_NOT_SAFE_OR_MISSING");
        var request = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(),
            CapabilityId = NIRACapabilityIds.DirectoryList,
            Arguments = JsonSerializer.SerializeToElement(new
            {
                path = _scope.FullPath,
                pattern = "*",
                recursive = false,
                maxEntries = PageSize,
                offset = 0,
                filesOnly = false
            }),
            Reason = "V2 nonrecursive directory listing explicitly requested for the exact current-turn path"
        };
        NIRACapabilityResult result;
        try { result = await _executor.ExecuteListAsync(request, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            return AgentToolReceipt.Uncertain("LEGACY_DIRECTORY_LIST_FAILED");
        }
        if (result is null || result.CapabilityId != NIRACapabilityIds.DirectoryList ||
            result.RequestId != request.RequestId || result.Risk != NIRACapabilityRisk.Observe ||
            result.ChangedSystemState || result.OutcomeUncertain || !result.AuditRecorded)
            return AgentToolReceipt.Uncertain("LEGACY_RECEIPT_POLICY_REJECTED");
        if (result.Status != NIRACapabilityResultStatus.Succeeded)
            return AgentToolReceipt.Uncertain(result.Status == NIRACapabilityResultStatus.AuthorizationRequired
                ? "LEGACY_PERMISSION_REQUIRED" : "LEGACY_LIST_NOT_COMPLETED");

        // The legacy handler emits absolute paths. They must NEVER enter model observations.
        // Parse the stable FILE<tab>size<tab>path and DIR<tab>path formats, validate
        // each immediate-child path, then publish only safe relative names.
        if (!TryParseOutput(result.Output, _scope.FullPath, out var entries, out int skipped))
            return AgentToolReceipt.Uncertain("DIRECTORY_OUTPUT_REJECTED");
        var observation = JsonSerializer.SerializeToElement(new
        {
            source = "authoritative_nira_legacy_capability_service",
            capability = NIRACapabilityIds.DirectoryList,
            requestedPath = _scope.UserLabel,
            result = "observed_bounded_directory_listing",
            entries,
            omittedSensitiveOrHidden = skipped,
            firstPageOnly = true,
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

    // Pure method for targeted security regression tests; never calls the legacy executor.
    public static bool TryParseOutput(string? raw, string root,
        out IReadOnlyList<DirectoryEntryObservation> entries, out int filtered)
    {
        entries = [];
        filtered = 0;
        if (raw is null || raw.Length > 24_000 || !Path.IsPathFullyQualified(root))
            return false;
        var results = new List<DirectoryEntryObservation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > PageSize) return false;
        foreach (string lineRaw in lines)
        {
            string line = lineRaw.TrimEnd('\r');
            string[] fields = line.Split('\t');
            bool directory = fields.Length == 2 && fields[0] == "DIR";
            bool file = fields.Length == 3 && fields[0] == "FILE";
            if (!directory && !file) return false;
            string itemPath = fields[^1];
            string canonical;
            try { canonical = Path.GetFullPath(itemPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return false; }
            if (!string.Equals(Path.GetDirectoryName(canonical), root,
                StringComparison.OrdinalIgnoreCase)) return false;
            string name = Path.GetFileName(canonical);
            long? size = null;
            if (file)
            {
                if (!long.TryParse(fields[1], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out long number) || number < 0)
                    return false;
                size = number;
            }
            if (!seen.Add(name)) return false;
            if (!LegacyDirectoryListRegistration.IsSafeEntryName(name))
            {
                filtered++;
                continue;
            }
            results.Add(new DirectoryEntryObservation(name, directory ? "directory" : "file", size));
        }
        entries = results;
        return true;
    }
}

// Model-visible observation schema must match the audited contract used by tests.
// System.Text.Json normally serializes record properties as PascalCase;
// explicitly pin wire names so model observation and consumer agree.
public sealed record DirectoryEntryObservation(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("sizeBytes")] long? SizeBytes);

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
/// Phase 4E.3: single-file SHA-256 through the EXISTING NIRA permission/audit
/// executor. The V2 model never chooses a filesystem path or hashes file bytes.
/// This is an Observe capability; this phase NEVER enables PC writes.
/// </summary>
public interface ILegacyFileHashExecutor
{
    Task<NIRACapabilityResult> ExecuteHashAsync(NIRACapabilityRequest request, CancellationToken ct);
}

public sealed class NIRALegacyFileHashExecutor(NIRACapabilityService service) : ILegacyFileHashExecutor
{
    private readonly NIRACapabilityService _service = service ?? throw new ArgumentNullException(nameof(service));

    public async Task<NIRACapabilityResult> ExecuteHashAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        IReadOnlyList<NIRACapabilityResult> results = await _service.ExecuteAsync([request], ct);
        if (results.Count != 1)
            throw new InvalidDataException("Legacy hashing returned an unexpected result count.");
        return results[0];
    }
}

public static class LegacyFileHashRegistration
{
    public const string ConsentValue = "ALLOW_EXPLICIT_FILE_HASH";
    private const long MaxFileBytes = 64L * 1024 * 1024;
    private static readonly Regex ExplicitHashIntent = new(
        @"\b(sha[ -]?256|checksum|fingerprint|hash|integrity)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(150));

    public static bool IsConsented(string? consent) =>
        string.Equals(consent, ConsentValue, StringComparison.Ordinal);

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyFileHashExecutor executor, string? consent, string currentUserTurn,
        IRequestedPathProbe? probe = null, Func<string, string?>? rootResolver = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (!IsConsented(consent) || string.IsNullOrWhiteSpace(currentUserTurn) ||
            currentUserTurn.Length > 5000 || !ExplicitHashIntent.IsMatch(currentUserTurn))
            return [];
        if (!RequestedPathObservationRegistration.TryCreateScope(currentUserTurn,
            rootResolver ?? LegacyTextReadRegistration.ResolveKnownFolder,
            out RequestedPathScope? scope) || scope is null)
            return [];
        // First V2 integrity profile: only explicit, non-sensitive text/code paths.
        // This is a controlled foundation for later atomic-write reconciliation.
        if (!LegacyTextReadRegistration.IsAllowlistedTextName(scope.FullPath)) return [];
        return [new LegacyFileHashCapability(vault, scope, executor,
            probe ?? new LocalRequestedPathProbe(), MaxFileBytes)];
    }
}

public sealed class LegacyFileHashCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly RequestedPathScope _scope;
    private readonly ILegacyFileHashExecutor _executor;
    private readonly IRequestedPathProbe _probe;
    private readonly long _maxBytes;

    public LegacyFileHashCapability(AgentPayloadVault vault, RequestedPathScope scope,
        ILegacyFileHashExecutor executor, IRequestedPathProbe probe,
        long maxBytes = 64L * 1024 * 1024)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _maxBytes = maxBytes;
    }

    public AgentToolSpec Spec { get; } = new("legacy.filesystem.hash_exact",
        "Calculate the SHA-256 integrity fingerprint of exactly one allowlisted existing " +
        "text/code file expressly named in the CURRENT user request. Host chooses the " +
        "path, validates the existing NIRA audit and permission receipt, and returns " +
        "only SHA-256 and byte count. Does not open file content for the model or " +
        "modify any file. Requires independent owner consent.", AccessLevel.Observe);

    public string FunctionName => "nira_hash_exact_user_file";
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
        JsonElement args;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or UnauthorizedAccessException)
        {
            return AgentToolReceipt.Uncertain("INVALID_HASH_ARGUMENT_REFERENCE");
        }
        if (!ValidateArguments(args))
            return AgentToolReceipt.Uncertain("HASH_MODEL_ARGUMENTS_NOT_ALLOWED");
        if (!LegacyTextReadRegistration.IsAllowlistedTextName(_scope.FullPath) ||
            _probe.Observe(_scope, ct) != RequestedPathStatus.File)
            return AgentToolReceipt.Uncertain("HASH_FILE_UNSAFE_OR_MISSING");
        try
        {
            FileInfo info = new(_scope.FullPath);
            if (!info.Exists || info.Length < 0 || info.Length > _maxBytes)
                return AgentToolReceipt.Uncertain("HASH_FILE_TOO_LARGE_OR_MISSING");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return AgentToolReceipt.Uncertain("HASH_FILE_UNAVAILABLE");
        }
        var request = new NIRACapabilityRequest
        {
            RequestId = Guid.NewGuid(),
            CapabilityId = NIRACapabilityIds.FileHash,
            Arguments = JsonSerializer.SerializeToElement(new { path = _scope.FullPath }),
            Reason = "V2 read-only SHA-256 fingerprint of the exact current-turn file through NIRA's authoritative permission and audit service"
        };
        NIRACapabilityResult result;
        try { result = await _executor.ExecuteHashAsync(request, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        { return AgentToolReceipt.Uncertain("LEGACY_HASH_EXECUTION_FAILED"); }

        if (result is null || result.RequestId != request.RequestId ||
            result.CapabilityId != NIRACapabilityIds.FileHash ||
            result.Risk != NIRACapabilityRisk.Observe || result.ChangedSystemState ||
            result.OutcomeUncertain || !result.AuditRecorded)
            return AgentToolReceipt.Uncertain("LEGACY_HASH_RECEIPT_REJECTED");
        if (result.Status != NIRACapabilityResultStatus.Succeeded)
            return AgentToolReceipt.Uncertain(result.Status == NIRACapabilityResultStatus.AuthorizationRequired
                ? "LEGACY_HASH_PERMISSION_REQUIRED" : "LEGACY_HASH_NOT_COMPLETED");
        if (!TryParseOutput(result.Output, _scope.FullPath, _maxBytes,
            out long bytes, out string? sha256))
            return AgentToolReceipt.Uncertain("LEGACY_HASH_OUTPUT_REJECTED");
        try
        {
            // Validate size remains consistent before disclosing the observed hash.
            var current = new FileInfo(_scope.FullPath);
            if (!current.Exists || current.Length != bytes)
                return AgentToolReceipt.Uncertain("HASH_SOURCE_CHANGED_AFTER_OBSERVATION");
            var observation = JsonSerializer.SerializeToElement(new
            {
                source = "authoritative_nira_legacy_capability_service",
                capability = NIRACapabilityIds.FileHash,
                requestedPath = _scope.UserLabel,
                sha256,
                bytes,
                audited = true,
                result = "observed_file_fingerprint",
                contentDisclosed = false,
                observedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, observation, ct);
            return AgentToolReceipt.ReadOnlyResult(token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { return AgentToolReceipt.Uncertain("HASH_OBSERVATION_STORE_UNAVAILABLE"); }
    }

    /// <summary>Reject even a successful but unbound legacy output.</summary>
    public static bool TryParseOutput(string? output, string exactPath, long maxBytes,
        out long bytes, out string? sha256)
    {
        bytes = -1; sha256 = null;
        if (string.IsNullOrWhiteSpace(output) || output.Length > 1200 ||
            !Path.IsPathFullyQualified(exactPath) || maxBytes <= 0) return false;
        string[] lines = output.Split('\n');
        if (lines.Length is < 4 or > 5) return false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            int i = line.IndexOf('=');
            if (i < 1 || !fields.TryAdd(line[..i], line[(i + 1)..])) return false;
        }
        if (fields.Count != 4 ||
            !fields.TryGetValue("Path", out string? path) ||
            !string.Equals(path, exactPath, StringComparison.OrdinalIgnoreCase) ||
            !fields.TryGetValue("ReadComplete", out string? complete) || complete != "True" ||
            !fields.TryGetValue("Bytes", out string? size) ||
            !long.TryParse(size, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long count) ||
            count < 0 || count > maxBytes ||
            !fields.TryGetValue("Sha256", out string? digest) || digest.Length != 64 ||
            digest.Any(c => !Uri.IsHexDigit(c))) return false;
        bytes = count;
        sha256 = digest.ToLowerInvariant();
        return true;
    }
}

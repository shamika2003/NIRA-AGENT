using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Capabilities;
using Nira.V2.Contracts;
using Nira.V2.Filesystem;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.LegacyAdapters;

public interface ILegacyTextCreateExecutor
{
    Task<NIRACapabilityResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken ct);
}

public sealed class NIRALegacyTextCreateExecutor(NIRACapabilityService service) : ILegacyTextCreateExecutor
{
    private readonly NIRACapabilityService _service = service ?? throw new ArgumentNullException(nameof(service));
    public async Task<NIRACapabilityResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken ct)
    {
        IReadOnlyList<NIRACapabilityResult> results = await _service.ExecuteAsync([request], ct);
        if (results.Count != 1) throw new InvalidDataException("Unexpected legacy write receipt count.");
        return results[0];
    }
}

/// <summary>Strict host-selected grant. Model supplies an empty JSON object,
/// never a path, content, overwrite flag, or approval token.</summary>
public static class LegacyTextCreateRegistration
{
    public const string ConsentValue = "ALLOW_EXPLICIT_TEXT_CREATE";
    private static readonly Regex CreateIntent = new(@"\b(?:create|make)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
    private static readonly Regex NegativeIntent = new(@"\b(?:don\x27t|do not|never)\s+(?:create|make)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
    private static readonly Regex ContentPattern = new(
        "\\bcontaining\\s+\"(?<content>[^\"\\r\\n]{1,256})\"",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));

    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        ILegacyTextCreateExecutor executor, string? consent, string userTurn,
        IRequestedPathProbe? probe = null, Func<string, string?>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(executor);
        if (!string.Equals(consent, ConsentValue, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(userTurn) || userTurn.Length > 1500 ||
            !CreateIntent.IsMatch(userTurn) || NegativeIntent.IsMatch(userTurn)) return [];
        MatchCollection matches = ContentPattern.Matches(userTurn);
        if (matches.Count != 1) return [];
        string content = matches[0].Groups["content"].Value;
        if (string.IsNullOrWhiteSpace(content) || content.Any(char.IsControl)) return [];
        if (!RequestedPathObservationRegistration.TryCreateScope(userTurn,
            resolver ?? LegacyTextReadRegistration.ResolveKnownFolder, out RequestedPathScope? scope) ||
            scope is null ||
            !string.Equals(Path.GetExtension(scope.FullPath), ".txt", StringComparison.OrdinalIgnoreCase) ||
            !LegacyTextReadRegistration.IsAllowlistedTextName(scope.FullPath)) return [];
        var tool = new LegacyTextCreateCapability(vault, scope, content, executor,
            probe ?? new LocalRequestedPathProbe());
        return tool.IsSafeToCreate(CancellationToken.None) ? [tool] : [];
    }
}

public sealed class LegacyTextCreateCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly RequestedPathScope _scope;
    private readonly string _content;
    private readonly ILegacyTextCreateExecutor _executor;
    private readonly IRequestedPathProbe _probe;
    public NIRACapabilityRequest ApprovalRequest { get; }

    public LegacyTextCreateCapability(AgentPayloadVault vault, RequestedPathScope scope,
        string content, ILegacyTextCreateExecutor executor, IRequestedPathProbe probe)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        if (string.IsNullOrWhiteSpace(content) || content.Length > 256 || content.Any(char.IsControl))
            throw new ArgumentException("Invalid create-only content.", nameof(content));
        _content = content;
        ApprovalRequest = NIRAV2CreateOnlyRequestFactory.Create(Guid.NewGuid(),
            scope.FullPath, content);
    }

    public AgentToolSpec Spec { get; } = new("legacy.filesystem.create_text_exact",
        "Create ONE NEW .txt file from the EXACT current user request, with fixed " +
        "host-selected content. Requires a separate owner Allow Once approval. " +
        "Never overwrite, append, delete, execute shell commands, or choose other paths.",
        AccessLevel.Modify);
    public string FunctionName => "nira_create_exact_text_file";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
    {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false
    });
    public bool ValidateArguments(JsonElement args) => args.ValueKind == JsonValueKind.Object &&
        !args.EnumerateObject().Any();

    public bool IsSafeToCreate(CancellationToken ct)
    {
        if (!string.Equals(Path.GetExtension(_scope.FullPath), ".txt", StringComparison.OrdinalIgnoreCase) ||
            !LegacyTextReadRegistration.IsAllowlistedTextName(_scope.FullPath) ||
            _scope.ChildSegments.Count < 2 ||
            _probe.Observe(_scope, ct) != RequestedPathStatus.Missing) return false;
        string[] parentSegments = _scope.ChildSegments.Take(_scope.ChildSegments.Count - 1).ToArray();
        var parent = new RequestedPathScope(
            string.Join('/', new[] { _scope.UserLabel.Split('/')[0] }.Concat(parentSegments)),
            _scope.KnownFolderRoot,
            Path.GetDirectoryName(_scope.FullPath)!, parentSegments);
        return _probe.Observe(parent, ct) == RequestedPathStatus.Directory;
    }

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        // The V2 engine must have checkpointed DISPATCH and verified approval first.
        // Never retry an unknown outcome automatically.
        Guid runId;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            if (!ValidateArguments(args)) return AgentToolReceipt.Uncertain("MODEL_WRITE_ARGUMENT_REJECTED");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        { return AgentToolReceipt.Uncertain("INVALID_WRITE_REFERENCE"); }
        if (!IsSafeToCreate(ct)) return AgentToolReceipt.Uncertain("CREATE_TARGET_EXISTS_OR_UNSAFE");

        try
        {
            NIRACapabilityResult receipt = await _executor.ExecuteAsync(ApprovalRequest, ct);
            if (receipt is null || receipt.RequestId != ApprovalRequest.RequestId ||
                receipt.CapabilityId != NIRACapabilityIds.FileWrite ||
                receipt.Risk != NIRACapabilityRisk.Modify || !receipt.AuditRecorded ||
                receipt.OutcomeUncertain || !receipt.ChangedSystemState ||
                receipt.Status != NIRACapabilityResultStatus.Succeeded)
                return AgentToolReceipt.Uncertain("CREATE_RECEIPT_UNCONFIRMED");

            // Independent read-back is authoritative, not the model or legacy summary.
            // Validate the path again to reject redirected/junction destinations.
            if (_probe.Observe(_scope, ct) != RequestedPathStatus.File)
                return AgentToolReceipt.Uncertain("CREATED_PATH_NOT_SAFE");
            byte[] bytes = await File.ReadAllBytesAsync(_scope.FullPath, ct);
            byte[] expected = new UTF8Encoding(false).GetBytes(_content);
            if (!bytes.AsSpan().SequenceEqual(expected))
                return AgentToolReceipt.Uncertain("CREATED_BYTES_DIFFER");
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            JsonElement evidence = JsonSerializer.SerializeToElement(new
            {
                action = "authorized_create_only", requestedPath = _scope.UserLabel,
                bytes = bytes.Length, sha256 = hash, audited = true,
                verifiedBy = "independent_host_readback", changedSystemState = true,
                observedUtc = DateTimeOffset.UtcNow
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, evidence, ct);
            return AgentToolReceipt.Verified(token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException or ArgumentException or
            System.Security.SecurityException)
        { return AgentToolReceipt.Uncertain("WRITE_OUTCOME_REQUIRES_RECONCILIATION"); }
    }
}

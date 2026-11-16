using System.IO;
using System.Text.Json;
using NIRAAgent.Capabilities;
using NIRAAgent.Runtime.Contracts;
using NIRAAgent.Runtime.General;
using NIRAAgent.Runtime.Runtime.AgentLoop;

namespace NIRAAgent.Runtime.HostAdapters;

/// <summary>
/// Three discoverable model functions over NIRA's ONE established capability registry.
/// No model-supplied tool name grants authority: schema, risk, user scopes and audit
/// remain with NIRACapabilityService. A modifying call is checkpointed BEFORE dispatch.
/// </summary>
public static class NIRAAuthoritativeCapabilityTools
{
    public static IReadOnlyList<IGeneralCapability> Create(
        AgentPayloadVault vault, NIRACapabilityRegistry registry, NIRACapabilityService service)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(service);
        return new IGeneralCapability[]
        {
            new CapabilityDiscovery(vault, registry),
            new CapabilityInvocation(vault, registry, service, mutate: false),
            new CapabilityInvocation(vault, registry, service, mutate: true)
        };
    }

    public sealed class CapabilityDiscovery : IGeneralCapability
    {
        private readonly AgentPayloadVault _vault;
        private readonly NIRACapabilityRegistry _registry;
        private const int PageSize = 8;
        public CapabilityDiscovery(AgentPayloadVault vault, NIRACapabilityRegistry registry)
        { _vault = vault; _registry = registry; }

        public AgentToolSpec Spec { get; } = new("nira.capabilities.discover",
            "Browse NIRA's registered capabilities by optional prefix and offset (filesystem., browser., " +
            "image., vision., desktop., process., web., http., math., etc.). Returns the " +
            "declared parameter names/types and default risk. Use offset for later pages; inspect before invoking.",
            AccessLevel.Observe);
        public string FunctionName => "nira_discover_capabilities";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { prefix = new { type = "string", description =
                "Optional case-insensitive registered capability ID prefix. Empty lists first page." },
                offset = new { type = "integer", minimum = 0, maximum = 4096,
                    description = "Zero-based result offset for pagination. Default 0." } },
            additionalProperties = false
        });
        public bool ValidateArguments(JsonElement args) =>
            args.ValueKind == JsonValueKind.Object &&
            args.EnumerateObject().All(p =>
                (p.NameEquals("prefix") && p.Value.ValueKind == JsonValueKind.String &&
                 p.Value.GetString() is string prefix && prefix.Length <= 48 &&
                 !prefix.Any(char.IsControl)) ||
                (p.NameEquals("offset") && p.Value.ValueKind == JsonValueKind.Number &&
                 p.Value.TryGetInt32(out int offset) && offset is >= 0 and <= 4096));

        public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
        {
            Guid runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadArgumentAsync(runId, Spec.Id, reference, ct);
            if (!ValidateArguments(args)) return AgentToolReceipt.Uncertain("INVALID_DISCOVERY_ARGUMENT");
            string prefix = args.TryGetProperty("prefix", out var p) ? p.GetString()!.Trim() : "";
            int offset = args.TryGetProperty("offset", out var start) ? start.GetInt32() : 0;
            var matches = _registry.Descriptors.Where(d =>
                d.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            var result = JsonSerializer.SerializeToElement(new
            {
                matching = matches.Length, offset,
                shown = Math.Min(PageSize, Math.Max(0, matches.Length - offset)),
                nextOffset = offset + PageSize < matches.Length ? (int?)(offset + PageSize) : null,
                additionalMatches = Math.Max(0, matches.Length - offset - PageSize),
                hint = "Use nextOffset or narrow the prefix. Risk and arguments are validated again at dispatch.",
                capabilities = matches.Skip(offset).Take(PageSize).Select(d => new {
                    id = d.Id, description = d.Description.Length > 180 ? d.Description[..180] : d.Description,
                    defaultRisk = d.DefaultRisk.ToString(),
                    operationLane = CapabilityInvocation.RequiresCheckpointedDispatch(d.Id) ||
                        d.DefaultRisk != NIRACapabilityRisk.Observe ? "execute" : "observe",
                    parameters = d.Parameters.Select(a => new {
                        name = a.Name, type = a.Type, required = a.Required,
                        description = a.Description.Length > 100 ? a.Description[..100] : a.Description,
                        allowedValues = a.AllowedValues.Take(12).ToArray(),
                        defaultValue = a.DefaultValue is { Length: > 80 } ?
                            a.DefaultValue[..80] : a.DefaultValue,
                        minimum = a.Minimum, maximum = a.Maximum
                    }).ToArray()
                }).ToArray()
            });
            string token = await _vault.SaveResultAsync(runId, Spec.Id, result, ct);
            return AgentToolReceipt.ReadOnlyResult(token);
        }
    }

    public sealed class CapabilityInvocation : IGeneralCapability
    {
        private readonly AgentPayloadVault _vault;
        private readonly NIRACapabilityRegistry _registry;
        private readonly NIRACapabilityService _service;
        private readonly bool _mutate;
        public CapabilityInvocation(AgentPayloadVault vault, NIRACapabilityRegistry registry,
            NIRACapabilityService service, bool mutate)
        { _vault = vault; _registry = registry; _service = service; _mutate = mutate; }

        public AgentToolSpec Spec => _mutate
            ? new("nira.capabilities.execute",
                "Request one checkpointed capability (writes, browser navigation/session, HTTP requests). " +
                "The host validates the capability's TRUE risk, owner permissions and durable audit. " +
                "Use only for an authorized user task, never to grant yourself permission.", AccessLevel.Modify)
            : new("nira.capabilities.observe",
                "Execute one purely observational registered NIRA capability, using its descriptor's " +
                "exact argument names. Browser navigation and HTTP requests use nira_execute_capability " +
                "even when their authorization risk is Observe; a write cannot " +
                "be smuggled into this function.", AccessLevel.Observe);
        public string FunctionName => _mutate ? "nira_execute_capability" : "nira_observe_capability";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new {
                capabilityId = new { type = "string", description = "ID from nira_discover_capabilities" },
                arguments = new { type = "object", description = "Exact named arguments from its declared schema" }
            },
            required = new[] { "capabilityId", "arguments" },
            additionalProperties = false
        });

        public bool ValidateArguments(JsonElement args) =>
            args.ValueKind == JsonValueKind.Object &&
            args.EnumerateObject().Count() == 2 &&
            args.TryGetProperty("capabilityId", out var id) &&
            id.ValueKind == JsonValueKind.String &&
            id.GetString() is string value && value.Length is >= 3 and <= 160 &&
            !value.Any(char.IsControl) &&
            args.TryGetProperty("arguments", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.GetRawText().Length <= 16_000;

        // Checkpoint semantics are independent from authorization risk. A browser
        // navigation is Observe for the existing scope authorizer, but it still
        // mutates a live browser session and MUST NOT auto-replay on restart.
        // HTTP requests also use this lane even for GET/HEAD: a remote server may
        // have effects the host cannot reliably determine after interruption.
        internal static bool RequiresCheckpointedDispatch(string id) => id is
            NIRACapabilityIds.HttpRequest or
            NIRACapabilityIds.ImageSearch or
            NIRACapabilityIds.ImageRetrieve or
            NIRACapabilityIds.VisionCapture or
            NIRACapabilityIds.BrowserScreenshot or
            NIRACapabilityIds.BrowserRequest or
            NIRACapabilityIds.BrowserSessionOpen or
            NIRACapabilityIds.BrowserSessionClose or
            NIRACapabilityIds.BrowserPageSelect or
            NIRACapabilityIds.BrowserNavigate or
            NIRACapabilityIds.BrowserBack or
            NIRACapabilityIds.BrowserFollow or
            NIRACapabilityIds.BrowserExplore or
            NIRACapabilityIds.BrowserClick or
            NIRACapabilityIds.BrowserRecoveryResume;

        public bool TryPreflight(JsonElement args, out NIRACapabilityRequest request,
            out NIRACapabilityRisk resolvedRisk)
        {
            request = new NIRACapabilityRequest();
            resolvedRisk = NIRACapabilityRisk.Destructive;
            if (!ValidateArguments(args)) return false;
            string id = args.GetProperty("capabilityId").GetString()!;
            if (!_registry.TryResolve(id, out INIRACapabilityHandler? handler) || handler is null)
                return false;
            NIRACapabilityRequest raw = new() {
                CapabilityId = id,
                Arguments = args.GetProperty("arguments").Clone(),
                Reason = "Model-selected tool for the current host-scoped NIRA task"
            };
            if (!_service.TryNormalizeAndValidateSchema(raw, out request, out _)) return false;
            try { resolvedRisk = handler.ResolveRisk(request); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
            bool checkpointed = RequiresCheckpointedDispatch(id) ||
                handler.Descriptor.DefaultRisk != NIRACapabilityRisk.Observe;
            // Observe-risk browser/HTTP operations are valid through the
            // checkpointed lane; the underlying scope authorizer STILL sees
            // their original resolved risk (no permission escalation).
            return _mutate ? checkpointed || resolvedRisk != NIRACapabilityRisk.Observe
                           : !checkpointed && resolvedRisk == NIRACapabilityRisk.Observe;
        }

        public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
        {
            Guid runId;
            JsonElement args;
            try {
                runId = _vault.RunIdFromArgument(reference);
                args = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
            { return _mutate ? AgentToolReceipt.Uncertain("INVALID_SCOPED_TOOL_INPUT")
                              : new AgentToolReceipt(false, false, null, "INVALID_SCOPED_TOOL_INPUT"); }

            if (!TryPreflight(args, out NIRACapabilityRequest normalized, out _))
                return _mutate ? AgentToolReceipt.ConfirmedNoChange("CAPABILITY_PREFLIGHT_REJECTED")
                               : new AgentToolReceipt(false, false, null, "CAPABILITY_PREFLIGHT_REJECTED");

            // The existing service, and ONLY that service, prepares the request,
            // authorizes its resolved risk, dispatches the handler and audits it.
            IReadOnlyList<NIRACapabilityResult> results = await _service.ExecuteAsync(
                new[] { normalized }, ct);
            if (results.Count != 1)
                return _mutate ? AgentToolReceipt.Uncertain("CAPABILITY_RECEIPT_COUNT_UNKNOWN")
                               : new AgentToolReceipt(false, false, null, "CAPABILITY_RECEIPT_COUNT_UNKNOWN");
            NIRACapabilityResult outcome = results[0];
            if (outcome.OutcomeUncertain) return AgentToolReceipt.Uncertain("AUTHORITATIVE_OUTCOME_UNCERTAIN");
            if (_mutate && (outcome.Status == NIRACapabilityResultStatus.Rejected ||
                outcome.Status == NIRACapabilityResultStatus.AuthorizationRequired))
                return AgentToolReceipt.ConfirmedNoChange("AUTHORIZATION_NOT_GRANTED");
            if (outcome.Status != NIRACapabilityResultStatus.Succeeded)
                return _mutate ? AgentToolReceipt.Uncertain("CAPABILITY_NOT_VERIFIED")
                               : new AgentToolReceipt(false, false, null, "CAPABILITY_FAILED");
            if (!outcome.AuditRecorded)
                return _mutate ? AgentToolReceipt.Uncertain("AUTHORITATIVE_AUDIT_NOT_PERSISTED")
                    : new AgentToolReceipt(false, false, null, "AUTHORITATIVE_AUDIT_NOT_PERSISTED");

            // A successful browser.navigate carries its trusted submitted URL
            // separately from untrusted webpage text. The reasoner may link the
            // exact current-user URL, not URLs embedded in page content.
            string verifiedRequestedUrl = string.Empty;
            if (outcome.CapabilityId == NIRACapabilityIds.BrowserNavigate &&
                normalized.Arguments.TryGetProperty("url", out JsonElement requestedUrl) &&
                requestedUrl.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(requestedUrl.GetString(), UriKind.Absolute, out Uri? targetUrl) &&
                targetUrl.Scheme == Uri.UriSchemeHttps &&
                string.IsNullOrEmpty(targetUrl.UserInfo))
                verifiedRequestedUrl = targetUrl.AbsoluteUri;

            JsonElement response = JsonSerializer.SerializeToElement(new {
                capabilityId = outcome.CapabilityId, status = outcome.Status.ToString(),
                verifiedRequestedUrl,
                risk = outcome.Risk.ToString(), summary = outcome.Summary,
                // Preserve the complete authoritative bounded receipt in the
                // encrypted vault. Observation context applies separate budgets.
                output = outcome.Output,
                changedSystemState = outcome.ChangedSystemState,
                auditRecorded = outcome.AuditRecorded, auditAttemptId = outcome.AuditAttemptId,
                outcomeUncertain = outcome.OutcomeUncertain,
                httpStatus = outcome.HttpStatusCode,
                visualArtifacts = outcome.VisualArtifacts.Select(a => new {
                    surface = a.Surface, title = a.Title, evidenceId = a.EvidenceId,
                    localPath = a.LocalPath, caption = a.Caption, sourceUri = a.SourceUri,
                    subjectQuery = a.SubjectQuery, presentToUser = a.PresentToUser
                }).Take(4).ToArray()
            });
            string resultRef = await _vault.SaveResultAsync(runId, Spec.Id, response, ct);
            return _mutate ? AgentToolReceipt.Verified(resultRef)
                           : AgentToolReceipt.ReadOnlyResult(resultRef);
        }
    }
}

/// <summary>Preflight validation only. Actual owner approval remains in the
/// existing NIRAScopedCapabilityAuthorizer during NIRACapabilityService dispatch.</summary>
public sealed class NIRAAuthoritativeCapabilityPermissionGate : IAgentPermissionGate
{
    private readonly AgentPayloadVault _vault;
    private readonly NIRAAuthoritativeCapabilityTools.CapabilityInvocation _action;
    public NIRAAuthoritativeCapabilityPermissionGate(AgentPayloadVault vault,
        NIRAAuthoritativeCapabilityTools.CapabilityInvocation action)
    { _vault = vault; _action = action; }

    public async Task<bool> IsAuthorizedAsync(Guid runId, AgentToolSpec tool,
        string reference, CancellationToken ct)
    {
        if (tool.Access == AccessLevel.Observe) return true;
        if (runId == Guid.Empty || tool != _action.Spec ||
            tool.Access != AccessLevel.Modify) return false;
        try {
            if (_vault.RunIdFromArgument(reference) != runId) return false;
            JsonElement args = await _vault.ReadToolArgumentAsync(tool.Id, reference, ct);
            return _action.TryPreflight(args, out _, out _);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return false; }
    }
}

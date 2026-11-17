using System.IO;
using System.Text;
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
        var discovery = new CapabilityDiscovery(vault, registry);
        var observer = new CapabilityInvocation(vault, registry, service, mutate: false);
        var executor = new CapabilityInvocation(vault, registry, service, mutate: true);
        var tools = new List<IGeneralCapability> { discovery, observer, executor };
        // These are provider-native facades over EXISTING read-only handlers,
        // not a second execution engine. The model still selects the operation;
        // every invocation goes through observer -> NIRACapabilityService (audit,
        // scope and permission), with the original outcome preserved.
        foreach ((string id, string function) in new[] {
            (NIRACapabilityIds.DirectoryList, "nira_list_directory"),
            (NIRACapabilityIds.FileLocate, "nira_locate_file"),
            (NIRACapabilityIds.FileSearch, "nira_search_files"),
            (NIRACapabilityIds.FileRead, "nira_read_file"),
            (NIRACapabilityIds.FileMetadata, "nira_file_metadata"),
            (NIRACapabilityIds.MathCalculate, "nira_calculate"),
            (NIRACapabilityIds.WebSearch, "nira_search_web")
        }) {
            if (registry.TryResolve(id, out INIRACapabilityHandler? handler) &&
                handler is not null &&
                handler.Descriptor.DefaultRisk == NIRACapabilityRisk.Observe &&
                !CapabilityInvocation.RequiresCheckpointedDispatch(id))
                tools.Add(new BoundObservation(vault, service, observer, handler, function));
        }
        return tools;
    }

    // Hierarchical model-visible inventory built from the authoritative registry.
    // All capability families are represented without shipping 84 costly schemas
    // per round; the agent can use query discovery for detailed arguments.
    // This is data from the actual handlers, not a task-specific keyword router.
    public static string BuildQuickReference(NIRACapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var b = new StringBuilder("NIRA capability families (discover with query or prefix):\n");
        foreach (var group in registry.Descriptors
            .GroupBy(d => d.Id.Split('.')[0], StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string examples = string.Join(", ", group.Take(3).Select(d => d.Id));
            string line = "- " + group.Key + " (" + group.Count() + "): " + examples + "\n";
            if (b.Length + line.Length > 2950) break;
            b.Append(line);
        }
        b.Append("Native functions already advertise exact schemas; use those directly. " +
            "For other operations, nira_discover_capabilities(query=task words) returns " +
            "ranked IDs, exact parameters and the safe operation lane. " +
            "Discovering is not authorization.\n");
        return b.Length <= 3000 ? b.ToString() : b.ToString(0, 3000);
    }

    /// <summary>
    /// A model-native, typed schema for a frequently used real NIRA observation.
    /// Execution is delegated to the SAME audited observation adapter; its
    /// provider-visible function name is never trusted as authority.
    /// </summary>
    public sealed class BoundObservation : IGeneralCapability
    {
        private readonly AgentPayloadVault _vault;
        private readonly NIRACapabilityService _service;
        private readonly CapabilityInvocation _observer;
        private readonly INIRACapabilityHandler _handler;
        private readonly NIRACapabilityDescriptor _descriptor;

        public BoundObservation(AgentPayloadVault vault, NIRACapabilityService service,
            CapabilityInvocation observer, INIRACapabilityHandler handler, string function)
        {
            _vault = vault;
            _service = service;
            _observer = observer;
            _handler = handler;
            _descriptor = handler.Descriptor.Normalize();
            if (_descriptor.DefaultRisk != NIRACapabilityRisk.Observe ||
                CapabilityInvocation.RequiresCheckpointedDispatch(_descriptor.Id))
                throw new ArgumentException("Direct tool must be a pure observation.");
            FunctionName = function;
            Spec = new AgentToolSpec("nira.direct." + _descriptor.Id,
                "Execute registered " + _descriptor.Id + " through NIRA's scoped, audited " +
                "capability service. " + (_descriptor.Description.Length > 500 ?
                    _descriptor.Description[..500] : _descriptor.Description), AccessLevel.Observe);
            var props = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (NIRACapabilityParameterDescriptor param in _descriptor.Parameters)
            {
                string type = param.Type.ToLowerInvariant();
                if (type is not ("string" or "integer" or "number" or "boolean" or "array" or "object"))
                    type = "string";
                string description = param.Description.Length > 240 ?
                    param.Description[..240] : param.Description;
                var schema = new Dictionary<string, object> {
                    ["type"] = type, ["description"] = description
                };
                if (type == "array") schema["items"] = new { type = "string" };
                if (type == "object") schema["additionalProperties"] = true;
                if (param.AllowedValues.Count > 0 && type == "string")
                    schema["enum"] = param.AllowedValues.ToArray();
                props[param.Name] = schema;
            }
            InputSchema = JsonSerializer.SerializeToElement(new {
                type = "object", properties = props,
                required = _descriptor.Parameters.Where(a => a.Required)
                    .Select(a => a.Name).ToArray(), additionalProperties = false
            });
        }

        public AgentToolSpec Spec { get; }
        public string FunctionName { get; }
        public JsonElement InputSchema { get; }

        public bool ValidateArguments(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object || args.GetRawText().Length > 16000)
                return false;
            NIRACapabilityRequest raw = new() {
                CapabilityId = _descriptor.Id, Arguments = args.Clone(),
                Reason = "Model-selected, host-registered read-only operation"
            };
            if (!_service.TryNormalizeAndValidateSchema(raw, out var normalized, out _))
                return false;
            try { return _handler.ResolveRisk(normalized) == NIRACapabilityRisk.Observe; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            { return false; }
        }

        public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
        {
            Guid runId;
            JsonElement args;
            try {
                runId = _vault.RunIdFromArgument(reference);
                args = await _vault.ReadArgumentAsync(runId, Spec.Id, reference, ct);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
            { return new AgentToolReceipt(false, false, null, "DIRECT_TOOL_INPUT_INVALID"); }
            if (!ValidateArguments(args))
                return new AgentToolReceipt(false, false, null, "DIRECT_TOOL_PREFLIGHT_REJECTED");
            var wrapped = JsonSerializer.SerializeToElement(new {
                capabilityId = _descriptor.Id, arguments = args
            });
            string wrappedRef = await _vault.SaveArgumentAsync(runId,
                _observer.Spec.Id, wrapped, ct);
            AgentToolReceipt result = await _observer.ExecuteAsync(wrappedRef, ct);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.ResultReference))
                return result;
            // Re-scope the existing audited receipt to this native tool ID.
            // A receipt from one tool can NEVER be read under another tool ID.
            JsonElement body = await _vault.ReadResultAsync(runId,
                _observer.Spec.Id, result.ResultReference, ct);
            string saved = await _vault.SaveResultAsync(runId, Spec.Id, body, ct);
            return AgentToolReceipt.ReadOnlyResult(saved);
        }
    }

    public sealed class CapabilityDiscovery : IGeneralCapability
    {
        private readonly AgentPayloadVault _vault;
        private readonly NIRACapabilityRegistry _registry;
        private const int PageSize = 8;
        public CapabilityDiscovery(AgentPayloadVault vault, NIRACapabilityRegistry registry)
        { _vault = vault; _registry = registry; }

        public AgentToolSpec Spec { get; } = new("nira.capabilities.discover",
            "Search every registered NIRA capability by optional natural-language query, " +
            "ID prefix and page offset. Returns ranked matches, exact parameter schema, " +
            "risk and operation lane. Query e.g. 'create document' without guessing IDs.",
            AccessLevel.Observe);
        public string FunctionName => "nira_discover_capabilities";
        public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new {
                query = new { type = "string", maxLength = 120,
                    description = "Optional natural-language task words. Ranks matching registered IDs and descriptions." },
                prefix = new { type = "string", description =
                    "Optional case-insensitive ID prefix, e.g. browser. or filesystem.docx." },
                offset = new { type = "integer", minimum = 0, maximum = 4096,
                    description = "Zero-based result offset for pagination. Default 0." } },
            additionalProperties = false
        });
        public bool ValidateArguments(JsonElement args) =>
            args.ValueKind == JsonValueKind.Object &&
            args.EnumerateObject().All(p =>
                (p.NameEquals("query") && p.Value.ValueKind == JsonValueKind.String &&
                 p.Value.GetString() is string query && query.Length <= 120 &&
                 !query.Any(char.IsControl)) ||
                (p.NameEquals("prefix") && p.Value.ValueKind == JsonValueKind.String &&
                 p.Value.GetString() is string prefix && prefix.Length <= 48 &&
                 !prefix.Any(char.IsControl)) ||
                (p.NameEquals("offset") && p.Value.ValueKind == JsonValueKind.Number &&
                 p.Value.TryGetInt32(out int offset) && offset is >= 0 and <= 4096));

        private static int Relevance(NIRACapabilityDescriptor d, string[] terms)
        {
            if (terms.Length == 0) return 0;
            int score = 0;
            string id = d.Id;
            string description = d.Description;
            string parameterNames = string.Join(" ", d.Parameters.Select(x => x.Name));
            foreach (string t in terms)
            {
                if (id.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 8;
                if (description.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 3;
                if (parameterNames.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 1;
            }
            return score;
        }

        public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
        {
            Guid runId = _vault.RunIdFromArgument(reference);
            JsonElement args = await _vault.ReadArgumentAsync(runId, Spec.Id, reference, ct);
            if (!ValidateArguments(args)) return AgentToolReceipt.Uncertain("INVALID_DISCOVERY_ARGUMENT");
            string query = args.TryGetProperty("query", out var q) ? q.GetString()!.Trim() : "";
            string prefix = args.TryGetProperty("prefix", out var p) ? p.GetString()!.Trim() : "";
            int offset = args.TryGetProperty("offset", out var start) ? start.GetInt32() : 0;
            string[] terms = query.Split(new[] { ' ', '-', '_', '.', '/', ',', ':' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
            var matches = _registry.Descriptors
                .Where(d => d.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(d => new { Descriptor = d, Rank = Relevance(d, terms) })
                .Where(x => terms.Length == 0 || x.Rank > 0)
                .OrderByDescending(x => x.Rank)
                .ThenBy(x => x.Descriptor.Id, StringComparer.Ordinal)
                .Select(x => x.Descriptor).ToArray();
            var result = JsonSerializer.SerializeToElement(new
            {
                prefix, query, matching = matches.Length, offset,
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
            {
                // Only the AUTHORITATIVE service can classify a dispatched
                // mutation as definitely not applied. Failed != no-effect.
                if (_mutate && outcome.DefiniteNoEffect && outcome.AuditRecorded)
                {
                    JsonElement failed = JsonSerializer.SerializeToElement(new {
                        capabilityId = outcome.CapabilityId, status = outcome.Status.ToString(),
                        summary = outcome.Summary, auditRecorded = outcome.AuditRecorded,
                        changedSystemState = outcome.ChangedSystemState,
                        outcomeUncertain = outcome.OutcomeUncertain,
                        definiteNoEffect = true
                    });
                    string failedRef = await _vault.SaveResultAsync(runId, Spec.Id, failed, ct);
                    return new AgentToolReceipt(false, true, failedRef,
                        "CAPABILITY_REJECTED_NO_EFFECT");
                }
                return _mutate ? AgentToolReceipt.Uncertain("CAPABILITY_NOT_VERIFIED")
                               : new AgentToolReceipt(false, false, null, "CAPABILITY_FAILED");
            }
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
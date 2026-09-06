using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SegaAgent.Authorization;

namespace SegaAgent.Capabilities;

public sealed class SegaCapabilityService
{
    private const int MaximumResultOutputCharacters = 24000;
    private readonly SegaCapabilityRegistry _registry;
    private readonly ISegaCapabilityAuthorizer _authorizer;
    private readonly SegaCapabilityRequestPolicy _policy;
    private readonly SegaAuthorityStore _audit;
    // Serialize direct filesystem mutations within this runtime. This prevents
    // concurrent append/replacement and parent-delete races between branches.
    private readonly SemaphoreSlim _filesystemMutations = new(1, 1);

    public SegaCapabilityService(SegaCapabilityRegistry registry, ISegaCapabilityAuthorizer authorizer,
        SegaCapabilityRequestPolicy policy, SegaAuthorityStore audit)
    {
        _registry = registry; _authorizer = authorizer; _policy = policy; _audit = audit;
        Debug.WriteLine($"[Capabilities] READY | Registered={_registry.Descriptors.Count} | StateChangingAuthorization=PersistentScopes");
    }

    public string BuildCognitionContext()
    {
        StringBuilder text = new();
        text.AppendLine("SEGA TRUSTED PRIMITIVE CAPABILITIES");
        text.AppendLine(_authorizer.BuildCognitionContext());
        text.AppendLine("AVAILABLE PRIMITIVES");
        foreach (SegaCapabilityDescriptor descriptor in _registry.Descriptors)
        {
            text.AppendLine($"- {descriptor.Id} | defaultRisk={descriptor.DefaultRisk} | {descriptor.Description}");
            foreach (SegaCapabilityParameterDescriptor parameter in descriptor.Parameters)
                text.AppendLine($"  - {parameter.Name}: {parameter.Type} | required={parameter.Required} | {parameter.Description}");
        }
        return text.ToString().Trim();
    }

    public async Task<IReadOnlyList<SegaCapabilityResult>> ExecuteAsync(
        IReadOnlyList<SegaCapabilityRequest> requests, CancellationToken cancellationToken = default)
    {
        List<SegaCapabilityResult> results = [];
        if (requests == null) return results;
        foreach (SegaCapabilityRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SegaCapabilityResult result = await ExecuteOneAsync(request, cancellationToken);
            results.Add(result);
            Debug.WriteLine($"[Capability] {result.Status.ToString().ToUpperInvariant()} | Id={result.CapabilityId} | " +
                $"Risk={result.Risk} | Changed={result.ChangedSystemState} | Uncertain={result.OutcomeUncertain} | " +
                $"ExitCode={result.ExitCode?.ToString() ?? "-"} | Audit={result.AuditAttemptId?.ToString("D") ?? "-"}");
        }
        return results;
    }

    private async Task<SegaCapabilityResult> ExecuteOneAsync(SegaCapabilityRequest raw, CancellationToken ct)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        SegaCapabilityRequest request = raw;
        SegaCapabilityRisk risk = SegaCapabilityRisk.Observe;
        Guid? attempt = null;
        Guid? scope = null;
        bool dispatched = false;
        bool mutationGateTaken = false;
        SegaCapabilityResult result;
        try
        {
            request = raw.Normalize();
            if (!_registry.TryResolve(request.CapabilityId, out ISegaCapabilityHandler? handler) || handler == null)
                throw new InvalidOperationException("The capability ID is not registered.");
            SegaCapabilityDescriptor descriptor = handler.Descriptor.Normalize();

            // Once the trusted handler is known, never leave a preflight failure
            // mislabeled as Observe merely because preparation failed before the
            // normal risk resolver ran. The descriptor gives us a safe baseline,
            // then the validated raw request can refine dynamic risks (for
            // example HTTP POST or overwrite=true) before request preparation.
            risk = descriptor.DefaultRisk;
            ValidateArguments(request, descriptor);
            risk = handler.ResolveRisk(request);

            request = _policy.Prepare(request);
            risk = handler.ResolveRisk(request);
            SegaAuthorityOperation operation = _policy.Describe(request, risk);
            // No handler can run unless its durable audit row already exists.
            attempt = _audit.BeginAttempt(request, operation.Target, operation.Fingerprint);
            SegaCapabilityAuthorizationDecision decision = await _authorizer.AuthorizeAsync(descriptor, risk, request, ct);
            if (decision.Status == SegaCapabilityAuthorizationStatus.Allowed)
            {
                if (risk != SegaCapabilityRisk.Observe &&
                    (request.CapabilityId.StartsWith("filesystem.", StringComparison.Ordinal) ||
                     request.CapabilityId == SegaCapabilityIds.HttpDownload))
                {
                    await _filesystemMutations.WaitAsync(ct);
                    mutationGateTaken = true;
                }
                decision = await _authorizer.ValidateForDispatchAsync(descriptor, risk, request, decision, ct);
            }
            scope = decision.ScopeId;
            _audit.RecordDecision(attempt.Value, decision);
            ct.ThrowIfCancellationRequested();
            if (decision.Status != SegaCapabilityAuthorizationStatus.Allowed)
            {
                result = Make(decision.Status == SegaCapabilityAuthorizationStatus.AuthorizationRequired
                    ? SegaCapabilityResultStatus.AuthorizationRequired : SegaCapabilityResultStatus.Rejected, decision.Reason);
            }
            else
            {
                dispatched = true;
                SegaCapabilityHandlerResult handled = await handler.ExecuteAsync(request, ct);
                result = Make(handled.Succeeded ? SegaCapabilityResultStatus.Succeeded : SegaCapabilityResultStatus.Failed,
                    SegaCapabilityArguments.Truncate(handled.Summary, 2000)) with
                {
                    Output = SegaCapabilityArguments.Truncate(handled.Output, MaximumResultOutputCharacters),
                    ChangedSystemState = handled.ChangedSystemState,
                    ExitCode = handled.ExitCode,
                    HttpStatusCode = handled.HttpStatusCode
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result = Make(SegaCapabilityResultStatus.Failed, dispatched
                ? "Cancelled after dispatch. Inspect the target before retrying; partial effects may remain."
                : "Cancelled before dispatch. No handler was started.") with
            { OutcomeUncertain = dispatched && risk != SegaCapabilityRisk.Observe };
            TryFinish(result);
            throw;
        }
        catch (Exception ex)
        {
            result = Make(dispatched ? SegaCapabilityResultStatus.Failed : SegaCapabilityResultStatus.Rejected,
                ex.Message + (dispatched && risk != SegaCapabilityRisk.Observe
                    ? " Partial effects may remain; inspect before retrying." : "")) with
            { OutcomeUncertain = dispatched && risk != SegaCapabilityRisk.Observe };
            if (!attempt.HasValue)
            {
                // Malformed/rejected attempts get a minimal record, without their
                // unvalidated arguments or exception text being persisted.
                try { attempt = _audit.BeginAttempt(request, "Rejected before dispatch", ""); }
                catch (Exception auditError) { Debug.WriteLine($"[AuthorityAudit] Unavailable: {auditError.GetType().Name}"); }
                result = result with { AuditAttemptId = attempt };
            }
        }
        finally
        {
            if (mutationGateTaken) _filesystemMutations.Release();
        }
        bool recorded = TryFinish(result);
        return result with
        {
            AuditRecorded = recorded,
            Summary = recorded ? result.Summary : result.Summary +
                " The audit result could not be saved. Do not repeat an already-dispatched action just to repair auditing."
        };

        SegaCapabilityResult Make(SegaCapabilityResultStatus status, string summary) => new()
        {
            RequestId = request.RequestId, CapabilityId = request.CapabilityId, Risk = risk,
            Status = status, Summary = summary, StartedAtUtc = started, FinishedAtUtc = DateTimeOffset.UtcNow,
            AuthorizationScopeId = scope, AuditAttemptId = attempt
        };
        bool TryFinish(SegaCapabilityResult value)
        {
            if (!attempt.HasValue) return false;
            try { _audit.FinishAttempt(attempt.Value, value); return true; }
            catch (Exception ex) { Debug.WriteLine($"[AuthorityAudit] Result save failed: {ex.GetType().Name}"); return false; }
        }
    }

    private static void ValidateArguments(SegaCapabilityRequest request, SegaCapabilityDescriptor descriptor)
    {
        Dictionary<string, JsonElement> supplied = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in request.Arguments.EnumerateObject())
        {
            if (!supplied.TryAdd(property.Name, property.Value))
                throw new InvalidOperationException($"Duplicate argument '{property.Name}'.");
            if (!descriptor.Parameters.Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Unknown argument '{property.Name}' for {descriptor.Id}.");
        }
        foreach (SegaCapabilityParameterDescriptor parameter in descriptor.Parameters)
        {
            if (!supplied.TryGetValue(parameter.Name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                if (parameter.Required) throw new InvalidOperationException($"Required argument '{parameter.Name}' is missing.");
                continue;
            }
            bool valid = parameter.Type.ToLowerInvariant() switch
            {
                "string" => value.ValueKind == JsonValueKind.String,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
                "object" => value.ValueKind == JsonValueKind.Object,
                _ => false
            };
            if (!valid) throw new InvalidOperationException($"Argument '{parameter.Name}' must be {parameter.Type}.");
        }
    }
}
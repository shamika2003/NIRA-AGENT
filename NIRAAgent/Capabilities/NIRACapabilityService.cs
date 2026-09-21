using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NIRAAgent.Authorization;

namespace NIRAAgent.Capabilities;

public sealed class NIRACapabilityService
{
    private const int MaximumResultOutputCharacters = 24000;
    private readonly NIRACapabilityRegistry _registry;
    private readonly INIRACapabilityAuthorizer _authorizer;
    private readonly NIRACapabilityRequestPolicy _policy;
    private readonly NIRAAuthorityStore _audit;
    // Serialize direct filesystem mutations within this runtime. This prevents
    // concurrent append/replacement and parent-delete races between branches.
    private readonly SemaphoreSlim _filesystemMutations = new(1, 1);

    public NIRACapabilityService(NIRACapabilityRegistry registry, INIRACapabilityAuthorizer authorizer,
        NIRACapabilityRequestPolicy policy, NIRAAuthorityStore audit)
    {
        _registry = registry; _authorizer = authorizer; _policy = policy; _audit = audit;
        Debug.WriteLine($"[Capabilities] READY | Registered={_registry.Descriptors.Count} | StateChangingAuthorization=PersistentScopes");
    }

    public string BuildCognitionContext()
    {
        StringBuilder text = new();
        text.AppendLine("NIRA TRUSTED PRIMITIVE CAPABILITIES");
        text.AppendLine(_authorizer.BuildCognitionContext());
        text.AppendLine("AVAILABLE PRIMITIVES");
        foreach (NIRACapabilityDescriptor descriptor in _registry.Descriptors)
        {
            text.AppendLine($"- {descriptor.Id} | defaultRisk={descriptor.DefaultRisk} | {descriptor.Description}");
            foreach (NIRACapabilityParameterDescriptor parameter in descriptor.Parameters)
                text.AppendLine($"  - {parameter.Name}: {parameter.Type} | required={parameter.Required} | {parameter.Description}");
        }
        return text.ToString().Trim();
    }

    public async Task<IReadOnlyList<NIRACapabilityResult>> ExecuteAsync(
        IReadOnlyList<NIRACapabilityRequest> requests, CancellationToken cancellationToken = default)
    {
        List<NIRACapabilityResult> results = [];
        if (requests == null) return results;
        foreach (NIRACapabilityRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NIRACapabilityResult result = await ExecuteOneAsync(request, cancellationToken);
            results.Add(result);
            Debug.WriteLine($"[Capability] {result.Status.ToString().ToUpperInvariant()} | Id={result.CapabilityId} | " +
                $"Risk={result.Risk} | Changed={result.ChangedSystemState} | Uncertain={result.OutcomeUncertain} | " +
                $"ExitCode={result.ExitCode?.ToString() ?? "-"} | Audit={result.AuditAttemptId?.ToString("D") ?? "-"}");
            // Structured, NON-SECRET diagnostics. Do not write raw exception
            // messages: browser sites may include private form data in errors.
            if (result.CapabilityId == NIRACapabilityIds.BrowserAuthenticate &&
                result.Status != NIRACapabilityResultStatus.Succeeded)
            {
                string reason = result.Summary;
                string stage = reason.Contains("MissingLoginFields", StringComparison.OrdinalIgnoreCase) ? "MissingLoginFields" :
                    reason.Contains("StaleLoginFields", StringComparison.OrdinalIgnoreCase) ? "StaleLoginFields" :
                    reason.Contains("different observed login", StringComparison.OrdinalIgnoreCase) ? "AccountRouteMismatch" :
                    reason.Contains("No login submission was recorded", StringComparison.OrdinalIgnoreCase) ? "NoSubmissionRecorded" :
                    reason.Contains("not shown a fresh, explicit rejection", StringComparison.OrdinalIgnoreCase) ? "NoWebsiteRejection" :
                    reason.Contains("grounded HTTP/HTTPS origin", StringComparison.OrdinalIgnoreCase) ? "PageOwnershipOrOrigin" :
                    reason.Contains("credential was selected", StringComparison.OrdinalIgnoreCase) ? "CredentialPromptCancelled" :
                    reason.Contains("before login submission", StringComparison.OrdinalIgnoreCase) ||
                    reason.Contains("did not reach login submission", StringComparison.OrdinalIgnoreCase) ? "PreSubmissionFailure" :
                    reason.Contains("may have been dispatched", StringComparison.OrdinalIgnoreCase) ? "PossibleSubmittedFailure" :
                    result.Status == NIRACapabilityResultStatus.Rejected ? "RejectedBeforeDispatch" : "OtherFailure";
                Debug.WriteLine($"[AuthDiagnostics] Stage={stage} | " +
                    $"Uncertain={result.OutcomeUncertain} | Audit={result.AuditAttemptId?.ToString("D") ?? "-"}");
            }
        }
        return results;
    }

    private async Task<NIRACapabilityResult> ExecuteOneAsync(NIRACapabilityRequest raw, CancellationToken ct)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        NIRACapabilityRequest request = raw;
        NIRACapabilityRisk risk = NIRACapabilityRisk.Observe;
        Guid? attempt = null;
        Guid? scope = null;
        bool dispatched = false;
        bool mutationGateTaken = false;
        NIRACapabilityResult result;
        try
        {
            request = raw.Normalize();
            if (!_registry.TryResolve(request.CapabilityId, out INIRACapabilityHandler? handler) || handler == null)
                throw new InvalidOperationException("The capability ID is not registered.");
            NIRACapabilityDescriptor descriptor = handler.Descriptor.Normalize();

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
            NIRAAuthorityOperation operation = _policy.Describe(request, risk);
            // No handler can run unless its durable audit row already exists.
            attempt = _audit.BeginAttempt(request, operation.Target, operation.Fingerprint);
            NIRACapabilityAuthorizationDecision decision = await _authorizer.AuthorizeAsync(descriptor, risk, request, ct);
            if (decision.Status == NIRACapabilityAuthorizationStatus.Allowed)
            {
                if (risk != NIRACapabilityRisk.Observe &&
                    (request.CapabilityId.StartsWith("filesystem.", StringComparison.Ordinal) ||
                     request.CapabilityId == NIRACapabilityIds.HttpDownload))
                {
                    await _filesystemMutations.WaitAsync(ct);
                    mutationGateTaken = true;
                }
                decision = await _authorizer.ValidateForDispatchAsync(descriptor, risk, request, decision, ct);
            }
            scope = decision.ScopeId;
            _audit.RecordDecision(attempt.Value, decision);
            ct.ThrowIfCancellationRequested();
            if (decision.Status != NIRACapabilityAuthorizationStatus.Allowed)
            {
                result = Make(decision.Status == NIRACapabilityAuthorizationStatus.AuthorizationRequired
                    ? NIRACapabilityResultStatus.AuthorizationRequired : NIRACapabilityResultStatus.Rejected, decision.Reason);
            }
            else
            {
                if (decision.DerivedFromDirectUserRequest &&
                    request.CapabilityId == NIRACapabilityIds.FileWrite)
                {
                    request = request with { RequireCreateNew = true };
                }
                dispatched = true;
                NIRACapabilityHandlerResult handled = await handler.ExecuteAsync(request, ct);
                result = Make(handled.Succeeded ? NIRACapabilityResultStatus.Succeeded : NIRACapabilityResultStatus.Failed,
                    NIRACapabilityArguments.Truncate(handled.Summary, 2000)) with
                {
                    Output = NIRACapabilityArguments.Truncate(handled.Output, MaximumResultOutputCharacters),
                    ChangedSystemState = handled.ChangedSystemState,
                    ExitCode = handled.ExitCode,
                    HttpStatusCode = handled.HttpStatusCode
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result = Make(NIRACapabilityResultStatus.Failed, dispatched
                ? "Cancelled after dispatch. Inspect the target before retrying; partial effects may remain."
                : "Cancelled before dispatch. No handler was started.") with
            { OutcomeUncertain = dispatched && risk != NIRACapabilityRisk.Observe };
            TryFinish(result);
            throw;
        }
        catch (Exception ex)
        {
            result = Make(dispatched ? NIRACapabilityResultStatus.Failed : NIRACapabilityResultStatus.Rejected,
                ex.Message + (dispatched && risk != NIRACapabilityRisk.Observe
                    ? " Partial effects may remain; inspect before retrying." : "")) with
            { OutcomeUncertain = dispatched && risk != NIRACapabilityRisk.Observe };
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

        NIRACapabilityResult Make(NIRACapabilityResultStatus status, string summary) => new()
        {
            RequestId = request.RequestId, CapabilityId = request.CapabilityId, Risk = risk,
            Status = status, Summary = summary, StartedAtUtc = started, FinishedAtUtc = DateTimeOffset.UtcNow,
            AuthorizationScopeId = scope, AuditAttemptId = attempt
        };
        bool TryFinish(NIRACapabilityResult value)
        {
            if (!attempt.HasValue) return false;
            try { _audit.FinishAttempt(attempt.Value, value); return true; }
            catch (Exception ex) { Debug.WriteLine($"[AuthorityAudit] Result save failed: {ex.GetType().Name}"); return false; }
        }
    }

    private static void ValidateArguments(NIRACapabilityRequest request, NIRACapabilityDescriptor descriptor)
    {
        Dictionary<string, JsonElement> supplied = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in request.Arguments.EnumerateObject())
        {
            if (!supplied.TryAdd(property.Name, property.Value))
                throw new InvalidOperationException($"Duplicate argument '{property.Name}'.");
            if (!descriptor.Parameters.Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Unknown argument '{property.Name}' for {descriptor.Id}.");
        }
        foreach (NIRACapabilityParameterDescriptor parameter in descriptor.Parameters)
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

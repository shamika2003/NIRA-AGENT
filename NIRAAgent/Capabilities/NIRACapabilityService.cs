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

    // Model-generated capability calls are preflighted by the Executive before
    // dispatch so malformed shadow requests can be rejected without creating audit
    // noise or consuming a machine-action round trip. This performs ONLY structural
    // schema normalization/validation. It does not prepare paths, authorize, bind
    // browser state, or execute anything.
    public bool TryNormalizeAndValidateSchema(
        NIRACapabilityRequest raw,
        out NIRACapabilityRequest normalized,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(
            raw);

        normalized =
            raw;

        error =
            string.Empty;

        try
        {
            normalized =
                NormalizeCapabilityArgumentAliases(
                    raw.Normalize());

            if (!_registry.TryResolve(
                    normalized.CapabilityId,
                    out INIRACapabilityHandler? handler)
                ||
                handler ==
                    null)
            {
                error =
                    $"Capability '{normalized.CapabilityId}' is not registered.";

                return false;
            }

            NIRACapabilityDescriptor descriptor =
                handler.Descriptor.Normalize();

            normalized =
                NormalizeCapabilityArgumentsToDescriptor(
                    normalized,
                    descriptor,
                    handler);

            ValidateArguments(
                normalized,
                descriptor);

            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            error =
                ex.Message;

            return false;
        }
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

            bool browserFailure =
                result.CapabilityId.StartsWith(
                    "browser.",
                    StringComparison.OrdinalIgnoreCase)
                &&
                result.Status !=
                    NIRACapabilityResultStatus.Succeeded;

            if (result.CapabilityId.StartsWith("browser.", StringComparison.OrdinalIgnoreCase))
                Debug.WriteLine($"[BrowserFlow] DISPATCH RESULT | Id={result.CapabilityId} | " +
                    $"Status={result.Status} | Risk={result.Risk} | " +
                    $"OutputChars={result.Output.Length} | Uncertain={result.OutcomeUncertain} | " +
                    $"ElapsedMs={(result.FinishedAtUtc - result.StartedAtUtc).TotalMilliseconds:F0} | " +
                    $"Audit={result.AuditAttemptId?.ToString("D") ?? "-"}");
            if (result.CapabilityId.StartsWith("browser.", StringComparison.OrdinalIgnoreCase) &&
                result.Status != NIRACapabilityResultStatus.Succeeded)
            {
                string failureKind = result.Summary.StartsWith("UngroundedLoopbackNavigation:", StringComparison.Ordinal)
                    ? "UngroundedLoopback" :
                    result.Summary.StartsWith("BrowserNavigationFailed:", StringComparison.Ordinal)
                    ? "NavigationFailedRestored" :
                    result.Summary.StartsWith("BrowserNavigationUncertain:", StringComparison.Ordinal)
                    ? "NavigationUncertain" :
                    result.Summary.StartsWith("BrowserErrorDocument:", StringComparison.Ordinal)
                    ? "BrowserErrorDocument" :
                    result.Summary.Contains("Unknown argument", StringComparison.Ordinal)
                    ? "ArgumentSchemaMismatch" : "Other";
                Debug.WriteLine($"[BrowserFlow] ERROR | Capability={result.CapabilityId} | " +
                    $"Kind={failureKind} | Status={result.Status} | " +
                    $"Uncertain={result.OutcomeUncertain} | Audit={result.AuditAttemptId?.ToString("D") ?? "-"}");
            }
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

            // A direct capability array is executed in order. If one browser
            // operation fails/rejects, later browser actions may depend on state
            // that was never established (for example fill -> submit). Stop this
            // batch and return to cognition instead of dispatching potentially
            // unsafe or meaningless follow-up actions. Independent work belongs
            // in explicit branches/another decision, where ownership is clear.
            if (browserFailure)
            {
                bool hasRemainingRequests =
                    results.Count <
                        requests.Count;

                if (hasRemainingRequests)
                {
                    Debug.WriteLine(
                        $"[BrowserFlow] BATCH STOP | Failed={result.CapabilityId} | " +
                        $"Status={result.Status} | Remaining={requests.Count - results.Count} | " +
                        "Reason=PriorBrowserStepFailed");
                }

                break;
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
            request =
                NormalizeCapabilityArgumentAliases(
                    raw.Normalize());

            if (!_registry.TryResolve(request.CapabilityId, out INIRACapabilityHandler? handler) || handler == null)
                throw new InvalidOperationException("The capability ID is not registered.");
            NIRACapabilityDescriptor descriptor = handler.Descriptor.Normalize();

            request =
                NormalizeCapabilityArgumentsToDescriptor(
                    request,
                    descriptor,
                    handler);

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
                    VisualArtifacts = (handled.VisualArtifacts ?? Array.Empty<NIRACapabilityVisualArtifact>())
                        .Take(4)
                        .Select(artifact => artifact.Normalize())
                        .ToArray(),
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
            if (request.CapabilityId.StartsWith("browser.", StringComparison.OrdinalIgnoreCase))
            {
                // Log NAMES, never values: URLs may carry tokens and credential
                // arguments must not appear in a diagnostic transcript.
                string argumentNames = request.Arguments.ValueKind == JsonValueKind.Object
                    ? string.Join(",", request.Arguments.EnumerateObject().Select(p => p.Name))
                    : "invalid-json";
                Debug.WriteLine($"[BrowserFlow] DISPATCH FAILURE | Id={request.CapabilityId} | " +
                    $"Stage={(dispatched ? "AfterDispatch" : "BeforeDispatch")} | " +
                    $"ArgumentNames={argumentNames} | ExceptionType={ex.GetType().Name}");
            }
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

    // Canonicalize a deliberately small set of capability-API vocabulary aliases
    // before validation/authorization. These are schema compatibility aliases,
    // not user-intent phrase heuristics, app-name tables, or website-specific routing.
    //
    // Keeping normalization here means every handler, policy and audit path sees one
    // canonical schema. If both an alias and its canonical name are supplied, reject
    // the ambiguous request instead of guessing which value the model intended.
    private static NIRACapabilityRequest NormalizeCapabilityArgumentAliases(
        NIRACapabilityRequest request)
    {
        if (request.Arguments.ValueKind != JsonValueKind.Object)
        {
            return request;
        }

        bool browserCapability =
            request.CapabilityId.StartsWith(
                "browser.",
                StringComparison.OrdinalIgnoreCase);

        bool applicationResolve =
            request.CapabilityId ==
            NIRACapabilityIds.ApplicationResolve;

        bool processStart =
            request.CapabilityId ==
            NIRACapabilityIds.ProcessStart;

        if (!browserCapability &&
            !applicationResolve &&
            !processStart)
        {
            return request;
        }

        Dictionary<string, JsonElement> canonical =
            new(
                StringComparer.OrdinalIgnoreCase);

        bool changed =
            false;

        foreach (JsonProperty property in
                 request.Arguments.EnumerateObject())
        {
            string name =
                property.Name;

            string mapped =
                applicationResolve &&
                (name.Equals(
                     "applicationName",
                     StringComparison.OrdinalIgnoreCase)
                 ||
                 name.Equals(
                     "appName",
                     StringComparison.OrdinalIgnoreCase)
                 ||
                 name.Equals(
                     "name",
                     StringComparison.OrdinalIgnoreCase))
                    ? "query"
                    : processStart &&
                      (name.Equals(
                           "executable",
                           StringComparison.OrdinalIgnoreCase)
                       ||
                       name.Equals(
                           "executablePath",
                           StringComparison.OrdinalIgnoreCase)
                       ||
                       name.Equals(
                           "program",
                           StringComparison.OrdinalIgnoreCase))
                        ? "fileName"
                        : processStart &&
                          name.Equals(
                              "args",
                              StringComparison.OrdinalIgnoreCase)
                            ? "arguments"
                            : browserCapability &&
                              name.Equals(
                                  "elementRef",
                                  StringComparison.OrdinalIgnoreCase)
                                ? "ref"
                                : browserCapability &&
                                  request.CapabilityId ==
                                      NIRACapabilityIds.BrowserFill
                                  &&
                                  name.Equals(
                                      "text",
                                      StringComparison.OrdinalIgnoreCase)
                                    ? "value"
                                    : name;

            if (!canonical.TryAdd(
                    mapped,
                    property.Value.Clone()))
            {
                throw new InvalidOperationException(
                    $"Supply only one value for capability argument '{mapped}'.");
            }

            if (!string.Equals(
                    mapped,
                    name,
                    StringComparison.Ordinal))
            {
                changed =
                    true;
            }
        }

        if (!changed)
        {
            return request;
        }

        return request with
        {
            Arguments =
                JsonSerializer.SerializeToElement(
                    canonical)
        };
    }


    private static NIRACapabilityRequest NormalizeCapabilityArgumentsToDescriptor(
        NIRACapabilityRequest request,
        NIRACapabilityDescriptor descriptor,
        INIRACapabilityHandler handler)
    {
        if (request.Arguments.ValueKind != JsonValueKind.Object)
        {
            return request;
        }

        Dictionary<string, NIRACapabilityParameterDescriptor> declared =
            descriptor.Parameters.ToDictionary(
                parameter => parameter.Name,
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, JsonElement> canonical =
            new(StringComparer.OrdinalIgnoreCase);

        List<string> unknownNames =
            new();

        bool changed =
            false;

        foreach (JsonProperty property in request.Arguments.EnumerateObject())
        {
            if (!declared.TryGetValue(
                    property.Name,
                    out NIRACapabilityParameterDescriptor? parameter))
            {
                unknownNames.Add(
                    property.Name);

                if (!canonical.TryAdd(
                        property.Name,
                        property.Value.Clone()))
                {
                    throw new InvalidOperationException(
                        $"Duplicate argument '{property.Name}'.");
                }

                continue;
            }

            JsonElement normalizedValue =
                property.Value.Clone();

            string parameterType =
                parameter.Type.Trim().ToLowerInvariant();

            if (parameterType == "integer")
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    int.TryParse(
                        property.Value.GetString(),
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int parsedInteger))
                {
                    normalizedValue =
                        JsonSerializer.SerializeToElement(
                            parsedInteger);

                    changed =
                        true;
                }
                else if (property.Value.ValueKind == JsonValueKind.Number &&
                         !property.Value.TryGetInt32(out _) &&
                         property.Value.TryGetDouble(out double numericInteger) &&
                         double.IsFinite(numericInteger) &&
                         numericInteger >= int.MinValue &&
                         numericInteger <= int.MaxValue &&
                         numericInteger == Math.Truncate(numericInteger))
                {
                    normalizedValue =
                        JsonSerializer.SerializeToElement(
                            (int)numericInteger);

                    changed =
                        true;
                }
            }
            else if (parameterType == "boolean" &&
                     property.Value.ValueKind == JsonValueKind.String &&
                     bool.TryParse(
                         property.Value.GetString(),
                         out bool parsedBoolean))
            {
                normalizedValue =
                    JsonSerializer.SerializeToElement(
                        parsedBoolean);

                changed =
                    true;
            }

            if (!canonical.TryAdd(
                    parameter.Name,
                    normalizedValue))
            {
                throw new InvalidOperationException(
                    $"Supply only one value for capability argument '{parameter.Name}'.");
            }

            if (!string.Equals(
                    property.Name,
                    parameter.Name,
                    StringComparison.Ordinal))
            {
                changed =
                    true;
            }
        }

        NIRACapabilityRequest normalized =
            changed
                ? request with
                {
                    Arguments =
                        JsonSerializer.SerializeToElement(
                            canonical)
                }
                : request;

        if (unknownNames.Count ==
            0)
        {
            return normalized;
        }

        // Never silently repair browser actions or anything that can resolve
        // above Observe risk. For a local read-only primitive, however, an
        // undeclared model metadata field cannot grant extra authority. If the
        // request is complete and valid WITHOUT those unknown fields and the
        // trusted handler still classifies it as Observe, discard only the
        // undeclared fields instead of burning another model cycle.
        if (descriptor.Id.StartsWith(
                "browser.",
                StringComparison.OrdinalIgnoreCase) ||
            descriptor.DefaultRisk !=
                NIRACapabilityRisk.Observe)
        {
            return normalized;
        }

        Dictionary<string, JsonElement> declaredOnly =
            canonical
                .Where(pair =>
                    declared.ContainsKey(
                        pair.Key))
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase);

        NIRACapabilityRequest repaired =
            request with
            {
                Arguments =
                    JsonSerializer.SerializeToElement(
                        declaredOnly)
            };

        try
        {
            ValidateArguments(
                repaired,
                descriptor);

            if (handler.ResolveRisk(
                    repaired) !=
                NIRACapabilityRisk.Observe)
            {
                return normalized;
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or ArgumentException
            or JsonException)
        {
            return normalized;
        }

        Debug.WriteLine(
            $"[CapabilitySchema] SAFE OBSERVE REPAIR | " +
            $"Capability={descriptor.Id} | " +
            $"DroppedUndeclared={string.Join(",", unknownNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))}");

        return repaired;
    }


    private static void ValidateArguments(NIRACapabilityRequest request, NIRACapabilityDescriptor descriptor)
    {
        Dictionary<string, JsonElement> supplied = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in request.Arguments.EnumerateObject())
        {
            if (!supplied.TryAdd(property.Name, property.Value))
                throw new InvalidOperationException($"Duplicate argument '{property.Name}'.");
            bool browserRefAlias = descriptor.Id.StartsWith("browser.", StringComparison.OrdinalIgnoreCase) &&
                property.Name.Equals("elementRef", StringComparison.OrdinalIgnoreCase) &&
                descriptor.Parameters.Any(p => p.Name.Equals("ref", StringComparison.OrdinalIgnoreCase));
            if (!browserRefAlias && !descriptor.Parameters.Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                string declared = descriptor.Parameters.Count == 0
                    ? "(none)"
                    : string.Join(", ", descriptor.Parameters.Select(parameter =>
                        $"{parameter.Name}:{parameter.Type}{(parameter.Required ? " required" : " optional")}"));
                throw new InvalidOperationException(
                    $"Unknown argument '{property.Name}' for {descriptor.Id}. Declared arguments: {declared}.");
            }
        }
        if (descriptor.Id.StartsWith("browser.", StringComparison.OrdinalIgnoreCase) &&
            supplied.ContainsKey("elementRef") && supplied.ContainsKey("ref"))
            throw new InvalidOperationException("Supply only 'ref' or 'elementRef', not both.");
        foreach (NIRACapabilityParameterDescriptor parameter in descriptor.Parameters)
        {
            bool hasValue = supplied.TryGetValue(parameter.Name, out JsonElement value);
            if (!hasValue && parameter.Name.Equals("ref", StringComparison.OrdinalIgnoreCase) &&
                descriptor.Id.StartsWith("browser.", StringComparison.OrdinalIgnoreCase))
                hasValue = supplied.TryGetValue("elementRef", out value);
            if (!hasValue || value.ValueKind == JsonValueKind.Null)
            {
                // The browser policy can bind the current exact, task-owned
                // inspected page for these specific ref actions. It must
                // still verify ownership, live origin and latest DOM ref
                // before the handler ever executes. All other required
                // arguments (including refs) retain strict validation.
                bool runtimeBoundPage = parameter.Name.Equals("pageId", StringComparison.OrdinalIgnoreCase) &&
                    NIRACapabilityRequestPolicy.IsBrowserPageAction(descriptor.Id);
                if (parameter.Required && !runtimeBoundPage)
                {
                    string declared = string.Join(", ", descriptor.Parameters.Select(item =>
                        $"{item.Name}:{item.Type}{(item.Required ? " required" : " optional")}"));
                    throw new InvalidOperationException(
                        $"Required argument '{parameter.Name}' is missing for {descriptor.Id}. Declared arguments: {declared}.");
                }
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

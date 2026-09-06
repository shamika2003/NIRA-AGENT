/*
 * filename: SegaDynamicToolExecutor.cs
 */

using System.Text.Json;
using System.Text.RegularExpressions;

using SegaAgent.Capabilities;

namespace SegaAgent.Tools;

public sealed class SegaDynamicToolExecutor
{
    private static readonly Regex TemplateTokenRegex =
        new(
            @"\$\{(?<kind>param|step):(?<name>[A-Za-z][A-Za-z0-9_]{0,63})(?:\.(?<field>output|summary|exitCode|httpStatusCode|succeeded))?\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly SegaDynamicToolValidator _validator;
    private readonly SegaCapabilityService _capabilities;

    public SegaDynamicToolExecutor(
        SegaDynamicToolValidator validator,
        SegaCapabilityService capabilities)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public async Task<SegaDynamicToolExecutionResult> ExecuteAsync(
        SegaDynamicToolDefinition definition,
        SegaDynamicToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        SegaDynamicToolDefinition tool = _validator.ValidateDefinition(definition);

        if (tool.Status != SegaDynamicToolStatus.Active)
        {
            string unavailable =
                $"Dynamic tool '{tool.Name}' is {tool.Status}.";

            return Result(
                succeeded: false,
                wasAborted: true,
                summary: unavailable,
                failureReason: unavailable,
                stepResults: Array.Empty<SegaDynamicToolStepExecutionResult>());
        }

        IReadOnlyDictionary<string, JsonElement> parameters =
            _validator.ResolveInvocationArguments(tool, invocation);

        IReadOnlyList<SegaDynamicToolStep> order =
            _validator.GetExecutionOrder(tool);

        Dictionary<string, int> originalOrder =
            order
                .Select((step, index) => (step.Id, index))
                .ToDictionary(
                    value => value.Id,
                    value => value.index,
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, SegaDynamicToolStepExecutionResult> byStep =
            new(StringComparer.OrdinalIgnoreCase);

        List<SegaDynamicToolStepExecutionResult> results =
            new();

        List<SegaDynamicToolStep> pending =
            order.ToList();

        bool aborted = false;
        string abortReason = string.Empty;

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(
            TimeSpan.FromSeconds(
                tool.TimeoutSeconds));

        try
        {
            while (pending.Count > 0)
            {
                timeout.Token.ThrowIfCancellationRequested();

                if (aborted)
                {
                    foreach (SegaDynamicToolStep step in pending)
                    {
                        AddSkipped(
                            step,
                            "Not executed because an earlier step stopped the tool.");
                    }

                    pending.Clear();
                    break;
                }

                SegaDynamicToolStep[] ready =
                    pending
                        .Where(
                            step =>
                                step.DependsOnStepIds.All(
                                    byStep.ContainsKey))
                        .OrderBy(
                            step => originalOrder[step.Id])
                        .ToArray();

                if (ready.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Dynamic tool execution could not find a ready step. " +
                        "The validated dependency graph is inconsistent.");
                }

                SegaDynamicToolStep[] batch =
                    tool.AllowIndependentConcurrency
                        ? ready
                        : new[]
                        {
                            ready[0]
                        };

                List<SegaDynamicToolStep> executable =
                    new();

                foreach (SegaDynamicToolStep step in batch)
                {
                    if (!ConditionMatches(
                            step.Condition,
                            parameters,
                            byStep))
                    {
                        AddSkipped(
                            step,
                            "Condition evaluated false.");

                        pending.Remove(
                            step);

                        continue;
                    }

                    executable.Add(
                        step);
                }

                if (executable.Count == 0)
                {
                    continue;
                }

                Dictionary<string, SegaDynamicToolStepExecutionResult> completed =
                    tool.AllowIndependentConcurrency && executable.Count > 1
                        ? await ExecuteBatchAsync(
                            tool,
                            executable,
                            parameters,
                            byStep,
                            timeout.Token)
                        : new Dictionary<string, SegaDynamicToolStepExecutionResult>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [executable[0].Id] =
                                await ExecuteStepAsync(
                                    tool,
                                    executable[0],
                                    parameters,
                                    byStep,
                                    timeout.Token)
                        };

                foreach (SegaDynamicToolStep step in executable
                             .OrderBy(value => originalOrder[value.Id]))
                {
                    SegaDynamicToolStepExecutionResult stepResult =
                        completed[step.Id];

                    results.Add(
                        stepResult);

                    byStep[step.Id] =
                        stepResult;

                    pending.Remove(
                        step);

                    if (stepResult.Status !=
                        SegaDynamicToolStepStatus.Failed)
                    {
                        continue;
                    }

                    if (!MustStop(
                            step,
                            stepResult,
                            out string reason))
                    {
                        continue;
                    }

                    aborted = true;

                    if (string.IsNullOrWhiteSpace(
                            abortReason))
                    {
                        abortReason =
                            reason;
                    }
                }
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            aborted = true;
            abortReason =
                $"Dynamic tool timed out after {tool.TimeoutSeconds} seconds.";

            foreach (SegaDynamicToolStep step in pending
                         .Where(value => !byStep.ContainsKey(value.Id)))
            {
                AddSkipped(
                    step,
                    "Not executed because the dynamic tool timed out.");
            }

            pending.Clear();
        }

        HashSet<string> dependedOn =
            tool.Steps
                .SelectMany(
                    value => value.DependsOnStepIds)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        string[] terminalIds =
            tool.Steps
                .Where(
                    value => !dependedOn.Contains(value.Id))
                .Select(
                    value => value.Id)
                .ToArray();

        SegaDynamicToolStepExecutionResult[] terminalResults =
            terminalIds
                .Where(
                    byStep.ContainsKey)
                .Select(
                    id => byStep[id])
                .ToArray();

        bool anyTerminalSucceeded =
            terminalResults.Any(
                value =>
                    value.Status ==
                    SegaDynamicToolStepStatus.Succeeded);

        bool terminalClean =
            terminalResults.All(
                value =>
                    value.Status is
                        SegaDynamicToolStepStatus.Succeeded or
                        SegaDynamicToolStepStatus.Skipped);

        bool succeededTool =
            !aborted &&
            anyTerminalSucceeded &&
            terminalClean;

        string failureReason =
            succeededTool
                ? string.Empty
                : BuildFailureReason(
                    abortReason,
                    results);

        string summary =
            succeededTool
                ? $"Dynamic tool '{tool.Name}' v{tool.Version} completed successfully " +
                  $"({results.Count(value => value.Status == SegaDynamicToolStepStatus.Succeeded)} step(s) succeeded)."
                : !string.IsNullOrWhiteSpace(abortReason)
                    ? abortReason
                    : $"Dynamic tool '{tool.Name}' v{tool.Version} finished without satisfying a successful terminal step.";

        return Result(
            succeededTool,
            aborted,
            summary,
            failureReason,
            results);

        void AddSkipped(
            SegaDynamicToolStep step,
            string summary)
        {
            SegaDynamicToolStepExecutionResult skipped =
                new()
                {
                    StepId = step.Id,
                    Status = SegaDynamicToolStepStatus.Skipped,
                    Summary = summary
                };

            results.Add(
                skipped);

            byStep[step.Id] =
                skipped;
        }

        SegaDynamicToolExecutionResult Result(
            bool succeeded,
            bool wasAborted,
            string summary,
            string failureReason,
            IReadOnlyList<SegaDynamicToolStepExecutionResult> stepResults) =>
            new()
            {
                ToolId = tool.Id,
                ToolName = tool.Name,
                ToolVersion = tool.Version,
                Succeeded = succeeded,
                Aborted = wasAborted,
                FailureReason = failureReason,
                Summary = summary,
                StepResults = stepResults,
                StartedAtUtc = started,
                FinishedAtUtc = DateTimeOffset.UtcNow
            };
    }

    private async Task<Dictionary<string, SegaDynamicToolStepExecutionResult>> ExecuteBatchAsync(
        SegaDynamicToolDefinition tool,
        IReadOnlyList<SegaDynamicToolStep> steps,
        IReadOnlyDictionary<string, JsonElement> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStepExecutionResult> previousResults,
        CancellationToken cancellationToken)
    {
        Task<SegaDynamicToolStepExecutionResult>[] tasks =
            steps
                .Select(
                    step => ExecuteStepAsync(
                        tool,
                        step,
                        parameters,
                        previousResults,
                        cancellationToken))
                .ToArray();

        SegaDynamicToolStepExecutionResult[] completed =
            await Task.WhenAll(
                tasks);

        return completed.ToDictionary(
            value => value.StepId,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<SegaDynamicToolStepExecutionResult> ExecuteStepAsync(
        SegaDynamicToolDefinition tool,
        SegaDynamicToolStep step,
        IReadOnlyDictionary<string, JsonElement> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStepExecutionResult> previousResults,
        CancellationToken cancellationToken)
    {
        try
        {
            JsonElement arguments =
                ResolveStepArguments(
                    step,
                    parameters,
                    previousResults);

            SegaCapabilityRequest request =
                new SegaCapabilityRequest
                {
                    CapabilityId = step.CapabilityId,
                    Arguments = arguments,
                    Reason =
                        $"Dynamic tool '{tool.Name}' v{tool.Version}, step '{step.Id}': {step.Description}".Trim()
                }
                .Normalize();

            IReadOnlyList<SegaCapabilityResult> capabilityResults =
                await _capabilities.ExecuteAsync(
                    new[]
                    {
                        request
                    },
                    cancellationToken);

            SegaCapabilityResult capability =
                capabilityResults.Single();

            bool succeeded =
                capability.Status ==
                SegaCapabilityResultStatus.Succeeded;

            return new SegaDynamicToolStepExecutionResult
            {
                StepId = step.Id,
                Status =
                    succeeded
                        ? SegaDynamicToolStepStatus.Succeeded
                        : SegaDynamicToolStepStatus.Failed,
                Summary = capability.Summary,
                Request = request,
                CapabilityResult = capability
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SegaDynamicToolStepExecutionResult
            {
                StepId = step.Id,
                Status = SegaDynamicToolStepStatus.Failed,
                Summary =
                    $"Dynamic tool step could not resolve/dispatch safely: {ex.Message}"
            };
        }
    }

    private static bool MustStop(
        SegaDynamicToolStep step,
        SegaDynamicToolStepExecutionResult result,
        out string reason)
    {
        SegaCapabilityResult? capability =
            result.CapabilityResult;

        if (capability == null)
        {
            reason =
                $"Step '{step.Id}' failed before a trusted primitive result was produced.";

            return true;
        }

        bool authorityBlock =
            capability.Status is
                SegaCapabilityResultStatus.AuthorizationRequired or
                SegaCapabilityResultStatus.Rejected;

        if (authorityBlock)
        {
            reason =
                $"Step '{step.Id}' could not proceed through the Stage 10 authority boundary.";

            return true;
        }

        if (capability.OutcomeUncertain)
        {
            reason =
                $"Step '{step.Id}' has an uncertain outcome and requires inspection before continuing.";

            return true;
        }

        if (step.FailurePolicy ==
            SegaDynamicToolFailurePolicy.StopTool)
        {
            reason =
                $"Step '{step.Id}' failed and its failure policy stops the tool.";

            return true;
        }

        reason =
            string.Empty;

        return false;
    }

    private static string BuildFailureReason(
        string abortReason,
        IReadOnlyList<SegaDynamicToolStepExecutionResult> results)
    {
        if (!string.IsNullOrWhiteSpace(
                abortReason))
        {
            return abortReason;
        }

        string[] failed =
            results
                .Where(
                    value =>
                        value.Status ==
                        SegaDynamicToolStepStatus.Failed)
                .Take(4)
                .Select(
                    value =>
                    {
                        SegaCapabilityResult? capability =
                            value.CapabilityResult;

                        if (capability == null)
                        {
                            return
                                $"{value.StepId}: {value.Summary}";
                        }

                        string details =
                            capability.ExitCode.HasValue
                                ? $"exit={capability.ExitCode.Value}"
                                : capability.HttpStatusCode.HasValue
                                    ? $"http={capability.HttpStatusCode.Value}"
                                    : capability.Status.ToString();

                        return
                            $"{value.StepId}: {details} | {capability.Summary}";
                    })
                .ToArray();

        return failed.Length == 0
            ? "No terminal step produced authoritative success evidence."
            : string.Join(
                " || ",
                failed);
    }

    private static bool ConditionMatches(
        SegaDynamicToolCondition condition,
        IReadOnlyDictionary<string, JsonElement> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStepExecutionResult> results)
    {
        return condition.Kind switch
        {
            SegaDynamicConditionKind.Always =>
                true,

            SegaDynamicConditionKind.ParameterEquals =>
                condition.ParameterName != null &&
                condition.ExpectedValue.HasValue &&
                parameters.TryGetValue(
                    condition.ParameterName,
                    out JsonElement actual) &&
                JsonEquals(
                    actual,
                    condition.ExpectedValue.Value),

            SegaDynamicConditionKind.StepSucceeded =>
                condition.StepId != null &&
                results.TryGetValue(
                    condition.StepId,
                    out SegaDynamicToolStepExecutionResult? succeeded) &&
                succeeded.Status ==
                    SegaDynamicToolStepStatus.Succeeded,

            SegaDynamicConditionKind.StepFailed =>
                condition.StepId != null &&
                results.TryGetValue(
                    condition.StepId,
                    out SegaDynamicToolStepExecutionResult? failed) &&
                failed.Status ==
                    SegaDynamicToolStepStatus.Failed,

            _ =>
                false
        };
    }

    private static JsonElement ResolveStepArguments(
        SegaDynamicToolStep step,
        IReadOnlyDictionary<string, JsonElement> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStepExecutionResult> results)
    {
        Dictionary<string, JsonElement> values =
            new(
                StringComparer.OrdinalIgnoreCase);

        foreach ((string name, SegaDynamicToolValueBinding binding)
                 in step.Arguments)
        {
            if (!TryResolveBinding(
                    binding,
                    parameters,
                    results,
                    out JsonElement value))
            {
                throw new InvalidOperationException(
                    $"Step '{step.Id}' could not resolve argument '{name}' from its declared binding.");
            }

            values[name] =
                value;
        }

        return JsonSerializer.SerializeToElement(
            values);
    }

    private static bool TryResolveBinding(
        SegaDynamicToolValueBinding binding,
        IReadOnlyDictionary<string, JsonElement> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStepExecutionResult> results,
        out JsonElement value)
    {
        value =
            default;

        switch (binding.Source)
        {
            case SegaDynamicValueSource.Literal:
                if (!binding.Literal.HasValue)
                {
                    return false;
                }

                value =
                    binding.Literal.Value.Clone();

                return true;

            case SegaDynamicValueSource.Template:
                if (string.IsNullOrWhiteSpace(
                        binding.Template))
                {
                    return false;
                }

                value =
                    JsonSerializer.SerializeToElement(
                        RenderTemplate(
                            binding.Template,
                            parameters,
                            results));

                return true;

            case SegaDynamicValueSource.Parameter:
                return
                    binding.ParameterName != null &&
                    parameters.TryGetValue(
                        binding.ParameterName,
                        out value);

            case SegaDynamicValueSource.StepOutput:
                if (!TryGetCapability(
                        binding.StepId,
                        results,
                        out SegaCapabilityResult? outputResult))
                {
                    return false;
                }

                value =
                    JsonSerializer.SerializeToElement(
                        outputResult.Output ??
                        string.Empty);

                return true;

            case SegaDynamicValueSource.StepSummary:
                if (!TryGetCapability(
                        binding.StepId,
                        results,
                        out SegaCapabilityResult? summaryResult))
                {
                    return false;
                }

                value =
                    JsonSerializer.SerializeToElement(
                        summaryResult.Summary ??
                        string.Empty);

                return true;

            case SegaDynamicValueSource.StepExitCode:
                if (!TryGetCapability(
                        binding.StepId,
                        results,
                        out SegaCapabilityResult? exitResult) ||
                    !exitResult.ExitCode.HasValue)
                {
                    return false;
                }

                value =
                    JsonSerializer.SerializeToElement(
                        exitResult.ExitCode.Value);

                return true;

            case SegaDynamicValueSource.StepHttpStatusCode:
                if (!TryGetCapability(
                        binding.StepId,
                        results,
                        out SegaCapabilityResult? httpResult) ||
                    !httpResult.HttpStatusCode.HasValue)
                {
                    return false;
                }

                value =
                    JsonSerializer.SerializeToElement(
                        httpResult.HttpStatusCode.Value);

                return true;

            case SegaDynamicValueSource.StepSucceeded:
                if (binding.StepId == null ||
                    !results.TryGetValue(
                        binding.StepId,
                        out SegaDynamicToolStepExecutionResult? stepResult))
                {
                    return false;
                }

                value =
                    JsonSerializer.SerializeToElement(
                        stepResult.Status ==
                        SegaDynamicToolStepStatus.Succeeded);

                return true;

            default:
                return false;
        }
    }

    private static string RenderTemplate(
        string template,
        IReadOnlyDictionary<string, JsonElement> parameters,
        IReadOnlyDictionary<string, SegaDynamicToolStepExecutionResult> results)
    {
        return TemplateTokenRegex.Replace(
            template,
            match =>
            {
                string kind =
                    match.Groups["kind"].Value;

                string name =
                    match.Groups["name"].Value;

                string field =
                    match.Groups["field"].Value;

                if (kind.Equals(
                        "param",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!parameters.TryGetValue(
                            name,
                            out JsonElement parameter))
                    {
                        throw new InvalidOperationException(
                            $"Template parameter '{name}' is unavailable.");
                    }

                    return JsonElementToText(
                        parameter);
                }

                if (!results.TryGetValue(
                        name,
                        out SegaDynamicToolStepExecutionResult? stepResult))
                {
                    throw new InvalidOperationException(
                        $"Template step result '{name}' is unavailable.");
                }

                return field.ToLowerInvariant() switch
                {
                    "output" =>
                        stepResult.CapabilityResult?.Output ??
                        throw MissingField(name, field),

                    "summary" =>
                        stepResult.CapabilityResult?.Summary ??
                        stepResult.Summary,

                    "exitcode" =>
                        stepResult.CapabilityResult?.ExitCode?.ToString() ??
                        throw MissingField(name, field),

                    "httpstatuscode" =>
                        stepResult.CapabilityResult?.HttpStatusCode?.ToString() ??
                        throw MissingField(name, field),

                    "succeeded" =>
                        (stepResult.Status ==
                         SegaDynamicToolStepStatus.Succeeded)
                            .ToString()
                            .ToLowerInvariant(),

                    _ =>
                        throw MissingField(
                            name,
                            field)
                };
            });
    }

    private static InvalidOperationException MissingField(
        string stepId,
        string field) =>
        new(
            $"Template step '{stepId}' does not expose '{field}' for this execution.");

    private static string JsonElementToText(
        JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String =>
                value.GetString() ??
                string.Empty,

            JsonValueKind.True =>
                "true",

            JsonValueKind.False =>
                "false",

            JsonValueKind.Number =>
                value.GetRawText(),

            JsonValueKind.Object =>
                value.GetRawText(),

            _ =>
                value.GetRawText()
        };
    }

    private static bool TryGetCapability(
        string? stepId,
        IReadOnlyDictionary<string, SegaDynamicToolStepExecutionResult> results,
        out SegaCapabilityResult? capabilityResult)
    {
        capabilityResult =
            null;

        if (stepId == null ||
            !results.TryGetValue(
                stepId,
                out SegaDynamicToolStepExecutionResult? result))
        {
            return false;
        }

        capabilityResult =
            result.CapabilityResult;

        return capabilityResult !=
            null;
    }

    private static bool JsonEquals(
        JsonElement left,
        JsonElement right) =>
        string.Equals(
            left.GetRawText(),
            right.GetRawText(),
            StringComparison.Ordinal);
}

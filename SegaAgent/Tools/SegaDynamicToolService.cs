/*
 * filename: SegaDynamicToolService.cs
 */

using System.Diagnostics;
using System.Text;

namespace SegaAgent.Tools;

public sealed class SegaDynamicToolService
{
    private const double MinimumCreateConfidence = 0.76;
    private const double MinimumReviseConfidence = 0.72;
    private const double MinimumStatusConfidence = 0.70;

    private readonly SegaDynamicToolStore _store;
    private readonly SegaDynamicToolValidator _validator;
    private readonly SegaDynamicToolExecutor _executor;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly object _temporarySync = new();
    private readonly Dictionary<Guid, SegaDynamicToolRecord> _temporary = new();

    public SegaDynamicToolService(
        SegaDynamicToolStore store,
        SegaDynamicToolValidator validator,
        SegaDynamicToolExecutor executor)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<string> BuildCognitionContextAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SegaDynamicToolRecord> persistent = await _store.ReadAllAsync(cancellationToken);
        IReadOnlyList<SegaDynamicToolExecutionHistoryRecord> recentHistory =
            await _store.ReadRecentExecutionHistoryAsync(64, cancellationToken);

        SegaDynamicToolRecord[] temporary;
        lock (_temporarySync)
            temporary = _temporary.Values.ToArray();

        SegaDynamicToolRecord[] all = persistent
            .Concat(temporary)
            .OrderByDescending(value => value.Definition.UpdatedAtUtc)
            .ToArray();

        SegaDynamicToolRecord[] active = all
            .Where(value => value.Definition.Status == SegaDynamicToolStatus.Active)
            .Take(32)
            .ToArray();

        SegaDynamicToolRecord[] inactive = all
            .Where(value => value.Definition.Status != SegaDynamicToolStatus.Active)
            .Take(24)
            .ToArray();

        StringBuilder text = new();
        text.AppendLine("SEGA DYNAMIC TOOLS");
        text.AppendLine("Dynamic tools are validated structured compositions of trusted primitives. A tool definition grants no authority; every primitive step still passes Stage 10 authorization at execution time.");
        text.AppendLine($"Active tools: {active.Length}");

        foreach (SegaDynamicToolRecord record in active)
        {
            SegaDynamicToolDefinition tool = record.Definition;

            SegaDynamicToolExecutionHistoryRecord[] toolHistory =
                recentHistory
                    .Where(value => value.ToolId == tool.Id)
                    .OrderByDescending(value => value.RecordedAtUtc)
                    .ToArray();

            int consecutiveFailures = 0;
            foreach (SegaDynamicToolExecutionHistoryRecord history in toolHistory)
            {
                if (history.Succeeded) break;
                consecutiveFailures++;
            }

            text.AppendLine(
                $"- {tool.Id:D} | {tool.Name} | v{tool.Version} | {tool.Persistence} | " +
                $"runs={record.ExecutionCount} | reliability={(record.ExecutionCount == 0 ? "unproven" : record.Reliability.ToString("0.00"))} | " +
                $"consecutiveFailures={consecutiveFailures} | parallelIndependent={tool.AllowIndependentConcurrency}");

            text.AppendLine($"  {tool.Description}");

            if (!string.IsNullOrWhiteSpace(record.LastResult))
                text.AppendLine($"  Last result: {BoundContext(record.LastResult, 700)}");

            SegaDynamicToolExecutionHistoryRecord? lastFailure =
                consecutiveFailures > 0
                    ? toolHistory.FirstOrDefault(value => !value.Succeeded)
                    : null;

            if (lastFailure != null && !string.IsNullOrWhiteSpace(lastFailure.FailureReason))
                text.AppendLine(
                    $"  Recent failure: v{lastFailure.ToolVersion} | {lastFailure.RecordedAtUtc:O} | " +
                    $"{BoundContext(lastFailure.FailureReason, 900)}");
            if (tool.Parameters.Count > 0)
            {
                text.AppendLine("  Parameters:");
                foreach (SegaDynamicToolParameterDefinition parameter in tool.Parameters)
                    text.AppendLine($"  - {parameter.Name}: {parameter.Type} | required={parameter.Required} | default={(parameter.DefaultValue.HasValue ? "yes" : "no")} | {parameter.Description}");
            }
            text.AppendLine("  Steps:");
            foreach (SegaDynamicToolStep step in tool.Steps)
            {
                string dependencies = step.DependsOnStepIds.Count == 0
                    ? "none"
                    : string.Join(',', step.DependsOnStepIds);
                text.AppendLine(
                    $"  - {step.Id} -> {step.CapabilityId} | depends={dependencies} | " +
                    $"condition={DescribeCondition(step.Condition)} | failure={step.FailurePolicy}");

                foreach ((string argumentName, SegaDynamicToolValueBinding binding) in step.Arguments)
                    text.AppendLine(
                        $"    arg {argumentName} = {DescribeBinding(binding)}");
            }
        }

        if (inactive.Length > 0)
        {
            text.AppendLine();
            text.AppendLine($"Inactive/archived tools: {inactive.Length}");
            foreach (SegaDynamicToolRecord record in inactive)
            {
                SegaDynamicToolDefinition tool = record.Definition;
                text.AppendLine(
                    $"- {tool.Id:D} | {tool.Name} | v{tool.Version} | {tool.Persistence} | " +
                    $"status={tool.Status} | runs={record.ExecutionCount} | " +
                    $"reliability={(record.ExecutionCount == 0 ? "unproven" : record.Reliability.ToString("0.00"))}");
            }
        }

        return text.ToString().Trim();
    }

    public async Task<IReadOnlyList<SegaDynamicToolMutationResult>> ApplyProposalsAsync(
        IReadOnlyList<SegaDynamicToolProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        List<SegaDynamicToolMutationResult> results = new();
        if (proposals == null) return results;

        foreach (SegaDynamicToolProposal proposal in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _mutationLock.WaitAsync(cancellationToken);
            try
            {
                results.Add(await ApplyOneAsync(proposal.Normalize(), cancellationToken));
            }
            catch (Exception ex)
            {
                results.Add(new SegaDynamicToolMutationResult
                {
                    Action = SegaDynamicToolApplyAction.Rejected,
                    Reason = ex.Message
                });
            }
            finally
            {
                _mutationLock.Release();
            }
        }
        return results;
    }

    public async Task<IReadOnlyList<SegaDynamicToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<SegaDynamicToolInvocation> invocations,
        CancellationToken cancellationToken = default)
    {
        List<SegaDynamicToolExecutionResult> results = new();
        if (invocations == null) return results;

        foreach (SegaDynamicToolInvocation raw in invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SegaDynamicToolInvocation invocation = raw.Normalize();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (!Guid.TryParse(invocation.ToolId, out Guid toolId) || toolId == Guid.Empty)
            {
                results.Add(new SegaDynamicToolExecutionResult
                {
                    ToolName = "unknown",
                    Succeeded = false,
                    Aborted = true,
                    FailureReason = "Dynamic tool invocation requires an exact authoritative tool GUID.",
                    Summary = "Dynamic tool invocation requires an exact authoritative tool GUID.",
                    StartedAtUtc = now,
                    FinishedAtUtc = now
                });
                continue;
            }

            SegaDynamicToolRecord? record = await ReadRecordAsync(toolId, cancellationToken);
            if (record == null)
            {
                results.Add(new SegaDynamicToolExecutionResult
                {
                    ToolId = toolId,
                    ToolName = "unknown",
                    Succeeded = false,
                    Aborted = true,
                    FailureReason = "The requested dynamic tool does not exist.",
                    Summary = "The requested dynamic tool does not exist.",
                    StartedAtUtc = now,
                    FinishedAtUtc = now
                });
                continue;
            }

            SegaDynamicToolExecutionResult result;
            try
            {
                result = await _executor.ExecuteAsync(record.Definition, invocation, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new SegaDynamicToolExecutionResult
                {
                    ToolId = record.Definition.Id,
                    ToolName = record.Definition.Name,
                    ToolVersion = record.Definition.Version,
                    Succeeded = false,
                    Aborted = true,
                    FailureReason = ex.Message,
                    Summary = ex.Message,
                    StartedAtUtc = now,
                    FinishedAtUtc = DateTimeOffset.UtcNow
                };
            }

            await RecordExecutionAsync(record, result, cancellationToken);
            results.Add(result);
            Debug.WriteLine($"[DynamicTool] EXECUTE | Tool={result.ToolName} | Version={result.ToolVersion} | Success={result.Succeeded} | Steps={result.StepResults.Count}");
        }

        return results;
    }

    private async Task<SegaDynamicToolMutationResult> ApplyOneAsync(
        SegaDynamicToolProposal proposal,
        CancellationToken cancellationToken)
    {
        return proposal.Action switch
        {
            SegaDynamicToolProposalAction.Create => await CreateAsync(proposal, cancellationToken),
            SegaDynamicToolProposalAction.Revise => await ReviseAsync(proposal, cancellationToken),
            SegaDynamicToolProposalAction.Enable => await SetStatusAsync(proposal, SegaDynamicToolStatus.Active, SegaDynamicToolApplyAction.Enabled, cancellationToken),
            SegaDynamicToolProposalAction.Disable => await SetStatusAsync(proposal, SegaDynamicToolStatus.Disabled, SegaDynamicToolApplyAction.Disabled, cancellationToken),
            SegaDynamicToolProposalAction.Retire => await SetStatusAsync(proposal, SegaDynamicToolStatus.Retired, SegaDynamicToolApplyAction.Retired, cancellationToken),
            _ => Rejected("Unsupported dynamic tool proposal action.")
        };
    }

    private async Task<SegaDynamicToolMutationResult> CreateAsync(
        SegaDynamicToolProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumCreateConfidence)
            return Rejected("Dynamic tool create confidence is below the authoritative threshold.");
        if (proposal.Definition == null)
            return Rejected("Create requires a complete dynamic tool definition.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SegaDynamicToolDefinition candidate = proposal.Definition.Normalize() with
        {
            Id = Guid.NewGuid(),
            Version = 1,
            Status = SegaDynamicToolStatus.Active,
            CreationReason = proposal.Reason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        candidate = _validator.ValidateDefinition(candidate);

        if (await NameExistsAsync(candidate.Name, null, cancellationToken))
            return Rejected($"A dynamic tool named '{candidate.Name}' already exists.");

        SegaDynamicToolRecord record = new() { Definition = candidate };
        if (candidate.Persistence == SegaDynamicToolPersistence.Persistent)
            await _store.UpsertAsync(record, cancellationToken);
        else
            lock (_temporarySync) _temporary[candidate.Id] = record;

        Debug.WriteLine($"[DynamicTool] CREATED | Id={candidate.Id:D} | Name={candidate.Name} | Version=1 | Persistence={candidate.Persistence}");
        return new SegaDynamicToolMutationResult
        {
            Action = SegaDynamicToolApplyAction.Created,
            Tool = candidate,
            Reason = "Dynamic tool validated and created."
        };
    }

    private async Task<SegaDynamicToolMutationResult> ReviseAsync(
        SegaDynamicToolProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumReviseConfidence)
            return Rejected("Dynamic tool revision confidence is below the authoritative threshold.");
        if (!TryToolId(proposal.ToolId, out Guid toolId))
            return Rejected("Revise requires an exact existing dynamic tool GUID.");
        if (proposal.Definition == null)
            return Rejected("Revise requires a complete replacement definition.");

        SegaDynamicToolRecord? existing = await ReadRecordAsync(toolId, cancellationToken);
        if (existing == null)
            return Rejected("The dynamic tool to revise does not exist.");
        if (existing.Definition.Status == SegaDynamicToolStatus.Retired)
            return Rejected("A retired dynamic tool cannot be revised.");

        SegaDynamicToolDefinition candidate = proposal.Definition.Normalize() with
        {
            Id = existing.Definition.Id,
            Version = existing.Definition.Version + 1,
            Persistence = existing.Definition.Persistence,
            Status = existing.Definition.Status,
            CreationReason = existing.Definition.CreationReason,
            CreatedAtUtc = existing.Definition.CreatedAtUtc,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        candidate = _validator.ValidateDefinition(candidate);

        if (await NameExistsAsync(candidate.Name, candidate.Id, cancellationToken))
            return Rejected($"Another dynamic tool named '{candidate.Name}' already exists.");

        SegaDynamicToolRecord updated = existing with { Definition = candidate };
        if (candidate.Persistence == SegaDynamicToolPersistence.Persistent)
            await _store.UpsertAsync(updated, cancellationToken);
        else
            lock (_temporarySync) _temporary[candidate.Id] = updated;

        Debug.WriteLine($"[DynamicTool] REVISED | Id={candidate.Id:D} | Name={candidate.Name} | Version={candidate.Version}");
        return new SegaDynamicToolMutationResult
        {
            Action = SegaDynamicToolApplyAction.Revised,
            Tool = candidate,
            Reason = "Dynamic tool revision validated and committed as a new version."
        };
    }

    private async Task<SegaDynamicToolMutationResult> SetStatusAsync(
        SegaDynamicToolProposal proposal,
        SegaDynamicToolStatus status,
        SegaDynamicToolApplyAction action,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumStatusConfidence)
            return Rejected("Dynamic tool status confidence is below the authoritative threshold.");
        if (!TryToolId(proposal.ToolId, out Guid toolId))
            return Rejected("Dynamic tool status change requires an exact existing GUID.");

        SegaDynamicToolRecord? existing = await ReadRecordAsync(toolId, cancellationToken);
        if (existing == null)
            return Rejected("The dynamic tool does not exist.");
        if (existing.Definition.Status == status)
            return new SegaDynamicToolMutationResult
            {
                Action = SegaDynamicToolApplyAction.NoChange,
                Tool = existing.Definition,
                Reason = $"Dynamic tool is already {status}."
            };
        if (existing.Definition.Status == SegaDynamicToolStatus.Retired && status != SegaDynamicToolStatus.Retired)
            return Rejected("Retirement is terminal; create a new tool or revision lineage instead of re-enabling a retired tool.");

        SegaDynamicToolDefinition updatedDefinition = existing.Definition with
        {
            Status = status,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        SegaDynamicToolRecord updated = existing with { Definition = updatedDefinition };

        if (updatedDefinition.Persistence == SegaDynamicToolPersistence.Persistent)
        {
            await _store.SetStatusAsync(toolId, status, cancellationToken);
        }
        else
        {
            lock (_temporarySync)
            {
                if (status == SegaDynamicToolStatus.Retired)
                    _temporary.Remove(toolId);
                else
                    _temporary[toolId] = updated;
            }
        }

        return new SegaDynamicToolMutationResult
        {
            Action = action,
            Tool = updatedDefinition,
            Reason = $"Dynamic tool status changed to {status}."
        };
    }

    private async Task<SegaDynamicToolRecord?> ReadRecordAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        lock (_temporarySync)
        {
            if (_temporary.TryGetValue(id, out SegaDynamicToolRecord? temporary))
                return temporary;
        }
        return await _store.ReadByIdAsync(id, cancellationToken);
    }

    private async Task<bool> NameExistsAsync(
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken)
    {
        lock (_temporarySync)
        {
            if (_temporary.Values.Any(value =>
                    (!exceptId.HasValue || value.Definition.Id != exceptId.Value) &&
                    string.Equals(value.Definition.Name, name, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        IReadOnlyList<SegaDynamicToolRecord> persistent = await _store.ReadAllAsync(cancellationToken);
        return persistent.Any(value =>
            (!exceptId.HasValue || value.Definition.Id != exceptId.Value) &&
            string.Equals(value.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RecordExecutionAsync(
        SegaDynamicToolRecord record,
        SegaDynamicToolExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (record.Definition.Persistence == SegaDynamicToolPersistence.Persistent)
        {
            await _store.RecordExecutionAsync(record.Definition.Id, result, cancellationToken);
            return;
        }

        lock (_temporarySync)
        {
            if (!_temporary.TryGetValue(record.Definition.Id, out SegaDynamicToolRecord? current)) return;
            _temporary[record.Definition.Id] = current with
            {
                ExecutionCount = current.ExecutionCount + 1,
                SuccessCount = current.SuccessCount + (result.Succeeded ? 1 : 0),
                FailureCount = current.FailureCount + (result.Succeeded ? 0 : 1),
                LastResult = result.Summary.Length <= 2400 ? result.Summary : result.Summary[..2400],
                LastExecutedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private static string DescribeBinding(SegaDynamicToolValueBinding binding)
    {
        return binding.Source switch
        {
            SegaDynamicValueSource.Literal =>
                $"Literal:{BoundContext(binding.Literal?.GetRawText() ?? "null", 260)}",
            SegaDynamicValueSource.Template =>
                $"Template:{BoundContext(binding.Template ?? string.Empty, 420)}",
            SegaDynamicValueSource.Parameter =>
                $"Parameter:{binding.ParameterName ?? "-"}",
            SegaDynamicValueSource.StepOutput =>
                $"StepOutput:{binding.StepId ?? "-"}",
            SegaDynamicValueSource.StepSummary =>
                $"StepSummary:{binding.StepId ?? "-"}",
            SegaDynamicValueSource.StepExitCode =>
                $"StepExitCode:{binding.StepId ?? "-"}",
            SegaDynamicValueSource.StepHttpStatusCode =>
                $"StepHttpStatusCode:{binding.StepId ?? "-"}",
            SegaDynamicValueSource.StepSucceeded =>
                $"StepSucceeded:{binding.StepId ?? "-"}",
            _ =>
                binding.Source.ToString()
        };
    }

    private static string DescribeCondition(SegaDynamicToolCondition condition)
    {
        return condition.Kind switch
        {
            SegaDynamicConditionKind.ParameterEquals =>
                $"ParameterEquals:{condition.ParameterName ?? "-"}={BoundContext(condition.ExpectedValue?.GetRawText() ?? "null", 180)}",
            SegaDynamicConditionKind.StepSucceeded =>
                $"StepSucceeded:{condition.StepId ?? "-"}",
            SegaDynamicConditionKind.StepFailed =>
                $"StepFailed:{condition.StepId ?? "-"}",
            _ =>
                condition.Kind.ToString()
        };
    }

    private static string BoundContext(string value, int maximum)
    {
        string text = value?.Trim() ?? string.Empty;
        return text.Length <= maximum ? text : text[..maximum];
    }

    private static bool TryToolId(string? value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;

    private static SegaDynamicToolMutationResult Rejected(string reason) => new()
    {
        Action = SegaDynamicToolApplyAction.Rejected,
        Reason = reason
    };
}

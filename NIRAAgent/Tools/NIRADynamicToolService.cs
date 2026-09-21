/*
 * filename: NIRADynamicToolService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using NIRAAgent.Skills;

namespace NIRAAgent.Tools;

public sealed class NIRADynamicToolService
{
    private const double MinimumCreateConfidence = 0.76;
    private const double MinimumReviseConfidence = 0.72;
    private const double MinimumStatusConfidence = 0.70;

    private readonly NIRADynamicToolStore _store;
    private readonly NIRADynamicToolValidator _validator;
    private readonly NIRADynamicToolExecutor _executor;
    private readonly NIRALearnedSkillService _skills;
    private readonly SemaphoreSlim _mutationLock = new(1, 1);
    private readonly object _temporarySync = new();
    private readonly Dictionary<Guid, NIRADynamicToolRecord> _temporary = new();

    public NIRADynamicToolService(
        NIRADynamicToolStore store,
        NIRADynamicToolValidator validator,
        NIRADynamicToolExecutor executor,
        NIRALearnedSkillService skills)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));
    }

    public async Task<string> BuildCognitionContextAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NIRADynamicToolRecord> persistent = await _store.ReadAllAsync(cancellationToken);
        IReadOnlyList<NIRADynamicToolExecutionHistoryRecord> recentHistory =
            await _store.ReadRecentExecutionHistoryAsync(64, cancellationToken);

        NIRADynamicToolRecord[] temporary;
        lock (_temporarySync)
            temporary = _temporary.Values.ToArray();

        NIRADynamicToolRecord[] all = persistent
            .Concat(temporary)
            .OrderByDescending(value => value.Definition.UpdatedAtUtc)
            .ToArray();

        NIRADynamicToolRecord[] active = all
            .Where(value => value.Definition.Status == NIRADynamicToolStatus.Active)
            .Take(32)
            .ToArray();

        NIRADynamicToolRecord[] inactive = all
            .Where(value => value.Definition.Status != NIRADynamicToolStatus.Active)
            .Take(24)
            .ToArray();

        StringBuilder text = new();
        text.AppendLine("NIRA DYNAMIC TOOLS");
        text.AppendLine("Dynamic tools are validated structured compositions of trusted primitives. A tool definition grants no authority; every primitive step still passes Stage 10 authorization at execution time.");
        text.AppendLine($"Active tools: {active.Length}");

        foreach (NIRADynamicToolRecord record in active)
        {
            NIRADynamicToolDefinition tool = record.Definition;

            NIRADynamicToolExecutionHistoryRecord[] toolHistory =
                recentHistory
                    .Where(value => value.ToolId == tool.Id)
                    .OrderByDescending(value => value.RecordedAtUtc)
                    .ToArray();

            int consecutiveFailures = 0;
            foreach (NIRADynamicToolExecutionHistoryRecord history in toolHistory)
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

            NIRADynamicToolExecutionHistoryRecord? lastFailure =
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
                foreach (NIRADynamicToolParameterDefinition parameter in tool.Parameters)
                    text.AppendLine($"  - {parameter.Name}: {parameter.Type} | required={parameter.Required} | default={(parameter.DefaultValue.HasValue ? "yes" : "no")} | {parameter.Description}");
            }
            text.AppendLine("  Steps:");
            foreach (NIRADynamicToolStep step in tool.Steps)
            {
                string dependencies = step.DependsOnStepIds.Count == 0
                    ? "none"
                    : string.Join(',', step.DependsOnStepIds);
                text.AppendLine(
                    $"  - {step.Id} -> {step.CapabilityId} | depends={dependencies} | " +
                    $"condition={DescribeCondition(step.Condition)} | failure={step.FailurePolicy}");

                foreach ((string argumentName, NIRADynamicToolValueBinding binding) in step.Arguments)
                    text.AppendLine(
                        $"    arg {argumentName} = {DescribeBinding(binding)}");
            }
        }

        if (inactive.Length > 0)
        {
            text.AppendLine();
            text.AppendLine($"Inactive/archived tools: {inactive.Length}");
            foreach (NIRADynamicToolRecord record in inactive)
            {
                NIRADynamicToolDefinition tool = record.Definition;
                text.AppendLine(
                    $"- {tool.Id:D} | {tool.Name} | v{tool.Version} | {tool.Persistence} | " +
                    $"status={tool.Status} | runs={record.ExecutionCount} | " +
                    $"reliability={(record.ExecutionCount == 0 ? "unproven" : record.Reliability.ToString("0.00"))}");
            }
        }

        return text.ToString().Trim();
    }

    public async Task<IReadOnlyList<NIRADynamicToolMutationResult>> ApplyProposalsAsync(
        IReadOnlyList<NIRADynamicToolProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        List<NIRADynamicToolMutationResult> results = new();
        if (proposals == null) return results;

        foreach (NIRADynamicToolProposal proposal in proposals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _mutationLock.WaitAsync(cancellationToken);
            try
            {
                results.Add(await ApplyOneAsync(proposal.Normalize(), cancellationToken));
            }
            catch (Exception ex)
            {
                results.Add(new NIRADynamicToolMutationResult
                {
                    Action = NIRADynamicToolApplyAction.Rejected,
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

    public async Task<IReadOnlyList<NIRADynamicToolExecutionResult>> ExecuteAsync(
        IReadOnlyList<NIRADynamicToolInvocation> invocations,
        CancellationToken cancellationToken = default)
    {
        List<NIRADynamicToolExecutionResult> results = new();
        if (invocations == null) return results;

        foreach (NIRADynamicToolInvocation raw in invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NIRADynamicToolInvocation invocation = raw.Normalize();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (!Guid.TryParse(invocation.ToolId, out Guid toolId) || toolId == Guid.Empty)
            {
                results.Add(new NIRADynamicToolExecutionResult
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

            NIRADynamicToolRecord? record = await ReadRecordAsync(toolId, cancellationToken);
            if (record == null)
            {
                results.Add(new NIRADynamicToolExecutionResult
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

            NIRALearnedSkillRecord? sourceSkill = null;
            if (!string.IsNullOrWhiteSpace(invocation.SourceSkillId))
            {
                try
                {
                    sourceSkill = await _skills.ValidateReuseAsync(
                        invocation.SourceSkillId,
                        record.Definition,
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    results.Add(new NIRADynamicToolExecutionResult
                    {
                        ToolId = record.Definition.Id,
                        ToolName = record.Definition.Name,
                        ToolVersion = record.Definition.Version,
                        Succeeded = false,
                        Aborted = true,
                        FailureReason = $"Learned-skill reuse validation failed: {ex.Message}",
                        Summary = $"Learned-skill reuse validation failed: {ex.Message}",
                        StartedAtUtc = now,
                        FinishedAtUtc = DateTimeOffset.UtcNow
                    });
                    continue;
                }
            }

            NIRADynamicToolExecutionResult result;
            try
            {
                result = await _executor.ExecuteAsync(record.Definition, invocation, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new NIRADynamicToolExecutionResult
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

            await RecordExecutionAsync(record, invocation, result, cancellationToken);

            try
            {
                await _skills.RecordLinkedToolExecutionAsync(
                    record.Definition,
                    result,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Procedural learning must never turn a successfully executed
                // tool into a failed tool result. The authoritative execution
                // evidence is already stored; skill evidence can be reconciled
                // by a later run if its own persistence temporarily fails.
                Debug.WriteLine(
                    $"[LearnedSkill] EVIDENCE RECORD FAILED | " +
                    $"Tool={record.Definition.Name} | " +
                    $"Type={ex.GetType().Name} | Message='{ex.Message}'");
            }

            if (sourceSkill != null)
            {
                try
                {
                    await _skills.RecordReuseAsync(
                        sourceSkill,
                        record.Definition,
                        result,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Reuse provenance is important procedural evidence, but
                    // failure to persist that attribution must not falsify the
                    // already-completed dynamic-tool execution result.
                    Debug.WriteLine(
                        $"[LearnedSkill] REUSE RECORD FAILED | " +
                        $"SkillId={sourceSkill.Definition.Id:D} | Tool={record.Definition.Name} | " +
                        $"Type={ex.GetType().Name} | Message='{ex.Message}'");
                }
            }

            results.Add(result);
            Debug.WriteLine($"[DynamicTool] EXECUTE | Tool={result.ToolName} | Version={result.ToolVersion} | Success={result.Succeeded} | Steps={result.StepResults.Count}");
        }

        return results;
    }

    private async Task<NIRADynamicToolMutationResult> ApplyOneAsync(
        NIRADynamicToolProposal proposal,
        CancellationToken cancellationToken)
    {
        return proposal.Action switch
        {
            NIRADynamicToolProposalAction.Create => await CreateAsync(proposal, cancellationToken),
            NIRADynamicToolProposalAction.Revise => await ReviseAsync(proposal, cancellationToken),
            NIRADynamicToolProposalAction.Enable => await SetStatusAsync(proposal, NIRADynamicToolStatus.Active, NIRADynamicToolApplyAction.Enabled, cancellationToken),
            NIRADynamicToolProposalAction.Disable => await SetStatusAsync(proposal, NIRADynamicToolStatus.Disabled, NIRADynamicToolApplyAction.Disabled, cancellationToken),
            NIRADynamicToolProposalAction.Retire => await SetStatusAsync(proposal, NIRADynamicToolStatus.Retired, NIRADynamicToolApplyAction.Retired, cancellationToken),
            _ => Rejected("Unsupported dynamic tool proposal action.")
        };
    }

    private async Task<NIRADynamicToolMutationResult> CreateAsync(
        NIRADynamicToolProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumCreateConfidence)
            return Rejected("Dynamic tool create confidence is below the authoritative threshold.");
        if (proposal.Definition == null)
            return Rejected("Create requires a complete dynamic tool definition.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        NIRADynamicToolDefinition candidate = proposal.Definition.Normalize() with
        {
            Id = Guid.NewGuid(),
            Version = 1,
            Status = NIRADynamicToolStatus.Active,
            CreationReason = proposal.Reason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        candidate = _validator.ValidateDefinition(candidate);

        NIRALearnedSkillRecord? equivalentProvenSkill =
            await _skills.FindEquivalentCurrentProvenSkillAsync(
                candidate,
                cancellationToken);

        if (equivalentProvenSkill != null)
        {
            return Rejected(
                $"A current Proven learned skill already owns an equivalent executable procedure. " +
                $"Reuse linked tool {equivalentProvenSkill.Definition.SourceToolId:D} from skill " +
                $"{equivalentProvenSkill.Definition.Id:D} ('{equivalentProvenSkill.Definition.Name}') instead of creating a duplicate tool.");
        }

        if (await NameExistsAsync(candidate.Name, null, cancellationToken))
            return Rejected($"A dynamic tool named '{candidate.Name}' already exists.");

        NIRADynamicToolRecord record = new() { Definition = candidate };
        if (candidate.Persistence == NIRADynamicToolPersistence.Persistent)
            await _store.UpsertAsync(record, cancellationToken);
        else
            lock (_temporarySync) _temporary[candidate.Id] = record;

        Debug.WriteLine($"[DynamicTool] CREATED | Id={candidate.Id:D} | Name={candidate.Name} | Version=1 | Persistence={candidate.Persistence}");
        return new NIRADynamicToolMutationResult
        {
            Action = NIRADynamicToolApplyAction.Created,
            Tool = candidate,
            Reason = "Dynamic tool validated and created."
        };
    }

    private async Task<NIRADynamicToolMutationResult> ReviseAsync(
        NIRADynamicToolProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumReviseConfidence)
            return Rejected("Dynamic tool revision confidence is below the authoritative threshold.");
        if (!TryToolId(proposal.ToolId, out Guid toolId))
            return Rejected("Revise requires an exact existing dynamic tool GUID.");
        if (proposal.Definition == null)
            return Rejected("Revise requires a complete replacement definition.");

        NIRADynamicToolRecord? existing = await ReadRecordAsync(toolId, cancellationToken);
        if (existing == null)
            return Rejected("The dynamic tool to revise does not exist.");
        if (existing.Definition.Status == NIRADynamicToolStatus.Retired)
            return Rejected("A retired dynamic tool cannot be revised.");

        NIRADynamicToolDefinition candidate = proposal.Definition.Normalize() with
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

        NIRADynamicToolRecord updated = existing with { Definition = candidate };
        if (candidate.Persistence == NIRADynamicToolPersistence.Persistent)
            await _store.UpsertAsync(updated, cancellationToken);
        else
            lock (_temporarySync) _temporary[candidate.Id] = updated;

        Debug.WriteLine($"[DynamicTool] REVISED | Id={candidate.Id:D} | Name={candidate.Name} | Version={candidate.Version}");
        return new NIRADynamicToolMutationResult
        {
            Action = NIRADynamicToolApplyAction.Revised,
            Tool = candidate,
            Reason = "Dynamic tool revision validated and committed as a new version."
        };
    }

    private async Task<NIRADynamicToolMutationResult> SetStatusAsync(
        NIRADynamicToolProposal proposal,
        NIRADynamicToolStatus status,
        NIRADynamicToolApplyAction action,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumStatusConfidence)
            return Rejected("Dynamic tool status confidence is below the authoritative threshold.");
        if (!TryToolId(proposal.ToolId, out Guid toolId))
            return Rejected("Dynamic tool status change requires an exact existing GUID.");

        NIRADynamicToolRecord? existing = await ReadRecordAsync(toolId, cancellationToken);
        if (existing == null)
            return Rejected("The dynamic tool does not exist.");
        if (existing.Definition.Status == status)
            return new NIRADynamicToolMutationResult
            {
                Action = NIRADynamicToolApplyAction.NoChange,
                Tool = existing.Definition,
                Reason = $"Dynamic tool is already {status}."
            };
        if (existing.Definition.Status == NIRADynamicToolStatus.Retired && status != NIRADynamicToolStatus.Retired)
            return Rejected("Retirement is terminal; create a new tool or revision lineage instead of re-enabling a retired tool.");

        NIRADynamicToolDefinition updatedDefinition = existing.Definition with
        {
            Status = status,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        NIRADynamicToolRecord updated = existing with { Definition = updatedDefinition };

        if (updatedDefinition.Persistence == NIRADynamicToolPersistence.Persistent)
        {
            await _store.SetStatusAsync(toolId, status, cancellationToken);
        }
        else
        {
            lock (_temporarySync)
            {
                if (status == NIRADynamicToolStatus.Retired)
                    _temporary.Remove(toolId);
                else
                    _temporary[toolId] = updated;
            }
        }

        if (updatedDefinition.Persistence == NIRADynamicToolPersistence.Persistent)
        {
            await _skills.ReconcileSourceToolLifecycleAsync(
                updatedDefinition,
                cancellationToken);
        }

        return new NIRADynamicToolMutationResult
        {
            Action = action,
            Tool = updatedDefinition,
            Reason = $"Dynamic tool status changed to {status}."
        };
    }

    private async Task<NIRADynamicToolRecord?> ReadRecordAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        lock (_temporarySync)
        {
            if (_temporary.TryGetValue(id, out NIRADynamicToolRecord? temporary))
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

        IReadOnlyList<NIRADynamicToolRecord> persistent = await _store.ReadAllAsync(cancellationToken);
        return persistent.Any(value =>
            (!exceptId.HasValue || value.Definition.Id != exceptId.Value) &&
            string.Equals(value.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RecordExecutionAsync(
        NIRADynamicToolRecord record,
        NIRADynamicToolInvocation invocation,
        NIRADynamicToolExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (record.Definition.Persistence == NIRADynamicToolPersistence.Persistent)
        {
            string invocationFingerprint = BuildInvocationFingerprint(invocation);
            await _store.RecordExecutionAsync(
                record.Definition.Id,
                result,
                invocationFingerprint,
                cancellationToken);
            return;
        }

        lock (_temporarySync)
        {
            if (!_temporary.TryGetValue(record.Definition.Id, out NIRADynamicToolRecord? current)) return;
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

    private static string BuildInvocationFingerprint(
        NIRADynamicToolInvocation invocation)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(invocation.BuildSignature());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string DescribeBinding(NIRADynamicToolValueBinding binding)
    {
        return binding.Source switch
        {
            NIRADynamicValueSource.Literal =>
                $"Literal:{BoundContext(binding.Literal?.GetRawText() ?? "null", 260)}",
            NIRADynamicValueSource.Template =>
                $"Template:{BoundContext(binding.Template ?? string.Empty, 420)}",
            NIRADynamicValueSource.Parameter =>
                $"Parameter:{binding.ParameterName ?? "-"}",
            NIRADynamicValueSource.StepOutput =>
                $"StepOutput:{binding.StepId ?? "-"}",
            NIRADynamicValueSource.StepSummary =>
                $"StepSummary:{binding.StepId ?? "-"}",
            NIRADynamicValueSource.StepExitCode =>
                $"StepExitCode:{binding.StepId ?? "-"}",
            NIRADynamicValueSource.StepHttpStatusCode =>
                $"StepHttpStatusCode:{binding.StepId ?? "-"}",
            NIRADynamicValueSource.StepSucceeded =>
                $"StepSucceeded:{binding.StepId ?? "-"}",
            _ =>
                binding.Source.ToString()
        };
    }

    private static string DescribeCondition(NIRADynamicToolCondition condition)
    {
        return condition.Kind switch
        {
            NIRADynamicConditionKind.ParameterEquals =>
                $"ParameterEquals:{condition.ParameterName ?? "-"}={BoundContext(condition.ExpectedValue?.GetRawText() ?? "null", 180)}",
            NIRADynamicConditionKind.StepSucceeded =>
                $"StepSucceeded:{condition.StepId ?? "-"}",
            NIRADynamicConditionKind.StepFailed =>
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

    private static NIRADynamicToolMutationResult Rejected(string reason) => new()
    {
        Action = NIRADynamicToolApplyAction.Rejected,
        Reason = reason
    };
}


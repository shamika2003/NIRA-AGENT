/*
 * filename: SegaGoalService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Hosting;

using SegaAgent.Mind;
using SegaAgent.Branches;
using SegaAgent.Self.Model;

namespace SegaAgent.Goals;

public sealed class SegaGoalService : IHostedService
{
    private const double MinimumCreateConfidence = 0.78;
    private const double MinimumTransitionConfidence = 0.70;
    private const double MinimumCompletionConfidence = 0.76;

    private readonly SegaGoalStore _store;
    private readonly SegaSelfModelService _selfModel;
    private readonly SegaBranchStore _branches;

    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    private Dictionary<Guid, SegaGoalState> _goals = new();

    public SegaGoalService(
        SegaGoalStore store,
        SegaSelfModelService selfModel,
        SegaBranchStore branches)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _selfModel = selfModel ?? throw new ArgumentNullException(nameof(selfModel));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken);

        IReadOnlyList<SegaGoalState> goals =
            await _store.ReadGoalsAsync(cancellationToken);

        lock (_stateSync)
        {
            _goals = goals.ToDictionary(goal => goal.Id, goal => goal);
        }

        Debug.WriteLine(
            $"[Goals] READY | " +
            $"Open={goals.Count(g => g.IsOpen)} | " +
            $"Active={goals.Count(g => g.Status == SegaGoalStatus.Active)} | " +
            $"Pending={goals.Count(g => g.Status == SegaGoalStatus.Pending)} | " +
            $"Waiting={goals.Count(g => g.Status == SegaGoalStatus.Waiting)} | " +
            $"Blocked={goals.Count(g => g.Status == SegaGoalStatus.Blocked)} | " +
            $"Resolved={goals.Count(g => g.IsResolved)} | " +
            $"Database='{_store.DatabasePath}'");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public IReadOnlyList<SegaGoalState> CurrentGoals
    {
        get
        {
            lock (_stateSync)
            {
                return _goals.Values
                    .OrderByDescending(goal => goal.IsOpen)
                    .ThenByDescending(goal => goal.Priority)
                    .ThenByDescending(goal => goal.UpdatedAt)
                    .ToArray();
            }
        }
    }

    public bool TryGetGoal(
        Guid goalId,
        out SegaGoalState? goal)
    {
        lock (_stateSync)
        {
            return _goals.TryGetValue(
                goalId,
                out goal);
        }
    }


    public string BuildCognitionContext()
    {
        SegaGoalState[] open;
        SegaGoalState[] resolved;

        lock (_stateSync)
        {
            open = _goals.Values
                .Where(goal => goal.IsOpen)
                .OrderByDescending(goal => goal.Priority)
                .ThenByDescending(goal => goal.UpdatedAt)
                .Take(32)
                .ToArray();

            resolved = _goals.Values
                .Where(goal => goal.IsResolved)
                .OrderByDescending(goal => goal.UpdatedAt)
                .Take(8)
                .ToArray();
        }

        StringBuilder builder = new();

        builder.AppendLine("SEGA PERSISTENT GOALS / INTENTIONS");
        builder.AppendLine();
        builder.AppendLine(
            "Goals are executable/intention state. They are separate from remembered facts and from commitments/obligations.");
        builder.AppendLine();
        builder.AppendLine("OPEN GOALS");

        if (open.Length == 0)
        {
            builder.AppendLine("- No open goals.");
        }
        else
        {
            foreach (SegaGoalState goal in open)
            {
                builder.AppendLine(
                    $"- id={goal.Id:D} | status={goal.Status} | " +
                    $"priority={goal.Priority:F2} | source={goal.Source} | " +
                    $"linkedCommitment={goal.LinkedCommitmentId?.ToString("D") ?? "-"} | " +
                    $"objective={goal.Objective}");

                if (goal.CompletionCriteria.Count > 0)
                {
                    builder.AppendLine("  completion criteria:");

                    foreach (string criterion in goal.CompletionCriteria)
                    {
                        builder.AppendLine($"  - {criterion}");
                    }
                }

                if (goal.DependsOnGoalIds.Count > 0)
                {
                    builder.AppendLine(
                        $"  depends on: {string.Join(", ", goal.DependsOnGoalIds.Select(id => id.ToString("D")))}");
                }

                if (!string.IsNullOrWhiteSpace(goal.WaitingFor))
                {
                    builder.AppendLine($"  waiting for: {goal.WaitingFor}");
                }

                if (goal.NextWakeAtUtc.HasValue)
                {
                    builder.AppendLine($"  next wake UTC: {goal.NextWakeAtUtc.Value:O}");
                }

                if (!string.IsNullOrWhiteSpace(goal.Blocker))
                {
                    builder.AppendLine($"  blocker: {goal.Blocker}");
                }

                if (!string.IsNullOrWhiteSpace(goal.LastEvidenceSummary))
                {
                    builder.AppendLine(
                        $"  evidence count={goal.EvidenceCount} | last evidence={goal.LastEvidenceSummary}");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("RECENTLY RESOLVED GOALS");

        if (resolved.Length == 0)
        {
            builder.AppendLine("- No recently resolved goals.");
        }
        else
        {
            foreach (SegaGoalState goal in resolved)
            {
                builder.AppendLine(
                    $"- id={goal.Id:D} | status={goal.Status} | objective={goal.Objective}");
            }
        }

        return builder.ToString().Trim();
    }

    public async Task<IReadOnlyList<SegaGoalApplyResult>> ApplyProposalsAsync(
        SegaMindEvent mindEvent,
        string finalReply,
        string capabilityEvidence,
        IReadOnlyList<SegaGoalProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mindEvent);

        if (proposals == null || proposals.Count == 0)
        {
            return Array.Empty<SegaGoalApplyResult>();
        }

        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            List<SegaGoalApplyResult> results = new();

            foreach (SegaGoalProposal raw in proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SegaGoalProposal proposal;

                try
                {
                    proposal = raw.Normalize();
                }
                catch (Exception ex)
                {
                    SegaGoalApplyResult malformed = Reject(
                        $"Malformed goal proposal: {ex.Message}");

                    LogResult(malformed, raw);
                    results.Add(malformed);
                    continue;
                }

                SegaGoalApplyResult result =
                    await ApplyProposalCoreAsync(
                        mindEvent,
                        finalReply,
                        capabilityEvidence,
                        proposal,
                        cancellationToken);

                LogResult(result, proposal);
                results.Add(result);
            }

            return results;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<IReadOnlyList<SegaGoalState>> ClaimDueGoalsAsync(
        DateTimeOffset now,
        int maximumGoals = 3,
        CancellationToken cancellationToken = default)
    {
        maximumGoals = Math.Clamp(maximumGoals, 1, 8);

        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            SegaGoalState[] due;

            lock (_stateSync)
            {
                due = _goals.Values
                    .Where(goal =>
                        goal.Status == SegaGoalStatus.Waiting &&
                        goal.NextWakeAtUtc.HasValue &&
                        goal.NextWakeAtUtc.Value <= now)
                    .OrderByDescending(goal => goal.Priority)
                    .ThenBy(goal => goal.NextWakeAtUtc)
                    .Take(maximumGoals)
                    .ToArray();
            }

            List<SegaGoalState> claimed = new();

            foreach (SegaGoalState goal in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!AreDependenciesComplete(goal))
                {
                    continue;
                }

                SegaGoalState updated = goal with
                {
                    Status = SegaGoalStatus.Active,
                    WaitingFor = null,
                    NextWakeAtUtc = null,
                    LastReason = "Scheduled goal wake time was reached.",
                    UpdatedAt = now
                };

                await _store.UpsertGoalAsync(updated, null, cancellationToken);

                lock (_stateSync)
                {
                    _goals[updated.Id] = updated;
                }

                claimed.Add(updated);
            }

            return claimed;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task<SegaGoalApplyResult> ApplyProposalCoreAsync(
        SegaMindEvent mindEvent,
        string finalReply,
        string capabilityEvidence,
        SegaGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        string evidenceText = proposal.EvidenceSource switch
        {
            SegaGoalEvidenceSource.CurrentEvent =>
                mindEvent.Content,

            SegaGoalEvidenceSource.SegaReply =>
                finalReply,

            SegaGoalEvidenceSource.CapabilityResult =>
                capabilityEvidence,

            _ =>
                string.Empty
        };

        if (string.IsNullOrWhiteSpace(proposal.EvidenceQuote))
        {
            return Reject("A goal mutation requires a grounded evidence quote.");
        }

        if (!ContainsEvidenceQuote(evidenceText, proposal.EvidenceQuote))
        {
            return Reject(
                $"Evidence quote is not present in the declared {proposal.EvidenceSource} source.");
        }

        if (proposal.Action == SegaGoalProposalAction.Create)
        {
            if (IsDirectUserTemporalWakeProposal(
                    mindEvent,
                    proposal))
            {
                return Reject(
                    "A new goal cannot own the first future wake of a user-accepted temporal obligation. " +
                    "That wake belongs to the temporal commitment scheduler. If executable work is needed " +
                    "when the obligation becomes due, create or activate that work from the temporal commitment wake.");
            }

            return await CreateGoalAsync(
                mindEvent,
                proposal,
                cancellationToken);
        }

        if (proposal.Confidence < MinimumTransitionConfidence)
        {
            return Reject("Goal mutation confidence is below the runtime threshold.");
        }

        if (string.IsNullOrWhiteSpace(proposal.GoalId) ||
            !Guid.TryParse(proposal.GoalId, out Guid goalId))
        {
            return Reject("A non-create goal mutation requires an exact existing goal GUID.");
        }

        SegaGoalState? existing;

        lock (_stateSync)
        {
            _goals.TryGetValue(goalId, out existing);
        }

        if (existing == null)
        {
            return Reject("The proposed goal ID does not exist in authoritative goal state.");
        }

        if (existing.IsResolved)
        {
            return new SegaGoalApplyResult
            {
                Action = SegaGoalApplyAction.NoChange,
                Goal = existing,
                Reason = "Resolved goals are terminal."
            };
        }

        if (IsSameTurnUserTemporalWakeMutation(
                mindEvent,
                existing,
                proposal))
        {
            return Reject(
                "A newly-created goal from this same user event cannot be converted into a parallel user-timed wake. " +
                "The accepted temporal commitment owns that future wake. Keep the goal untimed or let the " +
                "temporal commitment wake Sega first.");
        }

        return proposal.Action switch
        {
            SegaGoalProposalAction.RecordProgress =>
                await RecordProgressAsync(
                    existing,
                    mindEvent,
                    proposal,
                    cancellationToken),

            SegaGoalProposalAction.Revise =>
                await ReviseGoalAsync(
                    existing,
                    mindEvent,
                    proposal,
                    cancellationToken),

            _ =>
                await TransitionGoalAsync(
                    existing,
                    mindEvent,
                    finalReply,
                    proposal,
                    cancellationToken)
        };
    }

    // =========================================================
    // TEMPORAL WAKE OWNERSHIP
    //
    // User-facing future obligations have one authoritative first
    // wake owner: the temporal commitment scheduler. A persistent
    // goal may still represent executable work, but it must not be
    // born with a duplicate timer copied from the same user event.
    //
    // Goal NextWakeAtUtc remains valid for executive-owned
    // continuation/retry scheduling after real work already exists
    // (for example from capability evidence or later internal events).
    // =========================================================

    private static bool IsDirectUserTemporalWakeProposal(
        SegaMindEvent mindEvent,
        SegaGoalProposal proposal)
    {
        return
            mindEvent.Source == SegaMindEventSource.User
            &&
            proposal.NextWakeAtUtc.HasValue
            &&
            proposal.EvidenceSource !=
                SegaGoalEvidenceSource.CapabilityResult;
    }


    private static bool IsSameTurnUserTemporalWakeMutation(
        SegaMindEvent mindEvent,
        SegaGoalState existing,
        SegaGoalProposal proposal)
    {
        if (
            mindEvent.Source != SegaMindEventSource.User
            ||
            proposal.Action != SegaGoalProposalAction.SetWaiting
            ||
            !proposal.NextWakeAtUtc.HasValue
            ||
            proposal.EvidenceSource == SegaGoalEvidenceSource.CapabilityResult)
        {
            return false;
        }

        return
            existing.Source == SegaGoalSource.UserRequest
            &&
            existing.SourceEventId == mindEvent.Id
            &&
            existing.EvidenceCount <= 1
            &&
            !existing.LinkedCommitmentId.HasValue;
    }


    private async Task<SegaGoalApplyResult> CreateGoalAsync(
        SegaMindEvent mindEvent,
        SegaGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        // Internal goal/branch/work events carry the authoritative owning
        // goal ID in metadata. If that parent resolves while cognition is
        // in flight, the stale event may not escape by manufacturing a new
        // replacement goal. A later user event can still start new work.
        if (mindEvent.Source == SegaMindEventSource.Internal
            &&
            mindEvent.Metadata.TryGetValue(
                "goalId",
                out string? owningGoalText)
            &&
            Guid.TryParse(
                owningGoalText,
                out Guid owningGoalId)
            &&
            owningGoalId != Guid.Empty)
        {
            if (!TryGetGoal(
                    owningGoalId,
                    out SegaGoalState? owningGoal)
                ||
                owningGoal == null
                ||
                owningGoal.IsResolved)
            {
                return Reject(
                    "This internal event belongs to a goal that is already resolved. Stale internal events cannot create replacement goals.");
            }
        }

        if (proposal.Confidence < MinimumCreateConfidence)
        {
            return Reject("Goal creation confidence is below the runtime threshold.");
        }

        if (string.IsNullOrWhiteSpace(proposal.Objective))
        {
            return Reject("A new persistent goal requires an objective.");
        }

        if (proposal.CompletionCriteria.Count == 0)
        {
            return Reject(
                "A new persistent goal requires goal-specific completion criteria.");
        }

        Guid? linkedCommitmentId = ResolveLinkedCommitmentId(
            proposal.LinkedCommitmentId);

        if (!string.IsNullOrWhiteSpace(proposal.LinkedCommitmentId) &&
            !linkedCommitmentId.HasValue)
        {
            return Reject(
                "The proposed linked commitment does not exist as an active authoritative commitment.");
        }

        IReadOnlyList<Guid>? dependencies = ResolveDependencies(
            proposal.DependsOnGoalIds);

        if (dependencies == null)
        {
            return Reject(
                "One or more proposed goal dependencies do not exist.");
        }

        string fingerprint = ComputeFingerprint(proposal.Objective);

        SegaGoalState? duplicate;
        SegaGoalState? cancelledEquivalent;

        lock (_stateSync)
        {
            duplicate = _goals.Values.FirstOrDefault(goal =>
                goal.IsOpen &&
                string.Equals(
                    goal.Fingerprint,
                    fingerprint,
                    StringComparison.OrdinalIgnoreCase));

            cancelledEquivalent =
                _goals.Values
                    .Where(goal =>
                        goal.Status == SegaGoalStatus.Cancelled &&
                        string.Equals(
                            goal.Fingerprint,
                            fingerprint,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(goal =>
                        goal.ResolvedAt ?? goal.UpdatedAt)
                    .FirstOrDefault();
        }

        if (duplicate != null)
        {
            return new SegaGoalApplyResult
            {
                Action = SegaGoalApplyAction.Duplicate,
                Goal = duplicate,
                Reason = "An equivalent open goal already exists."
            };
        }

        if (cancelledEquivalent != null)
        {
            DateTimeOffset cancelledAt =
                cancelledEquivalent.ResolvedAt
                ?? cancelledEquivalent.UpdatedAt;

            bool isLaterGroundedUserRestart =
                mindEvent.Source == SegaMindEventSource.User
                &&
                mindEvent.Timestamp > cancelledAt
                &&
                proposal.EvidenceSource == SegaGoalEvidenceSource.CurrentEvent;

            if (!isLaterGroundedUserRestart)
            {
                return Reject(
                    "An equivalent goal was previously cancelled. " +
                    "Internal/background events and the same cancellation turn cannot resurrect it. " +
                    "A later grounded user request is required before this objective may be started again.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool dependenciesComplete = dependencies.All(IsGoalCompleted);

        SegaGoalStatus status;
        string? waitingFor = proposal.WaitingFor;

        if (!dependenciesComplete)
        {
            status = SegaGoalStatus.Waiting;
            waitingFor ??= "Waiting for prerequisite goals to complete.";
        }
        else if (!string.IsNullOrWhiteSpace(proposal.WaitingFor) ||
                 proposal.NextWakeAtUtc.HasValue)
        {
            status = SegaGoalStatus.Waiting;
        }
        else
        {
            status = SegaGoalStatus.Active;
        }

        SegaGoalSource source = linkedCommitmentId.HasValue
            ? SegaGoalSource.Commitment
            : mindEvent.Source == SegaMindEventSource.User
                ? SegaGoalSource.UserRequest
                : SegaGoalSource.SegaInitiated;

        SegaGoalEvidenceRecord evidence = BuildEvidence(
            Guid.Empty,
            mindEvent,
            proposal);

        SegaGoalState goal = new SegaGoalState
        {
            Id = Guid.NewGuid(),
            Fingerprint = fingerprint,
            Objective = proposal.Objective,
            Status = status,
            Source = source,
            Priority = proposal.Priority ?? 0.50,
            LinkedCommitmentId = linkedCommitmentId,
            CompletionCriteria = proposal.CompletionCriteria,
            DependsOnGoalIds = dependencies,
            WaitingFor = waitingFor,
            Blocker = null,
            NextWakeAtUtc = proposal.NextWakeAtUtc,
            EvidenceCount = 1,
            LastEvidenceSummary = ResolveEvidenceSummary(proposal),
            LastReason = proposal.Reason,
            SourceEventId = mindEvent.Id,
            SourceEventName = mindEvent.Name,
            CreatedAt = now,
            UpdatedAt = now
        }.Normalize();

        evidence = evidence with { GoalId = goal.Id };

        await _store.UpsertGoalAsync(goal, evidence, cancellationToken);

        lock (_stateSync)
        {
            _goals[goal.Id] = goal;
        }

        return new SegaGoalApplyResult
        {
            Action = SegaGoalApplyAction.Created,
            Goal = goal,
            Reason = "Persistent executable goal created from grounded cognition evidence."
        };
    }

    private async Task<SegaGoalApplyResult> RecordProgressAsync(
        SegaGoalState existing,
        SegaMindEvent mindEvent,
        SegaGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        SegaGoalEvidenceRecord evidence = BuildEvidence(
            existing.Id,
            mindEvent,
            proposal);

        SegaGoalState updated = existing with
        {
            EvidenceCount = existing.EvidenceCount + 1,
            LastEvidenceSummary = ResolveEvidenceSummary(proposal),
            LastReason = proposal.Reason,
            UpdatedAt = now
        };

        await PersistUpdatedAsync(updated, evidence, cancellationToken);

        return new SegaGoalApplyResult
        {
            Action = SegaGoalApplyAction.EvidenceRecorded,
            Goal = updated,
            Reason = "Goal progress evidence recorded."
        };
    }

    private async Task<SegaGoalApplyResult> ReviseGoalAsync(
        SegaGoalState existing,
        SegaMindEvent mindEvent,
        SegaGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumCreateConfidence)
        {
            return Reject("Goal revision confidence is below the runtime threshold.");
        }

        string objective = string.IsNullOrWhiteSpace(proposal.Objective)
            ? existing.Objective
            : proposal.Objective;

        IReadOnlyList<string> criteria = proposal.CompletionCriteria.Count == 0
            ? existing.CompletionCriteria
            : proposal.CompletionCriteria;

        IReadOnlyList<Guid> dependencies = existing.DependsOnGoalIds;

        if (proposal.DependsOnGoalIds.Count > 0)
        {
            IReadOnlyList<Guid>? resolved = ResolveDependencies(
                proposal.DependsOnGoalIds);

            if (resolved == null)
            {
                return Reject("One or more revised goal dependencies do not exist.");
            }

            dependencies = resolved;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SegaGoalEvidenceRecord evidence = BuildEvidence(existing.Id, mindEvent, proposal);

        SegaGoalState updated = existing with
        {
            Fingerprint = ComputeFingerprint(objective),
            Objective = objective,
            Priority = proposal.Priority ?? existing.Priority,
            CompletionCriteria = criteria,
            DependsOnGoalIds = dependencies,
            EvidenceCount = existing.EvidenceCount + 1,
            LastEvidenceSummary = ResolveEvidenceSummary(proposal),
            LastReason = proposal.Reason,
            UpdatedAt = now
        };

        await PersistUpdatedAsync(updated, evidence, cancellationToken);

        return new SegaGoalApplyResult
        {
            Action = SegaGoalApplyAction.Revised,
            Goal = updated,
            Reason = "Goal plan/criteria were revised using grounded evidence."
        };
    }

    private async Task<SegaGoalApplyResult> TransitionGoalAsync(
        SegaGoalState existing,
        SegaMindEvent mindEvent,
        string finalReply,
        SegaGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Action == SegaGoalProposalAction.Complete &&
            proposal.Confidence < MinimumCompletionConfidence)
        {
            return Reject("Goal completion confidence is below the runtime threshold.");
        }

        SegaGoalStatus target = ResolveTargetStatus(proposal.Action);

        if (existing.Status == target)
        {
            return new SegaGoalApplyResult
            {
                Action = SegaGoalApplyAction.NoChange,
                Goal = existing,
                Reason = "The goal is already in the requested state."
            };
        }

        if (!IsTransitionAllowed(existing.Status, target))
        {
            return Reject($"Goal transition {existing.Status}->{target} is not allowed.");
        }

        if (target == SegaGoalStatus.Active && !AreDependenciesComplete(existing))
        {
            return Reject("The goal cannot activate while prerequisite goals remain incomplete.");
        }

        if (target == SegaGoalStatus.Completed && !AreDependenciesComplete(existing))
        {
            return Reject("The goal cannot complete while prerequisite goals remain incomplete.");
        }

        if (target == SegaGoalStatus.Completed &&
            await _branches.HasIncompleteRequiredRootBranchesAsync(
                existing.Id,
                cancellationToken))
        {
            return Reject(
                "The goal cannot complete until every REQUIRED root branch has completed successfully.");
        }

        if (target == SegaGoalStatus.Waiting &&
            string.IsNullOrWhiteSpace(proposal.WaitingFor) &&
            !proposal.NextWakeAtUtc.HasValue &&
            existing.DependsOnGoalIds.Count == 0)
        {
            return Reject("Waiting requires a real waiting condition, dependency, or wake time.");
        }

        if (target == SegaGoalStatus.Blocked &&
            string.IsNullOrWhiteSpace(proposal.Blocker))
        {
            return Reject("Blocked requires a concrete blocker.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SegaGoalEvidenceRecord evidence = BuildEvidence(existing.Id, mindEvent, proposal);

        SegaGoalState updated = existing with
        {
            Status = target,
            Priority = proposal.Priority ?? existing.Priority,
            WaitingFor = target == SegaGoalStatus.Waiting
                ? proposal.WaitingFor ?? existing.WaitingFor
                : null,
            Blocker = target == SegaGoalStatus.Blocked
                ? proposal.Blocker
                : null,
            NextWakeAtUtc = target == SegaGoalStatus.Waiting
                ? proposal.NextWakeAtUtc
                : null,
            EvidenceCount = existing.EvidenceCount + 1,
            LastEvidenceSummary = ResolveEvidenceSummary(proposal),
            LastReason = proposal.Reason,
            UpdatedAt = now,
            ResolvedAt = target is SegaGoalStatus.Completed or SegaGoalStatus.Cancelled
                ? now
                : null
        };

        await PersistUpdatedAsync(updated, evidence, cancellationToken);

        if (target == SegaGoalStatus.Completed &&
            updated.LinkedCommitmentId.HasValue)
        {
            await CompleteLinkedCommitmentAsync(
                updated,
                mindEvent,
                finalReply,
                proposal,
                cancellationToken);
        }

        if (target == SegaGoalStatus.Cancelled &&
            updated.LinkedCommitmentId.HasValue &&
            mindEvent.Source == SegaMindEventSource.User)
        {
            await CancelLinkedCommitmentAsync(
                updated,
                mindEvent,
                finalReply,
                proposal,
                cancellationToken);
        }

        return new SegaGoalApplyResult
        {
            Action = SegaGoalApplyAction.Transitioned,
            Goal = updated,
            Reason = $"Goal transitioned {existing.Status}->{target}."
        };
    }

    private async Task PersistUpdatedAsync(
        SegaGoalState updated,
        SegaGoalEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        SegaGoalState normalized = updated.Normalize();

        await _store.UpsertGoalAsync(normalized, evidence, cancellationToken);

        lock (_stateSync)
        {
            _goals[normalized.Id] = normalized;
        }
    }

    private async Task CompleteLinkedCommitmentAsync(
        SegaGoalState goal,
        SegaMindEvent mindEvent,
        string finalReply,
        SegaGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        Guid commitmentId = goal.LinkedCommitmentId!.Value;

        SegaCommitmentState? commitment = _selfModel.CurrentCommitments
            .FirstOrDefault(value => value.Id == commitmentId && value.IsActive);

        if (commitment == null)
        {
            return;
        }

        SegaCommitmentEvidenceSource evidenceSource =
            proposal.EvidenceSource == SegaGoalEvidenceSource.SegaReply
                ? SegaCommitmentEvidenceSource.SegaReply
                : SegaCommitmentEvidenceSource.UserEvent;

        SegaCommitmentFormationProposal completion = new()
        {
            Action = SegaCommitmentProposalAction.Complete,
            CommitmentId = commitmentId.ToString("D"),
            Summary = commitment.Summary,
            EvidenceSource = evidenceSource,
            EvidenceQuote = proposal.EvidenceQuote,
            Reason = $"Linked persistent goal completed: {goal.Objective}",
            Confidence = proposal.Confidence
        };

        await _selfModel.ApplyCommitmentProposalsAsync(
            mindEvent,
            finalReply,
            new[] { completion },
            cancellationToken);
    }

    private async Task CancelLinkedCommitmentAsync(
        SegaGoalState goal,
        SegaMindEvent mindEvent,
        string finalReply,
        SegaGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        Guid commitmentId = goal.LinkedCommitmentId!.Value;

        SegaCommitmentState? commitment = _selfModel.CurrentCommitments
            .FirstOrDefault(value => value.Id == commitmentId && value.IsActive);

        if (commitment == null)
        {
            return;
        }

        SegaCommitmentEvidenceSource evidenceSource =
            proposal.EvidenceSource == SegaGoalEvidenceSource.SegaReply
                ? SegaCommitmentEvidenceSource.SegaReply
                : SegaCommitmentEvidenceSource.UserEvent;

        SegaCommitmentFormationProposal cancellation = new()
        {
            Action = SegaCommitmentProposalAction.Cancel,
            CommitmentId = commitmentId.ToString("D"),
            Summary = commitment.Summary,
            EvidenceSource = evidenceSource,
            EvidenceQuote = proposal.EvidenceQuote,
            Reason = $"Linked persistent goal cancelled by the user: {goal.Objective}",
            Confidence = proposal.Confidence
        };

        await _selfModel.ApplyCommitmentProposalsAsync(
            mindEvent,
            finalReply,
            new[] { cancellation },
            cancellationToken);
    }


    private Guid? ResolveLinkedCommitmentId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Guid.TryParse(value, out Guid id))
        {
            return null;
        }

        SegaCommitmentState? commitment = _selfModel.CurrentCommitments
            .FirstOrDefault(item => item.Id == id && item.IsActive);

        return commitment?.Id;
    }

    private IReadOnlyList<Guid>? ResolveDependencies(
        IReadOnlyList<string> values)
    {
        if (values == null || values.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        List<Guid> result = new();

        lock (_stateSync)
        {
            foreach (string value in values)
            {
                if (!Guid.TryParse(value, out Guid id) ||
                    !_goals.ContainsKey(id))
                {
                    return null;
                }

                if (!result.Contains(id))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    private bool AreDependenciesComplete(SegaGoalState goal)
    {
        return goal.DependsOnGoalIds.All(IsGoalCompleted);
    }

    private bool IsGoalCompleted(Guid goalId)
    {
        lock (_stateSync)
        {
            return _goals.TryGetValue(goalId, out SegaGoalState? goal) &&
                   goal.Status == SegaGoalStatus.Completed;
        }
    }

    private static SegaGoalStatus ResolveTargetStatus(
        SegaGoalProposalAction action)
    {
        return action switch
        {
            SegaGoalProposalAction.Activate => SegaGoalStatus.Active,
            SegaGoalProposalAction.SetPending => SegaGoalStatus.Pending,
            SegaGoalProposalAction.SetWaiting => SegaGoalStatus.Waiting,
            SegaGoalProposalAction.SetBlocked => SegaGoalStatus.Blocked,
            SegaGoalProposalAction.Complete => SegaGoalStatus.Completed,
            SegaGoalProposalAction.Cancel => SegaGoalStatus.Cancelled,
            _ => throw new InvalidOperationException(
                $"Goal action {action} is not a lifecycle transition.")
        };
    }

    private static bool IsTransitionAllowed(
        SegaGoalStatus from,
        SegaGoalStatus to)
    {
        if (from is SegaGoalStatus.Completed or SegaGoalStatus.Cancelled)
        {
            return false;
        }

        return to is
            SegaGoalStatus.Pending or
            SegaGoalStatus.Active or
            SegaGoalStatus.Waiting or
            SegaGoalStatus.Blocked or
            SegaGoalStatus.Completed or
            SegaGoalStatus.Cancelled;
    }

    private SegaGoalEvidenceRecord BuildEvidence(
        Guid goalId,
        SegaMindEvent mindEvent,
        SegaGoalProposal proposal)
    {
        return new SegaGoalEvidenceRecord
        {
            GoalId = goalId,
            SourceEventId = mindEvent.Id,
            SourceEventName = mindEvent.Name,
            Source = proposal.EvidenceSource,
            Quote = proposal.EvidenceQuote,
            Summary = ResolveEvidenceSummary(proposal),
            Reason = proposal.Reason,
            RecordedAt = DateTimeOffset.UtcNow
        };
    }

    private static string ResolveEvidenceSummary(SegaGoalProposal proposal)
    {
        if (!string.IsNullOrWhiteSpace(proposal.EvidenceSummary))
        {
            return proposal.EvidenceSummary;
        }

        if (!string.IsNullOrWhiteSpace(proposal.Reason))
        {
            return proposal.Reason;
        }

        return proposal.EvidenceQuote;
    }

    private static string ComputeFingerprint(string objective)
    {
        string normalized = NormalizeEvidenceText(objective).ToLowerInvariant();
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool ContainsEvidenceQuote(
        string? source,
        string quote)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(quote))
        {
            return false;
        }

        string normalizedSource = NormalizeEvidenceText(source);
        string normalizedQuote = NormalizeEvidenceText(quote);

        return normalizedSource.Contains(
            normalizedQuote,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEvidenceText(string value)
    {
        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static SegaGoalApplyResult Reject(string reason)
    {
        return new SegaGoalApplyResult
        {
            Action = SegaGoalApplyAction.Rejected,
            Reason = reason
        };
    }

    private static void LogResult(
        SegaGoalApplyResult result,
        SegaGoalProposal proposal)
    {
        Debug.WriteLine(
            $"[Goal] {result.Action.ToString().ToUpperInvariant()} | " +
            $"Id={result.Goal?.Id.ToString("D") ?? proposal.GoalId ?? "-"} | " +
            $"Status={result.Goal?.Status.ToString() ?? "-"} | " +
            $"Priority={result.Goal?.Priority.ToString("F2") ?? "-"} | " +
            $"Objective='{TrimLog(result.Goal?.Objective ?? proposal.Objective)}' | " +
            $"Reason='{TrimLog(result.Reason)}'");
    }

    private static string TrimLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string clean = value.Trim();
        const int maximumLength = 180;

        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }
}

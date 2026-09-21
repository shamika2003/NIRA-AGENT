/*
 * filename: NIRAGoalService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Mind;
using NIRAAgent.Branches;
using NIRAAgent.Self.Model;
using NIRAAgent.Authorization;

namespace NIRAAgent.Goals;

public sealed class NIRAGoalService : IHostedService
{
    private const double MinimumCreateConfidence = 0.78;
    private const double MinimumTransitionConfidence = 0.70;
    private const double MinimumCompletionConfidence = 0.76;

    private readonly NIRAGoalStore _store;
    private readonly NIRASelfModelService _selfModel;
    private readonly NIRABranchStore _branches;
    private readonly NIRAAuthorityStore _authority;

    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

    private Dictionary<Guid, NIRAGoalState> _goals = new();

    public NIRAGoalService(
        NIRAGoalStore store,
        NIRASelfModelService selfModel,
        NIRABranchStore branches,
        NIRAAuthorityStore authority)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _selfModel = selfModel ?? throw new ArgumentNullException(nameof(selfModel));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken);

        IReadOnlyList<NIRAGoalState> goals =
            await _store.ReadGoalsAsync(cancellationToken);

        lock (_stateSync)
        {
            _goals = goals.ToDictionary(goal => goal.Id, goal => goal);
        }

        // Reconcile persisted task grants against authoritative goal state
        // before the goal service is ready to accept new work. Persistent
        // project/site grants are intentionally left alone.
        _authority.RevokeInactiveTaskScopes(
            goals.Where(goal => goal.IsOpen)
                .Select(goal => goal.Id)
                .ToArray());

        Debug.WriteLine(
            $"[Goals] READY | " +
            $"Open={goals.Count(g => g.IsOpen)} | " +
            $"Active={goals.Count(g => g.Status == NIRAGoalStatus.Active)} | " +
            $"Pending={goals.Count(g => g.Status == NIRAGoalStatus.Pending)} | " +
            $"Waiting={goals.Count(g => g.Status == NIRAGoalStatus.Waiting)} | " +
            $"Blocked={goals.Count(g => g.Status == NIRAGoalStatus.Blocked)} | " +
            $"Resolved={goals.Count(g => g.IsResolved)} | " +
            $"Database='{_store.DatabasePath}'");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public IReadOnlyList<NIRAGoalState> CurrentGoals
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
        out NIRAGoalState? goal)
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
        NIRAGoalState[] open;
        NIRAGoalState[] resolved;

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

        builder.AppendLine("NIRA PERSISTENT GOALS / INTENTIONS");
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
            foreach (NIRAGoalState goal in open)
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
            foreach (NIRAGoalState goal in resolved)
            {
                builder.AppendLine(
                    $"- id={goal.Id:D} | status={goal.Status} | objective={goal.Objective}");
            }
        }

        return builder.ToString().Trim();
    }

    public async Task<IReadOnlyList<NIRAGoalApplyResult>> ApplyProposalsAsync(
        NIRAMindEvent mindEvent,
        string finalReply,
        string capabilityEvidence,
        IReadOnlyList<NIRAGoalProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mindEvent);

        if (proposals == null || proposals.Count == 0)
        {
            return Array.Empty<NIRAGoalApplyResult>();
        }

        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            List<NIRAGoalApplyResult> results = new();

            foreach (NIRAGoalProposal raw in proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();

                NIRAGoalProposal proposal;

                try
                {
                    proposal = raw.Normalize();
                }
                catch (Exception ex)
                {
                    NIRAGoalApplyResult malformed = Reject(
                        $"Malformed goal proposal: {ex.Message}");

                    LogResult(malformed, raw);
                    results.Add(malformed);
                    continue;
                }

                NIRAGoalApplyResult result =
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

    public async Task<IReadOnlyList<NIRAGoalState>> ClaimDueGoalsAsync(
        DateTimeOffset now,
        int maximumGoals = 3,
        CancellationToken cancellationToken = default)
    {
        maximumGoals = Math.Clamp(maximumGoals, 1, 8);

        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            NIRAGoalState[] due;

            lock (_stateSync)
            {
                due = _goals.Values
                    .Where(goal =>
                        goal.Status == NIRAGoalStatus.Waiting &&
                        goal.NextWakeAtUtc.HasValue &&
                        goal.NextWakeAtUtc.Value <= now)
                    .OrderByDescending(goal => goal.Priority)
                    .ThenBy(goal => goal.NextWakeAtUtc)
                    .Take(maximumGoals)
                    .ToArray();
            }

            List<NIRAGoalState> claimed = new();

            foreach (NIRAGoalState goal in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!AreDependenciesComplete(goal))
                {
                    continue;
                }

                NIRAGoalState updated = goal with
                {
                    Status = NIRAGoalStatus.Active,
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

    private async Task<NIRAGoalApplyResult> ApplyProposalCoreAsync(
        NIRAMindEvent mindEvent,
        string finalReply,
        string capabilityEvidence,
        NIRAGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        string evidenceText = proposal.EvidenceSource switch
        {
            NIRAGoalEvidenceSource.CurrentEvent =>
                mindEvent.Content,

            NIRAGoalEvidenceSource.NIRAReply =>
                finalReply,

            NIRAGoalEvidenceSource.CapabilityResult =>
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

        if (proposal.Action == NIRAGoalProposalAction.Create)
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

        NIRAGoalState? existing;

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
            return new NIRAGoalApplyResult
            {
                Action = NIRAGoalApplyAction.NoChange,
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
                "temporal commitment wake NIRA first.");
        }

        return proposal.Action switch
        {
            NIRAGoalProposalAction.RecordProgress =>
                await RecordProgressAsync(
                    existing,
                    mindEvent,
                    proposal,
                    cancellationToken),

            NIRAGoalProposalAction.Revise =>
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
        NIRAMindEvent mindEvent,
        NIRAGoalProposal proposal)
    {
        return
            mindEvent.Source == NIRAMindEventSource.User
            &&
            proposal.NextWakeAtUtc.HasValue
            &&
            proposal.EvidenceSource !=
                NIRAGoalEvidenceSource.CapabilityResult;
    }


    private static bool IsSameTurnUserTemporalWakeMutation(
        NIRAMindEvent mindEvent,
        NIRAGoalState existing,
        NIRAGoalProposal proposal)
    {
        if (
            mindEvent.Source != NIRAMindEventSource.User
            ||
            proposal.Action != NIRAGoalProposalAction.SetWaiting
            ||
            !proposal.NextWakeAtUtc.HasValue
            ||
            proposal.EvidenceSource == NIRAGoalEvidenceSource.CapabilityResult)
        {
            return false;
        }

        return
            existing.Source == NIRAGoalSource.UserRequest
            &&
            existing.SourceEventId == mindEvent.Id
            &&
            existing.EvidenceCount <= 1
            &&
            !existing.LinkedCommitmentId.HasValue;
    }


    private async Task<NIRAGoalApplyResult> CreateGoalAsync(
        NIRAMindEvent mindEvent,
        NIRAGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        // Internal goal/branch/work events carry the authoritative owning
        // goal ID in metadata. If that parent resolves while cognition is
        // in flight, the stale event may not escape by manufacturing a new
        // replacement goal. A later user event can still start new work.
        if (mindEvent.Source == NIRAMindEventSource.Internal
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
                    out NIRAGoalState? owningGoal)
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

        HashSet<Guid> staleExecutionGoals = new();
        if (mindEvent.Source == NIRAMindEventSource.User)
        {
            NIRAGoalState[] priorCandidates = CurrentGoals
                .Where(goal => goal.IsOpen &&
                    goal.SourceEventId != mindEvent.Id &&
                    mindEvent.Timestamp > goal.CreatedAt &&
                    string.Equals(goal.Fingerprint, fingerprint,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (priorCandidates.Length > 0)
            {
                IReadOnlyList<NIRABranchState> previousBranches =
                    await _branches.ReadBranchesAsync(cancellationToken);
                foreach (NIRAGoalState prior in priorCandidates)
                {
                    if (prior.Status == NIRAGoalStatus.Blocked ||
                        previousBranches.Any(branch =>
                            branch.GoalId == prior.Id &&
                            branch.ParentBranchId == null &&
                            branch.JoinPolicy == NIRABranchJoinPolicy.Required &&
                            (branch.IsResolved ||
                             branch.Status == NIRABranchStatus.Blocked)))
                        staleExecutionGoals.Add(prior.Id);
                }
            }
        }

        NIRAGoalState? duplicate;
        NIRAGoalState? cancelledEquivalent;

        lock (_stateSync)
        {
            duplicate = _goals.Values.FirstOrDefault(goal =>
                goal.IsOpen &&
                // A fresh user attempt must not deduplicate back onto an
                // older ACTIVE goal with terminal/incomplete required roots.
                !staleExecutionGoals.Contains(goal.Id) &&
                string.Equals(
                    goal.Fingerprint,
                    fingerprint,
                    StringComparison.OrdinalIgnoreCase));

            cancelledEquivalent =
                _goals.Values
                    .Where(goal =>
                        goal.Status == NIRAGoalStatus.Cancelled &&
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
            return new NIRAGoalApplyResult
            {
                Action = NIRAGoalApplyAction.Duplicate,
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
                mindEvent.Source == NIRAMindEventSource.User
                &&
                mindEvent.Timestamp > cancelledAt
                &&
                proposal.EvidenceSource == NIRAGoalEvidenceSource.CurrentEvent;

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

        NIRAGoalStatus status;
        string? waitingFor = proposal.WaitingFor;

        if (!dependenciesComplete)
        {
            status = NIRAGoalStatus.Waiting;
            waitingFor ??= "Waiting for prerequisite goals to complete.";
        }
        else if (!string.IsNullOrWhiteSpace(proposal.WaitingFor) ||
                 proposal.NextWakeAtUtc.HasValue)
        {
            status = NIRAGoalStatus.Waiting;
        }
        else
        {
            status = NIRAGoalStatus.Active;
        }

        NIRAGoalSource source = linkedCommitmentId.HasValue
            ? NIRAGoalSource.Commitment
            : mindEvent.Source == NIRAMindEventSource.User
                ? NIRAGoalSource.UserRequest
                : NIRAGoalSource.NIRAInitiated;

        NIRAGoalEvidenceRecord evidence = BuildEvidence(
            Guid.Empty,
            mindEvent,
            proposal);

        NIRAGoalState goal = new NIRAGoalState
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

        return new NIRAGoalApplyResult
        {
            Action = NIRAGoalApplyAction.Created,
            Goal = goal,
            Reason = "Persistent executable goal created from grounded cognition evidence."
        };
    }

    private async Task<NIRAGoalApplyResult> RecordProgressAsync(
        NIRAGoalState existing,
        NIRAMindEvent mindEvent,
        NIRAGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        NIRAGoalEvidenceRecord evidence = BuildEvidence(
            existing.Id,
            mindEvent,
            proposal);

        NIRAGoalState updated = existing with
        {
            EvidenceCount = existing.EvidenceCount + 1,
            LastEvidenceSummary = ResolveEvidenceSummary(proposal),
            LastReason = proposal.Reason,
            UpdatedAt = now
        };

        await PersistUpdatedAsync(updated, evidence, cancellationToken);

        return new NIRAGoalApplyResult
        {
            Action = NIRAGoalApplyAction.EvidenceRecorded,
            Goal = updated,
            Reason = "Goal progress evidence recorded."
        };
    }

    private async Task<NIRAGoalApplyResult> ReviseGoalAsync(
        NIRAGoalState existing,
        NIRAMindEvent mindEvent,
        NIRAGoalProposal proposal,
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
        NIRAGoalEvidenceRecord evidence = BuildEvidence(existing.Id, mindEvent, proposal);

        NIRAGoalState updated = existing with
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

        return new NIRAGoalApplyResult
        {
            Action = NIRAGoalApplyAction.Revised,
            Goal = updated,
            Reason = "Goal plan/criteria were revised using grounded evidence."
        };
    }

    private async Task<NIRAGoalApplyResult> TransitionGoalAsync(
        NIRAGoalState existing,
        NIRAMindEvent mindEvent,
        string finalReply,
        NIRAGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Action == NIRAGoalProposalAction.Complete &&
            proposal.Confidence < MinimumCompletionConfidence)
        {
            return Reject("Goal completion confidence is below the runtime threshold.");
        }

        NIRAGoalStatus target = ResolveTargetStatus(proposal.Action);

        // A new user's direct request must not silently clear an older
        // blocked attempt, which would inherit its exhausted work ledger.
        // A newly grounded task gets its own goal; a separate explicit
        // recovery protocol can later support audited resume in place.
        if (existing.Status == NIRAGoalStatus.Blocked &&
            target != NIRAGoalStatus.Blocked &&
            mindEvent.Source == NIRAMindEventSource.User &&
            existing.SourceEventId != mindEvent.Id &&
            mindEvent.Timestamp > existing.UpdatedAt)
            return Reject("This is a prior blocked execution. A new direct-user request must create a fresh goal instead of reactivating the previous failure ledger.");

        if (existing.Status == target)
        {
            return new NIRAGoalApplyResult
            {
                Action = NIRAGoalApplyAction.NoChange,
                Goal = existing,
                Reason = "The goal is already in the requested state."
            };
        }

        if (!IsTransitionAllowed(existing.Status, target))
        {
            return Reject($"Goal transition {existing.Status}->{target} is not allowed.");
        }

        if (target == NIRAGoalStatus.Active && !AreDependenciesComplete(existing))
        {
            return Reject("The goal cannot activate while prerequisite goals remain incomplete.");
        }

        if (target == NIRAGoalStatus.Completed && !AreDependenciesComplete(existing))
        {
            return Reject("The goal cannot complete while prerequisite goals remain incomplete.");
        }

        if (target == NIRAGoalStatus.Completed &&
            await _branches.HasIncompleteRequiredRootBranchesAsync(
                existing.Id,
                cancellationToken))
        {
            return Reject(
                "The goal cannot complete until every REQUIRED root branch has completed successfully.");
        }

        if (target == NIRAGoalStatus.Waiting &&
            string.IsNullOrWhiteSpace(proposal.WaitingFor) &&
            !proposal.NextWakeAtUtc.HasValue &&
            existing.DependsOnGoalIds.Count == 0)
        {
            return Reject("Waiting requires a real waiting condition, dependency, or wake time.");
        }

        if (target == NIRAGoalStatus.Blocked &&
            string.IsNullOrWhiteSpace(proposal.Blocker))
        {
            return Reject("Blocked requires a concrete blocker.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        NIRAGoalEvidenceRecord evidence = BuildEvidence(existing.Id, mindEvent, proposal);

        NIRAGoalState updated = existing with
        {
            Status = target,
            Priority = proposal.Priority ?? existing.Priority,
            WaitingFor = target == NIRAGoalStatus.Waiting
                ? proposal.WaitingFor ?? existing.WaitingFor
                : null,
            Blocker = target == NIRAGoalStatus.Blocked
                ? proposal.Blocker
                : null,
            NextWakeAtUtc = target == NIRAGoalStatus.Waiting
                ? proposal.NextWakeAtUtc
                : null,
            EvidenceCount = existing.EvidenceCount + 1,
            LastEvidenceSummary = ResolveEvidenceSummary(proposal),
            LastReason = proposal.Reason,
            UpdatedAt = now,
            ResolvedAt = target is NIRAGoalStatus.Completed or NIRAGoalStatus.Cancelled
                ? now
                : null
        };

        await PersistUpdatedAsync(updated, evidence, cancellationToken);

        if (updated.IsResolved)
        {
            // Fail closed on dispatch even if retirement encounters a
            // transient database error (the authorizer checks goal.IsOpen).
            // The next startup reconciliation retries retirement.
            try
            {
                _authority.RevokeTaskScopesForGoal(updated.Id);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Authorization] Task grant retirement deferred: " +
                    $"Goal={updated.Id:D} | {ex.GetType().Name}");
            }
        }

        if (target == NIRAGoalStatus.Completed &&
            updated.LinkedCommitmentId.HasValue)
        {
            await CompleteLinkedCommitmentAsync(
                updated,
                mindEvent,
                finalReply,
                proposal,
                cancellationToken);
        }

        if (target == NIRAGoalStatus.Cancelled &&
            updated.LinkedCommitmentId.HasValue &&
            mindEvent.Source == NIRAMindEventSource.User)
        {
            await CancelLinkedCommitmentAsync(
                updated,
                mindEvent,
                finalReply,
                proposal,
                cancellationToken);
        }

        return new NIRAGoalApplyResult
        {
            Action = NIRAGoalApplyAction.Transitioned,
            Goal = updated,
            Reason = $"Goal transitioned {existing.Status}->{target}."
        };
    }

    private async Task PersistUpdatedAsync(
        NIRAGoalState updated,
        NIRAGoalEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        NIRAGoalState normalized = updated.Normalize();

        await _store.UpsertGoalAsync(normalized, evidence, cancellationToken);

        lock (_stateSync)
        {
            _goals[normalized.Id] = normalized;
        }
    }

    private async Task CompleteLinkedCommitmentAsync(
        NIRAGoalState goal,
        NIRAMindEvent mindEvent,
        string finalReply,
        NIRAGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        Guid commitmentId = goal.LinkedCommitmentId!.Value;

        NIRACommitmentState? commitment = _selfModel.CurrentCommitments
            .FirstOrDefault(value => value.Id == commitmentId && value.IsActive);

        if (commitment == null)
        {
            return;
        }

        NIRACommitmentEvidenceSource evidenceSource =
            proposal.EvidenceSource == NIRAGoalEvidenceSource.NIRAReply
                ? NIRACommitmentEvidenceSource.NIRAReply
                : NIRACommitmentEvidenceSource.UserEvent;

        NIRACommitmentFormationProposal completion = new()
        {
            Action = NIRACommitmentProposalAction.Complete,
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
        NIRAGoalState goal,
        NIRAMindEvent mindEvent,
        string finalReply,
        NIRAGoalProposal proposal,
        CancellationToken cancellationToken)
    {
        Guid commitmentId = goal.LinkedCommitmentId!.Value;

        NIRACommitmentState? commitment = _selfModel.CurrentCommitments
            .FirstOrDefault(value => value.Id == commitmentId && value.IsActive);

        if (commitment == null)
        {
            return;
        }

        NIRACommitmentEvidenceSource evidenceSource =
            proposal.EvidenceSource == NIRAGoalEvidenceSource.NIRAReply
                ? NIRACommitmentEvidenceSource.NIRAReply
                : NIRACommitmentEvidenceSource.UserEvent;

        NIRACommitmentFormationProposal cancellation = new()
        {
            Action = NIRACommitmentProposalAction.Cancel,
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

        NIRACommitmentState? commitment = _selfModel.CurrentCommitments
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

    private bool AreDependenciesComplete(NIRAGoalState goal)
    {
        return goal.DependsOnGoalIds.All(IsGoalCompleted);
    }

    private bool IsGoalCompleted(Guid goalId)
    {
        lock (_stateSync)
        {
            return _goals.TryGetValue(goalId, out NIRAGoalState? goal) &&
                   goal.Status == NIRAGoalStatus.Completed;
        }
    }

    private static NIRAGoalStatus ResolveTargetStatus(
        NIRAGoalProposalAction action)
    {
        return action switch
        {
            NIRAGoalProposalAction.Activate => NIRAGoalStatus.Active,
            NIRAGoalProposalAction.SetPending => NIRAGoalStatus.Pending,
            NIRAGoalProposalAction.SetWaiting => NIRAGoalStatus.Waiting,
            NIRAGoalProposalAction.SetBlocked => NIRAGoalStatus.Blocked,
            NIRAGoalProposalAction.Complete => NIRAGoalStatus.Completed,
            NIRAGoalProposalAction.Cancel => NIRAGoalStatus.Cancelled,
            _ => throw new InvalidOperationException(
                $"Goal action {action} is not a lifecycle transition.")
        };
    }

    private static bool IsTransitionAllowed(
        NIRAGoalStatus from,
        NIRAGoalStatus to)
    {
        if (from is NIRAGoalStatus.Completed or NIRAGoalStatus.Cancelled)
        {
            return false;
        }

        return to is
            NIRAGoalStatus.Pending or
            NIRAGoalStatus.Active or
            NIRAGoalStatus.Waiting or
            NIRAGoalStatus.Blocked or
            NIRAGoalStatus.Completed or
            NIRAGoalStatus.Cancelled;
    }

    private NIRAGoalEvidenceRecord BuildEvidence(
        Guid goalId,
        NIRAMindEvent mindEvent,
        NIRAGoalProposal proposal)
    {
        return new NIRAGoalEvidenceRecord
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

    private static string ResolveEvidenceSummary(NIRAGoalProposal proposal)
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

    private static NIRAGoalApplyResult Reject(string reason)
    {
        return new NIRAGoalApplyResult
        {
            Action = NIRAGoalApplyAction.Rejected,
            Reason = reason
        };
    }

    private static void LogResult(
        NIRAGoalApplyResult result,
        NIRAGoalProposal proposal)
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



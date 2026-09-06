/*
 * filename: SegaBranchService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Hosting;

using SegaAgent.Goals;
using SegaAgent.Mind;

namespace SegaAgent.Branches;

public sealed class SegaBranchService
    : IHostedService
{
    private const double MinimumCreateConfidence =
        0.78;


    private const double MinimumTransitionConfidence =
        0.70;


    private const double MinimumCompletionConfidence =
        0.76;


    private readonly SegaBranchStore
        _store;


    private readonly SegaGoalService
        _goals;


    private readonly object
        _stateSync =
            new();


    private readonly SemaphoreSlim
        _mutationLock =
            new(
                1,
                1);


    private Dictionary<Guid, SegaBranchState>
        _branches =
            new();


    public event Action<SegaBranchResultEvent>?
        BranchResolved;


    // Read-only observers such as the desktop UI can refresh whenever
    // authoritative branch state changes. This event never drives branch
    // cognition or decides work; it only exposes that persisted state moved.
    public event Action?
        StateChanged;


    public SegaBranchService(
        SegaBranchStore store,
        SegaGoalService goals)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _goals =
            goals
            ?? throw new ArgumentNullException(
                nameof(goals));
    }


    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(
            cancellationToken);


        IReadOnlyList<SegaBranchState> branches =
            await _store.ReadBranchesAsync(
                cancellationToken);


        lock (_stateSync)
        {
            _branches =
                branches.ToDictionary(
                    branch =>
                        branch.Id,
                    branch =>
                        branch);
        }


        Debug.WriteLine(
            $"[Branches] READY | " +
            $"Open={branches.Count(branch => branch.IsOpen)} | " +
            $"Active={branches.Count(branch => branch.Status == SegaBranchStatus.Active)} | " +
            $"Waiting={branches.Count(branch => branch.Status == SegaBranchStatus.Waiting)} | " +
            $"Background={branches.Count(branch => branch.IsOpen && branch.JoinPolicy == SegaBranchJoinPolicy.Background)} | " +
            $"Resolved={branches.Count(branch => branch.IsResolved)} | " +
            $"Database='{_store.DatabasePath}'");
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    public IReadOnlyList<SegaBranchState> CurrentBranches
    {
        get
        {
            lock (_stateSync)
            {
                return _branches.Values
                    .OrderByDescending(
                        branch =>
                            branch.IsOpen)
                    .ThenByDescending(
                        branch =>
                            branch.Priority)
                    .ThenByDescending(
                        branch =>
                            branch.UpdatedAt)
                    .ToArray();
            }
        }
    }


    public bool TryGetBranch(
        Guid branchId,
        out SegaBranchState? branch)
    {
        lock (_stateSync)
        {
            return _branches.TryGetValue(
                branchId,
                out branch);
        }
    }


    public IReadOnlyList<SegaBranchState> GetForGoal(
        Guid goalId)
    {
        lock (_stateSync)
        {
            return _branches.Values
                .Where(
                    branch =>
                        branch.GoalId ==
                        goalId)
                .OrderBy(
                    branch =>
                        branch.ParentBranchId.HasValue)
                .ThenByDescending(
                    branch =>
                        branch.Priority)
                .ThenBy(
                    branch =>
                        branch.CreatedAt)
                .ToArray();
        }
    }


    public async Task<IReadOnlyList<SegaBranchState>>
        CancelOpenForGoalAsync(
            Guid goalId,
            SegaMindEvent sourceEvent,
            string reason,
            CancellationToken cancellationToken = default)
    {
        if (goalId == Guid.Empty)
        {
            throw new ArgumentException(
                "A goal cancellation cascade requires a valid goal ID.",
                nameof(goalId));
        }

        ArgumentNullException.ThrowIfNull(
            sourceEvent);

        string cleanReason =
            string.IsNullOrWhiteSpace(reason)
                ? "The parent goal was cancelled, so this branch is no longer valid work."
                : reason.Trim();

        List<SegaBranchState> cancelled =
            new();

        List<SegaBranchResultEvent> resolvedEvents =
            new();

        await _mutationLock.WaitAsync(
            cancellationToken);

        try
        {
            SegaBranchState[] open;

            lock (_stateSync)
            {
                open =
                    _branches.Values
                        .Where(
                            branch =>
                                branch.GoalId == goalId
                                &&
                                branch.IsOpen)
                        .OrderByDescending(
                            branch =>
                                branch.ParentBranchId.HasValue)
                        .ThenByDescending(
                            branch =>
                                branch.UpdatedAt)
                        .ToArray();
            }

            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            foreach (SegaBranchState existing in open)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SegaBranchEvidenceRecord evidence =
                    new()
                    {
                        BranchId =
                            existing.Id,

                        GoalId =
                            existing.GoalId,

                        SourceEventId =
                            sourceEvent.Id,

                        SourceEventName =
                            sourceEvent.Name,

                        Source =
                            sourceEvent.Source == SegaMindEventSource.User
                                ? SegaBranchEvidenceSource.CurrentEvent
                                : SegaBranchEvidenceSource.ExecutivePlan,

                        Quote =
                            sourceEvent.Source == SegaMindEventSource.User
                                ? sourceEvent.Content
                                : string.Empty,

                        Summary =
                            cleanReason,

                        Reason =
                            cleanReason,

                        RecordedAt =
                            now
                    };

                SegaBranchState updated =
                    existing with
                    {
                        Status =
                            SegaBranchStatus.Cancelled,

                        WaitingFor =
                            null,

                        Blocker =
                            null,

                        FailureReason =
                            null,

                        ResultSummary =
                            cleanReason,

                        EvidenceCount =
                            existing.EvidenceCount + 1,

                        LastEvidenceSummary =
                            cleanReason,

                        LastReason =
                            cleanReason,

                        UpdatedAt =
                            now,

                        ResolvedAt =
                            now
                    };

                await PersistUpdatedAsync(
                    updated,
                    evidence,
                    cancellationToken);

                cancelled.Add(
                    updated);

                resolvedEvents.Add(
                    BuildResultEvent(
                        updated));

                Debug.WriteLine(
                    $"[Branch] CASCADE CANCELLED | " +
                    $"Id={updated.Id:D} | " +
                    $"Goal={updated.GoalId:D} | " +
                    $"Objective='{updated.Objective}'");
            }
        }
        finally
        {
            _mutationLock.Release();
        }

        foreach (SegaBranchResultEvent resolvedEvent in resolvedEvents)
        {
            BranchResolved?.Invoke(
                resolvedEvent);
        }

        return cancelled;
    }


    public string BuildCognitionContext()
    {
        SegaBranchState[] open;

        SegaBranchState[] resolved;


        lock (_stateSync)
        {
            open =
                _branches.Values
                    .Where(
                        branch =>
                            branch.IsOpen)
                    .OrderByDescending(
                        branch =>
                            branch.Priority)
                    .ThenByDescending(
                        branch =>
                            branch.UpdatedAt)
                    .Take(
                        48)
                    .ToArray();


            resolved =
                _branches.Values
                    .Where(
                        branch =>
                            branch.IsResolved)
                    .OrderByDescending(
                        branch =>
                            branch.UpdatedAt)
                    .Take(
                        12)
                    .ToArray();
        }


        StringBuilder builder =
            new();


        builder.AppendLine(
            "SEGA FIRST-CLASS BRANCH / TASK GRAPH");


        builder.AppendLine();


        builder.AppendLine(
            "Branches are persistent responsibilities/work baskets owned by goals. " +
            "They do not think, create tools, or choose their own next actions. Sega cognition assigns bounded work separately, receives authoritative work results, then decides what the same branch should do next. " +
            "REQUIRED blocks dependent completion, OPPORTUNISTIC may join later if useful, and BACKGROUND may continue independently.");


        builder.AppendLine();


        builder.AppendLine(
            "OPEN BRANCHES");


        if (open.Length ==
            0)
        {
            builder.AppendLine(
                "- No open branches.");
        }
        else
        {
            foreach (
                IGrouping<Guid, SegaBranchState> group
                in open.GroupBy(
                    branch =>
                        branch.GoalId))
            {
                builder.AppendLine(
                    $"Goal {group.Key:D}");


                foreach (
                    SegaBranchState branch
                    in group)
                {
                    builder.AppendLine(
                        $"- id={branch.Id:D} | " +
                        $"parent={branch.ParentBranchId?.ToString("D") ?? "-"} | " +
                        $"status={branch.Status} | " +
                        $"join={branch.JoinPolicy} | " +
                        $"priority={branch.Priority:F2} | " +
                        $"objective={branch.Objective}");


                    if (branch.CompletionCriteria.Count >
                        0)
                    {
                        builder.AppendLine(
                            "  completion criteria:");


                        foreach (
                            string criterion
                            in branch.CompletionCriteria)
                        {
                            builder.AppendLine(
                                $"  - {criterion}");
                        }
                    }


                    if (branch.DependsOnBranchIds.Count >
                        0)
                    {
                        builder.AppendLine(
                            $"  depends on: {string.Join(", ", branch.DependsOnBranchIds.Select(id => id.ToString("D")))}");
                    }


                    if (!string.IsNullOrWhiteSpace(
                            branch.WaitingFor))
                    {
                        builder.AppendLine(
                            $"  waiting for: {branch.WaitingFor}");
                    }


                    if (!string.IsNullOrWhiteSpace(
                            branch.Blocker))
                    {
                        builder.AppendLine(
                            $"  blocker: {branch.Blocker}");
                    }


                    if (!string.IsNullOrWhiteSpace(
                            branch.LastEvidenceSummary))
                    {
                        builder.AppendLine(
                            $"  evidence count={branch.EvidenceCount} | " +
                            $"last evidence={branch.LastEvidenceSummary}");
                    }
                }
            }
        }


        builder.AppendLine();


        builder.AppendLine(
            "RECENTLY RESOLVED BRANCHES");


        if (resolved.Length ==
            0)
        {
            builder.AppendLine(
                "- No recently resolved branches.");
        }
        else
        {
            foreach (
                SegaBranchState branch
                in resolved)
            {
                builder.AppendLine(
                    $"- id={branch.Id:D} | goal={branch.GoalId:D} | " +
                    $"status={branch.Status} | join={branch.JoinPolicy} | " +
                    $"objective={branch.Objective}");


                if (!string.IsNullOrWhiteSpace(
                        branch.ResultSummary))
                {
                    builder.AppendLine(
                        $"  result: {branch.ResultSummary}");
                }


                if (!string.IsNullOrWhiteSpace(
                        branch.FailureReason))
                {
                    builder.AppendLine(
                        $"  failure: {branch.FailureReason}");
                }
            }
        }


        return builder
            .ToString()
            .Trim();
    }


    public async Task<IReadOnlyList<SegaBranchApplyResult>>
        ApplyProposalsAsync(
            SegaMindEvent mindEvent,
            string finalReply,
            string capabilityEvidence,
            IReadOnlyList<SegaBranchProposal> proposals,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        if (proposals ==
                null
            ||
            proposals.Count ==
                0)
        {
            return Array.Empty<SegaBranchApplyResult>();
        }


        List<SegaBranchResultEvent> resolvedEvents =
            new();


        await _mutationLock.WaitAsync(
            cancellationToken);


        List<SegaBranchApplyResult> results =
            new();


        try
        {
            foreach (
                SegaBranchProposal raw
                in proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();


                SegaBranchProposal proposal;


                try
                {
                    proposal =
                        raw.Normalize();
                }
                catch (Exception ex)
                {
                    SegaBranchApplyResult malformed =
                        Reject(
                            $"Malformed branch proposal: {ex.Message}");


                    LogResult(
                        malformed,
                        raw);


                    results.Add(
                        malformed);


                    continue;
                }


                SegaBranchApplyResult result =
                    await ApplyProposalCoreAsync(
                        mindEvent,
                        finalReply,
                        capabilityEvidence,
                        proposal,
                        cancellationToken);


                LogResult(
                    result,
                    proposal);


                results.Add(
                    result);


                if (
                    result.Changed
                    &&
                    result.Branch?.IsResolved ==
                        true)
                {
                    resolvedEvents.Add(
                        BuildResultEvent(
                            result.Branch));
                }
            }
        }
        finally
        {
            _mutationLock.Release();
        }


        foreach (
            SegaBranchResultEvent resolvedEvent
            in resolvedEvents)
        {
            BranchResolved?.Invoke(
                resolvedEvent);
        }


        return results;
    }


    private async Task<SegaBranchApplyResult>
        ApplyProposalCoreAsync(
            SegaMindEvent mindEvent,
            string finalReply,
            string capabilityEvidence,
            SegaBranchProposal proposal,
            CancellationToken cancellationToken)
    {
        SegaBranchApplyResult? evidenceRejection =
            ValidateProposalEvidence(
                mindEvent,
                finalReply,
                capabilityEvidence,
                proposal);


        if (evidenceRejection !=
            null)
        {
            return evidenceRejection;
        }


        if (proposal.Action ==
            SegaBranchProposalAction.Create)
        {
            return await CreateBranchAsync(
                mindEvent,
                proposal,
                cancellationToken);
        }


        if (proposal.Confidence <
            MinimumTransitionConfidence)
        {
            return Reject(
                "Branch mutation confidence is below the runtime threshold.");
        }


        if (string.IsNullOrWhiteSpace(
                proposal.BranchId)
            ||
            !Guid.TryParse(
                proposal.BranchId,
                out Guid branchId))
        {
            return Reject(
                "A non-create branch mutation requires an exact existing branch GUID.");
        }


        SegaBranchState? existing;


        lock (_stateSync)
        {
            _branches.TryGetValue(
                branchId,
                out existing);
        }


        if (existing ==
            null)
        {
            return Reject(
                "The proposed branch ID does not exist in authoritative branch state.");
        }


        if (existing.IsResolved)
        {
            return new SegaBranchApplyResult
            {
                Action =
                    SegaBranchApplyAction.NoChange,

                Branch =
                    existing,

                Reason =
                    "Resolved branches are terminal."
            };
        }


        return proposal.Action switch
        {
            SegaBranchProposalAction.RecordProgress =>
                await RecordProgressAsync(
                    existing,
                    mindEvent,
                    proposal,
                    cancellationToken),

            SegaBranchProposalAction.Revise =>
                await ReviseBranchAsync(
                    existing,
                    mindEvent,
                    proposal,
                    cancellationToken),

            _ =>
                await TransitionBranchAsync(
                    existing,
                    mindEvent,
                    proposal,
                    cancellationToken)
        };
    }


    private static SegaBranchApplyResult?
        ValidateProposalEvidence(
            SegaMindEvent mindEvent,
            string finalReply,
            string capabilityEvidence,
            SegaBranchProposal proposal)
    {
        if (proposal.EvidenceSource ==
            SegaBranchEvidenceSource.ExecutivePlan)
        {
            if (proposal.Action is not
                SegaBranchProposalAction.Create
                and not SegaBranchProposalAction.Revise)
            {
                return Reject(
                    "ExecutivePlan evidence is only valid for branch creation or revision planning.");
            }


            if (string.IsNullOrWhiteSpace(
                    proposal.EvidenceSummary)
                &&
                string.IsNullOrWhiteSpace(
                    proposal.Reason))
            {
                return Reject(
                    "ExecutivePlan branch planning requires an evidence summary or operational reason.");
            }


            return null;
        }


        string evidenceText =
            proposal.EvidenceSource switch
            {
                SegaBranchEvidenceSource.CurrentEvent =>
                    mindEvent.Content,

                SegaBranchEvidenceSource.SegaReply =>
                    finalReply,

                SegaBranchEvidenceSource.CapabilityResult =>
                    capabilityEvidence,

                _ =>
                    string.Empty
            };


        if (string.IsNullOrWhiteSpace(
                proposal.EvidenceQuote))
        {
            return Reject(
                "A branch mutation using CurrentEvent, SegaReply, or CapabilityResult requires a grounded evidence quote.");
        }


        if (!ContainsEvidenceQuote(
                evidenceText,
                proposal.EvidenceQuote))
        {
            return Reject(
                $"Evidence quote is not present in the declared {proposal.EvidenceSource} source.");
        }


        return null;
    }


    private async Task<SegaBranchApplyResult>
        CreateBranchAsync(
            SegaMindEvent mindEvent,
            SegaBranchProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.Confidence <
            MinimumCreateConfidence)
        {
            return Reject(
                "Branch creation confidence is below the runtime threshold.");
        }


        if (string.IsNullOrWhiteSpace(
                proposal.GoalId)
            ||
            !Guid.TryParse(
                proposal.GoalId,
                out Guid goalId))
        {
            return Reject(
                "A new branch requires an exact existing parent goal GUID.");
        }


        SegaGoalState? goal =
            _goals.CurrentGoals
                .FirstOrDefault(
                    value =>
                        value.Id ==
                        goalId);


        if (goal ==
                null
            ||
            goal.IsResolved)
        {
            return Reject(
                "The proposed parent goal does not exist as an open authoritative goal.");
        }


        if (string.IsNullOrWhiteSpace(
                proposal.Objective))
        {
            return Reject(
                "A new branch requires an objective.");
        }


        if (proposal.CompletionCriteria.Count ==
            0)
        {
            return Reject(
                "A new branch requires branch-specific completion criteria.");
        }


        Guid? parentBranchId =
            ResolveParentBranchId(
                goalId,
                proposal.ParentBranchId);


        if (!string.IsNullOrWhiteSpace(
                proposal.ParentBranchId)
            &&
            !parentBranchId.HasValue)
        {
            return Reject(
                "The proposed parent branch does not exist as an open branch under the same goal.");
        }


        IReadOnlyList<Guid>? dependencies =
            ResolveDependencies(
                goalId,
                proposal.DependsOnBranchIds,
                branchBeingRevised: null);


        if (dependencies ==
            null)
        {
            return Reject(
                "One or more proposed branch dependencies do not exist under the same goal.");
        }


        string fingerprint =
            ComputeFingerprint(
                proposal.Objective);


        SegaBranchState? duplicate;


        lock (_stateSync)
        {
            duplicate =
                _branches.Values
                    .FirstOrDefault(
                        branch =>
                            branch.IsOpen
                            &&
                            branch.GoalId ==
                                goalId
                            &&
                            branch.ParentBranchId ==
                                parentBranchId
                            &&
                            string.Equals(
                                branch.Fingerprint,
                                fingerprint,
                                StringComparison.OrdinalIgnoreCase));
        }


        if (duplicate !=
            null)
        {
            return new SegaBranchApplyResult
            {
                Action =
                    SegaBranchApplyAction.Duplicate,

                Branch =
                    duplicate,

                Reason =
                    "An equivalent open branch already exists under this goal/parent."
            };
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        bool dependenciesComplete =
            dependencies.All(
                IsBranchCompleted);


        SegaBranchStatus status;


        string? waitingFor =
            proposal.WaitingFor;


        if (!dependenciesComplete)
        {
            status =
                SegaBranchStatus.Waiting;


            waitingFor ??=
                "Waiting for prerequisite branches to complete.";
        }
        else if (!string.IsNullOrWhiteSpace(
                     proposal.WaitingFor))
        {
            status =
                SegaBranchStatus.Waiting;
        }
        else
        {
            status =
                SegaBranchStatus.Active;
        }


        SegaBranchEvidenceRecord evidence =
            BuildEvidence(
                Guid.Empty,
                goalId,
                mindEvent,
                proposal);


        SegaBranchState branch =
            new SegaBranchState
            {
                Id =
                    Guid.NewGuid(),

                GoalId =
                    goalId,

                ParentBranchId =
                    parentBranchId,

                Fingerprint =
                    fingerprint,

                Objective =
                    proposal.Objective,

                Status =
                    status,

                JoinPolicy =
                    proposal.JoinPolicy
                    ?? SegaBranchJoinPolicy.Required,

                Priority =
                    proposal.Priority
                    ?? goal.Priority,

                CompletionCriteria =
                    proposal.CompletionCriteria,

                DependsOnBranchIds =
                    dependencies,

                WaitingFor =
                    waitingFor,

                EvidenceCount =
                    1,

                LastEvidenceSummary =
                    ResolveEvidenceSummary(
                        proposal),

                LastReason =
                    proposal.Reason,

                SourceEventId =
                    mindEvent.Id,

                SourceEventName =
                    mindEvent.Name,

                CreatedAt =
                    now,

                UpdatedAt =
                    now
            }
            .Normalize();


        evidence =
            evidence with
            {
                BranchId =
                    branch.Id
            };


        await _store.UpsertBranchAsync(
            branch,
            evidence,
            cancellationToken);


        lock (_stateSync)
        {
            _branches[branch.Id] =
                branch;
        }


        StateChanged?.Invoke();


        return new SegaBranchApplyResult
        {
            Action =
                SegaBranchApplyAction.Created,

            Branch =
                branch,

            Reason =
                "Persistent branch created from grounded cognition evidence."
        };
    }


    private async Task<SegaBranchApplyResult>
        RecordProgressAsync(
            SegaBranchState existing,
            SegaMindEvent mindEvent,
            SegaBranchProposal proposal,
            CancellationToken cancellationToken)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaBranchEvidenceRecord evidence =
            BuildEvidence(
                existing.Id,
                existing.GoalId,
                mindEvent,
                proposal);


        SegaBranchState updated =
            existing with
            {
                EvidenceCount =
                    existing.EvidenceCount +
                    1,

                LastEvidenceSummary =
                    ResolveEvidenceSummary(
                        proposal),

                LastReason =
                    proposal.Reason,

                UpdatedAt =
                    now
            };


        await PersistUpdatedAsync(
            updated,
            evidence,
            cancellationToken);


        return new SegaBranchApplyResult
        {
            Action =
                SegaBranchApplyAction.EvidenceRecorded,

            Branch =
                updated,

            Reason =
                "Branch progress evidence recorded."
        };
    }


    private async Task<SegaBranchApplyResult>
        ReviseBranchAsync(
            SegaBranchState existing,
            SegaMindEvent mindEvent,
            SegaBranchProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.Confidence <
            MinimumCreateConfidence)
        {
            return Reject(
                "Branch revision confidence is below the runtime threshold.");
        }


        string objective =
            string.IsNullOrWhiteSpace(
                proposal.Objective)
                ? existing.Objective
                : proposal.Objective;


        IReadOnlyList<string> criteria =
            proposal.CompletionCriteria.Count ==
                0
                ? existing.CompletionCriteria
                : proposal.CompletionCriteria;


        IReadOnlyList<Guid> dependencies =
            existing.DependsOnBranchIds;


        if (proposal.DependsOnBranchIds.Count >
            0)
        {
            IReadOnlyList<Guid>? resolved =
                ResolveDependencies(
                    existing.GoalId,
                    proposal.DependsOnBranchIds,
                    existing.Id);


            if (resolved ==
                null)
            {
                return Reject(
                    "One or more revised branch dependencies do not exist under the same goal or would create a dependency cycle.");
            }


            dependencies =
                resolved;
        }


        bool dependenciesComplete =
            dependencies.All(
                IsBranchCompleted);


        SegaBranchStatus status =
            existing.Status;


        string? waitingFor =
            existing.WaitingFor;


        if (
            status ==
                SegaBranchStatus.Active
            &&
            !dependenciesComplete)
        {
            status =
                SegaBranchStatus.Waiting;


            waitingFor =
                "Waiting for revised prerequisite branches to complete.";
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaBranchEvidenceRecord evidence =
            BuildEvidence(
                existing.Id,
                existing.GoalId,
                mindEvent,
                proposal);


        SegaBranchState updated =
            existing with
            {
                Fingerprint =
                    ComputeFingerprint(
                        objective),

                Objective =
                    objective,

                JoinPolicy =
                    proposal.JoinPolicy
                    ?? existing.JoinPolicy,

                Priority =
                    proposal.Priority
                    ?? existing.Priority,

                CompletionCriteria =
                    criteria,

                DependsOnBranchIds =
                    dependencies,

                Status =
                    status,

                WaitingFor =
                    waitingFor,

                EvidenceCount =
                    existing.EvidenceCount +
                    1,

                LastEvidenceSummary =
                    ResolveEvidenceSummary(
                        proposal),

                LastReason =
                    proposal.Reason,

                UpdatedAt =
                    now
            };


        await PersistUpdatedAsync(
            updated,
            evidence,
            cancellationToken);


        return new SegaBranchApplyResult
        {
            Action =
                SegaBranchApplyAction.Revised,

            Branch =
                updated,

            Reason =
                "Branch plan/dependencies were revised using grounded evidence."
        };
    }


    private async Task<SegaBranchApplyResult>
        TransitionBranchAsync(
            SegaBranchState existing,
            SegaMindEvent mindEvent,
            SegaBranchProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.Action ==
                SegaBranchProposalAction.Complete
            &&
            proposal.Confidence <
                MinimumCompletionConfidence)
        {
            return Reject(
                "Branch completion confidence is below the runtime threshold.");
        }


        SegaBranchStatus target =
            ResolveTargetStatus(
                proposal.Action);


        if (existing.Status ==
            target)
        {
            return new SegaBranchApplyResult
            {
                Action =
                    SegaBranchApplyAction.NoChange,

                Branch =
                    existing,

                Reason =
                    "The branch is already in the requested state."
            };
        }


        if (!IsTransitionAllowed(
                existing.Status,
                target))
        {
            return Reject(
                $"Branch transition {existing.Status}->{target} is not allowed.");
        }


        if (
            target ==
                SegaBranchStatus.Active
            &&
            !AreDependenciesComplete(
                existing))
        {
            return Reject(
                "The branch cannot activate while prerequisite branches remain incomplete.");
        }


        if (target ==
            SegaBranchStatus.Completed)
        {
            if (!AreDependenciesComplete(
                    existing))
            {
                return Reject(
                    "The branch cannot complete while prerequisite branches remain incomplete.");
            }


            if (!AreRequiredChildrenCompletedSuccessfully(
                    existing.Id))
            {
                return Reject(
                    "The branch cannot complete until every REQUIRED child branch has completed successfully.");
            }


            if (string.IsNullOrWhiteSpace(
                    proposal.ResultSummary))
            {
                return Reject(
                    "A completed branch requires a concrete result/conclusion summary.");
            }
        }


        if (
            target ==
                SegaBranchStatus.Waiting
            &&
            string.IsNullOrWhiteSpace(
                proposal.WaitingFor)
            &&
            existing.DependsOnBranchIds.Count ==
                0)
        {
            return Reject(
                "Waiting requires a real waiting condition or branch dependency.");
        }


        if (
            target ==
                SegaBranchStatus.Blocked
            &&
            string.IsNullOrWhiteSpace(
                proposal.Blocker))
        {
            return Reject(
                "Blocked requires a concrete blocker.");
        }


        if (
            target ==
                SegaBranchStatus.Failed
            &&
            string.IsNullOrWhiteSpace(
                proposal.FailureReason))
        {
            return Reject(
                "Failed requires a concrete failure reason.");
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaBranchEvidenceRecord evidence =
            BuildEvidence(
                existing.Id,
                existing.GoalId,
                mindEvent,
                proposal);


        SegaBranchState updated =
            existing with
            {
                Status =
                    target,

                Priority =
                    proposal.Priority
                    ?? existing.Priority,

                WaitingFor =
                    target ==
                        SegaBranchStatus.Waiting
                        ? proposal.WaitingFor
                            ?? existing.WaitingFor
                        : null,

                Blocker =
                    target ==
                        SegaBranchStatus.Blocked
                        ? proposal.Blocker
                        : null,

                FailureReason =
                    target ==
                        SegaBranchStatus.Failed
                        ? proposal.FailureReason
                        : null,

                ResultSummary =
                    target ==
                        SegaBranchStatus.Completed
                        ? proposal.ResultSummary
                        : existing.ResultSummary,

                EvidenceCount =
                    existing.EvidenceCount +
                    1,

                LastEvidenceSummary =
                    ResolveEvidenceSummary(
                        proposal),

                LastReason =
                    proposal.Reason,

                UpdatedAt =
                    now,

                ResolvedAt =
                    target is
                        SegaBranchStatus.Completed
                        or SegaBranchStatus.Failed
                        or SegaBranchStatus.Cancelled
                        ? now
                        : null
            };


        await PersistUpdatedAsync(
            updated,
            evidence,
            cancellationToken);


        return new SegaBranchApplyResult
        {
            Action =
                SegaBranchApplyAction.Transitioned,

            Branch =
                updated,

            Reason =
                $"Branch transitioned {existing.Status}->{target}."
        };
    }


    private async Task PersistUpdatedAsync(
        SegaBranchState updated,
        SegaBranchEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        SegaBranchState normalized =
            updated.Normalize();


        await _store.UpsertBranchAsync(
            normalized,
            evidence,
            cancellationToken);


        lock (_stateSync)
        {
            _branches[normalized.Id] =
                normalized;
        }


        StateChanged?.Invoke();
    }


    private Guid? ResolveParentBranchId(
        Guid goalId,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value)
            ||
            !Guid.TryParse(
                value,
                out Guid id))
        {
            return null;
        }


        lock (_stateSync)
        {
            if (
                _branches.TryGetValue(
                    id,
                    out SegaBranchState? branch)
                &&
                branch.GoalId ==
                    goalId
                &&
                branch.IsOpen)
            {
                return branch.Id;
            }
        }


        return null;
    }


    private IReadOnlyList<Guid>? ResolveDependencies(
        Guid goalId,
        IReadOnlyList<string> values,
        Guid? branchBeingRevised)
    {
        if (values ==
                null
            ||
            values.Count ==
                0)
        {
            return Array.Empty<Guid>();
        }


        List<Guid> result =
            new();


        lock (_stateSync)
        {
            foreach (
                string value
                in values)
            {
                if (!Guid.TryParse(
                        value,
                        out Guid id)
                    ||
                    !_branches.TryGetValue(
                        id,
                        out SegaBranchState? dependency)
                    ||
                    dependency.GoalId !=
                        goalId)
                {
                    return null;
                }


                if (
                    branchBeingRevised.HasValue
                    &&
                    id ==
                        branchBeingRevised.Value)
                {
                    return null;
                }


                if (!result.Contains(
                        id))
                {
                    result.Add(
                        id);
                }
            }


            if (
                branchBeingRevised.HasValue
                &&
                WouldCreateDependencyCycleUnsafe(
                    branchBeingRevised.Value,
                    result))
            {
                return null;
            }
        }


        return result;
    }


    private bool AreDependenciesComplete(
        SegaBranchState branch)
    {
        lock (_stateSync)
        {
            return AreDependenciesCompleteUnsafe(
                branch);
        }
    }


    private bool AreDependenciesCompleteUnsafe(
        SegaBranchState branch)
    {
        return branch.DependsOnBranchIds.All(
            id =>
                _branches.TryGetValue(
                    id,
                    out SegaBranchState? dependency)
                &&
                dependency.Status ==
                    SegaBranchStatus.Completed);
    }


    private bool IsBranchCompleted(
        Guid branchId)
    {
        lock (_stateSync)
        {
            return _branches.TryGetValue(
                       branchId,
                       out SegaBranchState? branch)
                   &&
                   branch.Status ==
                       SegaBranchStatus.Completed;
        }
    }


    private bool AreRequiredChildrenCompletedSuccessfully(
        Guid parentBranchId)
    {
        lock (_stateSync)
        {
            return AreRequiredChildrenCompletedSuccessfullyUnsafe(
                parentBranchId);
        }
    }


    private bool AreRequiredChildrenCompletedSuccessfullyUnsafe(
        Guid parentBranchId)
    {
        return _branches.Values
            .Where(
                branch =>
                    branch.ParentBranchId ==
                        parentBranchId
                    &&
                    branch.JoinPolicy ==
                        SegaBranchJoinPolicy.Required)
            .All(
                branch =>
                    branch.Status ==
                        SegaBranchStatus.Completed);
    }


    private bool WouldCreateDependencyCycleUnsafe(
        Guid branchId,
        IReadOnlyList<Guid> proposedDependencies)
    {
        foreach (
            Guid dependencyId
            in proposedDependencies)
        {
            if (dependencyId ==
                branchId)
            {
                return true;
            }


            HashSet<Guid> visited =
                new();


            Stack<Guid> pending =
                new();


            pending.Push(
                dependencyId);


            while (pending.Count >
                0)
            {
                Guid current =
                    pending.Pop();


                if (!visited.Add(
                        current))
                {
                    continue;
                }


                if (current ==
                    branchId)
                {
                    return true;
                }


                if (!_branches.TryGetValue(
                        current,
                        out SegaBranchState? dependency))
                {
                    continue;
                }


                foreach (
                    Guid next
                    in dependency.DependsOnBranchIds)
                {
                    pending.Push(
                        next);
                }
            }
        }


        return false;
    }


    private static SegaBranchStatus ResolveTargetStatus(
        SegaBranchProposalAction action)
    {
        return action switch
        {
            SegaBranchProposalAction.Activate =>
                SegaBranchStatus.Active,

            SegaBranchProposalAction.SetPending =>
                SegaBranchStatus.Pending,

            SegaBranchProposalAction.SetWaiting =>
                SegaBranchStatus.Waiting,

            SegaBranchProposalAction.SetBlocked =>
                SegaBranchStatus.Blocked,

            SegaBranchProposalAction.Complete =>
                SegaBranchStatus.Completed,

            SegaBranchProposalAction.Fail =>
                SegaBranchStatus.Failed,

            SegaBranchProposalAction.Cancel =>
                SegaBranchStatus.Cancelled,

            _ =>
                throw new InvalidOperationException(
                    $"Branch action {action} is not a lifecycle transition.")
        };
    }


    private static bool IsTransitionAllowed(
        SegaBranchStatus from,
        SegaBranchStatus to)
    {
        if (from is
            SegaBranchStatus.Completed
            or SegaBranchStatus.Failed
            or SegaBranchStatus.Cancelled)
        {
            return false;
        }


        return to is
            SegaBranchStatus.Pending
            or SegaBranchStatus.Active
            or SegaBranchStatus.Waiting
            or SegaBranchStatus.Blocked
            or SegaBranchStatus.Completed
            or SegaBranchStatus.Failed
            or SegaBranchStatus.Cancelled;
    }


    private static SegaBranchEvidenceRecord BuildEvidence(
        Guid branchId,
        Guid goalId,
        SegaMindEvent mindEvent,
        SegaBranchProposal proposal)
    {
        return new SegaBranchEvidenceRecord
        {
            BranchId =
                branchId,

            GoalId =
                goalId,

            SourceEventId =
                mindEvent.Id,

            SourceEventName =
                mindEvent.Name,

            Source =
                proposal.EvidenceSource,

            Quote =
                proposal.EvidenceQuote,

            Summary =
                ResolveEvidenceSummary(
                    proposal),

            Reason =
                proposal.Reason,

            RecordedAt =
                DateTimeOffset.UtcNow
        };
    }


    private static string ResolveEvidenceSummary(
        SegaBranchProposal proposal)
    {
        if (!string.IsNullOrWhiteSpace(
                proposal.EvidenceSummary))
        {
            return proposal.EvidenceSummary;
        }


        if (!string.IsNullOrWhiteSpace(
                proposal.Reason))
        {
            return proposal.Reason;
        }


        return proposal.EvidenceQuote;
    }


    private static string ComputeFingerprint(
        string objective)
    {
        string normalized =
            NormalizeEvidenceText(
                    objective)
                .ToLowerInvariant();


        byte[] bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    normalized));


        return Convert
            .ToHexString(
                bytes)
            .ToLowerInvariant();
    }


    private static bool ContainsEvidenceQuote(
        string? source,
        string quote)
    {
        if (string.IsNullOrWhiteSpace(
                source)
            ||
            string.IsNullOrWhiteSpace(
                quote))
        {
            return false;
        }


        string normalizedSource =
            NormalizeEvidenceText(
                source);


        string normalizedQuote =
            NormalizeEvidenceText(
                quote);


        return normalizedSource.Contains(
            normalizedQuote,
            StringComparison.OrdinalIgnoreCase);
    }


    private static string NormalizeEvidenceText(
        string value)
    {
        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }


    private static SegaBranchResultEvent BuildResultEvent(
        SegaBranchState branch)
    {
        return new SegaBranchResultEvent
        {
            BranchId =
                branch.Id,

            GoalId =
                branch.GoalId,

            ParentBranchId =
                branch.ParentBranchId,

            Status =
                branch.Status,

            JoinPolicy =
                branch.JoinPolicy,

            Objective =
                branch.Objective,

            ResultSummary =
                branch.ResultSummary,

            FailureReason =
                branch.FailureReason,

            ResolvedAt =
                branch.ResolvedAt
                ?? branch.UpdatedAt
        };
    }


    private static SegaBranchApplyResult Reject(
        string reason)
    {
        return new SegaBranchApplyResult
        {
            Action =
                SegaBranchApplyAction.Rejected,

            Reason =
                reason
        };
    }


    private static void LogResult(
        SegaBranchApplyResult result,
        SegaBranchProposal proposal)
    {
        Debug.WriteLine(
            $"[Branch] {result.Action.ToString().ToUpperInvariant()} | " +
            $"Id={result.Branch?.Id.ToString("D") ?? proposal.BranchId ?? "-"} | " +
            $"Goal={result.Branch?.GoalId.ToString("D") ?? proposal.GoalId ?? "-"} | " +
            $"Status={result.Branch?.Status.ToString() ?? "-"} | " +
            $"Join={result.Branch?.JoinPolicy.ToString() ?? proposal.JoinPolicy?.ToString() ?? "-"} | " +
            $"Objective='{TrimLog(result.Branch?.Objective ?? proposal.Objective)}' | " +
            $"Reason='{TrimLog(result.Reason)}'");
    }


    private static string TrimLog(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }


        string clean =
            value.Trim();


        const int maximumLength =
            180;


        return clean.Length <=
                maximumLength
            ? clean
            : clean[..maximumLength] +
                "...";
    }
}
/*
 * filename: NIRABranchService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Goals;
using NIRAAgent.Mind;

namespace NIRAAgent.Branches;

public sealed class NIRABranchService
    : IHostedService
{
    private const double MinimumCreateConfidence =
        0.78;


    private const double MinimumTransitionConfidence =
        0.70;


    private const double MinimumCompletionConfidence =
        0.76;


    private readonly NIRABranchStore
        _store;


    private readonly NIRAGoalService
        _goals;


    private readonly object
        _stateSync =
            new();


    private readonly SemaphoreSlim
        _mutationLock =
            new(
                1,
                1);


    private Dictionary<Guid, NIRABranchState>
        _branches =
            new();


    public event Action<NIRABranchResultEvent>?
        BranchResolved;


    // Read-only observers such as the desktop UI can refresh whenever
    // authoritative branch state changes. This event never drives branch
    // cognition or decides work; it only exposes that persisted state moved.
    public event Action?
        StateChanged;


    public NIRABranchService(
        NIRABranchStore store,
        NIRAGoalService goals)
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


        IReadOnlyList<NIRABranchState> branches =
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
            $"Active={branches.Count(branch => branch.Status == NIRABranchStatus.Active)} | " +
            $"Waiting={branches.Count(branch => branch.Status == NIRABranchStatus.Waiting)} | " +
            $"Background={branches.Count(branch => branch.IsOpen && branch.JoinPolicy == NIRABranchJoinPolicy.Background)} | " +
            $"Resolved={branches.Count(branch => branch.IsResolved)} | " +
            $"Database='{_store.DatabasePath}'");
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    public IReadOnlyList<NIRABranchState> CurrentBranches
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
        out NIRABranchState? branch)
    {
        lock (_stateSync)
        {
            return _branches.TryGetValue(
                branchId,
                out branch);
        }
    }


    public IReadOnlyList<NIRABranchState> GetForGoal(
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


    public async Task<IReadOnlyList<NIRABranchState>>
        CancelOpenForGoalAsync(
            Guid goalId,
            NIRAMindEvent sourceEvent,
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

        List<NIRABranchState> cancelled =
            new();

        List<NIRABranchResultEvent> resolvedEvents =
            new();

        await _mutationLock.WaitAsync(
            cancellationToken);

        try
        {
            NIRABranchState[] open;

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

            foreach (NIRABranchState existing in open)
            {
                cancellationToken.ThrowIfCancellationRequested();

                NIRABranchEvidenceRecord evidence =
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
                            sourceEvent.Source == NIRAMindEventSource.User
                                ? NIRABranchEvidenceSource.CurrentEvent
                                : NIRABranchEvidenceSource.ExecutivePlan,

                        Quote =
                            sourceEvent.Source == NIRAMindEventSource.User
                                ? sourceEvent.Content
                                : string.Empty,

                        Summary =
                            cleanReason,

                        Reason =
                            cleanReason,

                        RecordedAt =
                            now
                    };

                NIRABranchState updated =
                    existing with
                    {
                        Status =
                            NIRABranchStatus.Cancelled,

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

        foreach (NIRABranchResultEvent resolvedEvent in resolvedEvents)
        {
            BranchResolved?.Invoke(
                resolvedEvent);
        }

        return cancelled;
    }


    public string BuildCognitionContext()
    {
        NIRABranchState[] open;

        NIRABranchState[] resolved;


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
            "NIRA FIRST-CLASS BRANCH / TASK GRAPH");


        builder.AppendLine();


        builder.AppendLine(
            "Branches are persistent responsibilities/work baskets owned by goals. " +
            "They do not think, create tools, or choose their own next actions. NIRA cognition assigns bounded work separately, receives authoritative work results, then decides what the same branch should do next. " +
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
                IGrouping<Guid, NIRABranchState> group
                in open.GroupBy(
                    branch =>
                        branch.GoalId))
            {
                builder.AppendLine(
                    $"Goal {group.Key:D}");


                foreach (
                    NIRABranchState branch
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
                NIRABranchState branch
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


    public async Task<IReadOnlyList<NIRABranchApplyResult>>
        ApplyProposalsAsync(
            NIRAMindEvent mindEvent,
            string finalReply,
            string capabilityEvidence,
            IReadOnlyList<NIRABranchProposal> proposals,
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
            return Array.Empty<NIRABranchApplyResult>();
        }


        List<NIRABranchResultEvent> resolvedEvents =
            new();


        await _mutationLock.WaitAsync(
            cancellationToken);


        List<NIRABranchApplyResult> results =
            new();


        try
        {
            foreach (
                NIRABranchProposal raw
                in proposals)
            {
                cancellationToken.ThrowIfCancellationRequested();


                NIRABranchProposal proposal;


                try
                {
                    proposal =
                        raw.Normalize();
                }
                catch (Exception ex)
                {
                    NIRABranchApplyResult malformed =
                        Reject(
                            $"Malformed branch proposal: {ex.Message}");


                    LogResult(
                        malformed,
                        raw);


                    results.Add(
                        malformed);


                    continue;
                }


                NIRABranchApplyResult result =
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
            NIRABranchResultEvent resolvedEvent
            in resolvedEvents)
        {
            BranchResolved?.Invoke(
                resolvedEvent);
        }


        return results;
    }


    private async Task<NIRABranchApplyResult>
        ApplyProposalCoreAsync(
            NIRAMindEvent mindEvent,
            string finalReply,
            string capabilityEvidence,
            NIRABranchProposal proposal,
            CancellationToken cancellationToken)
    {
        NIRABranchApplyResult? evidenceRejection =
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
            NIRABranchProposalAction.Create)
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


        NIRABranchState? existing;


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
            return new NIRABranchApplyResult
            {
                Action =
                    NIRABranchApplyAction.NoChange,

                Branch =
                    existing,

                Reason =
                    "Resolved branches are terminal."
            };
        }


        return proposal.Action switch
        {
            NIRABranchProposalAction.RecordProgress =>
                await RecordProgressAsync(
                    existing,
                    mindEvent,
                    proposal,
                    cancellationToken),

            NIRABranchProposalAction.Revise =>
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


    private static NIRABranchApplyResult?
        ValidateProposalEvidence(
            NIRAMindEvent mindEvent,
            string finalReply,
            string capabilityEvidence,
            NIRABranchProposal proposal)
    {
        if (proposal.EvidenceSource ==
            NIRABranchEvidenceSource.ExecutivePlan)
        {
            if (proposal.Action is not
                NIRABranchProposalAction.Create
                and not NIRABranchProposalAction.Revise)
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
                NIRABranchEvidenceSource.CurrentEvent =>
                    mindEvent.Content,

                NIRABranchEvidenceSource.NIRAReply =>
                    finalReply,

                NIRABranchEvidenceSource.CapabilityResult =>
                    capabilityEvidence,

                _ =>
                    string.Empty
            };


        if (string.IsNullOrWhiteSpace(
                proposal.EvidenceQuote))
        {
            return Reject(
                "A branch mutation using CurrentEvent, NIRAReply, or CapabilityResult requires a grounded evidence quote.");
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


    private async Task<NIRABranchApplyResult>
        CreateBranchAsync(
            NIRAMindEvent mindEvent,
            NIRABranchProposal proposal,
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


        NIRAGoalState? goal =
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

        if (goal.Status == NIRAGoalStatus.Blocked)
            return Reject("The parent goal is blocked. A new grounded user request requires a fresh execution goal, not the previous failure ledger.");

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


        NIRABranchState? duplicate;


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
            return new NIRABranchApplyResult
            {
                Action =
                    NIRABranchApplyAction.Duplicate,

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


        NIRABranchStatus status;


        string? waitingFor =
            proposal.WaitingFor;


        if (!dependenciesComplete)
        {
            status =
                NIRABranchStatus.Waiting;


            waitingFor ??=
                "Waiting for prerequisite branches to complete.";
        }
        else if (!string.IsNullOrWhiteSpace(
                     proposal.WaitingFor))
        {
            status =
                NIRABranchStatus.Waiting;
        }
        else
        {
            status =
                NIRABranchStatus.Active;
        }


        NIRABranchEvidenceRecord evidence =
            BuildEvidence(
                Guid.Empty,
                goalId,
                mindEvent,
                proposal);


        NIRABranchState branch =
            new NIRABranchState
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
                    ?? NIRABranchJoinPolicy.Required,

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


        return new NIRABranchApplyResult
        {
            Action =
                NIRABranchApplyAction.Created,

            Branch =
                branch,

            Reason =
                "Persistent branch created from grounded cognition evidence."
        };
    }


    private async Task<NIRABranchApplyResult>
        RecordProgressAsync(
            NIRABranchState existing,
            NIRAMindEvent mindEvent,
            NIRABranchProposal proposal,
            CancellationToken cancellationToken)
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        NIRABranchEvidenceRecord evidence =
            BuildEvidence(
                existing.Id,
                existing.GoalId,
                mindEvent,
                proposal);


        NIRABranchState updated =
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


        return new NIRABranchApplyResult
        {
            Action =
                NIRABranchApplyAction.EvidenceRecorded,

            Branch =
                updated,

            Reason =
                "Branch progress evidence recorded."
        };
    }


    private async Task<NIRABranchApplyResult>
        ReviseBranchAsync(
            NIRABranchState existing,
            NIRAMindEvent mindEvent,
            NIRABranchProposal proposal,
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


        NIRABranchStatus status =
            existing.Status;


        string? waitingFor =
            existing.WaitingFor;


        if (
            status ==
                NIRABranchStatus.Active
            &&
            !dependenciesComplete)
        {
            status =
                NIRABranchStatus.Waiting;


            waitingFor =
                "Waiting for revised prerequisite branches to complete.";
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        NIRABranchEvidenceRecord evidence =
            BuildEvidence(
                existing.Id,
                existing.GoalId,
                mindEvent,
                proposal);


        NIRABranchState updated =
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


        return new NIRABranchApplyResult
        {
            Action =
                NIRABranchApplyAction.Revised,

            Branch =
                updated,

            Reason =
                "Branch plan/dependencies were revised using grounded evidence."
        };
    }


    private async Task<NIRABranchApplyResult>
        TransitionBranchAsync(
            NIRABranchState existing,
            NIRAMindEvent mindEvent,
            NIRABranchProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.Action ==
                NIRABranchProposalAction.Complete
            &&
            proposal.Confidence <
                MinimumCompletionConfidence)
        {
            return Reject(
                "Branch completion confidence is below the runtime threshold.");
        }


        NIRABranchStatus target =
            ResolveTargetStatus(
                proposal.Action);


        if (existing.Status ==
            target)
        {
            return new NIRABranchApplyResult
            {
                Action =
                    NIRABranchApplyAction.NoChange,

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
                NIRABranchStatus.Active
            &&
            !AreDependenciesComplete(
                existing))
        {
            return Reject(
                "The branch cannot activate while prerequisite branches remain incomplete.");
        }


        if (target ==
            NIRABranchStatus.Completed)
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
                NIRABranchStatus.Waiting
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
                NIRABranchStatus.Blocked
            &&
            string.IsNullOrWhiteSpace(
                proposal.Blocker))
        {
            return Reject(
                "Blocked requires a concrete blocker.");
        }


        if (
            target ==
                NIRABranchStatus.Failed
            &&
            string.IsNullOrWhiteSpace(
                proposal.FailureReason))
        {
            return Reject(
                "Failed requires a concrete failure reason.");
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        NIRABranchEvidenceRecord evidence =
            BuildEvidence(
                existing.Id,
                existing.GoalId,
                mindEvent,
                proposal);


        NIRABranchState updated =
            existing with
            {
                Status =
                    target,

                Priority =
                    proposal.Priority
                    ?? existing.Priority,

                WaitingFor =
                    target ==
                        NIRABranchStatus.Waiting
                        ? proposal.WaitingFor
                            ?? existing.WaitingFor
                        : null,

                Blocker =
                    target ==
                        NIRABranchStatus.Blocked
                        ? proposal.Blocker
                        : null,

                FailureReason =
                    target ==
                        NIRABranchStatus.Failed
                        ? proposal.FailureReason
                        : null,

                ResultSummary =
                    target ==
                        NIRABranchStatus.Completed
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
                        NIRABranchStatus.Completed
                        or NIRABranchStatus.Failed
                        or NIRABranchStatus.Cancelled
                        ? now
                        : null
            };


        await PersistUpdatedAsync(
            updated,
            evidence,
            cancellationToken);


        return new NIRABranchApplyResult
        {
            Action =
                NIRABranchApplyAction.Transitioned,

            Branch =
                updated,

            Reason =
                $"Branch transitioned {existing.Status}->{target}."
        };
    }


    private async Task PersistUpdatedAsync(
        NIRABranchState updated,
        NIRABranchEvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        NIRABranchState normalized =
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
                    out NIRABranchState? branch)
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
                        out NIRABranchState? dependency)
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
        NIRABranchState branch)
    {
        lock (_stateSync)
        {
            return AreDependenciesCompleteUnsafe(
                branch);
        }
    }


    private bool AreDependenciesCompleteUnsafe(
        NIRABranchState branch)
    {
        return branch.DependsOnBranchIds.All(
            id =>
                _branches.TryGetValue(
                    id,
                    out NIRABranchState? dependency)
                &&
                dependency.Status ==
                    NIRABranchStatus.Completed);
    }


    private bool IsBranchCompleted(
        Guid branchId)
    {
        lock (_stateSync)
        {
            return _branches.TryGetValue(
                       branchId,
                       out NIRABranchState? branch)
                   &&
                   branch.Status ==
                       NIRABranchStatus.Completed;
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
                        NIRABranchJoinPolicy.Required)
            .All(
                branch =>
                    branch.Status ==
                        NIRABranchStatus.Completed);
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
                        out NIRABranchState? dependency))
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


    private static NIRABranchStatus ResolveTargetStatus(
        NIRABranchProposalAction action)
    {
        return action switch
        {
            NIRABranchProposalAction.Activate =>
                NIRABranchStatus.Active,

            NIRABranchProposalAction.SetPending =>
                NIRABranchStatus.Pending,

            NIRABranchProposalAction.SetWaiting =>
                NIRABranchStatus.Waiting,

            NIRABranchProposalAction.SetBlocked =>
                NIRABranchStatus.Blocked,

            NIRABranchProposalAction.Complete =>
                NIRABranchStatus.Completed,

            NIRABranchProposalAction.Fail =>
                NIRABranchStatus.Failed,

            NIRABranchProposalAction.Cancel =>
                NIRABranchStatus.Cancelled,

            _ =>
                throw new InvalidOperationException(
                    $"Branch action {action} is not a lifecycle transition.")
        };
    }


    private static bool IsTransitionAllowed(
        NIRABranchStatus from,
        NIRABranchStatus to)
    {
        if (from is
            NIRABranchStatus.Completed
            or NIRABranchStatus.Failed
            or NIRABranchStatus.Cancelled)
        {
            return false;
        }


        return to is
            NIRABranchStatus.Pending
            or NIRABranchStatus.Active
            or NIRABranchStatus.Waiting
            or NIRABranchStatus.Blocked
            or NIRABranchStatus.Completed
            or NIRABranchStatus.Failed
            or NIRABranchStatus.Cancelled;
    }


    private static NIRABranchEvidenceRecord BuildEvidence(
        Guid branchId,
        Guid goalId,
        NIRAMindEvent mindEvent,
        NIRABranchProposal proposal)
    {
        return new NIRABranchEvidenceRecord
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
        NIRABranchProposal proposal)
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


    private static NIRABranchResultEvent BuildResultEvent(
        NIRABranchState branch)
    {
        return new NIRABranchResultEvent
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


    private static NIRABranchApplyResult Reject(
        string reason)
    {
        return new NIRABranchApplyResult
        {
            Action =
                NIRABranchApplyAction.Rejected,

            Reason =
                reason
        };
    }


    private static void LogResult(
        NIRABranchApplyResult result,
        NIRABranchProposal proposal)
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


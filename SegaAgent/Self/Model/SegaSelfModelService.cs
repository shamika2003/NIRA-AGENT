/*
 * filename: SegaSelfModelService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Hosting;

using SegaAgent.Mind;
using SegaAgent.Self.Preferences;
using SegaAgent.Temporal;

namespace SegaAgent.Self.Model;


// =============================================================
// AUTHORITATIVE SEGA SELF-MODEL
//
// Owns:
// - persistent self facts
// - current capability/limitation knowledge supplied by runtime
// - persistent commitments and their lifecycle
// - cognition context that also includes Sega's existing learned
//   self-preference subsystem
//
// The main model can propose commitment changes, but this service
// validates evidence and state transitions before persistence.
// =============================================================

public sealed class SegaSelfModelService
    : IHostedService
{
    private const double MinimumCreateConfidence =
        0.80;


    private const double MinimumTransitionConfidence =
        0.70;


    private readonly SegaSelfModelStore
        _store;


    private readonly SegaSelfPreferenceService
        _selfPreferences;


    private readonly SegaTemporalContextService
        _temporal;


    private readonly IReadOnlyList<ISegaSelfKnowledgeProvider>
        _knowledgeProviders;


    private readonly object
        _stateSync =
            new();


    private readonly SemaphoreSlim
        _mutationLock =
            new(
                1,
                1);


    private Dictionary<string, SegaSelfFactState>
        _facts =
            new(
                StringComparer.OrdinalIgnoreCase);


    private Dictionary<Guid, SegaCommitmentState>
        _commitments =
            new();


    // Read-only observers such as the desktop UI can refresh their
    // projection when authoritative commitment state changes.
    public event Action?
        StateChanged;


    public SegaSelfModelService(
        SegaSelfModelStore store,
        SegaSelfPreferenceService selfPreferences,
        SegaTemporalContextService temporal,
        IEnumerable<ISegaSelfKnowledgeProvider> knowledgeProviders)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));


        _selfPreferences =
            selfPreferences
            ?? throw new ArgumentNullException(
                nameof(selfPreferences));


        _temporal =
            temporal
            ?? throw new ArgumentNullException(
                nameof(temporal));


        _knowledgeProviders =
            (
                knowledgeProviders
                ?? throw new ArgumentNullException(
                    nameof(knowledgeProviders))
            )
            .ToArray();
    }


    // =========================================================
    // STARTUP
    // =========================================================

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(
            cancellationToken);


        await SynchronizeAuthoritativeProvidersAsync(
            cancellationToken);


        IReadOnlyList<SegaSelfFactState> facts =
            await _store.ReadFactsAsync(
                cancellationToken);


        IReadOnlyList<SegaCommitmentState> commitments =
            await _store.ReadCommitmentsAsync(
                cancellationToken);


        lock (_stateSync)
        {
            _facts =
                facts.ToDictionary(
                    fact =>
                        fact.Key,

                    fact =>
                        fact,

                    StringComparer.OrdinalIgnoreCase);


            _commitments =
                commitments.ToDictionary(
                    commitment =>
                        commitment.Id,

                    commitment =>
                        commitment);
        }


        Debug.WriteLine(
            $"[SelfModel] READY | " +
            $"Facts={facts.Count(f => f.Status == SegaSelfFactStatus.Active)} | " +
            $"Identity={facts.Count(f => f.Status == SegaSelfFactStatus.Active && f.Category == SegaSelfFactCategory.Identity)} | " +
            $"Capabilities={facts.Count(f => f.Status == SegaSelfFactStatus.Active && f.Category == SegaSelfFactCategory.Capability)} | " +
            $"Limitations={facts.Count(f => f.Status == SegaSelfFactStatus.Active && f.Category == SegaSelfFactCategory.Limitation)} | " +
            $"Responsibilities={facts.Count(f => f.Status == SegaSelfFactStatus.Active && f.Category == SegaSelfFactCategory.Responsibility)} | " +
            $"ActiveCommitments={commitments.Count(c => c.IsActive)} | " +
            $"TemporalActive={commitments.Count(c => c.IsActive && c.Temporal?.IsScheduled == true)} | " +
            $"ResolvedCommitments={commitments.Count(c => !c.IsActive)} | " +
            $"Database='{_store.DatabasePath}'");
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    // =========================================================
    // CURRENT STATE
    // =========================================================

    public IReadOnlyList<SegaSelfFactState>
        CurrentFacts
    {
        get
        {
            lock (_stateSync)
            {
                return _facts
                    .Values
                    .Where(
                        fact =>
                            fact.Status ==
                            SegaSelfFactStatus.Active)
                    .OrderBy(
                        fact =>
                            fact.Category)
                    .ThenBy(
                        fact =>
                            fact.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }


    public IReadOnlyList<SegaCommitmentState>
        CurrentCommitments
    {
        get
        {
            lock (_stateSync)
            {
                return _commitments
                    .Values
                    .OrderByDescending(
                        commitment =>
                            commitment.IsActive)
                    .ThenByDescending(
                        commitment =>
                            commitment.UpdatedAt)
                    .ToArray();
            }
        }
    }


    // =========================================================
    // COGNITION CONTEXT
    // =========================================================

    public string BuildCognitionContext()
    {
        SegaSelfFactState[] facts;

        SegaCommitmentState[] active;

        SegaCommitmentState[] recentResolved;


        lock (_stateSync)
        {
            facts =
                _facts
                    .Values
                    .Where(
                        fact =>
                            fact.Status ==
                            SegaSelfFactStatus.Active)
                    .OrderBy(
                        fact =>
                            fact.Category)
                    .ThenBy(
                        fact =>
                            fact.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();


            active =
                _commitments
                    .Values
                    .Where(
                        commitment =>
                            commitment.IsActive)
                    .OrderByDescending(
                        commitment =>
                            commitment.UpdatedAt)
                    .Take(
                        24)
                    .ToArray();


            recentResolved =
                _commitments
                    .Values
                    .Where(
                        commitment =>
                            !commitment.IsActive)
                    .OrderByDescending(
                        commitment =>
                            commitment.UpdatedAt)
                    .Take(
                        8)
                    .ToArray();
        }


        StringBuilder builder =
            new();


        builder.AppendLine(
            "SEGA AUTHORITATIVE SELF-MODEL");


        builder.AppendLine();


        AppendFacts(
            builder,
            "IDENTITY",
            facts,
            SegaSelfFactCategory.Identity);


        AppendFacts(
            builder,
            "CURRENT CAPABILITIES",
            facts,
            SegaSelfFactCategory.Capability);


        AppendFacts(
            builder,
            "CURRENT LIMITATIONS",
            facts,
            SegaSelfFactCategory.Limitation);


        AppendFacts(
            builder,
            "CURRENT RESPONSIBILITIES",
            facts,
            SegaSelfFactCategory.Responsibility);


        builder.AppendLine(
            "DEVELOPED SELF-PREFERENCES");


        builder.AppendLine(
            _selfPreferences.BuildCognitionContext());


        builder.AppendLine();


        builder.AppendLine(
            "ACTIVE COMMITMENTS");


        builder.AppendLine(
            "Commitments are obligations Sega has accepted. They are not proof that work is complete and they are not executable goals.");


        if (active.Length ==
            0)
        {
            builder.AppendLine(
                "- No active commitments.");
        }
        else
        {
            foreach (
                SegaCommitmentState commitment
                in active)
            {
                builder.AppendLine(
                    $"- id={commitment.Id:D} | " +
                    $"status={commitment.Status} | " +
                    $"summary={commitment.Summary} | " +
                    $"temporal={_temporal.DescribeForCognition(commitment.Temporal)}");
            }
        }


        builder.AppendLine();


        builder.AppendLine(
            "RECENTLY RESOLVED COMMITMENTS");


        if (recentResolved.Length ==
            0)
        {
            builder.AppendLine(
                "- No recently resolved commitments.");
        }
        else
        {
            foreach (
                SegaCommitmentState commitment
                in recentResolved)
            {
                builder.AppendLine(
                    $"- id={commitment.Id:D} | " +
                    $"status={commitment.Status} | " +
                    $"summary={commitment.Summary}");
            }
        }


        return builder
            .ToString()
            .Trim();
    }


    public string BuildFormationContext()
    {
        SegaSelfFactState[] facts;

        SegaCommitmentState[] commitments;


        lock (_stateSync)
        {
            facts =
                _facts
                    .Values
                    .Where(
                        fact =>
                            fact.Status ==
                            SegaSelfFactStatus.Active)
                    .OrderBy(
                        fact =>
                            fact.Category)
                    .ThenBy(
                        fact =>
                            fact.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();


            commitments =
                _commitments
                    .Values
                    .OrderByDescending(
                        commitment =>
                            commitment.IsActive)
                    .ThenByDescending(
                        commitment =>
                            commitment.UpdatedAt)
                    .Take(
                        60)
                    .ToArray();
        }


        StringBuilder builder =
            new();


        builder.AppendLine(
            "CURRENT AUTHORITATIVE SEGA SELF FACTS");


        builder.AppendLine();


        AppendFacts(
            builder,
            "IDENTITY",
            facts,
            SegaSelfFactCategory.Identity);


        AppendFacts(
            builder,
            "CURRENT CAPABILITIES",
            facts,
            SegaSelfFactCategory.Capability);


        AppendFacts(
            builder,
            "CURRENT LIMITATIONS",
            facts,
            SegaSelfFactCategory.Limitation);


        AppendFacts(
            builder,
            "CURRENT RESPONSIBILITIES",
            facts,
            SegaSelfFactCategory.Responsibility);


        builder.AppendLine(
            "COMMITMENT FORMATION DETAIL");


        builder.AppendLine(
            "Only use an exact listed commitment ID for a lifecycle transition. Do not invent IDs.");


        if (commitments.Length ==
            0)
        {
            builder.AppendLine(
                "- No commitments currently exist.");
        }
        else
        {
            foreach (
                SegaCommitmentState commitment
                in commitments)
            {
                builder.AppendLine(
                    $"- id={commitment.Id:D} | " +
                    $"status={commitment.Status} | " +
                    $"created={commitment.CreatedAt:O} | " +
                    $"updated={commitment.UpdatedAt:O} | " +
                    $"summary={commitment.Summary} | " +
                    $"temporal={_temporal.DescribeForCognition(commitment.Temporal)}");
            }
        }


        return builder
            .ToString()
            .Trim();
    }


    // =========================================================
    // COMMITMENT PROPOSAL APPLICATION
    // =========================================================

    public async Task<int> ApplyCommitmentProposalsAsync(
        SegaMindEvent mindEvent,
        string finalReply,
        IReadOnlyList<SegaCommitmentFormationProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        if (
            proposals ==
                null
            ||
            proposals.Count ==
                0)
        {
            return 0;
        }


        await _mutationLock.WaitAsync(
            cancellationToken);


        try
        {
            int applied =
                0;


            foreach (
                SegaCommitmentFormationProposal raw
                in proposals)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();


                SegaCommitmentFormationProposal proposal;


                try
                {
                    proposal =
                        raw.Normalize();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[Commitment] REJECTED | " +
                        $"Reason='Malformed proposal: {TrimLog(ex.Message)}'");


                    continue;
                }


                if (proposal.Action ==
                    SegaCommitmentProposalAction.CancelAllActive)
                {
                    int cancelled =
                        await CancelAllActiveCommitmentsAsync(
                            mindEvent,
                            proposal,
                            cancellationToken);

                    applied +=
                        cancelled;

                    Debug.WriteLine(
                        $"[Commitment] CANCELLED_ALL_ACTIVE | " +
                        $"Count={cancelled} | " +
                        $"Reason='{TrimLog(proposal.Reason)}'");

                    continue;
                }


                SegaCommitmentApplyResult result =
                    await ApplyCommitmentProposalCoreAsync(
                        mindEvent,
                        finalReply,
                        proposal,
                        cancellationToken);


                Debug.WriteLine(
                    $"[Commitment] {result.Action.ToString().ToUpperInvariant()} | " +
                    $"Id={result.Commitment?.Id.ToString("D") ?? "-"} | " +
                    $"Status={result.Commitment?.Status.ToString() ?? "-"} | " +
                    $"Summary='{TrimLog(result.Commitment?.Summary ?? proposal.Summary)}' | " +
                    $"Reason='{TrimLog(result.Reason)}'");


                if (result.Action is
                    SegaCommitmentApplyAction.Created
                    or SegaCommitmentApplyAction.Transitioned
                    or SegaCommitmentApplyAction.Rescheduled)
                {
                    applied++;
                }
            }


            return applied;
        }
        finally
        {
            _mutationLock.Release();
        }
    }


    // =========================================================
    // BULK ACTIVE-COMMITMENT CANCELLATION
    //
    // This is intentionally a first-class semantic lifecycle operation,
    // not UI deletion and not phrase matching. The formation model proposes
    // CancelAllActive from fresh user intent; this authoritative service
    // validates that evidence and transitions every currently active
    // Pending/Waiting/Blocked commitment while holding the mutation lock.
    // Resolved history remains persisted for continuity/audit, but because
    // cancelled commitments are terminal they disappear from ACTIVE UI and
    // cannot later be resumed as the same commitment.
    // =========================================================

    private async Task<int> CancelAllActiveCommitmentsAsync(
        SegaMindEvent mindEvent,
        SegaCommitmentFormationProposal proposal,
        CancellationToken cancellationToken)
    {
        if (proposal.Confidence <
            MinimumTransitionConfidence)
        {
            Debug.WriteLine(
                "[Commitment] CANCEL_ALL REJECTED | " +
                "Reason='Confidence below transition threshold.'");

            return 0;
        }

        if (proposal.EvidenceSource !=
            SegaCommitmentEvidenceSource.UserEvent)
        {
            Debug.WriteLine(
                "[Commitment] CANCEL_ALL REJECTED | " +
                "Reason='Bulk cancellation requires fresh user evidence.'");

            return 0;
        }

        if (string.IsNullOrWhiteSpace(
                proposal.EvidenceQuote)
            ||
            !ContainsEvidenceQuote(
                mindEvent.Content,
                proposal.EvidenceQuote))
        {
            Debug.WriteLine(
                "[Commitment] CANCEL_ALL REJECTED | " +
                "Reason='Evidence quote is not grounded in the fresh user event.'");

            return 0;
        }

        if (!string.IsNullOrWhiteSpace(
                proposal.CommitmentId))
        {
            Debug.WriteLine(
                "[Commitment] CANCEL_ALL REJECTED | " +
                "Reason='Bulk cancellation must not target one commitment ID.'");

            return 0;
        }

        SegaCommitmentState[] active;

        lock (_stateSync)
        {
            active =
                _commitments
                    .Values
                    .Where(
                        commitment =>
                            commitment.IsActive)
                    .OrderBy(
                        commitment =>
                            commitment.CreatedAt)
                    .ToArray();
        }

        if (active.Length ==
            0)
        {
            return 0;
        }

        int cancelled =
            0;

        foreach (
            SegaCommitmentState commitment
            in active)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            SegaCommitmentFormationProposal targeted =
                proposal with
                {
                    Action =
                        SegaCommitmentProposalAction.Cancel,

                    CommitmentId =
                        commitment.Id.ToString("D"),

                    Summary =
                        commitment.Summary
                };

            SegaCommitmentApplyResult result =
                await TransitionCommitmentAsync(
                    mindEvent,
                    targeted,
                    cancellationToken);

            Debug.WriteLine(
                $"[Commitment] BULK_{result.Action.ToString().ToUpperInvariant()} | " +
                $"Id={commitment.Id:D} | " +
                $"From={commitment.Status} | " +
                $"Summary='{TrimLog(commitment.Summary)}'");

            if (result.Action ==
                SegaCommitmentApplyAction.Transitioned)
            {
                cancelled++;
            }
        }

        return cancelled;
    }


    private async Task<SegaCommitmentApplyResult>
        ApplyCommitmentProposalCoreAsync(
            SegaMindEvent mindEvent,
            string finalReply,
            SegaCommitmentFormationProposal proposal,
            CancellationToken cancellationToken)
    {
        string evidenceText =
            proposal.EvidenceSource ==
                SegaCommitmentEvidenceSource.UserEvent
                ? mindEvent.Content
                : finalReply;


        if (string.IsNullOrWhiteSpace(
                proposal.EvidenceQuote))
        {
            return Reject(
                "A commitment proposal requires a grounded evidence quote.");
        }


        if (!ContainsEvidenceQuote(
                evidenceText,
                proposal.EvidenceQuote))
        {
            return Reject(
                $"Evidence quote is not present in the declared {proposal.EvidenceSource} source.");
        }


        if (proposal.Action ==
            SegaCommitmentProposalAction.Create)
        {
            return await CreateCommitmentAsync(
                mindEvent,
                proposal,
                cancellationToken);
        }


        if (proposal.Action ==
            SegaCommitmentProposalAction.Reschedule)
        {
            return await RescheduleCommitmentAsync(
                mindEvent,
                proposal,
                cancellationToken);
        }


        return await TransitionCommitmentAsync(
            mindEvent,
            proposal,
            cancellationToken);
    }


    private async Task<SegaCommitmentApplyResult>
        CreateCommitmentAsync(
            SegaMindEvent mindEvent,
            SegaCommitmentFormationProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.EvidenceSource !=
            SegaCommitmentEvidenceSource.SegaReply)
        {
            return Reject(
                "A new Sega commitment must be grounded in Sega's own final reply accepting the future obligation.");
        }


        if (proposal.Confidence <
            MinimumCreateConfidence)
        {
            return Reject(
                "Commitment creation confidence is below the runtime threshold.");
        }


        if (string.IsNullOrWhiteSpace(
                proposal.Summary))
        {
            return Reject(
                "A new commitment requires a concise summary.");
        }


        string fingerprint =
            ComputeFingerprint(
                proposal.Summary);


        SegaCommitmentState? duplicate;


        lock (_stateSync)
        {
            duplicate =
                _commitments
                    .Values
                    .FirstOrDefault(
                        commitment =>
                            commitment.IsActive
                            &&
                            string.Equals(
                                commitment.Fingerprint,
                                fingerprint,
                                StringComparison.OrdinalIgnoreCase));
        }


        if (duplicate !=
            null)
        {
            return new SegaCommitmentApplyResult
            {
                Action =
                    SegaCommitmentApplyAction.Duplicate,

                Commitment =
                    duplicate,

                Reason =
                    "An equivalent active commitment already exists."
            };
        }


        SegaTemporalScheduleState? temporal =
            null;


        if (proposal.Temporal != null &&
            proposal.Temporal.Mode != SegaTemporalTimingMode.None)
        {
            try
            {
                temporal =
                    _temporal.ResolveProposal(
                        proposal.Temporal,
                        mindEvent.Timestamp);
            }
            catch (Exception ex)
            {
                return Reject(
                    $"Temporal commitment schedule was invalid: {ex.Message}");
            }
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaCommitmentState commitment =
            new SegaCommitmentState
            {
                Id =
                    Guid.NewGuid(),

                Fingerprint =
                    fingerprint,

                Summary =
                    proposal.Summary,

                Status =
                    temporal == null
                        ? SegaCommitmentStatus.Pending
                        : SegaCommitmentStatus.Waiting,

                Temporal =
                    temporal,

                SourceEventId =
                    mindEvent.Id,

                SourceEventName =
                    mindEvent.Name,

                LastEvidenceQuote =
                    proposal.EvidenceQuote,

                LastReason =
                    proposal.Reason,

                CreatedAt =
                    now,

                UpdatedAt =
                    now
            }
            .Normalize();


        await _store.InsertCommitmentAsync(
            commitment,
            proposal.EvidenceSource,
            cancellationToken);


        lock (_stateSync)
        {
            _commitments[
                commitment.Id] =
                    commitment;
        }


        RaiseStateChanged();


        return new SegaCommitmentApplyResult
        {
            Action =
                SegaCommitmentApplyAction.Created,

            Commitment =
                commitment,

            Reason =
                temporal == null
                    ? "Sega explicitly accepted a future unresolved obligation."
                    : "Sega explicitly accepted a future unresolved obligation with an authoritative temporal wake schedule."
        };
    }


    private async Task<SegaCommitmentApplyResult>
        RescheduleCommitmentAsync(
            SegaMindEvent mindEvent,
            SegaCommitmentFormationProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.Confidence < MinimumTransitionConfidence)
        {
            return Reject(
                "Commitment reschedule confidence is below the runtime threshold.");
        }

        if (string.IsNullOrWhiteSpace(proposal.CommitmentId) ||
            !Guid.TryParse(proposal.CommitmentId, out Guid commitmentId))
        {
            return Reject(
                "A commitment reschedule requires an exact existing commitment GUID.");
        }

        if (proposal.Temporal == null ||
            proposal.Temporal.Mode == SegaTemporalTimingMode.None)
        {
            return Reject(
                "A commitment reschedule requires a concrete temporal schedule.");
        }

        SegaCommitmentState? existing;

        lock (_stateSync)
        {
            _commitments.TryGetValue(commitmentId, out existing);
        }

        if (existing == null)
        {
            return Reject(
                "The commitment selected for rescheduling does not exist.");
        }

        if (!existing.IsActive)
        {
            return new SegaCommitmentApplyResult
            {
                Action = SegaCommitmentApplyAction.NoChange,
                Commitment = existing,
                Reason = "Resolved commitments are terminal and cannot be rescheduled."
            };
        }

        SegaTemporalScheduleState temporal;

        try
        {
            temporal = _temporal.ResolveProposal(
                proposal.Temporal,
                mindEvent.Timestamp,
                existing.Temporal);
        }
        catch (Exception ex)
        {
            return Reject(
                $"Temporal commitment reschedule was invalid: {ex.Message}");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        SegaCommitmentState updated = existing with
        {
            Status = SegaCommitmentStatus.Waiting,
            Temporal = temporal,
            LastEvidenceQuote = proposal.EvidenceQuote,
            LastReason = proposal.Reason,
            UpdatedAt = now,
            ResolvedAt = null
        };

        await _store.UpdateCommitmentTemporalAsync(
            existing,
            updated,
            proposal.EvidenceSource,
            mindEvent.Id,
            "Reschedule",
            recordTemporalHistory: true,
            cancellationToken: cancellationToken);

        lock (_stateSync)
        {
            _commitments[updated.Id] = updated;
        }

        RaiseStateChanged();

        return new SegaCommitmentApplyResult
        {
            Action = SegaCommitmentApplyAction.Rescheduled,
            Commitment = updated,
            Reason = "Commitment temporal schedule was revised using grounded evidence."
        };
    }


    private async Task<SegaCommitmentApplyResult>
        TransitionCommitmentAsync(
            SegaMindEvent mindEvent,
            SegaCommitmentFormationProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.Confidence <
            MinimumTransitionConfidence)
        {
            return Reject(
                "Commitment transition confidence is below the runtime threshold.");
        }


        if (
            string.IsNullOrWhiteSpace(
                proposal.CommitmentId)
            ||
            !Guid.TryParse(
                proposal.CommitmentId,
                out Guid commitmentId))
        {
            return Reject(
                "A commitment lifecycle transition requires an exact existing commitment GUID.");
        }


        SegaCommitmentState? existing;


        lock (_stateSync)
        {
            _commitments.TryGetValue(
                commitmentId,
                out existing);
        }


        if (existing ==
            null)
        {
            return Reject(
                "The proposed commitment ID does not exist in authoritative self-state.");
        }


        if (!existing.IsActive)
        {
            return new SegaCommitmentApplyResult
            {
                Action =
                    SegaCommitmentApplyAction.NoChange,

                Commitment =
                    existing,

                Reason =
                    "Resolved commitments are terminal and cannot be transitioned again."
            };
        }


        if (proposal.Action == SegaCommitmentProposalAction.Complete &&
            existing.Temporal?.IsRecurring == true &&
            string.Equals(
                mindEvent.Name,
                "TemporalCommitmentDue",
                StringComparison.Ordinal))
        {
            return new SegaCommitmentApplyResult
            {
                Action = SegaCommitmentApplyAction.NoChange,
                Commitment = existing,
                Reason = "One delivered occurrence does not complete a recurring commitment."
            };
        }


        SegaCommitmentStatus target =
            ResolveTargetStatus(
                proposal.Action);


        if (existing.Status ==
            target)
        {
            return new SegaCommitmentApplyResult
            {
                Action =
                    SegaCommitmentApplyAction.NoChange,

                Commitment =
                    existing,

                Reason =
                    "The commitment is already in the requested state."
            };
        }


        if (!IsTransitionAllowed(
                existing.Status,
                target))
        {
            return Reject(
                $"Transition {existing.Status}->{target} is not allowed.");
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        SegaCommitmentState updated =
            existing with
            {
                Status =
                    target,

                LastEvidenceQuote =
                    proposal.EvidenceQuote,

                LastReason =
                    proposal.Reason,

                UpdatedAt =
                    now,

                ResolvedAt =
                    target is
                        SegaCommitmentStatus.Completed
                        or SegaCommitmentStatus.Cancelled
                        ? now
                        : null
            };


        await _store.UpdateCommitmentStatusAsync(
            existing,
            updated,
            proposal.EvidenceSource,
            mindEvent.Id,
            cancellationToken);


        lock (_stateSync)
        {
            _commitments[
                updated.Id] =
                    updated;
        }


        RaiseStateChanged();


        return new SegaCommitmentApplyResult
        {
            Action =
                SegaCommitmentApplyAction.Transitioned,

            Commitment =
                updated,

            Reason =
                $"Commitment transitioned {existing.Status}->{target}."
        };
    }


    // =========================================================
    // TEMPORAL RECONCILIATION
    //
    // Repairs an older active commitment that clearly already contained
    // time semantics but predates the temporal scheduler. This never creates
    // a new obligation; it only operationalizes timing already grounded in
    // the stored commitment text/evidence.
    // =========================================================

    public async Task<bool> ReconcileTemporalScheduleAsync(
        Guid commitmentId,
        SegaTemporalScheduleProposal proposal,
        string evidenceQuote,
        string reason,
        DateTimeOffset referenceTimestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            SegaCommitmentState? current;

            lock (_stateSync)
            {
                _commitments.TryGetValue(commitmentId, out current);
            }

            if (current == null || !current.IsActive || current.Temporal != null)
            {
                return false;
            }

            string source = string.Join(
                Environment.NewLine,
                new[]
                {
                    current.Summary,
                    current.LastEvidenceQuote ?? string.Empty
                });

            if (string.IsNullOrWhiteSpace(evidenceQuote) ||
                !ContainsEvidenceQuote(source, evidenceQuote))
            {
                Debug.WriteLine(
                    $"[TemporalCommitments] RECONCILIATION REJECTED | " +
                    $"Id={commitmentId:D} | Reason='Temporal evidence quote was not grounded in stored commitment text.'");
                return false;
            }

            SegaTemporalScheduleState temporal;

            try
            {
                temporal = _temporal.ResolveProposal(
                    proposal,
                    referenceTimestamp,
                    previous: null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[TemporalCommitments] RECONCILIATION REJECTED | " +
                    $"Id={commitmentId:D} | Reason='{TrimLog(ex.Message)}'");
                return false;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            SegaCommitmentState updated = current with
            {
                Status = SegaCommitmentStatus.Waiting,
                Temporal = temporal,
                LastReason = string.IsNullOrWhiteSpace(reason)
                    ? current.LastReason
                    : reason.Trim(),
                UpdatedAt = now,
                ResolvedAt = null
            };

            await _store.UpdateCommitmentTemporalAsync(
                current,
                updated,
                evidenceSource: null,
                sourceEventId: current.SourceEventId,
                changeKind: "Reconciliation",
                recordTemporalHistory: true,
                cancellationToken: cancellationToken);

            lock (_stateSync)
            {
                _commitments[updated.Id] = updated;
            }

            RaiseStateChanged();
            return true;
        }
        finally
        {
            _mutationLock.Release();
        }
    }


    // =========================================================
    // TEMPORAL WAKE OWNERSHIP
    //
    // The scheduler never decides dialogue. It atomically claims due
    // commitments so duplicate wake events cannot race, then Sega's normal
    // cognition receives the event. Rearm is guarded by schedule revision
    // and wake count so a concurrent user reschedule cannot be overwritten
    // by a stale wake completing later.
    // =========================================================

    public async Task<IReadOnlyList<SegaCommitmentState>>
        ClaimDueTemporalCommitmentsAsync(
            DateTimeOffset nowUtc,
            TimeSpan wakeLease,
            int maximumCommitments = 4,
            bool allowFlexibleWake = true,
            CancellationToken cancellationToken = default)
    {
        maximumCommitments = Math.Clamp(maximumCommitments, 1, 12);
        wakeLease = wakeLease < TimeSpan.FromSeconds(30)
            ? TimeSpan.FromSeconds(30)
            : wakeLease > TimeSpan.FromMinutes(30)
                ? TimeSpan.FromMinutes(30)
                : wakeLease;

        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            SegaCommitmentState[] due;

            lock (_stateSync)
            {
                due = _commitments.Values
                    .Where(value =>
                        value.IsActive &&
                        value.Temporal?.IsDue(nowUtc) == true &&
                        (allowFlexibleWake ||
                         value.Temporal.Mode == SegaTemporalTimingMode.Exact))
                    .OrderBy(value => value.Temporal!.NextWakeAtUtc)
                    .ThenBy(value => value.CreatedAt)
                    .Take(maximumCommitments)
                    .ToArray();
            }

            if (due.Length == 0)
            {
                return Array.Empty<SegaCommitmentState>();
            }

            List<SegaCommitmentState> claimed = new(due.Length);

            foreach (SegaCommitmentState previous in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (previous.Temporal == null)
                {
                    continue;
                }

                SegaTemporalScheduleState temporal =
                    _temporal.ClaimWake(
                        previous.Temporal,
                        nowUtc,
                        wakeLease);

                SegaCommitmentState updated = previous with
                {
                    Temporal = temporal,
                    UpdatedAt = nowUtc.ToUniversalTime()
                };

                await _store.UpdateCommitmentTemporalAsync(
                    previous,
                    updated,
                    evidenceSource: null,
                    sourceEventId: null,
                    changeKind: "SchedulerClaim",
                    recordTemporalHistory: false,
                    cancellationToken: cancellationToken);

                lock (_stateSync)
                {
                    _commitments[updated.Id] = updated;
                }

                claimed.Add(updated);
            }

            if (claimed.Count > 0)
            {
                RaiseStateChanged();
            }

            return claimed;
        }
        finally
        {
            _mutationLock.Release();
        }
    }


    public async Task<bool> RearmTemporalCommitmentAfterWakeAsync(
        Guid commitmentId,
        int expectedRevision,
        int expectedWakeCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            SegaCommitmentState? current;

            lock (_stateSync)
            {
                _commitments.TryGetValue(commitmentId, out current);
            }

            if (current == null ||
                !current.IsActive ||
                current.Temporal == null)
            {
                return false;
            }

            if (current.Temporal.Revision != expectedRevision ||
                current.Temporal.WakeCount != expectedWakeCount)
            {
                Debug.WriteLine(
                    $"[TemporalCommitments] STALE REARM SKIPPED | " +
                    $"Id={commitmentId:D} | " +
                    $"ExpectedRevision={expectedRevision} | " +
                    $"CurrentRevision={current.Temporal.Revision} | " +
                    $"ExpectedWakeCount={expectedWakeCount} | " +
                    $"CurrentWakeCount={current.Temporal.WakeCount}");

                return false;
            }

            SegaTemporalScheduleState temporal =
                _temporal.RearmAfterWake(
                    current.Temporal,
                    nowUtc);

            SegaCommitmentState updated = current with
            {
                Temporal = temporal,
                UpdatedAt = nowUtc.ToUniversalTime()
            };

            await _store.UpdateCommitmentTemporalAsync(
                current,
                updated,
                evidenceSource: null,
                sourceEventId: null,
                changeKind: temporal.IsRecurring
                    ? "RecurringAdvance"
                    : "SchedulerRearm",
                recordTemporalHistory: false,
                cancellationToken: cancellationToken);

            lock (_stateSync)
            {
                _commitments[updated.Id] = updated;
            }

            RaiseStateChanged();
            return true;
        }
        finally
        {
            _mutationLock.Release();
        }
    }


    private void RaiseStateChanged()
    {
        Action? handler =
            StateChanged;

        if (handler == null)
        {
            return;
        }

        try
        {
            handler();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[SelfModel] STATE OBSERVER ERROR | {ex.Message}");
        }
    }


    // =========================================================
    // AUTHORITATIVE PROVIDER SYNCHRONIZATION
    // =========================================================

    private async Task SynchronizeAuthoritativeProvidersAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SegaSelfFactState> existing =
            await _store.ReadFactsAsync(
                cancellationToken);


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        foreach (
            ISegaSelfKnowledgeProvider provider
            in _knowledgeProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();


            string providerId =
                NormalizeProviderId(
                    provider.ProviderId);


            SegaSelfFactSeed[] seeds =
                provider
                    .GetFacts()
                    .Select(
                        seed =>
                            seed.Normalize())
                    .GroupBy(
                        seed =>
                            seed.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(
                        group =>
                            group.Last())
                    .ToArray();


            HashSet<string> activeKeys =
                seeds
                    .Select(
                        seed =>
                            seed.Key)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);


            foreach (
                SegaSelfFactSeed seed
                in seeds)
            {
                SegaSelfFactState? old =
                    existing.FirstOrDefault(
                        fact =>
                            string.Equals(
                                fact.Key,
                                seed.Key,
                                StringComparison.OrdinalIgnoreCase));


                await _store.UpsertFactAsync(
                    new SegaSelfFactState
                    {
                        Key =
                            seed.Key,

                        Category =
                            seed.Category,

                        Statement =
                            seed.Statement,

                        Authority =
                            SegaSelfFactAuthority.Runtime,

                        SourceReference =
                            providerId,

                        Status =
                            SegaSelfFactStatus.Active,

                        CreatedAt =
                            old?.CreatedAt
                            ?? now,

                        UpdatedAt =
                            now
                    },
                    cancellationToken);
            }


            foreach (
                SegaSelfFactState stale
                in existing.Where(
                    fact =>
                        fact.Status ==
                            SegaSelfFactStatus.Active
                        &&
                        fact.Authority ==
                            SegaSelfFactAuthority.Runtime
                        &&
                        string.Equals(
                            fact.SourceReference,
                            providerId,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        !activeKeys.Contains(
                            fact.Key)))
            {
                await _store.RetireFactAsync(
                    stale.Key,
                    now,
                    cancellationToken);
            }
        }
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private static void AppendFacts(
        StringBuilder builder,
        string heading,
        IReadOnlyList<SegaSelfFactState> facts,
        SegaSelfFactCategory category)
    {
        builder.AppendLine(
            heading);


        SegaSelfFactState[] matches =
            facts
                .Where(
                    fact =>
                        fact.Category ==
                        category)
                .ToArray();


        if (matches.Length ==
            0)
        {
            builder.AppendLine(
                "- None currently represented.");
        }
        else
        {
            foreach (
                SegaSelfFactState fact
                in matches)
            {
                builder.AppendLine(
                    $"- {fact.Statement}");
            }
        }


        builder.AppendLine();
    }


    private static SegaCommitmentStatus ResolveTargetStatus(
        SegaCommitmentProposalAction action)
    {
        return action switch
        {
            SegaCommitmentProposalAction.SetPending =>
                SegaCommitmentStatus.Pending,

            SegaCommitmentProposalAction.SetWaiting =>
                SegaCommitmentStatus.Waiting,

            SegaCommitmentProposalAction.SetBlocked =>
                SegaCommitmentStatus.Blocked,

            SegaCommitmentProposalAction.Complete =>
                SegaCommitmentStatus.Completed,

            SegaCommitmentProposalAction.Cancel =>
                SegaCommitmentStatus.Cancelled,

            _ =>
                throw new InvalidOperationException(
                    $"{action} is not a commitment transition action.")
        };
    }


    private static bool IsTransitionAllowed(
        SegaCommitmentStatus from,
        SegaCommitmentStatus to)
    {
        if (from is
            SegaCommitmentStatus.Completed
            or SegaCommitmentStatus.Cancelled)
        {
            return false;
        }


        return to switch
        {
            SegaCommitmentStatus.Pending =>
                from is
                    SegaCommitmentStatus.Waiting
                    or SegaCommitmentStatus.Blocked,

            SegaCommitmentStatus.Waiting =>
                from is
                    SegaCommitmentStatus.Pending
                    or SegaCommitmentStatus.Blocked,

            SegaCommitmentStatus.Blocked =>
                from is
                    SegaCommitmentStatus.Pending
                    or SegaCommitmentStatus.Waiting,

            SegaCommitmentStatus.Completed =>
                true,

            SegaCommitmentStatus.Cancelled =>
                true,

            _ =>
                false
        };
    }


    private static SegaCommitmentApplyResult Reject(
        string reason)
    {
        return new SegaCommitmentApplyResult
        {
            Action =
                SegaCommitmentApplyAction.Rejected,

            Reason =
                reason
        };
    }


    private static bool ContainsEvidenceQuote(
        string source,
        string quote)
    {
        if (
            string.IsNullOrWhiteSpace(
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


        return normalizedQuote.Length >=
                4
            &&
            normalizedSource.Contains(
                normalizedQuote,
                StringComparison.OrdinalIgnoreCase);
    }


    private static string NormalizeEvidenceText(
        string value)
    {
        return string.Join(
            ' ',
            value
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));
    }


    private static string ComputeFingerprint(
        string summary)
    {
        string normalized =
            NormalizeEvidenceText(
                summary)
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


    private static string NormalizeProviderId(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                "Sega self-knowledge provider requires an ID.");
        }


        string normalized =
            value.Trim()
                .ToLowerInvariant();


        if (normalized.Length >
            120)
        {
            throw new InvalidOperationException(
                "Sega self-knowledge provider ID is too long.");
        }


        return normalized;
    }


    private static string TrimLog(
        string value)
    {
        const int maximumLength =
            180;


        string clean =
            NormalizeEvidenceText(
                value ?? string.Empty);


        return clean.Length <=
            maximumLength
            ? clean
            : clean[..maximumLength] +
                "...";
    }
}
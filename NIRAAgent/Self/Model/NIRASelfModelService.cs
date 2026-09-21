/*
 * filename: NIRASelfModelService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Hosting;

using NIRAAgent.Mind;
using NIRAAgent.Self.Preferences;
using NIRAAgent.Temporal;

namespace NIRAAgent.Self.Model;


// =============================================================
// AUTHORITATIVE NIRA SELF-MODEL
//
// Owns:
// - persistent self facts
// - current capability/limitation knowledge supplied by runtime
// - persistent commitments and their lifecycle
// - cognition context that also includes NIRA's existing learned
//   self-preference subsystem
//
// The main model can propose commitment changes, but this service
// validates evidence and state transitions before persistence.
// =============================================================

public sealed class NIRASelfModelService
    : IHostedService
{
    private const double MinimumCreateConfidence =
        0.80;


    private const double MinimumTransitionConfidence =
        0.70;


    private readonly NIRASelfModelStore
        _store;


    private readonly NIRASelfPreferenceService
        _selfPreferences;


    private readonly NIRATemporalContextService
        _temporal;


    private readonly IReadOnlyList<INIRASelfKnowledgeProvider>
        _knowledgeProviders;


    private readonly object
        _stateSync =
            new();


    private readonly SemaphoreSlim
        _mutationLock =
            new(
                1,
                1);


    private Dictionary<string, NIRASelfFactState>
        _facts =
            new(
                StringComparer.OrdinalIgnoreCase);


    private Dictionary<Guid, NIRACommitmentState>
        _commitments =
            new();


    // Read-only observers such as the desktop UI can refresh their
    // projection when authoritative commitment state changes.
    public event Action?
        StateChanged;


    public NIRASelfModelService(
        NIRASelfModelStore store,
        NIRASelfPreferenceService selfPreferences,
        NIRATemporalContextService temporal,
        IEnumerable<INIRASelfKnowledgeProvider> knowledgeProviders)
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


        IReadOnlyList<NIRASelfFactState> facts =
            await _store.ReadFactsAsync(
                cancellationToken);


        IReadOnlyList<NIRACommitmentState> commitments =
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
            $"Facts={facts.Count(f => f.Status == NIRASelfFactStatus.Active)} | " +
            $"Identity={facts.Count(f => f.Status == NIRASelfFactStatus.Active && f.Category == NIRASelfFactCategory.Identity)} | " +
            $"Capabilities={facts.Count(f => f.Status == NIRASelfFactStatus.Active && f.Category == NIRASelfFactCategory.Capability)} | " +
            $"Limitations={facts.Count(f => f.Status == NIRASelfFactStatus.Active && f.Category == NIRASelfFactCategory.Limitation)} | " +
            $"Responsibilities={facts.Count(f => f.Status == NIRASelfFactStatus.Active && f.Category == NIRASelfFactCategory.Responsibility)} | " +
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

    public IReadOnlyList<NIRASelfFactState>
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
                            NIRASelfFactStatus.Active)
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


    public IReadOnlyList<NIRACommitmentState>
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
        NIRASelfFactState[] facts;

        NIRACommitmentState[] active;

        NIRACommitmentState[] recentResolved;


        lock (_stateSync)
        {
            facts =
                _facts
                    .Values
                    .Where(
                        fact =>
                            fact.Status ==
                            NIRASelfFactStatus.Active)
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
            "NIRA AUTHORITATIVE SELF-MODEL");


        builder.AppendLine();


        AppendFacts(
            builder,
            "IDENTITY",
            facts,
            NIRASelfFactCategory.Identity);


        AppendFacts(
            builder,
            "CURRENT CAPABILITIES",
            facts,
            NIRASelfFactCategory.Capability);


        AppendFacts(
            builder,
            "CURRENT LIMITATIONS",
            facts,
            NIRASelfFactCategory.Limitation);


        AppendFacts(
            builder,
            "CURRENT RESPONSIBILITIES",
            facts,
            NIRASelfFactCategory.Responsibility);


        builder.AppendLine(
            "DEVELOPED SELF-PREFERENCES");


        builder.AppendLine(
            _selfPreferences.BuildCognitionContext());


        builder.AppendLine();


        builder.AppendLine(
            "ACTIVE COMMITMENTS");


        builder.AppendLine(
            "Commitments are obligations NIRA has accepted. They are not proof that work is complete and they are not executable goals.");


        if (active.Length ==
            0)
        {
            builder.AppendLine(
                "- No active commitments.");
        }
        else
        {
            foreach (
                NIRACommitmentState commitment
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
                NIRACommitmentState commitment
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
        NIRASelfFactState[] facts;

        NIRACommitmentState[] commitments;


        lock (_stateSync)
        {
            facts =
                _facts
                    .Values
                    .Where(
                        fact =>
                            fact.Status ==
                            NIRASelfFactStatus.Active)
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
            "CURRENT AUTHORITATIVE NIRA SELF FACTS");


        builder.AppendLine();


        AppendFacts(
            builder,
            "IDENTITY",
            facts,
            NIRASelfFactCategory.Identity);


        AppendFacts(
            builder,
            "CURRENT CAPABILITIES",
            facts,
            NIRASelfFactCategory.Capability);


        AppendFacts(
            builder,
            "CURRENT LIMITATIONS",
            facts,
            NIRASelfFactCategory.Limitation);


        AppendFacts(
            builder,
            "CURRENT RESPONSIBILITIES",
            facts,
            NIRASelfFactCategory.Responsibility);


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
                NIRACommitmentState commitment
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
        NIRAMindEvent mindEvent,
        string finalReply,
        IReadOnlyList<NIRACommitmentFormationProposal> proposals,
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
                NIRACommitmentFormationProposal raw
                in proposals)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();


                NIRACommitmentFormationProposal proposal;


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
                    NIRACommitmentProposalAction.CancelAllActive)
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


                NIRACommitmentApplyResult result =
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
                    NIRACommitmentApplyAction.Created
                    or NIRACommitmentApplyAction.Transitioned
                    or NIRACommitmentApplyAction.Rescheduled)
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
        NIRAMindEvent mindEvent,
        NIRACommitmentFormationProposal proposal,
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
            NIRACommitmentEvidenceSource.UserEvent)
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

        NIRACommitmentState[] active;

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
            NIRACommitmentState commitment
            in active)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            NIRACommitmentFormationProposal targeted =
                proposal with
                {
                    Action =
                        NIRACommitmentProposalAction.Cancel,

                    CommitmentId =
                        commitment.Id.ToString("D"),

                    Summary =
                        commitment.Summary
                };

            NIRACommitmentApplyResult result =
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
                NIRACommitmentApplyAction.Transitioned)
            {
                cancelled++;
            }
        }

        return cancelled;
    }


    private async Task<NIRACommitmentApplyResult>
        ApplyCommitmentProposalCoreAsync(
            NIRAMindEvent mindEvent,
            string finalReply,
            NIRACommitmentFormationProposal proposal,
            CancellationToken cancellationToken)
    {
        string evidenceText =
            proposal.EvidenceSource ==
                NIRACommitmentEvidenceSource.UserEvent
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
            NIRACommitmentProposalAction.Create)
        {
            return await CreateCommitmentAsync(
                mindEvent,
                proposal,
                cancellationToken);
        }


        if (proposal.Action ==
            NIRACommitmentProposalAction.Reschedule)
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


    private async Task<NIRACommitmentApplyResult>
        CreateCommitmentAsync(
            NIRAMindEvent mindEvent,
            NIRACommitmentFormationProposal proposal,
            CancellationToken cancellationToken)
    {
        if (proposal.EvidenceSource !=
            NIRACommitmentEvidenceSource.NIRAReply)
        {
            return Reject(
                "A new NIRA commitment must be grounded in NIRA's own final reply accepting the future obligation.");
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


        NIRACommitmentState? duplicate;


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
            return new NIRACommitmentApplyResult
            {
                Action =
                    NIRACommitmentApplyAction.Duplicate,

                Commitment =
                    duplicate,

                Reason =
                    "An equivalent active commitment already exists."
            };
        }


        NIRATemporalScheduleState? temporal =
            null;


        if (proposal.Temporal != null &&
            proposal.Temporal.Mode != NIRATemporalTimingMode.None)
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


        NIRACommitmentState commitment =
            new NIRACommitmentState
            {
                Id =
                    Guid.NewGuid(),

                Fingerprint =
                    fingerprint,

                Summary =
                    proposal.Summary,

                Status =
                    temporal == null
                        ? NIRACommitmentStatus.Pending
                        : NIRACommitmentStatus.Waiting,

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


        return new NIRACommitmentApplyResult
        {
            Action =
                NIRACommitmentApplyAction.Created,

            Commitment =
                commitment,

            Reason =
                temporal == null
                    ? "NIRA explicitly accepted a future unresolved obligation."
                    : "NIRA explicitly accepted a future unresolved obligation with an authoritative temporal wake schedule."
        };
    }


    private async Task<NIRACommitmentApplyResult>
        RescheduleCommitmentAsync(
            NIRAMindEvent mindEvent,
            NIRACommitmentFormationProposal proposal,
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
            proposal.Temporal.Mode == NIRATemporalTimingMode.None)
        {
            return Reject(
                "A commitment reschedule requires a concrete temporal schedule.");
        }

        NIRACommitmentState? existing;

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
            return new NIRACommitmentApplyResult
            {
                Action = NIRACommitmentApplyAction.NoChange,
                Commitment = existing,
                Reason = "Resolved commitments are terminal and cannot be rescheduled."
            };
        }

        NIRATemporalScheduleState temporal;

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

        NIRACommitmentState updated = existing with
        {
            Status = NIRACommitmentStatus.Waiting,
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

        return new NIRACommitmentApplyResult
        {
            Action = NIRACommitmentApplyAction.Rescheduled,
            Commitment = updated,
            Reason = "Commitment temporal schedule was revised using grounded evidence."
        };
    }


    private async Task<NIRACommitmentApplyResult>
        TransitionCommitmentAsync(
            NIRAMindEvent mindEvent,
            NIRACommitmentFormationProposal proposal,
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


        NIRACommitmentState? existing;


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
            return new NIRACommitmentApplyResult
            {
                Action =
                    NIRACommitmentApplyAction.NoChange,

                Commitment =
                    existing,

                Reason =
                    "Resolved commitments are terminal and cannot be transitioned again."
            };
        }


        if (proposal.Action == NIRACommitmentProposalAction.Complete &&
            existing.Temporal?.IsRecurring == true &&
            string.Equals(
                mindEvent.Name,
                "TemporalCommitmentDue",
                StringComparison.Ordinal))
        {
            return new NIRACommitmentApplyResult
            {
                Action = NIRACommitmentApplyAction.NoChange,
                Commitment = existing,
                Reason = "One delivered occurrence does not complete a recurring commitment."
            };
        }


        NIRACommitmentStatus target =
            ResolveTargetStatus(
                proposal.Action);


        if (existing.Status ==
            target)
        {
            return new NIRACommitmentApplyResult
            {
                Action =
                    NIRACommitmentApplyAction.NoChange,

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


        NIRACommitmentState updated =
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
                        NIRACommitmentStatus.Completed
                        or NIRACommitmentStatus.Cancelled
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


        return new NIRACommitmentApplyResult
        {
            Action =
                NIRACommitmentApplyAction.Transitioned,

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
        NIRATemporalScheduleProposal proposal,
        string evidenceQuote,
        string reason,
        DateTimeOffset referenceTimestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        await _mutationLock.WaitAsync(cancellationToken);

        try
        {
            NIRACommitmentState? current;

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

            NIRATemporalScheduleState temporal;

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

            NIRACommitmentState updated = current with
            {
                Status = NIRACommitmentStatus.Waiting,
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
    // commitments so duplicate wake events cannot race, then NIRA's normal
    // cognition receives the event. Rearm is guarded by schedule revision
    // and wake count so a concurrent user reschedule cannot be overwritten
    // by a stale wake completing later.
    // =========================================================

    public async Task<IReadOnlyList<NIRACommitmentState>>
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
            NIRACommitmentState[] due;

            lock (_stateSync)
            {
                due = _commitments.Values
                    .Where(value =>
                        value.IsActive &&
                        value.Temporal?.IsDue(nowUtc) == true &&
                        (allowFlexibleWake ||
                         value.Temporal.Mode == NIRATemporalTimingMode.Exact))
                    .OrderBy(value => value.Temporal!.NextWakeAtUtc)
                    .ThenBy(value => value.CreatedAt)
                    .Take(maximumCommitments)
                    .ToArray();
            }

            if (due.Length == 0)
            {
                return Array.Empty<NIRACommitmentState>();
            }

            List<NIRACommitmentState> claimed = new(due.Length);

            foreach (NIRACommitmentState previous in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (previous.Temporal == null)
                {
                    continue;
                }

                NIRATemporalScheduleState temporal =
                    _temporal.ClaimWake(
                        previous.Temporal,
                        nowUtc,
                        wakeLease);

                NIRACommitmentState updated = previous with
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
            NIRACommitmentState? current;

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

            NIRATemporalScheduleState temporal =
                _temporal.RearmAfterWake(
                    current.Temporal,
                    nowUtc);

            NIRACommitmentState updated = current with
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
        IReadOnlyList<NIRASelfFactState> existing =
            await _store.ReadFactsAsync(
                cancellationToken);


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        foreach (
            INIRASelfKnowledgeProvider provider
            in _knowledgeProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();


            string providerId =
                NormalizeProviderId(
                    provider.ProviderId);


            NIRASelfFactSeed[] seeds =
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
                NIRASelfFactSeed seed
                in seeds)
            {
                NIRASelfFactState? old =
                    existing.FirstOrDefault(
                        fact =>
                            string.Equals(
                                fact.Key,
                                seed.Key,
                                StringComparison.OrdinalIgnoreCase));


                await _store.UpsertFactAsync(
                    new NIRASelfFactState
                    {
                        Key =
                            seed.Key,

                        Category =
                            seed.Category,

                        Statement =
                            seed.Statement,

                        Authority =
                            NIRASelfFactAuthority.Runtime,

                        SourceReference =
                            providerId,

                        Status =
                            NIRASelfFactStatus.Active,

                        CreatedAt =
                            old?.CreatedAt
                            ?? now,

                        UpdatedAt =
                            now
                    },
                    cancellationToken);
            }


            foreach (
                NIRASelfFactState stale
                in existing.Where(
                    fact =>
                        fact.Status ==
                            NIRASelfFactStatus.Active
                        &&
                        fact.Authority ==
                            NIRASelfFactAuthority.Runtime
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
        IReadOnlyList<NIRASelfFactState> facts,
        NIRASelfFactCategory category)
    {
        builder.AppendLine(
            heading);


        NIRASelfFactState[] matches =
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
                NIRASelfFactState fact
                in matches)
            {
                builder.AppendLine(
                    $"- {fact.Statement}");
            }
        }


        builder.AppendLine();
    }


    private static NIRACommitmentStatus ResolveTargetStatus(
        NIRACommitmentProposalAction action)
    {
        return action switch
        {
            NIRACommitmentProposalAction.SetPending =>
                NIRACommitmentStatus.Pending,

            NIRACommitmentProposalAction.SetWaiting =>
                NIRACommitmentStatus.Waiting,

            NIRACommitmentProposalAction.SetBlocked =>
                NIRACommitmentStatus.Blocked,

            NIRACommitmentProposalAction.Complete =>
                NIRACommitmentStatus.Completed,

            NIRACommitmentProposalAction.Cancel =>
                NIRACommitmentStatus.Cancelled,

            _ =>
                throw new InvalidOperationException(
                    $"{action} is not a commitment transition action.")
        };
    }


    private static bool IsTransitionAllowed(
        NIRACommitmentStatus from,
        NIRACommitmentStatus to)
    {
        if (from is
            NIRACommitmentStatus.Completed
            or NIRACommitmentStatus.Cancelled)
        {
            return false;
        }


        return to switch
        {
            NIRACommitmentStatus.Pending =>
                from is
                    NIRACommitmentStatus.Waiting
                    or NIRACommitmentStatus.Blocked,

            NIRACommitmentStatus.Waiting =>
                from is
                    NIRACommitmentStatus.Pending
                    or NIRACommitmentStatus.Blocked,

            NIRACommitmentStatus.Blocked =>
                from is
                    NIRACommitmentStatus.Pending
                    or NIRACommitmentStatus.Waiting,

            NIRACommitmentStatus.Completed =>
                true,

            NIRACommitmentStatus.Cancelled =>
                true,

            _ =>
                false
        };
    }


    private static NIRACommitmentApplyResult Reject(
        string reason)
    {
        return new NIRACommitmentApplyResult
        {
            Action =
                NIRACommitmentApplyAction.Rejected,

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
                "NIRA self-knowledge provider requires an ID.");
        }


        string normalized =
            value.Trim()
                .ToLowerInvariant();


        if (normalized.Length >
            120)
        {
            throw new InvalidOperationException(
                "NIRA self-knowledge provider ID is too long.");
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


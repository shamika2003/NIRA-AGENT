/*
 * filename: NIRASelfPreferenceService.cs
 */

using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.Hosting;

namespace NIRAAgent.Self.Preferences;

public sealed class NIRASelfPreferenceService
    : IHostedService
{
    private readonly NIRASelfPreferenceStore
        _store;


    private readonly object
        _stateSync =
            new();


    private readonly SemaphoreSlim
        _mutationLock =
            new(
                1,
                1);


    private Dictionary<string, NIRASelfPreferenceState>
        _preferences =
            new(
                StringComparer.OrdinalIgnoreCase);


    private Dictionary<string, NIRATemporaryOpinionState>
        _temporaryOpinions =
            new(
                StringComparer.OrdinalIgnoreCase);


    public NIRASelfPreferenceService(
        NIRASelfPreferenceStore store)
    {
        _store =
            store
            ?? throw new ArgumentNullException(
                nameof(store));
    }


    // =========================================================
    // START
    // =========================================================

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(
            cancellationToken);


        IReadOnlyList<NIRASelfPreferenceState> stored =
            await _store.ReadAllAsync(
                cancellationToken);


        IReadOnlyList<NIRATemporaryOpinionState> storedTemporary =
            await _store.ReadTemporaryOpinionsAsync(
                cancellationToken);


        lock (_stateSync)
        {
            _preferences =
                stored.ToDictionary(
                    preference =>
                        preference.Key,

                    preference =>
                        preference,

                    StringComparer.OrdinalIgnoreCase);


            _temporaryOpinions =
                storedTemporary.ToDictionary(
                    opinion =>
                        opinion.Key,

                    opinion =>
                        opinion,

                    StringComparer.OrdinalIgnoreCase);
        }


        int activeTemporary =
            storedTemporary.Count(
                opinion =>
                    opinion.ResolveSnapshot(
                            DateTimeOffset.UtcNow)
                        .IsActive);


        Debug.WriteLine(
            $"[SelfPreference] READY | " +
            $"Stored={stored.Count} | " +
            $"Established={stored.Count(p => p.Status == NIRASelfPreferenceStatus.Established)} | " +
            $"TemporaryStored={storedTemporary.Count} | " +
            $"TemporaryActive={activeTemporary} | " +
            $"Database='{_store.DatabasePath}'");
    }


    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    // =========================================================
    // CURRENT
    // =========================================================

    public IReadOnlyList<NIRASelfPreferenceState>
        Current
    {
        get
        {
            lock (_stateSync)
            {
                return _preferences
                    .Values
                    .OrderByDescending(
                        preference =>
                            preference.Status ==
                            NIRASelfPreferenceStatus.Established)
                    .ThenByDescending(
                        preference =>
                            Math.Abs(
                                preference.Affinity))
                    .ThenBy(
                        preference =>
                            preference.Subject,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
    }


    // =========================================================
    // CURRENT TEMPORARY OPINIONS
    //
    // Decay is resolved from timestamps at read time.
    // No timer is required to keep the values honest.
    // =========================================================

    public IReadOnlyList<NIRATemporaryOpinionSnapshot>
        CurrentTemporaryOpinions
    {
        get
        {
            DateTimeOffset now =
                DateTimeOffset.UtcNow;


            lock (_stateSync)
            {
                return _temporaryOpinions
                    .Values
                    .Select(
                        opinion =>
                            opinion.ResolveSnapshot(
                                now))
                    .Where(
                        opinion =>
                            opinion.IsActive)
                    .OrderByDescending(
                        opinion =>
                            Math.Abs(
                                opinion.Affinity))
                    .ThenByDescending(
                        opinion =>
                            opinion.Confidence)
                    .ToArray();
            }
        }
    }


    // =========================================================
    // APPLY EXPERIENCE OBSERVATION
    //
    // The semantic reasoner may propose an observation.
    // This deterministic service owns whether and how much NIRA's
    // actual developed self-state moves.
    // =========================================================

    public async Task<NIRASelfPreferenceApplyResult>
        ApplyObservationAsync(
            NIRASelfPreferenceObservation observation,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            observation);


        NIRASelfPreferenceObservation normalized;


        try
        {
            normalized =
                observation.Normalize();
        }
        catch (Exception ex)
        {
            return new NIRASelfPreferenceApplyResult
            {
                Action =
                    NIRASelfPreferenceApplyAction.Ignored,

                Reason =
                    $"Malformed observation: {ex.Message}"
            };
        }


        // =====================================================
        // MINIMUM EVIDENCE FLOOR
        //
        // These are not thresholds for becoming durable.
        // They merely prevent meaningless model noise from even
        // entering NIRA's preference evidence history.
        // =====================================================

        if (Math.Abs(
                normalized.Affinity) <
            0.10)
        {
            return Ignore(
                "Affinity is too weak to represent a meaningful preference observation.");
        }


        if (normalized.EvidenceStrength <
            0.20)
        {
            return Ignore(
                "Evidence strength is too weak to affect NIRA's developed preferences.");
        }


        if (normalized.Confidence <
            0.60)
        {
            return Ignore(
                "Observation confidence is too low to affect NIRA's developed preferences.");
        }


        await _mutationLock.WaitAsync(
            cancellationToken);


        try
        {
            NIRASelfPreferenceState? existing;

            NIRATemporaryOpinionState? existingTemporary;


            lock (_stateSync)
            {
                _preferences.TryGetValue(
                    normalized.Key,
                    out existing);


                _temporaryOpinions.TryGetValue(
                    normalized.Key,
                    out existingTemporary);
            }


            // =================================================
            // EVENT DE-DUPLICATION
            // =================================================

            if (normalized.SourceEventId.HasValue)
            {
                bool alreadyApplied =
                    await _store.ContainsEvidenceAsync(
                        normalized.Key,
                        normalized.SourceEventId.Value,
                        cancellationToken);


                if (alreadyApplied)
                {
                    Debug.WriteLine(
                        $"[SelfPreference] DUPLICATE | " +
                        $"Key='{normalized.Key}' | " +
                        $"Event={normalized.SourceEventId.Value}");


                    return new NIRASelfPreferenceApplyResult
                    {
                        Action =
                            NIRASelfPreferenceApplyAction.Duplicate,

                        Preference =
                            existing,

                        Reason =
                            "This completed event already contributed evidence to this preference."
                    };
                }
            }


            DateTimeOffset now =
                DateTimeOffset.UtcNow;


            bool contradiction =
                existing !=
                    null
                &&
                Math.Abs(
                    existing.Affinity) >=
                    0.15
                &&
                Math.Sign(
                    existing.Affinity) !=
                    Math.Sign(
                        normalized.Affinity);


            double reliability =
                normalized.EvidenceStrength
                *
                normalized.Confidence;


            // =================================================
            // LEARNING RATE
            //
            // Emerging preferences are allowed to form gradually.
            // Established parts of NIRA's developed self change
            // much more slowly.
            // =================================================

            double learningRate;


            if (existing ==
                null)
            {
                learningRate =
                    0.18
                    +
                    reliability *
                        0.22;
            }
            else if (
                existing.Status ==
                    NIRASelfPreferenceStatus.Established)
            {
                learningRate =
                    0.045
                    +
                    reliability *
                        0.075;
            }
            else
            {
                learningRate =
                    0.12
                    +
                    reliability *
                        0.16;
            }


            if (contradiction)
            {
                // Contradictory evidence should first weaken an
                // existing preference rather than instantly flip it.
                learningRate *=
                    0.55;
            }


            learningRate =
                Math.Clamp(
                    learningRate,
                    0.03,
                    0.40);


            double previousAffinity =
                existing?.Affinity
                ?? 0.0;


            double newAffinity =
                previousAffinity
                +
                (
                    normalized.Affinity
                    -
                    previousAffinity
                )
                *
                learningRate;


            newAffinity =
                Math.Clamp(
                    newAffinity,
                    -1.0,
                    1.0);


            int observationCount =
                (
                    existing?.ObservationCount
                    ?? 0
                )
                +
                1;


            int contradictionCount =
                (
                    existing?.ContradictionCount
                    ?? 0
                )
                +
                (
                    contradiction
                        ? 1
                        : 0
                );


            double previousConfidence =
                existing?.Confidence
                ?? normalized.Confidence;


            int confidenceHistoryWeight =
                Math.Min(
                    Math.Max(
                        1,
                        observationCount -
                            1),
                    8);


            double confidence =
                (
                    previousConfidence *
                        confidenceHistoryWeight
                    +
                    normalized.Confidence
                )
                /
                (
                    confidenceHistoryWeight +
                    1
                );


            NIRASelfPreferenceStatus status =
                ResolveStatus(
                    existing,
                    newAffinity,
                    confidence,
                    observationCount,
                    contradictionCount);


            NIRASelfPreferenceState updated =
                new NIRASelfPreferenceState
                {
                    Key =
                        normalized.Key,

                    Subject =
                        normalized.Subject,

                    TopicKey =
                        normalized.TopicKey
                        ?? existing?.TopicKey,

                    Affinity =
                        newAffinity,

                    Confidence =
                        confidence,

                    ObservationCount =
                        observationCount,

                    ContradictionCount =
                        contradictionCount,

                    Status =
                        status,

                    CreatedAt =
                        existing?.CreatedAt
                        ?? now,

                    UpdatedAt =
                        now
                }
                .Normalize();


            NIRATemporaryOpinionState temporaryOpinion =
                BuildTemporaryOpinion(
                    existingTemporary,
                    normalized,
                    now);


            await _store.UpsertAsync(
                updated,
                temporaryOpinion,
                normalized,
                cancellationToken);


            lock (_stateSync)
            {
                _preferences[
                    updated.Key] =
                        updated;


                _temporaryOpinions[
                    temporaryOpinion.Key] =
                        temporaryOpinion;
            }


            NIRASelfPreferenceApplyAction action =
                existing ==
                    null
                    ? NIRASelfPreferenceApplyAction.Created
                    : existing.Status !=
                            NIRASelfPreferenceStatus.Established
                        &&
                        updated.Status ==
                            NIRASelfPreferenceStatus.Established
                        ? NIRASelfPreferenceApplyAction.Established
                        : NIRASelfPreferenceApplyAction.Updated;


            Debug.WriteLine(
                $"[SelfPreference] {action.ToString().ToUpperInvariant()} | " +
                $"Key='{updated.Key}' | " +
                $"Subject='{updated.Subject}' | " +
                $"Affinity={previousAffinity:F3}->{updated.Affinity:F3} | " +
                $"Confidence={updated.Confidence:F3} | " +
                $"Evidence={normalized.EvidenceStrength:F3} | " +
                $"Observations={updated.ObservationCount} | " +
                $"Contradictions={updated.ContradictionCount} | " +
                $"Status={updated.Status} | " +
                $"Temporary={temporaryOpinion.BaseAffinity:F3} | " +
                $"TemporaryHalfLife={temporaryOpinion.HalfLifeHours:F1}h");


            return new NIRASelfPreferenceApplyResult
            {
                Action =
                    action,

                Preference =
                    updated,

                Reason =
                    contradiction
                        ? "Contradictory experience was applied conservatively."
                        : "Meaningful experience evidence was applied to NIRA's developed preference state."
            };
        }
        finally
        {
            _mutationLock.Release();
        }
    }


    // =========================================================
    // STATUS
    //
    // One event can never establish a durable preference.
    //
    // Three strong consistent observations can establish one.
    // Weaker evidence requires a larger body of experience.
    // Established preferences are intentionally sticky.
    // =========================================================

    private static NIRASelfPreferenceStatus ResolveStatus(
        NIRASelfPreferenceState? existing,
        double affinity,
        double confidence,
        int observations,
        int contradictions)
    {
        double magnitude =
            Math.Abs(
                affinity);


        if (
            existing?.Status ==
                NIRASelfPreferenceStatus.Established)
        {
            if (
                contradictions >=
                    3
                &&
                magnitude <
                    0.18)
            {
                return
                    NIRASelfPreferenceStatus.Emerging;
            }


            return
                NIRASelfPreferenceStatus.Established;
        }


        bool strongRepeatedEvidence =
            observations >=
                3
            &&
            magnitude >=
                0.40
            &&
            confidence >=
                0.75;


        bool accumulatedEvidence =
            observations >=
                5
            &&
            magnitude >=
                0.28
            &&
            confidence >=
                0.72;


        if (
            strongRepeatedEvidence
            ||
            accumulatedEvidence)
        {
            return
                NIRASelfPreferenceStatus.Established;
        }


        return
            NIRASelfPreferenceStatus.Emerging;
    }


    // =========================================================
    // MAIN COGNITION CONTEXT
    //
    // Only ESTABLISHED learned preferences enter NIRA's normal
    // self-context. Emerging evidence must not silently become
    // "who NIRA is" before enough experience exists.
    // =========================================================

    public string BuildCognitionContext()
    {
        NIRASelfPreferenceState[] established;

        NIRATemporaryOpinionSnapshot[] temporary;


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        lock (_stateSync)
        {
            established =
                _preferences
                    .Values
                    .Where(
                        preference =>
                            preference.Status ==
                            NIRASelfPreferenceStatus.Established)
                    .OrderByDescending(
                        preference =>
                            Math.Abs(
                                preference.Affinity))
                    .ThenByDescending(
                        preference =>
                            preference.Confidence)
                    .Take(
                        30)
                    .ToArray();


            temporary =
                _temporaryOpinions
                    .Values
                    .Select(
                        opinion =>
                            opinion.ResolveSnapshot(
                                now))
                    .Where(
                        opinion =>
                            opinion.IsActive)
                    .OrderByDescending(
                        opinion =>
                            Math.Abs(
                                opinion.Affinity))
                    .ThenByDescending(
                        opinion =>
                            opinion.Confidence)
                    .Take(
                        20)
                    .ToArray();
        }


        StringBuilder builder =
            new();


        builder.AppendLine(
            "NIRA DEVELOPED SELF-PREFERENCES");


        builder.AppendLine();


        builder.AppendLine(
            "Core identity preferences are supplied separately by NIRA's identity/personality policy.");


        builder.AppendLine();


        builder.AppendLine(
            "LEARNED DURABLE");


        if (established.Length ==
            0)
        {
            builder.AppendLine(
                "- No learned durable self-preferences have been established yet.");
        }
        else
        {
            foreach (
                NIRASelfPreferenceState preference
                in established)
            {
                builder.AppendLine(
                    $"- key={preference.Key} | " +
                    $"subject={preference.Subject} | " +
                    $"affinity={preference.Affinity:F2} | " +
                    $"confidence={preference.Confidence:F2} | " +
                    $"experience_count={preference.ObservationCount}");
            }
        }


        builder.AppendLine();


        builder.AppendLine(
            "CURRENT TEMPORARY OPINIONS / TASTES");


        builder.AppendLine(
            "These are recent experience-sensitive reactions. They may temporarily disagree with durable preferences and naturally decay toward neutral.");


        if (temporary.Length ==
            0)
        {
            builder.AppendLine(
                "- No temporary self-opinions are currently strong enough to matter.");
        }
        else
        {
            foreach (
                NIRATemporaryOpinionSnapshot opinion
                in temporary)
            {
                double ageHours =
                    Math.Max(
                        0.0,
                        (
                            now -
                            opinion.LastExperiencedAt
                        )
                        .TotalHours);


                builder.AppendLine(
                    $"- key={opinion.Key} | " +
                    $"subject={opinion.Subject} | " +
                    $"current_affinity={opinion.Affinity:F2} | " +
                    $"confidence={opinion.Confidence:F2} | " +
                    $"age_hours={ageHours:F1} | " +
                    $"half_life_hours={opinion.HalfLifeHours:F1}");
            }
        }


        return builder
            .ToString()
            .Trim();
    }


    // =========================================================
    // FORMATION CONTEXT
    //
    // The post-experience formation reasoner sees both emerging
    // and established state so it can reuse the same key and
    // recognize reinforcement/contradiction.
    //
    // This is context, NOT fresh evidence.
    // =========================================================

    public string BuildFormationContext()
    {
        NIRASelfPreferenceState[] current;

        NIRATemporaryOpinionSnapshot[] temporary;


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        lock (_stateSync)
        {
            current =
                _preferences
                    .Values
                    .OrderByDescending(
                        preference =>
                            preference.Status ==
                            NIRASelfPreferenceStatus.Established)
                    .ThenByDescending(
                        preference =>
                            preference.UpdatedAt)
                    .Take(
                        60)
                    .ToArray();


            temporary =
                _temporaryOpinions
                    .Values
                    .Select(
                        opinion =>
                            opinion.ResolveSnapshot(
                                now))
                    .Where(
                        opinion =>
                            opinion.IsActive)
                    .OrderByDescending(
                        opinion =>
                            Math.Abs(
                                opinion.Affinity))
                    .Take(
                        30)
                    .ToArray();
        }


        if (
            current.Length ==
                0
            &&
            temporary.Length ==
                0)
        {
            return
                "No developed NIRA self-preference evidence or temporary opinions exist yet.";
        }


        StringBuilder builder =
            new();


        builder.AppendLine(
            "CURRENT DEVELOPED SELF-PREFERENCE STATE");


        builder.AppendLine(
            "Reuse an existing key when fresh experience concerns the same subject.");


        if (current.Length ==
            0)
        {
            builder.AppendLine(
                "- No emerging or established durable preference state.");
        }
        else
        {
            foreach (
                NIRASelfPreferenceState preference
                in current)
            {
                builder.AppendLine(
                    $"- key={preference.Key} | " +
                    $"subject={preference.Subject} | " +
                    $"topic={preference.TopicKey ?? "-"} | " +
                    $"affinity={preference.Affinity:F2} | " +
                    $"confidence={preference.Confidence:F2} | " +
                    $"observations={preference.ObservationCount} | " +
                    $"contradictions={preference.ContradictionCount} | " +
                    $"status={preference.Status}");
            }
        }


        builder.AppendLine();


        builder.AppendLine(
            "CURRENT TEMPORARY OPINIONS");


        builder.AppendLine(
            "These are context only, not fresh evidence. Do not reinforce a preference merely because a temporary opinion already exists.");


        if (temporary.Length ==
            0)
        {
            builder.AppendLine(
                "- No active temporary opinions.");
        }
        else
        {
            foreach (
                NIRATemporaryOpinionSnapshot opinion
                in temporary)
            {
                builder.AppendLine(
                    $"- key={opinion.Key} | " +
                    $"subject={opinion.Subject} | " +
                    $"current_affinity={opinion.Affinity:F2} | " +
                    $"confidence={opinion.Confidence:F2}");
            }
        }


        return builder
            .ToString()
            .Trim();
    }


    // =========================================================
    // TEMPORARY OPINION UPDATE
    //
    // Temporary state reacts much faster than durable preference
    // state. Before applying fresh experience, the old temporary
    // opinion is first resolved through time decay.
    // =========================================================

    private static NIRATemporaryOpinionState BuildTemporaryOpinion(
        NIRATemporaryOpinionState? existing,
        NIRASelfPreferenceObservation observation,
        DateTimeOffset now)
    {
        double previousAffinity =
            0.0;

        double previousConfidence =
            observation.Confidence;

        int previousExperienceCount =
            0;

        DateTimeOffset createdAt =
            now;


        if (existing !=
            null)
        {
            NIRATemporaryOpinionSnapshot current =
                existing.ResolveSnapshot(
                    now);


            previousAffinity =
                current.Affinity;


            previousConfidence =
                current.Confidence;


            previousExperienceCount =
                existing.ExperienceCount;


            createdAt =
                existing.CreatedAt;
        }


        double reliability =
            observation.EvidenceStrength
            *
            observation.Confidence;


        // Immediate opinions should react quickly to experience.
        double blend =
            Math.Clamp(
                0.50
                +
                reliability *
                    0.35,
                0.50,
                0.85);


        double affinity =
            previousAffinity
            +
            (
                observation.Affinity
                -
                previousAffinity
            )
            *
            blend;


        affinity =
            Math.Clamp(
                affinity,
                -1.0,
                1.0);


        double confidence =
            Math.Clamp(
                previousConfidence *
                    0.35
                +
                observation.Confidence *
                    0.65,
                0.0,
                1.0);


        // Strong meaningful experiences remain emotionally/currently
        // relevant longer, but temporary opinions never become
        // permanent merely through elapsed time.
        double halfLifeHours =
            Math.Clamp(
                6.0
                +
                reliability *
                    30.0,
                6.0,
                36.0);


        if (existing !=
            null)
        {
            bool sameDirection =
                Math.Abs(
                    previousAffinity) <
                    0.05
                ||
                Math.Sign(
                    previousAffinity) ==
                    Math.Sign(
                        observation.Affinity);


            if (sameDirection)
            {
                halfLifeHours =
                    Math.Clamp(
                        Math.Max(
                            existing.HalfLifeHours,
                            halfLifeHours)
                        +
                        Math.Min(
                            12.0,
                            previousExperienceCount *
                                1.5),
                        6.0,
                        48.0);
            }
        }


        return new NIRATemporaryOpinionState
        {
            Key =
                observation.Key,

            Subject =
                observation.Subject,

            TopicKey =
                observation.TopicKey
                ?? existing?.TopicKey,

            BaseAffinity =
                affinity,

            Confidence =
                confidence,

            HalfLifeHours =
                halfLifeHours,

            ExperienceCount =
                previousExperienceCount +
                1,

            CreatedAt =
                createdAt,

            LastExperiencedAt =
                now
        }
        .Normalize();
    }


    private static NIRASelfPreferenceApplyResult Ignore(
        string reason)
    {
        return new NIRASelfPreferenceApplyResult
        {
            Action =
                NIRASelfPreferenceApplyAction.Ignored,

            Reason =
                reason
        };
    }
}


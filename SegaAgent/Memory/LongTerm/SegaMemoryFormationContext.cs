/*
 * filename: SegaMemoryFormationContext.cs
 */

using SegaAgent.Mind;
using SegaAgent.Self.Model;

namespace SegaAgent.Memory.LongTerm;


public enum SegaMemoryEvidenceBasis
{
    UserExplicit,

    SegaInference,

    SharedExperience,

    SystemDerived
}


// =============================================================
// DURABLE MEMORY PROPOSAL
// =============================================================

public sealed record SegaMemoryFormationProposal
{
    public SegaMemoryEvidenceBasis EvidenceBasis
    {
        get;
        init;
    }


    public SegaMemoryKind Kind
    {
        get;
        init;
    }


    public string Content
    {
        get;
        init;
    } =
        string.Empty;


    public string? CanonicalKey
    {
        get;
        init;
    }


    public string? TopicKey
    {
        get;
        init;
    }


    public double Importance
    {
        get;
        init;
    } =
        0.50;


    public double Confidence
    {
        get;
        init;
    } =
        0.50;


    public double EmotionalWeight
    {
        get;
        init;
    }


    public SegaMemoryAssociationProfile Association
    {
        get;
        init;
    } =
        new();


    public SegaMemoryFormationProposal Normalize()
    {
        SegaMemoryCandidate normalized =
            ToCandidate()
                .Normalize();


        return this with
        {
            Content =
                normalized.Content,

            CanonicalKey =
                normalized.CanonicalKey,

            TopicKey =
                normalized.TopicKey,

            Importance =
                normalized.Importance,

            Confidence =
                normalized.Confidence,

            EmotionalWeight =
                normalized.EmotionalWeight,

            Association =
                normalized.Association
        };
    }


    public SegaMemoryCandidate ToCandidate()
    {
        return new SegaMemoryCandidate
        {
            Kind =
                Kind,

            Content =
                Content,

            CanonicalKey =
                CanonicalKey,

            TopicKey =
                TopicKey,

            Importance =
                Importance,

            Confidence =
                Confidence,

            EmotionalWeight =
                EmotionalWeight,

            Association =
                Association
        };
    }
}


// =============================================================
// SEGA SELF-PREFERENCE FORMATION PROPOSAL
//
// This is NOT a durable memory write.
//
// The same unified post-experience reasoning pass may notice that
// a fresh experience is evidence about Sega's own developing
// preference. C# later converts this into an observation and the
// authoritative self-preference service decides how much, if at
// all, Sega's current developed self-state changes.
// =============================================================

public sealed record SegaSelfPreferenceFormationProposal
{
    public SegaMemoryEvidenceBasis EvidenceBasis
    {
        get;
        init;
    }


    public string Key
    {
        get;
        init;
    } =
        string.Empty;


    public string Subject
    {
        get;
        init;
    } =
        string.Empty;


    public string? TopicKey
    {
        get;
        init;
    }


    public double Affinity
    {
        get;
        init;
    }


    public double EvidenceStrength
    {
        get;
        init;
    }


    public double Confidence
    {
        get;
        init;
    }


    public string? EvidenceSummary
    {
        get;
        init;
    }


    public SegaSelfPreferenceFormationProposal Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Key))
        {
            throw new InvalidOperationException(
                "Self-preference formation proposal requires a key.");
        }


        if (string.IsNullOrWhiteSpace(
                Subject))
        {
            throw new InvalidOperationException(
                "Self-preference formation proposal requires a subject.");
        }


        return this with
        {
            Key =
                Key.Trim()
                    .ToLowerInvariant(),

            Subject =
                Subject.Trim(),

            TopicKey =
                string.IsNullOrWhiteSpace(
                        TopicKey)
                    ? null
                    : TopicKey.Trim()
                        .ToLowerInvariant(),

            Affinity =
                Math.Clamp(
                    Affinity,
                    -1.0,
                    1.0),

            EvidenceStrength =
                Math.Clamp(
                    EvidenceStrength,
                    0.0,
                    1.0),

            Confidence =
                Math.Clamp(
                    Confidence,
                    0.0,
                    1.0),

            EvidenceSummary =
                string.IsNullOrWhiteSpace(
                        EvidenceSummary)
                    ? null
                    : EvidenceSummary.Trim()
        };
    }
}


// =============================================================
// FORMATION CONTEXT
// =============================================================

public sealed record SegaMemoryFormationContext
{
    public Guid RunId
    {
        get;
        init;
    }


    public SegaMindEvent Event
    {
        get;
        init;
    } =
        null!;


    public string FinalDecisionSummary
    {
        get;
        init;
    } =
        string.Empty;


    public string FinalReply
    {
        get;
        init;
    } =
        string.Empty;


    public string CharacterContext
    {
        get;
        init;
    } =
        string.Empty;


    public string SelfPreferenceContext
    {
        get;
        init;
    } =
        string.Empty;


    public string SelfModelContext
    {
        get;
        init;
    } =
        string.Empty;


    public string TemporalContext
    {
        get;
        init;
    } =
        string.Empty;


    public string ConversationContext
    {
        get;
        init;
    } =
        string.Empty;


    public string LongTermMemoryContext
    {
        get;
        init;
    } =
        string.Empty;


    public string MemorySearchEvidence
    {
        get;
        init;
    } =
        string.Empty;
}


// =============================================================
// FORMATION RESULT
// =============================================================

public sealed record SegaMemoryFormationResult
{
    public string Summary
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyList<SegaMemoryFormationProposal> Proposals
    {
        get;
        init;
    } =
        Array.Empty<SegaMemoryFormationProposal>();


    public IReadOnlyList<SegaSelfPreferenceFormationProposal>
        SelfPreferenceObservations
    {
        get;
        init;
    } =
        Array.Empty<SegaSelfPreferenceFormationProposal>();


    public IReadOnlyList<SegaCommitmentFormationProposal>
        CommitmentProposals
    {
        get;
        init;
    } =
        Array.Empty<SegaCommitmentFormationProposal>();
}
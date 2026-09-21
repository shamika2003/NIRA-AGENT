/*
 * filename: NIRAMemoryFormationContext.cs
 */

using NIRAAgent.Mind;
using NIRAAgent.Self.Model;
using NIRAAgent.Skills;

namespace NIRAAgent.Memory.LongTerm;


public enum NIRAMemoryEvidenceBasis
{
    UserExplicit,

    NIRAInference,

    SharedExperience,

    SystemDerived
}


// =============================================================
// DURABLE MEMORY PROPOSAL
// =============================================================

public sealed record NIRAMemoryFormationProposal
{
    public NIRAMemoryEvidenceBasis EvidenceBasis
    {
        get;
        init;
    }


    public NIRAMemoryKind Kind
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


    public NIRAMemoryAssociationProfile Association
    {
        get;
        init;
    } =
        new();


    public NIRAMemoryFormationProposal Normalize()
    {
        NIRAMemoryCandidate normalized =
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


    public NIRAMemoryCandidate ToCandidate()
    {
        return new NIRAMemoryCandidate
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
// NIRA SELF-PREFERENCE FORMATION PROPOSAL
//
// This is NOT a durable memory write.
//
// The same unified post-experience reasoning pass may notice that
// a fresh experience is evidence about NIRA's own developing
// preference. C# later converts this into an observation and the
// authoritative self-preference service decides how much, if at
// all, NIRA's current developed self-state changes.
// =============================================================

public sealed record NIRASelfPreferenceFormationProposal
{
    public NIRAMemoryEvidenceBasis EvidenceBasis
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


    public NIRASelfPreferenceFormationProposal Normalize()
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

public sealed record NIRAMemoryFormationContext
{
    public Guid RunId
    {
        get;
        init;
    }


    public NIRAMindEvent Event
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


    public string DynamicToolContext
    {
        get;
        init;
    } =
        string.Empty;


    public string DynamicToolEvidence
    {
        get;
        init;
    } =
        string.Empty;
}


// =============================================================
// FORMATION RESULT
// =============================================================

public sealed record NIRAMemoryFormationResult
{
    public string Summary
    {
        get;
        init;
    } =
        string.Empty;


    public IReadOnlyList<NIRAMemoryFormationProposal> Proposals
    {
        get;
        init;
    } =
        Array.Empty<NIRAMemoryFormationProposal>();


    public IReadOnlyList<NIRASelfPreferenceFormationProposal>
        SelfPreferenceObservations
    {
        get;
        init;
    } =
        Array.Empty<NIRASelfPreferenceFormationProposal>();


    public IReadOnlyList<NIRACommitmentFormationProposal>
        CommitmentProposals
    {
        get;
        init;
    } =
        Array.Empty<NIRACommitmentFormationProposal>();


    public IReadOnlyList<NIRALearnedSkillFormationProposal>
        LearnedSkillProposals
    {
        get;
        init;
    } =
        Array.Empty<NIRALearnedSkillFormationProposal>();
}


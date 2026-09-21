/*
 * filename: NIRASelfModelContracts.cs
 */

using NIRAAgent.Temporal;

namespace NIRAAgent.Self.Model;


// =============================================================
// SELF FACT CATEGORY
// =============================================================

public enum NIRASelfFactCategory
{
    Identity,

    Capability,

    Limitation,

    Responsibility
}


// =============================================================
// SELF FACT AUTHORITY
//
// Runtime facts are supplied by authoritative application code.
// UserAccepted / SystemDerived are reserved for later validated
// self-development paths. The main model never writes these
// records directly.
// =============================================================

public enum NIRASelfFactAuthority
{
    Runtime,

    UserAccepted,

    SystemDerived
}


public enum NIRASelfFactStatus
{
    Active,

    Retired
}


public sealed record NIRASelfFactState
{
    public string Key
    {
        get;
        init;
    } =
        string.Empty;


    public NIRASelfFactCategory Category
    {
        get;
        init;
    }


    public string Statement
    {
        get;
        init;
    } =
        string.Empty;


    public NIRASelfFactAuthority Authority
    {
        get;
        init;
    }


    public string? SourceReference
    {
        get;
        init;
    }


    public NIRASelfFactStatus Status
    {
        get;
        init;
    } =
        NIRASelfFactStatus.Active;


    public DateTimeOffset CreatedAt
    {
        get;
        init;
    }


    public DateTimeOffset UpdatedAt
    {
        get;
        init;
    }


    public DateTimeOffset? RetiredAt
    {
        get;
        init;
    }


    public NIRASelfFactState Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Key))
        {
            throw new InvalidOperationException(
                "NIRA self fact requires a key.");
        }


        if (string.IsNullOrWhiteSpace(
                Statement))
        {
            throw new InvalidOperationException(
                "NIRA self fact requires a statement.");
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        return this with
        {
            Key =
                NormalizeKey(
                    Key),

            Statement =
                Statement.Trim(),

            SourceReference =
                string.IsNullOrWhiteSpace(
                        SourceReference)
                    ? null
                    : SourceReference.Trim(),

            CreatedAt =
                CreatedAt == default
                    ? now
                    : CreatedAt,

            UpdatedAt =
                UpdatedAt == default
                    ? now
                    : UpdatedAt
        };
    }


    internal static string NormalizeKey(
        string value)
    {
        string normalized =
            value
                .Trim()
                .ToLowerInvariant();


        if (normalized.Length >
            160)
        {
            throw new InvalidOperationException(
                "NIRA self fact key is too long.");
        }


        return normalized;
    }
}


// =============================================================
// AUTHORITATIVE SELF-KNOWLEDGE PROVIDER
//
// Future capability systems can contribute their own provider.
// NIRASelfModelService then synchronizes the provider facts into
// persistent self-state without hard-coding all future tools into
// the self-model store itself.
// =============================================================

public sealed record NIRASelfFactSeed
{
    public string Key
    {
        get;
        init;
    } =
        string.Empty;


    public NIRASelfFactCategory Category
    {
        get;
        init;
    }


    public string Statement
    {
        get;
        init;
    } =
        string.Empty;


    public NIRASelfFactSeed Normalize()
    {
        if (string.IsNullOrWhiteSpace(
                Statement))
        {
            throw new InvalidOperationException(
                "NIRA self fact seed requires a statement.");
        }


        return this with
        {
            Key =
                NIRASelfFactState.NormalizeKey(
                    Key),

            Statement =
                Statement.Trim()
        };
    }
}


public interface INIRASelfKnowledgeProvider
{
    string ProviderId
    {
        get;
    }


    IReadOnlyList<NIRASelfFactSeed>
        GetFacts();
}


// =============================================================
// COMMITMENTS
//
// A commitment is an obligation NIRA has actually accepted.
// It is not a long-term-memory record and it is not an executable
// goal. Stage 7 goals may later be created to satisfy commitments.
// =============================================================

public enum NIRACommitmentStatus
{
    Pending,

    Waiting,

    Blocked,

    Completed,

    Cancelled
}


public enum NIRACommitmentProposalAction
{
    Create,

    SetPending,

    SetWaiting,

    SetBlocked,

    Complete,

    Cancel,

    // Change the authoritative temporal schedule of an existing active
    // commitment without inventing a replacement obligation.
    Reschedule,

    // Semantic bulk lifecycle action. The model may propose this when
    // the user explicitly asks NIRA to clear/remove/cancel every active
    // commitment. Runtime still validates fresh user evidence before
    // transitioning Pending, Waiting and Blocked commitments.
    CancelAllActive
}


public enum NIRACommitmentEvidenceSource
{
    UserEvent,

    NIRAReply
}


public sealed record NIRACommitmentState
{
    public Guid Id
    {
        get;
        init;
    }


    public string Fingerprint
    {
        get;
        init;
    } =
        string.Empty;


    public string Summary
    {
        get;
        init;
    } =
        string.Empty;


    public NIRACommitmentStatus Status
    {
        get;
        init;
    } =
        NIRACommitmentStatus.Pending;


    // Optional authoritative temporal condition. A commitment may exist
    // without one; when present, the scheduler can wake NIRA even when
    // the user sends no new message.
    public NIRATemporalScheduleState? Temporal
    {
        get;
        init;
    }


    public Guid? SourceEventId
    {
        get;
        init;
    }


    public string? SourceEventName
    {
        get;
        init;
    }


    public string? LastEvidenceQuote
    {
        get;
        init;
    }


    public string? LastReason
    {
        get;
        init;
    }


    public DateTimeOffset CreatedAt
    {
        get;
        init;
    }


    public DateTimeOffset UpdatedAt
    {
        get;
        init;
    }


    public DateTimeOffset? ResolvedAt
    {
        get;
        init;
    }


    public bool IsActive =>
        Status is
            NIRACommitmentStatus.Pending
            or NIRACommitmentStatus.Waiting
            or NIRACommitmentStatus.Blocked;


    public NIRACommitmentState Normalize()
    {
        if (Id ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "NIRA commitment requires an ID.");
        }


        if (string.IsNullOrWhiteSpace(
                Fingerprint))
        {
            throw new InvalidOperationException(
                "NIRA commitment requires a fingerprint.");
        }


        if (string.IsNullOrWhiteSpace(
                Summary))
        {
            throw new InvalidOperationException(
                "NIRA commitment requires a summary.");
        }


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        return this with
        {
            Fingerprint =
                Fingerprint.Trim()
                    .ToLowerInvariant(),

            Summary =
                Summary.Trim(),

            Temporal =
                Temporal?.Normalize(),

            SourceEventName =
                string.IsNullOrWhiteSpace(
                        SourceEventName)
                    ? null
                    : SourceEventName.Trim(),

            LastEvidenceQuote =
                string.IsNullOrWhiteSpace(
                        LastEvidenceQuote)
                    ? null
                    : LastEvidenceQuote.Trim(),

            LastReason =
                string.IsNullOrWhiteSpace(
                        LastReason)
                    ? null
                    : LastReason.Trim(),

            CreatedAt =
                CreatedAt == default
                    ? now
                    : CreatedAt,

            UpdatedAt =
                UpdatedAt == default
                    ? now
                    : UpdatedAt
        };
    }
}


// =============================================================
// MODEL PROPOSAL
//
// This is only a semantic proposal from the post-experience
// reasoner. NIRASelfModelService validates IDs, evidence quotes,
// confidence, allowed transitions and duplicate state before any
// persistent change is committed.
// =============================================================

public sealed record NIRACommitmentFormationProposal
{
    public NIRACommitmentProposalAction Action
    {
        get;
        init;
    }


    public string? CommitmentId
    {
        get;
        init;
    }


    public string Summary
    {
        get;
        init;
    } =
        string.Empty;


    // Optional semantic time proposal. Create may attach one to a new
    // obligation; Reschedule requires one for an existing obligation.
    public NIRATemporalScheduleProposal? Temporal
    {
        get;
        init;
    }


    public NIRACommitmentEvidenceSource EvidenceSource
    {
        get;
        init;
    }


    public string EvidenceQuote
    {
        get;
        init;
    } =
        string.Empty;


    public string Reason
    {
        get;
        init;
    } =
        string.Empty;


    public double Confidence
    {
        get;
        init;
    }


    public NIRACommitmentFormationProposal Normalize()
    {
        string summary =
            Summary?.Trim()
            ?? string.Empty;


        string evidence =
            EvidenceQuote?.Trim()
            ?? string.Empty;


        string reason =
            Reason?.Trim()
            ?? string.Empty;


        if (summary.Length >
            360)
        {
            throw new InvalidOperationException(
                "Commitment summary is too long.");
        }


        if (evidence.Length >
            420)
        {
            throw new InvalidOperationException(
                "Commitment evidence quote is too long.");
        }


        if (reason.Length >
            600)
        {
            throw new InvalidOperationException(
                "Commitment proposal reason is too long.");
        }


        string? commitmentId =
            string.IsNullOrWhiteSpace(
                    CommitmentId)
                ? null
                : CommitmentId.Trim();


        if (commitmentId?.Length >
            80)
        {
            throw new InvalidOperationException(
                "Commitment ID proposal is too long.");
        }


        return this with
        {
            CommitmentId =
                commitmentId,

            Summary =
                summary,

            Temporal =
                Temporal?.Normalize(),

            EvidenceQuote =
                evidence,

            Reason =
                reason,

            Confidence =
                Math.Clamp(
                    Confidence,
                    0.0,
                    1.0)
        };
    }
}


public enum NIRACommitmentApplyAction
{
    Created,

    Transitioned,

    Rescheduled,

    Duplicate,

    NoChange,

    Rejected
}


public sealed record NIRACommitmentApplyResult
{
    public NIRACommitmentApplyAction Action
    {
        get;
        init;
    }


    public NIRACommitmentState? Commitment
    {
        get;
        init;
    }


    public string Reason
    {
        get;
        init;
    } =
        string.Empty;
}

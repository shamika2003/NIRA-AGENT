/*
 * filename: NIRABranchWorkContracts.cs
 */

using NIRAAgent.Capabilities;
using NIRAAgent.Tools;

namespace NIRAAgent.Branches;

// =============================================================
// BRANCH-OWNED ASSIGNED WORK
//
// A branch is a persistent responsibility/work basket. It does
// not reason and it is not a tool. NIRA cognition chooses a
// bounded piece of work, assigns it to a branch, then the runtime
// executes that exact work and returns the result to NIRA.
// =============================================================

public enum NIRABranchWorkKind
{
    Capability,
    DynamicTool
}

public enum NIRABranchWorkStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Rejected,
    AuthorizationRequired,
    Interrupted,
    Cancelled
}

public sealed record NIRABranchWorkProposal
{
    public string BranchId { get; init; } = string.Empty;

    public NIRABranchWorkKind Kind { get; init; }

    public NIRACapabilityRequest? CapabilityRequest { get; init; }

    public NIRADynamicToolInvocation? DynamicToolInvocation { get; init; }

    public string Reason { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public NIRABranchWorkProposal Normalize()
    {
        string branchId =
            BranchId?.Trim()
            ?? string.Empty;

        if (branchId.Length > 80)
        {
            throw new InvalidOperationException(
                "Branch work branchId is too long.");
        }

        string reason =
            Reason?.Trim()
            ?? string.Empty;

        if (reason.Length > 1600)
        {
            throw new InvalidOperationException(
                "Branch work reason is too long.");
        }

        NIRACapabilityRequest? capabilityRequest =
            CapabilityRequest?.Normalize();

        NIRADynamicToolInvocation? dynamicToolInvocation =
            DynamicToolInvocation?.Normalize();

        switch (Kind)
        {
            case NIRABranchWorkKind.Capability:
            {
                if (capabilityRequest == null)
                {
                    throw new InvalidOperationException(
                        "Capability branch work requires capabilityRequest.");
                }

                if (dynamicToolInvocation != null)
                {
                    throw new InvalidOperationException(
                        "Capability branch work cannot also contain dynamicToolInvocation.");
                }

                break;
            }

            case NIRABranchWorkKind.DynamicTool:
            {
                if (dynamicToolInvocation == null)
                {
                    throw new InvalidOperationException(
                        "DynamicTool branch work requires dynamicToolInvocation.");
                }

                if (capabilityRequest != null)
                {
                    throw new InvalidOperationException(
                        "DynamicTool branch work cannot also contain capabilityRequest.");
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(Kind));
        }

        return this with
        {
            BranchId = branchId,
            CapabilityRequest = capabilityRequest,
            DynamicToolInvocation = dynamicToolInvocation,
            Reason = reason,
            Confidence = Math.Clamp(
                Confidence,
                0.0,
                1.0)
        };
    }

    public string BuildExecutionSignature()
    {
        NIRABranchWorkProposal normalized =
            Normalize();

        string payloadSignature =
            normalized.Kind switch
            {
                NIRABranchWorkKind.Capability =>
                    normalized.CapabilityRequest!
                        .BuildSignature(),

                NIRABranchWorkKind.DynamicTool =>
                    normalized.DynamicToolInvocation!
                        .BuildSignature(),

                _ =>
                    string.Empty
            };

        // Reason is explanatory metadata, not executable identity.
        // Changing prose must never turn the same branch-owned side effect
        // into a fresh work item.
        return string.Join(
            '|',
            normalized.BranchId.ToLowerInvariant(),
            normalized.Kind,
            payloadSignature);
    }


    public string BuildSignature()
    {
        return BuildExecutionSignature();
    }
}

public sealed record NIRABranchWorkItem
{
    public Guid Id { get; init; }

    public Guid BranchId { get; init; }

    public Guid GoalId { get; init; }

    public NIRABranchWorkKind Kind { get; init; }

    public NIRABranchWorkStatus Status { get; init; } =
        NIRABranchWorkStatus.Pending;

    public NIRACapabilityRequest? CapabilityRequest { get; init; }

    public NIRADynamicToolInvocation? DynamicToolInvocation { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string? ResultSummary { get; init; }

    public string? ResultEvidence { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? FinishedAtUtc { get; init; }

    public DateTimeOffset? ResultNotifiedAtUtc { get; init; }

    public bool IsTerminal =>
        Status is
            NIRABranchWorkStatus.Succeeded
            or NIRABranchWorkStatus.Failed
            or NIRABranchWorkStatus.Rejected
            or NIRABranchWorkStatus.AuthorizationRequired
            or NIRABranchWorkStatus.Interrupted
            or NIRABranchWorkStatus.Cancelled;

    public bool IsOpen =>
        !IsTerminal;

    public bool Succeeded =>
        Status ==
            NIRABranchWorkStatus.Succeeded;

    public string BuildExecutionSignature()
    {
        NIRABranchWorkItem normalized =
            Normalize();

        string payloadSignature =
            normalized.Kind switch
            {
                NIRABranchWorkKind.Capability =>
                    normalized.CapabilityRequest!
                        .BuildSignature(),

                NIRABranchWorkKind.DynamicTool =>
                    normalized.DynamicToolInvocation!
                        .BuildSignature(),

                _ =>
                    string.Empty
            };

        return string.Join(
            '|',
            normalized.BranchId.ToString("D").ToLowerInvariant(),
            normalized.Kind,
            payloadSignature);
    }


    public NIRABranchWorkItem Normalize()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A branch work item requires an ID.");
        }

        if (BranchId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A branch work item requires a branch ID.");
        }

        if (GoalId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A branch work item requires a goal ID.");
        }

        string reason =
            Reason?.Trim()
            ?? string.Empty;

        if (reason.Length > 1600)
        {
            throw new InvalidOperationException(
                "Branch work reason is too long.");
        }

        string? resultSummary =
            NormalizeOptional(
                ResultSummary,
                2400);

        string? resultEvidence =
            NormalizeOptional(
                ResultEvidence,
                24000);

        NIRACapabilityRequest? capabilityRequest =
            CapabilityRequest?.Normalize();

        NIRADynamicToolInvocation? dynamicToolInvocation =
            DynamicToolInvocation?.Normalize();

        if (Kind == NIRABranchWorkKind.Capability &&
            capabilityRequest == null)
        {
            throw new InvalidOperationException(
                "Capability work requires a capability request.");
        }

        if (Kind == NIRABranchWorkKind.DynamicTool &&
            dynamicToolInvocation == null)
        {
            throw new InvalidOperationException(
                "Dynamic-tool work requires a dynamic tool invocation.");
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        return this with
        {
            CapabilityRequest = capabilityRequest,
            DynamicToolInvocation = dynamicToolInvocation,
            Reason = reason,
            ResultSummary = resultSummary,
            ResultEvidence = resultEvidence,
            CreatedAtUtc =
                CreatedAtUtc == default
                    ? now
                    : CreatedAtUtc
        };
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }

        string clean =
            value.Trim();

        if (clean.Length > maximumLength)
        {
            clean =
                clean[..maximumLength];
        }

        return clean;
    }
}

public enum NIRABranchWorkApplyAction
{
    Rejected,
    Queued,
    Duplicate,
    NoChange
}

public sealed record NIRABranchWorkApplyResult
{
    public NIRABranchWorkApplyAction Action { get; init; }

    public NIRABranchWorkItem? Work { get; init; }

    public string Reason { get; init; } = string.Empty;

    public bool Changed =>
        Action == NIRABranchWorkApplyAction.Queued;
}

// =============================================================
// LONG-RUNNING RECONSIDERATION SNAPSHOT
//
// This is observation state only. It is not a tool result and it
// does not imply success/failure/progress beyond still Running.
// =============================================================

public sealed record NIRABranchWorkReconsiderationEntry
{
    public Guid WorkId { get; init; }

    public Guid BranchId { get; init; }

    public Guid GoalId { get; init; }

    public NIRABranchWorkKind Kind { get; init; }

    public string BranchObjective { get; init; } = string.Empty;

    public string WorkReason { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }
}

public sealed record NIRABranchWorkReconsiderationSnapshot
{
    public Guid GoalId { get; init; }

    public int Sequence { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public IReadOnlyList<NIRABranchWorkReconsiderationEntry> RunningWork { get; init; } =
        Array.Empty<NIRABranchWorkReconsiderationEntry>();
}


public sealed record NIRABranchWorkResultEvent
{
    public Guid WorkId { get; init; }

    public Guid BranchId { get; init; }

    public Guid GoalId { get; init; }

    public NIRABranchWorkKind Kind { get; init; }

    public NIRABranchWorkStatus Status { get; init; }

    public string BranchObjective { get; init; } = string.Empty;

    public string WorkReason { get; init; } = string.Empty;

    public string ResultSummary { get; init; } = string.Empty;

    public string ResultEvidence { get; init; } = string.Empty;

    public DateTimeOffset FinishedAtUtc { get; init; }

    public bool Succeeded =>
        Status == NIRABranchWorkStatus.Succeeded;
}

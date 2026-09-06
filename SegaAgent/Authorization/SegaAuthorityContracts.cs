using System.Text.Json;
using SegaAgent.Capabilities;

namespace SegaAgent.Authorization;

public enum SegaAuthorityScopeKind { Folder, ExactRequest, HttpOrigin, VisionCloud }
public enum SegaApprovalChoice { Deny, AllowOnce, Remember }

public sealed record SegaAuthorityScope
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public SegaAuthorityScopeKind Kind { get; init; }
    public string Label { get; init; } = string.Empty;
    public string[] CapabilityIds { get; init; } = [];
    public string RootPath { get; init; } = string.Empty;
    public string Origin { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public bool AllowDestructive { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; init; }
    public bool Active => RevokedAtUtc == null &&
        (ExpiresAtUtc == null || ExpiresAtUtc > DateTimeOffset.UtcNow);
    public string Description => Kind switch
    {
        SegaAuthorityScopeKind.Folder => $"{RootPath} | {string.Join(", ", CapabilityIds)} | destructive={AllowDestructive}",
        SegaAuthorityScopeKind.HttpOrigin => $"{Method} {Origin} | {string.Join(", ", CapabilityIds)}",
        SegaAuthorityScopeKind.VisionCloud => $"Cloud visual interpretation | {string.Join(", ", CapabilityIds)}",
        _ => $"{Label} | exact request {Fingerprint[..Math.Min(12, Fingerprint.Length)]}"
    };
}

public sealed record SegaAuthorityOperation
{
    public required SegaCapabilityRequest Request { get; init; }
    public required SegaCapabilityRisk Risk { get; init; }
    public required string Fingerprint { get; init; }
    public string[] Paths { get; init; } = [];
    public string Origin { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Notice { get; init; } = string.Empty;
    public bool Sensitive { get; init; }
    public bool RequiresExplicitAuthorization { get; init; }
    public bool CanRemember { get; init; } = true;
    public string SuggestedRoot { get; init; } = string.Empty;
    public string Preview => SegaCapabilityRequestPolicy.BuildPreview(Request);
}

public sealed record SegaApprovalRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required SegaAuthorityOperation Operation { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Display => $"{Operation.Request.CapabilityId} | {Operation.Target}";
}

public sealed record SegaApprovalResponse
{
    public SegaApprovalChoice Choice { get; init; }
    public string FolderRoot { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record SegaAuthorityAuditRow
{
    public string Id { get; init; } = string.Empty;
    public string TimeUtc { get; init; } = string.Empty;
    public string Capability { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ScopeId { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
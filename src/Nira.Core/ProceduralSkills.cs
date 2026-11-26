namespace Nira.Core;

/// <summary>Advisory procedural memory. This contract cannot execute or authorize tools.</summary>
public interface IProceduralSkillService
{
    Task<string?> RecallAsync(string objective, CancellationToken ct);
    Task ObserveAsync(Guid taskId, CancellationToken ct, IProgress<AgentProgress>? progress);
}

/// <summary>Stable identity of the registered contract, not an implementation or result hash.</summary>
public static class CapabilityContractFingerprint
{
    public static string Compute(ToolDescriptor descriptor) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new { descriptor.Name, descriptor.Description, descriptor.Risk,
                descriptor.HasSideEffects, descriptor.MaximumDispatchAttempts, descriptor.Parameters })))).ToLowerInvariant();
}

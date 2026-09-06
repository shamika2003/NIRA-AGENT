/*
 * filename: SegaStage9CapabilityAuthorizer.cs
 */

namespace SegaAgent.Capabilities;

public sealed class SegaStage9CapabilityAuthorizer
    : ISegaCapabilityAuthorizer
{
    public Task<SegaCapabilityAuthorizationDecision> AuthorizeAsync(
        SegaCapabilityDescriptor descriptor,
        SegaCapabilityRisk risk,
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        if (risk ==
            SegaCapabilityRisk.Observe)
        {
            return Task.FromResult(
                SegaCapabilityAuthorizationDecision.Allow(
                    "Read-only observation capability is enabled by the Stage 9 baseline authority boundary."));
        }


        return Task.FromResult(
            SegaCapabilityAuthorizationDecision.RequireAuthorization(
                "This capability can change or execute state. Persistent scoped authorization is intentionally deferred to Stage 10."));
    }
}

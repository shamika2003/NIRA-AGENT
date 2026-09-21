/*
 * filename: NIRAStage9CapabilityAuthorizer.cs
 */

namespace NIRAAgent.Capabilities;

public sealed class NIRAStage9CapabilityAuthorizer
    : INIRACapabilityAuthorizer
{
    public Task<NIRACapabilityAuthorizationDecision> AuthorizeAsync(
        NIRACapabilityDescriptor descriptor,
        NIRACapabilityRisk risk,
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        if (risk ==
            NIRACapabilityRisk.Observe)
        {
            return Task.FromResult(
                NIRACapabilityAuthorizationDecision.Allow(
                    "Read-only observation capability is enabled by the Stage 9 baseline authority boundary."));
        }


        return Task.FromResult(
            NIRACapabilityAuthorizationDecision.RequireAuthorization(
                "This capability can change or execute state. Persistent scoped authorization is intentionally deferred to Stage 10."));
    }
}


using System.Text.Json;

namespace NIRAAgent.Capabilities;

/// <summary>Trusted host bridge for the existing create-only invariant.
/// Never expose RequireCreateNew as a model argument.</summary>
public static class NIRAV2CreateOnlyRequestFactory
{
    public static NIRACapabilityRequest Create(Guid requestId, string fullPath, string content)
    {
        if (requestId == Guid.Empty || !Path.IsPathFullyQualified(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".txt", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(content) || content.Length > 256 ||
            content.Any(char.IsControl))
            throw new ArgumentException("Invalid V2 create-only request.");
        return new NIRACapabilityRequest
        {
            RequestId = requestId,
            CapabilityId = NIRACapabilityIds.FileWrite,
            Arguments = JsonSerializer.SerializeToElement(new
            {
                path = fullPath, content, mode = "overwrite"
            }),
            Reason = "NIRA V2 first write: create one NEW text file only, exactly as requested. " +
                     "Review the full path and content in Details. No overwrites or remembered approvals.",
            RequireCreateNew = true
        };
    }
}

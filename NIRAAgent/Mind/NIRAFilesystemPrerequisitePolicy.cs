using NIRAAgent.Capabilities;

namespace NIRAAgent.Mind;

/// <summary>
/// Prevents another model cycle from retrying an impossible filesystem
/// prerequisite. Only applies to an explicitly attempted failed directory
/// creation, and only when all unexecuted batch operations depend on it.
/// Does not infer Defender from a missing-path error or reroute destinations.
/// </summary>
internal static class NIRAFilesystemPrerequisitePolicy
{
    internal static bool TryDescribeBlockedPrerequisite(
        IReadOnlyList<NIRACapabilityRequest> issued,
        IReadOnlyList<NIRACapabilityResult> completed,
        out string message,
        out string diagnosticKind)
    {
        message = string.Empty;
        diagnosticKind = string.Empty;
        if (completed.Count != 1 || issued.Count < completed.Count)
            return false;

        // This fast terminal path is only for a single failed prerequisite.
        // Other completed actions may have useful independent results and must
        // stay with the normal evidence-preserving Executive review path.

        NIRACapabilityResult failure = completed[completed.Count - 1];
        NIRACapabilityRequest failedRequest = issued[completed.Count - 1];
        if (failure.CapabilityId != NIRACapabilityIds.DirectoryCreate ||
            failedRequest.CapabilityId != NIRACapabilityIds.DirectoryCreate ||
            failure.Succeeded ||
            failure.Status != NIRACapabilityResultStatus.Failed ||
            !failure.Summary.Contains("DirectoryCreateFailed | Kind=", StringComparison.Ordinal))
            return false;

        // Stop only when the outstanding batch is empty or entirely blocked by
        // this exact parent; unrelated sibling work must remain executable.
        if (issued.Skip(completed.Count).Any(next =>
                !NIRACapabilityService.DependsOnFailedDirectoryCreation(failedRequest, next)))
            return false;

        if (failure.Summary.Contains("Kind=AccessDenied", StringComparison.Ordinal))
        {
            diagnosticKind = "AccessDenied";
            message = "Windows denied creation of the requested directory, so I didn't save the file there. " +
                "Check Windows Security protection history and the folder's permissions. " +
                "I won't change Defender settings or choose another destination without your approval.";
        }
        else if (failure.Summary.Contains("Kind=PathOrProviderNotFound", StringComparison.Ordinal) ||
                 failure.Summary.Contains("Kind=ParentPathUnavailable", StringComparison.Ordinal))
        {
            diagnosticKind = "PathOrProviderNotFound";
            message = "Windows could not create the requested directory because its configured path or storage provider " +
                "was unavailable. No file was saved there. This error does not establish a Defender block. " +
                "Check that the Windows folder location and any cloud-sync folder are accessible; " +
                "I won't substitute another folder and call it complete.";
        }
        else
        {
            diagnosticKind = "FilesystemIOError";
            message = "The directory creation failed at the operating-system level, so no dependent file " +
                "was saved. I stopped rather than repeating the same write. " +
                "Check the destination's access and availability before trying again.";
        }
        return true;
    }
}

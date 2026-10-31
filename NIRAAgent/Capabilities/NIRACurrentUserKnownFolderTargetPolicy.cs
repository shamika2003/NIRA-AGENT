namespace NIRAAgent.Capabilities;

/// <summary>
/// Prevents a non-existent known-folder path under a different Windows profile
/// from being mistaken for the current user's Downloads/Documents/etc.
/// This never rewrites a user-selected destination or grants access. A real,
/// observed existing known-folder path under another profile is not blocked.
/// </summary>
internal static class NIRACurrentUserKnownFolderTargetPolicy
{
    internal const string DiagnosticCode = "UnverifiedOtherProfileKnownFolder";

    internal static bool IsUnverifiedOtherProfileKnownFolder(
        string target, string currentProfile, Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(target) ||
            string.IsNullOrWhiteSpace(currentProfile) ||
            !Path.IsPathFullyQualified(target) ||
            !Path.IsPathFullyQualified(currentProfile)) return false;

        string current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentProfile));
        string? profilesRoot = Path.GetDirectoryName(current);
        if (string.IsNullOrWhiteSpace(profilesRoot)) return false;
        string rootWithSeparator = Path.EndsInDirectorySeparator(profilesRoot)
            ? profilesRoot : profilesRoot + Path.DirectorySeparatorChar;
        string fullTarget = Path.GetFullPath(target);
        if (!fullTarget.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            return false;

        // Relative to the parent of the actual profile: <other-user>/<KnownFolder>/...
        string relative = fullTarget[rootWithSeparator.Length..];
        string[] segments = relative.Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[0] is "." or ".." ||
            !NIRACapabilityArguments.IsCurrentUserKnownFolderName(segments[1]))
            return false;
        string otherProfile = Path.Combine(profilesRoot, segments[0]);
        if (otherProfile.Equals(current, StringComparison.OrdinalIgnoreCase))
            return false;
        string otherKnownFolder = Path.Combine(otherProfile, segments[1]);
        // Do not interfere with explicitly targeted, already observed external
        // profile directories; the normal authorization policy still applies.
        return !directoryExists(otherKnownFolder);
    }

    internal static void RejectUnverifiedOtherProfileKnownFolder(string target)
    {
        if (!IsUnverifiedOtherProfileKnownFolder(target,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Directory.Exists)) return;
        throw new InvalidOperationException(DiagnosticCode +
            ": The requested path names a missing Windows known folder under " +
            "a DIFFERENT user profile. That is not proof of a Windows access denial. " +
            "For the current user's Downloads/Documents/Desktop, use the registered " +
            "path alias (example: Downloads\\file.docx) so Windows resolves the " +
            "actual current-user folder. If another user's folder was intentionally " +
            "requested, observe its real path before attempting a write. " +
            "No destination was changed and no write was attempted.");
    }
}

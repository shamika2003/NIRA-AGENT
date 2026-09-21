using System.Text.RegularExpressions;

using NIRAAgent.Capabilities;

namespace NIRAAgent.Authorization;

// A deliberately NARROW bridge between a direct user instruction and the
// requested primitive. This is not a model-authored scope or a free-form
// natural-language permissions parser. Uncertain requests still go through
// the existing grant/approval pipeline. A user request from another run,
// branch, memory, web page or model response cannot grant this authority.
public static class NIRARiskAdaptiveAuthority
{
    public const string BaselineCommandBasis = "BaselineSafeCommand";
    public const string DirectTaskBasis = "DirectUserTask";

    // Get-Date with no arguments is a pure local clock observation; the shell
    // handler still runs with an audit record and a bounded timeout.
    public static bool IsSafeLocalObservation(NIRAAuthorityOperation operation)
    {
        if (operation.Sensitive || operation.RequiresExplicitAuthorization ||
            operation.Risk == NIRACapabilityRisk.Destructive ||
            operation.Request.CapabilityId != NIRACapabilityIds.ShellExecute)
            return false;

        string shell = NIRACapabilityArguments.GetOptionalString(
            operation.Request, "shell", 32) ?? "powershell";
        if (shell is not ("powershell" or "pwsh"))
            return false;

        string command = NIRACapabilityArguments.RequireRawString(
            operation.Request, "command", 12000);
        return string.Equals(command.Trim(), "Get-Date", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDirectUserDelegatedAction(
        NIRAAuthorityOperation operation,
        NIRACapabilityRequestPolicy policy,
        string authorityDirectory)
    {
        NIRAAuthorityExecutionContext context = operation.ExecutionContext;
        if (!context.UserInitiated || context.RunId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.DirectUserRequest) ||
            operation.Sensitive || operation.RequiresCredentialUse ||
            operation.RequiresExplicitAuthorization ||
            operation.Risk == NIRACapabilityRisk.Destructive)
            return false;

        string request = context.DirectUserRequest;
        if (request.Length > 12000)
            return false;

        if (operation.Request.CapabilityId == NIRACapabilityIds.FileWrite)
            return IsExplicitNewFile(operation, policy, authorityDirectory, request);

        return IsExplicitProjectCommand(operation, policy, authorityDirectory, request);
    }

    private static bool IsExplicitNewFile(
        NIRAAuthorityOperation operation,
        NIRACapabilityRequestPolicy policy,
        string authorityDirectory,
        string originalRequest)
    {
        if (operation.Paths.Length != 1 || operation.Risk != NIRACapabilityRisk.Modify)
            return false;

        string target = operation.Paths[0];
        string? root = Path.GetDirectoryName(target);
        if (root == null || !Directory.Exists(root) ||
            File.Exists(target) || Directory.Exists(target) ||
            !IsSafeTarget(target, root, policy, authorityDirectory))
            return false;

        string name = Path.GetFileName(target);
        if (name.Length == 0 || !ContainsStandalone(originalRequest, name) ||
            !ContainsExplicitPath(originalRequest, root) ||
            Regex.IsMatch(originalRequest,
                @"\b(?:don't|do not|never|avoid)\s+(?:create|make|write|save|add)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        // In this narrow implicit lane, only a newly named file can be
        // created. Existing files/append/overwrite stay on an approved scope.
        string mode = NIRACapabilityArguments.GetOptionalString(
            operation.Request, "mode", 32) ?? "overwrite";
        if (!string.Equals(mode, "overwrite", StringComparison.OrdinalIgnoreCase) ||
            NIRACapabilityArguments.GetOptionalString(
                operation.Request, "expectedLastWriteUtc", 128) is not null)
            return false;

        string content = NIRACapabilityArguments.RequireRawString(
            operation.Request, "content", 2_000_000);
        // Only the literal content the user supplied is eligible for this
        // narrow automatic path. Generated/rewritten code needs a scope grant.
        string requestedContent = content.TrimEnd('\r', '\n');
        if (content.Length > 4096 ||
            (requestedContent.Length > 0 && !originalRequest.Contains(
                requestedContent, StringComparison.Ordinal)))
            return false;

        return Regex.IsMatch(originalRequest,
            @"\b(create|make|write|save|add)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsExplicitProjectCommand(
        NIRAAuthorityOperation operation,
        NIRACapabilityRequestPolicy policy,
        string authorityDirectory,
        string originalRequest)
    {
        if (!NIRAAuthorityExecutionProfiles.IsExecutionCapability(
                operation.Request.CapabilityId) ||
            operation.Risk != NIRACapabilityRisk.Execute ||
            string.IsNullOrWhiteSpace(operation.WorkingDirectory))
            return false;

        string root = operation.WorkingDirectory;
        if (!IsSafeTarget(root, root, policy, authorityDirectory) ||
            !ContainsExplicitPath(originalRequest, root) ||
            !Directory.Exists(root) ||
            !NIRAAuthorityExecutionProfiles.SuggestForWorkspace(root)
                .Contains(NIRAAuthorityExecutionProfileIds.DotNetBuildTest,
                    StringComparer.Ordinal))
            return false;

        // This implicit lane is much narrower than the separately-approved
        // execution profile: no extra MSBuild properties, targets, project
        // paths, shell syntax or command arguments may sneak through.
        string? verb = ExactDotNetVerb(operation);
        if (verb == null)
            return false;

        if (Regex.IsMatch(originalRequest,
                @"\b(?:don't|do not|never|avoid)\s+(?:run\s+|execute\s+)?dotnet(?:\.exe)?\s+" +
                Regex.Escape(verb) + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        return Regex.IsMatch(originalRequest,
            @"\b(?:run|execute)\s+dotnet(?:\.exe)?[ \t]+" +
            Regex.Escape(verb) + @"(?![\w/\\-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string? ExactDotNetVerb(NIRAAuthorityOperation operation)
    {
        string? command;
        if (operation.Request.CapabilityId == NIRACapabilityIds.ShellExecute)
        {
            string shell = NIRACapabilityArguments.GetOptionalString(
                operation.Request, "shell", 32) ?? "powershell";
            if (shell is not ("powershell" or "pwsh" or "cmd"))
                return null;
            command = NIRACapabilityArguments.RequireRawString(
                operation.Request, "command", 12000).Trim();
        }
        else
        {
            string executable = NIRACapabilityArguments.RequireString(
                operation.Request, "fileName", 32760);
            if (!string.Equals(Path.GetFileNameWithoutExtension(executable),
                    "dotnet", StringComparison.OrdinalIgnoreCase))
                return null;
            string arguments = NIRACapabilityArguments.GetOptionalRawString(
                operation.Request, "arguments", 12000) ?? string.Empty;
            command = "dotnet " + arguments.Trim();
        }

        Match match = Regex.Match(command,
            @"^dotnet(?:\.exe)?[ \t]+(clean|restore|build|test)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static bool IsSafeTarget(string path, string root,
        NIRACapabilityRequestPolicy policy, string authorityDirectory)
    {
        try
        {
            policy.ValidatePath(path);
            policy.ValidatePath(root);
            return !NIRACapabilityRequestPolicy.IsWithin(path, authorityDirectory)
                && !NIRACapabilityRequestPolicy.IsWithin(authorityDirectory, root);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsExplicitPath(string request, string path) =>
        ContainsStandalone(request.Replace('/', '\\'), path.Replace('/', '\\'));

    private static bool ContainsStandalone(string haystack, string needle)
    {
        for (int index = 0; (index = haystack.IndexOf(
                 needle, index, StringComparison.OrdinalIgnoreCase)) >= 0; index++)
        {
            int end = index + needle.Length;
            bool before = index == 0 || !IsPartOfIdentifier(haystack[index - 1]);
            bool after = end == haystack.Length || !IsPartOfIdentifier(haystack[end]);
            if (before && after)
                return true;
        }
        return false;
    }

    private static bool IsPartOfIdentifier(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.' or '\\' or '/';
}


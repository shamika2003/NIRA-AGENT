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
        string exact = command.Trim();
        if (new[] { "Get-Date", "Get-NetIPConfiguration", "Get-NetAdapter",
                    "Get-DnsClientServerAddress" }
                .Contains(exact, StringComparer.OrdinalIgnoreCase))
            return true;
        // Pure outbound ICMP reachability probes cannot mutate Windows state.
        // Anchor the WHOLE command; metacharacters, interpolation, redirects,
        // scripts and composed commands are never implicitly authorized.
        return Regex.IsMatch(exact,
            @"^ping(?:\.exe)?(?:[ \t]+-n[ \t]+[1-4])?[ \t]+[a-z0-9][a-z0-9.-]{0,252}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(exact,
            @"^Test-NetConnection[ \t]+-ComputerName[ \t]+[a-z0-9][a-z0-9.-]{0,252}[ \t]+-Port[ \t]+(?:80|443)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

        if (IsBoundedPersonalFileCreation(operation, policy, authorityDirectory, request))
            return true;

        if (operation.Request.CapabilityId == NIRACapabilityIds.FileWrite)
            return IsExplicitNewFile(operation, policy, authorityDirectory, request);

        return IsExplicitProjectCommand(operation, policy, authorityDirectory, request);
    }

    // A trusted direct user instruction to CREATE new work in their ordinary
    // personal folders is itself sufficient for this bounded operation. This
    // is NOT a reusable or model-authored grant. Existing files, deletion,
    // shell execution, hidden profiles, junctions and credential data remain
    // under the normal approval pipeline.
    private static bool IsBoundedPersonalFileCreation(
        NIRAAuthorityOperation operation,
        NIRACapabilityRequestPolicy policy,
        string authorityDirectory,
        string originalRequest)
    {
        string id = operation.Request.CapabilityId;
        bool folderCreate = id == NIRACapabilityIds.DirectoryCreate;
        bool textCreate = id == NIRACapabilityIds.FileWrite;
        if ((!folderCreate && !textCreate) ||
            operation.Risk != NIRACapabilityRisk.Modify ||
            operation.Paths.Length != 1 || operation.Sensitive ||
            operation.RequiresExplicitAuthorization)
            return false;

        // Only an imperative from the CURRENT actual user, not a retrieved
        // page, stored goal, or worker continuation, can open this lane.
        if (!Regex.IsMatch(originalRequest,
            @"\b(?:create|save|write|generate|draft|prepare|make\s+(?:me|a|an|the|new)\b|put\s+(?:it|this|that|the)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(originalRequest,
                @"\b(?:don't|do not|never|avoid)\s+(?:create|make|save|write|generate|prepare|put|draft|build)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        string target = operation.Paths[0];
        string personal = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(personal, "Downloads")
        ];
        if (roots.All(root => string.IsNullOrWhiteSpace(root) ||
            !NIRACapabilityRequestPolicy.IsWithin(target, root)))
            return false;
        if (!IsSafeTarget(target, Path.GetDirectoryName(target) ?? target,
                policy, authorityDirectory) ||
            File.Exists(target) || Directory.Exists(target))
            return false;
        if (folderCreate) return true;

        string? parent = Path.GetDirectoryName(target);
        if (parent == null || !Directory.Exists(parent)) return false;
        if (textCreate)
        {
            string extension = Path.GetExtension(target);
            if (!new[] { ".txt", ".md", ".csv", ".json", ".xml", ".yml",
                    ".yaml", ".html", ".css", ".js", ".cs", ".py", ".rtf" }
                    .Contains(extension, StringComparer.OrdinalIgnoreCase))
                return false;
            string mode = NIRACapabilityArguments.GetOptionalString(
                operation.Request, "mode", 32) ?? "overwrite";
            if (!mode.Equals("overwrite", StringComparison.OrdinalIgnoreCase) ||
                NIRACapabilityArguments.GetOptionalString(operation.Request,
                    "expectedLastWriteUtc", 128) is not null)
                return false;
            string content = NIRACapabilityArguments.RequireRawString(
                operation.Request, "content", 2_000_000);
            return content.Length <= 40_000;
        }
        return false;
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

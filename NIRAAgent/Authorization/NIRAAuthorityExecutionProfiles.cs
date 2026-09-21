using System.Text;

using NIRAAgent.Capabilities;

namespace NIRAAgent.Authorization;

public static class NIRAAuthorityExecutionProfileIds
{
    public const string DotNetBuildTest =
        "project.dotnet.build-test";

    public const string GitInspect =
        "project.git.inspect";

    public static IReadOnlyList<string> All { get; } =
        new[]
        {
            DotNetBuildTest,
            GitInspect
        };
}

public sealed record NIRAAuthorityExecutionProfileDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
}

// =============================================================
// BOUNDED PROJECT EXECUTION PROFILES
//
// A trusted project/folder does NOT become unrestricted shell authority.
// These profiles permit only narrowly recognized command families and only
// while the working directory remains inside the approved workspace root.
// The shell grammar deliberately rejects command chaining, redirection,
// substitutions and multiline commands before a profile can match.
// =============================================================
public static class NIRAAuthorityExecutionProfiles
{
    private static readonly IReadOnlyDictionary<string, NIRAAuthorityExecutionProfileDefinition>
        Definitions =
            new Dictionary<string, NIRAAuthorityExecutionProfileDefinition>(
                StringComparer.Ordinal)
            {
                [NIRAAuthorityExecutionProfileIds.DotNetBuildTest] =
                    new NIRAAuthorityExecutionProfileDefinition
                    {
                        Id = NIRAAuthorityExecutionProfileIds.DotNetBuildTest,
                        DisplayName = ".NET build / test",
                        Description =
                            "Allows dotnet clean, restore, build and test inside the approved project. " +
                            "The project/build files themselves can execute code with the Windows user's access; this is not a sandbox."
                    },

                [NIRAAuthorityExecutionProfileIds.GitInspect] =
                    new NIRAAuthorityExecutionProfileDefinition
                    {
                        Id = NIRAAuthorityExecutionProfileIds.GitInspect,
                        DisplayName = "Git inspection",
                        Description =
                            "Allows read-only Git inspection such as status, diff, log, show, rev-parse, remote -v and branch listing. " +
                            "It does not authorize add, commit, checkout, reset, fetch, pull, push or other repository mutations."
                    }
            };

    public static IReadOnlyList<NIRAAuthorityExecutionProfileDefinition> AllDefinitions =>
        Definitions.Values.ToArray();

    public static IReadOnlyList<string> NormalizeIds(
        IEnumerable<string>? profileIds)
    {
        if (profileIds == null)
        {
            return Array.Empty<string>();
        }

        List<string> normalized = new();

        foreach (string raw in profileIds)
        {
            string id = raw?.Trim() ?? string.Empty;

            if (id.Length == 0)
            {
                continue;
            }

            if (!Definitions.ContainsKey(id))
            {
                throw new InvalidOperationException(
                    $"Unknown project execution profile '{id}'.");
            }

            if (!normalized.Contains(id, StringComparer.Ordinal))
            {
                normalized.Add(id);
            }
        }

        return normalized;
    }

    public static IReadOnlyList<string> SuggestForWorkspace(
        string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return Array.Empty<string>();
        }

        string normalized;

        try
        {
            normalized =
                NIRACapabilityRequestPolicy.NormalizeLocalPath(root);
        }
        catch
        {
            return Array.Empty<string>();
        }

        if (!Directory.Exists(normalized))
        {
            return Array.Empty<string>();
        }

        List<string> suggestions = new();

        try
        {
            bool dotnet =
                Directory.EnumerateFiles(normalized, "*.sln", SearchOption.TopDirectoryOnly).Any()
                ||
                Directory.EnumerateFiles(normalized, "*.slnx", SearchOption.TopDirectoryOnly).Any()
                ||
                Directory.EnumerateFiles(normalized, "*.csproj", SearchOption.TopDirectoryOnly).Any()
                ||
                Directory.EnumerateDirectories(normalized, "*", SearchOption.TopDirectoryOnly)
                    .Take(30)
                    .Any(directory =>
                        Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any());

            if (dotnet)
            {
                suggestions.Add(
                    NIRAAuthorityExecutionProfileIds.DotNetBuildTest);
            }
        }
        catch
        {
            // A suggestion is convenience only; failure to inspect never grants authority.
        }

        try
        {
            if (Directory.Exists(Path.Combine(normalized, ".git")))
            {
                suggestions.Add(
                    NIRAAuthorityExecutionProfileIds.GitInspect);
            }
        }
        catch
        {
        }

        return suggestions;
    }

    public static bool IsExecutionCapability(
        string capabilityId) =>
        capabilityId is
            NIRACapabilityIds.ShellExecute
            or
            NIRACapabilityIds.ProcessStart;

    public static bool MatchesAny(
        NIRAAuthorityOperation operation,
        IEnumerable<string>? profileIds,
        out string matchedProfileId)
    {
        matchedProfileId = string.Empty;

        foreach (string id in NormalizeIds(profileIds))
        {
            if (Matches(operation, id))
            {
                matchedProfileId = id;
                return true;
            }
        }

        return false;
    }

    public static bool Matches(
        NIRAAuthorityOperation operation,
        string profileId)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (!IsExecutionCapability(operation.Request.CapabilityId))
        {
            return false;
        }

        IReadOnlyList<string> tokens;

        if (operation.Request.CapabilityId == NIRACapabilityIds.ShellExecute)
        {
            string command =
                NIRACapabilityArguments.RequireRawString(
                    operation.Request,
                    "command",
                    12000);

            if (!TryTokenizeShellCommand(command, out tokens))
            {
                return false;
            }
        }
        else
        {
            string fileName =
                NIRACapabilityArguments.RequireString(
                    operation.Request,
                    "fileName",
                    32760);

            string arguments =
                NIRACapabilityArguments.GetOptionalRawString(
                    operation.Request,
                    "arguments",
                    12000)
                ?? string.Empty;

            if (!TryTokenizeProcessArguments(arguments, out IReadOnlyList<string> args))
            {
                return false;
            }

            List<string> combined =
                new()
                {
                    fileName
                };

            combined.AddRange(args);
            tokens = combined;
        }

        return profileId switch
        {
            NIRAAuthorityExecutionProfileIds.DotNetBuildTest =>
                MatchDotNetBuildTest(tokens),

            NIRAAuthorityExecutionProfileIds.GitInspect =>
                MatchGitInspect(tokens),

            _ => false
        };
    }

    private static bool MatchDotNetBuildTest(
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2 ||
            !ExecutableNameIs(tokens[0], "dotnet"))
        {
            return false;
        }

        string command =
            tokens[1].Trim().ToLowerInvariant();

        return command is
            "clean"
            or
            "restore"
            or
            "build"
            or
            "test";
    }

    private static bool MatchGitInspect(
        IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2 ||
            !ExecutableNameIs(tokens[0], "git"))
        {
            return false;
        }

        string command =
            tokens[1].Trim().ToLowerInvariant();

        switch (command)
        {
            case "status":
            case "diff":
            case "log":
            case "show":
            case "rev-parse":
                return true;

            case "remote":
                return tokens.Count == 3 &&
                       tokens[2].Equals("-v", StringComparison.OrdinalIgnoreCase);

            case "branch":
                return MatchReadOnlyBranch(tokens.Skip(2).ToArray());

            default:
                return false;
        }
    }

    private static bool MatchReadOnlyBranch(
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return true;
        }

        foreach (string argument in arguments)
        {
            string value = argument.Trim();

            if (value.Equals("--show-current", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("--list", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-l", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("--all", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-a", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("--remotes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("-vv", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("--no-color", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("--sort=", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A positional branch name can create a branch, so reject it.
            return false;
        }

        return true;
    }

    private static bool ExecutableNameIs(
        string value,
        string expected)
    {
        string file =
            Path.GetFileNameWithoutExtension(
                value.Trim().Trim('"'));

        return string.Equals(
            file,
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryTokenizeShellCommand(
        string command,
        out IReadOnlyList<string> tokens)
    {
        tokens = Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(command) ||
            command.Contains('`') ||
            command.Contains('$'))
        {
            return false;
        }

        return TryTokenize(
            command,
            rejectShellOperators: true,
            out tokens);
    }

    private static bool TryTokenizeProcessArguments(
        string arguments,
        out IReadOnlyList<string> tokens) =>
        TryTokenize(
            arguments,
            rejectShellOperators: false,
            out tokens);

    private static bool TryTokenize(
        string text,
        bool rejectShellOperators,
        out IReadOnlyList<string> tokens)
    {
        List<string> result = new();
        StringBuilder current = new();
        char quote = '\0';

        for (int index = 0; index < text.Length; index++)
        {
            char ch = text[index];

            if (quote == '\0')
            {
                if (ch is '\r' or '\n')
                {
                    tokens = Array.Empty<string>();
                    return false;
                }

                if (rejectShellOperators &&
                    ch is ';' or '|' or '&' or '>' or '<')
                {
                    tokens = Array.Empty<string>();
                    return false;
                }

                if (ch is '\'' or '"')
                {
                    quote = ch;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    Flush();
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch == quote)
            {
                quote = '\0';
                continue;
            }

            current.Append(ch);
        }

        if (quote != '\0')
        {
            tokens = Array.Empty<string>();
            return false;
        }

        Flush();
        tokens = result;
        return true;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            result.Add(current.ToString());
            current.Clear();
        }
    }
}


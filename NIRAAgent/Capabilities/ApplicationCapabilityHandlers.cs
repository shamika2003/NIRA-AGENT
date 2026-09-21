/*
 * filename: ApplicationCapabilityHandlers.cs
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Win32;

namespace NIRAAgent.Capabilities;


// =============================================================
// INSTALLED APPLICATION DISCOVERY
//
// Resolves a human application name to a concrete launch target.
// This is deliberately observation-only: it discovers executable
// identity/arguments, while process.start remains the authoritative
// state-changing primitive that actually launches the application.
// =============================================================

[SupportedOSPlatform("windows")]
public sealed class NIRAApplicationResolveCapabilityHandler
    : INIRACapabilityHandler
{
    private const int MaximumRegistryEntries =
        6000;


    private const int MaximumStartMenuShortcuts =
        5000;


    private const int MaximumInstallLocationExecutables =
        32;


    private const int MinimumCandidateScore =
        320;


    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.ApplicationResolve,

            Description =
                "Resolve a human-installed application name to one or more concrete Windows launch targets using PATH, App Paths, installed-program registrations, and Start Menu shortcuts. This only discovers launch targets; use process.start to launch the chosen executable.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter(
                        "query",
                        "string",
                        true,
                        "Human application name, executable name, or exact executable path to resolve."),

                    Parameter(
                        "maxResults",
                        "integer",
                        false,
                        "Maximum candidates to return. Default 8, maximum 20.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Observe;
    }


    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "application.resolve currently supports the Windows desktop runtime only.");
        }


        string query =
            NIRACapabilityArguments.RequireString(
                    request,
                    "query",
                    256)
                .Trim();


        int maximum =
            NIRACapabilityArguments.GetInteger(
                request,
                "maxResults",
                8,
                1,
                20);


        CandidateCollector collector =
            new(query);


        TryAddDirectOrPathCandidate(
            query,
            collector);


        DiscoverAppPaths(
            collector,
            cancellationToken);


        DiscoverStartMenuShortcuts(
            collector,
            cancellationToken);


        DiscoverInstalledPrograms(
            collector,
            cancellationToken);


        ApplicationCandidate[] candidates =
            collector
                .GetRanked()
                .Where(
                    candidate =>
                        candidate.Score >=
                        MinimumCandidateScore)
                .Take(
                    maximum)
                .ToArray();


        ResolutionDisposition disposition =
            ResolveDisposition(
                candidates);


        string output =
            BuildOutput(
                query,
                disposition,
                candidates);


        string summary =
            disposition switch
            {
                ResolutionDisposition.Resolved =>
                    $"Resolved installed application '{query}' to '{candidates[0].ExecutablePath}'.",

                ResolutionDisposition.Ambiguous =>
                    $"Found {candidates.Length} plausible installed application candidates for '{query}'; a single authoritative launch target was not established.",

                _ =>
                    $"No registered/Start Menu/PATH application launch target matched '{query}'."
            };


        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    summary,

                Output =
                    output,

                ChangedSystemState =
                    false
            });
    }


    // =========================================================
    // DIRECT / PATH
    // =========================================================

    private static void TryAddDirectOrPathCandidate(
        string query,
        CandidateCollector collector)
    {
        try
        {
            string expanded =
                Environment.ExpandEnvironmentVariables(
                    query.Trim().Trim('"'));


            if (Path.IsPathFullyQualified(
                    expanded))
            {
                if (File.Exists(
                        expanded)
                    &&
                    IsLaunchableExecutable(
                        expanded))
                {
                    AddExecutableCandidate(
                        collector,
                        expanded,
                        displayName: null,
                        aliases: new[] { query },
                        arguments: string.Empty,
                        workingDirectory: Path.GetDirectoryName(expanded),
                        source: "ExactPath",
                        sourceBonus: 220);
                }


                return;
            }


            if (query.Contains('/') ||
                query.Contains('\\'))
            {
                return;
            }


            string[] suffixes =
                Path.HasExtension(query)
                    ? new[] { string.Empty }
                    : new[] { string.Empty, ".exe", ".com" };


            foreach (
                string directory
                in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                    .Split(
                        Path.PathSeparator,
                        StringSplitOptions.RemoveEmptyEntries))
            {
                string cleanDirectory =
                    directory.Trim().Trim('"');


                if (!Path.IsPathFullyQualified(
                        cleanDirectory))
                {
                    continue;
                }


                foreach (
                    string suffix
                    in suffixes)
                {
                    string candidate =
                        Path.Combine(
                            cleanDirectory,
                            query + suffix);


                    if (!File.Exists(
                            candidate)
                        ||
                        !IsLaunchableExecutable(
                            candidate))
                    {
                        continue;
                    }


                    AddExecutableCandidate(
                        collector,
                        candidate,
                        displayName: null,
                        aliases: new[] { query },
                        arguments: string.Empty,
                        workingDirectory: Path.GetDirectoryName(candidate),
                        source: "PATH",
                        sourceBonus: 190);
                }
            }
        }
        catch
        {
            // Discovery must remain best-effort. Other authoritative
            // discovery sources may still resolve the application.
        }
    }


    // =========================================================
    // WINDOWS APP PATHS REGISTRY
    // =========================================================

    private static void DiscoverAppPaths(
        CandidateCollector collector,
        CancellationToken cancellationToken)
    {
        foreach (
            RegistryLocation location
            in RegistryLocations(
                @"Software\Microsoft\Windows\CurrentVersion\App Paths"))
        {
            cancellationToken.ThrowIfCancellationRequested();


            try
            {
                using RegistryKey? baseKey =
                    RegistryKey.OpenBaseKey(
                        location.Hive,
                        location.View);


                using RegistryKey? root =
                    baseKey.OpenSubKey(
                        location.SubKey,
                        writable: false);


                if (root ==
                    null)
                {
                    continue;
                }


                int visited =
                    0;


                foreach (
                    string childName
                    in root.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();


                    if (++visited >
                        MaximumRegistryEntries)
                    {
                        break;
                    }


                    using RegistryKey? child =
                        root.OpenSubKey(
                            childName,
                            writable: false);


                    string? executable =
                        CleanExecutablePath(
                            child?.GetValue(null) as string);


                    if (executable ==
                            null
                        ||
                        !File.Exists(
                            executable)
                        ||
                        !IsLaunchableExecutable(
                            executable))
                    {
                        continue;
                    }


                    string executableName =
                        Path.GetFileNameWithoutExtension(
                            childName);


                    AddExecutableCandidate(
                        collector,
                        executable,
                        displayName: null,
                        aliases: new[]
                        {
                            childName,
                            executableName
                        },
                        arguments: string.Empty,
                        workingDirectory: Path.GetDirectoryName(executable),
                        source: "WindowsAppPaths",
                        sourceBonus: 170);
                }
            }
            catch
            {
                // One registry view may be unavailable or partially denied.
            }
        }
    }


    // =========================================================
    // START MENU SHORTCUTS
    // =========================================================

    private static void DiscoverStartMenuShortcuts(
        CandidateCollector collector,
        CancellationToken cancellationToken)
    {
        HashSet<string> roots =
            new(
                StringComparer.OrdinalIgnoreCase);


        AddSpecialFolder(
            roots,
            Environment.SpecialFolder.Programs);


        AddSpecialFolder(
            roots,
            Environment.SpecialFolder.CommonPrograms);


        int visited =
            0;


        foreach (
            string root
            in roots)
        {
            if (!Directory.Exists(
                    root))
            {
                continue;
            }


            foreach (
                string shortcut
                in EnumerateShortcutFilesSafe(
                    root,
                    cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();


                if (++visited >
                    MaximumStartMenuShortcuts)
                {
                    return;
                }


                string shortcutName =
                    Path.GetFileNameWithoutExtension(
                        shortcut);


                if (collector.ScoreAlias(
                        shortcutName) <
                    180)
                {
                    continue;
                }


                ShortcutTarget? target =
                    TryResolveShortcut(
                        shortcut);


                if (target ==
                        null
                    ||
                    !File.Exists(
                        target.ExecutablePath)
                    ||
                    !IsLaunchableExecutable(
                        target.ExecutablePath))
                {
                    continue;
                }


                AddExecutableCandidate(
                    collector,
                    target.ExecutablePath,
                    displayName: shortcutName,
                    aliases: new[]
                    {
                        shortcutName
                    },
                    arguments: target.Arguments,
                    workingDirectory:
                        string.IsNullOrWhiteSpace(
                                target.WorkingDirectory)
                            ? Path.GetDirectoryName(
                                target.ExecutablePath)
                            : target.WorkingDirectory,
                    source: "StartMenuShortcut",
                    sourceBonus: 210);
            }
        }
    }




    private static IEnumerable<string> EnumerateShortcutFilesSafe(
        string root,
        CancellationToken cancellationToken)
    {
        Stack<string> pending =
            new();


        pending.Push(
            root);


        int visitedDirectories =
            0;


        while (pending.Count >
               0)
        {
            cancellationToken.ThrowIfCancellationRequested();


            if (++visitedDirectories >
                2500)
            {
                yield break;
            }


            string current =
                pending.Pop();


            string[] files;


            try
            {
                files =
                    Directory.GetFiles(
                        current,
                        "*.lnk",
                        SearchOption.TopDirectoryOnly);
            }
            catch
            {
                files =
                    Array.Empty<string>();
            }


            foreach (
                string file
                in files)
            {
                yield return file;
            }


            string[] directories;


            try
            {
                directories =
                    Directory.GetDirectories(
                        current,
                        "*",
                        SearchOption.TopDirectoryOnly);
            }
            catch
            {
                directories =
                    Array.Empty<string>();
            }


            for (
                int index = directories.Length - 1;
                index >= 0;
                index--)
            {
                pending.Push(
                    directories[index]);
            }
        }
    }


    // =========================================================
    // INSTALLED-PROGRAM REGISTRY
    // =========================================================

    private static void DiscoverInstalledPrograms(
        CandidateCollector collector,
        CancellationToken cancellationToken)
    {
        foreach (
            RegistryLocation location
            in RegistryLocations(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            cancellationToken.ThrowIfCancellationRequested();


            try
            {
                using RegistryKey? baseKey =
                    RegistryKey.OpenBaseKey(
                        location.Hive,
                        location.View);


                using RegistryKey? root =
                    baseKey.OpenSubKey(
                        location.SubKey,
                        writable: false);


                if (root ==
                    null)
                {
                    continue;
                }


                int visited =
                    0;


                foreach (
                    string childName
                    in root.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();


                    if (++visited >
                        MaximumRegistryEntries)
                    {
                        break;
                    }


                    using RegistryKey? child =
                        root.OpenSubKey(
                            childName,
                            writable: false);


                    if (child ==
                        null)
                    {
                        continue;
                    }


                    string? displayName =
                        ReadRegistryText(
                            child,
                            "DisplayName");


                    if (string.IsNullOrWhiteSpace(
                            displayName))
                    {
                        continue;
                    }


                    int displayScore =
                        collector.ScoreAlias(
                            displayName);


                    if (displayScore <
                        180)
                    {
                        continue;
                    }


                    string? displayIcon =
                        ReadRegistryText(
                            child,
                            "DisplayIcon");


                    string? iconExecutable =
                        CleanDisplayIconExecutable(
                            displayIcon);


                    if (iconExecutable !=
                            null
                        &&
                        File.Exists(
                            iconExecutable)
                        &&
                        IsLaunchableExecutable(
                            iconExecutable)
                        &&
                        !LooksLikeSupportExecutable(
                            iconExecutable))
                    {
                        AddExecutableCandidate(
                            collector,
                            iconExecutable,
                            displayName,
                            aliases: new[]
                            {
                                displayName,
                                childName
                            },
                            arguments: string.Empty,
                            workingDirectory: Path.GetDirectoryName(iconExecutable),
                            source: "InstalledProgramDisplayIcon",
                            sourceBonus: 185);
                    }


                    string? installLocation =
                        ReadRegistryText(
                            child,
                            "InstallLocation");


                    if (string.IsNullOrWhiteSpace(
                            installLocation))
                    {
                        continue;
                    }


                    string expandedLocation =
                        Environment.ExpandEnvironmentVariables(
                                installLocation)
                            .Trim()
                            .Trim('"');


                    if (!Directory.Exists(
                            expandedLocation))
                    {
                        continue;
                    }


                    IEnumerable<string> executables;


                    try
                    {
                        executables =
                            Directory.EnumerateFiles(
                                expandedLocation,
                                "*.exe",
                                SearchOption.TopDirectoryOnly)
                            .Take(
                                MaximumInstallLocationExecutables)
                            .ToArray();
                    }
                    catch
                    {
                        continue;
                    }


                    foreach (
                        string executable
                        in executables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();


                        if (LooksLikeSupportExecutable(
                                executable))
                        {
                            continue;
                        }


                        AddExecutableCandidate(
                            collector,
                            executable,
                            displayName,
                            aliases: new[]
                            {
                                displayName,
                                childName,
                                Path.GetFileNameWithoutExtension(executable)
                            },
                            arguments: string.Empty,
                            workingDirectory: expandedLocation,
                            source: "InstalledProgramLocation",
                            sourceBonus: 95);
                    }
                }
            }
            catch
            {
                // One registry view may be unavailable or partially denied.
            }
        }
    }


    // =========================================================
    // CANDIDATE ENRICHMENT
    // =========================================================

    private static void AddExecutableCandidate(
        CandidateCollector collector,
        string executablePath,
        string? displayName,
        IEnumerable<string> aliases,
        string arguments,
        string? workingDirectory,
        string source,
        int sourceBonus)
    {
        string? cleanExecutable =
            CleanExecutablePath(
                executablePath);


        if (cleanExecutable ==
                null
            ||
            !File.Exists(
                cleanExecutable)
            ||
            !IsLaunchableExecutable(
                cleanExecutable))
        {
            return;
        }


        HashSet<string> allAliases =
            new(
                StringComparer.OrdinalIgnoreCase);


        foreach (
            string alias
            in aliases)
        {
            AddAlias(
                allAliases,
                alias);
        }


        AddAlias(
            allAliases,
            Path.GetFileNameWithoutExtension(
                cleanExecutable));


        string? productName =
            null;


        string? fileDescription =
            null;


        try
        {
            FileVersionInfo info =
                FileVersionInfo.GetVersionInfo(
                    cleanExecutable);


            productName =
                info.ProductName;


            fileDescription =
                info.FileDescription;


            AddAlias(
                allAliases,
                productName);


            AddAlias(
                allAliases,
                fileDescription);
        }
        catch
        {
        }


        string effectiveDisplayName =
            FirstUseful(
                displayName,
                productName,
                fileDescription,
                Path.GetFileNameWithoutExtension(
                    cleanExecutable));


        AddAlias(
            allAliases,
            effectiveDisplayName);


        int score =
            allAliases
                .Select(
                    collector.ScoreAlias)
                .DefaultIfEmpty(0)
                .Max()
            +
            sourceBonus
            +
            ExecutablePreferenceBonus(
                cleanExecutable,
                effectiveDisplayName);


        collector.Add(
            new ApplicationCandidate(
                DisplayName:
                    effectiveDisplayName,

                ExecutablePath:
                    Path.GetFullPath(
                        cleanExecutable),

                Arguments:
                    SingleLine(
                        arguments),

                WorkingDirectory:
                    NormalizeExistingDirectory(
                        workingDirectory,
                        cleanExecutable),

                Source:
                    source,

                Match:
                    collector.DescribeBestMatch(
                        allAliases),

                Score:
                    score));
    }


    // =========================================================
    // MATCH / DISPOSITION
    // =========================================================

    private static ResolutionDisposition ResolveDisposition(
        IReadOnlyList<ApplicationCandidate> candidates)
    {
        if (candidates.Count ==
            0)
        {
            return ResolutionDisposition.NotFound;
        }


        ApplicationCandidate first =
            candidates[0];


        if (candidates.Count ==
            1)
        {
            return first.Score >=
                   690
                ? ResolutionDisposition.Resolved
                : ResolutionDisposition.Ambiguous;
        }


        int margin =
            first.Score -
            candidates[1].Score;


        return first.Score >=
                   690
               &&
               margin >=
                   90
            ? ResolutionDisposition.Resolved
            : ResolutionDisposition.Ambiguous;
    }


    private static string BuildOutput(
        string query,
        ResolutionDisposition disposition,
        IReadOnlyList<ApplicationCandidate> candidates)
    {
        StringBuilder output =
            new();


        output.AppendLine(
            $"ResolutionStatus={disposition}");


        output.AppendLine(
            $"Query={SingleLine(query)}");


        output.AppendLine(
            $"CandidateCount={candidates.Count}");


        if (disposition ==
            ResolutionDisposition.NotFound)
        {
            output.AppendLine(
                "DiscoveryCoverage=PATH;WindowsAppPaths;StartMenuShortcuts;InstalledProgramRegistry");

            output.AppendLine(
                "NotFoundMeaning=No launch target was discovered through the registered Windows application sources checked by this capability. Do not invent an executable path.");

            return output
                .ToString()
                .TrimEnd();
        }


        for (
            int index = 0;
            index < candidates.Count;
            index++)
        {
            ApplicationCandidate candidate =
                candidates[index];


            output.AppendLine(
                $"Candidate[{index}].DisplayName={SingleLine(candidate.DisplayName)}");

            output.AppendLine(
                $"Candidate[{index}].ExecutablePath={candidate.ExecutablePath}");

            output.AppendLine(
                $"Candidate[{index}].Arguments={SingleLine(candidate.Arguments)}");

            output.AppendLine(
                $"Candidate[{index}].WorkingDirectory={candidate.WorkingDirectory}");

            output.AppendLine(
                $"Candidate[{index}].Source={candidate.Source}");

            output.AppendLine(
                $"Candidate[{index}].Match={candidate.Match}");

            output.AppendLine(
                $"Candidate[{index}].Score={candidate.Score}");
        }


        if (disposition ==
            ResolutionDisposition.Resolved)
        {
            output.AppendLine(
                "NextAction=Use process.start with Candidate[0].ExecutablePath, Candidate[0].Arguments, and Candidate[0].WorkingDirectory when the user's objective is to launch this application.");
        }
        else
        {
            output.AppendLine(
                "NextAction=Use current context to choose only when one candidate is clearly intended; otherwise ask the user to disambiguate. Do not guess an executable path.");
        }


        return output
            .ToString()
            .TrimEnd();
    }


    // =========================================================
    // START MENU COM INTEROP
    // =========================================================

    private static ShortcutTarget? TryResolveShortcut(
        string shortcutPath)
    {
        object? shellObject =
            null;


        try
        {
            shellObject =
                new ShellLinkComObject();


            IShellLinkW shellLink =
                (IShellLinkW)shellObject;


            IPersistFile persistFile =
                (IPersistFile)shellLink;


            persistFile.Load(
                shortcutPath,
                0);


            StringBuilder path =
                new(32768);


            shellLink.GetPath(
                path,
                path.Capacity,
                IntPtr.Zero,
                0);


            StringBuilder arguments =
                new(8192);


            shellLink.GetArguments(
                arguments,
                arguments.Capacity);


            StringBuilder workingDirectory =
                new(32768);


            shellLink.GetWorkingDirectory(
                workingDirectory,
                workingDirectory.Capacity);


            string? executable =
                CleanExecutablePath(
                    path.ToString());


            if (executable ==
                null)
            {
                return null;
            }


            return new ShortcutTarget(
                executable,
                arguments.ToString(),
                workingDirectory.ToString());
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shellObject !=
                    null
                &&
                Marshal.IsComObject(
                    shellObject))
            {
                try
                {
                    Marshal.FinalReleaseComObject(
                        shellObject);
                }
                catch
                {
                }
            }
        }
    }


    // =========================================================
    // REGISTRY HELPERS
    // =========================================================

    private static IEnumerable<RegistryLocation> RegistryLocations(
        string subKey)
    {
        yield return new RegistryLocation(
            RegistryHive.CurrentUser,
            RegistryView.Registry64,
            subKey);

        yield return new RegistryLocation(
            RegistryHive.CurrentUser,
            RegistryView.Registry32,
            subKey);

        yield return new RegistryLocation(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            subKey);

        yield return new RegistryLocation(
            RegistryHive.LocalMachine,
            RegistryView.Registry32,
            subKey);
    }


    private static string? ReadRegistryText(
        RegistryKey key,
        string valueName)
    {
        try
        {
            return key.GetValue(
                    valueName)
                as string;
        }
        catch
        {
            return null;
        }
    }


    // =========================================================
    // PATH / NAME HELPERS
    // =========================================================

    private static string? CleanDisplayIconExecutable(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        string expanded =
            Environment.ExpandEnvironmentVariables(
                    value)
                .Trim();


        string path;


        if (expanded.StartsWith('"'))
        {
            int closing =
                expanded.IndexOf(
                    '"',
                    1);


            if (closing <=
                1)
            {
                return null;
            }


            path =
                expanded[1..closing];
        }
        else
        {
            int comma =
                expanded.LastIndexOf(',');


            path =
                comma >
                    1
                    ? expanded[..comma]
                    : expanded;
        }


        return CleanExecutablePath(
            path);
    }


    private static string? CleanExecutablePath(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return null;
        }


        string expanded =
            Environment.ExpandEnvironmentVariables(
                    value)
                .Trim()
                .Trim('"');


        if (expanded.Length ==
            0)
        {
            return null;
        }


        try
        {
            return Path.IsPathFullyQualified(
                    expanded)
                ? Path.GetFullPath(
                    expanded)
                : expanded;
        }
        catch
        {
            return null;
        }
    }


    private static bool IsLaunchableExecutable(
        string path)
    {
        string extension =
            Path.GetExtension(
                    path)
                .ToLowerInvariant();


        return extension is
            ".exe" or ".com";
    }


    private static string NormalizeExistingDirectory(
        string? workingDirectory,
        string executablePath)
    {
        string? value =
            string.IsNullOrWhiteSpace(
                    workingDirectory)
                ? Path.GetDirectoryName(
                    executablePath)
                : Environment.ExpandEnvironmentVariables(
                        workingDirectory)
                    .Trim()
                    .Trim('"');


        if (string.IsNullOrWhiteSpace(
                value)
            ||
            !Directory.Exists(
                value))
        {
            return Path.GetDirectoryName(
                       executablePath)
                   ?? string.Empty;
        }


        try
        {
            return Path.GetFullPath(
                value);
        }
        catch
        {
            return Path.GetDirectoryName(
                       executablePath)
                   ?? string.Empty;
        }
    }


    private static bool LooksLikeSupportExecutable(
        string executablePath)
    {
        string name =
            NormalizeText(
                Path.GetFileNameWithoutExtension(
                    executablePath));


        string[] supportTerms =
        {
            "unins",
            "uninstall",
            "setup",
            "installer",
            "update",
            "updater",
            "crash",
            "report",
            "helper",
            "service",
            "repair"
        };


        return supportTerms.Any(
            term =>
                name.Contains(
                    term,
                    StringComparison.Ordinal));
    }


    private static int ExecutablePreferenceBonus(
        string executablePath,
        string displayName)
    {
        string executable =
            NormalizeText(
                Path.GetFileNameWithoutExtension(
                    executablePath));


        string display =
            NormalizeText(
                displayName);


        if (executable ==
            display)
        {
            return 100;
        }


        if (display.Contains(
                executable,
                StringComparison.Ordinal)
            ||
            executable.Contains(
                display,
                StringComparison.Ordinal))
        {
            return 55;
        }


        return 0;
    }


    private static void AddSpecialFolder(
        ISet<string> values,
        Environment.SpecialFolder folder)
    {
        try
        {
            string path =
                Environment.GetFolderPath(
                    folder);


            if (!string.IsNullOrWhiteSpace(
                    path))
            {
                values.Add(
                    path);
            }
        }
        catch
        {
        }
    }


    private static void AddAlias(
        ISet<string> aliases,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(
                value))
        {
            aliases.Add(
                value.Trim());
        }
    }


    private static string FirstUseful(
        params string?[] values)
    {
        return values
                   .FirstOrDefault(
                       value =>
                           !string.IsNullOrWhiteSpace(
                               value))
                   ?.Trim()
               ?? "Application";
    }


    private static string SingleLine(
        string? value)
    {
        return (value ?? string.Empty)
            .Replace(
                "\r",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\n",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\t",
                " ",
                StringComparison.Ordinal)
            .Trim();
    }


    private static string NormalizeText(
        string value)
    {
        StringBuilder text =
            new();


        bool previousSpace =
            false;


        foreach (
            char character
            in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(
                    character))
            {
                text.Append(
                    character);

                previousSpace =
                    false;
            }
            else if (!previousSpace)
            {
                text.Append(' ');

                previousSpace =
                    true;
            }
        }


        return text
            .ToString()
            .Trim();
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name =
                name,

            Type =
                type,

            Required =
                required,

            Description =
                description
        };
    }


    // =========================================================
    // CANDIDATE COLLECTOR
    // =========================================================

    private sealed class CandidateCollector
    {
        private readonly string
            _query;


        private readonly string
            _normalizedQuery;


        private readonly string[]
            _queryTokens;


        private readonly Dictionary<string, ApplicationCandidate>
            _candidates =
                new(
                    StringComparer.OrdinalIgnoreCase);


        public CandidateCollector(
            string query)
        {
            _query =
                query;


            _normalizedQuery =
                NormalizeText(
                    query);


            _queryTokens =
                Tokens(
                    _normalizedQuery);
        }


        public void Add(
            ApplicationCandidate candidate)
        {
            string key =
                $"{candidate.ExecutablePath}\0{candidate.Arguments}";


            if (!_candidates.TryGetValue(
                    key,
                    out ApplicationCandidate? existing)
                ||
                candidate.Score >
                existing.Score)
            {
                _candidates[key] =
                    candidate;
            }
        }


        public IReadOnlyList<ApplicationCandidate> GetRanked()
        {
            return _candidates.Values
                .OrderByDescending(
                    candidate =>
                        candidate.Score)
                .ThenBy(
                    candidate =>
                        candidate.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    candidate =>
                        candidate.ExecutablePath,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }


        public int ScoreAlias(
            string? alias)
        {
            if (string.IsNullOrWhiteSpace(
                    alias)
                ||
                _normalizedQuery.Length ==
                    0)
            {
                return 0;
            }


            string normalizedAlias =
                NormalizeText(
                    alias);


            if (normalizedAlias.Length ==
                0)
            {
                return 0;
            }


            if (normalizedAlias ==
                _normalizedQuery)
            {
                return 1000;
            }


            string queryCompact =
                _normalizedQuery.Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal);


            string aliasCompact =
                normalizedAlias.Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal);


            if (aliasCompact ==
                queryCompact)
            {
                return 970;
            }


            string acronym =
                Acronym(
                    normalizedAlias);


            if (acronym.Length >=
                    2
                &&
                acronym ==
                queryCompact)
            {
                return 950;
            }


            if (normalizedAlias.Contains(
                    _normalizedQuery,
                    StringComparison.Ordinal))
            {
                return 880;
            }


            if (_normalizedQuery.Contains(
                    normalizedAlias,
                    StringComparison.Ordinal)
                &&
                normalizedAlias.Length >=
                    3)
            {
                return 790;
            }


            string[] aliasTokens =
                Tokens(
                    normalizedAlias);


            if (_queryTokens.Length ==
                    0
                ||
                aliasTokens.Length ==
                    0)
            {
                return 0;
            }


            if (MatchesAbbreviatedTokens(
                    _queryTokens,
                    aliasTokens))
            {
                return 910;
            }


            int overlap =
                _queryTokens
                    .Intersect(
                        aliasTokens,
                        StringComparer.Ordinal)
                    .Count();


            if (overlap ==
                0)
            {
                return 0;
            }


            bool allQueryTokens =
                overlap ==
                _queryTokens.Length;


            if (allQueryTokens)
            {
                return 720 +
                       Math.Min(
                           120,
                           overlap * 25);
            }


            double queryCoverage =
                (double)overlap /
                _queryTokens.Length;


            double aliasCoverage =
                (double)overlap /
                aliasTokens.Length;


            return (int)Math.Round(
                180 +
                queryCoverage *
                300 +
                aliasCoverage *
                180);
        }


        public string DescribeBestMatch(
            IEnumerable<string> aliases)
        {
            string? bestAlias =
                null;


            int bestScore =
                0;


            foreach (
                string alias
                in aliases)
            {
                int score =
                    ScoreAlias(
                        alias);


                if (score >
                    bestScore)
                {
                    bestScore =
                        score;

                    bestAlias =
                        alias;
                }
            }


            if (bestAlias ==
                null)
            {
                return "Weak";
            }


            string normalized =
                NormalizeText(
                    bestAlias);


            if (normalized ==
                _normalizedQuery)
            {
                return "ExactName";
            }


            if (Acronym(
                    normalized) ==
                _normalizedQuery.Replace(
                    " ",
                    string.Empty,
                    StringComparison.Ordinal))
            {
                return "Acronym";
            }


            if (bestScore >=
                850)
            {
                return "StrongName";
            }


            if (bestScore >=
                650)
            {
                return "TokenMatch";
            }


            return "PartialName";
        }


        private static bool MatchesAbbreviatedTokens(
            IReadOnlyList<string> queryTokens,
            IReadOnlyList<string> aliasTokens)
        {
            int aliasIndex =
                0;


            foreach (
                string queryToken
                in queryTokens)
            {
                bool matched =
                    false;


                for (
                    int start = aliasIndex;
                    start < aliasTokens.Count;
                    start++)
                {
                    if (string.Equals(
                            queryToken,
                            aliasTokens[start],
                            StringComparison.Ordinal))
                    {
                        aliasIndex =
                            start + 1;

                        matched =
                            true;

                        break;
                    }


                    if (queryToken.Length is >= 2 and <= 5)
                    {
                        StringBuilder initials =
                            new();


                        for (
                            int end = start;
                            end < aliasTokens.Count &&
                            end < start + queryToken.Length;
                            end++)
                        {
                            if (aliasTokens[end].Length ==
                                0)
                            {
                                continue;
                            }


                            initials.Append(
                                aliasTokens[end][0]);


                            if (initials.Length >=
                                    2
                                &&
                                string.Equals(
                                    queryToken,
                                    initials.ToString(),
                                    StringComparison.Ordinal))
                            {
                                aliasIndex =
                                    end + 1;

                                matched =
                                    true;

                                break;
                            }
                        }
                    }


                    if (matched)
                    {
                        break;
                    }
                }


                if (!matched)
                {
                    return false;
                }
            }


            return true;
        }


        private static string[] Tokens(
            string value)
        {
            return value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        }


        private static string Acronym(
            string normalized)
        {
            string[] tokens =
                Tokens(
                    normalized);


            if (tokens.Length <
                2)
            {
                return string.Empty;
            }


            StringBuilder acronym =
                new();


            foreach (
                string token
                in tokens)
            {
                if (token.Length >
                    0)
                {
                    acronym.Append(
                        token[0]);
                }
            }


            return acronym
                .ToString();
        }
    }


    // =========================================================
    // DATA
    // =========================================================

    private enum ResolutionDisposition
    {
        NotFound,

        Ambiguous,

        Resolved
    }


    private sealed record ApplicationCandidate(
        string DisplayName,
        string ExecutablePath,
        string Arguments,
        string WorkingDirectory,
        string Source,
        string Match,
        int Score);


    private sealed record ShortcutTarget(
        string ExecutablePath,
        string Arguments,
        string WorkingDirectory);


    private sealed record RegistryLocation(
        RegistryHive Hive,
        RegistryView View,
        string SubKey);


    // =========================================================
    // SHELL LINK INTEROP
    // =========================================================

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkComObject
    {
    }


    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cch,
            IntPtr pfd,
            uint fFlags);


        void GetIDList(
            out IntPtr ppidl);


        void SetIDList(
            IntPtr pidl);


        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
            int cch);


        void SetDescription(
            [MarshalAs(UnmanagedType.LPWStr)] string pszName);


        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
            int cch);


        void SetWorkingDirectory(
            [MarshalAs(UnmanagedType.LPWStr)] string pszDir);


        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
            int cch);


        void SetArguments(
            [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);


        void GetHotkey(
            out short pwHotkey);


        void SetHotkey(
            short wHotkey);


        void GetShowCmd(
            out int piShowCmd);


        void SetShowCmd(
            int iShowCmd);


        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cch,
            out int piIcon);


        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
            int iIcon);


        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
            uint dwReserved);


        void Resolve(
            IntPtr hwnd,
            uint fFlags);


        void SetPath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}


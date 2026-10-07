/*
 * filename: ProcessCapabilityHandlers.cs
 */

using System.Diagnostics;
using System.Text;

namespace NIRAAgent.Capabilities;


public sealed class NIRAProcessListCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.ProcessList,

            Description =
                "Inspect running processes with PID, window title, working-set memory and private memory; optionally filter and sort without using a shell.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("name", "string", false, "Optional process-name substring filter."),
                    Parameter("sortBy", "string", false, "Name, Pid, WorkingSet, or PrivateMemory. Memory sorts largest first; Name/Pid sort ascending. Default Name."),
                    Parameter("maxResults", "integer", false, "Maximum returned processes after filtering/sorting. Default 100, maximum 500.")
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

        string? filter =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "name",
                256);

        string sortBy =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "sortBy",
                32)
            ?? "Name";

        int maximum =
            NIRACapabilityArguments.GetInteger(
                request,
                "maxResults",
                100,
                1,
                500);

        if (sortBy.Equals("Name", StringComparison.OrdinalIgnoreCase))
            sortBy = "Name";
        else if (sortBy.Equals("Pid", StringComparison.OrdinalIgnoreCase))
            sortBy = "Pid";
        else if (sortBy.Equals("WorkingSet", StringComparison.OrdinalIgnoreCase))
            sortBy = "WorkingSet";
        else if (sortBy.Equals("PrivateMemory", StringComparison.OrdinalIgnoreCase))
            sortBy = "PrivateMemory";
        else
            throw new ArgumentException(
                "sortBy must be Name, Pid, WorkingSet, or PrivateMemory.");

        List<ProcessSnapshot> snapshots = new();

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    string name = process.ProcessName;

                    if (!string.IsNullOrWhiteSpace(filter) &&
                        !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    snapshots.Add(
                        new ProcessSnapshot(
                            process.Id,
                            name,
                            SafeText(() => process.MainWindowTitle),
                            SafeLong(() => process.WorkingSet64),
                            SafeLong(() => process.PrivateMemorySize64)));
                }
                catch
                {
                    // Processes can exit or deny inspection while enumerating.
                    // A single inaccessible process must not invalidate the whole
                    // observation result.
                }
            }
        }

        IEnumerable<ProcessSnapshot> ordered = sortBy switch
        {
            "WorkingSet" => snapshots
                .OrderByDescending(snapshot => snapshot.WorkingSetBytes)
                .ThenBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(snapshot => snapshot.Pid),

            "PrivateMemory" => snapshots
                .OrderByDescending(snapshot => snapshot.PrivateMemoryBytes)
                .ThenBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(snapshot => snapshot.Pid),

            "Pid" => snapshots
                .OrderBy(snapshot => snapshot.Pid),

            _ => snapshots
                .OrderBy(snapshot => snapshot.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(snapshot => snapshot.Pid)
        };

        ProcessSnapshot[] selected =
            ordered
                .Take(maximum)
                .ToArray();

        StringBuilder output = new();
        output.AppendLine(
            "PID\tName\tWorkingSetBytes\tPrivateMemoryBytes\tWindow");

        foreach (ProcessSnapshot snapshot in selected)
        {
            output.AppendLine(
                $"{snapshot.Pid}\t{snapshot.Name}\t{snapshot.WorkingSetBytes}\t" +
                $"{snapshot.PrivateMemoryBytes}\t{snapshot.WindowTitle}");
        }

        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Inspected {snapshots.Count} matching running process(es); " +
                    $"returned {selected.Length}. Filter='{filter ?? "-"}'. SortBy={sortBy}.",

                Output =
                    output.ToString().TrimEnd(),

                ChangedSystemState =
                    false
            });
    }


    private static long SafeLong(
        Func<long> reader)
    {
        try
        {
            return Math.Max(0L, reader());
        }
        catch
        {
            return 0L;
        }
    }


    private static string SafeText(
        Func<string> reader)
    {
        try
        {
            return reader() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }


    private sealed record ProcessSnapshot(
        int Pid,
        string Name,
        string WindowTitle,
        long WorkingSetBytes,
        long PrivateMemoryBytes);


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRAProcessStartCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.ProcessStart,

            Description =
                "Start an executable directly without shell expansion; optionally wait and capture stdout/stderr/exit code.",

            DefaultRisk =
                NIRACapabilityRisk.Execute,

            Parameters =
                new[]
                {
                    Parameter("fileName", "string", true, "Executable name or path."),
                    Parameter("arguments", "string", false, "Command-line arguments passed to the executable."),
                    Parameter("workingDirectory", "string", false, "Optional working directory."),
                    Parameter("waitForExit", "boolean", false, "Wait for completion and capture output. Default false."),
                    Parameter("timeoutSeconds", "integer", false, "When waiting, timeout from 1-300 seconds. Default 60.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Execute;
    }


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string fileName =
            NIRACapabilityArguments.RequireString(
                request,
                "fileName",
                32760);


        string arguments =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "arguments",
                12000)
            ?? string.Empty;


        string? workingDirectory =
            NIRACapabilityArguments.GetOptionalString(
                request,
                "workingDirectory",
                32760);


        if (!string.IsNullOrWhiteSpace(
                workingDirectory))
        {
            workingDirectory =
                NIRACapabilityArguments.NormalizePath(
                    workingDirectory);


            if (!Directory.Exists(
                    workingDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Working directory does not exist: {workingDirectory}");
            }
        }


        bool waitForExit =
            NIRACapabilityArguments.GetBoolean(
                request,
                "waitForExit");


        int timeoutSeconds =
            NIRACapabilityArguments.GetInteger(
                request,
                "timeoutSeconds",
                60,
                1,
                300);


        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty
        };

        NIRACapabilityHandlerResult execution =
            await NIRACapabilityProcessRunner.RunAsync(
                startInfo,
                waitForExit,
                timeoutSeconds,
                cancellationToken);

        // Bind the returned stdout/exit code to the executable that produced it
        // without echoing arbitrary command-line arguments, which may contain
        // secrets. This makes multi-part task evidence easier to reuse safely.
        StringBuilder output =
            new();

        output.AppendLine(
            $"RequestedExecutable={fileName}");

        output.AppendLine(
            $"WaitForExit={waitForExit}");

        output.AppendLine(
            $"CompletionObserved={waitForExit}");

        if (!string.IsNullOrWhiteSpace(
                execution.Output))
        {
            output.Append(
                execution.Output.TrimEnd());
        }

        return execution with
        {
            Output =
                output.ToString().TrimEnd()
        };
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


public sealed class NIRAProcessStopCapabilityHandler
    : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.ProcessStop,

            Description =
                "Terminate a running process by PID, optionally including its process tree.",

            DefaultRisk =
                NIRACapabilityRisk.Destructive,

            Parameters =
                new[]
                {
                    Parameter("pid", "integer", true, "Target process ID."),
                    Parameter("entireProcessTree", "boolean", false, "Terminate descendants too. Default false.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Destructive;
    }


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        int pid =
            NIRACapabilityArguments.RequireInteger(
                request,
                "pid",
                1,
                int.MaxValue);


        bool entireTree =
            NIRACapabilityArguments.GetBoolean(
                request,
                "entireProcessTree");


        using Process process =
            Process.GetProcessById(
                pid);


        if (!request.Arguments.TryGetProperty("__startticks", out System.Text.Json.JsonElement startTicks)
            || process.StartTime.ToUniversalTime().Ticks != startTicks.GetInt64())
            throw new InvalidOperationException("The PID no longer identifies the process that was approved.");
        cancellationToken.ThrowIfCancellationRequested();
        string name = process.ProcessName;

        process.Kill(
            entireTree);


        await process.WaitForExitAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);


        return new NIRACapabilityHandlerResult
        {
            Summary =
                $"Terminated process PID={pid} | Name='{name}' | EntireTree={entireTree}.",

            ChangedSystemState =
                true
        };
    }


    private static NIRACapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new NIRACapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}


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
                "Inspect running processes, optionally filtered by process name.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter("name", "string", false, "Optional process-name substring filter."),
                    Parameter("maxResults", "integer", false, "Maximum returned processes. Default 100, maximum 500.")
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


        int maximum =
            NIRACapabilityArguments.GetInteger(
                request,
                "maxResults",
                100,
                1,
                500);


        Process[] processes =
            Process.GetProcesses();


        StringBuilder output =
            new();


        int count =
            0;


        foreach (
            Process process
            in processes
                .OrderBy(
                    value =>
                        SafeName(
                            value),
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    value =>
                        SafeId(
                            value)))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();


                string name =
                    process.ProcessName;


                if (!string.IsNullOrWhiteSpace(
                        filter)
                    &&
                    !name.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }


                string title;


                try
                {
                    title =
                        process.MainWindowTitle;
                }
                catch
                {
                    title =
                        string.Empty;
                }


                output.AppendLine(
                    $"PID={process.Id}\tName={name}\tWindow={title}");


                count++;


                if (count >=
                    maximum)
                {
                    break;
                }
            }
            catch
            {
                // A process may terminate or deny inspection between
                // enumeration and field access. Skip it.
            }
            finally
            {
                process.Dispose();
            }
        }


        return Task.FromResult(
            new NIRACapabilityHandlerResult
            {
                Summary =
                    $"Inspected {count} running process(es). Filter='{filter ?? "-"}'.",

                Output =
                    output.ToString().TrimEnd(),

                ChangedSystemState =
                    false
            });
    }


    private static string SafeName(
        Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }


    private static int SafeId(
        Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return int.MaxValue;
        }
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
        return await NIRACapabilityProcessRunner.RunAsync(startInfo, waitForExit, timeoutSeconds, cancellationToken);
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


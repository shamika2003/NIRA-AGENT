/*
 * filename: ShellCapabilityHandler.cs
 */

using System.Diagnostics;

namespace SegaAgent.Capabilities;

public sealed class SegaShellExecuteCapabilityHandler
    : ISegaCapabilityHandler
{
    public SegaCapabilityDescriptor Descriptor
    {
        get;
    } =
        new SegaCapabilityDescriptor
        {
            Id =
                SegaCapabilityIds.ShellExecute,

            Description =
                "Run a bounded PowerShell, pwsh, or cmd command and capture exit code/stdout/stderr.",

            DefaultRisk =
                SegaCapabilityRisk.Execute,

            Parameters =
                new[]
                {
                    Parameter("command", "string", true, "Command text to execute."),
                    Parameter("shell", "string", false, "powershell, pwsh, or cmd. Default powershell."),
                    Parameter("workingDirectory", "string", false, "Optional working directory."),
                    Parameter("timeoutSeconds", "integer", false, "Timeout from 1-300 seconds. Default 60.")
                }
        };


    public SegaCapabilityRisk ResolveRisk(
        SegaCapabilityRequest request)
    {
        return SegaCapabilityRisk.Execute;
    }


    public async Task<SegaCapabilityHandlerResult> ExecuteAsync(
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string command =
            SegaCapabilityArguments.RequireString(
                request,
                "command",
                12000);


        string shell =
            SegaCapabilityArguments.GetOptionalString(
                request,
                "shell",
                32)
            ?? "powershell";


        shell =
            shell.Trim().ToLowerInvariant();


        string executable;
        string commandSwitch;


        switch (shell)
        {
            case "powershell":
                executable =
                    "powershell.exe";
                commandSwitch =
                    "-Command";
                break;

            case "pwsh":
                executable =
                    "pwsh.exe";
                commandSwitch =
                    "-Command";
                break;

            case "cmd":
                executable =
                    "cmd.exe";
                commandSwitch =
                    "/C";
                break;

            default:
                throw new InvalidOperationException(
                    "shell.execute shell must be powershell, pwsh, or cmd.");
        }


        string? workingDirectory =
            SegaCapabilityArguments.GetOptionalString(
                request,
                "workingDirectory",
                32760);


        if (!string.IsNullOrWhiteSpace(
                workingDirectory))
        {
            workingDirectory =
                SegaCapabilityArguments.NormalizePath(
                    workingDirectory);


            if (!Directory.Exists(
                    workingDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Working directory does not exist: {workingDirectory}");
            }
        }


        int timeoutSeconds =
            SegaCapabilityArguments.GetInteger(
                request,
                "timeoutSeconds",
                60,
                1,
                300);


        // The request policy resolves the executable before asking for consent.
        executable = SegaCapabilityArguments.RequireString(request, "__executable", 32760);
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty
        };
        if (shell is "powershell" or "pwsh")
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            command = "$ErrorActionPreference = 'Stop'\n$global:LASTEXITCODE = 0\n" + command +
                "\nif ($null -ne $LASTEXITCODE) { exit $LASTEXITCODE }";
        }
        else startInfo.ArgumentList.Add("/D");
        startInfo.ArgumentList.Add(commandSwitch);
        startInfo.ArgumentList.Add(command);
        return await SegaCapabilityProcessRunner.RunAsync(startInfo, true, timeoutSeconds, cancellationToken);
    }


    private static SegaCapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description)
    {
        return new SegaCapabilityParameterDescriptor
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Nira.Core;

namespace Nira.Infrastructure;

public sealed record ApplicationCandidate(string ExecutablePath, string Fingerprint, string Source);

public interface IApplicationDesktopBackend
{
    IReadOnlyList<ApplicationCandidate> Find(string executable, CancellationToken ct);
    ToolReceipt Launch(string executablePath, string fingerprint, CancellationToken ct);
}

public sealed class ApplicationFindTool(IApplicationDesktopBackend backend) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.application_find",
        "Find an installed desktop executable by exact basename (e.g. notepad or code.exe), using Windows App Paths, local PATH and system directory. Returns observed launch candidates; no execution. For friendly product names use system.inspect first.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"executable":{"type":"string","minLength":1,"maxLength":80}},"required":["executable"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => "local executable discovery: " + NormalizeExecutable(args.GetProperty("executable").GetString()!);
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        string executable = NormalizeExecutable(args.GetProperty("executable").GetString()!);
        return Task.Run(() => {
            var candidates = backend.Find(executable, ct).Take(16).ToArray();
            return ToolReceipt.Ok(candidates.Length == 0 ? "No executable candidate observed" : "Observed executable candidates (not launched)",
                new { executable, candidates, count = candidates.Length, limitations = "Only exact executable basenames in bounded local App Paths, PATH and system directory are checked. No match does not prove absence. A candidate is not proof of launchability or publisher authenticity." });
        }, ct);
    }
    public static string NormalizeExecutable(string executable)
    {
        if (executable.Length is < 1 or > 80 || executable is "." or ".." ||
            !executable.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+'))
            throw new ArgumentException("Use an exact executable basename, not a path, command line or wildcard.");
        return executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? executable : executable + ".exe";
    }
}

public sealed class ApplicationLaunchTool(IApplicationDesktopBackend backend) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.application_launch",
        "Launch exactly one executable previously observed by desktop.application_find in this task. Requires explicit host approval for the exact path. No arguments, shell, elevation or document opening. A started process does not establish that an application window is ready; list windows to verify UI outcomes.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{"executablePath":{"type":"string","minLength":4,"maxLength":1024},"fingerprint":{"type":"string","minLength":64,"maxLength":64}},"required":["executablePath","fingerprint"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => args.GetProperty("executablePath").GetString()!;
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.Run(() =>
        backend.Launch(args.GetProperty("executablePath").GetString()!, args.GetProperty("fingerprint").GetString()!, ct), ct);
}

/// <summary>Exact executable discovery and separately authorized process start. No shell or arbitrary command line.</summary>
public sealed class WindowsApplicationDesktopBackend : IApplicationDesktopBackend
{
    public IReadOnlyList<ApplicationCandidate> Find(string executable, CancellationToken ct)
    {
        executable = ApplicationFindTool.NormalizeExecutable(executable);
        var candidates = new Dictionary<string, ApplicationCandidate>(StringComparer.OrdinalIgnoreCase);
        void Add(string? path, string source)
        {
            ct.ThrowIfCancellationRequested();
            if (candidates.Count >= 16 || path is null) return;
            var candidate = Observe(path, source, ct);
            if (candidate is not null && Path.GetFileName(candidate.ExecutablePath).Equals(executable, StringComparison.OrdinalIgnoreCase))
                candidates.TryAdd(candidate.ExecutablePath, candidate);
        }
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, view);
                using var app = key.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + executable, writable: false);
                Add(app?.GetValue(null) as string, "Windows App Paths");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        string[] paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in paths.Take(128).Prepend(Environment.SystemDirectory))
        {
            ct.ThrowIfCancellationRequested();
            if (candidates.Count >= 16) break;
            try
            {
                string path = raw.Trim().Trim('"');
                if (!IsLocalAbsolute(path)) continue;
                Add(Path.Combine(path, executable), "Local PATH or system directory");
            }
            catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException) { }
        }
        return candidates.Values.ToArray();
    }

    private static bool IsLocalAbsolute(string path) => path.Length is >= 3 and <= 1024 &&
        char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is ('\\' or '/') &&
        Path.IsPathFullyQualified(path) && !path.Any(char.IsControl) && path.IndexOf(':', 2) < 0;

    // A fingerprint binds path and observed file content. No execution during discovery.
    // Revalidate after approval; this is not a handle-based sandbox or an OS-wide race lock.
    private static ApplicationCandidate? Observe(string path, string source, CancellationToken ct)
    {
        try
        {
            path = path.Trim().Trim('"');
            if (!IsLocalAbsolute(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            path = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(path);
            if (root is null || new DriveInfo(root).DriveType != DriveType.Fixed) return null;
            for (string? item = path; item is not null; item = Path.GetDirectoryName(item))
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) return null;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 2 or > 134217728) return null;
            ct.ThrowIfCancellationRequested();
            // Metadata plus file contents; streaming prevents large allocation.
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') return null;
            stream.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(path.ToUpperInvariant() + "\0"));
            byte[] buffer = new byte[65536];
            int count;
            long total = 0;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                total += count;
                if (total > 134217728) return null;
                hash.AppendData(buffer, 0, count);
            }
            return new ApplicationCandidate(path, Convert.ToHexString(hash.GetHashAndReset()), source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) { return null; }
    }

    public ToolReceipt Launch(string executablePath, string fingerprint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var current = Observe(executablePath, "Revalidated executable", ct);
        if (current is null || !string.Equals(current.ExecutablePath, executablePath, StringComparison.Ordinal) ||
            !string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal))
            return ToolReceipt.Fail("APPLICATION_STALE", "The observed executable is unavailable or changed. Nothing was started; rediscover before requesting a new approval.");
        var start = new ProcessStartInfo(current.ExecutablePath) {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(current.ExecutablePath)!
        };
        // Do not pass arbitrary provider keys from the parent environment.
        start.Environment.Clear();
        foreach (string key in new[] { "PATH", "PATHEXT", "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "SystemDrive", "COMPUTERNAME", "USERNAME", "OS", "ComSpec", "PUBLIC", "ProgramData", "HOMEDRIVE", "HOMEPATH" })
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value) start.Environment[key] = value;
        ct.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.Start(start);
            if (process is null) return ToolReceipt.Fail("APPLICATION_NOT_STARTED", "Windows did not return a started process.");
            return ToolReceipt.Ok("Windows started the approved executable; its window and resulting application state are not verified.",
                new { executablePath, pid = process.Id, processStarted = true, windowVerified = false }, changedState: true);
        }
        catch (Win32Exception) { return ToolReceipt.Fail("APPLICATION_START_FAILED", "Windows rejected the executable start. No window has been verified."); }
    }
}

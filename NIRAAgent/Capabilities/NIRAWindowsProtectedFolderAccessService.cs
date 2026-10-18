using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace NIRAAgent.Capabilities;

/// <summary>
/// OS-owned Controlled Folder Access recovery. Not a NIRA capability and not callable
/// by a model: only a failed filesystem mutation at the current user's protected
/// known folder can reach this flow. The Windows owner must explicitly consent.
/// </summary>
public sealed class NIRAWindowsProtectedFolderAccessService
{
    private Func<string, CancellationToken, Task<bool>>? _consent;
    private int _consentAttempted;

    public void SetConsentHandler(Func<string, CancellationToken, Task<bool>> consent) =>
        _consent = consent ?? throw new ArgumentNullException(nameof(consent));

    public static bool IsInCurrentUserProtectedFolder(string target)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(target)) return false;
        foreach (Environment.SpecialFolder folder in new[] {
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyPictures })
        {
            string root = Environment.GetFolderPath(folder);
            if (IsUnder(target, root)) return true;
            try
            {
                string name = folder == Environment.SpecialFolder.MyDocuments ? "Documents" :
                    folder == Environment.SpecialFolder.DesktopDirectory ? "Desktop" : "Pictures";
                if (IsUnder(target, NIRAFileLocationCapabilityHandler.ResolveConfiguredCurrentUserFolder(name)))
                    return true;
            }
            catch (DirectoryNotFoundException) { }
        }
        return false;
    }

    private static bool IsUnder(string target, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        string fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullTarget.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
            fullTarget.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Refuses mistaken writes into an ordinary profile folder masquerading as
    /// a Windows known folder that is actually redirected (e.g. OneDrive Documents).
    /// It does not rewrite a destination or relocate data silently.
    /// </summary>
    public static void RejectRedirectedKnownFolderShadow(string target)
    {
        if (!OperatingSystem.IsWindows()) return;
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (name, folder) in new[] {
            ("Documents", Environment.SpecialFolder.MyDocuments),
            ("Desktop", Environment.SpecialFolder.DesktopDirectory),
            ("Pictures", Environment.SpecialFolder.MyPictures) })
        {
            string canonical;
            try { canonical = NIRAFileLocationCapabilityHandler.ResolveConfiguredCurrentUserFolder(name); }
            catch (DirectoryNotFoundException) { canonical = Environment.GetFolderPath(folder); }
            if (IsRedirectedKnownFolderShadow(target, profile, name, canonical))
                throw new InvalidOperationException(
                    $"WrongWindowsKnownFolder | Requested='{target}' | " +
                    $"The current user's actual Windows {name} location is '{canonical}'. " +
                    "Do not use the separate unredirected profile folder as evidence of completing work in the Windows known folder.");
        }
    }

    // Pure path rule used by the real known-folder guard and its regression tests.
    // Windows can return an empty known-folder path in an isolated regression
    // process. Absence of a mapping is not proof of a redirected location.
    internal static bool IsRedirectedKnownFolderShadow(
        string target, string userProfile, string folderName, string knownFolderPath)
    {
        if (string.IsNullOrWhiteSpace(target) ||
            string.IsNullOrWhiteSpace(userProfile) ||
            string.IsNullOrWhiteSpace(folderName) ||
            string.IsNullOrWhiteSpace(knownFolderPath)) return false;

        string legacy = Path.Combine(userProfile, folderName);
        return !Path.GetFullPath(legacy).Equals(
                   Path.GetFullPath(knownFolderPath), StringComparison.OrdinalIgnoreCase) &&
               IsUnder(target, legacy);
    }

    public async Task<bool> TryRecoverAsync(string target, Exception failure, CancellationToken token)
    {
        if (_consent is null || !OperatingSystem.IsWindows() ||
            !IsInCurrentUserProtectedFolder(target) ||
            failure is not (IOException or UnauthorizedAccessException)) return false;
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) ||
            !Path.GetFileName(exe).Equals("NIRAAgent.UI.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(exe)) return false;

        // Read-only Windows Defender preflight; a generic missing path must not
        // cause an elevation dialog if CFA is not enabled.
        if (await PowerShellExitAsync("$p=Get-MpPreference -ErrorAction Stop; " +
                "if([int]$p.EnableControlledFolderAccess -eq 1){exit 0}else{exit 1}", false, token) != 0)
            return false;

        // Never request the same broad app permission repeatedly in one session.
        if (await IsAllowedAsync(exe, token)) return false; // unrelated OS failure
        if (Interlocked.Exchange(ref _consentAttempted, 1) != 0) return false;

        const string details =
            "Windows Controlled folder access appears to be blocking a file operation.\n\n" +
            "Allow the current NIRA app to modify Windows protected folders?\n\n" +
            "This is a PERSISTENT, APP-WIDE Defender allowance, not just permission for this one file. " +
            "Only approve if you trust this version of NIRA. Windows will show its own administrator approval screen. " +
            "You can revoke the allowance later in Windows Security.\n\n" +
            "No approval is needed for ordinary unprotected files.";
        bool consent;
        try { consent = await _consent(details, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
        if (!consent) return false;

        string escaped = exe.Replace("'", "''", StringComparison.Ordinal);
        string script = "$ErrorActionPreference='Stop'; " +
            $"Add-MpPreference -ControlledFolderAccessAllowedApplications '{escaped}' -ErrorAction Stop; exit 0";
        int outcome = await PowerShellExitAsync(script, true, token).ConfigureAwait(false);
        if (outcome != 0) return false;
        bool verified = await IsAllowedAsync(exe, token).ConfigureAwait(false);
        Debug.WriteLine($"[WindowsFolderAccess] CONSENT | Granted={verified} | Path={exe}");
        return verified;
    }

    private static async Task<bool> IsAllowedAsync(string exe, CancellationToken token)
    {
        string quoted = exe.Replace("'", "''", StringComparison.Ordinal);
        string script = "$a=@((Get-MpPreference -ErrorAction Stop).ControlledFolderAccessAllowedApplications); " +
            $"if($a -contains '{quoted}'){{exit 0}}else{{exit 1}}";
        return await PowerShellExitAsync(script, false, token).ConfigureAwait(false) == 0;
    }

    private static async Task<int> PowerShellExitAsync(string script, bool elevate, CancellationToken token)
    {
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) return -1;
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo(powershell) {
            UseShellExecute = elevate, Verb = elevate ? "runas" : string.Empty,
            CreateNoWindow = !elevate, WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(encoded);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return -1;
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception) { return -1; } // includes declined Windows UAC
        catch (InvalidOperationException) { return -1; }
    }
}
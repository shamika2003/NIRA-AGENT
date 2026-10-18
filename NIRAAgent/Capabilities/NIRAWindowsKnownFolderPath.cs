using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NIRAAgent.Capabilities;

/// <summary>
/// Reads the current user's authoritative Windows shell-folder configuration.
/// Does not create folders, change registry values or invent legacy locations.
/// </summary>
internal static class NIRAWindowsKnownFolderPath
{
    private static readonly IReadOnlyDictionary<string, Guid> FolderIds =
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
            ["desktop"] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
            ["downloads"] = new("374DE290-123F-4565-9164-39C4925E467B"),
            ["pictures"] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
            ["music"] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
            ["videos"] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC")
        };

    private static readonly IReadOnlyDictionary<string, string> RegistryNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["documents"] = "Personal",
            ["desktop"] = "Desktop",
            ["downloads"] = "{374DE290-123F-4565-9164-39C4925E467B}",
            ["pictures"] = "My Pictures",
            ["music"] = "My Music",
            ["videos"] = "My Video"
        };

    internal static string? GetConfiguredPath(string folder)
    {
        if (!OperatingSystem.IsWindows() || !FolderIds.TryGetValue(folder, out Guid id))
            return null;
        // KF_FLAG_DONT_VERIFY: the shell may know the correct redirected path
        // even when the provider directory is temporarily absent.
        IntPtr ptr = IntPtr.Zero;
        try
        {
            int status = SHGetKnownFolderPath(in id, 0x00004000, IntPtr.Zero, out ptr);
            if (status == 0)
            {
                string? candidate = Validate(Marshal.PtrToStringUni(ptr));
                if (candidate is not null) return candidate;
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }

        // Fallback is the CURRENT USER'S registry shell mapping, never
        // C:\Users\Public or a guessed OneDrive/legacy path.
        try
        {
            using RegistryKey? userShell = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (RegistryNames.TryGetValue(folder, out string? name))
                return Validate(userShell?.GetValue(name) as string);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static string? Validate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        string expanded = Environment.ExpandEnvironmentVariables(candidate.Trim());
        return Path.IsPathFullyQualified(expanded) ? Path.GetFullPath(expanded) : null;
    }

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}

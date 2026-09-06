using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SegaAgent.Capabilities;

namespace SegaAgent.Authorization;

// The checked, canonical request is the request the handler executes.
// Folder permissions govern direct primitives, not the effects of arbitrary
// programs. Execution therefore requires a separate, exact-request approval.
public sealed class SegaCapabilityRequestPolicy
{
    private readonly SegaAuthorityStore _store;
    public SegaCapabilityRequestPolicy(SegaAuthorityStore store) => _store = store;
    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public SegaCapabilityRequest Prepare(SegaCapabilityRequest request)
    {
        Dictionary<string, JsonElement> args = ReadObject(request.Arguments);
        foreach (string key in new[] { "path", "source", "destination", "workingdirectory" })
            if (args.TryGetValue(key, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
                args[key] = JsonSerializer.SerializeToElement(NormalizeLocalPath(RequireText(value, key)));

        if (request.CapabilityId is SegaCapabilityIds.ProcessStart or SegaCapabilityIds.ShellExecute)
        {
            if (!args.TryGetValue("workingdirectory", out JsonElement cwd) || cwd.ValueKind == JsonValueKind.Null)
                args["workingdirectory"] = JsonSerializer.SerializeToElement(NormalizeLocalPath(Environment.CurrentDirectory));
            if (request.CapabilityId == SegaCapabilityIds.ProcessStart)
            {
                args["filename"] = JsonSerializer.SerializeToElement(ResolveExecutable(
                    SegaCapabilityArguments.RequireString(request, "fileName", 32760)));
                args.TryAdd("arguments", JsonSerializer.SerializeToElement(""));
            }
            else
            {
                string shell = (SegaCapabilityArguments.GetOptionalString(request, "shell", 32) ?? "powershell")
                    .ToLowerInvariant();
                args["shell"] = JsonSerializer.SerializeToElement(shell);
                args["__executable"] = JsonSerializer.SerializeToElement(ResolveShellExecutable(shell));
            }
        }

        if (request.CapabilityId is SegaCapabilityIds.HttpRequest or SegaCapabilityIds.HttpDownload)
        {
            Uri uri = ParseUri(SegaCapabilityArguments.RequireString(request, "url", 4096));
            args["url"] = JsonSerializer.SerializeToElement(uri.AbsoluteUri);
            string method = (SegaCapabilityArguments.GetOptionalString(request, "method", 16) ?? "GET").ToUpperInvariant();
            if (method is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE"))
                throw new InvalidOperationException("Unsupported HTTP method.");
            if (request.CapabilityId == SegaCapabilityIds.HttpRequest)
                args["method"] = JsonSerializer.SerializeToElement(method);
            if (args.TryGetValue("headers", out JsonElement headers) && headers.ValueKind != JsonValueKind.Null)
            {
                Dictionary<string, JsonElement> normalized = ReadObject(headers);
                foreach ((string name, JsonElement value) in normalized)
                {
                    string text = RequireText(value, name);
                    if (name.IndexOfAny(['\r', '\n']) >= 0 || text.IndexOfAny(['\r', '\n']) >= 0)
                        throw new InvalidOperationException("HTTP headers cannot contain newlines.");
                    if (name is "host" or "content-length" or "transfer-encoding" or "connection")
                        throw new InvalidOperationException($"HTTP header '{name}' is controlled by the runtime.");
                }
                args["headers"] = JsonSerializer.SerializeToElement(normalized);
            }
        }

        if (request.CapabilityId == SegaCapabilityIds.ProcessStop)
        {
            int pid = SegaCapabilityArguments.RequireInteger(request, "pid", 1, int.MaxValue);
            if (pid == Environment.ProcessId) throw new InvalidOperationException("Sega cannot terminate its own authorization host.");
            using Process process = Process.GetProcessById(pid);
            args["__startticks"] = JsonSerializer.SerializeToElement(process.StartTime.ToUniversalTime().Ticks);
        }
        if (request.CapabilityId == SegaCapabilityIds.DirectoryList)
        {
            string pattern = SegaCapabilityArguments.GetOptionalString(request, "pattern", 256) ?? "*";
            if (pattern.Contains('/') || pattern.Contains('\\') || pattern.Contains(':'))
                throw new InvalidOperationException("Directory patterns must be filenames, not paths.");
        }
        return request with { Arguments = JsonSerializer.SerializeToElement(args) };
    }

    public SegaAuthorityOperation Describe(SegaCapabilityRequest request, SegaCapabilityRisk risk)
    {
        List<string> paths = [];
        foreach (string name in new[] { "path", "source", "destination" })
        {
            string? path = SegaCapabilityArguments.GetOptionalString(request, name, 32760);
            if (path != null) paths.Add(path);
        }
        bool sensitive = false;
        foreach (string path in paths)
        {
            ValidatePath(path);
            if (IsWithin(path, _store.ProtectedDirectory) ||
                (request.CapabilityId == SegaCapabilityIds.FileDelete && IsWithin(_store.ProtectedDirectory, path)))
                throw new UnauthorizedAccessException("The authority store is managed only by Permissions.");
            sensitive |= IsCredentialPath(path);
        }
        if (request.CapabilityId == SegaCapabilityIds.FileDelete && paths.Count == 1 &&
            SegaCapabilityArguments.GetBoolean(request, "recursive") && Directory.Exists(paths[0]))
        {
            foreach (string child in EnumerateCheckedTree(paths[0]))
                sensitive |= IsCredentialPath(child);
        }
        string? cwd = SegaCapabilityArguments.GetOptionalString(request, "workingDirectory", 32760);
        if (cwd != null) ValidatePath(cwd);
        foreach (string name in new[] { "fileName", "__executable" })
        {
            string? executable = SegaCapabilityArguments.GetOptionalString(request, name, 32760);
            if (executable != null) ValidateExecutionPath(executable);
        }
        string origin = "";
        string method = "";
        string target = string.Join(" -> ", paths);
        if (request.CapabilityId is SegaCapabilityIds.HttpRequest or SegaCapabilityIds.HttpDownload)
        {
            Uri uri = ParseUri(SegaCapabilityArguments.RequireString(request, "url", 4096));
            origin = uri.GetLeftPart(UriPartial.Authority);
            method = SegaCapabilityArguments.GetOptionalString(request, "method", 16) ?? "GET";
            target = $"{method} {origin}" +
                (paths.Count == 0 ? "" : $" -> {string.Join(" -> ", paths)}");
            JsonElement? headers = SegaCapabilityArguments.GetOptionalObject(request, "headers");
            sensitive |= headers.HasValue && headers.Value.EnumerateObject().Any(h => IsCredentialHeader(h.Name));
            sensitive |= uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Uri.UnescapeDataString(part.Split('=')[0]).ToLowerInvariant())
                .Any(name => name is "token" or "access_token" or "api_key" or "apikey" or "key" or "password" or "secret" or "signature" or "sig");
        }
        if (request.CapabilityId == SegaCapabilityIds.ProcessStart)
            target = $"{SegaCapabilityArguments.RequireString(request, "fileName")} | cwd={cwd}";
        if (request.CapabilityId == SegaCapabilityIds.ShellExecute)
            target = $"{SegaCapabilityArguments.RequireString(request, "shell")} | cwd={cwd}";
        if (request.CapabilityId == SegaCapabilityIds.ProcessStop)
            target = $"PID={SegaCapabilityArguments.RequireInteger(request, "pid", 1, int.MaxValue)}";

        bool externalVision =
            request.CapabilityId == SegaCapabilityIds.VisionInspect;

        if (externalVision)
        {
            string evidenceId =
                SegaCapabilityArguments.RequireString(
                    request,
                    "evidenceId",
                    80);

            target =
                $"Ollama cloud visual interpretation | EvidenceId={evidenceId}";
        }

        if (target.Length == 0) target = request.CapabilityId;
        bool execution = request.CapabilityId is SegaCapabilityIds.ProcessStart or SegaCapabilityIds.ShellExecute;
        string notice = sensitive
            ? "This request may access credentials or send private data. Approval applies once; its payload is not written to the audit log."
            : externalVision
                ? "This sends the grounded screenshot pixels to Sega's configured Ollama cloud vision model for interpretation. Remembering this permission allows future vision.inspect captures to be sent for visual analysis until you revoke the permission."
                : execution
                    ? "This command runs with your Windows account's access. Its working folder is not a sandbox. Remembering permits this exact request again."
                    : request.CapabilityId == SegaCapabilityIds.ProcessStop
                        ? "This stops the identified process. Approval applies once and checks that the PID still identifies the same process."
                        : "Only the listed capability and matching scope are authorized. Other operations keep their own permission checks.";
        string suggestedRoot = paths.Count == 1
            ? request.CapabilityId == SegaCapabilityIds.DirectoryCreate ? paths[0] : Path.GetDirectoryName(paths[0]) ?? ""
            : paths.Count > 1 ? Path.GetDirectoryName(paths[^1]) ?? "" : "";
        return new()
        {
            Request = request, Risk = risk, Fingerprint = Fingerprint(request),
            Paths = paths.ToArray(), Origin = origin, Method = method, Target = target,
            Sensitive = sensitive, RequiresExplicitAuthorization = externalVision,
            CanRemember = !sensitive && request.CapabilityId != SegaCapabilityIds.ProcessStop,
            SuggestedRoot = suggestedRoot, Notice = notice
        };
    }

    public void ValidatePath(string path)
    {
        string canonical = NormalizeLocalPath(path);
        if (!string.Equals(canonical, path, PathComparison))
            throw new InvalidOperationException("The canonical path changed before dispatch.");
        string root = Path.GetPathRoot(path)!;
        string current = root;
        foreach (string component in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked files and junctions require a direct canonical target, not a path through a link.");
            if ((attributes & FileAttributes.Directory) == 0 && OperatingSystem.IsWindows())
                RejectHardLink(current);
        }
    }

    // Executables are authorized as exact execution requests rather than by
    // filesystem mutation scopes. Windows servicing legitimately uses hard
    // links for some system binaries, so rejecting every multiply-linked
    // executable incorrectly blocks programs such as Notepad/PowerShell.
    // We still require a canonical direct path and reject reparse traversal.
    private static void ValidateExecutionPath(string path)
    {
        string canonical = NormalizeLocalPath(path);
        if (!string.Equals(canonical, path, PathComparison))
            throw new InvalidOperationException("The executable path changed before dispatch.");
        if (!File.Exists(canonical))
            throw new FileNotFoundException("Executable does not exist.", canonical);

        string root = Path.GetPathRoot(canonical)!;
        string current = root;
        foreach (string component in canonical[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Executable paths cannot traverse linked files or junctions.");
        }
    }

    private IEnumerable<string> EnumerateCheckedTree(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        int count = 0;
        while (pending.Count > 0)
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                if (++count > 10000) throw new InvalidOperationException("Recursive deletion is limited to 10,000 checked entries. Choose a smaller target.");
                ValidatePath(child);
                if (IsWithin(child, _store.ProtectedDirectory))
                    throw new UnauthorizedAccessException("Recursive deletion cannot include the authority store.");
                yield return child;
                if (Directory.Exists(child)) pending.Push(child);
            }
        }
    }

    public static string NormalizeLocalPath(string value)
    {
        string input = Environment.ExpandEnvironmentVariables(value.Trim());
        if (input.Length == 0 || input.Contains('\0')) throw new InvalidOperationException("A local path is required.");
        if (OperatingSystem.IsWindows())
        {
            if (input.StartsWith(@"\\") || input.StartsWith("//"))
                throw new InvalidOperationException("Device and network paths are not supported by local folder grants.");
            if (input.Contains('~'))
                throw new InvalidOperationException("Use the full path instead of a Windows short-name alias.");
            foreach (string part in input.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
                if (part is not ("." or "..") && (part.EndsWith('.') || part.EndsWith(' ')))
                    throw new InvalidOperationException("Paths cannot contain components ending in a dot or space.");
        }
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        if (full.Length > 32760 || full.Contains('%')) throw new InvalidOperationException("Invalid or unresolved filesystem path.");
        if (OperatingSystem.IsWindows() && full.IndexOf(':', 2) >= 0)
            throw new InvalidOperationException("Alternate data streams are not supported by folder grants.");
        return full;
    }

    public static bool IsWithin(string path, string root)
    {
        string p = Path.TrimEndingDirectorySeparator(path);
        string r = Path.TrimEndingDirectorySeparator(root);
        return string.Equals(p, r, PathComparison) ||
            p.StartsWith(Path.EndsInDirectorySeparator(r) ? r : r + Path.DirectorySeparatorChar, PathComparison);
    }

    public static string ResolveExecutable(string value)
    {
        if (Path.IsPathFullyQualified(value))
        {
            string full = NormalizeLocalPath(value);
            if (!File.Exists(full)) throw new FileNotFoundException("Executable does not exist.", full);
            return full;
        }
        if (value.Contains('/') || value.Contains('\\'))
            throw new InvalidOperationException("Executable paths must be fully qualified, or a name found on PATH.");
        string[] suffixes = OperatingSystem.IsWindows() && !Path.HasExtension(value) ? [".exe", ".com"] : [""];
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory.Trim('"'))) continue;
            foreach (string suffix in suffixes)
            {
                string candidate = Path.Combine(directory.Trim('"'), value + suffix);
                if (File.Exists(candidate)) return NormalizeLocalPath(candidate);
            }
        }
        throw new FileNotFoundException($"Executable '{value}' was not found on PATH. Supply its full path.");
    }

    public static string ResolveShellExecutable(string shell) => shell switch
    {
        "powershell" when OperatingSystem.IsWindows() => ResolveExecutable(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")),
        "cmd" when OperatingSystem.IsWindows() => ResolveExecutable(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")),
        "pwsh" => ResolveExecutable(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh"),
        _ => throw new InvalidOperationException("This shell is unavailable on the current operating system.")
    };

    public static Uri ParseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Use an HTTP/HTTPS URL without embedded credentials.");
        return new UriBuilder(uri) { Fragment = "" }.Uri;
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Expected a JSON object.");
        Dictionary<string, JsonElement> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in value.EnumerateObject())
            if (!result.TryAdd(property.Name.ToLowerInvariant(), property.Value.Clone()))
                throw new InvalidOperationException($"Duplicate argument '{property.Name}'.");
        return result.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value);
    }
    private static string RequireText(JsonElement value, string name) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvalidOperationException($"'{name}' must be text.");
    public static string Fingerprint(SegaCapabilityRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.BuildSignature()))).ToLowerInvariant();

    private static bool IsCredentialHeader(string name) => name.ToLowerInvariant() is
        "authorization" or "proxy-authorization" or "cookie" or "x-api-key" or "api-key" or "x-auth-token";
    private static bool IsCredentialPath(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        string file = parts[^1].ToLowerInvariant();
        return parts.Any(x => x.ToLowerInvariant() is ".ssh" or ".aws" or ".azure" or "gcloud") ||
            file is ".env" or ".git-credentials" or "credentials" or "credentials.json" or "login data" or "cookies" ||
            file.StartsWith(".env.", StringComparison.Ordinal) ||
            Path.GetExtension(file) is ".pem" or ".key" or ".pfx" or ".p12";
    }

    public static string BuildPreview(SegaCapabilityRequest request)
    {
        // This exact payload is displayed only in the trusted UI, never saved in
        // the audit trail or inserted into model context as a permission grant.
        return JsonSerializer.Serialize(request.Arguments, new JsonSerializerOptions { WriteIndented = true });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    private static void RejectHardLink(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out FileInformation info))
            throw new IOException("Unable to verify the target file identity.");
        if (info.Links > 1)
            throw new UnauthorizedAccessException("Files with multiple hard links are not accepted by direct filesystem grants.");
    }
}
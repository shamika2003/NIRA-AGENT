using System.Text.Json;
using System.Text.RegularExpressions;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.Runtime.AgentLoop;

namespace Nira.V2.Filesystem;

/// <summary>
/// A strictly scoped, read-only metadata capability. Registration is a host-owned
/// permission grant for ONE path explicitly typed in the CURRENT user turn.
/// Neither the model nor previous memories can supply or broaden that path.
/// Does not enumerate directories, read file contents, or execute anything.
/// </summary>
public static class RequestedPathObservationRegistration
{
    public const string ConsentValue = "ALLOW_REQUESTED_PATH_METADATA";
    private static readonly Regex ExplicitAlias = new(
        @"(?<![A-Za-z0-9_./\\-])(?<path>(?:Documents|Desktop|Downloads|Pictures|Music|Videos)[\\/][A-Za-z0-9_+.\-]+(?:[\\/][A-Za-z0-9_+.\-]+){0,7})(?![A-Za-z0-9_./\\-])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex QuotedAlias = new(
        "[`\"](?<path>(?:Documents|Desktop|Downloads|Pictures|Music|Videos)[\\\\/][^`\"\\r\\n]{1,180})[`\"]",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex SafeSegment = new(
        @"^[A-Za-z0-9_+ .()\-]{1,80}$", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    public static bool IsConsented(string? consent) =>
        string.Equals(consent, ConsentValue, StringComparison.Ordinal);

    /// <summary>
    /// An untrusted user request is allowed to REQUEST one path's existence only
    /// after an independent, explicit owner opt-in. No model-provided path is used.
    /// For tests, rootResolver supplies the current user's known-folder mappings.
    /// </summary>
    public static IReadOnlyList<IGeneralCapability> Create(AgentPayloadVault vault,
        string? ownerConsent, string currentUserTurn,
        IRequestedPathProbe? probe = null,
        Func<string, string?>? rootResolver = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        if (!IsConsented(ownerConsent) || string.IsNullOrWhiteSpace(currentUserTurn))
            return Array.Empty<IGeneralCapability>();
        if (!TryCreateScope(currentUserTurn, rootResolver ?? ResolveWindowsKnownFolder,
                out RequestedPathScope? scope))
            return Array.Empty<IGeneralCapability>();
        probe ??= new LocalRequestedPathProbe();
        return new IGeneralCapability[] { new RequestedPathMetadataCapability(vault, scope!, probe) };
    }

    /// <summary>Never infer a path from archived chat or memory. A user must type it
    /// in the current request. More than one distinct path is ambiguous and grants none.</summary>
    public static bool TryCreateScope(string currentUserTurn,
        Func<string, string?> rootResolver, out RequestedPathScope? result)
    {
        result = null;
        ArgumentNullException.ThrowIfNull(rootResolver);
        if (string.IsNullOrWhiteSpace(currentUserTurn) || currentUserTurn.Length > 12_000)
            return false;
        var candidates = new List<RequestedPathScope>();
        try
        {
            Match[] quoted = QuotedAlias.Matches(currentUserTurn).Cast<Match>().ToArray();
            foreach (Match match in quoted)
                if (TryParseAlias(match.Groups["path"].Value, rootResolver, out var s) &&
                    !candidates.Any(c => string.Equals(c.FullPath, s!.FullPath,
                        StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(s!);
            foreach (Match match in ExplicitAlias.Matches(currentUserTurn))
            {
                // Never turn a rejected or partial quoted path into a grant.
                if (quoted.Any(q => match.Index >= q.Index && match.Index < q.Index + q.Length))
                    continue;
                if (TryParseAlias(match.Groups["path"].Value, rootResolver, out var s) &&
                    !candidates.Any(c => string.Equals(c.FullPath, s!.FullPath,
                        StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(s!);
            }
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
        if (candidates.Count != 1) return false;
        result = candidates[0];
        return true;
    }

    private static bool TryParseAlias(string raw, Func<string, string?> resolver,
        out RequestedPathScope? scope)
    {
        scope = null;
        if (raw.Length is < 4 or > 230 || raw.Any(char.IsControl) ||
            raw.IndexOfAny(new[] { ':', '*', '?', '<', '>', '|', '%' }) >= 0)
            return false;
        string[] parts = raw.Replace('\\', '/').Split('/');
        if (parts.Length is < 2 or > 9 || parts.Any(p =>
                !SafeSegment.IsMatch(p) || p is "." or ".." ||
                p.EndsWith(' ') || p.EndsWith('.') || IsDeviceName(p)))
            return false;
        string alias = parts[0];
        if (!new[] { "Documents", "Desktop", "Downloads", "Pictures", "Music", "Videos" }
                .Contains(alias, StringComparer.OrdinalIgnoreCase)) return false;
        string? root = resolver(alias);
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return false;
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full = Path.GetFullPath(Path.Combine(new[] { canonicalRoot }.Concat(parts.Skip(1)).ToArray()));
        if (!full.StartsWith(canonicalRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) return false;
        scope = new RequestedPathScope(string.Join('/', parts), canonicalRoot, full,
            parts.Skip(1).ToArray());
        return true;
    }

    private static bool IsDeviceName(string part)
    {
        string n = part.Split('.')[0];
        return new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
            "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3",
            "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
            .Contains(n, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveWindowsKnownFolder(string alias)
    {
        if (!OperatingSystem.IsWindows()) return null;
        Environment.SpecialFolder folder = alias.ToLowerInvariant() switch
        {
            "documents" => Environment.SpecialFolder.MyDocuments,
            "desktop" => Environment.SpecialFolder.DesktopDirectory,
            "pictures" => Environment.SpecialFolder.MyPictures,
            "music" => Environment.SpecialFolder.MyMusic,
            "videos" => Environment.SpecialFolder.MyVideos,
            _ => (Environment.SpecialFolder)(-1)
        };
        if ((int)folder != -1)
        {
            string configured = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(configured) ? null : configured;
        }
        // Downloads lacks a stable SpecialFolder enum value. Resolve the actual
        // Windows current-user shell mapping, never guess a OneDrive/profile path.
        if (!alias.Equals("Downloads", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            string? configured = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") as string;
            if (string.IsNullOrWhiteSpace(configured)) return null;
            string path = Environment.ExpandEnvironmentVariables(configured);
            return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
        }
        catch (System.Security.SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

/// <summary>Absolute path is host private; the model receives only the
/// user-typed alias and boolean/shape of the actual observation.</summary>
public sealed record RequestedPathScope(string UserLabel, string KnownFolderRoot,
    string FullPath, IReadOnlyList<string> ChildSegments);

public enum RequestedPathStatus { Directory, File, Missing, UnsafeLink, Unavailable }

public interface IRequestedPathProbe
{
    RequestedPathStatus Observe(RequestedPathScope scope, CancellationToken cancellationToken);
}

public sealed class LocalRequestedPathProbe : IRequestedPathProbe
{
    public RequestedPathStatus Observe(RequestedPathScope scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // Check the anchor and every component, rejecting junction/symlink
            // traversal. Never follow a reparse point to a different location.
            FileAttributes root = File.GetAttributes(scope.KnownFolderRoot);
            if ((root & FileAttributes.ReparsePoint) != 0)
                return RequestedPathStatus.UnsafeLink;
            if ((root & FileAttributes.Directory) == 0)
                return RequestedPathStatus.Unavailable;
            string current = scope.KnownFolderRoot;
            for (int i = 0; i < scope.ChildSegments.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                current = Path.Combine(current, scope.ChildSegments[i]);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (FileNotFoundException) { return RequestedPathStatus.Missing; }
                catch (DirectoryNotFoundException) { return RequestedPathStatus.Missing; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return RequestedPathStatus.UnsafeLink;
                if (i + 1 < scope.ChildSegments.Count && (attributes & FileAttributes.Directory) == 0)
                    return RequestedPathStatus.Missing;
                if (i + 1 == scope.ChildSegments.Count)
                    return (attributes & FileAttributes.Directory) != 0
                        ? RequestedPathStatus.Directory : RequestedPathStatus.File;
            }
            return RequestedPathStatus.Unavailable;
        }
        catch (FileNotFoundException) { return RequestedPathStatus.Unavailable; }
        catch (DirectoryNotFoundException) { return RequestedPathStatus.Unavailable; }
        catch (UnauthorizedAccessException) { return RequestedPathStatus.Unavailable; }
        catch (IOException) { return RequestedPathStatus.Unavailable; }
        catch (System.Security.SecurityException) { return RequestedPathStatus.Unavailable; }
        catch (ArgumentException) { return RequestedPathStatus.Unavailable; }
    }
}

public sealed class RequestedPathMetadataCapability : IGeneralCapability
{
    private readonly AgentPayloadVault _vault;
    private readonly RequestedPathScope _scope;
    private readonly IRequestedPathProbe _probe;

    public RequestedPathMetadataCapability(AgentPayloadVault vault,
        RequestedPathScope scope, IRequestedPathProbe probe)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }
    public AgentToolSpec Spec { get; } = new("filesystem.requested_path_metadata",
        "Check whether ONE file or folder explicitly named in this CURRENT user message exists. " +
        "The host already fixed the path; this tool takes NO path arguments. " +
        "Read-only existence and file/directory classification only. No file contents, " +
        "directory listing, inherited memory permissions, or system changes.",
        AccessLevel.Observe);
    public string FunctionName => "filesystem_check_requested_path";
    public JsonElement InputSchema { get; } = JsonSerializer.SerializeToElement(new {
        type = "object", properties = new { }, required = Array.Empty<string>(),
        additionalProperties = false });
    public bool ValidateArguments(JsonElement args) => args.ValueKind == JsonValueKind.Object &&
        !args.EnumerateObject().Any();

    public async Task<AgentToolReceipt> ExecuteAsync(string reference, CancellationToken ct)
    {
        Guid runId;
        JsonElement arguments;
        try
        {
            runId = _vault.RunIdFromArgument(reference);
            arguments = await _vault.ReadToolArgumentAsync(Spec.Id, reference, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or
            JsonException or UnauthorizedAccessException)
        {
            return new AgentToolReceipt(false, false, null, "INVALID_SCOPED_ARGUMENT");
        }
        if (!ValidateArguments(arguments))
            return new AgentToolReceipt(false, false, null, "INVALID_PATH_ARGUMENTS");
        RequestedPathStatus status;
        try { status = _probe.Observe(_scope, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException)
        {
            status = RequestedPathStatus.Unavailable;
        }
        if (status is RequestedPathStatus.UnsafeLink or RequestedPathStatus.Unavailable)
            return new AgentToolReceipt(false, false, null,
                status == RequestedPathStatus.UnsafeLink ? "PATH_LINK_NOT_FOLLOWED" : "PATH_OBSERVATION_UNAVAILABLE");
        JsonElement data = JsonSerializer.SerializeToElement(new
        {
            observation = "exact_current_user_requested_path_metadata",
            requestedPath = _scope.UserLabel,
            observedUtc = DateTimeOffset.UtcNow,
            exists = status != RequestedPathStatus.Missing,
            kind = status switch {
                RequestedPathStatus.Directory => "directory",
                RequestedPathStatus.File => "file",
                _ => "missing"
            },
            confirmedBy = "host_filesystem_attribute_observation",
            excluded = "file_contents,directory_listing,other_paths,link_targets,modifications"
        });
        string token = await _vault.SaveResultAsync(runId, Spec.Id, data, ct);
        return AgentToolReceipt.ReadOnlyResult(token);
    }
}

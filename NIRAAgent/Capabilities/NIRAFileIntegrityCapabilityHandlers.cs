using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NIRAAgent.Capabilities;

// R38 / GATE 14.8: bounded, hash-evidenced file operations. All calls enter
// through the existing normalizer -> authority -> durable audit pipeline.
// Restoration intentionally creates NEW files only; no overwrite is allowed.
internal static class NIRAFileIntegrity
{
    private const long MaximumFileBytes = 1024L * 1024 * 1024;
    internal const string SidecarSuffix = ".nira-backup.json";
    internal sealed record Manifest(
        string Format, string SourcePath, string BackupPath, long Bytes,
        string Sha256, DateTimeOffset CreatedUtc);
    internal sealed record Fingerprint(string Sha256, long Bytes);

    internal static string PathArgument(NIRACapabilityRequest request, string argument)
    {
        string path = NIRACapabilityArguments.NormalizePath(
            NIRACapabilityArguments.RequireString(request, argument, 32760));
        RejectLinkedPaths(path);
        return path;
    }

    internal static void RejectLinkedPaths(string path)
    {
        string current = Path.GetPathRoot(path) ?? throw new InvalidOperationException("Expected rooted path.");
        foreach (string part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileAttributes attrs;
            try { attrs = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Linked files and junctions cannot be used for file integrity actions.");
        }
    }

    internal static void Different(string a, string b)
    {
        if (string.Equals(a, b, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Source and destination must be different files.");
    }

    internal static void RequireNewDestination(string destination)
    {
        NIRAWindowsProtectedFolderAccessService.RejectRedirectedKnownFolderShadow(destination);
        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("The destination folder must already exist. Create it using the authorized directory-create capability.");
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Destination exists; NIRA will not overwrite a file during backup or restore.");
    }

    internal static async Task<Fingerprint> HashAsync(string path, CancellationToken ct)
    {
        RejectLinkedPaths(path);
        FileInfo info = new(path);
        if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
            throw new FileNotFoundException("A regular existing file is required.", path);
        if (info.Length > MaximumFileBytes)
            throw new InvalidOperationException("File is larger than the 1 GiB integrity-operation limit.");
        long originalLength = info.Length;
        DateTime originalModified = info.LastWriteTimeUtc;
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)
            .ConfigureAwait(false)).ToLowerInvariant();
        FileInfo after = new(path);
        if (stream.Length != originalLength || after.Length != originalLength ||
            after.LastWriteTimeUtc != originalModified)
            throw new IOException("File changed while it was being fingerprinted; retry from fresh evidence.");
        return new Fingerprint(digest, stream.Length);
    }

    internal static async Task<Manifest> ReadAndVerifyAsync(string backup, CancellationToken ct)
    {
        string sidecar = backup + SidecarSuffix;
        RejectLinkedPaths(sidecar);
        FileInfo sidecarInfo = new(sidecar);
        if (!sidecarInfo.Exists || sidecarInfo.Length > 8192)
            throw new InvalidOperationException("Backup metadata is missing or invalid; cannot verify this as a NIRA backup.");
        string data = await File.ReadAllTextAsync(sidecar, ct);
        Manifest? record = JsonSerializer.Deserialize<Manifest>(data);
        if (record is null || record.Format != "NIRA-FILE-BACKUP-1" ||
            !string.Equals(record.BackupPath, backup, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            record.Bytes < 0 || record.Bytes > MaximumFileBytes ||
            string.IsNullOrWhiteSpace(record.SourcePath) ||
            string.IsNullOrWhiteSpace(record.Sha256) || record.Sha256.Length != 64 || record.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidOperationException("Invalid or mismatched NIRA backup manifest.");
        Fingerprint observed = await HashAsync(backup, ct);
        if (observed.Bytes != record.Bytes ||
            !string.Equals(observed.Sha256, record.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("BACKUP_VERIFICATION_FAILED: backup bytes do not match the recorded SHA-256. No restore performed.");
        return record;
    }

    internal static NIRACapabilityParameterDescriptor Param(string name, bool required, string description) => new()
    {
        Name = name, Type = "string", Required = required, Description = description
    };
}

public sealed class NIRAFileHashCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileHash,
        Description = "Compute SHA-256 of one existing regular file (up to 1 GiB); read-only, no file content emitted.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = [NIRAFileIntegrity.Param("path", true, "Verified path to an existing file.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string path = NIRAFileIntegrity.PathArgument(request, "path");
        var result = await NIRAFileIntegrity.HashAsync(path, cancellationToken);
        return new NIRACapabilityHandlerResult
        {
            Summary = "SHA-256 verified for the requested file.",
            Output = $"Path={path}\nBytes={result.Bytes}\nSha256={result.Sha256}\nReadComplete=True",
            ChangedSystemState = false
        };
    }
}

public sealed class NIRAFileCompareCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileCompare,
        Description = "Read-only byte equality comparison using complete SHA-256 hashes (up to 1 GiB each).",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = [NIRAFileIntegrity.Param("source", true, "First verified file path."),
            NIRAFileIntegrity.Param("destination", true, "Second verified file path; read only.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string source = NIRAFileIntegrity.PathArgument(request, "source");
        string destination = NIRAFileIntegrity.PathArgument(request, "destination");
        var left = await NIRAFileIntegrity.HashAsync(source, cancellationToken);
        var right = await NIRAFileIntegrity.HashAsync(destination, cancellationToken);
        bool equal = left.Bytes == right.Bytes && left.Sha256 == right.Sha256;
        return new NIRACapabilityHandlerResult
        {
            Summary = equal ? "Files have matching SHA-256 hashes." : "Files differ in length or SHA-256 hash.",
            Output = $"Source={source}\nDestination={destination}\nEqual={equal}\nSourceBytes={left.Bytes}\nDestinationBytes={right.Bytes}\nSourceSha256={left.Sha256}\nDestinationSha256={right.Sha256}",
            ChangedSystemState = false
        };
    }
}

public sealed class NIRAFileBackupCreateCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileBackupCreate,
        Description = "Create a new, non-overwriting exact-file backup and SHA-256 manifest. Requires separate authorized source and destination paths; no folder creation.",
        DefaultRisk = NIRACapabilityRisk.Modify,
        Parameters = [NIRAFileIntegrity.Param("source", true, "Existing regular source file."),
            NIRAFileIntegrity.Param("destination", true, "New full backup file path; do not overwrite any existing file.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string source = NIRAFileIntegrity.PathArgument(request, "source");
        string destination = NIRAFileIntegrity.PathArgument(request, "destination");
        NIRAFileIntegrity.Different(source, destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        string sidecar = destination + NIRAFileIntegrity.SidecarSuffix;
        NIRAFileIntegrity.RejectLinkedPaths(sidecar);
        NIRAFileIntegrity.RequireNewDestination(sidecar);
        var first = await NIRAFileIntegrity.HashAsync(source, cancellationToken);
        string staged = destination + ".nira-stage-" + Guid.NewGuid().ToString("N") + ".tmp";
        string stagedManifest = sidecar + ".nira-stage-" + Guid.NewGuid().ToString("N") + ".tmp";
        bool backupCommitted = false;
        try
        {
            await using (FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            var copied = await NIRAFileIntegrity.HashAsync(staged, cancellationToken);
            var after = await NIRAFileIntegrity.HashAsync(source, cancellationToken);
            if (copied != first || after != first)
                throw new IOException("Source or staged backup changed; no backup was committed.");
            var manifest = new NIRAFileIntegrity.Manifest("NIRA-FILE-BACKUP-1", source,
                destination, copied.Bytes, copied.Sha256, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(stagedManifest, JsonSerializer.Serialize(manifest), new UTF8Encoding(false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // No clobber: neither commit can replace a pre-existing path.
            File.Move(staged, destination, overwrite: false);
            backupCommitted = true;
            File.Move(stagedManifest, sidecar, overwrite: false);
            return new NIRACapabilityHandlerResult
            {
                Summary = "New backup created and SHA-256 matched the source.",
                Output = $"Source={source}\nBackupPath={destination}\nManifestPath={sidecar}\nBytes={copied.Bytes}\nSha256={copied.Sha256}\nVerified=True\nSourceUnchanged=True",
                ChangedSystemState = true
            };
        }
        catch
        {
            // A valid backup without a manifest is not a verified backup.
            // Leave it visible for inspection rather than deleting user data.
            if (backupCommitted && !File.Exists(sidecar))
                throw new IOException("Backup bytes were committed but the manifest was not; incomplete backup remains. Inspect it before any retry.");
            throw;
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
            if (File.Exists(stagedManifest)) File.Delete(stagedManifest);
        }
    }
}

public sealed class NIRAFileBackupVerifyCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileBackupVerify,
        Description = "Verify an existing NIRA backup against its sidecar manifest using SHA-256, without modifying files.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = [NIRAFileIntegrity.Param("path", true, "Exact backup file path (not the .json sidecar).")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string path = NIRAFileIntegrity.PathArgument(request, "path");
        var record = await NIRAFileIntegrity.ReadAndVerifyAsync(path, cancellationToken);
        return new NIRACapabilityHandlerResult
        {
            Summary = "NIRA backup bytes match its SHA-256 manifest.",
            Output = $"BackupPath={path}\nOriginalSource={record.SourcePath}\nBytes={record.Bytes}\nSha256={record.Sha256}\nVerified=True",
            ChangedSystemState = false
        };
    }
}

public sealed class NIRAFileBackupRestoreCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileBackupRestore,
        Description = "Recover a verified NIRA backup to a NEW explicit destination path only. Never replace or erase the original file.",
        DefaultRisk = NIRACapabilityRisk.Modify,
        Parameters = [NIRAFileIntegrity.Param("source", true, "Verified NIRA backup file path (with matching .nira-backup.json)."),
            NIRAFileIntegrity.Param("destination", true, "New recovery output path. Existing files are never overwritten.")]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string backup = NIRAFileIntegrity.PathArgument(request, "source");
        string destination = NIRAFileIntegrity.PathArgument(request, "destination");
        NIRAFileIntegrity.Different(backup, destination);
        NIRAFileIntegrity.RequireNewDestination(destination);
        var recorded = await NIRAFileIntegrity.ReadAndVerifyAsync(backup, cancellationToken);
        string temporary = destination + ".nira-recover-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream input = new(backup, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            var staged = await NIRAFileIntegrity.HashAsync(temporary, cancellationToken);
            if (staged.Bytes != recorded.Bytes || staged.Sha256 != recorded.Sha256)
                throw new IOException("Recovered bytes do not match the verified backup; no file committed.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            return new NIRACapabilityHandlerResult
            {
                Summary = "Verified backup restored to a NEW file; existing original untouched.",
                Output = $"BackupPath={backup}\nRestoredPath={destination}\nBytes={staged.Bytes}\nSha256={staged.Sha256}\nVerified=True\nOverwroteExisting=False",
                ChangedSystemState = true
            };
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

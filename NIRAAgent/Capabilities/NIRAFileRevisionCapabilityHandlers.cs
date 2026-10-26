using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NIRAAgent.Capabilities;

// R40 / GATE 14.8: bounded compare-and-swap file revisions with a durable
// SHA-256 rollback snapshot. Explicit paths remain subject to NIRA authority.
// This is NOT a multi-file transaction or protection from external races.
internal static class NIRAFileRevision
{
    internal const long MaxBytes = 16L * 1024 * 1024;
    internal const string SidecarSuffix = ".nira-revision.json";
    internal const string Format = "NIRA-FILE-REVISION-1";

    internal sealed record Manifest(
        string Format, string SourcePath, string DestinationPath, string BackupPath,
        string BeforeSha256, string AfterSha256, long BeforeBytes, long AfterBytes,
        DateTimeOffset PreparedUtc);

    internal static NIRACapabilityParameterDescriptor Parameter(string name, string description) =>
        new() { Name = name, Type = "string", Required = true, Description = description };

    internal static string GetPath(NIRACapabilityRequest request, string name) =>
        NIRAFileIntegrity.PathArgument(request, name);

    internal static string GetDigest(NIRACapabilityRequest request, string name)
    {
        string sha = NIRACapabilityArguments.RequireString(request, name, 64);
        if (sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException($"{name} must be an exact 64-character SHA-256 hash.");
        return sha.ToLowerInvariant();
    }

    internal static string Sidecar(string backup)
    {
        string path = backup + SidecarSuffix;
        NIRAFileIntegrity.RejectLinkedPaths(path);
        return path;
    }

    internal static void CheckDistinct(params string[] paths)
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (paths.Distinct(comparer).Count() != paths.Length)
            throw new InvalidOperationException("Source, destination, backup, and journal paths must be distinct.");
    }

    internal static bool SamePath(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static async Task<NIRAFileIntegrity.Fingerprint> Fingerprint(string path, CancellationToken ct)
    {
        var observed = await NIRAFileIntegrity.HashAsync(path, ct);
        if (observed.Bytes > MaxBytes)
            throw new InvalidOperationException("Revisions are limited to 16 MiB per file.");
        return observed;
    }

    internal static async Task CopyNew(string source, string destination, CancellationToken ct)
    {
        NIRAFileIntegrity.RejectLinkedPaths(source);
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
        output.Flush(flushToDisk: true);
    }

    internal static async Task<Manifest> ReadManifest(string backup, string destination, CancellationToken ct)
    {
        string journal = Sidecar(backup);
        var meta = new FileInfo(journal);
        if (!meta.Exists || meta.Length > 8192)
            throw new InvalidOperationException("NIRA revision journal is missing or oversized.");
        Manifest? record = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(journal, ct));
        if (record is null || record.Format != Format ||
            !SamePath(record.BackupPath, backup) || !SamePath(record.DestinationPath, destination) ||
            string.IsNullOrWhiteSpace(record.SourcePath) ||
            record.BeforeBytes < 0 || record.AfterBytes < 0 ||
            record.BeforeBytes > MaxBytes || record.AfterBytes > MaxBytes ||
            !ValidDigest(record.BeforeSha256) || !ValidDigest(record.AfterSha256) ||
            string.Equals(record.BeforeSha256, record.AfterSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Revision journal does not describe this backup/destination.");
        var snapshot = await Fingerprint(backup, ct);
        if (snapshot.Bytes != record.BeforeBytes ||
            !string.Equals(snapshot.Sha256, record.BeforeSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("ROLLBACK_BACKUP_INVALID: snapshot does not match its journal; no file changed.");
        return record;
    }

    internal static bool ValidDigest(string? digest) =>
        !string.IsNullOrWhiteSpace(digest) && digest.Length == 64 && digest.All(Uri.IsHexDigit);

    internal static async Task SaveManifest(Manifest record, string path, CancellationToken ct)
    {
        // The journal is published with CreateNew BEFORE atomic replacement.
        // If NIRA exits after publication, inspect can distinguish prepared,
        // applied, reverted, or externally modified destination by its hash.
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record));
        if (data.Length > 8192)
            throw new InvalidOperationException("Revision journal path metadata is too large; no replacement performed.");
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
        stream.Flush(flushToDisk: true);
    }

    internal static string NewStage(string destination) =>
        destination + ".nira-stage-" + Guid.NewGuid().ToString("N") + ".tmp";

    internal static void DeleteTransient(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    internal static void RequireExistingRegularDestination(string destination)
    {
        NIRAFileIntegrity.RejectLinkedPaths(destination);
        NIRAWindowsProtectedFolderAccessService.RejectRedirectedKnownFolderShadow(destination);
        if (!File.Exists(destination) || (File.GetAttributes(destination) & FileAttributes.Directory) != 0)
            throw new FileNotFoundException("Revision destination must be an existing regular file.", destination);
    }
}

public sealed class NIRAFileRevisionApplyCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileRevisionApply,
        Description = "Atomically replace an existing file with bytes from another existing file, only when its expected SHA-256 still matches. Creates a new immutable-by-convention snapshot and revision journal before changing the destination (16 MiB limit). Explicit approval required by ordinary NIRA policy; never use for an unverified target.",
        DefaultRisk = NIRACapabilityRisk.Modify,
        Parameters = [
            NIRAFileRevision.Parameter("source", "Existing replacement-content file, not a directory."),
            NIRAFileRevision.Parameter("destination", "Existing file to change, explicitly approved."),
            NIRAFileRevision.Parameter("backup", "New unique snapshot file path in an existing authorized folder (not overwritten)."),
            NIRAFileRevision.Parameter("expectedSha256", "Current destination's exact observed SHA-256. Obtain via filesystem.hash."),
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;

    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string source = NIRAFileRevision.GetPath(request, "source");
        string destination = NIRAFileRevision.GetPath(request, "destination");
        string backup = NIRAFileRevision.GetPath(request, "backup");
        string journal = NIRAFileRevision.Sidecar(backup);
        string expected = NIRAFileRevision.GetDigest(request, "expectedSha256");
        NIRAFileRevision.CheckDistinct(source, destination, backup, journal);
        NIRAFileRevision.RequireExistingRegularDestination(destination);
        NIRAFileIntegrity.RequireNewDestination(backup);
        NIRAFileIntegrity.RequireNewDestination(journal);

        var before = await NIRAFileRevision.Fingerprint(destination, cancellationToken);
        if (before.Sha256 != expected)
            throw new IOException("STALE_DESTINATION: current file does not match expected SHA-256; no change made.");
        var candidate = await NIRAFileRevision.Fingerprint(source, cancellationToken);
        if (candidate.Sha256 == before.Sha256)
            throw new InvalidOperationException("Source content is already identical; no revision needed.");

        string stage = NIRAFileRevision.NewStage(destination);
        bool snapshotExists = false;
        bool journalExists = false;
        bool committed = false;
        try
        {
            // Same destination filesystem -> File.Replace can be atomic.
            await NIRAFileRevision.CopyNew(source, stage, cancellationToken);
            var staged = await NIRAFileRevision.Fingerprint(stage, cancellationToken);
            var sourceAgain = await NIRAFileRevision.Fingerprint(source, cancellationToken);
            if (staged != candidate || sourceAgain != candidate)
                throw new IOException("Replacement content changed during staging; no destination change.");

            await NIRAFileRevision.CopyNew(destination, backup, cancellationToken);
            snapshotExists = true;
            var saved = await NIRAFileRevision.Fingerprint(backup, cancellationToken);
            if (saved != before)
                throw new IOException("Destination changed during snapshot; no destination change.");
            var beforeCommit = await NIRAFileRevision.Fingerprint(destination, cancellationToken);
            if (beforeCommit != before)
                throw new IOException("Destination changed before commit; refusing to replace.");

            var record = new NIRAFileRevision.Manifest(NIRAFileRevision.Format, source,
                destination, backup, before.Sha256, staged.Sha256,
                before.Bytes, staged.Bytes, DateTimeOffset.UtcNow);
            await NIRAFileRevision.SaveManifest(record, journal, cancellationToken);
            journalExists = true;
            cancellationToken.ThrowIfCancellationRequested();
            // Only File.Replace, never File.Copy(overwrite: true) for the commit.
            File.Replace(stage, destination, destinationBackupFileName: null);
            committed = true;
            var confirmed = await NIRAFileRevision.Fingerprint(destination, CancellationToken.None);
            if (confirmed != staged)
                return new NIRACapabilityHandlerResult {
                    Succeeded = false, ChangedSystemState = true,
                    Summary = "Revision replacement occurred but final verification differs; inspect the journal before any further action.",
                    Output = $"Destination={destination}\nBackupPath={backup}\nJournalPath={journal}\nPostCommitVerified=False\nNoAutomaticRetry=True"
                };
            return new NIRACapabilityHandlerResult
            {
                Summary = "Atomic file revision completed, original snapshot and journal retained; destination SHA-256 verified.",
                Output = $"Source={source}\nDestination={destination}\nBackupPath={backup}\nJournalPath={journal}\nBeforeSha256={before.Sha256}\nAfterSha256={staged.Sha256}\nVerified=True\nChangedExistingFile=True",
                ChangedSystemState = true
            };
        }
        catch (Exception ex) when (committed)
        {
            return new NIRACapabilityHandlerResult {
                Succeeded = false, ChangedSystemState = true,
                Summary = "Revision changed the destination but post-commit confirmation failed. Do not retry blindly.",
                Output = $"Destination={destination}\nBackupPath={backup}\nJournalPath={journal}\nOutcomeUncertain=True\nError={ex.GetType().Name}: {ex.Message}"
            };
        }
        finally
        {
            NIRAFileRevision.DeleteTransient(stage);
            // If revision never committed, leave the snapshot/journal intact
            // once published, so interrupted work is diagnosable.
            // An unfinished unpublished snapshot must NOT be labeled verified.
            if (!committed && snapshotExists && !journalExists)
            {
                // Keep the snapshot for manual review. Never discard bytes
                // after a partial failure; a later apply must choose a new path.
            }
        }
    }
}

public sealed class NIRAFileRevisionInspectCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileRevisionInspect,
        Description = "Read-only verification of a revision snapshot/journal and current destination. Reports Applied, OriginalPresent, or Diverged; cannot infer whether OriginalPresent means pre-commit or reverted.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = [
            NIRAFileRevision.Parameter("destination", "Exact destination recorded in journal."),
            NIRAFileRevision.Parameter("backup", "Exact revision backup file; journal is <backup>.nira-revision.json.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string destination = NIRAFileRevision.GetPath(request, "destination");
        string backup = NIRAFileRevision.GetPath(request, "backup");
        NIRAFileRevision.CheckDistinct(destination, backup, NIRAFileRevision.Sidecar(backup));
        var journal = await NIRAFileRevision.ReadManifest(backup, destination, cancellationToken);
        string state;
        string observed = "missing";
        if (File.Exists(destination))
        {
            var current = await NIRAFileRevision.Fingerprint(destination, cancellationToken);
            observed = current.Sha256;
            state = current.Bytes == journal.AfterBytes && current.Sha256 == journal.AfterSha256
                ? "Applied" : current.Bytes == journal.BeforeBytes && current.Sha256 == journal.BeforeSha256
                ? "OriginalPresent" : "Diverged";
        }
        else state = "Missing";
        return new NIRACapabilityHandlerResult {
            ChangedSystemState = false,
            Summary = $"Revision status: {state}. Snapshot SHA-256 verified against journal.",
            Output = $"Destination={destination}\nBackupPath={backup}\nJournalPath={NIRAFileRevision.Sidecar(backup)}\nState={state}\nCurrentSha256={observed}\nBeforeSha256={journal.BeforeSha256}\nAfterSha256={journal.AfterSha256}\nSnapshotVerified=True\nReadOnly=True"
        };
    }
}

public sealed class NIRAFileRevisionRollbackCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.FileRevisionRollback,
        Description = "Safely undo one NIRA revision by atomic snapshot restore ONLY when the current destination still matches the recorded revised hash. Requires explicit destination, backup and current hash, and normal modify permission. Refuses to clobber independent edits.",
        DefaultRisk = NIRACapabilityRisk.Modify,
        Parameters = [
            NIRAFileRevision.Parameter("destination", "Exact existing file to roll back."),
            NIRAFileRevision.Parameter("backup", "Verified snapshot file from filesystem.revision.apply."),
            NIRAFileRevision.Parameter("expectedCurrentSha256", "Exact current destination SHA-256 from filesystem.hash.")
        ]
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Modify;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string destination = NIRAFileRevision.GetPath(request, "destination");
        string backup = NIRAFileRevision.GetPath(request, "backup");
        string journal = NIRAFileRevision.Sidecar(backup);
        NIRAFileRevision.CheckDistinct(destination, backup, journal);
        string expected = NIRAFileRevision.GetDigest(request, "expectedCurrentSha256");
        NIRAFileRevision.RequireExistingRegularDestination(destination);
        var manifest = await NIRAFileRevision.ReadManifest(backup, destination, cancellationToken);
        if (manifest.AfterSha256 != expected)
            throw new IOException("STALE_ROLLBACK_REQUEST: expected hash does not match revised journal; no change made.");
        var current = await NIRAFileRevision.Fingerprint(destination, cancellationToken);
        if (current.Sha256 != manifest.AfterSha256 || current.Bytes != manifest.AfterBytes)
            throw new IOException("ROLLBACK_REFUSED: destination changed after revision or has already been restored. No change made.");
        string stage = NIRAFileRevision.NewStage(destination);
        bool committed = false;
        try
        {
            await NIRAFileRevision.CopyNew(backup, stage, cancellationToken);
            var staged = await NIRAFileRevision.Fingerprint(stage, cancellationToken);
            if (staged.Bytes != manifest.BeforeBytes || staged.Sha256 != manifest.BeforeSha256)
                throw new IOException("Rollback stage differs from original SHA-256; no change made.");
            // Revalidate source snapshot and destination immediately before replace.
            if ((await NIRAFileRevision.Fingerprint(backup, cancellationToken)) != staged ||
                (await NIRAFileRevision.Fingerprint(destination, cancellationToken)) != current)
                throw new IOException("Backup or destination changed before rollback commit; no change made.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Replace(stage, destination, destinationBackupFileName: null);
            committed = true;
            var restored = await NIRAFileRevision.Fingerprint(destination, CancellationToken.None);
            if (restored != staged)
                return new NIRACapabilityHandlerResult {
                    Succeeded = false, ChangedSystemState = true,
                    Summary = "Rollback was performed but final verification differs; inspect destination.",
                    Output = $"Destination={destination}\nBackupPath={backup}\nJournalPath={journal}\nPostRollbackVerified=False\nNoAutomaticRetry=True"
                };
            return new NIRACapabilityHandlerResult {
                ChangedSystemState = true,
                Summary = "Verified rollback restored previous file bytes atomically; snapshot and journal retained.",
                Output = $"Destination={destination}\nBackupPath={backup}\nJournalPath={journal}\nRestoredSha256={restored.Sha256}\nVerified=True\nOriginalRestored=True"
            };
        }
        catch (Exception ex) when (committed)
        {
            return new NIRACapabilityHandlerResult {
                Succeeded = false, ChangedSystemState = true,
                Summary = "Rollback replaced destination but post-commit state could not be verified. Inspect before retry.",
                Output = $"Destination={destination}\nBackupPath={backup}\nJournalPath={journal}\nOutcomeUncertain=True\nError={ex.GetType().Name}: {ex.Message}"
            };
        }
        finally { NIRAFileRevision.DeleteTransient(stage); }
    }
}

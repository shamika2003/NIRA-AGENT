/*
 * filename: NIRAVisualArtifactService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using NIRAAgent.Vision;

namespace NIRAAgent.Artifacts;

// =============================================================
// VISUAL ARTIFACT SERVICE
//
// Converts already-grounded image sources into stable presentation
// artifacts. This service never performs web research, browser navigation,
// screenshot interpretation, or OS control. It only validates/copies a
// concrete image source and records bounded recent presentation context.
// =============================================================

public sealed class NIRAVisualArtifactService
{
    private const long MaximumImageBytes =
        40L * 1024L * 1024L;


    private const int MaximumRecentArtifacts =
        64;


    private static readonly TimeSpan RetentionAge =
        TimeSpan.FromDays(7);


    private static readonly HashSet<string> SupportedExtensions =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".gif"
        };


    private readonly NIRAVisualEvidenceService
        _visualEvidence;


    private readonly string
        _artifactDirectory;


    private readonly object
        _sync =
            new();


    private readonly List<NIRAVisualArtifact>
        _recent =
            new();


    public NIRAVisualArtifactService(
        NIRAVisualEvidenceService visualEvidence)
    {
        _visualEvidence =
            visualEvidence
            ?? throw new ArgumentNullException(
                nameof(visualEvidence));

        _artifactDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "NIRAAgent",
                "visual-artifacts");

        Directory.CreateDirectory(
            _artifactDirectory);

        CleanupOldFilesBestEffort();
    }


    public string ArtifactDirectory =>
        _artifactDirectory;


    public IReadOnlyList<NIRAVisualArtifact> RecentArtifacts
    {
        get
        {
            lock (_sync)
            {
                return _recent
                    .OrderByDescending(
                        item => item.CreatedAtUtc)
                    .ToArray();
            }
        }
    }


    public async Task<NIRAVisualArtifact> CreateAsync(
        NIRAVisualArtifactPresentationRequest rawRequest,
        string groundedRunEvidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            rawRequest);

        cancellationToken.ThrowIfCancellationRequested();

        NIRAVisualArtifactPresentationRequest request =
            rawRequest.Normalize();

        string sourcePath;
        string title = request.Title;
        string sourceReference;
        NIRAVisualArtifactSourceKind sourceKind;
        Guid? evidenceId = null;

        if (request.EvidenceId.HasValue)
        {
            Guid requestedEvidenceId =
                request.EvidenceId.Value;

            if (!_visualEvidence.TryGetEvidence(
                    requestedEvidenceId,
                    out NIRAVisualEvidence? evidence)
                || evidence == null)
            {
                throw new InvalidOperationException(
                    $"Visual evidence '{requestedEvidenceId:D}' is no longer available for presentation.");
            }

            sourcePath =
                evidence.ImagePath;

            evidenceId =
                evidence.EvidenceId;

            sourceKind =
                NIRAVisualArtifactSourceKind.VisionEvidence;

            sourceReference =
                $"vision:{evidence.EvidenceId:D}";

            if (string.IsNullOrWhiteSpace(title))
            {
                title =
                    ResolveEvidenceTitle(evidence);
            }
        }
        else
        {
            sourcePath =
                Path.GetFullPath(
                    request.LocalPath);

            if (!IsLocalPathGrounded(
                    sourcePath,
                    groundedRunEvidence))
            {
                throw new InvalidOperationException(
                    "The requested local image path was not established by authoritative evidence in this cognition run. " +
                    "Use a grounded vision EvidenceId or first obtain the image through the normal authorized capability/tool path.");
            }

            sourceKind =
                NIRAVisualArtifactSourceKind.LocalImage;

            sourceReference =
                sourcePath;

            if (string.IsNullOrWhiteSpace(title))
            {
                title =
                    Path.GetFileNameWithoutExtension(
                        sourcePath);
            }
        }

        FileInfo source =
            ValidateSourceImage(
                sourcePath);

        Guid artifactId =
            Guid.NewGuid();

        string extension =
            source.Extension.ToLowerInvariant();

        string dayDirectory =
            Path.Combine(
                _artifactDirectory,
                DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"));

        Directory.CreateDirectory(
            dayDirectory);

        string destinationPath =
            Path.Combine(
                dayDirectory,
                $"{artifactId:N}{extension}");

        await CopyFileAsync(
            source.FullName,
            destinationPath,
            cancellationToken);

        string sha256 =
            await ComputeSha256Async(
                destinationPath,
                cancellationToken);

        NIRAVisualArtifact artifact =
            new()
            {
                ArtifactId =
                    artifactId,

                SourceKind =
                    sourceKind,

                EvidenceId =
                    evidenceId,

                LocalPath =
                    destinationPath,

                Title =
                    string.IsNullOrWhiteSpace(title)
                        ? "Visual result"
                        : title.Trim(),

                Caption =
                    request.Caption,

                SourceUri =
                    request.SourceUri,

                SourceReference =
                    sourceReference,

                Sha256 =
                    sha256,

                Surface =
                    request.Surface,

                CreatedAtUtc =
                    DateTimeOffset.UtcNow
            };

        lock (_sync)
        {
            _recent.Add(
                artifact);

            if (_recent.Count > MaximumRecentArtifacts)
            {
                _recent.RemoveRange(
                    0,
                    _recent.Count - MaximumRecentArtifacts);
            }
        }

        Debug.WriteLine(
            $"[VisualArtifact] READY | " +
            $"Artifact={artifact.ArtifactId:D} | " +
            $"Source={artifact.SourceKind} | " +
            $"Evidence={(artifact.EvidenceId.HasValue ? artifact.EvidenceId.Value.ToString("D") : "-")} | " +
            $"Surface={artifact.Surface} | " +
            $"Title='{TrimLog(artifact.Title)}'");

        return artifact;
    }


    public string BuildCognitionContext()
    {
        NIRAVisualArtifact[] recent;

        lock (_sync)
        {
            recent =
                _recent
                    .OrderByDescending(
                        item => item.CreatedAtUtc)
                    .Take(12)
                    .ToArray();
        }

        StringBuilder text =
            new();

        text.AppendLine(
            "RECENT NIRA VISUAL ARTIFACTS");

        text.AppendLine(
            "These are images NIRA has already surfaced during this running session. They are presentation history, not new visual interpretation. Use a listed EvidenceId with vision.inspect only when fresh/current visual reasoning about that grounded capture is actually needed and the evidence is still available.");

        if (recent.Length == 0)
        {
            text.AppendLine(
                "- none");

            return text
                .ToString()
                .Trim();
        }

        foreach (NIRAVisualArtifact artifact in recent)
        {
            text.AppendLine(
                $"- ArtifactId={artifact.ArtifactId:D} | " +
                $"Source={artifact.SourceKind} | " +
                $"EvidenceId={(artifact.EvidenceId.HasValue ? artifact.EvidenceId.Value.ToString("D") : "-")} | " +
                $"Title='{CleanContext(artifact.Title)}' | " +
                $"Caption='{CleanContext(artifact.Caption)}' | " +
                $"ShownAtUtc={artifact.CreatedAtUtc:O}");
        }

        return text
            .ToString()
            .Trim();
    }


    private bool IsLocalPathGrounded(
        string fullPath,
        string? groundedRunEvidence)
    {
        // Files already owned by NIRA's vision/artifact caches are runtime
        // products. Other arbitrary local files must have appeared verbatim in
        // authoritative current-run capability/tool evidence before presentation
        // is allowed to read/copy them.
        if (IsUnderDirectory(
                fullPath,
                _visualEvidence.CaptureDirectory)
            ||
            IsUnderDirectory(
                fullPath,
                _artifactDirectory))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(groundedRunEvidence)
            &&
            groundedRunEvidence.Contains(
                fullPath,
                StringComparison.OrdinalIgnoreCase);
    }


    private static bool IsUnderDirectory(
        string path,
        string directory)
    {
        string fullPath =
            Path.GetFullPath(path);

        string fullDirectory =
            Path.GetFullPath(directory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
            +
            Path.DirectorySeparatorChar;

        return fullPath.StartsWith(
            fullDirectory,
            StringComparison.OrdinalIgnoreCase);
    }


    private static FileInfo ValidateSourceImage(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "Visual artifact source path is empty.");
        }

        string fullPath =
            Path.GetFullPath(path);

        FileInfo file =
            new(fullPath);

        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "Visual artifact source image does not exist.",
                fullPath);
        }

        if (file.Length <= 0
            || file.Length > MaximumImageBytes)
        {
            throw new InvalidOperationException(
                $"Visual artifact image has unsupported size {file.Length} bytes.");
        }

        if (!SupportedExtensions.Contains(
                file.Extension))
        {
            throw new InvalidOperationException(
                $"Visual artifact format '{file.Extension}' is not currently supported for WPF presentation. " +
                "Use PNG/JPEG/BMP/GIF or capture the source as PNG first.");
        }

        return file;
    }


    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using FileStream source =
            new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using FileStream destination =
            new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(
            destination,
            128 * 1024,
            cancellationToken);
    }


    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] hash =
            await SHA256.HashDataAsync(
                stream,
                cancellationToken);

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }


    private static string ResolveEvidenceTitle(
        NIRAVisualEvidence evidence)
    {
        if (!string.IsNullOrWhiteSpace(
                evidence.CapturedWindowTitle))
        {
            return evidence.CapturedWindowTitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(
                evidence.CapturedWindowProcessName))
        {
            return evidence.CapturedWindowProcessName.Trim();
        }

        return evidence.Target switch
        {
            NIRAVisualCaptureTarget.ActiveMonitor =>
                "Screen capture",

            NIRAVisualCaptureTarget.Region =>
                "Screen region",

            _ =>
                "Window capture"
        };
    }


    private void CleanupOldFilesBestEffort()
    {
        try
        {
            if (!Directory.Exists(
                    _artifactDirectory))
            {
                return;
            }

            DateTime threshold =
                DateTime.UtcNow - RetentionAge;

            foreach (string filePath in
                     Directory.EnumerateFiles(
                         _artifactDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                try
                {
                    FileInfo file =
                        new(filePath);

                    if (file.LastWriteTimeUtc < threshold)
                    {
                        file.Delete();
                    }
                }
                catch
                {
                    // Best-effort cache cleanup must never block NIRA startup.
                }
            }

            foreach (string directory in
                     Directory.EnumerateDirectories(
                         _artifactDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }


    private static string CleanContext(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return string.Join(
            ' ',
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }


    private static string TrimLog(
        string? value)
    {
        string clean =
            CleanContext(value);

        return clean.Length <= 120
            ? clean
            : clean[..120] + "...";
    }
}


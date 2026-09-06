/*
 * filename: SegaVisualEvidenceService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;

using SegaAgent.PC.Awareness;

namespace SegaAgent.Vision;


// =============================================================
// GROUNDED VISUAL EVIDENCE SERVICE
//
// Responsibilities:
// - resolve an on-demand capture against authoritative PC world state
// - make the exact screen rectangle explicit
// - verify foreground identity for foreground-window captures
// - store the PNG in Sega's transient visual-evidence cache
// - attach process/window/monitor provenance + content hash
//
// This service does NOT interpret image pixels.
// =============================================================

public sealed class SegaVisualEvidenceService
{
    private const long MaximumCapturePixels =
        34_000_000;


    private const int MaximumRecentEvidence =
        32;


    private static readonly TimeSpan MaximumCacheAge =
        TimeSpan.FromHours(12);


    private readonly PcWorldStateService
        _worldState;


    private readonly ISegaScreenCaptureBackend
        _captureBackend;


    private readonly object
        _evidenceSync =
            new();


    private readonly Dictionary<Guid, SegaVisualEvidence>
        _recentEvidence =
            new();


    private readonly string
        _captureDirectory;


    public SegaVisualEvidenceService(
        PcWorldStateService worldState,
        ISegaScreenCaptureBackend captureBackend)
    {
        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));


        _captureBackend =
            captureBackend
            ?? throw new ArgumentNullException(
                nameof(captureBackend));


        _captureDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SegaAgent",
                "vision",
                "captures");


        Directory.CreateDirectory(
            _captureDirectory);
    }


    public string CaptureDirectory =>
        _captureDirectory;


    public IReadOnlyList<SegaVisualEvidence> RecentEvidence
    {
        get
        {
            lock (_evidenceSync)
            {
                return _recentEvidence.Values
                    .OrderByDescending(
                        value => value.CapturedAtUtc)
                    .Take(MaximumRecentEvidence)
                    .ToArray();
            }
        }
    }


    public bool TryGetEvidence(
        Guid evidenceId,
        out SegaVisualEvidence? evidence)
    {
        lock (_evidenceSync)
        {
            return _recentEvidence.TryGetValue(
                evidenceId,
                out evidence);
        }
    }


    public async Task<SegaVisualEvidence> CaptureAsync(
        SegaVisualCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);


        cancellationToken.ThrowIfCancellationRequested();


        PcWorldState snapshot =
            _worldState.Current;


        long worldVersion =
            _worldState.Version;


        PcForegroundWindowState foreground =
            snapshot.ForegroundWindow;


        PcRectangle requestedBounds =
            ResolveRequestedBounds(
                request,
                snapshot);


        PcRectangle captureBounds =
            Intersect(
                requestedBounds,
                _captureBackend.VirtualScreenBounds);


        ValidateCaptureBounds(
            captureBounds);


        IntPtr foregroundBefore =
            _captureBackend.GetForegroundWindowHandle();


        if (request.Target == SegaVisualCaptureTarget.ForegroundWindow)
        {
            if (!foreground.IsValid)
            {
                throw new InvalidOperationException(
                    "There is no valid foreground window to capture.");
            }


            if (foreground.IsMinimized)
            {
                throw new InvalidOperationException(
                    "The foreground window is minimized and cannot provide grounded visible content.");
            }


            if (foregroundBefore != foreground.Handle)
            {
                throw new InvalidOperationException(
                    "The foreground window changed before capture. Retry from fresh PC world state.");
            }
        }


        Guid evidenceId =
            Guid.NewGuid();


        string imagePath =
            Path.Combine(
                _captureDirectory,
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{evidenceId:D}.png");


        try
        {
            await _captureBackend.CapturePngAsync(
                captureBounds,
                imagePath,
                cancellationToken);


            IntPtr foregroundAfter =
                _captureBackend.GetForegroundWindowHandle();


            bool foregroundStable =
                foregroundBefore == foreground.Handle
                &&
                foregroundAfter == foreground.Handle;


            if (request.Target == SegaVisualCaptureTarget.ForegroundWindow
                && !foregroundStable)
            {
                TryDelete(
                    imagePath);


                throw new InvalidOperationException(
                    "The foreground window changed during capture, so the image was discarded instead of being mis-grounded.");
            }


            string sha256 =
                await ComputeSha256Async(
                    imagePath,
                    cancellationToken);


            SegaVisualEvidence evidence =
                new()
                {
                    EvidenceId =
                        evidenceId,

                    Target =
                        request.Target,

                    CapturedAtUtc =
                        DateTimeOffset.UtcNow,

                    WorldVersion =
                        worldVersion,

                    WorldTimestampUtc =
                        snapshot.Timestamp,

                    ImagePath =
                        imagePath,

                    Sha256 =
                        sha256,

                    CaptureBounds =
                        captureBounds,

                    ForegroundIdentityStableDuringCapture =
                        foregroundStable,

                    ForegroundWindowHandle =
                        foreground.Handle.ToInt64(),

                    ForegroundProcessId =
                        foreground.ProcessId,

                    ForegroundProcessName =
                        foreground.ProcessName,

                    ForegroundWindowTitle =
                        foreground.Title,

                    ForegroundWindowClass =
                        foreground.ClassName,

                    ForegroundWindowBounds =
                        foreground.Bounds,

                    ActiveMonitorBounds =
                        snapshot.Display.MonitorBounds,

                    ForegroundWindowWasMinimized =
                        foreground.IsMinimized,

                    ForegroundWindowWasFullscreen =
                        foreground.IsFullscreen
                };


            RememberEvidence(
                evidence);


            PruneCacheBestEffort();


            Debug.WriteLine(
                $"[VisionCapture] CAPTURED | " +
                $"Evidence={evidence.EvidenceId:D} | " +
                $"Target={evidence.Target} | " +
                $"Bounds={DescribeBounds(evidence.CaptureBounds)} | " +
                $"Process='{TrimLog(evidence.ForegroundProcessName)}' | " +
                $"PID={evidence.ForegroundProcessId} | " +
                $"Window='{TrimLog(evidence.ForegroundWindowTitle)}' | " +
                $"Stable={evidence.ForegroundIdentityStableDuringCapture}");


            return evidence;
        }
        catch
        {
            TryDelete(
                imagePath);


            throw;
        }
    }


    private static PcRectangle ResolveRequestedBounds(
        SegaVisualCaptureRequest request,
        PcWorldState snapshot)
    {
        return request.Target switch
        {
            SegaVisualCaptureTarget.ForegroundWindow =>
                snapshot.ForegroundWindow.Bounds,

            SegaVisualCaptureTarget.ActiveMonitor =>
                snapshot.Display.MonitorBounds,

            SegaVisualCaptureTarget.Region =>
                request.Region
                ?? throw new InvalidOperationException(
                    "A Region capture requires explicit screen coordinates."),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported visual capture target '{request.Target}'.")
        };
    }


    private static PcRectangle Intersect(
        PcRectangle first,
        PcRectangle second)
    {
        int left =
            Math.Max(
                first.Left,
                second.Left);


        int top =
            Math.Max(
                first.Top,
                second.Top);


        int right =
            Math.Min(
                first.Right,
                second.Right);


        int bottom =
            Math.Min(
                first.Bottom,
                second.Bottom);


        return right <= left || bottom <= top
            ? new PcRectangle()
            : new PcRectangle(
                left,
                top,
                right,
                bottom);
    }


    private static void ValidateCaptureBounds(
        PcRectangle bounds)
    {
        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException(
                "The requested visual capture does not intersect the current virtual desktop.");
        }


        if (bounds.Area > MaximumCapturePixels)
        {
            throw new InvalidOperationException(
                $"The requested capture is too large ({bounds.Width}x{bounds.Height}). Capture a smaller monitor/window/region.");
        }
    }


    private void RememberEvidence(
        SegaVisualEvidence evidence)
    {
        lock (_evidenceSync)
        {
            _recentEvidence[evidence.EvidenceId] =
                evidence;


            SegaVisualEvidence[] excess =
                _recentEvidence.Values
                    .OrderByDescending(
                        value => value.CapturedAtUtc)
                    .Skip(MaximumRecentEvidence)
                    .ToArray();


            foreach (SegaVisualEvidence item in excess)
            {
                _recentEvidence.Remove(
                    item.EvidenceId);
            }
        }
    }


    private async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);


        using SHA256 sha256 =
            SHA256.Create();


        byte[] hash =
            await sha256.ComputeHashAsync(
                stream,
                cancellationToken);


        return Convert.ToHexString(
            hash)
            .ToLowerInvariant();
    }


    private void PruneCacheBestEffort()
    {
        try
        {
            DateTimeOffset cutoff =
                DateTimeOffset.UtcNow -
                MaximumCacheAge;


            FileInfo[] files =
                new DirectoryInfo(
                    _captureDirectory)
                .EnumerateFiles(
                    "*.png",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(
                    file => file.LastWriteTimeUtc)
                .ToArray();


            for (int index = 0;
                 index < files.Length;
                 index++)
            {
                FileInfo file =
                    files[index];


                bool tooOld =
                    file.LastWriteTimeUtc <
                    cutoff.UtcDateTime;


                bool beyondCount =
                    index >=
                    MaximumRecentEvidence;


                if (tooOld || beyondCount)
                {
                    TryDelete(
                        file.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VisionCapture] CACHE PRUNE ERROR | {ex.GetType().Name}: {ex.Message}");
        }
    }


    private static void TryDelete(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }


    private static string DescribeBounds(
        PcRectangle bounds) =>
        $"{bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height}";


    private static string TrimLog(
        string value)
    {
        const int maximumLength =
            140;


        string clean =
            value?.Trim()
            ?? string.Empty;


        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }
}

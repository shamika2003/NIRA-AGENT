/*
 * filename: NIRAVisualEvidenceService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using NIRAAgent.PC.Awareness;

namespace NIRAAgent.Vision;


// =============================================================
// GROUNDED VISUAL EVIDENCE SERVICE
//
// Responsibilities:
// - resolve an on-demand capture against authoritative PC world state
// - remember recent non-NIRA windows so opening NIRA's chat does not
//   destroy the user's visual task context
// - capture a specific HWND without requiring it to stay foreground
// - store the PNG in NIRA's transient visual-evidence cache
// - attach current-foreground + actual-captured-window provenance
// - attach a content hash
//
// This service does NOT interpret image pixels.
// =============================================================

public sealed class NIRAVisualEvidenceService
{
    private const long MaximumCapturePixels =
        34_000_000;


    private const int MaximumRecentEvidence =
        32;


    private const int MaximumRecentExternalWindows =
        12;


    private static readonly TimeSpan MaximumCacheAge =
        TimeSpan.FromHours(12);


    private static readonly TimeSpan MaximumExternalWindowAge =
        TimeSpan.FromHours(4);


    private readonly PcWorldStateService
        _worldState;


    private readonly INIRAScreenCaptureBackend
        _captureBackend;


    private readonly object
        _evidenceSync =
            new();


    private readonly Dictionary<Guid, NIRAVisualEvidence>
        _recentEvidence =
            new();


    private readonly object
        _windowSync =
            new();


    private readonly Dictionary<long, RecentExternalWindow>
        _recentExternalWindows =
            new();


    private readonly string
        _captureDirectory;


    private readonly object
        _activitySync =
            new();


    private NIRAVisionActivitySnapshot
        _activity =
            new()
            {
                Kind =
                    NIRAVisionActivityKind.Idle
            };


    public event Action<NIRAVisionActivitySnapshot>?
        ActivityChanged;


    public NIRAVisualEvidenceService(
        PcWorldStateService worldState,
        INIRAScreenCaptureBackend captureBackend)
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
                "NIRAAgent",
                "vision",
                "captures");


        Directory.CreateDirectory(
            _captureDirectory);


        ObserveWorldSnapshot(
            _worldState.Current);


        _worldState.SnapshotUpdated +=
            WorldState_SnapshotUpdated;
    }


    public string CaptureDirectory =>
        _captureDirectory;


    public NIRAVisionActivitySnapshot CurrentActivity
    {
        get
        {
            lock (_activitySync)
            {
                return _activity;
            }
        }
    }


    public IReadOnlyList<NIRAVisualEvidence> RecentEvidence
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



    // =========================================================
    // COGNITION CONTEXT - AVAILABLE VISUAL TARGETS
    //
    // Expose two complementary target sources to cognition:
    // 1. recent foreground-observed external windows, preserving
    //    the semantic meaning of lastExternalWindow;
    // 2. a live Windows top-level HWND catalogue, so a named app
    //    can be captured even when it has never been foreground
    //    during the current NIRA process.
    //
    // Every selected HWND is still revalidated at capture time.
    // =========================================================

    public string BuildCognitionContext()
    {
        PcWorldState snapshot =
            _worldState.Current;


        ObserveWorldSnapshot(
            snapshot);


        RecentExternalWindow[] recent;


        lock (_windowSync)
        {
            recent =
                _recentExternalWindows.Values
                    .OrderByDescending(
                        value => value.LastObservedAtUtc)
                    .Take(8)
                    .ToArray();
        }


        NIRACaptureWindowSnapshot[] live =
            ReadLiveExternalWindows()
                .Take(16)
                .ToArray();


        StringBuilder text =
            new();


        text.AppendLine(
            "NIRA AVAILABLE VISUAL WINDOW TARGETS");


        text.AppendLine(
            "vision.capture can use both recent foreground-observed HWNDs and currently discoverable live top-level HWNDs. A named application does not need to have been foreground after NIRA started before target=window can resolve and background-capture it.");


        PcForegroundWindowState foreground =
            snapshot.ForegroundWindow;


        text.AppendLine(
            $"Current foreground: process='{CleanContext(foreground.ProcessName)}' | " +
            $"pid={foreground.ProcessId} | " +
            $"hwnd={FormatHandle(foreground.Handle.ToInt64())} | " +
            $"title='{CleanContext(foreground.Title)}'");


        if (recent.Length == 0)
        {
            text.AppendLine(
                "Recent foreground-observed external windows: none currently remembered.");
        }
        else
        {
            text.AppendLine(
                "Recent foreground-observed external windows (newest observation first):");


            DateTimeOffset now =
                DateTimeOffset.UtcNow;


            foreach (RecentExternalWindow item in recent)
            {
                TimeSpan age =
                    now -
                    item.LastObservedAtUtc;


                if (age < TimeSpan.Zero)
                {
                    age =
                        TimeSpan.Zero;
                }


                NIRACaptureWindowSnapshot window =
                    item.Window;


                text.AppendLine(
                    $"- hwnd={FormatHandle(window.Handle)} | " +
                    $"process='{CleanContext(window.ProcessName)}' | " +
                    $"pid={window.ProcessId} | " +
                    $"title='{CleanContext(window.Title)}' | " +
                    $"class='{CleanContext(window.ClassName)}' | " +
                    $"minimized={window.IsMinimized} | " +
                    $"lastForegroundSeen={age.TotalSeconds:F1}s ago");
            }
        }


        if (live.Length == 0)
        {
            text.AppendLine(
                "Live discoverable external top-level windows: none currently available.");
        }
        else
        {
            text.AppendLine(
                "Live discoverable external top-level windows (current Windows enumeration order):");


            foreach (NIRACaptureWindowSnapshot window in live)
            {
                text.AppendLine(
                    $"- hwnd={FormatHandle(window.Handle)} | " +
                    $"process='{CleanContext(window.ProcessName)}' | " +
                    $"pid={window.ProcessId} | " +
                    $"title='{CleanContext(window.Title)}' | " +
                    $"class='{CleanContext(window.ClassName)}' | " +
                    $"minimized={window.IsMinimized}");
            }
        }


        text.AppendLine(
            "Selection guidance: for a named application/window, prefer target=window with an exact live/recent hwnd when available. target=window may also use processName/titleContains and the runtime will resolve against both recent foreground history and live top-level windows. Use lastExternalWindow only for an unambiguous immediate 'the window I was just using' reference.");


        return text
            .ToString()
            .Trim();
    }


    public bool TryGetEvidence(
        Guid evidenceId,
        out NIRAVisualEvidence? evidence)
    {
        lock (_evidenceSync)
        {
            return _recentEvidence.TryGetValue(
                evidenceId,
                out evidence);
        }
    }


    // =========================================================
    // CAPTURE
    // =========================================================

    public async Task<NIRAVisualEvidence> CaptureAsync(
        NIRAVisualCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);


        cancellationToken.ThrowIfCancellationRequested();


        PcWorldState snapshot =
            _worldState.Current;


        ObserveWorldSnapshot(
            snapshot);


        long worldVersion =
            _worldState.Version;


        PcForegroundWindowState foreground =
            snapshot.ForegroundWindow;


        IntPtr foregroundBefore =
            _captureBackend.GetForegroundWindowHandle();


        Guid evidenceId =
            Guid.NewGuid();


        string imagePath =
            Path.Combine(
                _captureDirectory,
                $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{evidenceId:D}.png");


        NIRACaptureWindowSnapshot? capturedWindow =
            null;


        DateTimeOffset? capturedWindowLastObservedAtUtc =
            null;


        PcRectangle captureBounds;


        string captureMethod;


        bool backgroundWindowCapture =
            false;


        try
        {
            if (request.Target is
                NIRAVisualCaptureTarget.ForegroundWindow
                or NIRAVisualCaptureTarget.LastExternalWindow
                or NIRAVisualCaptureTarget.Window)
            {
                WindowResolution resolved =
                    ResolveWindowTarget(
                        request,
                        snapshot);


                capturedWindow =
                    resolved.Window;


                capturedWindowLastObservedAtUtc =
                    resolved.LastObservedAtUtc;


                if (capturedWindow.IsMinimized)
                {
                    throw new InvalidOperationException(
                        "The requested visual window is minimized. Bring it out of the minimized state before relying on its pixels.");
                }


                ValidateCaptureBounds(
                    capturedWindow.Bounds);


                SetActivity(
                    NIRAVisionActivityKind.Capturing,
                    request.Target,
                    capturedWindow.ProcessName,
                    capturedWindow.Title);


                NIRAWindowCaptureBackendResult result =
                    await _captureBackend.CaptureWindowPngAsync(
                        new IntPtr(
                            capturedWindow.Handle),
                        imagePath,
                        cancellationToken);


                capturedWindow =
                    result.Window;


                captureBounds =
                    result.Window.Bounds;


                captureMethod =
                    result.CaptureMethod;


                backgroundWindowCapture =
                    foregroundBefore !=
                    new IntPtr(
                        capturedWindow.Handle);
            }
            else
            {
                PcRectangle requestedBounds =
                    ResolveDesktopBounds(
                        request,
                        snapshot);


                captureBounds =
                    Intersect(
                        requestedBounds,
                        _captureBackend.VirtualScreenBounds);


                ValidateCaptureBounds(
                    captureBounds);


                SetActivity(
                    NIRAVisionActivityKind.Capturing,
                    request.Target,
                    foreground.ProcessName,
                    foreground.Title);


                await _captureBackend.CapturePngAsync(
                    captureBounds,
                    imagePath,
                    cancellationToken);


                captureMethod =
                    "BitBlt(desktop)";
            }


            IntPtr foregroundAfter =
                _captureBackend.GetForegroundWindowHandle();


            bool foregroundStable =
                foregroundBefore ==
                foregroundAfter;


            string sha256 =
                await ComputeSha256Async(
                    imagePath,
                    cancellationToken);


            NIRAVisualEvidence evidence =
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

                    CaptureMethod =
                        captureMethod,

                    IsBackgroundWindowCapture =
                        backgroundWindowCapture,

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
                        foreground.IsFullscreen,

                    CapturedWindowHandle =
                        capturedWindow?.Handle
                        ?? 0,

                    CapturedWindowProcessId =
                        capturedWindow?.ProcessId
                        ?? 0,

                    CapturedWindowProcessName =
                        capturedWindow?.ProcessName
                        ?? string.Empty,

                    CapturedWindowTitle =
                        capturedWindow?.Title
                        ?? string.Empty,

                    CapturedWindowClass =
                        capturedWindow?.ClassName
                        ?? string.Empty,

                    CapturedWindowBounds =
                        capturedWindow?.Bounds
                        ?? default,

                    CapturedWindowWasMinimized =
                        capturedWindow?.IsMinimized
                        ?? false,

                    CapturedWindowWasMaximized =
                        capturedWindow?.IsMaximized
                        ?? false,

                    CapturedWindowLastObservedAtUtc =
                        capturedWindowLastObservedAtUtc
                };


            RememberEvidence(
                evidence);


            PruneCacheBestEffort();


            string groundedProcess =
                !string.IsNullOrWhiteSpace(
                    evidence.CapturedWindowProcessName)
                    ? evidence.CapturedWindowProcessName
                    : evidence.ForegroundProcessName;


            string groundedWindow =
                !string.IsNullOrWhiteSpace(
                    evidence.CapturedWindowTitle)
                    ? evidence.CapturedWindowTitle
                    : evidence.ForegroundWindowTitle;


            Debug.WriteLine(
                $"[VisionCapture] CAPTURED | " +
                $"Evidence={evidence.EvidenceId:D} | " +
                $"Target={evidence.Target} | " +
                $"Method='{evidence.CaptureMethod}' | " +
                $"Bounds={DescribeBounds(evidence.CaptureBounds)} | " +
                $"CapturedProcess='{TrimLog(groundedProcess)}' | " +
                $"CapturedWindow='{TrimLog(groundedWindow)}' | " +
                $"Background={evidence.IsBackgroundWindowCapture} | " +
                $"ForegroundNow='{TrimLog(evidence.ForegroundProcessName)}' | " +
                $"ForegroundStable={evidence.ForegroundIdentityStableDuringCapture}");


            return evidence;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VisionCapture] FAILED | " +
                $"Target={request.Target} | " +
                $"Hwnd={(request.WindowHandle.HasValue ? FormatHandle(request.WindowHandle.Value) : "-")} | " +
                $"Process='{TrimLog(request.ProcessName ?? string.Empty)}' | " +
                $"TitleContains='{TrimLog(request.TitleContains ?? string.Empty)}' | " +
                $"{ex.GetType().Name}: {ex.Message}");


            TryDelete(
                imagePath);


            throw;
        }
        finally
        {
            ClearActivity();
        }
    }


    // =========================================================
    // USER-VISIBLE VISION ACTIVITY
    // =========================================================

    internal void BeginInspectionActivity(
        NIRAVisualEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(
            evidence);


        string process =
            !string.IsNullOrWhiteSpace(
                evidence.CapturedWindowProcessName)
                ? evidence.CapturedWindowProcessName
                : evidence.ForegroundProcessName;


        string title =
            !string.IsNullOrWhiteSpace(
                evidence.CapturedWindowTitle)
                ? evidence.CapturedWindowTitle
                : evidence.ForegroundWindowTitle;


        SetActivity(
            NIRAVisionActivityKind.Inspecting,
            evidence.Target,
            process,
            title);
    }


    internal void EndInspectionActivity() =>
        ClearActivity();


    private void ClearActivity()
    {
        SetActivity(
            NIRAVisionActivityKind.Idle,
            null,
            string.Empty,
            string.Empty);
    }


    private void SetActivity(
        NIRAVisionActivityKind kind,
        NIRAVisualCaptureTarget? target,
        string processName,
        string windowTitle)
    {
        NIRAVisionActivitySnapshot snapshot =
            new()
            {
                Kind =
                    kind,

                Target =
                    target,

                ProcessName =
                    processName?.Trim()
                    ?? string.Empty,

                WindowTitle =
                    windowTitle?.Trim()
                    ?? string.Empty,

                ChangedAtUtc =
                    DateTimeOffset.UtcNow
            };


        Action<NIRAVisionActivitySnapshot>? handlers;


        lock (_activitySync)
        {
            _activity =
                snapshot;


            handlers =
                ActivityChanged;
        }


        if (handlers == null)
        {
            return;
        }


        foreach (Action<NIRAVisionActivitySnapshot> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(
                    snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[VisionActivity] OBSERVER ERROR | {ex.GetType().Name}: {ex.Message}");
            }
        }
    }


    // =========================================================
    // RECENT EXTERNAL WINDOW CONTEXT
    //
    // Opening NIRA's UI necessarily changes foreground focus. Preserve
    // recent non-NIRA HWNDs so a task such as "look at the VS Code
    // errors" can continue without asking the user to keep VS Code in
    // front. No app names are hard-coded; the current process ID is the
    // only special case because NIRA must not remember her own windows
    // as external task targets.
    // =========================================================

    private void WorldState_SnapshotUpdated(
        PcWorldState snapshot)
    {
        try
        {
            ObserveWorldSnapshot(
                snapshot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VisionWindowContext] OBSERVE ERROR | {ex.GetType().Name}: {ex.Message}");
        }
    }


    private void ObserveWorldSnapshot(
        PcWorldState snapshot)
    {
        PcForegroundWindowState window =
            snapshot.ForegroundWindow;


        if (!window.IsValid ||
            window.ProcessId <= 0 ||
            window.ProcessId == Environment.ProcessId ||
            window.Bounds.IsEmpty ||
            window.IsMinimized ||
            string.IsNullOrWhiteSpace(window.Title))
        {
            return;
        }


        long handle =
            window.Handle.ToInt64();


        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        RecentExternalWindow recent =
            new()
            {
                Window =
                    new NIRACaptureWindowSnapshot
                    {
                        Handle =
                            handle,

                        ProcessId =
                            window.ProcessId,

                        ProcessName =
                            window.ProcessName,

                        Title =
                            window.Title,

                        ClassName =
                            window.ClassName,

                        Bounds =
                            window.Bounds,

                        IsMinimized =
                            window.IsMinimized,

                        IsMaximized =
                            window.IsMaximized
                    },

                LastObservedAtUtc =
                    now
            };


        lock (_windowSync)
        {
            _recentExternalWindows[handle] =
                recent;


            DateTimeOffset cutoff =
                now -
                MaximumExternalWindowAge;


            foreach (long staleHandle in
                     _recentExternalWindows.Values
                         .Where(
                             value =>
                                 value.LastObservedAtUtc < cutoff)
                         .Select(
                             value => value.Window.Handle)
                         .ToArray())
            {
                _recentExternalWindows.Remove(
                    staleHandle);
            }


            foreach (long excessHandle in
                     _recentExternalWindows.Values
                         .OrderByDescending(
                             value => value.LastObservedAtUtc)
                         .Skip(MaximumRecentExternalWindows)
                         .Select(
                             value => value.Window.Handle)
                         .ToArray())
            {
                _recentExternalWindows.Remove(
                    excessHandle);
            }
        }
    }


    private WindowResolution ResolveWindowTarget(
        NIRAVisualCaptureRequest request,
        PcWorldState snapshot)
    {
        if (request.Target ==
            NIRAVisualCaptureTarget.ForegroundWindow)
        {
            PcForegroundWindowState foreground =
                snapshot.ForegroundWindow;


            if (!foreground.IsValid)
            {
                throw new InvalidOperationException(
                    "There is no valid foreground window to capture.");
            }


            NIRACaptureWindowSnapshot current =
                _captureBackend.ReadWindow(
                    foreground.Handle);


            if (current.ProcessId !=
                foreground.ProcessId)
            {
                throw new InvalidOperationException(
                    "The foreground window changed process identity before capture. Retry from fresh PC world state.");
            }


            return new WindowResolution
            {
                Window =
                    current,

                LastObservedAtUtc =
                    DateTimeOffset.UtcNow
            };
        }


        if (request.Target ==
            NIRAVisualCaptureTarget.Window &&
            request.WindowHandle.HasValue &&
            request.WindowHandle.Value != 0)
        {
            IntPtr handle =
                new(
                    request.WindowHandle.Value);


            NIRACaptureWindowSnapshot current =
                _captureBackend.ReadWindow(
                    handle);


            if (current.ProcessId ==
                Environment.ProcessId)
            {
                throw new InvalidOperationException(
                    "The requested visual HWND belongs to NIRA itself, not an external visual target.");
            }


            DateTimeOffset? observed =
                FindRecentObservedAt(
                    current.Handle,
                    current.ProcessId);


            return new WindowResolution
            {
                Window =
                    current,

                LastObservedAtUtc =
                    observed
            };
        }


        string processName =
            request.ProcessName?.Trim()
            ?? string.Empty;


        string titleContains =
            request.TitleContains?.Trim()
            ?? string.Empty;


        RecentExternalWindow[] recentCandidates;


        lock (_windowSync)
        {
            recentCandidates =
                _recentExternalWindows.Values
                    .OrderByDescending(
                        value => value.LastObservedAtUtc)
                    .ToArray();
        }


        if (request.Target ==
            NIRAVisualCaptureTarget.LastExternalWindow)
        {
            foreach (RecentExternalWindow candidate in recentCandidates)
            {
                try
                {
                    NIRACaptureWindowSnapshot current =
                        _captureBackend.ReadWindow(
                            new IntPtr(
                                candidate.Window.Handle));


                    if (current.ProcessId !=
                        candidate.Window.ProcessId)
                    {
                        RemoveRecentWindow(
                            candidate.Window.Handle);


                        continue;
                    }


                    return new WindowResolution
                    {
                        Window =
                            current,

                        LastObservedAtUtc =
                            candidate.LastObservedAtUtc
                    };
                }
                catch
                {
                    RemoveRecentWindow(
                        candidate.Window.Handle);
                }
            }


            throw new InvalidOperationException(
                "No recent external application window is available for lastExternalWindow capture.");
        }


        // target=window without an exact HWND may resolve against both
        // foreground-history and live Windows top-level discovery. This
        // is what lets NIRA inspect a running background application that
        // has never been foreground during the current NIRA process.
        List<WindowResolutionCandidate> candidates =
            new();


        foreach (RecentExternalWindow recent in recentCandidates)
        {
            try
            {
                NIRACaptureWindowSnapshot current =
                    _captureBackend.ReadWindow(
                        new IntPtr(
                            recent.Window.Handle));


                if (current.ProcessId !=
                    recent.Window.ProcessId)
                {
                    RemoveRecentWindow(
                        recent.Window.Handle);


                    continue;
                }


                candidates.Add(
                    new WindowResolutionCandidate(
                        current,
                        recent.LastObservedAtUtc,
                        IsRecentForegroundObservation: true));
            }
            catch
            {
                RemoveRecentWindow(
                    recent.Window.Handle);
            }
        }


        HashSet<long> knownHandles =
            candidates
                .Select(
                    value => value.Window.Handle)
                .ToHashSet();


        foreach (NIRACaptureWindowSnapshot live in
                 ReadLiveExternalWindows())
        {
            if (knownHandles.Add(
                    live.Handle))
            {
                candidates.Add(
                    new WindowResolutionCandidate(
                        live,
                        FindRecentObservedAt(
                            live.Handle,
                            live.ProcessId),
                        IsRecentForegroundObservation: false));
            }
        }


        WindowResolutionCandidate? best =
            candidates
                .Select(
                    candidate =>
                        new
                        {
                            Candidate = candidate,
                            Score = ScoreWindowCandidate(
                                candidate,
                                processName,
                                titleContains)
                        })
                .Where(
                    value =>
                        value.Score >= 0)
                .OrderByDescending(
                    value =>
                        value.Score)
                .ThenByDescending(
                    value =>
                        WindowArea(
                            value.Candidate.Window.Bounds))
                .Select(
                    value =>
                        value.Candidate)
                .FirstOrDefault();


        if (best != null)
        {
            return new WindowResolution
            {
                Window =
                    best.Window,

                LastObservedAtUtc =
                    best.LastObservedAtUtc
            };
        }


        if (!string.IsNullOrWhiteSpace(processName) ||
            !string.IsNullOrWhiteSpace(titleContains))
        {
            throw new InvalidOperationException(
                $"No live or recent external top-level window matched the requested visual selector (processName='{CleanContext(processName)}', titleContains='{CleanContext(titleContains)}'). The application may not currently expose a visible top-level window, may be minimized, or the selector may not match its grounded process/title identity.");
        }


        throw new InvalidOperationException(
            "target=window requires an exact windowHandle or a grounded processName/titleContains selector when no recent external foreground target is unambiguous.");
    }


    private IReadOnlyList<NIRACaptureWindowSnapshot> ReadLiveExternalWindows()
    {
        try
        {
            return _captureBackend
                .EnumerateTopLevelWindows()
                .Where(
                    window =>
                        window.Handle != 0 &&
                        window.ProcessId > 0 &&
                        window.ProcessId != Environment.ProcessId &&
                        !window.Bounds.IsEmpty &&
                        !string.IsNullOrWhiteSpace(window.Title))
                .GroupBy(
                    window =>
                        window.Handle)
                .Select(
                    group =>
                        group.First())
                .ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VisionWindowContext] LIVE ENUMERATION ERROR | {ex.GetType().Name}: {ex.Message}");


            return Array.Empty<NIRACaptureWindowSnapshot>();
        }
    }


    private static int ScoreWindowCandidate(
        WindowResolutionCandidate candidate,
        string processName,
        string titleContains)
    {
        NIRACaptureWindowSnapshot window =
            candidate.Window;


        int score =
            0;


        if (!string.IsNullOrWhiteSpace(processName))
        {
            string selector =
                NormalizeProcessSelector(
                    processName);


            string actual =
                NormalizeProcessSelector(
                    window.ProcessName);


            if (string.Equals(
                    actual,
                    selector,
                    StringComparison.OrdinalIgnoreCase))
            {
                score +=
                    300;
            }
            else if (window.Title.Contains(
                         processName,
                         StringComparison.OrdinalIgnoreCase))
            {
                // Structural fallback for a caller that supplied an application
                // display name instead of the executable stem. No app-specific
                // alias table is used.
                score +=
                    120;
            }
            else
            {
                return -1;
            }
        }


        if (!string.IsNullOrWhiteSpace(titleContains))
        {
            if (!window.Title.Contains(
                    titleContains,
                    StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }


            score +=
                260;
        }


        if (string.IsNullOrWhiteSpace(processName) &&
            string.IsNullOrWhiteSpace(titleContains))
        {
            // With no selector, only a genuinely recent foreground observation
            // is safe enough to infer as the intended background window.
            if (!candidate.IsRecentForegroundObservation)
            {
                return -1;
            }


            score +=
                100;
        }


        if (!window.IsMinimized)
        {
            score +=
                40;
        }


        if (candidate.IsRecentForegroundObservation)
        {
            score +=
                20;
        }


        return score;
    }


    private static string NormalizeProcessSelector(
        string value)
    {
        string clean =
            value.Trim();


        if (clean.EndsWith(
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            clean =
                clean[..^4];
        }


        return clean;
    }


    private static long WindowArea(
        PcRectangle bounds)
    {
        if (bounds.IsEmpty)
        {
            return 0;
        }


        return (long)bounds.Width *
               bounds.Height;
    }


    private DateTimeOffset? FindRecentObservedAt(
        long handle,
        int processId)
    {
        lock (_windowSync)
        {
            if (_recentExternalWindows.TryGetValue(
                    handle,
                    out RecentExternalWindow? recent) &&
                recent.Window.ProcessId == processId)
            {
                return recent.LastObservedAtUtc;
            }
        }


        return null;
    }


    private void RemoveRecentWindow(
        long handle)
    {
        lock (_windowSync)
        {
            _recentExternalWindows.Remove(
                handle);
        }
    }


    // =========================================================
    // DESKTOP TARGET BOUNDS
    // =========================================================

    private static PcRectangle ResolveDesktopBounds(
        NIRAVisualCaptureRequest request,
        PcWorldState snapshot)
    {
        return request.Target switch
        {
            NIRAVisualCaptureTarget.ActiveMonitor =>
                snapshot.Display.MonitorBounds,

            NIRAVisualCaptureTarget.Region =>
                request.Region
                ?? throw new InvalidOperationException(
                    "A Region capture requires explicit screen coordinates."),

            _ =>
                throw new InvalidOperationException(
                    $"Capture target '{request.Target}' is not a desktop rectangle target.")
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
                "The requested visual capture has empty bounds.");
        }


        if (bounds.Area > MaximumCapturePixels)
        {
            throw new InvalidOperationException(
                $"The requested capture is too large ({bounds.Width}x{bounds.Height}). Capture a smaller monitor/window/region.");
        }
    }


    // =========================================================
    // EVIDENCE CACHE
    // =========================================================

    private void RememberEvidence(
        NIRAVisualEvidence evidence)
    {
        lock (_evidenceSync)
        {
            _recentEvidence[evidence.EvidenceId] =
                evidence;


            NIRAVisualEvidence[] excess =
                _recentEvidence.Values
                    .OrderByDescending(
                        value => value.CapturedAtUtc)
                    .Skip(MaximumRecentEvidence)
                    .ToArray();


            foreach (NIRAVisualEvidence item in excess)
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



    private static string FormatHandle(
        long handle)
    {
        return handle == 0
            ? "0x0"
            : $"0x{handle:X}";
    }


    private static string CleanContext(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }


        const int maximumLength =
            260;


        string clean =
            value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();


        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }


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


    private sealed record WindowResolutionCandidate(
        NIRACaptureWindowSnapshot Window,
        DateTimeOffset? LastObservedAtUtc,
        bool IsRecentForegroundObservation);


    private sealed record RecentExternalWindow
    {
        public NIRACaptureWindowSnapshot Window
        {
            get;
            init;
        } =
            new();


        public DateTimeOffset LastObservedAtUtc
        {
            get;
            init;
        }
    }


    private sealed record WindowResolution
    {
        public NIRACaptureWindowSnapshot Window
        {
            get;
            init;
        } =
            new();


        public DateTimeOffset? LastObservedAtUtc
        {
            get;
            init;
        }
    }
}


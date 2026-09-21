/*
 * filename: NIRAVisualEvidenceContracts.cs
 */

using NIRAAgent.PC.Awareness;

namespace NIRAAgent.Vision;


// =============================================================
// STAGE 12 VISUAL EVIDENCE CONTRACTS
//
// A capture is an observation artifact, not an interpretation.
// The image can later be supplied to a visual reasoner, but the
// existence of a PNG never means NIRA has understood its pixels.
// =============================================================

public enum NIRAVisualCaptureTarget
{
    ForegroundWindow,

    LastExternalWindow,

    Window,

    ActiveMonitor,

    Region
}


public sealed record NIRAVisualCaptureRequest
{
    public NIRAVisualCaptureTarget Target
    {
        get;
        init;
    } =
        NIRAVisualCaptureTarget.ForegroundWindow;


    // Required only when Target == Region.
    public PcRectangle? Region
    {
        get;
        init;
    }


    // Optional exact HWND selector for Target == Window.
    public long? WindowHandle
    {
        get;
        init;
    }


    // Optional recent-window selector for Target == Window.
    public string? ProcessName
    {
        get;
        init;
    }


    // Optional recent-window selector for Target == Window.
    public string? TitleContains
    {
        get;
        init;
    }
}


// =============================================================
// USER-VISIBLE VISION ACTIVITY
//
// Read-only ephemeral state so the desktop UI can show when NIRA
// is capturing pixels or asking the vision model to inspect them.
// It is not memory, a goal, or evidence of success.
// =============================================================

public enum NIRAVisionActivityKind
{
    Idle,

    Capturing,

    Inspecting
}


public sealed record NIRAVisionActivitySnapshot
{
    public NIRAVisionActivityKind Kind
    {
        get;
        init;
    }


    public NIRAVisualCaptureTarget? Target
    {
        get;
        init;
    }


    public string ProcessName
    {
        get;
        init;
    } =
        string.Empty;


    public string WindowTitle
    {
        get;
        init;
    } =
        string.Empty;


    public DateTimeOffset ChangedAtUtc
    {
        get;
        init;
    } =
        DateTimeOffset.UtcNow;
}


// =============================================================
// NATIVE WINDOW SNAPSHOT USED BY THE CAPTURE BACKEND
//
// This is raw operating-system evidence about one HWND. It is not
// visual interpretation and it is not executable authority.
// =============================================================

public sealed record NIRACaptureWindowSnapshot
{
    public long Handle
    {
        get;
        init;
    }


    public int ProcessId
    {
        get;
        init;
    }


    public string ProcessName
    {
        get;
        init;
    } =
        string.Empty;


    public string Title
    {
        get;
        init;
    } =
        string.Empty;


    public string ClassName
    {
        get;
        init;
    } =
        string.Empty;


    public PcRectangle Bounds
    {
        get;
        init;
    }


    public bool IsMinimized
    {
        get;
        init;
    }


    public bool IsMaximized
    {
        get;
        init;
    }


    public bool IsValid =>
        Handle != 0
        && ProcessId > 0
        && !Bounds.IsEmpty;
}


public sealed record NIRAWindowCaptureBackendResult
{
    public NIRACaptureWindowSnapshot Window
    {
        get;
        init;
    } =
        new();


    public string CaptureMethod
    {
        get;
        init;
    } =
        string.Empty;
}


public sealed record NIRAVisualEvidence
{
    public Guid EvidenceId
    {
        get;
        init;
    }


    public NIRAVisualCaptureTarget Target
    {
        get;
        init;
    }


    public DateTimeOffset CapturedAtUtc
    {
        get;
        init;
    }


    public long WorldVersion
    {
        get;
        init;
    }


    public DateTime WorldTimestampUtc
    {
        get;
        init;
    }


    public string ImagePath
    {
        get;
        init;
    } =
        string.Empty;


    public string Sha256
    {
        get;
        init;
    } =
        string.Empty;


    public PcRectangle CaptureBounds
    {
        get;
        init;
    }


    public string CaptureMethod
    {
        get;
        init;
    } =
        string.Empty;


    public bool IsBackgroundWindowCapture
    {
        get;
        init;
    }


    public bool ForegroundIdentityStableDuringCapture
    {
        get;
        init;
    }


    // =========================================================
    // FOREGROUND AT CAPTURE TIME
    //
    // For a background-window capture this may be NIRAAgent.UI,
    // Chrome, or another app. It is provenance only; it is not the
    // identity of the window whose pixels were captured.
    // =========================================================

    public long ForegroundWindowHandle
    {
        get;
        init;
    }


    public int ForegroundProcessId
    {
        get;
        init;
    }


    public string ForegroundProcessName
    {
        get;
        init;
    } =
        string.Empty;


    public string ForegroundWindowTitle
    {
        get;
        init;
    } =
        string.Empty;


    public string ForegroundWindowClass
    {
        get;
        init;
    } =
        string.Empty;


    public PcRectangle ForegroundWindowBounds
    {
        get;
        init;
    }


    public bool ForegroundWindowWasMinimized
    {
        get;
        init;
    }


    public bool ForegroundWindowWasFullscreen
    {
        get;
        init;
    }


    // =========================================================
    // ACTUAL CAPTURED WINDOW
    //
    // Populated for ForegroundWindow, LastExternalWindow and
    // Window targets. This is the authoritative app/window identity
    // that the vision model should use for the captured pixels.
    // =========================================================

    public long CapturedWindowHandle
    {
        get;
        init;
    }


    public int CapturedWindowProcessId
    {
        get;
        init;
    }


    public string CapturedWindowProcessName
    {
        get;
        init;
    } =
        string.Empty;


    public string CapturedWindowTitle
    {
        get;
        init;
    } =
        string.Empty;


    public string CapturedWindowClass
    {
        get;
        init;
    } =
        string.Empty;


    public PcRectangle CapturedWindowBounds
    {
        get;
        init;
    }


    public bool CapturedWindowWasMinimized
    {
        get;
        init;
    }


    public bool CapturedWindowWasMaximized
    {
        get;
        init;
    }


    public DateTimeOffset? CapturedWindowLastObservedAtUtc
    {
        get;
        init;
    }


    public PcRectangle ActiveMonitorBounds
    {
        get;
        init;
    }


    public int Width =>
        CaptureBounds.Width;


    public int Height =>
        CaptureBounds.Height;
}


// =============================================================
// PLATFORM CAPTURE BACKEND
//
// Core NIRA owns evidence semantics and grounding. The executable
// host supplies Windows-specific pixel acquisition. Desktop/region
// capture and HWND capture are separate because a background window
// must not depend on whatever happens to be foreground.
// =============================================================

public interface INIRAScreenCaptureBackend
{
    PcRectangle VirtualScreenBounds
    {
        get;
    }


    IntPtr GetForegroundWindowHandle();


    NIRACaptureWindowSnapshot ReadWindow(
        IntPtr windowHandle);


    // Enumerate currently discoverable top-level application windows.
    // This is live Windows state, not NIRA's foreground-history cache.
    // The core visual-evidence service applies NIRA/process/title semantics.
    IReadOnlyList<NIRACaptureWindowSnapshot> EnumerateTopLevelWindows();


    Task CapturePngAsync(
        PcRectangle bounds,
        string destinationPath,
        CancellationToken cancellationToken = default);


    Task<NIRAWindowCaptureBackendResult> CaptureWindowPngAsync(
        IntPtr windowHandle,
        string destinationPath,
        CancellationToken cancellationToken = default);
}


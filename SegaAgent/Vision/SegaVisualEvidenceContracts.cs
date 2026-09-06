/*
 * filename: SegaVisualEvidenceContracts.cs
 */

using SegaAgent.PC.Awareness;

namespace SegaAgent.Vision;


// =============================================================
// STAGE 12 VISUAL EVIDENCE CONTRACTS
//
// A capture is an observation artifact, not an interpretation.
// The image can later be supplied to a visual reasoner, but the
// existence of a PNG never means Sega has understood its pixels.
// =============================================================

public enum SegaVisualCaptureTarget
{
    ForegroundWindow,

    ActiveMonitor,

    Region
}


public sealed record SegaVisualCaptureRequest
{
    public SegaVisualCaptureTarget Target
    {
        get;
        init;
    } =
        SegaVisualCaptureTarget.ForegroundWindow;


    // Required only when Target == Region.
    public PcRectangle? Region
    {
        get;
        init;
    }
}


public sealed record SegaVisualEvidence
{
    public Guid EvidenceId
    {
        get;
        init;
    }


    public SegaVisualCaptureTarget Target
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


    public bool ForegroundIdentityStableDuringCapture
    {
        get;
        init;
    }


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


    public PcRectangle ActiveMonitorBounds
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


    public int Width =>
        CaptureBounds.Width;


    public int Height =>
        CaptureBounds.Height;
}


// =============================================================
// PLATFORM CAPTURE BACKEND
//
// Core Sega owns evidence semantics and grounding. The executable
// host supplies the Windows-specific implementation that can read
// pixels from the desktop.
// =============================================================

public interface ISegaScreenCaptureBackend
{
    PcRectangle VirtualScreenBounds
    {
        get;
    }


    IntPtr GetForegroundWindowHandle();


    Task CapturePngAsync(
        PcRectangle bounds,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

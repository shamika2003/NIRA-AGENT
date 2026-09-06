/*
 * filename: VisionCapabilityHandlers.cs
 */

using System.Text;

using SegaAgent.PC.Awareness;
using SegaAgent.Vision;

namespace SegaAgent.Capabilities;


// =============================================================
// STAGE 12 - ON-DEMAND VISUAL CAPTURE PRIMITIVE
//
// This capability creates grounded raw visual evidence only.
// A later visual-understanding stage consumes the resulting image.
// =============================================================

public sealed class SegaScreenCaptureCapabilityHandler
    : ISegaCapabilityHandler
{
    private readonly SegaVisualEvidenceService
        _visualEvidence;


    public SegaScreenCaptureCapabilityHandler(
        SegaVisualEvidenceService visualEvidence)
    {
        _visualEvidence =
            visualEvidence
            ?? throw new ArgumentNullException(
                nameof(visualEvidence));
    }


    public SegaCapabilityDescriptor Descriptor
    {
        get;
    } =
        new SegaCapabilityDescriptor
        {
            Id =
                SegaCapabilityIds.VisionCapture,

            Description =
                "Capture an on-demand PNG from the current desktop and return raw visual-evidence metadata grounded to authoritative process/window/monitor state. Target may be foregroundWindow, activeMonitor, or region. Capture alone does not visually interpret the image.",

            DefaultRisk =
                SegaCapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter(
                        "target",
                        "string",
                        false,
                        "foregroundWindow (default), activeMonitor, or region."),

                    Parameter(
                        "left",
                        "integer",
                        false,
                        "Screen-space X coordinate. Required only for target=region; may be negative on a multi-monitor desktop."),

                    Parameter(
                        "top",
                        "integer",
                        false,
                        "Screen-space Y coordinate. Required only for target=region; may be negative on a multi-monitor desktop."),

                    Parameter(
                        "width",
                        "integer",
                        false,
                        "Region width in physical screen pixels. Required only for target=region."),

                    Parameter(
                        "height",
                        "integer",
                        false,
                        "Region height in physical screen pixels. Required only for target=region.")
                }
        };


    public SegaCapabilityRisk ResolveRisk(
        SegaCapabilityRequest request)
    {
        return SegaCapabilityRisk.Observe;
    }


    public async Task<SegaCapabilityHandlerResult> ExecuteAsync(
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        SegaVisualCaptureTarget target =
            ParseTarget(
                SegaCapabilityArguments.GetOptionalString(
                    request,
                    "target",
                    64));


        PcRectangle? region =
            target == SegaVisualCaptureTarget.Region
                ? BuildRegion(request)
                : null;


        SegaVisualEvidence evidence =
            await _visualEvidence.CaptureAsync(
                new SegaVisualCaptureRequest
                {
                    Target =
                        target,

                    Region =
                        region
                },
                cancellationToken);


        StringBuilder output =
            new();


        output.AppendLine(
            "SEGA GROUNDED VISUAL EVIDENCE");

        output.AppendLine(
            $"EvidenceId: {evidence.EvidenceId:D}");

        output.AppendLine(
            $"Target: {evidence.Target}");

        output.AppendLine(
            $"CapturedAtUtc: {evidence.CapturedAtUtc:O}");

        output.AppendLine(
            $"WorldVersion: {evidence.WorldVersion}");

        output.AppendLine(
            $"WorldTimestampUtc: {evidence.WorldTimestampUtc:O}");

        output.AppendLine(
            $"ImagePath: {evidence.ImagePath}");

        output.AppendLine(
            $"Sha256: {evidence.Sha256}");

        output.AppendLine(
            $"CaptureBounds: left={evidence.CaptureBounds.Left}, top={evidence.CaptureBounds.Top}, width={evidence.Width}, height={evidence.Height}");

        output.AppendLine(
            $"ForegroundIdentityStableDuringCapture: {evidence.ForegroundIdentityStableDuringCapture}");

        output.AppendLine();
        output.AppendLine(
            "FOREGROUND WINDOW GROUNDING");

        output.AppendLine(
            $"Handle: 0x{evidence.ForegroundWindowHandle:X}");

        output.AppendLine(
            $"Process: {Normalize(evidence.ForegroundProcessName)}");

        output.AppendLine(
            $"ProcessId: {evidence.ForegroundProcessId}");

        output.AppendLine(
            $"WindowTitle: {Normalize(evidence.ForegroundWindowTitle)}");

        output.AppendLine(
            $"WindowClass: {Normalize(evidence.ForegroundWindowClass)}");

        output.AppendLine(
            $"WindowBounds: left={evidence.ForegroundWindowBounds.Left}, top={evidence.ForegroundWindowBounds.Top}, width={evidence.ForegroundWindowBounds.Width}, height={evidence.ForegroundWindowBounds.Height}");

        output.AppendLine(
            $"WindowMinimized: {evidence.ForegroundWindowWasMinimized}");

        output.AppendLine(
            $"WindowFullscreen: {evidence.ForegroundWindowWasFullscreen}");

        output.AppendLine(
            $"ActiveMonitorBounds: left={evidence.ActiveMonitorBounds.Left}, top={evidence.ActiveMonitorBounds.Top}, width={evidence.ActiveMonitorBounds.Width}, height={evidence.ActiveMonitorBounds.Height}");

        output.AppendLine();
        output.AppendLine(
            "InterpretationStatus: RAW_CAPTURE_ONLY");

        output.AppendLine(
            "The PNG has been captured and grounded, but this capability has not interpreted, read, recognized, or verified any visual content inside it.");


        string subject =
            string.IsNullOrWhiteSpace(
                    evidence.ForegroundProcessName)
                ? "the desktop"
                : $"{evidence.ForegroundProcessName} (PID {evidence.ForegroundProcessId})";


        return new SegaCapabilityHandlerResult
        {
            Succeeded =
                true,

            Summary =
                $"Captured grounded {evidence.Target} visual evidence ({evidence.Width}x{evidence.Height}) while foreground identity was {subject}.",

            Output =
                output.ToString().TrimEnd(),

            ChangedSystemState =
                false
        };
    }


    private static SegaVisualCaptureTarget ParseTarget(
        string? value)
    {
        string normalized =
            string.IsNullOrWhiteSpace(value)
                ? "foregroundwindow"
                : value.Trim()
                    .Replace("_", string.Empty)
                    .Replace("-", string.Empty)
                    .Replace(" ", string.Empty)
                    .ToLowerInvariant();


        return normalized switch
        {
            "foreground" or
            "foregroundwindow" or
            "window" =>
                SegaVisualCaptureTarget.ForegroundWindow,

            "monitor" or
            "activemonitor" or
            "screen" =>
                SegaVisualCaptureTarget.ActiveMonitor,

            "region" =>
                SegaVisualCaptureTarget.Region,

            _ =>
                throw new InvalidOperationException(
                    "Capability argument 'target' must be foregroundWindow, activeMonitor, or region.")
        };
    }


    private static PcRectangle BuildRegion(
        SegaCapabilityRequest request)
    {
        int left =
            SegaCapabilityArguments.RequireInteger(
                request,
                "left",
                -1_000_000,
                1_000_000);


        int top =
            SegaCapabilityArguments.RequireInteger(
                request,
                "top",
                -1_000_000,
                1_000_000);


        int width =
            SegaCapabilityArguments.RequireInteger(
                request,
                "width",
                1,
                32768);


        int height =
            SegaCapabilityArguments.RequireInteger(
                request,
                "height",
                1,
                32768);


        long rightLong =
            (long)left + width;


        long bottomLong =
            (long)top + height;


        if (rightLong > int.MaxValue ||
            rightLong < int.MinValue ||
            bottomLong > int.MaxValue ||
            bottomLong < int.MinValue)
        {
            throw new InvalidOperationException(
                "The requested capture region exceeds supported screen coordinates.");
        }


        return new PcRectangle(
            left,
            top,
            (int)rightLong,
            (int)bottomLong);
    }


    private static SegaCapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description) =>
        new()
        {
            Name =
                name,

            Type =
                type,

            Required =
                required,

            Description =
                description
        };


    private static string Normalize(
        string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim();
}


// =============================================================
// STAGE 12 - GROUNDED VISUAL INTERPRETATION PRIMITIVE
//
// Consumes one existing raw SegaVisualEvidence capture and returns
// structured model-derived visual observations. It never performs
// mouse/keyboard/UI actions and it does not grant action authority.
// =============================================================

public sealed class SegaVisualInspectCapabilityHandler
    : ISegaCapabilityHandler
{
    private readonly SegaVisualUnderstandingService
        _understanding;


    public SegaVisualInspectCapabilityHandler(
        SegaVisualUnderstandingService understanding)
    {
        _understanding =
            understanding
            ?? throw new ArgumentNullException(
                nameof(understanding));
    }


    public SegaCapabilityDescriptor Descriptor
    {
        get;
    } =
        new SegaCapabilityDescriptor
        {
            Id =
                SegaCapabilityIds.VisionInspect,

            Description =
                "Interpret an existing grounded vision.capture screenshot with Sega's dedicated vision model. Returns structured visible text, UI elements, issues, confidence and limitations grounded to the exact EvidenceId. This observes pixels only and performs no UI action.",

            DefaultRisk =
                SegaCapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter(
                        "evidenceId",
                        "string",
                        true,
                        "Exact EvidenceId returned by a successful vision.capture result in this Sega process."),

                    Parameter(
                        "question",
                        "string",
                        false,
                        "Optional focused visual question, for example 'Is there a build error visible?' or 'What does the dialog say?'.")
                }
        };


    public SegaCapabilityRisk ResolveRisk(
        SegaCapabilityRequest request) =>
        SegaCapabilityRisk.Observe;


    public async Task<SegaCapabilityHandlerResult> ExecuteAsync(
        SegaCapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string evidenceText =
            SegaCapabilityArguments.RequireString(
                request,
                "evidenceId",
                80);

        if (!Guid.TryParse(evidenceText, out Guid evidenceId) ||
            evidenceId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Capability argument 'evidenceId' must be an exact non-empty visual EvidenceId GUID returned by vision.capture.");
        }

        string? question =
            SegaCapabilityArguments.GetOptionalString(
                request,
                "question",
                1200);

        SegaVisualObservation observation =
            await _understanding.InspectAsync(
                evidenceId,
                question,
                cancellationToken);

        StringBuilder output = new();

        output.AppendLine(
            "SEGA GROUNDED VISUAL OBSERVATION");

        output.AppendLine(
            $"ObservationId: {observation.ObservationId:D}");

        output.AppendLine(
            $"EvidenceId: {observation.EvidenceId:D}");

        output.AppendLine(
            $"ObservedAtUtc: {observation.ObservedAtUtc:O}");

        output.AppendLine(
            $"VisionModel: {observation.Model}");

        output.AppendLine(
            $"Confidence: {observation.Confidence:F2}");

        output.AppendLine(
            $"WorldVersion: {observation.WorldVersion}");

        output.AppendLine(
            $"ImageSha256: {observation.ImageSha256}");

        output.AppendLine(
            $"CaptureBounds: left={observation.CaptureBounds.Left}, top={observation.CaptureBounds.Top}, width={observation.CaptureBounds.Width}, height={observation.CaptureBounds.Height}");

        output.AppendLine(
            $"GroundedForeground: {Normalize(observation.ForegroundProcessName)} | PID={observation.ForegroundProcessId} | Window={Normalize(observation.ForegroundWindowTitle)}");

        if (!string.IsNullOrWhiteSpace(observation.Question))
        {
            output.AppendLine(
                $"VisualQuestion: {observation.Question}");
        }

        output.AppendLine();
        output.AppendLine("SUMMARY");
        output.AppendLine(observation.Summary);

        if (!string.IsNullOrWhiteSpace(observation.ApplicationContent))
        {
            output.AppendLine();
            output.AppendLine("VISIBLE APPLICATION / WORKSPACE CONTENT");
            output.AppendLine(observation.ApplicationContent);
        }

        if (observation.VisibleText.Count > 0)
        {
            output.AppendLine();
            output.AppendLine("IMPORTANT LEGIBLE TEXT");

            foreach (string text in observation.VisibleText)
            {
                output.AppendLine($"- {text}");
            }
        }

        if (observation.Elements.Count > 0)
        {
            output.AppendLine();
            output.AppendLine("VISUAL UI ELEMENTS");

            foreach (SegaVisualElementObservation element in observation.Elements)
            {
                string normalizedBounds =
                    element.BoundsNormalized == null
                        ? "unknown"
                        : $"{element.BoundsNormalized.Left},{element.BoundsNormalized.Top}-{element.BoundsNormalized.Right},{element.BoundsNormalized.Bottom}";

                string screenBounds =
                    element.ScreenBounds.HasValue
                        ? $"left={element.ScreenBounds.Value.Left}, top={element.ScreenBounds.Value.Top}, width={element.ScreenBounds.Value.Width}, height={element.ScreenBounds.Value.Height}"
                        : "unknown";

                output.AppendLine(
                    $"- kind={element.Kind} | label={Normalize(element.Label)} | state={Normalize(element.State)} | confidence={element.Confidence:F2} | normalizedBounds={normalizedBounds} | screenBounds={screenBounds}");
            }
        }

        if (observation.Issues.Count > 0)
        {
            output.AppendLine();
            output.AppendLine("VISIBLE ISSUES / WARNINGS");

            foreach (string issue in observation.Issues)
            {
                output.AppendLine($"- {issue}");
            }
        }

        if (observation.Limitations.Count > 0)
        {
            output.AppendLine();
            output.AppendLine("VISUAL LIMITATIONS / UNCERTAINTY");

            foreach (string limitation in observation.Limitations)
            {
                output.AppendLine($"- {limitation}");
            }
        }

        output.AppendLine();
        output.AppendLine(
            "EvidenceStatus: MODEL_DERIVED_VISUAL_OBSERVATION");

        output.AppendLine(
            "This observation is grounded to the exact captured PNG and authoritative capture metadata, but visual interpretation remains model-derived evidence. Confidence and limitations must be respected; it is not proof of an OS action or hidden/off-screen state.");

        return new SegaCapabilityHandlerResult
        {
            Succeeded = true,
            Summary =
                $"Interpreted visual evidence {observation.EvidenceId:D} with {observation.Model}: {Bound(observation.Summary, 360)}",
            Output = output.ToString().TrimEnd(),
            ChangedSystemState = false
        };
    }


    private static SegaCapabilityParameterDescriptor Parameter(
        string name,
        string type,
        bool required,
        string description) =>
        new()
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };


    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim();


    private static string Bound(string? value, int maximum)
    {
        string clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximum
            ? clean
            : clean[..maximum] + "...";
    }
}

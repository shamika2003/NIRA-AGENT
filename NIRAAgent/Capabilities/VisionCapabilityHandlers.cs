/*
 * filename: VisionCapabilityHandlers.cs
 */

using System.Globalization;
using System.Text;

using NIRAAgent.PC.Awareness;
using NIRAAgent.Vision;

namespace NIRAAgent.Capabilities;


// =============================================================
// STAGE 12 - ON-DEMAND VISUAL CAPTURE PRIMITIVE
//
// Captures grounded raw visual evidence. Specific-window targets
// can be captured by HWND without requiring that window to remain
// foreground. Interpretation remains a separate vision.inspect step.
// =============================================================

public sealed class NIRAScreenCaptureCapabilityHandler
    : INIRACapabilityHandler
{
    private readonly NIRAVisualEvidenceService
        _visualEvidence;


    public NIRAScreenCaptureCapabilityHandler(
        NIRAVisualEvidenceService visualEvidence)
    {
        _visualEvidence =
            visualEvidence
            ?? throw new ArgumentNullException(
                nameof(visualEvidence));
    }


    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.VisionCapture,

            Description =
                "Capture an on-demand PNG and return grounded visual-evidence metadata. target may be foregroundWindow, lastExternalWindow, window, activeMonitor, or region. target=window can resolve a live or recent external top-level HWND and capture it without keeping it foreground. Capture alone does not interpret pixels.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter(
                        "target",
                        "string",
                        false,
                        "foregroundWindow (default), lastExternalWindow, window, activeMonitor, or region."),

                    Parameter(
                        "windowHandle",
                        "string",
                        false,
                        "Optional exact HWND for target=window. Accepts decimal or 0x-prefixed hexadecimal handle returned by earlier grounded visual evidence."),

                    Parameter(
                        "processName",
                        "string",
                        false,
                        "Optional live/recent external process selector for target=window, for example a grounded process name such as Code. No app name is hard-coded by the runtime."),

                    Parameter(
                        "titleContains",
                        "string",
                        false,
                        "Optional case-insensitive live/recent external window-title selector for target=window."),

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


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request)
    {
        return NIRACapabilityRisk.Observe;
    }


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        NIRAVisualCaptureTarget target =
            ParseTarget(
                NIRACapabilityArguments.GetOptionalString(
                    request,
                    "target",
                    64));


        PcRectangle? region =
            target == NIRAVisualCaptureTarget.Region
                ? BuildRegion(request)
                : null;


        long? windowHandle =
            target == NIRAVisualCaptureTarget.Window
                ? ParseWindowHandle(
                    NIRACapabilityArguments.GetOptionalString(
                        request,
                        "windowHandle",
                        80))
                : null;


        string? processName =
            target == NIRAVisualCaptureTarget.Window
                ? NIRACapabilityArguments.GetOptionalString(
                    request,
                    "processName",
                    260)
                : null;


        string? titleContains =
            target == NIRAVisualCaptureTarget.Window
                ? NIRACapabilityArguments.GetOptionalString(
                    request,
                    "titleContains",
                    600)
                : null;


        NIRAVisualEvidence evidence =
            await _visualEvidence.CaptureAsync(
                new NIRAVisualCaptureRequest
                {
                    Target =
                        target,

                    Region =
                        region,

                    WindowHandle =
                        windowHandle,

                    ProcessName =
                        processName,

                    TitleContains =
                        titleContains
                },
                cancellationToken);


        StringBuilder output =
            new();


        output.AppendLine(
            "NIRA GROUNDED VISUAL EVIDENCE");

        output.AppendLine(
            $"EvidenceId: {evidence.EvidenceId:D}");

        output.AppendLine(
            $"Target: {evidence.Target}");

        output.AppendLine(
            $"CaptureMethod: {evidence.CaptureMethod}");

        output.AppendLine(
            $"BackgroundWindowCapture: {evidence.IsBackgroundWindowCapture}");

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


        if (evidence.CapturedWindowHandle != 0)
        {
            output.AppendLine();
            output.AppendLine(
                "ACTUAL CAPTURED WINDOW GROUNDING");

            output.AppendLine(
                $"CapturedWindowHandle: 0x{evidence.CapturedWindowHandle:X}");

            output.AppendLine(
                $"CapturedProcess: {Normalize(evidence.CapturedWindowProcessName)}");

            output.AppendLine(
                $"CapturedProcessId: {evidence.CapturedWindowProcessId}");

            output.AppendLine(
                $"CapturedWindowTitle: {Normalize(evidence.CapturedWindowTitle)}");

            output.AppendLine(
                $"CapturedWindowClass: {Normalize(evidence.CapturedWindowClass)}");

            output.AppendLine(
                $"CapturedWindowBounds: left={evidence.CapturedWindowBounds.Left}, top={evidence.CapturedWindowBounds.Top}, width={evidence.CapturedWindowBounds.Width}, height={evidence.CapturedWindowBounds.Height}");

            output.AppendLine(
                $"CapturedWindowMinimized: {evidence.CapturedWindowWasMinimized}");

            output.AppendLine(
                $"CapturedWindowMaximized: {evidence.CapturedWindowWasMaximized}");

            output.AppendLine(
                $"CapturedWindowLastObservedAtUtc: {(evidence.CapturedWindowLastObservedAtUtc.HasValue ? evidence.CapturedWindowLastObservedAtUtc.Value.ToString("O") : "-")}");
        }


        output.AppendLine();
        output.AppendLine(
            "FOREGROUND AT CAPTURE TIME");

        output.AppendLine(
            $"ForegroundHandle: 0x{evidence.ForegroundWindowHandle:X}");

        output.AppendLine(
            $"ForegroundProcess: {Normalize(evidence.ForegroundProcessName)}");

        output.AppendLine(
            $"ForegroundProcessId: {evidence.ForegroundProcessId}");

        output.AppendLine(
            $"ForegroundWindowTitle: {Normalize(evidence.ForegroundWindowTitle)}");

        output.AppendLine(
            $"ForegroundIdentityStableDuringCapture: {evidence.ForegroundIdentityStableDuringCapture}");

        output.AppendLine(
            $"ActiveMonitorBounds: left={evidence.ActiveMonitorBounds.Left}, top={evidence.ActiveMonitorBounds.Top}, width={evidence.ActiveMonitorBounds.Width}, height={evidence.ActiveMonitorBounds.Height}");

        output.AppendLine();
        output.AppendLine(
            "InterpretationStatus: RAW_CAPTURE_ONLY");

        output.AppendLine(
            "The PNG has been captured and grounded, but this capability has not interpreted, read, recognized, or verified any visual content inside it.");


        string subject =
            !string.IsNullOrWhiteSpace(
                evidence.CapturedWindowProcessName)
                ? $"{evidence.CapturedWindowProcessName} (PID {evidence.CapturedWindowProcessId})"
                : !string.IsNullOrWhiteSpace(
                    evidence.ForegroundProcessName)
                    ? $"the desktop while {evidence.ForegroundProcessName} was foreground"
                    : "the desktop";


        return new NIRACapabilityHandlerResult
        {
            Succeeded =
                true,

            Summary =
                evidence.IsBackgroundWindowCapture
                    ? $"Background-captured grounded {evidence.Target} visual evidence ({evidence.Width}x{evidence.Height}) from {subject} without requiring it to be foreground."
                    : $"Captured grounded {evidence.Target} visual evidence ({evidence.Width}x{evidence.Height}) from {subject}.",

            Output =
                output.ToString().TrimEnd(),

            ChangedSystemState =
                false
        };
    }


    private static NIRAVisualCaptureTarget ParseTarget(
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
            "foregroundwindow" =>
                NIRAVisualCaptureTarget.ForegroundWindow,

            "lastexternal" or
            "lastexternalwindow" or
            "previouswindow" or
            "previousapp" =>
                NIRAVisualCaptureTarget.LastExternalWindow,

            "window" or
            "specificwindow" or
            "backgroundwindow" =>
                NIRAVisualCaptureTarget.Window,

            "monitor" or
            "activemonitor" or
            "screen" =>
                NIRAVisualCaptureTarget.ActiveMonitor,

            "region" =>
                NIRAVisualCaptureTarget.Region,

            _ =>
                throw new InvalidOperationException(
                    "Capability argument 'target' must be foregroundWindow, lastExternalWindow, window, activeMonitor, or region.")
        };
    }


    private static long? ParseWindowHandle(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }


        string clean =
            value.Trim();


        if (clean.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase))
        {
            if (long.TryParse(
                    clean[2..],
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out long hex) &&
                hex != 0)
            {
                return hex;
            }
        }
        else if (long.TryParse(
                     clean,
                     NumberStyles.Integer,
                     CultureInfo.InvariantCulture,
                     out long numeric) &&
                 numeric != 0)
        {
            return numeric;
        }


        throw new InvalidOperationException(
            "Capability argument 'windowHandle' must be a non-zero decimal HWND or a 0x-prefixed hexadecimal HWND.");
    }


    private static PcRectangle BuildRegion(
        NIRACapabilityRequest request)
    {
        int left =
            NIRACapabilityArguments.RequireInteger(
                request,
                "left",
                -1_000_000,
                1_000_000);


        int top =
            NIRACapabilityArguments.RequireInteger(
                request,
                "top",
                -1_000_000,
                1_000_000);


        int width =
            NIRACapabilityArguments.RequireInteger(
                request,
                "width",
                1,
                32768);


        int height =
            NIRACapabilityArguments.RequireInteger(
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


    private static NIRACapabilityParameterDescriptor Parameter(
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
// Consumes one existing raw NIRAVisualEvidence capture and returns
// structured model-derived visual observations. It never performs
// mouse/keyboard/UI actions and it does not grant action authority.
// =============================================================

public sealed class NIRAVisualInspectCapabilityHandler
    : INIRACapabilityHandler
{
    private readonly NIRAVisualUnderstandingService
        _understanding;


    public NIRAVisualInspectCapabilityHandler(
        NIRAVisualUnderstandingService understanding)
    {
        _understanding =
            understanding
            ?? throw new ArgumentNullException(
                nameof(understanding));
    }


    public NIRACapabilityDescriptor Descriptor
    {
        get;
    } =
        new NIRACapabilityDescriptor
        {
            Id =
                NIRACapabilityIds.VisionInspect,

            Description =
                "Interpret an existing grounded vision.capture screenshot with NIRA's dedicated vision model. Returns structured visible text, UI elements, issues, confidence and limitations grounded to the exact EvidenceId. This observes pixels only and performs no UI action.",

            DefaultRisk =
                NIRACapabilityRisk.Observe,

            Parameters =
                new[]
                {
                    Parameter(
                        "evidenceId",
                        "string",
                        true,
                        "Exact EvidenceId returned by a successful vision.capture result in this NIRA process."),

                    Parameter(
                        "question",
                        "string",
                        false,
                        "Optional focused visual question, for example 'Is there a build error visible?' or 'What does the dialog say?'.")
                }
        };


    public NIRACapabilityRisk ResolveRisk(
        NIRACapabilityRequest request) =>
        NIRACapabilityRisk.Observe;


    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string evidenceText =
            NIRACapabilityArguments.RequireString(
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
            NIRACapabilityArguments.GetOptionalString(
                request,
                "question",
                1200);

        NIRAVisualObservation observation =
            await _understanding.InspectAsync(
                evidenceId,
                question,
                cancellationToken);

        StringBuilder output = new();

        output.AppendLine(
            "NIRA GROUNDED VISUAL OBSERVATION");

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
            $"GroundedWindow: {Normalize(observation.ForegroundProcessName)} | PID={observation.ForegroundProcessId} | Window={Normalize(observation.ForegroundWindowTitle)}");

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

            foreach (NIRAVisualElementObservation element in observation.Elements)
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

        return new NIRACapabilityHandlerResult
        {
            Succeeded = true,
            Summary =
                $"Interpreted visual evidence {observation.EvidenceId:D} with {observation.Model}: {Bound(observation.Summary, 360)}",
            Output = output.ToString().TrimEnd(),
            ChangedSystemState = false
        };
    }


    private static NIRACapabilityParameterDescriptor Parameter(
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


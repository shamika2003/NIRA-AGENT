using System.Globalization;
using NIRAAgent.Vision;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.Capabilities;

public sealed class NIRADesktopInspectCapabilityHandler : INIRACapabilityHandler
{
    private readonly NIRAVisualEvidenceService _windows;
    private readonly INIRADesktopInteractionBackend _desktop;
    public NIRADesktopInspectCapabilityHandler(
        NIRAVisualEvidenceService windows, INIRADesktopInteractionBackend desktop)
    { _windows = windows; _desktop = desktop; }

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DesktopInspect,
        DefaultRisk = NIRACapabilityRisk.Observe,
        Description = "Inspect live controls of a named desktop application's window, including background/minimized windows when UI Automation supports them. Returns exact HWND, control names, types and automation IDs for desktop.act; no screenshot or filesystem path is implied. Use this when the user asks NIRA to operate an application.",
        Parameters = WindowParameters()
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest _) => NIRACapabilityRisk.Observe;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        var w = DesktopWindowResolver.Resolve(_windows, request);
        string result = await _desktop.InspectAsync(new IntPtr(w.Handle), w.ProcessId, cancellationToken);
        return new NIRACapabilityHandlerResult
        {
            Succeeded = true, ChangedSystemState = false,
            Summary = $"Inspected verified desktop HWND=0x{w.Handle:X}, PID={w.ProcessId}, title='{w.Title}'.",
            Output = "NIRA DESKTOP CONTROL SNAPSHOT\n" +
                $"WindowHandle: 0x{w.Handle:X}\nProcessId: {w.ProcessId}\n" +
                $"Process: {w.ProcessName}\nTitle: {w.Title}\nMinimized: {w.IsMinimized}\n" +
                "This is a read-only UIA snapshot; listed controls are NOT evidence of an action or of a filesystem path.\n" + result
        };
    }
    internal static NIRACapabilityParameterDescriptor[] WindowParameters() => new[]
    {
        new NIRACapabilityParameterDescriptor { Name="windowHandle", Type="string", Description="Exact 0x HWND of the target, obtained from live desktop evidence; optional for inspection." },
        new NIRACapabilityParameterDescriptor { Name="processName", Type="string", Description="Optional live process name selector." },
        new NIRACapabilityParameterDescriptor { Name="titleContains", Type="string", Description="Optional live title substring selector." }
    };
}

public sealed class NIRADesktopActCapabilityHandler : INIRACapabilityHandler
{
    private readonly NIRAVisualEvidenceService _windows;
    private readonly INIRADesktopInteractionBackend _desktop;
    public NIRADesktopActCapabilityHandler(
        NIRAVisualEvidenceService windows, INIRADesktopInteractionBackend desktop)
    { _windows = windows; _desktop = desktop; }

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.DesktopAct,
        DefaultRisk = NIRACapabilityRisk.Modify,
        Description = "Operate an exact, verified Windows HWND using UI Automation: activate, invoke, select, toggle, expand, collapse, setValue, focus, click, type, press a bounded key chord, or close the exact top-level window gracefully. Close uses native UI Automation WindowPattern or background-safe WM_CLOSE and never force-kills the process; verify window/process state afterward. Background-safe UIA actions run without stealing focus; foreground-only actions pause if the owner changes windows and resume after the exact target returns, with zero model calls spent waiting. Inspect controls first. Reported success means only one action dispatched; verify the user's objective via another desktop.inspect or trusted application state before saying Done. Actions execute under existing owner authorization.",
        Parameters = NIRADesktopInspectCapabilityHandler.WindowParameters().Select(p =>
            p.Name == "windowHandle" ? p with { Required = true } : p).Concat(new[]
        {
            new NIRACapabilityParameterDescriptor { Name="action", Type="string", Required=true, Description="activate/invoke/select/toggle/expand/collapse/setValue/focus/click/type/press/close (graceful window close, not process kill)" },
            new NIRACapabilityParameterDescriptor { Name="controlName", Type="string", Description="Exact control name from desktop.inspect; required for control actions except 'type' into a verified focused editor." },
            new NIRACapabilityParameterDescriptor { Name="automationId", Type="string", Description="Exact control automation ID observed in desktop.inspect." },
            new NIRACapabilityParameterDescriptor { Name="controlType", Type="string", Description="Optional extra exact ControlType constraint from desktop.inspect; when omitted, live lookup must still uniquely match controlName/automationId." },
            new NIRACapabilityParameterDescriptor { Name="text", Type="string", Description="Non-secret text for setValue or type; type uses a unique matched control or the focused EDIT/DOCUMENT inside verified HWND. Never logs text." },
            new NIRACapabilityParameterDescriptor { Name="key", Type="string", Description="For press only, e.g. CTRL+N, TAB, ENTER, ESC. Never embed ^a or {BACKSPACE} in type text." },
            new NIRACapabilityParameterDescriptor { Name="allowExistingContent", Type="boolean", Description="Only for type: set true ONLY if the user's actual request requires inserting into an already populated editor. False by default; for new content, use press CTRL+N/new document instead of modifying existing unsaved text." }
        }).ToArray()
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest _) => NIRACapabilityRisk.Modify;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request, CancellationToken cancellationToken = default)
    {
        string hwnd = NIRACapabilityArguments.RequireString(request, "windowHandle", 80);
        // Mutations require an exact live window identity: no title-only targeting.
        long handle = DesktopWindowResolver.ParseHandle(hwnd) ?? throw new ArgumentException("An exact HWND is required.");
        string? expectedProcess = NIRACapabilityArguments.GetOptionalString(request, "processName", 260);
        string? expectedTitle = NIRACapabilityArguments.GetOptionalString(request, "titleContains", 600);
        var w = _windows.ResolveExternalWindow(handle, expectedProcess, expectedTitle);
        if (!string.IsNullOrWhiteSpace(expectedProcess) &&
            !string.Equals(w.ProcessName, expectedProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? expectedProcess[..^4] : expectedProcess, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The exact HWND no longer belongs to the expected application.");
        if (!string.IsNullOrWhiteSpace(expectedTitle) &&
            !w.Title.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The exact HWND no longer has the expected window title.");
        var action = new NIRADesktopAction
        {
            Kind = NIRACapabilityArguments.RequireString(request, "action", 40),
            ControlName = NIRACapabilityArguments.GetOptionalString(request, "controlName", 180) ?? "",
            AutomationId = NIRACapabilityArguments.GetOptionalString(request, "automationId", 180) ?? "",
            ControlType = NIRACapabilityArguments.GetOptionalString(request, "controlType", 120) ?? "",
            Text = NIRACapabilityArguments.GetOptionalRawString(request, "text", 10000) ?? "",
            Key = NIRACapabilityArguments.GetOptionalString(request, "key", 80) ?? "",
            AllowExistingContent = request.Arguments.ValueKind == System.Text.Json.JsonValueKind.Object &&
                request.Arguments.TryGetProperty("allowExistingContent", out var allow) &&
                allow.ValueKind == System.Text.Json.JsonValueKind.True
        };
        NIRADesktopActionPolicy.Validate(action);
        if (action.Kind.Equals("type", StringComparison.OrdinalIgnoreCase))
            NIRADiagnosticTrace.Event("DESKTOP-TEXT-PREFLIGHT",
                $"HWND=0x{w.Handle:X} Chars={action.Text.Length} " +
                $"ProposedSha256={NIRADesktopTextEntryPolicy.TextFingerprint(action.Text)} " +
                "Rejected=False");
        NIRADesktopActionResult result = await _desktop.ActAsync(
            new IntPtr(w.Handle), w.ProcessId, action, cancellationToken);
        // Report independent postcondition evidence without logging user text.
        NIRADiagnosticTrace.Event("DESKTOP-ACTION-OUTCOME",
            $"Action={action.Kind} HWND=0x{w.Handle:X} " +
            $"Dispatched={result.ActionDispatched} PostconditionVerified={result.PostconditionVerified} " +
            $"TextChars={action.Text.Length} " +
            $"Proof={result.Evidence}");
        return new NIRACapabilityHandlerResult
        {
            Succeeded = result.ActionDispatched,
            ChangedSystemState = result.ActionDispatched,
            Summary = result.ActionDispatched
                ? $"Desktop action '{action.Kind}' dispatched to verified HWND=0x{w.Handle:X}; goal completion NOT established."
                : "Desktop action did not dispatch.",
            Output = $"WindowHandle: 0x{w.Handle:X}\nProcessId: {w.ProcessId}\n" +
                $"ActionDispatched: {result.ActionDispatched}\n" +
                $"ActionPostconditionVerified: {result.PostconditionVerified}\n" +
                "UserObjectiveComplete: NOT VERIFIED BY THIS ACTION.\n" + result.Evidence
        };
    }
}

internal static class DesktopWindowResolver
{
    internal static NIRACaptureWindowSnapshot Resolve(NIRAVisualEvidenceService windows,
        NIRACapabilityRequest request) => windows.ResolveExternalWindow(
            ParseHandle(NIRACapabilityArguments.GetOptionalString(request, "windowHandle", 80)),
            NIRACapabilityArguments.GetOptionalString(request, "processName", 260),
            NIRACapabilityArguments.GetOptionalString(request, "titleContains", 600));

    internal static long? ParseHandle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        bool hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (!long.TryParse(hex ? raw[2..] : raw,
            hex ? NumberStyles.HexNumber : NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long handle) || handle <= 0)
            throw new ArgumentException("windowHandle must be a positive decimal or 0x hexadecimal HWND.");
        return handle;
    }
}

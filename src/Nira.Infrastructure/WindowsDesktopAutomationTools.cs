using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Nira.Core;

namespace Nira.Infrastructure;

// The backend is independently injectable: the kernel and approvals are tested
// without performing automation against the developer's own desktop.
public sealed record DesktopWindow(string WindowId, string Title, string ProcessName,
    int ProcessId, bool IsForeground, bool IsMinimized);
public sealed record DesktopControl(string ControlId, string Name, string Kind,
    string AutomationId, bool Enabled, bool Offscreen, bool CanInvoke, bool CanSetText,
    bool CanTypeText = false);
public sealed record DesktopInspection(DesktopWindow Window,
    IReadOnlyList<DesktopControl> Controls, bool Truncated);
public sealed record DesktopAction(bool Success, bool Dispatched, string Summary,
    string? ErrorCode = null, bool? Verified = null,
    int? ObservedCharacters = null, string? VerificationStatus = null);

// Verification is exact, never a character-count or dispatch-only success test.
// Trim only the terminal line break a TextPattern provider may append to a
// single-line document. No observed document text is included in receipts.
public static class DesktopTextVerification
{
    public static bool Exact(string? observed, string expected) =>
        observed is not null && string.Equals(
            observed.TrimEnd('\r', '\n'), expected, StringComparison.Ordinal);
}

public interface IDesktopAutomationBackend
{
    IReadOnlyList<DesktopWindow> ListWindows();
    DesktopInspection Inspect(string windowId);
    DesktopAction Activate(string windowId);
    DesktopAction Invoke(string windowId, string controlId);
    DesktopAction SetText(string windowId, string controlId, string text);
    DesktopAction TypeText(string windowId, string controlId, string text);
    DesktopAction TypeText(string windowId, string controlId, string text,
        CancellationToken ct) => TypeText(windowId, controlId, text);
}

// Pure, fakeable policy: activation is requested by NIRA once, followed by
// bounded verification. It never converts a rejected focus into permission to type.
public static class DesktopForegroundAcquisition
{
    public static bool TryAcquire(Func<bool> isForeground, Func<bool> activate,
        Action wait, int checks = 20)
    {
        ArgumentNullException.ThrowIfNull(isForeground);
        ArgumentNullException.ThrowIfNull(activate);
        ArgumentNullException.ThrowIfNull(wait);
        if (checks < 1 || checks > 40) throw new ArgumentOutOfRangeException(nameof(checks));
        if (isForeground()) return true;
        _ = activate();
        for (int i = 0; i < checks; i++)
        {
            if (isForeground()) return true;
            wait();
        }
        return isForeground();
    }
}

internal static class DesktopToolInput
{
    public static string Window(JsonElement args) => args.GetProperty("windowId").GetString()!;
    public static string Control(JsonElement args) => args.GetProperty("controlId").GetString()!;
    public static string Target(JsonElement args, string operation) =>
        "desktop/" + operation + "/" + Window(args) +
        (args.TryGetProperty("controlId", out var id) ? "/" + id.GetString() : "");

    public static async Task<ToolReceipt> Run(Func<DesktopAction> action,
        CancellationToken ct)
    {
        try
        {
            DesktopAction result = await Task.Run(action, ct);
            return result.Success
                ? ToolReceipt.Ok(result.Summary, new { result.Dispatched, result.Verified,
                    result.ObservedCharacters, result.VerificationStatus },
                    changedState: result.Dispatched)
                : ToolReceipt.Fail(result.ErrorCode ?? "DESKTOP_ACTION_FAILED", result.Summary);
        }
        catch (DesktopAutomationException e)
        {
            return ToolReceipt.Fail(e.Code, e.Message);
        }
    }
}

public sealed class DesktopWindowsTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.windows_list",
        "List titled, visible top-level Windows applications on the user's current desktop; " +
        "never reads window content. Explicit host approval is required.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{},"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => "desktop/windows";
    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<DesktopWindow> windows = await Task.Run(desktop.ListWindows, ct);
            return ToolReceipt.Ok("Enumerated visible application windows", new { windows,
                count = windows.Count });
        }
        catch (DesktopAutomationException e) { return ToolReceipt.Fail(e.Code, e.Message); }
    }
}

public sealed class DesktopInspectTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.window_inspect",
        "Inspect bounded accessibility metadata in an observed window. Reveals control names " +
        "and supported actions but never reads edit-field values or passwords. Re-list if stale.",
        ToolRisk.ReadLocal, JsonToolSchema.Schema("""
        {"type":"object","properties":{"windowId":{"type":"string","minLength":8,"maxLength":100}},
         "required":["windowId"],"additionalProperties":false}
        """));
    public string GetTarget(JsonElement args) => DesktopToolInput.Target(args, "inspect");
    public async Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        try
        {
            DesktopInspection result = await Task.Run(() => desktop.Inspect(
                DesktopToolInput.Window(args)), ct);
            return ToolReceipt.Ok("Inspected accessible desktop controls", new {
                result.Window, result.Controls, result.Truncated, count = result.Controls.Count });
        }
        catch (DesktopAutomationException e) { return ToolReceipt.Fail(e.Code, e.Message); }
    }
}

public sealed class DesktopActivateTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.window_activate",
        "Bring the exact observed desktop window to the foreground. Requires an independent " +
        "host approval and fresh window identity validation. Does not click or type.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{"windowId":{"type":"string","minLength":8,"maxLength":100}},
         "required":["windowId"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => DesktopToolInput.Target(args, "activate");
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        DesktopToolInput.Run(() => desktop.Activate(DesktopToolInput.Window(args)), ct);
}

public sealed class DesktopInvokeTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.control_invoke",
        "Invoke ONE observed, enabled, visible accessibility button in the exact approved " +
        "window using UI Automation InvokePattern, without keyboard/mouse injection. " +
        "The target may be in the background because CLI approval prompts steal focus. " +
        "Requires separate host approval; app outcome may be unverified. " +
        "Never operates security prompts or NIRA's own windows.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "windowId":{"type":"string","minLength":8,"maxLength":100},
          "controlId":{"type":"string","minLength":8,"maxLength":100}},
         "required":["windowId","controlId"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => DesktopToolInput.Target(args, "invoke");
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        DesktopToolInput.Run(() => desktop.Invoke(DesktopToolInput.Window(args),
            DesktopToolInput.Control(args)), ct);
}

public sealed class DesktopSetTextTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.control_set_text",
        "Set text in ONE observed, enabled, visible, writable non-password Edit field of the " +
        "exact approved window via UI Automation ValuePattern, not global keystrokes. " +
        "May target a background window because CLI approval prompts steal focus. " +
        "Replaces the field's entire existing value; requires separate host approval. " +
        "Never retrieves prior values.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "windowId":{"type":"string","minLength":8,"maxLength":100},
          "controlId":{"type":"string","minLength":8,"maxLength":100},
          "text":{"type":"string","minLength":1,"maxLength":500}},
         "required":["windowId","controlId","text"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => DesktopToolInput.Target(args, "set-text") +
        "?characters=" + args.GetProperty("text").GetString()!.Length;
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct) =>
        DesktopToolInput.Run(() => desktop.SetText(DesktopToolInput.Window(args),
            DesktopToolInput.Control(args), args.GetProperty("text").GetString()!), ct);
}

/// <summary>
/// Explicit opt-in for rich editors that provide TextPattern but not a writable
/// ValuePattern. Sends Unicode keyboard input only after verifying the exact
/// previously observed window and control are foreground and keyboard-focused.
/// Does not provide a silent fallback from desktop.control_set_text.
/// </summary>
public sealed class DesktopTypeTextTool(IDesktopAutomationBackend desktop) : IAgentTool
{
    public ToolDescriptor Descriptor { get; } = new("desktop.control_type_text",
        "TYPE into one observed, verified-empty, non-password Edit/Document control through " +
        "foreground-only Windows Unicode keyboard input. NOT background text setting. " +
        "Use ONLY after explicit host approval for this exact target and text. " +
        "After approval, NIRA itself activates the exact observed window; " +
        "the user does not need to switch applications. Refuses wrong " +
        "window, stale or non-keyboard-focusable controls, or a non-empty editor. " +
        "Does not save documents or use shortcuts.",
        ToolRisk.Dangerous, JsonToolSchema.Schema("""
        {"type":"object","properties":{
          "windowId":{"type":"string","minLength":8,"maxLength":100},
          "controlId":{"type":"string","minLength":8,"maxLength":100},
          "text":{"type":"string","minLength":1,"maxLength":200},
          "requireEmpty":{"type":"boolean"}},
         "required":["windowId","controlId","text","requireEmpty"],"additionalProperties":false}
        """), HasSideEffects: true);
    public string GetTarget(JsonElement args) => DesktopToolInput.Target(args, "type-text") +
        "?characters=" + args.GetProperty("text").GetString()!.Length;
    public Task<ToolReceipt> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (args.GetProperty("requireEmpty").ValueKind != JsonValueKind.True)
            return Task.FromResult(ToolReceipt.Fail("EMPTY_REQUIRED",
                "Foreground typing requires requireEmpty=true; never overwrite an existing document."));
        string text = args.GetProperty("text").GetString()!;
        if (text.Any(char.IsControl))
            return Task.FromResult(ToolReceipt.Fail("TEXT_CHARACTERS_UNSUPPORTED",
                "Only printable text is supported; no control keys or newlines."));
        return DesktopToolInput.Run(() => desktop.TypeText(DesktopToolInput.Window(args),
            DesktopToolInput.Control(args), text, ct), ct);
    }
}

public sealed class DesktopAutomationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Bounded, same-interactive-session Windows UIA adapter. Window/control IDs are
/// observational identities, NOT permissions. The host revalidates them immediately
/// before each action. Every desktop tool targets a non-workspace URI so the existing
/// permission broker handles each operation, including reads.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopAutomationBackend : IDesktopAutomationBackend
{
    private const int MaxWindows = 80;
    private const int MaxControls = 100;
    private const int MaxVisitedNodes = 400;
    private const int MaxDepth = 5;
    private static readonly TreeWalker Walker = TreeWalker.ControlViewWalker;
    // Serialize NIRA-owned desktop foreground operations in this process.
    // This is NOT an OS-wide keyboard/mouse lock; the user retains an emergency exit.
    private static readonly object DesktopForegroundLease = new();

    public IReadOnlyList<DesktopWindow> ListWindows() => Enumerate().Select(x => x.Info).ToArray();

    public DesktopInspection Inspect(string windowId)
    {
        var window = ResolveWindow(windowId);
        var controls = ScanControls(window.Handle, windowId, out bool truncated);
        return new DesktopInspection(window.Info, controls.Select(c => c.Info).ToArray(), truncated);
    }

    public DesktopAction Activate(string windowId)
    {
        lock (DesktopForegroundLease)
        {
            var window = ResolveWindow(windowId);
            bool alreadyForeground = GetForegroundWindow() == window.Handle;
            if (!AcquireObservedForeground(window.Handle) ||
                ResolveWindow(windowId).Handle != window.Handle)
                return new DesktopAction(false, false,
                    "Windows prevented activation of the observed application window. " +
                    "NIRA did not click or type; retry only after conditions change.",
                    "FOREGROUND_DENIED");
            return new DesktopAction(true, !alreadyForeground,
                "Observed application activated and foreground identity verified", Verified: true);
        }
    }

    public DesktopAction Invoke(string windowId, string controlId)
    {
        var window = ResolveWindow(windowId);
        // CLI / WPF approval necessarily changes foreground focus. InvokePattern
        // targets this exact observed UIA element; it does not send global clicks.
        // Keep window/control identity and per-action authorization as the guard.
        var control = ResolveControl(window.Handle, windowId, controlId);
        if (!control.Info.CanInvoke || !control.Info.Enabled || control.Info.Offscreen)
            return new DesktopAction(false, false, "The target is not an actionable visible button", "CONTROL_NOT_INVOKABLE");
        if (!control.Element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern) ||
            pattern is not InvokePattern invoke)
            return new DesktopAction(false, false, "The button no longer supports InvokePattern", "CONTROL_CHANGED");
        // The originally approved window must still be the same live HWND,
        // process and title just before dispatch. Foreground is not identity.
        if (ResolveWindow(windowId).Handle != window.Handle)
            throw new DesktopAutomationException("WINDOW_STALE",
                "The approved window changed before the button could be invoked.");
        invoke.Invoke();
        return new DesktopAction(true, true,
            "Approved button invocation dispatched; the resulting application state is not verified",
            Verified: false);
    }

    public DesktopAction SetText(string windowId, string controlId, string text)
    {
        var window = ResolveWindow(windowId);
        // ValuePattern.SetValue addresses the exact observed UIA control, never
        // whichever window is foreground. Requiring foreground after a CLI
        // approval makes the approved operation fail by design.
        var control = ResolveControl(window.Handle, windowId, controlId);
        if (!control.Info.CanSetText || !control.Info.Enabled || control.Info.Offscreen)
        {
            bool textOnly = control.Info.CanTypeText && !control.Info.CanSetText;
            return new DesktopAction(false, false, textOnly
                ? "This editor supports TextPattern but not writable ValuePattern. " +
                  "Background set-text is unavailable. Only a separately approved " +
                  "foreground keyboard-input operation can type here."
                : "The observed control is not an enabled, writable non-password Edit field. " +
                  "Inspect CanSetText before attempting set-text.",
                textOnly ? "CONTROL_REQUIRES_KEYBOARD" : "CONTROL_NOT_EDITABLE");
        }
        if (!control.Element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern) ||
            pattern is not ValuePattern value || value.Current.IsReadOnly ||
            control.Element.Current.IsPassword)
            return new DesktopAction(false, false, "The edit control is now read-only or sensitive", "CONTROL_CHANGED");
        if (ResolveWindow(windowId).Handle != window.Handle)
            throw new DesktopAutomationException("WINDOW_STALE",
                "The approved window changed before text entry.");
        value.SetValue(text);
        // Only compare the string written in this approved operation. Never expose
        // prior or observed field text to the model, audit, or UI.
        bool verified;
        try { verified = string.Equals(value.Current.Value, text, StringComparison.Ordinal); }
        catch (ElementNotAvailableException) { verified = false; }
        return new DesktopAction(true, true, verified
            ? "Approved edit value set and independently verified"
            : "Approved edit value sent; read-back could not verify the result",
            Verified: verified);
    }

    // UIA TextPattern is read-only. This separate, explicitly approved operation
    // is limited to an empty, keyboard-focusable editor and the exact foreground
    // window. SendInput never targets a background app.
    public DesktopAction TypeText(string windowId, string controlId, string text) =>
        TypeText(windowId, controlId, text, CancellationToken.None);

    public DesktopAction TypeText(string windowId, string controlId, string text,
        CancellationToken ct)
    {
        lock (DesktopForegroundLease)
            return TypeTextOwned(windowId, controlId, text, ct);
    }

    private DesktopAction TypeTextOwned(string windowId, string controlId, string text,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var window = ResolveWindow(windowId);
        var control = ResolveControl(window.Handle, windowId, controlId);
        if (!control.Info.CanTypeText || !control.Info.Enabled || control.Info.Offscreen ||
            control.Element.Current.IsPassword)
            return new DesktopAction(false, false,
                "The selected control is not an eligible non-password keyboard editor.",
                "CONTROL_NOT_TYPEABLE");
        if (!control.Element.TryGetCurrentPattern(TextPattern.Pattern, out object? rawPattern) ||
            rawPattern is not TextPattern pattern)
            return new DesktopAction(false, false,
                "This editor no longer exposes TextPattern. Reinspect before acting.",
                "CONTROL_CHANGED");

        // Never expose existing contents. The operation refuses any nonempty editor.
        try
        {
            string initial = pattern.DocumentRange.GetText(16);
            if (!string.IsNullOrEmpty(initial.Trim('\r', '\n')))
                return new DesktopAction(false, false,
                    "Editor is not empty. No text was entered; existing content is preserved.",
                    "EDITOR_NOT_EMPTY");
        }
        catch (COMException)
        {
            return new DesktopAction(false, false,
                "Cannot verify that the editor is empty. No input was sent.",
                "EDITOR_STATE_UNKNOWN");
        }
        catch (ElementNotAvailableException)
        {
            return new DesktopAction(false, false,
                "Editor disappeared before input. Nothing was typed.", "CONTROL_CHANGED");
        }

        // Approval authorizes this exact window/control and the target's activation.
        // Do not ask the user to switch manually. Windows can still deny focus;
        // without verified foreground, NO global keyboard events may be sent.
        if (!AcquireObservedForeground(window.Handle))
        {
            // UI Automation can request focus on the exact approved editor when
            // standard Windows activation is not granted to a terminal-hosted CLI.
            // Neither route authorizes keyboard input until foreground is verified.
            try { control.Element.SetFocus(); }
            catch (ElementNotAvailableException) { /* Discovered control went stale. */ }
            catch (COMException) { /* Accessibility provider refused focus. */ }
            if (!AcquireObservedForeground(window.Handle))
                return new DesktopAction(false, false,
                    "Windows refused automatic activation of the exact approved window. " +
                    "NIRA did not send keyboard input; no user window switch required.",
                    "WINDOW_NOT_FOREGROUND");
        }
        if (GetForegroundWindow() != window.Handle || IsIconic(window.Handle))
            return new DesktopAction(false, false,
                "Approved window could not be verified foreground; no input sent.",
                "WINDOW_NOT_FOREGROUND");
        if (ResolveWindow(windowId).Handle != window.Handle)
            throw new DesktopAutomationException("WINDOW_STALE",
                "Observed window changed before keyboard dispatch.");
        var fresh = ResolveControl(window.Handle, windowId, controlId);
        if (!fresh.Info.CanTypeText || !fresh.Info.Enabled || fresh.Info.Offscreen)
            return new DesktopAction(false, false,
                "Editor accessibility identity changed before input. Nothing was typed.",
                "CONTROL_CHANGED");
        if (!fresh.Element.Current.HasKeyboardFocus)
        {
            fresh.Element.SetFocus();
            System.Threading.Thread.Sleep(80);
        }
        if (GetForegroundWindow() != window.Handle ||
            !fresh.Element.Current.HasKeyboardFocus)
            return new DesktopAction(false, false,
                "The exact approved editor does not hold keyboard focus. Nothing was typed.",
                "CONTROL_NOT_FOCUSED");
        // Recheck emptiness immediately before the first key event.
        if (!fresh.Element.TryGetCurrentPattern(TextPattern.Pattern, out object? freshPattern) ||
            freshPattern is not TextPattern freshText)
            return new DesktopAction(false, false,
                "TextPattern disappeared before input. Nothing was typed.", "CONTROL_CHANGED");
        if (!string.IsNullOrEmpty(freshText.DocumentRange.GetText(16).Trim('\r', '\n')))
            return new DesktopAction(false, false,
                "Editor gained content before input; no keyboard event was sent.", "EDITOR_NOT_EMPTY");
        if (GetForegroundWindow() != window.Handle)
            return new DesktopAction(false, false,
                "Target lost foreground before dispatch; nothing was typed.", "WINDOW_NOT_FOREGROUND");

        // Bound the global keyboard-dispatch interval. Recheck foreground and
        // exact UIA focus for each code unit, so user interference stops further
        // input instead of forwarding the rest to an unrelated application.
        // Once any event is accepted, the result is uncertain if interrupted;
        // never issue an automatic second typing attempt.
        bool anyDispatched = false;
        for (int index = 0; index < text.Length; index++)
        {
            if (ct.IsCancellationRequested)
                return anyDispatched
                    ? new DesktopAction(true, true,
                        "Task paused after partial keyboard input; document may have changed. " +
                        "No more characters were sent and no retry is allowed.",
                        Verified: false, VerificationStatus: "interrupted")
                    : new DesktopAction(false, false,
                        "Task paused before keyboard dispatch.", "TASK_CANCELED");
            if (GetForegroundWindow() != window.Handle ||
                !fresh.Element.Current.HasKeyboardFocus)
            {
                if (anyDispatched)
                    return new DesktopAction(true, true,
                        "Foreground or editor focus changed during text entry; " +
                        "the document may have been partially modified. No retry performed.",
                        Verified: false, VerificationStatus: "interrupted");
                return new DesktopAction(false, false,
                    "Foreground or editor focus changed before text entry.", "CONTROL_NOT_FOCUSED");
            }
            var inputs = new[] { UnicodeInput(text[index], keyUp: false),
                UnicodeInput(text[index], keyUp: true) };
            uint inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
            if (inserted != (uint)inputs.Length)
            {
                // A Win32 return of zero is not proof no previous key down happened.
                // Keep the host's possible side-effect receipt, even on the first key.
                return new DesktopAction(true, true,
                    "Windows accepted an uncertain number of keyboard events. " +
                    "The target document may have changed; no retry performed.",
                    Verified: false, VerificationStatus: "incomplete");
            }
            anyDispatched = true;
        }

        // SendInput's return count attests that Windows accepted the keyboard
        // events, not that the target editor inserted the intended characters. Providers
        // may update their TextPattern ranges asynchronously; reacquire the
        // exact control and compare the resulting text up to four times.
        // A mismatch is NEVER silently retried with another typing operation.
        bool verified = false;
        int? observedCharacters = null;
        bool readable = false;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            System.Threading.Thread.Sleep(180);
            try
            {
                if (ResolveWindow(windowId).Handle != window.Handle) break;
                var current = ResolveControl(window.Handle, windowId, controlId);
                if (!current.Element.TryGetCurrentPattern(TextPattern.Pattern, out object? readPattern) ||
                    readPattern is not TextPattern reader) break;
                string observed = reader.DocumentRange.GetText(text.Length + 64);
                readable = true;
                observedCharacters = observed.TrimEnd('\r', '\n').Length;
                if (DesktopTextVerification.Exact(observed, text))
                {
                    verified = true;
                    break;
                }
            }
            catch (COMException) { /* Input was dispatched; content cannot be verified. */ }
            catch (ElementNotAvailableException) { /* Control may have changed. */ }
            catch (DesktopAutomationException) { /* Window/control identity changed. */ }
        }
        string status = verified ? "exact_match" : readable ? "mismatch" : "unavailable";
        return new DesktopAction(true, true, verified
            ? "Approved foreground text typed and independently verified through TextPattern"
            : readable
                ? "Approved keyboard events dispatched, but editor text differs from the requested text"
                : "Approved keyboard events dispatched; editor contents could not be read back",
            Verified: verified, ObservedCharacters: observedCharacters,
            VerificationStatus: status);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)] public NativeMouseInput Mouse;
        [FieldOffset(0)] public NativeKeyboardInput Keyboard;
        [FieldOffset(0)] public NativeHardwareInput Hardware;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeHardwareInput
    {
        public uint Message;
        public ushort ParamLow, ParamHigh;
    }
    private static NativeInput UnicodeInput(char value, bool keyUp) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Data = new NativeInputUnion { Keyboard = new NativeKeyboardInput {
            ScanCode = value, Flags = keyUp ? 0x0006u : 0x0004u // KEYEVENTF_UNICODE|KEYUP
        } }
    };
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint number, [In] NativeInput[] events, int size);

    // Generic foreground acquisition: one attempt to restore/activate the exact
    // observed HWND, with bounded observation. Uses Windows' regular foreground
    // policy; never targets consent/UAC desktops, never suppresses user input.
    private static bool AcquireObservedForeground(IntPtr handle)
    {
        if (DesktopForegroundAcquisition.TryAcquire(
            () => IsWindow(handle) && !IsIconic(handle) && GetForegroundWindow() == handle,
            () =>
            {
                if (!IsWindow(handle)) return false;
                if (IsIconic(handle)) _ = ShowWindow(handle, 9); // SW_RESTORE
                return SetForegroundWindow(handle);
            },
            () => System.Threading.Thread.Sleep(50))) return true;

        // A CLI hosted inside VS Code may not own the foreground thread.
        // Windows allows a brief input-queue attachment on the SAME desktop;
        // detach it in finally before any keyboard input is generated. Failure
        // never relaxes the exact-foreground requirement or target identity.
        if (!IsWindow(handle)) return false;
        IntPtr currentForeground = GetForegroundWindow();
        if (currentForeground == IntPtr.Zero) return false;
        uint foregroundThread = GetWindowThreadProcessId(currentForeground, out _);
        uint ownThread = GetCurrentThreadId();
        if (foregroundThread == 0 || ownThread == foregroundThread ||
            !AttachThreadInput(ownThread, foregroundThread, true)) return false;
        try
        {
            if (IsIconic(handle)) _ = ShowWindow(handle, 9);
            _ = SetForegroundWindow(handle);
        }
        finally { _ = AttachThreadInput(ownThread, foregroundThread, false); }
        return IsWindow(handle) && !IsIconic(handle) && GetForegroundWindow() == handle;
    }

    private static (IntPtr Handle, DesktopWindow Info) ResolveWindow(string windowId)
    {
        foreach (var window in Enumerate())
            if (string.Equals(window.Info.WindowId, windowId, StringComparison.Ordinal))
                return window;
        throw new DesktopAutomationException("WINDOW_STALE",
            "The window identity is missing or changed. List windows again.");
    }

    private static (AutomationElement Element, DesktopControl Info) ResolveControl(
        IntPtr handle, string windowId, string controlId)
    {
        var matches = ScanControls(handle, windowId, out _)
            .Where(c => c.Info.ControlId == controlId).Take(2).ToArray();
        if (matches.Length != 1)
            throw new DesktopAutomationException("CONTROL_STALE_OR_DUPLICATE",
                "The accessibility control disappeared or is ambiguous. Reinspect before acting.");
        return matches[0];
    }

    private static List<(AutomationElement Element, DesktopControl Info)> ScanControls(
        IntPtr hwnd, string windowId, out bool truncated)
    {
        truncated = false;
        var controls = new List<(AutomationElement Element, DesktopControl Info)>();
        AutomationElement root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch (ElementNotAvailableException)
        { throw new DesktopAutomationException("WINDOW_STALE", "The window is no longer accessible."); }

        var pending = new Queue<(AutomationElement Element, int Depth)>();
        pending.Enqueue((root, 0));
        int visited = 0;
        try
        {
            while (pending.Count > 0 && visited < MaxVisitedNodes)
            {
                var (element, depth) = pending.Dequeue();
                visited++;
                try
                {
                    var current = element.Current;
                    int[]? runtimeId = element.GetRuntimeId();
                    if (runtimeId is { Length: > 0 })
                    {
                        string kind = current.ControlType.ProgrammaticName;
                        string name = Bound(current.Name, 150);
                        string automationId = Bound(current.AutomationId, 120);
                        bool enabled = current.IsEnabled;
                        bool offscreen = current.IsOffscreen;
                        bool isPassword = current.IsPassword;
                        bool canInvoke = !isPassword && enabled && !offscreen &&
                            current.ControlType == ControlType.Button &&
                            element.TryGetCurrentPattern(InvokePattern.Pattern, out _);
                        bool canSetText = !isPassword && enabled && !offscreen &&
                            current.ControlType == ControlType.Edit &&
                            element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePattern) &&
                            valuePattern is ValuePattern value && !value.Current.IsReadOnly;
                        // Rich editors expose read-only TextPattern, not ValuePattern.
                        // Advertise a separate, foreground-only, explicitly approved
                        // input tool. Do not read document content during inspection.
                        // Require the TextPattern and either Edit or Document role.
                        bool canTypeText = !isPassword && enabled && !offscreen &&
                            current.IsKeyboardFocusable &&
                            (current.ControlType == ControlType.Edit ||
                             current.ControlType == ControlType.Document) &&
                            element.TryGetCurrentPattern(TextPattern.Pattern, out _);
                        // Read only metadata. Never read ValuePattern.Current.Value while inspecting.
                        string signature = windowId + ":" + string.Join(".", runtimeId) + ":" +
                            kind + ":" + current.Name + ":" + current.AutomationId;
                        string controlId = "ctrl-" + Convert.ToHexString(
                            SHA256.HashData(Encoding.UTF8.GetBytes(signature)).AsSpan(0, 12)).ToLowerInvariant();
                        if (!isPassword && controls.Count < MaxControls)
                            controls.Add((element, new DesktopControl(controlId, name,
                                kind, automationId, enabled, offscreen, canInvoke, canSetText, canTypeText)));
                        else if (controls.Count >= MaxControls) truncated = true;
                    }
                    if (depth >= MaxDepth) continue;
                    // Walk children incrementally to avoid FindAll over an unbounded desktop tree.
                    AutomationElement? child = Walker.GetFirstChild(element);
                    int siblings = 0;
                    while (child is not null && siblings++ < 160 &&
                           pending.Count + visited < MaxVisitedNodes)
                    {
                        pending.Enqueue((child, depth + 1));
                        child = Walker.GetNextSibling(child);
                    }
                    if (child is not null) truncated = true;
                }
                catch (ElementNotAvailableException) { /* Application changed during inspection. */ }
            }
        }
        catch (COMException)
        { throw new DesktopAutomationException("UIA_UNAVAILABLE", "The application did not provide a stable UI Automation tree."); }
        if (pending.Count > 0) truncated = true;
        // Duplicate identities can happen with a broken accessibility provider.
        // Never expose an ID as actionable if it is ambiguous.
        var duplicated = controls.GroupBy(c => c.Info.ControlId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicated.Count > 0)
            controls = controls.Select(c => duplicated.Contains(c.Info.ControlId)
                ? (c.Element, c.Info with { CanInvoke = false, CanSetText = false, CanTypeText = false }) : c).ToList();
        return controls;
    }

    private static IReadOnlyList<(IntPtr Handle, DesktopWindow Info)> Enumerate()
    {
        var result = new List<(IntPtr, DesktopWindow)>();
        IntPtr foreground = GetForegroundWindow();
        int ownPid = Environment.ProcessId;
        _ = EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || !IsWindow(hwnd)) return true;
            uint rawPid = 0;
            GetWindowThreadProcessId(hwnd, out rawPid);
            if (rawPid == 0 || rawPid == (uint)ownPid || rawPid > int.MaxValue) return true;
            var builder = new StringBuilder(512);
            GetWindowText(hwnd, builder, builder.Capacity);
            string title = builder.ToString().Trim();
            if (title.Length == 0) return true;
            int pid = (int)rawPid;
            string processName = "unknown";
            try { using Process process = Process.GetProcessById(pid); processName = process.ProcessName; }
            catch (ArgumentException) { return true; }
            catch (InvalidOperationException) { return true; }
            catch (Win32Exception) { /* Do not force elevated access. */ }
            // Exclude elevated consent/security shell processes and NIRA's own app.
            if (processName.Equals("consent", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("NIRA", StringComparison.OrdinalIgnoreCase)) return true;
            string key = hwnd.ToInt64().ToString("x") + "-" + pid.ToString("x") + "-" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(title))
                    .AsSpan(0, 6)).ToLowerInvariant();
            result.Add((hwnd, new DesktopWindow(key, Bound(title, 180), processName,
                pid, foreground == hwnd, IsIconic(hwnd))));
            return result.Count < MaxWindows;
        }, IntPtr.Zero);
        return result;
    }

    private static string Bound(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max];

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint fromThread, uint toThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
}

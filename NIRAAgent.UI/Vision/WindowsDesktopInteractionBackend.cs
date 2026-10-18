using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using NIRAAgent.Vision;

namespace NIRAAgent.UI.Vision;

// General desktop UI automation. No application-title tables, hardcoded test
// routes, browser DOM, OS elevation or clipboard modification. Every action
// is tied to a verified external HWND/PID and a freshly resolved UIA element.
public sealed class WindowsDesktopInteractionBackend : INIRADesktopInteractionBackend
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    // A waiting foreground task must not block background-safe UIA inspections
    // or modifications on another application.
    private readonly SemaphoreSlim _foregroundSerial = new(1, 1);
    private readonly NIRAForegroundWorkCoordinator _foreground = new();
    public async Task<string> InspectAsync(IntPtr hwnd, int pid,
        CancellationToken cancellationToken = default)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            return await Task.Run(() => Inspect(hwnd, pid, deadline.Token), deadline.Token)
                .WaitAsync(TimeSpan.FromSeconds(9), cancellationToken);
        }
        finally { _serial.Release(); }
    }

    public async Task<NIRADesktopActionResult> ActAsync(IntPtr hwnd, int pid,
        NIRADesktopAction action, CancellationToken cancellationToken = default)
    {
        NIRADesktopActionPolicy.Validate(action);
        SemaphoreSlim executionGate = NIRADesktopExecutionStrategy.NeedsForeground(action.Kind)
            ? _foregroundSerial : _serial;
        await executionGate.WaitAsync(cancellationToken);
        try
        {
            // Foreground tasks may legitimately wait while the owner works in
            // another app. Waiting must not consume the normal action timeout
            // or a model-call budget. The run's own cancellation stays live.
            if (NIRADesktopExecutionStrategy.NeedsForeground(action.Kind))
                return await Task.Run(() => Act(hwnd, pid, action, cancellationToken), cancellationToken);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Long Unicode input is paced to the receiving application's UI thread.
            // The timeout includes dispatch AND a bounded post-input readback.
            int inputChunks = action.Kind.Equals("type", StringComparison.OrdinalIgnoreCase)
                ? (action.Text.Length + 3) / 4 : 0;
            TimeSpan budget = TimeSpan.FromSeconds(Math.Clamp(10 + inputChunks / 9, 10, 55));
            deadline.CancelAfter(budget);
            return await Task.Run(() => Act(hwnd, pid, action, deadline.Token), deadline.Token)
                .WaitAsync(budget + TimeSpan.FromSeconds(1), cancellationToken);
        }
        finally { executionGate.Release(); }
    }

    private static AutomationElement Root(IntPtr hwnd, int pid)
    {
        if (hwnd == IntPtr.Zero || pid < 1 || pid == Environment.ProcessId || !IsWindow(hwnd))
            throw new InvalidOperationException("A live external window is required.");
        GetWindowThreadProcessId(hwnd, out uint actual);
        if (actual != (uint)pid)
            throw new InvalidOperationException("The target HWND changed process identity.");
        return AutomationElement.FromHandle(hwnd)
            ?? throw new InvalidOperationException("Target window has no accessibility root.");
    }

    private static string Inspect(IntPtr hwnd, int pid, CancellationToken ct)
    {
        AutomationElement root = Root(hwnd, pid);
        StringBuilder sb = new();
        int count = 0;
        bool truncated = false;
        foreach (var (element, depth) in Enumerate(root, ct))
        {
            try
            {
                string type = element.Current.ControlType?.ProgrammaticName ?? "Unknown";
                string name = Clean(element.Current.Name, 180);
                string id = Clean(element.Current.AutomationId, 180);
                bool editor = element.Current.ControlType == ControlType.Edit ||
                    element.Current.ControlType == ControlType.Document;
                if (name.Length == 0 && id.Length == 0 && !editor) continue;
                bool enabled = element.Current.IsEnabled;
                bool selected = element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection) &&
                    selection is SelectionItemPattern item && item.Current.IsSelected;
                sb.Append("Depth=").Append(depth).Append(" | ControlType='").Append(type)
                    .Append("' | Name='").Append(name).Append("' | AutomationId='")
                    .Append(id).Append("' | Enabled=").Append(enabled);
                if (selected) sb.Append(" | IsSelected=True");
                if (editor) sb.Append(" | HasKeyboardFocus=").Append(element.Current.HasKeyboardFocus);
                if (editor && !element.Current.IsPassword)
                {
                    var readable = ReadEditorText(element);
                    if (readable is not null)
                    {
                        sb.Append(" | EditorTextLength=").Append(readable.Length);
                        sb.Append(" | EditorTextSha256=")
                            .Append(NIRADesktopTextEntryPolicy.TextFingerprint(readable));
                        sb.Append(" | EditorTextPreview='").Append(Clean(readable, 2800)).Append('\'');
                        if (readable.Length > 2800) sb.Append(" | PreviewTruncated=True");
                    }
                    else sb.Append(" | EditorTextReadable=False");
                }
                else if (!element.Current.IsPassword &&
                    element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern) &&
                    valuePattern is ValuePattern v)
                    sb.Append(" | Value='").Append(Clean(v.Current.Value, 200)).Append('\'');
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out _)) sb.Append(" | CanInvoke=True");
                if (element.TryGetCurrentPattern(TogglePattern.Pattern, out _)) sb.Append(" | CanToggle=True");
                sb.AppendLine();
                count++;
                if (sb.Length > 14500) { truncated = true; break; }
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
        }
        Root(hwnd, pid); // Identity unchanged throughout inspection.
        if (count == 0) throw new InvalidOperationException("No readable controls exposed by this window.");
        return "NIRA VERIFIED DESKTOP CONTROLS\n" +
            "Names/AutomationIds and ControlType are exact selectors for desktop.act; duplicate matches are rejected.\n" +
            "This control list does not prove a directory exists on disk.\n" +
            "Truncated: " + truncated + "\n" + sb;
    }

    private NIRADesktopActionResult Act(IntPtr hwnd, int pid,
        NIRADesktopAction a, CancellationToken ct)
    {
        AutomationElement root = Root(hwnd, pid);
        ct.ThrowIfCancellationRequested();
        string kind = a.Kind.Trim().ToLowerInvariant();
        if (kind == "activate")
        {
            bool focused = BringForward(hwnd);
            return new NIRADesktopActionResult
            {
                ActionDispatched = focused, PostconditionVerified = focused,
                Evidence = "ForegroundVerified=" + focused + "; exact window identity rechecked."
            };
        }
        if (kind == "close")
        {
            // A real app close, never an administrator-privilege workaround.
            // Prefer the supported UI Automation WindowPattern when present;
            // some Windows apps expose this while rejecting direct window messages.
            ct.ThrowIfCancellationRequested();
            root = Root(hwnd, pid);
            bool dispatched = false;
            string transport = "None";
            if (root.TryGetCurrentPattern(WindowPattern.Pattern, out object windowPattern) &&
                windowPattern is WindowPattern window)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    window.Close();
                    dispatched = true;
                    transport = "UIAutomation.WindowPattern";
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
                {
                    // E_ACCESSDENIED means that this transport refused the call.
                    // A normal WM_CLOSE may still work for a same-integrity window.
                }
                // Any other provider failure has unknown side effects. Never
                // silently send a second close message after such a failure.
            }
            if (!dispatched)
            {
                ct.ThrowIfCancellationRequested();
                if (!PostMessage(hwnd, 0x0010u, IntPtr.Zero, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 5)
                        throw new NIRADesktopAccessDeniedException(
                            "WINDOW_CLOSE_ACCESS_DENIED: Windows denied a normal close request " +
                            "to the verified window (Win32=5). It may be protected or run " +
                            "at a higher integrity level. No close message was posted. " +
                            "Do not retry unchanged or bypass Windows elevation; report the barrier.");
                    throw new NIRADesktopNoInputException(
                        $"WINDOW_CLOSE_NOT_POSTED: Windows rejected WM_CLOSE (Win32={error}). " +
                        "No close message was posted; check the target identity.");
                }
                dispatched = true;
                transport = "Win32.WM_CLOSE";
            }
            // A local bounded wait lets an ordinary close finish without paying
            // for another language-model round. An unsaved-content prompt may
            // legitimately leave this window open.
            for (int i = 0; i < 6 && IsWindow(hwnd); i++)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(100);
            }
            bool windowGone = !IsWindow(hwnd);
            return new NIRADesktopActionResult
            {
                ActionDispatched = dispatched,
                PostconditionVerified = windowGone,
                Evidence = $"CloseTransport={transport}; OriginalWindowGone={windowGone}; " +
                    "ProcessExitNotVerified=True. A surviving window may require an " +
                    "ordinary user dialog. Process liveness is separate from HWND state."
            };
        }
        if (kind == "press")
        {
            KeyChord chord = KeyChord.Parse(a.Key);
            ct.ThrowIfCancellationRequested();
            _foreground.AwaitTarget(hwnd, pid, ct);
            Root(hwnd, pid);
            SendChord(chord, hwnd);
            return new NIRADesktopActionResult
            {
                ActionDispatched = true, PostconditionVerified = false,
                Evidence = "Key chord delivered to foreground-verified HWND; inspect application state to confirm its effect."
            };
        }
        // An unnamed editor can legitimately be the focused control. Before
        // sending anything, prove its HWND ancestry, editable type and current
        // state. Existing unsaved content is not an implicit append target.
        if (kind == "type" && a.ControlName.Length == 0 && a.AutomationId.Length == 0)
        {
            _foreground.AwaitTarget(hwnd, pid, ct);
            root = Root(hwnd, pid);
            AutomationElement focused = AutomationElement.FocusedElement
                ?? throw new InvalidOperationException("Windows has no readable focused UI control.");
            if (!IsFocusedEditableDescendant(focused, root, pid, ct))
                throw new NIRADesktopNoInputException("The focused editor is not a verified editable descendant of the target HWND. No text was typed.");
            return TypeIntoEditor(hwnd, pid, focused, a, ct);
        }
        AutomationElement element = FindUnique(root, a, ct);
        ct.ThrowIfCancellationRequested();
        Root(hwnd, pid);
        if (element.Current.IsPassword)
            throw new InvalidOperationException("Password fields require a separate trusted credential flow; plain model-authored desktop text is not permitted.");
        if (!element.Current.IsEnabled) throw new InvalidOperationException("The selected UI control is disabled.");
        bool verified = false;
        ct.ThrowIfCancellationRequested();
        switch (kind)
        {
            case "invoke":
                if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out object invocation) ||
                    invocation is not InvokePattern invoke)
                    throw new InvalidOperationException("This control does not support the Invoke pattern.");
                ct.ThrowIfCancellationRequested();
                invoke.Invoke();
                break;
            case "select":
                if (!element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selection) ||
                    selection is not SelectionItemPattern item)
                    throw new InvalidOperationException("This control does not support selection.");
                ct.ThrowIfCancellationRequested();
                item.Select();
                verified = item.Current.IsSelected;
                break;
            case "toggle":
                if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out object toggled) ||
                    toggled is not TogglePattern toggle)
                    throw new InvalidOperationException("This control does not support Toggle.");
                var previous = toggle.Current.ToggleState;
                ct.ThrowIfCancellationRequested();
                toggle.Toggle();
                verified = toggle.Current.ToggleState != previous;
                break;
            case "expand":
            case "collapse":
                if (!element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object expanded) ||
                    expanded is not ExpandCollapsePattern expand)
                    throw new InvalidOperationException("This control does not support ExpandCollapse.");
                ct.ThrowIfCancellationRequested();
                if (kind == "expand") expand.Expand(); else expand.Collapse();
                verified = expand.Current.ExpandCollapseState ==
                    (kind == "expand" ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
                break;
            case "setvalue":
                if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out object valueObject) ||
                    valueObject is not ValuePattern value || value.Current.IsReadOnly)
                    throw new InvalidOperationException("This field has no editable ValuePattern; inspect and use 'type' if appropriate.");
                ct.ThrowIfCancellationRequested();
                value.SetValue(a.Text);
                verified = string.Equals(value.Current.Value, a.Text, StringComparison.Ordinal);
                break;
            case "focus":
                _foreground.AwaitTarget(hwnd, pid, ct);
                element.SetFocus();
                verified = element.Current.HasKeyboardFocus;
                break;
            case "type":
                // A genuinely accessible editable UIA control can be updated
                // in the background when it is proven blank. No focus change,
                // SendInput or clipboard interference is required.
                if (!a.AllowExistingContent && element.Current.ControlType == ControlType.Edit &&
                    ReadEditorText(element) is string original && original.Length == 0 &&
                    element.TryGetCurrentPattern(ValuePattern.Pattern, out object native) &&
                    native is ValuePattern backgroundValue && !backgroundValue.Current.IsReadOnly)
                {
                    try { ct.ThrowIfCancellationRequested(); backgroundValue.SetValue(a.Text); }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        throw new NIRADesktopUncertainInputException(
                            "Background UI Automation write may have taken effect (HRESULT=" +
                            ex.ErrorCode.ToString("X8") + "). Inspect before recovery.");
                    }
                    return VerifyTextDelivery(element, original, a.Text,
                        "BackgroundValuePattern", ct);
                }
                // 'type' must NOT replace an existing document through
                // ValuePattern.SetValue. 'setValue' is the explicit replacement
                // operation; normal typing inserts at the editor's caret.
                _foreground.AwaitTarget(hwnd, pid, ct);
                if (!element.Current.IsKeyboardFocusable) throw new InvalidOperationException("Control cannot accept keyboard focus.");
                element.SetFocus();
                root = Root(hwnd, pid);
                AutomationElement focused = AutomationElement.FocusedElement
                    ?? throw new InvalidOperationException("Focused editor became unavailable.");
                if (!IsFocusedEditableDescendant(focused, root, pid, ct))
                    throw new NIRADesktopNoInputException("Focused control is not a verified editor inside the target window. No text was typed.");
                return TypeIntoEditor(hwnd, pid, focused, a, ct);
            case "click":
                _foreground.AwaitTarget(hwnd, pid, ct);
                element.SetFocus();
                var rect = element.Current.BoundingRectangle;
                if (rect.IsEmpty || rect.Width < 2 || rect.Height < 2)
                    throw new InvalidOperationException("The target control has no usable visible bounds.");
                int x = checked((int)Math.Round(rect.Left + rect.Width / 2));
                int y = checked((int)Math.Round(rect.Top + rect.Height / 2));
                var hit = WindowFromPoint(new NativePoint(x, y));
                if (hit == IntPtr.Zero || GetAncestor(hit, 2) != hwnd)
                    throw new InvalidOperationException("The requested control is occluded by another window; no click was sent.");
                Root(hwnd, pid);
                if (!SetCursorPos(x, y)) throw new InvalidOperationException("Cannot position pointer on selected control.");
                ct.ThrowIfCancellationRequested();
                if (GetForegroundWindow() != hwnd)
                    throw new InvalidOperationException("Target lost foreground before click; no click sent.");
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                break;
            default: throw new ArgumentException("Unsupported action kind.");
        }
        // A control may disappear as the desired effect; revalidate the HWND,
        // never imply that a UIA pattern call completed the whole user task.
        Root(hwnd, pid);
        return new NIRADesktopActionResult
        {
            ActionDispatched = true, PostconditionVerified = verified,
            Evidence = $"Dispatched={kind}; matched exact ControlType='{a.ControlType}', " +
                $"Name='{a.ControlName}', AutomationId='{a.AutomationId}'. " +
                (verified ? "Selected control postcondition observed." :
                    "Control effect not independently verified; inspect resulting application/document before completion.")
        };
    }

    private NIRADesktopActionResult TypeIntoEditor(IntPtr hwnd, int pid,
        AutomationElement focused, NIRADesktopAction a, CancellationToken ct)
    {
        // Read-only UIA is useful as a precondition, but not all applications
        // expose their document contents. Require positive proof that a new
        // document is empty; explicit appending to a populated editor remains
        // possible when the user has requested that operation.
        string? before = ReadEditorText(focused);
        if (!a.AllowExistingContent)
        {
            if (before is null)
                throw new NIRADesktopNoInputException("Cannot establish that the editor is blank. Inspect the target or specify an explicitly authorized append; no text was sent.");
            if (before.Length != 0)
                throw new NIRADesktopNoInputException("This editor already contains text. Preserve the existing document: create a new blank document or explicitly choose an authorized append; no text was sent.");
        }
        ct.ThrowIfCancellationRequested();
        Root(hwnd, pid);
        AutomationElement? currentFocus = AutomationElement.FocusedElement;
        if (GetForegroundWindow() != hwnd || currentFocus is null ||
            !IsFocusedEditableDescendant(currentFocus, Root(hwnd, pid), pid, ct))
            throw new NIRADesktopNoInputException("Target editor lost verified focus. No text was typed.");
        if (before is not null && !string.Equals(ReadEditorText(focused), before,
            StringComparison.Ordinal))
            throw new NIRADesktopNoInputException(
                "Editor content changed since preflight; no text was sent.");
        // A writable UIA ValuePattern is a first-class editor API. Replacing
        // the value of a PROVEN EMPTY document is equivalent to an insertion
        // and avoids keyboard timing/layout corruption. Never use this path to
        // overwrite a populated document or an unknown/unsaved editor.
        if (before is not null && before.Length == 0 &&
            focused.TryGetCurrentPattern(ValuePattern.Pattern, out object valueObject) &&
            valueObject is ValuePattern nativeValue && !nativeValue.Current.IsReadOnly)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                nativeValue.SetValue(a.Text);
            }
            catch (COMException ex)
            {
                throw new NIRADesktopUncertainInputException(
                    "Native editor rejected the write after dispatch (HRESULT=" +
                    ex.ErrorCode.ToString("X8") + "); inspect content before retrying.");
            }
            return VerifyTextDelivery(focused, before, a.Text, "NativeValuePattern", ct);
        }

        if (before is not null && before.Length == 0 &&
            TryNativeEmptyEditWrite(hwnd, pid, focused, a.Text, ct))
            return VerifyTextDelivery(focused, before, a.Text, "NativeWin32Edit", ct);

        // An explicitly authorized insertion into an unreadable editor still
        // works, but its postcondition MUST remain uncertain. No destructive
        // retry is permitted, and no observed content is fabricated.
        if (before is null)
        {
            SendText(a.Text, hwnd, pid, ct, focused, before);
            return new NIRADesktopActionResult
            {
                ActionDispatched = true,
                PostconditionVerified = false,
                Evidence = "TextMethod=PacedUnicode; EditorTextReadable=False; " +
                    "input dispatched once; actual content needs separate verification."
            };
        }

        // Keyboard-only providers receive short Unicode batches. Pause for
        // exact readback at checkpoints: a bad payload cannot flood the rest
        // of the document while the UI is lagging. NEVER repeat a batch whose
        // effect is uncertain. The caller blocks any destructive recovery.
        const int checkpointChars = 24;
        int sent = 0;
        while (sent < a.Text.Length)
        {
            int count = Math.Min(checkpointChars, a.Text.Length - sent);
            SendText(a.Text.Substring(sent, count), hwnd, pid, ct, focused, before + a.Text[..sent]);
            sent += count;
            // When the owner changes applications, wait here without another
            // cognition cycle; do not interpret lost foreground as bad text.
            _foreground.AwaitTarget(hwnd, pid, ct);
            Root(hwnd, pid);
            bool checkpointValid = WaitForText(focused, before, a.Text[..sent], ct, 8,
                out string? observed);
            if (!checkpointValid && GetForegroundWindow() != hwnd)
            {
                _foreground.AwaitTarget(hwnd, pid, ct);
                checkpointValid = WaitForText(focused, before, a.Text[..sent], ct, 8,
                    out observed);
            }
            if (!checkpointValid)
            {
                return new NIRADesktopActionResult
                {
                    ActionDispatched = true,
                    PostconditionVerified = false,
                    Evidence = $"Unicode checkpoint not verified; DispatchedPrefixLength={sent}; " +
                        $"ObservedLength={(observed is null ? -1 : observed.Length)}; " +
                        "further input suppressed; inspect without rewriting."
                };
            }
        }
        return VerifyTextDelivery(focused, before, a.Text, "PacedUnicode", ct);
    }

    // WM_SETTEXT is reserved for a positively identified native Win32 edit
    // child (Edit/RichEdit), not generic windows. It can change a document only
    // when the full editor was proven blank and the exact PID/HWND was checked.
    // It never touches the system clipboard or relies on keyboard layouts.
    private static bool TryNativeEmptyEditWrite(IntPtr hwnd, int pid,
        AutomationElement editor, string text, CancellationToken ct)
    {
        int raw = editor.Current.NativeWindowHandle;
        if (raw == 0) return false;
        IntPtr child = new(raw);
        if (child == hwnd || !IsWindow(child) || !IsChild(hwnd, child)) return false;
        GetWindowThreadProcessId(child, out uint actualPid);
        if (actualPid != (uint)pid) return false;
        StringBuilder cls = new(256);
        if (GetClassName(child, cls, cls.Capacity) == 0 ||
            !cls.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase))
            return false;
        ct.ThrowIfCancellationRequested();
        if (GetForegroundWindow() != hwnd)
            throw new NIRADesktopNoInputException("Target lost foreground before native editor input.");
        // A SendMessageTimeout failure can still have a partial effect;
        // never follow it with a second transport or a blind retry.
        IntPtr response;
        IntPtr completed = SendMessageTimeout(child, 0x000C, IntPtr.Zero, text,
            0x0002, 2500, out response); // WM_SETTEXT / SMTO_ABORTIFHUNG
        if (completed == IntPtr.Zero)
            throw new NIRADesktopUncertainInputException(
                "Native editor write timed out or failed; inspect before acting again.");
        return true;
    }

    private static NIRADesktopActionResult VerifyTextDelivery(
        AutomationElement editor, string before, string intended, string method,
        CancellationToken ct)
    {
        bool verified = WaitForText(editor, before, intended, ct, 16, out string? observed);
        return new NIRADesktopActionResult
        {
            ActionDispatched = true,
            PostconditionVerified = verified,
            Evidence = $"TextMethod={method}; ExactTextPostcondition={verified}; " +
                $"ExpectedSha256={NIRADesktopTextEntryPolicy.TextFingerprint(before + intended)}; " +
                $"ReadbackAvailable={observed is not null}; " +
                $"ReadbackLength={(observed is null ? -1 : observed.Length)}; " +
                $"ObservedSha256={(observed is null ? "Unavailable" : NIRADesktopTextEntryPolicy.TextFingerprint(observed))}. " +
                "Unverified delivery must not be retried without a new explicit, observed editing context."
        };
    }

    private static bool WaitForText(AutomationElement editor, string before,
        string inserted, CancellationToken ct, int retries, out string? observed)
    {
        observed = null;
        for (int attempt = 0; attempt < retries; attempt++)
        {
            if (ct.IsCancellationRequested)
                throw new NIRADesktopUncertainInputException(
                    "Input was dispatched before cancellation; inspect its result.");
            observed = ReadEditorText(editor);
            if (NIRADesktopTextEntryPolicy.MatchesInsertedText(before, observed, inserted))
                return true;
            if (attempt + 1 < retries) Thread.Sleep(85);
        }
        return false;
    }

    private static string? ReadEditorText(AutomationElement editor)
    {
        try
        {
            if (editor.Current.IsPassword) return null;
            if (editor.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern) &&
                valuePattern is ValuePattern v)
                return v.Current.Value;
            if (editor.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern) &&
                textPattern is TextPattern t)
            {
                // TextPattern.GetText includes at most 10,001 characters.
                // Longer observations cannot prove an exact full document.
                return t.DocumentRange.GetText(10001);
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        return null;
    }

    private static bool IsFocusedEditableDescendant(AutomationElement focused,
        AutomationElement root, int expectedPid, CancellationToken ct)
    {
        try
        {
            if (focused.Current.ProcessId != expectedPid ||
                !focused.Current.IsEnabled || !focused.Current.IsKeyboardFocusable ||
                focused.Current.IsPassword)
                return false;
            ControlType type = focused.Current.ControlType;
            if (type != ControlType.Edit && type != ControlType.Document)
                return false;
            int[] rootId = root.GetRuntimeId();
            AutomationElement? current = focused;
            // A child of the target HWND, not merely another window in the
            // same process. Keep traversal bounded even with broken UIA trees.
            for (int depth = 0; current != null && depth < 48; depth++)
            {
                ct.ThrowIfCancellationRequested();
                if (current.GetRuntimeId().SequenceEqual(rootId))
                    return depth > 0;
                current = TreeWalker.RawViewWalker.GetParent(current);
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        return false;
    }

    private static AutomationElement FindUnique(AutomationElement root,
        NIRADesktopAction action, CancellationToken ct)
    {
        AutomationElement? found = null;
        foreach (var (node, _) in Enumerate(root, ct))
        {
            try
            {
                if ((action.ControlType.Length > 0 &&
                     !string.Equals(node.Current.ControlType?.ProgrammaticName,
                         action.ControlType, StringComparison.OrdinalIgnoreCase)) ||
                    (action.ControlName.Length > 0 && !string.Equals(node.Current.Name,
                        action.ControlName, StringComparison.Ordinal)) ||
                    (action.AutomationId.Length > 0 && !string.Equals(node.Current.AutomationId,
                        action.AutomationId, StringComparison.Ordinal)))
                    continue;
                if (found != null) throw new InvalidOperationException(
                    "Multiple matching controls. Inspect again and specify an exact unique AutomationId or more precise window.");
                found = node;
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
        }
        return found ?? throw new InvalidOperationException("No matching control in the current live window. Inspect again.");
    }

    private static IEnumerable<(AutomationElement Node, int Depth)> Enumerate(
        AutomationElement root, CancellationToken ct)
    {
        var q = new Queue<(AutomationElement Node, int Depth)>();
        q.Enqueue((root, 0));
        int count = 0;
        var walker = TreeWalker.ControlViewWalker;
        while (q.Count != 0 && count++ < 220)
        {
            ct.ThrowIfCancellationRequested();
            var (node, depth) = q.Dequeue();
            yield return (node, depth);
            if (depth >= 12) continue;
            AutomationElement? child = null;
            try { child = walker.GetFirstChild(node); }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            catch (InvalidOperationException) { }
            int n = 0;
            while (child != null && n++ < 75 && q.Count < 220)
            {
                q.Enqueue((child, depth + 1));
                try { child = walker.GetNextSibling(child); }
                catch (ElementNotAvailableException) { break; }
                catch (COMException) { break; }
                catch (InvalidOperationException) { break; }
            }
        }
    }

    private static bool BringForward(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9);
        if (GetForegroundWindow() != hwnd) SetForegroundWindow(hwnd);
        return GetForegroundWindow() == hwnd;
    }

    private sealed record KeyChord(ushort Key, ushort[] Modifiers)
    {
        public static KeyChord Parse(string chord)
        {
            string[] parts = chord.Trim().ToUpperInvariant().Split('+', StringSplitOptions.TrimEntries);
            if (parts.Length < 1 || parts.Length > 4 || parts.Any(string.IsNullOrEmpty))
                throw new ArgumentException("Invalid key chord.");
            List<ushort> mods = new();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                ushort mod = parts[i] switch { "CTRL" => 0x11, "ALT" => 0x12,
                    "SHIFT" => 0x10, _ => throw new ArgumentException("Unsupported modifier.") };
                if (mods.Contains(mod)) throw new ArgumentException("Duplicate modifier.");
                mods.Add(mod);
            }
            string name = parts[^1];
            ushort vk = name switch
            {
                "ENTER" => 0x0D, "ESC" => 0x1B, "TAB" => 0x09,
                "SPACE" => 0x20, "BACKSPACE" => 0x08, "DELETE" => 0x2E,
                "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
                "HOME" => 0x24, "END" => 0x23, "PGUP" => 0x21, "PGDN" => 0x22,
                _ when name.Length == 1 && ((name[0] >= 'A' && name[0] <= 'Z') ||
                    (name[0] >= '0' && name[0] <= '9')) => name[0],
                _ when name.Length >= 2 && name[0] == 'F' &&
                    int.TryParse(name[1..], out int n) && n is >= 1 and <= 12 => (ushort)(0x70 + n - 1),
                _ => throw new ArgumentException("Unsupported key. Use named navigation keys, A-Z, 0-9 or F1-F12.")
            };
            return new KeyChord(vk, mods.ToArray());
        }
    }

    private static void SendChord(KeyChord chord, IntPtr hwnd)
    {
        if (GetForegroundWindow() != hwnd)
            throw new InvalidOperationException("Target lost foreground before keyboard input.");
        foreach (ushort mod in chord.Modifiers) keybd_event((byte)mod, 0, 0, UIntPtr.Zero);
        try
        {
            keybd_event((byte)chord.Key, 0, 0, UIntPtr.Zero);
            keybd_event((byte)chord.Key, 0, 2, UIntPtr.Zero);
        }
        finally
        {
            for (int i = chord.Modifiers.Length - 1; i >= 0; i--)
                keybd_event((byte)chord.Modifiers[i], 0, 2, UIntPtr.Zero);
        }
    }

    private void SendText(string text, IntPtr hwnd, int pid, CancellationToken ct,
        AutomationElement editor, string? prefix)
    {
        // Large single SendInput bursts were accepted by Windows but left the
        // receiving editor behind. Bound each burst and allow message pumping.
        // Never resend the full payload after a partial/uncertain dispatch.
        const int charactersPerBatch = 4;
        bool dispatchedAny = false;
        for (int offset = 0; offset < text.Length; offset += charactersPerBatch)
        {
            if (ct.IsCancellationRequested)
            {
                if (dispatchedAny) throw new NIRADesktopUncertainInputException(
                    "Cancelled after partial text dispatch; inspect the editor.");
                throw new NIRADesktopNoInputException("Cancelled before typing began.");
            }
            // No timed retry and no keystrokes in the owner's other app.
            // Continue remaining, never replay prior input, after return.
            bool resumed = _foreground.AwaitTarget(hwnd, pid, ct);
            if (resumed)
            {
                // An editor may have been changed while NIRA was paused.
                // Validate prior insertion before sending any remaining keys.
                if (prefix is null || !WaitForText(editor, prefix, text[..offset], ct,
                    8, out _))
                    throw new NIRADesktopUncertainInputException(
                        "Target content changed while paused, or cannot be read back. " +
                        "No remaining text was sent; inspect before recovery.");
                AutomationElement? resumedFocus = AutomationElement.FocusedElement;
                if (resumedFocus is null ||
                    !IsFocusedEditableDescendant(resumedFocus, Root(hwnd, pid), pid, ct))
                    throw new NIRADesktopUncertainInputException(
                        "Application returned to foreground but the editor lost focus. " +
                        "Remaining text was withheld.");
            }
            int count = Math.Min(charactersPerBatch, text.Length - offset);
            INPUT[] events = new INPUT[count * 2];
            for (int j = 0; j < count; j++)
            {
                ushort scan = text[offset + j];
                events[j * 2] = new INPUT { type = 1, U = new INPUTUNION { ki = new KEYBDINPUT
                    { wScan = scan, dwFlags = 0x0004 } } };
                events[j * 2 + 1] = new INPUT { type = 1, U = new INPUTUNION { ki = new KEYBDINPUT
                    { wScan = scan, dwFlags = 0x0004 | 0x0002 } } };
            }
            uint inserted = SendInput((uint)events.Length, events, Marshal.SizeOf<INPUT>());
            if (inserted != (uint)events.Length)
                throw new NIRADesktopUncertainInputException(
                    $"Input batch {offset / charactersPerBatch + 1} accepted {inserted} of {events.Length} events. Inspect instead of replaying.");
            dispatchedAny = true;
            if (offset + count < text.Length) Thread.Sleep(30);
        }
        _foreground.AwaitTarget(hwnd, pid, ct);
    }
    private static string Clean(string? s, int max)
    {
        string text = (s ?? "").Replace('\n', ' ').Replace('\r', ' ')
            .Replace('\t', ' ').Trim();
        return text.Length > max ? text[..max] : text;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint
    { public int X; public int Y; public NativePoint(int x,int y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
    { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    { public int dx,dy; public uint mouseData,flags,time; public IntPtr extra; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    private static extern int GetClassName(IntPtr h, StringBuilder className, int maxCount);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr SendMessageTimeout(IntPtr h, uint message,
        IntPtr wParam, string lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowAsync(IntPtr h, int cmd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, INPUT[] events, int size);
}

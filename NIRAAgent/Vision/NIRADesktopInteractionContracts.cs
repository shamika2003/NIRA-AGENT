using System;
using System.Threading;
using System.Threading.Tasks;

namespace NIRAAgent.Vision;

// Structured Windows UI interaction. HWND and PID are resolved independently
// by the existing live-window registry, then revalidated inside the OS backend.
public sealed record NIRADesktopAction
{
    public string Kind { get; init; } = "";
    public string ControlName { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public string ControlType { get; init; } = "";
    public string Text { get; init; } = "";
    public string Key { get; init; } = "";
    // Optional: the user intentionally asked to INSERT text into an already
    // populated editor. Default false protects unrelated unsaved documents.
    public bool AllowExistingContent { get; init; }
}

public sealed record NIRADesktopActionResult
{
    public string Evidence { get; init; } = "";
    // This refers only to the attempted UI action, not the user's full goal.
    public bool ActionDispatched { get; init; }
    public bool PostconditionVerified { get; init; }
}

public interface INIRADesktopInteractionBackend
{
    Task<string> InspectAsync(IntPtr windowHandle, int processId,
        CancellationToken cancellationToken = default);
    Task<NIRADesktopActionResult> ActAsync(IntPtr windowHandle, int processId,
        NIRADesktopAction action, CancellationToken cancellationToken = default);
}

// Testable preflight shared with handler and Windows backend. Never use a
// screen coordinate or blind keyboard sequence as a substitute for a HWND.
public static class NIRADesktopActionPolicy
{
    public static void Validate(NIRADesktopAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        string kind = action.Kind.Trim().ToLowerInvariant();
        if (kind is not ("activate" or "invoke" or "select" or "toggle" or
            "expand" or "collapse" or "setvalue" or "click" or "focus" or
            "type" or "press" or "close"))
            throw new ArgumentException("Unsupported desktop action.");
        if (action.ControlName.Length > 180 || action.AutomationId.Length > 180 ||
            action.ControlType.Length > 120 || action.Text.Length > 10000 ||
            action.Key.Length > 80)
            throw new ArgumentException("Desktop action arguments exceed their bounds.");
        bool control = action.ControlName.Length != 0 || action.AutomationId.Length != 0;
        if (kind == "close" && (control || action.ControlType.Length != 0))
            throw new ArgumentException("close is an exact HWND-level action; no control selector is accepted.");
        if (kind is "invoke" or "select" or "toggle" or "expand" or "collapse" or
            "setvalue" or "click" or "focus" || (kind == "type" && control))
        {
            if (!control)
                throw new ArgumentException("Specify a controlName or AutomationId from desktop.inspect.");
            // A unique live UIA control is authoritative even when the model did
            // not repeat its type. The backend rejects zero or multiple matches.
            // If a type is given, it remains an additional exact constraint.
        }
        // 'type' may use the currently focused EDIT/DOCUMENT control in the
        // exact HWND. The Windows backend must prove focus and ancestry first;
        // validating a string alone never grants permission for blind typing.
        if ((kind is "setvalue" or "type") && action.Text.Length == 0)
            throw new ArgumentException("A nonempty text argument is required.");
        if (kind == "press" && action.Key.Length == 0)
            throw new ArgumentException("A bounded key chord is required.");
        if (action.AllowExistingContent && kind != "type")
            throw new ArgumentException("allowExistingContent is valid only for type.");
        if (kind == "type" && NIRADesktopTextEntryPolicy.ContainsShortcutNotation(action.Text))
            throw new ArgumentException("type accepts literal text, not keyboard macros. Send shortcuts using separate press commands.");
        if (kind == "type" && NIRADesktopTextEntryPolicy.LooksCorruptedLongText(action.Text))
            throw new ArgumentException("Long desktop text contains a pathological character run; no input was sent. Request a fresh coherent literal payload before acting.");
        if (kind != "press" && action.Key.Length > 0)
            throw new ArgumentException("key is only valid for the press action.");
        if (kind is not ("setvalue" or "type") && action.Text.Length > 0)
            throw new ArgumentException("text is only valid for setValue or type.");
    }
}

// Used only when desktop preflight positively establishes that no document
// input was injected. A failure after SendInput must remain uncertain.
public class NIRADesktopNoInputException : InvalidOperationException
{
    public NIRADesktopNoInputException(string message) : base(message) { }
}

// A denied close-message transport is known not to have posted WM_CLOSE.
// Windows integrity boundaries must never be silently worked around.
public sealed class NIRADesktopAccessDeniedException : NIRADesktopNoInputException
{
    public NIRADesktopAccessDeniedException(string message) : base(message) { }
}

// Distinguishes an attempted Unicode input whose partial side effects cannot
// be ruled out. Callers must inspect rather than issue the same whole write.
public sealed class NIRADesktopUncertainInputException : InvalidOperationException
{
    public NIRADesktopUncertainInputException(string details)
        : base("DESKTOP_INPUT_MAY_BE_PARTIAL: " + details) { }
}

// Domain-neutral rules shared by the actual backend, run guard, and offline tests.
// The model may generate new prose every cycle; a second proposal isn't proof
// that an earlier non-idempotent text insertion should be repeated.
public static class NIRADesktopTextEntryPolicy
{
    public static string TextFingerprint(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized)));
    }

    // Only an inspected target editor's full accessible text matching the intended
    // insertion proves a prior blank-document typing operation. An HWND alone
    // or an overlapping preview substring does not prove it.
    public static bool InspectionMatchesNewDocumentText(string? inspection, string expected)
    {
        if (string.IsNullOrWhiteSpace(inspection) || string.IsNullOrEmpty(expected))
            return false;
        string marker = "EditorTextSha256=" + TextFingerprint(expected);
        // The target HWND has already been proven by SameTargetObservation.
        // Background window editors often report HasKeyboardFocus=False,
        // although their complete accessible text is still authoritative.
        return inspection.Split('\n').Any(line =>
            line.Contains("EditorTextLength=", StringComparison.Ordinal) &&
            line.Contains(marker, StringComparison.Ordinal));
    }

    // Deterministic pre-dispatch corruption sentinel, NOT a grammar/style judge.
    // It catches model-output failures such as hundreds of 'T' or '.' characters
    // without logging the user's words. A normal paragraph is never rewritten.
    // The specialist may propose a fresh payload, but this one is never sent.
    public static bool LooksCorruptedLongText(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 100) return false;
        int run = 1;
        for (int i = 1; i < text.Length; i++)
        {
            run = text[i] == text[i - 1] ? run + 1 : 1;
            if (run >= 36 && (char.IsLetterOrDigit(text[i]) ||
                              text[i] is '.' or '_' or '-'))
                return true;
        }
        return false;
    }

    public static bool ContainsShortcutNotation(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(text,
            @"\^[A-Za-z0-9]\{(?:BACKSPACE|DELETE|ENTER|TAB|ESC)\}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static bool MatchesInsertedText(string? before, string? after, string inserted)
    {
        if (before is null || after is null || string.IsNullOrEmpty(inserted)) return false;
        // UIA providers may normalize newlines to CRLF. An insertion only
        // verifies exactly when the full observed value matches the expected
        // result, not when a small substring happens to appear inside garbage.
        static string N(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);
        return string.Equals(N(after), N(before + inserted), StringComparison.Ordinal);
    }
}

// A freshly returned desktop.inspect result for the *same* HWND is the
// minimum provenance for reviewing an action. It is not by itself proof
// that a document was saved or a 3D model was created.
public static class NIRADesktopExecutionEvidencePolicy
{
    // A verified text write already includes exact in-window readback.
    // Other interactions (keys, clicks, window activation) need an independent
    // observation before the entire user objective can be declared done.
    public static bool RequiresFollowupInspection(string? action, bool exactPostconditionVerified)
    {
        bool textAction = string.Equals(action, "type", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "setValue", StringComparison.OrdinalIgnoreCase);
        return !textAction || !exactPostconditionVerified;
    }

    public static bool SameTargetObservation(long? actionHandle,
        string? inspectionOutput, bool inspectionSucceeded)
    {
        if (!inspectionSucceeded || actionHandle is null || actionHandle <= 0 ||
            string.IsNullOrWhiteSpace(inspectionOutput)) return false;
        foreach (string line in inspectionOutput.Split('\n'))
        {
            const string key = "WindowHandle: ";
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;
            string text = line[key.Length..].Trim();
            if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
            return long.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out long handle) &&
                handle == actionHandle;
        }
        return false;
    }
}

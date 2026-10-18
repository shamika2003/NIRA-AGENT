using NIRAAgent.Capabilities;
using NIRAAgent.Vision;

namespace NIRAAgent.Mind;

// One user's run can operate many fields, but a model may not repeatedly
// append fresh alternative versions of the same payload to one focused editor.
// This is runtime action sequencing, not task-specific phrase matching.
internal sealed class NIRADesktopWriteContinuityGuard
{
    private readonly HashSet<long> _writtenWindowHandles = new();
    private readonly HashSet<long> _unverifiedWriteHandles = new();
    private readonly Dictionary<long, string> _lastWindowInspections = new();
    private readonly Dictionary<long, string?> _pendingNewDocuments = new();

    // A Ctrl+N command is only an input receipt. It must not authorize
    // clearing, typing, or editing an existing file until a subsequent
    // inspection proves a DISTINCT, empty document in the same HWND.
    internal bool AwaitingNewDocument(long hwnd) => _pendingNewDocuments.ContainsKey(hwnd);

    internal bool RecordInspection(long hwnd, string inspection)
    {
        bool confirmed = false;
        if (_pendingNewDocuments.TryGetValue(hwnd, out string? before) &&
            NIRADesktopDocumentTransitionPolicy.VerifiesNewBlankDocument(before, inspection))
        {
            _pendingNewDocuments.Remove(hwnd);
            _writtenWindowHandles.Remove(hwnd);
            _unverifiedWriteHandles.Remove(hwnd);
            confirmed = true;
        }
        _lastWindowInspections[hwnd] = inspection;
        return confirmed;
    }

    internal static bool BlocksSameBatchAfterNewDocument(
        NIRACapabilityRequest request, IReadOnlySet<long> scheduledNewDocumentHandles)
    {
        if (request.CapabilityId != NIRACapabilityIds.DesktopAct) return false;
        return GetHandle(request) is long handle && scheduledNewDocumentHandles.Contains(handle);
    }

    internal bool BlocksPendingDocumentTransition(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.DesktopAct) return false;
        long? hwnd = GetHandle(request);
        if (hwnd is null || !_pendingNewDocuments.ContainsKey(hwnd.Value)) return false;
        string kind = NIRACapabilityArguments.GetOptionalString(request, "action", 40) ?? "";
        return !kind.Equals("activate", StringComparison.OrdinalIgnoreCase) &&
               !kind.Equals("focus", StringComparison.OrdinalIgnoreCase);
    }


    // After an unverified write, no subsequent UI mutation in the same
    // window is safe: CTRL+N / CTRL+A / BACKSPACE can destroy partial text.
    internal bool BlocksUnsafeRecovery(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.DesktopAct) return false;
        long? hwnd = GetHandle(request);
        if (hwnd is null || !_unverifiedWriteHandles.Contains(hwnd.Value)) return false;
        string kind = NIRACapabilityArguments.GetOptionalString(request, "action", 40) ?? "";
        return !kind.Equals("activate", StringComparison.OrdinalIgnoreCase) &&
               !kind.Equals("focus", StringComparison.OrdinalIgnoreCase);
    }

    internal void MarkTextVerified(long hwnd) => _unverifiedWriteHandles.Remove(hwnd);

    internal bool Blocks(NIRACapabilityRequest request)
    {
        if (!IsWrite(request, out long hwnd)) return false;
        return _writtenWindowHandles.Contains(hwnd);
    }

    internal void Record(NIRACapabilityRequest request, bool succeeded,
        bool postconditionVerified = true)
    {
        if (!succeeded || request.CapabilityId != NIRACapabilityIds.DesktopAct) return;
        long? hwnd = GetHandle(request);
        if (hwnd is null) return;
        string? kind = NIRACapabilityArguments.GetOptionalString(request, "action", 40);
        if (kind is null) return;
        if (kind.Equals("press", StringComparison.OrdinalIgnoreCase) &&
            IsNewDocumentChord(NIRACapabilityArguments.GetOptionalString(request, "key", 80)))
        {
            // Snapshot comes from the last *successful* read-only inspection.
            // Never assume the new tab is active merely because Ctrl+N dispatched.
            _pendingNewDocuments[hwnd.Value] =
                _lastWindowInspections.TryGetValue(hwnd.Value, out string? before)
                    ? before : null;
            return;
        }
        if (kind.Equals("type", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("setValue", StringComparison.OrdinalIgnoreCase))
        {
            _writtenWindowHandles.Add(hwnd.Value);
            if (!postconditionVerified) _unverifiedWriteHandles.Add(hwnd.Value);
            else _unverifiedWriteHandles.Remove(hwnd.Value);
        }
        else if (kind.Equals("focus", StringComparison.OrdinalIgnoreCase) ||
                 kind.Equals("select", StringComparison.OrdinalIgnoreCase) ||
                 (kind.Equals("press", StringComparison.OrdinalIgnoreCase) &&
                  IsEditingContextChange(NIRACapabilityArguments.GetOptionalString(request, "key", 80))))
            _writtenWindowHandles.Remove(hwnd.Value);
    }

    internal static long? TargetHandle(NIRACapabilityRequest request) => GetHandle(request);

    internal static long? NewDocumentTarget(NIRACapabilityRequest request)
    {
        if (request.CapabilityId != NIRACapabilityIds.DesktopAct) return null;
        if (!string.Equals(NIRACapabilityArguments.GetOptionalString(request, "action", 40),
                "press", StringComparison.OrdinalIgnoreCase)) return null;
        return IsNewDocumentChord(NIRACapabilityArguments.GetOptionalString(request, "key", 80))
            ? GetHandle(request) : null;
    }

    private static bool IsNewDocumentChord(string? key) =>
        string.Equals(key?.Trim(), "CTRL+N", StringComparison.OrdinalIgnoreCase);

    private static bool IsEditingContextChange(string? key) =>
        key is not null && (key.Equals("TAB", StringComparison.OrdinalIgnoreCase));

    internal static long? WriteTarget(NIRACapabilityRequest request) =>
        IsWrite(request, out long handle) ? handle : null;

    private static bool IsWrite(NIRACapabilityRequest request, out long hwnd)
    {
        hwnd = 0;
        if (request.CapabilityId != NIRACapabilityIds.DesktopAct) return false;
        string? kind = NIRACapabilityArguments.GetOptionalString(request, "action", 40);
        if (!string.Equals(kind, "type", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(kind, "setValue", StringComparison.OrdinalIgnoreCase)) return false;
        long? target = GetHandle(request);
        if (target is null) return false;
        hwnd = target.Value;
        return true;
    }

    private static long? GetHandle(NIRACapabilityRequest request)
    {
        try { return DesktopWindowResolver.ParseHandle(
            NIRACapabilityArguments.GetOptionalString(request, "windowHandle", 80)); }
        catch (ArgumentException) { return null; }
    }
}

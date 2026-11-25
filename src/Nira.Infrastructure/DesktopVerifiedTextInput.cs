namespace Nira.Infrastructure;

/// <summary>Coordinates bounded, verified Unicode input for an already approved editor.
/// No application-specific behavior, clipboard changes, shortcuts, or automatic retyping.</summary>
public static class DesktopVerifiedTextInput
{
    public static DesktopAction Write(string text, Func<bool> canSend, Func<string?> read,
        Func<string, bool> sendUnit, Action wait, CancellationToken ct)
    {
        if (text.Length is < 1 or > 200 || text.Any(char.IsControl) || !ValidUtf16(text))
            return new DesktopAction(false, false, "Unsupported text; no input sent.", "TEXT_CHARACTERS_UNSUPPORTED");
        bool dispatched = false;
        int? count = null;
        DesktopAction Stop(string status, string message, string code = "EDITOR_STATE_UNKNOWN") => dispatched
            ? new DesktopAction(true, true, message + " No additional input or retry performed.",
                Verified: false, ObservedCharacters: count, VerificationStatus: status)
            : new DesktopAction(false, false, message + " No input sent.", code);

        for (int index = 0; index < text.Length;)
        {
            if (ct.IsCancellationRequested) return Stop("interrupted", "Text entry canceled.", "TASK_CANCELED");
            if (!canSend()) return Stop("interrupted", "Approved editor focus, identity or keyboard state changed.", "CONTROL_NOT_FOCUSED");
            string previous = text[..index];
            string? before = read();
            if (!DesktopTextVerification.Exact(before, previous))
                return Stop(before is null ? "unavailable" : "mismatch", "Editor contents cannot be confirmed before the next character.", index == 0 && before is not null ? "EDITOR_NOT_EMPTY" : "EDITOR_STATE_UNKNOWN");
            int size = char.IsHighSurrogate(text[index]) ? 2 : 1;
            string unit = text.Substring(index, size);
            string expected = text[..(index + size)];
            if (ct.IsCancellationRequested || !canSend()) return Stop("interrupted", "Input conditions changed before dispatch.", "CONTROL_NOT_FOCUSED");
            // Treat even a failed native dispatch as uncertain: never replay a key.
            dispatched = true;
            if (!sendUnit(unit)) return Stop("incomplete", "Windows did not confirm the complete keyboard event sequence.");
            bool matched = false;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (ct.IsCancellationRequested || !canSend()) return Stop("interrupted", "Input conditions changed after dispatch.");
                wait();
                string? observed = read();
                if (observed is null) continue;
                string actual = observed.TrimEnd('\r', '\n');
                count = actual.Length;
                if (string.Equals(actual, expected, StringComparison.Ordinal)) { matched = true; break; }
                // Accessibility can lag or briefly expose half of a surrogate pair.
                // Any other content is corruption/interference, not a reason to retry.
                if (!expected.StartsWith(actual, StringComparison.Ordinal) ||
                    !actual.StartsWith(previous, StringComparison.Ordinal))
                    return Stop("mismatch", "Editor text differs from the approved text.");
            }
            if (!matched) return Stop("unconfirmed_prefix", "The next character was not confirmed by the editor within the read-back limit.");
            index += size;
        }
        // A final independent observation catches delayed duplicate input.
        wait();
        wait();
        string? final = read();
        if (final is not null) count = final.TrimEnd('\r', '\n').Length;
        if (!DesktopTextVerification.Exact(final, text))
            return Stop(final is null ? "unavailable" : "mismatch", "Final editor contents could not be verified exactly.");
        return new DesktopAction(true, true, "Approved foreground text typed and independently verified through TextPattern",
            Verified: true, ObservedCharacters: count, VerificationStatus: "exact_match");
    }

    private static bool ValidUtf16(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i])) return false;
            }
            else if (char.IsLowSurrogate(text[i])) return false;
        }
        return true;
    }
}

namespace NIRAAgent.Vision;

/// <summary>
/// Pure, app-independent inspection proof for a new blank document.
/// Sending Ctrl+N does NOT mean the target changed. UI Automation evidence
/// must show a different selected document or window title and a blank editor.
/// No application names, tab captions, or language-specific "Untitled" test.
/// </summary>
public static class NIRADesktopDocumentTransitionPolicy
{
    public static long? InspectedHandle(string? inspection)
    {
        if (string.IsNullOrWhiteSpace(inspection)) return null;
        foreach (string line in inspection.Split('\n'))
        {
            if (!line.StartsWith("WindowHandle: 0x", StringComparison.OrdinalIgnoreCase)) continue;
            string candidate = line["WindowHandle: 0x".Length..].Trim();
            if (long.TryParse(candidate, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out long handle) && handle > 0)
                return handle;
        }
        return null;
    }

    public static bool VerifiesNewBlankDocument(string? before, string? after)
    {
        if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(after)) return false;
        long? previousHandle = InspectedHandle(before);
        if (previousHandle is null || previousHandle != InspectedHandle(after)) return false;
        if (!HasBlankEditor(after)) return false;
        string? previousTab = SelectedTab(before);
        string? newTab = SelectedTab(after);
        // When tab selection is visible, an unrelated window-title change
        // (e.g. adding a modified marker to the SAME FILE) is not a new tab.
        if (previousTab is not null && newTab is not null)
            return !string.Equals(previousTab, newTab, StringComparison.Ordinal) ||
                   TabItemCount(after) > TabItemCount(before);
        // Non-tabbed document applications may instead expose a new title.
        string? previousTitle = Header(before, "Title: ");
        string? newTitle = Header(after, "Title: ");
        return previousTitle is not null && newTitle is not null &&
            !string.Equals(previousTitle, newTitle, StringComparison.Ordinal);
    }

    private static int TabItemCount(string inspection) => inspection.Split('\n').Count(line =>
        line.Contains("ControlType='ControlType.TabItem'", StringComparison.Ordinal));

    private static bool HasBlankEditor(string inspection) => inspection.Split('\n').Any(line =>
        (line.Contains("ControlType='ControlType.Edit'", StringComparison.Ordinal) ||
         line.Contains("ControlType='ControlType.Document'", StringComparison.Ordinal)) &&
        line.Contains("EditorTextLength=0", StringComparison.Ordinal) &&
        line.Contains("HasKeyboardFocus=True", StringComparison.Ordinal));

    private static string? SelectedTab(string inspection)
    {
        foreach (string line in inspection.Split('\n'))
        {
            if (!line.Contains("ControlType='ControlType.TabItem'", StringComparison.Ordinal) ||
                !line.Contains("IsSelected=True", StringComparison.Ordinal)) continue;
            const string start = "Name='";
            int i = line.IndexOf(start, StringComparison.Ordinal);
            if (i < 0) continue;
            i += start.Length;
            int end = line.IndexOf("' | AutomationId=", i, StringComparison.Ordinal);
            if (end >= 0) return line[i..end];
        }
        return null;
    }

    private static string? Header(string source, string prefix)
    {
        foreach (string line in source.Split('\n'))
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..].Trim();
        return null;
    }
}

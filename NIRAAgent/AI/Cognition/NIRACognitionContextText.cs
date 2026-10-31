namespace NIRAAgent.AI.Cognition;

// Pure shared context formatting. This is a utility, not a cognition stage.
// Block 01, Block 02 and Response Realization can evolve independently.
internal static class NIRACognitionContextText
{
    internal static string Bound(string? source, int max, bool tail = false)
    {
        string s = source?.Trim() ?? "";
        if (max <= 0) return string.Empty;
        if (s.Length <= max) return s;
        const string omitted = "\n[Context is abbreviated; missing text is NOT evidence of absence]\n";
        if (max <= omitted.Length) return s[..max];
        int keep = max - omitted.Length;
        if (!tail) return s[..keep] + omitted;
        int first = keep / 3;
        return s[..first] + omitted + s[^(keep - first)..];
    }
}

using System.Text.RegularExpressions;

namespace NIRAAgent.Runtime.General;

/// <summary>
/// Visual-only provisional text. Never persisted or spoken. Neither source URLs
/// nor native player commands are exposed before completed evidence binding.
/// The real final answer replaces the draft after all existing checks pass.
/// </summary>
public static class NiraSafeStreamDraft
{
    private static readonly Regex Url = new(
        @"https?://[^\s<>\]\)\('""\\]{1,1800}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static string Format(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return string.Empty;
        string visible = candidate.Length <= 16_000 ? candidate : candidate[..16_000];
        int appendix = visible.IndexOf("```nira-blocks", StringComparison.OrdinalIgnoreCase);
        if (appendix >= 0) visible = visible[..appendix];
        if (visible.Length == 0) return string.Empty;
        try { visible = Url.Replace(visible, "[link pending verification]"); }
        catch (RegexMatchTimeoutException) { return string.Empty; }
        return "*Draft · checking sources*\n\n" + visible;
    }

    public static int RunSelfCheck()
    {
        string draft = Format("Here is a draft https://unverified.invalid/test.");
        if (!draft.Contains("Draft", StringComparison.Ordinal) ||
            draft.Contains("https://", StringComparison.Ordinal))
            throw new InvalidOperationException("Draft URL was displayed before validation.");
        if (Format(null).Length != 0 || Format(string.Empty).Length != 0 ||
            Format("explanation\n```nira-blocks\n{...}").Contains("nira-blocks", StringComparison.Ordinal))
            throw new InvalidOperationException("Draft lifecycle or appendix shielding failed.");
        return 3;
    }
}

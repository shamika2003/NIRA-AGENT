using System.Text;
using System.Text.RegularExpressions;

namespace NIRAAgent.UI.Presentation;

/// <summary>Conservative provisional prose; structured content waits for final delivery.</summary>
public sealed class NIRALivePreview
{
    private const int MaximumLength = 24000;
    private readonly StringBuilder _pending = new();
    private readonly StringBuilder _visible = new();
    private bool _blocked;
    private string _partial = "";
    private string? _fence;
    private const int Lookahead = 32;
    private int _received;
    private static readonly Regex Boundary = new(@"\r?\n[ \t]*\r?\n", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Unsafe = new(@"[{}\[\]`<>\\|~]|https?://|\b(?:tool[_ ]?calls?|system prompt|developer message|internal (?:details|instructions|reasoning)|host.recorded|recorded observations|arguments json|receipt|schema routing|nira[_\.]|api[_ -]?key|bearer)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex ColorTag = new(@"\[color=#[a-fA-F0-9]{6}\]|\[/color\]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public string Text => _visible.ToString() + (_partial.Length == 0 ? "" : (_visible.Length == 0 ? "" : "\n\n") + _partial);
    public void Reset() { _pending.Clear(); _visible.Clear(); _blocked = false; _partial = ""; _fence = null; _received = 0; }

    // The visible preview is provisional and never used as evidence or dispatched.
    // Screen a whole accumulated paragraph, and retain a trailing lookahead to
    // avoid displaying a split internal identifier. Structured paragraphs wait
    // for the authoritative renderer; later ordinary prose may stream again.
    private static bool Safe(string part) => !Unsafe.IsMatch(ColorTag.Replace(part, "")) &&
        !NIRAPublicDelivery.ContainsInternal(part) && string.Equals(NIRAPublicDelivery.Answer(part), part, StringComparison.Ordinal);

    private bool Structured(string part)
    {
        bool hidden = _fence is not null;
        foreach (string line in part.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (_fence is { } fence)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal) && trimmed.Trim().All(c => c == fence[0])) _fence = null;
                hidden = true;
            }
            else if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                _fence = new string(trimmed[0], trimmed.TakeWhile(c => c == trimmed[0]).Count());
                hidden = true;
            }
        }
        return hidden || part.Contains('|');
    }

    public bool Append(string delta)
    {
        if (_blocked || delta.Length == 0) return false;
        string before = Text;
        _partial = "";
        if (delta.Length > MaximumLength - _received)
        { _blocked = true; _pending.Clear(); return before != Text; }
        _received += delta.Length;
        _pending.Append(delta);
        try
        {
            while (true)
            {
                string input = _pending.ToString();
                Match boundary = Boundary.Match(input);
                if (!boundary.Success) break;
                string part = input[..boundary.Index].Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
                _pending.Remove(0, boundary.Index + boundary.Length);
                if (part.Length == 0) continue;
                if (Structured(part)) continue;
                if (!Safe(part))
                {
                    if (part.TrimStart().StartsWith('{') || part.TrimStart().StartsWith('[') && !part.StartsWith("[color=", StringComparison.Ordinal) ||
                        NIRAPublicDelivery.ContainsInternal(part))
                    { _blocked = true; _pending.Clear(); break; }
                    // Links, inline code and equations are safe in the final renderer;
                    // omit them from this provisional projection, not all later prose.
                    continue;
                }
                if (_visible.Length > 0) _visible.Append("\n\n");
                _visible.Append(part);
            }
            if (!_blocked && _fence is null)
            {
                string pending = _pending.ToString().TrimStart();
                if (pending.Length > Lookahead && Safe(pending.TrimEnd()))
                {
                    int cutoff = pending.Length - Lookahead;
                    while (cutoff > 0 && !char.IsWhiteSpace(pending[cutoff])) cutoff--;
                    if (cutoff > 0)
                    {
                        string prefix = pending[..cutoff].TrimEnd();
                        if (Safe(prefix)) _partial = prefix;
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException) { _blocked = true; _pending.Clear(); _partial = ""; }
        return !string.Equals(before, Text, StringComparison.Ordinal);
    }
}

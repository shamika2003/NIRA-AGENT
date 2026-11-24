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
    private static readonly Regex Boundary = new(@"\r?\n[ \t]*\r?\n", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Unsafe = new(@"[{}\[\]`<>\\|~]|https?://|\b(?:tool[_ ]?calls?|system prompt|developer message|internal (?:details|instructions|reasoning)|host.recorded|recorded observations|arguments json|receipt|schema routing|nira[_\.]|api[_ -]?key|bearer)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex ColorTag = new(@"\[color=#[a-fA-F0-9]{6}\]|\[/color\]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public string Text => _visible.ToString();
    public void Reset() { _pending.Clear(); _visible.Clear(); _blocked = false; }

    // A split token cannot evade checks: we screen complete paragraphs, not individual deltas.
    // Stop at the first structured/private paragraph, including all later fragments.
    public bool Append(string delta)
    {
        if (_blocked || delta.Length == 0) return false;
        if (delta.Length > MaximumLength - _pending.Length - _visible.Length)
        { _blocked = true; _pending.Clear(); return false; }
        _pending.Append(delta);
        bool changed = false;
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
                if (Unsafe.IsMatch(ColorTag.Replace(part, "")) || NIRAPublicDelivery.ContainsInternal(part) ||
                    !string.Equals(NIRAPublicDelivery.Answer(part), part, StringComparison.Ordinal))
                { _blocked = true; _pending.Clear(); break; }
                if (_visible.Length > 0) _visible.Append("\n\n");
                _visible.Append(part); changed = true;
            }
        }
        catch (RegexMatchTimeoutException) { _blocked = true; _pending.Clear(); }
        return changed;
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace NIRAAgent.Vision;

// Independent from pixel evidence: UIA reports names, types and selected
// controls, not a photograph or an assertion about unexposed pixels.
public sealed record NIRADesktopAccessibilityResult
{
    public string ControlTree { get; init; } = string.Empty;
    public int ElementsRead { get; init; }
    public bool Truncated { get; init; }
}

public interface INIRADesktopAccessibilityBackend
{
    Task<NIRADesktopAccessibilityResult> InspectAsync(
        IntPtr windowHandle, int expectedProcessId,
        CancellationToken cancellationToken = default);
}

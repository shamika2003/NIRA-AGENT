/*
 * filename: PcState.cs
 */

namespace SegaAgent.PC.Awareness;

public sealed class PcState
{
    // =========================================================
    // TIME
    // =========================================================

    public DateTime Timestamp { get; init; }


    // =========================================================
    // MOUSE
    // =========================================================

    public int MouseX { get; init; }

    public int MouseY { get; init; }


    // =========================================================
    // SCREEN
    // =========================================================

    public int ScreenWidth { get; init; }

    public int ScreenHeight { get; init; }


    // =========================================================
    // USER ACTIVITY
    // =========================================================

    public TimeSpan UserIdleTime { get; init; }


    // =========================================================
    // ACTIVE APPLICATION
    // =========================================================

    public string ActiveApplication { get; init; } =
        string.Empty;
}
/*
 * filename: PcContextFormatter.cs
 */

namespace SegaAgent.PC.Awareness;

public static class PcContextFormatter
{
    public static string Format(
        PcState state)
    {
        return $"""
            CURRENT PC STATE

            Timestamp:
            {state.Timestamp:yyyy-MM-dd HH:mm:ss} UTC

            Mouse Position:
            X = {state.MouseX}
            Y = {state.MouseY}

            Screen:
            Width = {state.ScreenWidth}
            Height = {state.ScreenHeight}

            User Idle Time:
            {state.UserIdleTime.TotalSeconds:F1} seconds

            Active Application:
            {state.ActiveApplication}
            """;
    }
}
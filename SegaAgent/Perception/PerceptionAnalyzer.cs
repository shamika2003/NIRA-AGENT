/*
 * filename: PerceptionAnalyzer.cs
 */

using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class PerceptionAnalyzer
{
    private static readonly TimeSpan
        LongIdleThreshold =
            TimeSpan.FromMinutes(30);


    public PerceptionEvent? Analyze(
        PcState? previous,
        PcState current)
    {
        if (previous == null)
            return null;


        // =====================================================
        // APPLICATION CHANGE
        // =====================================================

        if (previous.ActiveApplication !=
            current.ActiveApplication)
        {
            return new PerceptionEvent
            {
                Type =
                    "ApplicationChanged",

                Description =
                    $"User changed application from " +
                    $"{previous.ActiveApplication} " +
                    $"to {current.ActiveApplication}",

                CurrentState =
                    current
            };
        }


        // =====================================================
        // LONG IDLE - EDGE DETECTION
        // =====================================================

        var wasIdle =
            previous.UserIdleTime >=
            LongIdleThreshold;

        var isIdle =
            current.UserIdleTime >=
            LongIdleThreshold;


        // Only trigger when crossing the threshold.

        if (!wasIdle && isIdle)
        {
            return new PerceptionEvent
            {
                Type =
                    "UserIdle",

                Description =
                    "The user has been inactive on the PC for more than 30 minutes.",

                CurrentState =
                    current
            };
        }


        return null;
    }
}
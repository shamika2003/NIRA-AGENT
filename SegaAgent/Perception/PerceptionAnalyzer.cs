/*
 * filename: PerceptionAnalyzer.cs
 */

using System.Diagnostics;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class PerceptionAnalyzer
{
    // =========================================================
    // SETTINGS
    // =========================================================

    // TEMPORARY TEST VALUE.
    //
    // After confirming everything works, you can change this
    // back to 5 / 10 / 30 minutes.
    private static readonly TimeSpan
        LongIdleThreshold =
            TimeSpan.FromMinutes(1);


    // =========================================================
    // ANALYZE
    // =========================================================

    public PerceptionEvent? Analyze(
        PcState? previous,
        PcState current)
    {
        if (previous == null)
        {
            Debug.WriteLine(
                "[Perception] First PC state captured.");

            return null;
        }


        // =====================================================
        // DEBUG
        // =====================================================

        Debug.WriteLine(
            $"[Perception] " +
            $"App='{current.ActiveApplication}' | " +
            $"Idle={current.UserIdleTime.TotalSeconds:F0}s");


        // =====================================================
        // APPLICATION CHANGE
        // =====================================================

        if (previous.ActiveApplication !=
            current.ActiveApplication)
        {
            Debug.WriteLine(
                $"[Perception] APPLICATION CHANGED: " +
                $"'{previous.ActiveApplication}' -> " +
                $"'{current.ActiveApplication}'");


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


        Debug.WriteLine(
            $"[Perception] " +
            $"wasIdle={wasIdle}, " +
            $"isIdle={isIdle}");


        // Only trigger when crossing the threshold.
        //
        // Example:
        //
        // 55 sec -> 60 sec
        // FALSE  -> TRUE
        //
        // This produces ONE event.
        //
        // 65 sec -> 70 sec
        // TRUE   -> TRUE
        //
        // No new event.

        if (!wasIdle && isIdle)
        {
            Debug.WriteLine(
                "[Perception] USER IDLE EVENT TRIGGERED");


            return new PerceptionEvent
            {
                Type =
                    "UserIdle",

                Description =
                    $"The user has been inactive on the PC " +
                    $"for more than " +
                    $"{LongIdleThreshold.TotalMinutes:F0} minute.",

                CurrentState =
                    current
            };
        }


        return null;
    }
}
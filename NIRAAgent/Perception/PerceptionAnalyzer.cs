/*
 * filename: PerceptionAnalyzer.cs
 */

using System.Diagnostics;
using NIRAAgent.Character.History;
using NIRAAgent.PC.Awareness;

namespace NIRAAgent.Perception;

public sealed class PerceptionAnalyzer
{
    // =========================================================
    // SETTINGS
    // =========================================================

    /*
     * Temporary test value.
     *
     * Later this should become configuration.
     */

    private static readonly TimeSpan
        LongIdleThreshold =
            TimeSpan.FromMinutes(1);


    // =========================================================
    // ANALYZE
    // =========================================================

    public PerceptionEvent? Analyze(
        PcWorldState? previous,
        PcWorldState current)
    {
        ArgumentNullException.ThrowIfNull(
            current);


        if (previous ==
            null)
        {
            Debug.WriteLine(
                "[Perception] " +
                "First PC world state captured.");


            return null;
        }


        PcForegroundWindowState
            previousWindow =
                previous.ForegroundWindow;


        PcForegroundWindowState
            currentWindow =
                current.ForegroundWindow;


        // Foreground transitions are logged below; unchanged snapshots are silent.


        // =====================================================
        // 1. FOREGROUND APPLICATION CHANGED
        //
        // This is now based on the real foreground PROCESS,
        // not just the title text.
        // =====================================================

        if (!IsSameForegroundProcess(
                previousWindow,
                currentWindow))
        {
            string previousDescription =
                DescribeWindow(
                    previousWindow);


            string currentDescription =
                DescribeWindow(
                    currentWindow);


 
            return new PerceptionEvent
            {
                Type =
                    "ForegroundApplicationChanged",

                TopicKey =
                    NIRASocialTopicKeys
                        .ForegroundApplication(
                            currentWindow.ProcessName),

                Description =
                    $"The foreground application changed " +
                    $"from {previousDescription} " +
                    $"to {currentDescription}.",

                Metadata =
                    new Dictionary<
                        string,
                        string>(
                            StringComparer.OrdinalIgnoreCase)
                    {
                        ["previousProcess"] =
                            previousWindow.ProcessName,

                        ["currentProcess"] =
                            currentWindow.ProcessName,

                        ["previousTitle"] =
                            previousWindow.Title,

                        ["currentTitle"] =
                            currentWindow.Title,

                        ["currentProcessId"] =
                            currentWindow.ProcessId
                                .ToString(),

                        ["fullscreen"] =
                            currentWindow.IsFullscreen
                                .ToString()
                    },

                CurrentState =
                    current
            };
        }


        // =====================================================
        // 2. LONG IDLE - EDGE DETECTION
        // =====================================================

        bool wasIdle =
            previous.User.IdleTime >=
            LongIdleThreshold;


        bool isIdle =
            current.User.IdleTime >=
            LongIdleThreshold;


        // Log only the idle threshold transition, not every PC snapshot.


        if (!wasIdle &&
            isIdle)
        {
            Debug.WriteLine(
                "[Perception] " +
                "USER IDLE EVENT TRIGGERED");


            return new PerceptionEvent
            {
                Type =
                    "UserIdle",

                TopicKey =
                    NIRASocialTopicKeys.UserIdle,

                Description =
                    $"The user has been inactive on the PC " +
                    $"for more than " +
                    $"{LongIdleThreshold.TotalMinutes:F0} minute.",

                Metadata =
                    new Dictionary<
                        string,
                        string>(
                            StringComparer.OrdinalIgnoreCase)
                    {
                        ["idleSeconds"] =
                            current.User
                                .IdleTime
                                .TotalSeconds
                                .ToString("F0"),

                        ["process"] =
                            currentWindow.ProcessName,

                        ["windowTitle"] =
                            currentWindow.Title
                    },

                CurrentState =
                    current
            };
        }


        // =====================================================
        // 3. FOREGROUND WINDOW CONTENT CHANGED
        //
        // Same process, different title.
        //
        // Examples:
        //
        // Chrome:
        // GitHub -> YouTube
        //
        // VS Code:
        // AgentCore.cs -> PcWorldState.cs
        //
        // This is intentionally different from an application
        // change.
        // =====================================================

        if (!string.Equals(
                previousWindow.Title,
                currentWindow.Title,
                StringComparison.Ordinal))
        {
            string previousTitle =
                NormalizeTitle(
                    previousWindow.Title);


            string currentTitle =
                NormalizeTitle(
                    currentWindow.Title);


 
            return new PerceptionEvent
            {
                Type =
                    "ForegroundWindowChanged",

                TopicKey =
                    NIRASocialTopicKeys
                        .ForegroundWindow(
                            currentWindow.ProcessName),

                Description =
                    $"The active window inside " +
                    $"{NormalizeProcessName(currentWindow.ProcessName)} " +
                    $"changed from \"{previousTitle}\" " +
                    $"to \"{currentTitle}\".",

                Metadata =
                    new Dictionary<
                        string,
                        string>(
                            StringComparer.OrdinalIgnoreCase)
                    {
                        ["process"] =
                            currentWindow.ProcessName,

                        ["previousTitle"] =
                            previousWindow.Title,

                        ["currentTitle"] =
                            currentWindow.Title,

                        ["processId"] =
                            currentWindow.ProcessId
                                .ToString()
                    },

                CurrentState =
                    current
            };
        }


        return null;
    }


    // =========================================================
    // SAME PROCESS
    // =========================================================

    private static bool IsSameForegroundProcess(
        PcForegroundWindowState previous,
        PcForegroundWindowState current)
    {
        // =========================================================
        // APPLICATION IDENTITY
        //
        // ProcessName represents the application identity.
        //
        // PID represents one specific running process instance.
        //
        // Applications such as browsers, editors and other
        // multi-process programs may have different PIDs while
        // still being the same application.
        // =========================================================

        if (!string.IsNullOrWhiteSpace(
                previous.ProcessName)
            &&
            !string.IsNullOrWhiteSpace(
                current.ProcessName))
        {
            return string.Equals(
                previous.ProcessName,
                current.ProcessName,
                StringComparison.OrdinalIgnoreCase);
        }


        // =========================================================
        // FALLBACK
        //
        // Only use PID if process names could not be resolved.
        // =========================================================

        if (previous.ProcessId > 0 &&
            current.ProcessId > 0)
        {
            return
                previous.ProcessId ==
                current.ProcessId;
        }


        return
            previous.Handle ==
            current.Handle;
    }

    // =========================================================
    // DESCRIBE WINDOW
    // =========================================================

    private static string DescribeWindow(
        PcForegroundWindowState window)
    {
        string process =
            NormalizeProcessName(
                window.ProcessName);


        string title =
            NormalizeTitle(
                window.Title);


        if (title ==
            "Unknown")
        {
            return process;
        }


        return
            $"{process} ({title})";
    }


    // =========================================================
    // NORMALIZE PROCESS
    // =========================================================

    private static string NormalizeProcessName(
        string? processName)
    {
        return string.IsNullOrWhiteSpace(
                processName)
            ? "Unknown application"
            : processName.Trim();
    }


    // =========================================================
    // NORMALIZE TITLE
    // =========================================================

    private static string NormalizeTitle(
        string? title)
    {
        return string.IsNullOrWhiteSpace(
                title)
            ? "Unknown"
            : title.Trim();
    }
}

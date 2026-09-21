/*
 * filename: NIRASocialTopicKeys.cs
 */

using System.Text;

namespace NIRAAgent.Character.History;

public static class NIRASocialTopicKeys
{
    // =========================================================
    // CONVERSATION
    // =========================================================

    public const string UserConversation =
        "conversation:user";


    // =========================================================
    // PC
    // =========================================================

    public const string UserIdle =
        "pc:user-idle";


    // =========================================================
    // PROACTIVE
    // =========================================================

    public const string CompanionCheck =
        "proactive:companion-check";


    // =========================================================
    // FOREGROUND APPLICATION
    //
    // Examples:
    //
    // pc:foreground:chrome
    // pc:foreground:code
    // =========================================================

    public static string ForegroundApplication(
        string? processName)
    {
        return
            $"pc:foreground:" +
            $"{NormalizePart(processName)}";
    }


    // =========================================================
    // FOREGROUND WINDOW
    //
    // IMPORTANT:
    //
    // The title is deliberately NOT included.
    //
    // Otherwise:
    //
    // AgentCore.cs
    // PcMonitorService.cs
    // Chrome Tab A
    // Chrome Tab B
    //
    // would all become unrelated social topics.
    //
    // We want NIRA to understand that these are repeated
    // window changes inside the same application.
    // =========================================================

    public static string ForegroundWindow(
        string? processName)
    {
        return
            $"pc:window:" +
            $"{NormalizePart(processName)}";
    }


    // =========================================================
    // GENERIC EVENT
    // =========================================================

    public static string Event(
        string? eventName)
    {
        return
            $"event:" +
            $"{NormalizePart(eventName)}";
    }


    // =========================================================
    // NORMALIZE
    // =========================================================

    private static string NormalizePart(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "unknown";
        }


        string input =
            value
                .Trim()
                .ToLowerInvariant();


        StringBuilder builder =
            new();


        bool previousSeparator =
            false;


        foreach (char character
                 in input)
        {
            if (char.IsLetterOrDigit(
                    character)
                ||
                character == '_'
                ||
                character == '.')
            {
                builder.Append(
                    character);


                previousSeparator =
                    false;


                continue;
            }


            if (!previousSeparator)
            {
                builder.Append(
                    '-');


                previousSeparator =
                    true;
            }
        }


        string result =
            builder
                .ToString()
                .Trim('-');


        return string.IsNullOrWhiteSpace(
                result)
            ? "unknown"
            : result;
    }
}

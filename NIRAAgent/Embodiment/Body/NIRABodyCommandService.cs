/*
 * filename: NIRABodyCommandService.cs
 */

using System.Diagnostics;

namespace NIRAAgent.Embodiment.Body;

public sealed class NIRABodyCommandService
{
    private long
        _sequence;


    // =========================================================
    // EVENT
    //
    // NIRA's UI body subscribes to this command stream.
    // =========================================================

    public event Action<NIRABodyCommand>?
        CommandIssued;


    // =========================================================
    // ISSUE
    // =========================================================

    public NIRABodyCommand Issue(
        NIRABodyCommand command)
    {
        ArgumentNullException.ThrowIfNull(
            command);


        NIRABodyCommand prepared =
            command with
            {
                Sequence =
                    Interlocked.Increment(
                        ref _sequence),

                IssuedAt =
                    DateTimeOffset.UtcNow
            };


        Publish(
            prepared);


        Debug.WriteLine(
            $"[NIRABody] " +
            $"Command #{prepared.Sequence} | " +
            $"{prepared.Type} | " +
            $"Source={prepared.Source}");


        return prepared;
    }


    // =========================================================
    // CONVENIENCE
    // =========================================================

    public NIRABodyCommand MoveTo(
        int screenLeft,
        int screenTop,
        TimeSpan? duration = null,
        NIRABodyCommandSource source =
            NIRABodyCommandSource.System)
    {
        return Issue(
            NIRABodyCommand.MoveTo(
                screenLeft,
                screenTop,
                duration,
                source));
    }


    public NIRABodyCommand Show(
        NIRABodyCommandSource source =
            NIRABodyCommandSource.System)
    {
        return Issue(
            NIRABodyCommand.Show(
                source));
    }


    public NIRABodyCommand Hide(
        NIRABodyCommandSource source =
            NIRABodyCommandSource.System)
    {
        return Issue(
            NIRABodyCommand.Hide(
                source));
    }


    // =========================================================
    // PUBLISH
    // =========================================================

    private void Publish(
        NIRABodyCommand command)
    {
        Action<NIRABodyCommand>?
            handlers =
                CommandIssued;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<NIRABodyCommand> handler
            in handlers.GetInvocationList())
        {
            try
            {
                handler(
                    command);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[NIRABody] " +
                    $"COMMAND OBSERVER ERROR: {ex}");
            }
        }
    }
}


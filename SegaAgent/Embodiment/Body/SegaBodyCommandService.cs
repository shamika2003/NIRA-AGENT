/*
 * filename: SegaBodyCommandService.cs
 */

using System.Diagnostics;

namespace SegaAgent.Embodiment.Body;

public sealed class SegaBodyCommandService
{
    private long
        _sequence;


    // =========================================================
    // EVENT
    //
    // Sega's UI body subscribes to this command stream.
    // =========================================================

    public event Action<SegaBodyCommand>?
        CommandIssued;


    // =========================================================
    // ISSUE
    // =========================================================

    public SegaBodyCommand Issue(
        SegaBodyCommand command)
    {
        ArgumentNullException.ThrowIfNull(
            command);


        SegaBodyCommand prepared =
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
            $"[SegaBody] " +
            $"Command #{prepared.Sequence} | " +
            $"{prepared.Type} | " +
            $"Source={prepared.Source}");


        return prepared;
    }


    // =========================================================
    // CONVENIENCE
    // =========================================================

    public SegaBodyCommand MoveTo(
        int screenLeft,
        int screenTop,
        TimeSpan? duration = null,
        SegaBodyCommandSource source =
            SegaBodyCommandSource.System)
    {
        return Issue(
            SegaBodyCommand.MoveTo(
                screenLeft,
                screenTop,
                duration,
                source));
    }


    public SegaBodyCommand Show(
        SegaBodyCommandSource source =
            SegaBodyCommandSource.System)
    {
        return Issue(
            SegaBodyCommand.Show(
                source));
    }


    public SegaBodyCommand Hide(
        SegaBodyCommandSource source =
            SegaBodyCommandSource.System)
    {
        return Issue(
            SegaBodyCommand.Hide(
                source));
    }


    // =========================================================
    // PUBLISH
    // =========================================================

    private void Publish(
        SegaBodyCommand command)
    {
        Action<SegaBodyCommand>?
            handlers =
                CommandIssued;


        if (handlers ==
            null)
        {
            return;
        }


        foreach (
            Action<SegaBodyCommand> handler
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
                    $"[SegaBody] " +
                    $"COMMAND OBSERVER ERROR: {ex}");
            }
        }
    }
}

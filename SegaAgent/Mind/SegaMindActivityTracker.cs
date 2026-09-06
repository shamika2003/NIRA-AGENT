/*
 * filename: SegaMindActivityTracker.cs
 */

namespace SegaAgent.Mind;

public sealed class SegaMindActivityTracker
{
    private readonly object
        _sync =
            new();


    private DateTimeOffset
        _lastUserInteractionUtc;


    private DateTimeOffset
        _lastAutonomousActivityUtc;


    private int
        _activeRuns;


    public SegaMindActivityTracker()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;


        _lastUserInteractionUtc =
            now;


        _lastAutonomousActivityUtc =
            DateTimeOffset.MinValue;
    }


    public bool IsProcessing
    {
        get
        {
            lock (_sync)
            {
                return _activeRuns >
                    0;
            }
        }
    }


    public int ActiveRuns
    {
        get
        {
            lock (_sync)
            {
                return _activeRuns;
            }
        }
    }


    public TimeSpan TimeSinceUserInteraction
    {
        get
        {
            lock (_sync)
            {
                return DateTimeOffset.UtcNow -
                    _lastUserInteractionUtc;
            }
        }
    }


    public TimeSpan TimeSinceAutonomousActivity
    {
        get
        {
            lock (_sync)
            {
                if (_lastAutonomousActivityUtc ==
                    DateTimeOffset.MinValue)
                {
                    return TimeSpan.MaxValue;
                }


                return DateTimeOffset.UtcNow -
                    _lastAutonomousActivityUtc;
            }
        }
    }


    public void RecordUserInteraction()
    {
        lock (_sync)
        {
            _lastUserInteractionUtc =
                DateTimeOffset.UtcNow;
        }
    }


    public void RecordAutonomousActivity()
    {
        lock (_sync)
        {
            _lastAutonomousActivityUtc =
                DateTimeOffset.UtcNow;
        }
    }


    public void BeginRun()
    {
        lock (_sync)
        {
            _activeRuns++;
        }
    }


    public void EndRun()
    {
        lock (_sync)
        {
            _activeRuns =
                Math.Max(
                    0,
                    _activeRuns -
                        1);
        }
    }
}

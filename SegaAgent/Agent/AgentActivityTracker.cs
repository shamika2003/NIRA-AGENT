/*
 * filename: AgentActivityTracker.cs
 */

namespace SegaAgent.Agent;

public sealed class AgentActivityTracker
{
    private readonly object _lock = new();

    private DateTimeOffset _lastUserInteractionUtc;

    private DateTimeOffset _lastAutonomousActivityUtc;

    private bool _isProcessing;


    public AgentActivityTracker()
    {
        var now =
            DateTimeOffset.UtcNow;

        _lastUserInteractionUtc = now;

        _lastAutonomousActivityUtc =
            DateTimeOffset.MinValue;
    }


    // =========================================================
    // USER INTERACTION
    // =========================================================

    public void RecordUserInteraction()
    {
        lock (_lock)
        {
            _lastUserInteractionUtc =
                DateTimeOffset.UtcNow;
        }
    }


    public TimeSpan TimeSinceUserInteraction
    {
        get
        {
            lock (_lock)
            {
                return
                    DateTimeOffset.UtcNow -
                    _lastUserInteractionUtc;
            }
        }
    }


    // =========================================================
    // AUTONOMOUS ACTIVITY
    // =========================================================

    public TimeSpan TimeSinceAutonomousActivity
    {
        get
        {
            lock (_lock)
            {
                if (_lastAutonomousActivityUtc ==
                    DateTimeOffset.MinValue)
                {
                    return TimeSpan.MaxValue;
                }

                return
                    DateTimeOffset.UtcNow -
                    _lastAutonomousActivityUtc;
            }
        }
    }


    public void RecordAutonomousActivity()
    {
        lock (_lock)
        {
            _lastAutonomousActivityUtc =
                DateTimeOffset.UtcNow;
        }
    }


    // =========================================================
    // PROCESSING
    // =========================================================

    public bool IsProcessing
    {
        get
        {
            lock (_lock)
            {
                return _isProcessing;
            }
        }
    }


    public void BeginProcessing()
    {
        lock (_lock)
        {
            _isProcessing = true;
        }
    }


    public void EndProcessing()
    {
        lock (_lock)
        {
            _isProcessing = false;
        }
    }
}
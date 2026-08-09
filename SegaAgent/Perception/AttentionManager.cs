/*
 * filename: AttentionManager.cs
 */

using SegaAgent.Agent;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class AttentionManager
{
    // =========================================================
    // SETTINGS
    // =========================================================

    private static readonly TimeSpan
        MinimumUserInactivity =
            TimeSpan.FromMinutes(10);

    private static readonly TimeSpan
        AutonomousCooldown =
            TimeSpan.FromMinutes(10);

    private static readonly TimeSpan
        PerceptionEventCooldown =
            TimeSpan.FromMinutes(5);


    private readonly AgentActivityTracker _activity;

    private readonly object _lock = new();

    private DateTimeOffset _lastPerceptionUtc =
        DateTimeOffset.MinValue;

    private string? _lastPerceptionKey;

    private PerceptionEvent? _pending;


    public AttentionManager(
        AgentActivityTracker activity)
    {
        _activity = activity;
    }


    // =========================================================
    // PROACTIVE CHECK
    // =========================================================

    public bool CanRunProactiveCheck()
    {
        if (_activity.IsProcessing)
            return false;


        if (_activity.TimeSinceUserInteraction <
            MinimumUserInactivity)
        {
            return false;
        }


        if (_activity.TimeSinceAutonomousActivity <
            AutonomousCooldown)
        {
            return false;
        }


        return true;
    }


    // =========================================================
    // PERCEPTION
    // =========================================================

    public bool TryAcceptPerception(
        PerceptionEvent perception)
    {
        if (_activity.IsProcessing)
        {
            Queue(perception);

            return false;
        }


        if (_activity.TimeSinceUserInteraction <
            MinimumUserInactivity)
        {
            Queue(perception);

            return false;
        }


        if (_activity.TimeSinceAutonomousActivity <
            AutonomousCooldown)
        {
            Queue(perception);

            return false;
        }


        var key =
            BuildEventKey(perception);


        lock (_lock)
        {
            if (_lastPerceptionKey == key &&
                DateTimeOffset.UtcNow -
                _lastPerceptionUtc <
                PerceptionEventCooldown)
            {
                return false;
            }


            _lastPerceptionKey = key;

            _lastPerceptionUtc =
                DateTimeOffset.UtcNow;
        }


        return true;
    }


    // =========================================================
    // PENDING
    // =========================================================

    private void Queue(
        PerceptionEvent perception)
    {
        lock (_lock)
        {
            // Keep only the newest event.
            //
            // We don't want a queue like:
            //
            // Chrome
            // Visual Studio
            // Chrome
            // Valorant
            // ...
            //
            // waiting for the AI.

            _pending = perception;
        }
    }


    public bool TryTakePending(
        PcState currentState,
        out PerceptionEvent? perception)
    {
        perception = null;


        if (_activity.IsProcessing)
            return false;


        if (_activity.TimeSinceUserInteraction <
            MinimumUserInactivity)
        {
            return false;
        }


        if (_activity.TimeSinceAutonomousActivity <
            AutonomousCooldown)
        {
            return false;
        }


        lock (_lock)
        {
            if (_pending == null)
                return false;


            var pending =
                _pending;


            // =============================================
            // Validate that the event is still relevant.
            // =============================================

            if (pending.Type ==
                    "ApplicationChanged" &&
                pending.CurrentState.ActiveApplication !=
                    currentState.ActiveApplication)
            {
                _pending = null;

                return false;
            }


            perception = pending;

            _pending = null;

            return true;
        }
    }


    // =========================================================
    // EVENT KEY
    // =========================================================

    private static string BuildEventKey(
        PerceptionEvent perception)
    {
        return
            $"{perception.Type}|" +
            $"{perception.CurrentState.ActiveApplication}";
    }
}
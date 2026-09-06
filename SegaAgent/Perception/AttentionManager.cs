/*
 * filename: AttentionManager.cs
 */

using SegaAgent.Mind;
using SegaAgent.Character.History;
using SegaAgent.Character.State;
using SegaAgent.PC.Awareness;
using SegaAgent.Settings;

namespace SegaAgent.Perception;

public sealed class AttentionManager
{
    private static readonly TimeSpan
        BasePerceptionUserInactivity =
            TimeSpan.FromMinutes(
                1);


    private static readonly TimeSpan
        BaseProactiveUserInactivity =
            TimeSpan.FromMinutes(
                8);


    private static readonly TimeSpan
        BaseAutonomousCooldown =
            TimeSpan.FromMinutes(
                6);


    private static readonly TimeSpan
        PerceptionEventCooldown =
            TimeSpan.FromMinutes(
                2);


    private readonly SegaMindActivityTracker
        _activity;


    private readonly SegaCharacterStateService
        _character;


    private readonly SegaSocialHistoryService
        _history;


    private readonly SegaRuntimeSettingsService
        _settings;


    private readonly object _lock =
        new();


    private DateTimeOffset _lastPerceptionUtc =
        DateTimeOffset.MinValue;


    private string?
        _lastPerceptionKey;


    private PerceptionEvent?
        _pending;


    public AttentionManager(
        SegaMindActivityTracker activity,
        SegaCharacterStateService character,
        SegaSocialHistoryService history,
        SegaRuntimeSettingsService settings)
    {
        _activity =
            activity
            ?? throw new ArgumentNullException(
                nameof(activity));


        _character =
            character
            ?? throw new ArgumentNullException(
                nameof(character));


        _history =
            history
            ?? throw new ArgumentNullException(
                nameof(history));


        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));
    }


    public bool IsPerceptionEnabled(
        PerceptionEvent perception)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        SegaRuntimeSettings settings =
            _settings.Current;


        if (perception.Type.Equals(
                "UserIdle",
                StringComparison.OrdinalIgnoreCase))
        {
            return settings.IdleBehaviorEnabled
                && settings.UserIdleAwarenessEnabled;
        }


        if (
            perception.Type.Equals(
                "ForegroundApplicationChanged",
                StringComparison.OrdinalIgnoreCase)
            ||
            perception.Type.Equals(
                "ForegroundWindowChanged",
                StringComparison.OrdinalIgnoreCase))
        {
            return settings.PcContextReactionsEnabled;
        }


        return true;
    }


    public bool CanRunProactiveCheck()
    {
        if (!_settings.Current.ProactiveCompanionEnabled)
        {
            return false;
        }


        if (_activity.IsProcessing)
        {
            return false;
        }


        SegaCharacterSnapshot character =
            _character.Current;


        TimeSpan requiredUserInactivity =
            ResolveProactiveUserInactivity(
                character);


        TimeSpan cooldown =
            ResolveAutonomousCooldown(
                character);


        if (_activity.TimeSinceUserInteraction <
            requiredUserInactivity)
        {
            return false;
        }


        if (_activity.TimeSinceAutonomousActivity <
            cooldown)
        {
            return false;
        }


        if (_history.HasRecentEvent(
                SegaSocialEventKind.SegaResponse,
                SegaSocialTopicKeys.CompanionCheck,
                cooldown))
        {
            return false;
        }


        return true;
    }


    public bool TryAcceptPerception(
        PerceptionEvent perception)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        if (!IsPerceptionEnabled(
                perception))
        {
            return false;
        }


        SegaCharacterSnapshot character =
            _character.Current;


        if (WasRecentlyAnswered(
                perception,
                character))
        {
            return false;
        }


        if (_activity.IsProcessing)
        {
            Queue(
                perception);

            return false;
        }


        TimeSpan requiredUserInactivity =
            ResolvePerceptionUserInactivity(
                character);


        if (_activity.TimeSinceUserInteraction <
            requiredUserInactivity)
        {
            Queue(
                perception);

            return false;
        }


        TimeSpan autonomousCooldown =
            ResolveAutonomousCooldown(
                character);


        if (_activity.TimeSinceAutonomousActivity <
            autonomousCooldown)
        {
            Queue(
                perception);

            return false;
        }


        string key =
            BuildEventKey(
                perception);


        lock (_lock)
        {
            if (
                _lastPerceptionKey ==
                    key
                &&
                DateTimeOffset.UtcNow -
                    _lastPerceptionUtc <
                PerceptionEventCooldown)
            {
                return false;
            }


            _lastPerceptionKey =
                key;


            _lastPerceptionUtc =
                DateTimeOffset.UtcNow;
        }


        return true;
    }


    public bool TryTakePending(
        PcWorldState currentState,
        out PerceptionEvent? perception)
    {
        perception =
            null;


        if (_activity.IsProcessing)
        {
            return false;
        }


        SegaCharacterSnapshot character =
            _character.Current;


        if (_activity.TimeSinceUserInteraction <
            ResolvePerceptionUserInactivity(
                character))
        {
            return false;
        }


        if (_activity.TimeSinceAutonomousActivity <
            ResolveAutonomousCooldown(
                character))
        {
            return false;
        }


        lock (_lock)
        {
            if (_pending ==
                null)
            {
                return false;
            }


            PerceptionEvent pending =
                _pending;


            if (!IsPerceptionEnabled(
                    pending))
            {
                _pending =
                    null;

                return false;
            }


            if (
                !IsStillRelevant(
                    pending,
                    currentState)
                ||
                WasRecentlyAnswered(
                    pending,
                    character))
            {
                _pending =
                    null;

                return false;
            }


            perception =
                pending;


            _pending =
                null;


            return true;
        }
    }


    private void Queue(
        PerceptionEvent perception)
    {
        lock (_lock)
        {
            _pending =
                perception;
        }
    }


    private bool WasRecentlyAnswered(
        PerceptionEvent perception,
        SegaCharacterSnapshot character)
    {
        if (string.IsNullOrWhiteSpace(
                perception.TopicKey))
        {
            return false;
        }


        TimeSpan responseWindow =
            ResolveResponseSuppressionWindow(
                perception,
                character);


        return _history.HasRecentEvent(
            SegaSocialEventKind.SegaResponse,
            perception.TopicKey,
            responseWindow);
    }


    private static TimeSpan
        ResolveResponseSuppressionWindow(
            PerceptionEvent perception,
            SegaCharacterSnapshot character)
    {
        double minutes =
            perception.Type switch
            {
                "ForegroundWindowChanged" =>
                    12.0,

                "ForegroundApplicationChanged" =>
                    10.0,

                "UserIdle" =>
                    20.0,

                "CompanionCheck" =>
                    20.0,

                _ =>
                    10.0
            };


        if (
            character.Situation.Mode ==
                SegaInteractionMode.FocusedWork)
        {
            minutes +=
                12.0 *
                character
                    .Situation
                    .Intensity;
        }


        minutes +=
            character
                .Relationship
                .Friction *
            8.0;


        return TimeSpan.FromMinutes(
            Math.Clamp(
                minutes,
                5.0,
                40.0));
    }


    private static TimeSpan
        ResolvePerceptionUserInactivity(
            SegaCharacterSnapshot character)
    {
        double minutes =
            BasePerceptionUserInactivity
                .TotalMinutes;


        if (
            character.Situation.Mode ==
                SegaInteractionMode.FocusedWork)
        {
            minutes +=
                character
                    .Situation
                    .Intensity *
                3.0;
        }


        return TimeSpan.FromMinutes(
            Math.Clamp(
                minutes,
                1.0,
                5.0));
    }


    private static TimeSpan
        ResolveProactiveUserInactivity(
            SegaCharacterSnapshot character)
    {
        SegaRelationshipState relationship =
            character.Relationship;


        double closeness =
            (
                relationship.Familiarity
                +
                relationship.Warmth
                +
                relationship.Playfulness
            )
            /
            3.0;


        double minutes =
            BaseProactiveUserInactivity
                .TotalMinutes;


        minutes -=
            closeness *
            3.0;


        minutes +=
            relationship.Friction *
            12.0;


        if (
            character.Situation.Mode ==
                SegaInteractionMode.FocusedWork)
        {
            minutes +=
                15.0 *
                character
                    .Situation
                    .Intensity;
        }


        if (
            character.Situation.Mode ==
                SegaInteractionMode.Serious)
        {
            minutes +=
                8.0 *
                character
                    .Situation
                    .Intensity;
        }


        return TimeSpan.FromMinutes(
            Math.Clamp(
                minutes,
                4.0,
                30.0));
    }


    private static TimeSpan
        ResolveAutonomousCooldown(
            SegaCharacterSnapshot character)
    {
        SegaRelationshipState relationship =
            character.Relationship;


        double closeness =
            (
                relationship.Warmth
                +
                relationship.Familiarity
                +
                relationship.Playfulness
            )
            /
            3.0;


        double minutes =
            BaseAutonomousCooldown
                .TotalMinutes;


        minutes -=
            closeness *
            2.0;


        minutes +=
            relationship.Friction *
            8.0;


        if (
            character.Situation.Mode ==
                SegaInteractionMode.FocusedWork)
        {
            minutes +=
                character
                    .Situation
                    .Intensity *
                8.0;
        }


        return TimeSpan.FromMinutes(
            Math.Clamp(
                minutes,
                3.0,
                20.0));
    }


    private static bool IsStillRelevant(
        PerceptionEvent pending,
        PcWorldState currentState)
    {
        PcForegroundWindowState pendingWindow =
            pending
                .CurrentState
                .ForegroundWindow;


        PcForegroundWindowState currentWindow =
            currentState
                .ForegroundWindow;


        return pending.Type switch
        {
            "ForegroundApplicationChanged" =>
                IsSameWindowProcess(
                    pendingWindow,
                    currentWindow),

            "ForegroundWindowChanged" =>
                IsSameWindowProcess(
                    pendingWindow,
                    currentWindow)
                &&
                string.Equals(
                    pendingWindow.Title,
                    currentWindow.Title,
                    StringComparison.Ordinal),

            "UserIdle" =>
                currentState.User.IdleTime >=
                pending.CurrentState.User.IdleTime,

            _ =>
                true
        };
    }


    private static bool IsSameWindowProcess(
        PcForegroundWindowState first,
        PcForegroundWindowState second)
    {
        if (
            !string.IsNullOrWhiteSpace(
                first.ProcessName)
            &&
            !string.IsNullOrWhiteSpace(
                second.ProcessName))
        {
            return string.Equals(
                first.ProcessName,
                second.ProcessName,
                StringComparison.OrdinalIgnoreCase);
        }


        if (
            first.ProcessId >
                0
            &&
            second.ProcessId >
                0)
        {
            return first.ProcessId ==
                second.ProcessId;
        }


        return first.Handle ==
            second.Handle;
    }


    private static string BuildEventKey(
        PerceptionEvent perception)
    {
        PcForegroundWindowState window =
            perception
                .CurrentState
                .ForegroundWindow;


        return perception.Type switch
        {
            "ForegroundApplicationChanged" =>
                $"{perception.Type}|" +
                $"{NormalizeApplicationName(
                    window.ProcessName)}",

            "ForegroundWindowChanged" =>
                $"{perception.Type}|" +
                $"{NormalizeApplicationName(
                    window.ProcessName)}|" +
                $"{window.Title}",

            "UserIdle" =>
                perception.Type,

            _ =>
                $"{perception.Type}|" +
                $"{NormalizeApplicationName(
                    window.ProcessName)}|" +
                $"{window.Title}"
        };
    }


    private static string NormalizeApplicationName(
        string? processName)
    {
        return string.IsNullOrWhiteSpace(
                processName)
            ? "unknown"
            : processName
                .Trim()
                .ToLowerInvariant();
    }
}
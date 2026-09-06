/*
 * filename: CompanionTimerService.cs
 */

using Microsoft.Extensions.Hosting;

using SegaAgent.Mind;
using SegaAgent.Character.History;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Perception;

public sealed class CompanionTimerService
    : BackgroundService
{
    private readonly PcWorldStateService
        _worldState;


    private readonly AttentionManager
        _attention;


    private readonly SegaBackgroundProcessor
        _backgroundProcessor;


    private readonly SegaSocialHistoryService
        _socialHistory;


    public CompanionTimerService(
        PcWorldStateService worldState,
        AttentionManager attention,
        SegaBackgroundProcessor backgroundProcessor,
        SegaSocialHistoryService socialHistory)
    {
        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));


        _attention =
            attention
            ?? throw new ArgumentNullException(
                nameof(attention));


        _backgroundProcessor =
            backgroundProcessor
            ?? throw new ArgumentNullException(
                nameof(backgroundProcessor));


        _socialHistory =
            socialHistory
            ?? throw new ArgumentNullException(
                nameof(socialHistory));
    }


    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using PeriodicTimer timer =
            new(
                TimeSpan.FromMinutes(
                    1));


        try
        {
            while (
                await timer.WaitForNextTickAsync(
                    stoppingToken))
            {
                if (!_attention
                    .CanRunProactiveCheck())
                {
                    continue;
                }


                PcWorldState currentState =
                    _worldState.Current;


                PerceptionEvent perception =
                    new()
                    {
                        Type =
                            "CompanionCheck",

                        TopicKey =
                            SegaSocialTopicKeys
                                .CompanionCheck,

                        Description =
                            "A natural opportunity for Sega to " +
                            "interact proactively may exist. " +
                            "Use the current relationship, mood, " +
                            "recent social history and PC context. " +
                            "Do not speak merely because this " +
                            "check occurred.",

                        Metadata =
                            new Dictionary<
                                string,
                                string>(
                                    StringComparer
                                        .OrdinalIgnoreCase)
                            {
                                ["process"] =
                                    currentState
                                        .ForegroundWindow
                                        .ProcessName,

                                ["windowTitle"] =
                                    currentState
                                        .ForegroundWindow
                                        .Title,

                                ["idleSeconds"] =
                                    currentState
                                        .User
                                        .IdleTime
                                        .TotalSeconds
                                        .ToString(
                                            "F0")
                            },

                        CurrentState =
                            currentState
                    };


                SegaSocialEvent socialEvent =
                    _socialHistory.Record(
                        SegaSocialEventSource.System,
                        SegaSocialEventKind.ProactiveEvent,
                        perception.Type,
                        perception.TopicKey,
                        perception.Description,
                        perception.Metadata);


                perception.SocialEventId =
                    socialEvent.Id;


                await _backgroundProcessor
                    .ProcessProactiveAsync(
                        perception,
                        stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken
                .IsCancellationRequested)
        {
        }
    }
}

/*
 * filename: SegaMindRuntime.cs
 */

using System.Runtime.CompilerServices;

using SegaAgent.Agent.State;
using SegaAgent.Character.History;
using SegaAgent.Conversation;
using SegaAgent.Perception;

namespace SegaAgent.Mind;

public sealed class SegaMindRuntime
{
    private readonly SegaExecutive
        _executive;


    private readonly SegaMindActivityTracker
        _activity;


    private readonly SegaStateService
        _state;


    private readonly SegaSocialHistoryService
        _socialHistory;


    private readonly ConversationManager
        _conversation;


    private int
        _activeCognitionRuns;


    public SegaMindRuntime(
        SegaExecutive executive,
        SegaMindActivityTracker activity,
        SegaStateService state,
        SegaSocialHistoryService socialHistory,
        ConversationManager conversation)
    {
        _executive =
            executive
            ?? throw new ArgumentNullException(
                nameof(executive));


        _activity =
            activity
            ?? throw new ArgumentNullException(
                nameof(activity));


        _state =
            state
            ?? throw new ArgumentNullException(
                nameof(state));


        _socialHistory =
            socialHistory
            ?? throw new ArgumentNullException(
                nameof(socialHistory));


        _conversation =
            conversation
            ?? throw new ArgumentNullException(
                nameof(conversation));
    }


    public async IAsyncEnumerable<SegaOutputChunk> ProcessUserMessageAsync(
        string userInput,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                userInput))
        {
            yield break;
        }


        string input =
            userInput.Trim();


        _activity.RecordUserInteraction();


        _conversation.AddUserMessage(
            input);


        SegaSocialEvent socialEvent =
            _socialHistory.Record(
                SegaSocialEventSource.User,
                SegaSocialEventKind.UserMessage,
                "UserMessage",
                SegaSocialTopicKeys.UserConversation,
                input);


        SegaMindEvent mindEvent =
            SegaMindEvent.UserMessage(
                input,
                socialEvent.Id,
                socialEvent.TopicKey);


        await foreach (
            SegaOutputChunk chunk
            in RunTrackedAsync(
                mindEvent,
                autonomous: false,
                cancellationToken))
        {
            yield return chunk;
        }
    }


    public IAsyncEnumerable<SegaOutputChunk> ProcessInternalAsync(
        SegaMindEvent mindEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        if (mindEvent.Source !=
            SegaMindEventSource.Internal)
        {
            throw new ArgumentException(
                "ProcessInternalAsync requires an Internal SegaMindEvent.",
                nameof(mindEvent));
        }


        return RunTrackedAsync(
            mindEvent,
            autonomous: true,
            cancellationToken);
    }


    public IAsyncEnumerable<SegaOutputChunk> ProcessPerceptionAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        return RunTrackedAsync(
            SegaMindEvent.FromPerception(
                perception,
                proactive: false),
            autonomous: true,
            cancellationToken);
    }


    public IAsyncEnumerable<SegaOutputChunk> ProcessProactiveAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        return RunTrackedAsync(
            SegaMindEvent.FromPerception(
                perception,
                proactive: true),
            autonomous: true,
            cancellationToken);
    }


    private async IAsyncEnumerable<SegaOutputChunk> RunTrackedAsync(
        SegaMindEvent mindEvent,
        bool autonomous,
        [EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        BeginCognitionRun();


        bool completed =
            false;


        try
        {
            await foreach (
                SegaOutputChunk chunk
                in _executive.RunAsync(
                    mindEvent,
                    cancellationToken))
            {
                if (chunk.Type ==
                    SegaOutputChunkType.Completed)
                {
                    completed =
                        true;
                }


                yield return chunk;
            }
        }
        finally
        {
            EndCognitionRun();


            if (
                autonomous
                &&
                completed)
            {
                _activity.RecordAutonomousActivity();
            }
        }
    }


    private void BeginCognitionRun()
    {
        _activity.BeginRun();


        int active =
            Interlocked.Increment(
                ref _activeCognitionRuns);


        if (active ==
            1)
        {
            _state.SetThinking(
                true);
        }
    }


    private void EndCognitionRun()
    {
        _activity.EndRun();


        int active =
            Interlocked.Decrement(
                ref _activeCognitionRuns);


        if (active <=
            0)
        {
            Interlocked.Exchange(
                ref _activeCognitionRuns,
                0);


            _state.SetThinking(
                false);
        }
    }
}
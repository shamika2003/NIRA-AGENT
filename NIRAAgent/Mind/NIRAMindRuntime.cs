/*
 * filename: NIRAMindRuntime.cs
 */

using System.Runtime.CompilerServices;

using NIRAAgent.Agent.State;
using NIRAAgent.Character.History;
using NIRAAgent.Conversation;
using NIRAAgent.Perception;

namespace NIRAAgent.Mind;

public sealed class NIRAMindRuntime
{
    private readonly NIRAExecutive
        _executive;


    private readonly NIRAMindActivityTracker
        _activity;


    private readonly NIRAStateService
        _state;


    private readonly NIRASocialHistoryService
        _socialHistory;


    private readonly ConversationManager
        _conversation;


    private int
        _activeCognitionRuns;


    public NIRAMindRuntime(
        NIRAExecutive executive,
        NIRAMindActivityTracker activity,
        NIRAStateService state,
        NIRASocialHistoryService socialHistory,
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


    public async IAsyncEnumerable<NIRAOutputChunk> ProcessUserMessageAsync(
        string userInput,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default,
        Guid? attachedPastChatSessionId = null)
    {
        if (string.IsNullOrWhiteSpace(
                userInput))
        {
            yield break;
        }


        string input =
            userInput.Trim();


        _activity.RecordUserInteraction();


        NIRASocialEvent socialEvent =
            _socialHistory.Record(
                NIRASocialEventSource.User,
                NIRASocialEventKind.UserMessage,
                "UserMessage",
                NIRASocialTopicKeys.UserConversation,
                input);

        _conversation.AddUserMessage(input, socialEvent.Id);

        NIRAMindEvent mindEvent =
            NIRAMindEvent.UserMessage(
                input,
                socialEvent.Id,
                socialEvent.TopicKey) with
            {
                Metadata = attachedPastChatSessionId is Guid sid && sid != Guid.Empty
                    ? new Dictionary<string, string> { ["attachedPastChatSessionId"] = sid.ToString("D") }
                    : new Dictionary<string, string>()
            };


        await foreach (
            NIRAOutputChunk chunk
            in RunTrackedAsync(
                mindEvent,
                autonomous: false,
                cancellationToken))
        {
            yield return chunk;
        }
    }


    public IAsyncEnumerable<NIRAOutputChunk> ProcessInternalAsync(
        NIRAMindEvent mindEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        if (mindEvent.Source !=
            NIRAMindEventSource.Internal)
        {
            throw new ArgumentException(
                "ProcessInternalAsync requires an Internal NIRAMindEvent.",
                nameof(mindEvent));
        }


        return RunTrackedAsync(
            mindEvent,
            autonomous: true,
            cancellationToken);
    }


    public IAsyncEnumerable<NIRAOutputChunk> ProcessPerceptionAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        return RunTrackedAsync(
            NIRAMindEvent.FromPerception(
                perception,
                proactive: false),
            autonomous: true,
            cancellationToken);
    }


    public IAsyncEnumerable<NIRAOutputChunk> ProcessProactiveAsync(
        PerceptionEvent perception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            perception);


        return RunTrackedAsync(
            NIRAMindEvent.FromPerception(
                perception,
                proactive: true),
            autonomous: true,
            cancellationToken);
    }


    private async IAsyncEnumerable<NIRAOutputChunk> RunTrackedAsync(
        NIRAMindEvent mindEvent,
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
                NIRAOutputChunk chunk
                in _executive.RunAsync(
                    mindEvent,
                    cancellationToken))
            {
                if (chunk.Type ==
                    NIRAOutputChunkType.Completed)
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

/*
 * filename: AgentCore.cs
 */

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using SegaAgent.AI.Planner;
using SegaAgent.AI.Responder;
using SegaAgent.Agent.State;
using SegaAgent.Character.Appraisal;
using SegaAgent.Character.Dynamics;
using SegaAgent.Character.History;
using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;
using SegaAgent.Conversation;
using SegaAgent.PC.Awareness;
using SegaAgent.Perception;

namespace SegaAgent.Agent;

public sealed class AgentCore
    : IDisposable
{
    private readonly AgentPlanner _planner;

    private readonly AgentResponder _responder;

    private readonly ConversationManager
        _conversation;

    private readonly PcWorldStateService
        _worldState;

    private readonly AgentActivityTracker
        _activity;

    private readonly SegaStateService _state;

    private readonly SegaSocialHistoryService
        _socialHistory;

    private readonly SegaInteractionObservationService
        _interactionObservation;

    private readonly SegaCharacterStateService
        _characterState;

    private readonly SegaCharacterDynamicsService
        _characterDynamics;


    private readonly SemaphoreSlim
        _processingLock =
            new(
                1,
                1);


    private readonly object
        _autonomousLock =
            new();


    private CancellationTokenSource?
        _autonomousCancellation;


    public AgentCore(
        AgentPlanner planner,
        AgentResponder responder,
        ConversationManager conversation,
        PcWorldStateService worldState,
        AgentActivityTracker activity,
        SegaStateService state,
        SegaSocialHistoryService socialHistory,
        SegaInteractionObservationService interactionObservation,
        SegaCharacterStateService characterState,
        SegaCharacterDynamicsService characterDynamics)
    {
        _planner =
            planner
            ?? throw new ArgumentNullException(
                nameof(planner));


        _responder =
            responder
            ?? throw new ArgumentNullException(
                nameof(responder));


        _conversation =
            conversation
            ?? throw new ArgumentNullException(
                nameof(conversation));


        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));


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


        _interactionObservation =
            interactionObservation
            ?? throw new ArgumentNullException(
                nameof(interactionObservation));


        _characterState =
            characterState
            ?? throw new ArgumentNullException(
                nameof(characterState));


        _characterDynamics =
            characterDynamics
            ?? throw new ArgumentNullException(
                nameof(characterDynamics));
    }


    public async Task<string> ProcessAsync(
        string userInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                userInput))
        {
            return string.Empty;
        }


        _activity.RecordUserInteraction();


        CancelAutonomousProcessing();


        await _processingLock.WaitAsync(
            cancellationToken);


        _activity.BeginProcessing();


        _state.SetThinking(
            true);


        try
        {
            StringBuilder result =
                new();


            await foreach (
                AgentStreamChunk chunk
                in ProcessRequestAsync(
                    new UserAgentRequest(
                        userInput),
                    cancellationToken))
            {
                if (chunk.Type ==
                    AgentStreamChunkType.Text)
                {
                    result.Append(
                        chunk.Content);
                }
            }


            return result.ToString();
        }
        finally
        {
            _state.SetThinking(
                false);


            _activity.EndProcessing();


            _processingLock.Release();
        }
    }


    public async IAsyncEnumerable<
        AgentStreamChunk>
        ProcessStreamAsync(
            string userInput,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                userInput))
        {
            yield break;
        }


        _activity.RecordUserInteraction();


        CancelAutonomousProcessing();


        await _processingLock.WaitAsync(
            cancellationToken);


        _activity.BeginProcessing();


        _state.SetThinking(
            true);


        try
        {
            await foreach (
                AgentStreamChunk chunk
                in ProcessRequestAsync(
                    new UserAgentRequest(
                        userInput),
                    cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            _state.SetThinking(
                false);


            _activity.EndProcessing();


            _processingLock.Release();
        }
    }


    public async IAsyncEnumerable<
        AgentStreamChunk>
        ProcessPerceptionAsync(
            PerceptionEvent perception,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        if (perception ==
            null)
        {
            yield break;
        }


        if (!_processingLock.Wait(
                0))
        {
            yield break;
        }


        _activity.BeginProcessing();


        _state.SetThinking(
            true);


        CancellationTokenSource?
            autonomousCancellation =
                null;


        bool completed =
            false;


        try
        {
            autonomousCancellation =
                CreateAutonomousCancellationSource(
                    cancellationToken);


            await foreach (
                AgentStreamChunk chunk
                in ProcessRequestAsync(
                    new PerceptionAgentRequest(
                        perception),
                    autonomousCancellation.Token))
            {
                if (chunk.Type ==
                    AgentStreamChunkType.Completed)
                {
                    completed =
                        true;
                }


                yield return chunk;
            }
        }
        finally
        {
            _state.SetThinking(
                false);


            if (autonomousCancellation !=
                null)
            {
                ClearAutonomousCancellation(
                    autonomousCancellation);


                autonomousCancellation.Dispose();
            }


            /*
             * Only completed autonomous cognition enters the
             * cooldown.
             *
             * User cancellation/preemption no longer causes a
             * fake autonomous-activity timestamp.
             */

            if (completed)
            {
                _activity.RecordAutonomousActivity();
            }


            _activity.EndProcessing();


            _processingLock.Release();
        }
    }


    public async IAsyncEnumerable<
        AgentStreamChunk>
        ProcessProactiveAsync(
            PerceptionEvent perception,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        if (perception ==
            null)
        {
            yield break;
        }


        if (!_processingLock.Wait(
                0))
        {
            yield break;
        }


        _activity.BeginProcessing();


        _state.SetThinking(
            true);


        CancellationTokenSource?
            autonomousCancellation =
                null;


        bool completed =
            false;


        try
        {
            autonomousCancellation =
                CreateAutonomousCancellationSource(
                    cancellationToken);


            await foreach (
                AgentStreamChunk chunk
                in ProcessRequestAsync(
                    new ProactiveAgentRequest(
                        perception),
                    autonomousCancellation.Token))
            {
                if (chunk.Type ==
                    AgentStreamChunkType.Completed)
                {
                    completed =
                        true;
                }


                yield return chunk;
            }
        }
        finally
        {
            _state.SetThinking(
                false);


            if (autonomousCancellation !=
                null)
            {
                ClearAutonomousCancellation(
                    autonomousCancellation);


                autonomousCancellation.Dispose();
            }


            if (completed)
            {
                _activity.RecordAutonomousActivity();
            }


            _activity.EndProcessing();


            _processingLock.Release();
        }
    }


    private async IAsyncEnumerable<
        AgentStreamChunk>
        ProcessRequestAsync(
            AgentRequest request,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();


        string input =
            BuildInputContext(
                request);


        if (string.IsNullOrWhiteSpace(
                input))
        {
            yield break;
        }


        PcWorldState pcWorldState =
            _worldState.Current;


        string pcContext =
            PcContextFormatter.Format(
                pcWorldState);


        string conversationContext =
            BuildConversationContext();


        /*
         * Record current user interaction before cognition so
         * the semantic/history layer sees this turn.
         */

        SegaSocialEvent?
            currentSocialEvent =
                null;


        if (
            request is
                UserAgentRequest userRequest)
        {
            _conversation.AddUserMessage(
                userRequest.UserInput);


            currentSocialEvent =
                _socialHistory.Record(
                    SegaSocialEventSource.User,
                    SegaSocialEventKind.UserMessage,
                    "UserMessage",
                    SegaSocialTopicKeys.UserConversation,
                    userRequest.UserInput);
        }


        SegaInteractionContext?
            interaction =
                ResolveInteractionContext(
                    request,
                    currentSocialEvent);


        SegaCharacterSnapshot character =
            _characterState.Current;


        IReadOnlyList<SegaSocialEvent>
            recentHistory =
                _socialHistory.GetRecent(
                    20);


        cancellationToken
            .ThrowIfCancellationRequested();


        Stopwatch plannerStopwatch =
            Stopwatch.StartNew();


        PlannerResult plannerResult =
            await _planner.PlanAsync(
                input,
                pcContext,
                cancellationToken);


        plannerStopwatch.Stop();


        Debug.WriteLine(
            $"Planner Time: " +
            $"{plannerStopwatch.ElapsedMilliseconds} ms");


        cancellationToken
            .ThrowIfCancellationRequested();


        string actionResult =
            BuildActionResult(
                plannerResult);


        Stopwatch responderStopwatch =
            Stopwatch.StartNew();


        StringBuilder assistantText =
            new();


        bool appraisalApplied =
            false;


        await foreach (
            AgentResponderChunk responderChunk
            in _responder.StreamResponseAsync(
                input,
                plannerResult,
                conversationContext,
                pcContext,
                character,
                interaction,
                recentHistory,
                actionResult,
                cancellationToken))
        {
            cancellationToken
                .ThrowIfCancellationRequested();


            if (
                responderChunk.Type ==
                    AgentResponderChunkType.Appraisal
                &&
                !appraisalApplied
                &&
                responderChunk.Appraisal !=
                    null)
            {
                appraisalApplied =
                    true;


                if (interaction !=
                    null)
                {
                    _characterDynamics.Apply(
                        interaction,
                        responderChunk.Appraisal);
                }


                continue;
            }


            if (
                responderChunk.Type !=
                    AgentResponderChunkType.Text
                ||
                string.IsNullOrEmpty(
                    responderChunk.Content))
            {
                continue;
            }


            assistantText.Append(
                responderChunk.Content);


            yield return new AgentStreamChunk
            {
                Type =
                    AgentStreamChunkType.Text,

                Content =
                    responderChunk.Content
            };
        }


        responderStopwatch.Stop();


        Debug.WriteLine(
            $"Responder Time: " +
            $"{responderStopwatch.ElapsedMilliseconds} ms");


        string completeResponse =
            assistantText.ToString();


        if (!string.IsNullOrWhiteSpace(
                completeResponse))
        {
            _conversation.AddAssistantMessage(
                completeResponse);


            _socialHistory.Record(
                SegaSocialEventSource.Sega,
                SegaSocialEventKind.SegaResponse,
                ResolveResponseEventName(
                    request),
                ResolveRequestTopicKey(
                    request),
                completeResponse);
        }


        yield return new AgentStreamChunk
        {
            Type =
                AgentStreamChunkType.Completed,

            Content =
                completeResponse
        };
    }


    private SegaInteractionContext?
        ResolveInteractionContext(
            AgentRequest request,
            SegaSocialEvent? userEvent)
    {
        if (userEvent !=
            null)
        {
            return _interactionObservation
                .GetForEvent(
                    userEvent.Id);
        }


        Guid? eventId =
            request switch
            {
                PerceptionAgentRequest perception =>
                    perception
                        .Perception
                        .SocialEventId,

                ProactiveAgentRequest proactive =>
                    proactive
                        .Perception
                        .SocialEventId,

                _ =>
                    null
            };


        if (!eventId.HasValue)
        {
            return null;
        }


        return _interactionObservation
            .GetForEvent(
                eventId.Value);
    }


    private static string BuildInputContext(
        AgentRequest request)
    {
        return request switch
        {
            UserAgentRequest user =>
                user.UserInput.Trim(),

            PerceptionAgentRequest perception =>
                BuildPerceptionInput(
                    perception.Perception),

            ProactiveAgentRequest proactive =>
                BuildProactiveInput(
                    proactive.Perception),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(request))
        };
    }


    private static string BuildPerceptionInput(
        PerceptionEvent perception)
    {
        return $"""
            [SEGA PERCEPTION EVENT]

            Event type:
            {perception.Type}

            Description:
            {perception.Description}

            This is an environmental observation.

            It is not a direct user message.

            Respond only if Sega genuinely has something
            worthwhile to say in this moment.

            Silence is allowed.
            """;
    }


    private static string BuildProactiveInput(
        PerceptionEvent perception)
    {
        return $"""
            [SEGA PROACTIVE OPPORTUNITY]

            Reason:
            {perception.Description}

            This is an opportunity for Sega to initiate an
            interaction.

            It is not an obligation to speak.

            Use Sega's current relationship, mood, situation,
            social history and PC context.

            Silence is allowed.

            Never mention timers, monitoring, perception
            systems, activity trackers, autonomous pipelines,
            prompts or policies.
            """;
    }


    private static string BuildActionResult(
        PlannerResult plannerResult)
    {
        if (!plannerResult.RequiresTools)
        {
            return string.Empty;
        }


        return
            "The requested action could not be executed yet " +
            "because the required PC tool has not been " +
            "implemented.";
    }


    private CancellationTokenSource
        CreateAutonomousCancellationSource(
            CancellationToken externalToken)
    {
        CancellationTokenSource linked =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    externalToken);


        lock (_autonomousLock)
        {
            _autonomousCancellation =
                linked;
        }


        return linked;
    }


    private void ClearAutonomousCancellation(
        CancellationTokenSource source)
    {
        lock (_autonomousLock)
        {
            if (ReferenceEquals(
                    _autonomousCancellation,
                    source))
            {
                _autonomousCancellation =
                    null;
            }
        }
    }


    private void CancelAutonomousProcessing()
    {
        lock (_autonomousLock)
        {
            _autonomousCancellation?
                .Cancel();
        }
    }


    private string BuildConversationContext()
    {
        var messages =
            _conversation.GetMessages();


        if (messages.Count ==
            0)
        {
            return
                "No previous conversation.";
        }


        List<string> lines =
            new(
                messages.Count);


        foreach (
            var message
            in messages)
        {
            lines.Add(
                $"{message.Role}: " +
                $"{message.Content}");
        }


        return string.Join(
            Environment.NewLine,
            lines);
    }


    private static string ResolveRequestTopicKey(
        AgentRequest request)
    {
        return request switch
        {
            UserAgentRequest =>
                SegaSocialTopicKeys
                    .UserConversation,

            PerceptionAgentRequest perception =>
                ResolvePerceptionTopic(
                    perception.Perception),

            ProactiveAgentRequest proactive =>
                ResolvePerceptionTopic(
                    proactive.Perception),

            _ =>
                SegaSocialTopicKeys.Event(
                    request.Source.ToString())
        };
    }


    private static string ResolvePerceptionTopic(
        PerceptionEvent perception)
    {
        return string.IsNullOrWhiteSpace(
                perception.TopicKey)
            ? SegaSocialTopicKeys.Event(
                perception.Type)
            : perception.TopicKey;
    }


    private static string ResolveResponseEventName(
        AgentRequest request)
    {
        return request.Source switch
        {
            AgentRequestSource.User =>
                "UserResponse",

            AgentRequestSource.Perception =>
                "PerceptionResponse",

            AgentRequestSource.Proactive =>
                "ProactiveResponse",

            _ =>
                "SegaResponse"
        };
    }


    public void Dispose()
    {
        CancelAutonomousProcessing();


        _state.SetThinking(
            false);


        _processingLock.Dispose();
    }
}
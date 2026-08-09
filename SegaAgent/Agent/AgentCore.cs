/*
 * filename: AgentCore.cs
 */

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using SegaAgent.AI.Planner;
using SegaAgent.AI.Responder;
using SegaAgent.Conversation;
using SegaAgent.PC.Awareness;
using SegaAgent.Perception;

namespace SegaAgent.Agent;

public sealed class AgentCore : IDisposable
{
    // =========================================================
    // DEPENDENCIES
    // =========================================================

    private readonly AgentPlanner _planner;

    private readonly AgentResponder _responder;

    private readonly ConversationManager _conversation;

    private readonly PcAwarenessService _pcAwareness;

    private readonly AgentActivityTracker _activity;


    // =========================================================
    // PROCESSING LOCK
    //
    // Only one request may actively use the AI pipeline.
    //
    // User requests have priority.
    //
    // Autonomous requests do NOT wait.
    // If the agent is busy, they are skipped.
    // =========================================================

    private readonly SemaphoreSlim _processingLock =
        new(1, 1);


    // =========================================================
    // AUTONOMOUS CANCELLATION
    //
    // If the user starts talking while an environmental
    // response is being generated, cancel that response.
    // =========================================================

    private readonly object _autonomousLock =
        new();

    private CancellationTokenSource? _autonomousCancellation;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public AgentCore(
        AgentPlanner planner,
        AgentResponder responder,
        ConversationManager conversation,
        PcAwarenessService pcAwareness,
        AgentActivityTracker activity)
    {
        _planner = planner
            ?? throw new ArgumentNullException(nameof(planner));

        _responder = responder
            ?? throw new ArgumentNullException(nameof(responder));

        _conversation = conversation
            ?? throw new ArgumentNullException(nameof(conversation));

        _pcAwareness = pcAwareness
            ?? throw new ArgumentNullException(nameof(pcAwareness));

        _activity = activity
            ?? throw new ArgumentNullException(nameof(activity));
    }


    // =========================================================
    // NORMAL USER PROCESS
    // =========================================================

    public async Task<string> ProcessAsync(
        string userInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return string.Empty;
        }

        _activity.RecordUserInteraction();

        // User always wins.
        CancelAutonomousProcessing();

        await _processingLock.WaitAsync(
            cancellationToken);

        _activity.BeginProcessing();

        try
        {
            var result =
                new StringBuilder();

            await foreach (
                var chunk
                in ProcessRequestAsync(
                    new UserAgentRequest(userInput),
                    cancellationToken))
            {
                if (chunk.Type ==
                    AgentStreamChunkType.Text)
                {
                    result.Append(chunk.Content);
                }
            }

            return result.ToString();
        }
        finally
        {
            _activity.EndProcessing();

            _processingLock.Release();
        }
    }


    // =========================================================
    // NORMAL USER STREAM
    // =========================================================

    public async IAsyncEnumerable<AgentStreamChunk>
        ProcessStreamAsync(
            string userInput,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            yield break;
        }

        _activity.RecordUserInteraction();

        // User always wins.
        CancelAutonomousProcessing();

        await _processingLock.WaitAsync(
            cancellationToken);

        _activity.BeginProcessing();

        try
        {
            await foreach (
                var chunk
                in ProcessRequestAsync(
                    new UserAgentRequest(userInput),
                    cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            _activity.EndProcessing();

            _processingLock.Release();
        }
    }


    // =========================================================
    // PERCEPTION STREAM
    //
    // IMPORTANT:
    //
    // This is NOT a second AI pipeline.
    //
    // Once accepted, it goes through exactly the same
    // ProcessRequestAsync() pipeline as a user request.
    //
    // There is NO AutonomousOutput event here.
    // =========================================================

    public async IAsyncEnumerable<AgentStreamChunk>
        ProcessPerceptionAsync(
            PerceptionEvent perception,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        if (perception == null)
        {
            yield break;
        }

        // Never wait behind a user request.
        if (!_processingLock.Wait(0))
        {
            yield break;
        }

        _activity.BeginProcessing();

        CancellationTokenSource?
            autonomousCancellation = null;

        try
        {
            autonomousCancellation =
                CreateAutonomousCancellationSource(
                    cancellationToken);

            await foreach (
                var chunk
                in ProcessRequestAsync(
                    new PerceptionAgentRequest(
                        perception),
                    autonomousCancellation.Token))
            {
                yield return chunk;
            }
        }
        finally
        {
            if (autonomousCancellation != null)
            {
                ClearAutonomousCancellation(
                    autonomousCancellation);

                autonomousCancellation.Dispose();
            }

            _activity.RecordAutonomousActivity();

            _activity.EndProcessing();

            _processingLock.Release();
        }
    }


    // =========================================================
    // PROACTIVE STREAM
    //
    // Same pipeline.
    // =========================================================

    public async IAsyncEnumerable<AgentStreamChunk>
        ProcessProactiveAsync(
            PerceptionEvent perception,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        if (perception == null)
        {
            yield break;
        }

        // Never wait behind a user request.
        if (!_processingLock.Wait(0))
        {
            yield break;
        }

        _activity.BeginProcessing();

        CancellationTokenSource?
            autonomousCancellation = null;

        try
        {
            autonomousCancellation =
                CreateAutonomousCancellationSource(
                    cancellationToken);

            await foreach (
                var chunk
                in ProcessRequestAsync(
                    new ProactiveAgentRequest(
                        perception),
                    autonomousCancellation.Token))
            {
                yield return chunk;
            }
        }
        finally
        {
            if (autonomousCancellation != null)
            {
                ClearAutonomousCancellation(
                    autonomousCancellation);

                autonomousCancellation.Dispose();
            }

            _activity.RecordAutonomousActivity();

            _activity.EndProcessing();

            _processingLock.Release();
        }
    }


    // =========================================================
    // COMMON REQUEST PIPELINE
    //
    // ALL requests end up here.
    //
    // USER
    // PERCEPTION
    // PROACTIVE
    //
    //      ↓
    // Context
    //      ↓
    // Planner
    //      ↓
    // Action
    //      ↓
    // Responder
    //      ↓
    // Stream
    // =========================================================

    private async IAsyncEnumerable<AgentStreamChunk>
        ProcessRequestAsync(
            AgentRequest request,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        // =====================================================
        // 1. BUILD INPUT
        // =====================================================

        var input =
            BuildInputContext(request);

        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }


        // =====================================================
        // 2. PC STATE
        // =====================================================

        var pcState =
            _pcAwareness.Read();

        var pcContext =
            PcContextFormatter.Format(
                pcState);

        cancellationToken.ThrowIfCancellationRequested();


        // =====================================================
        // 3. CONVERSATION CONTEXT
        // =====================================================

        var conversationContext =
            BuildConversationContext();


        // =====================================================
        // 4. STORE USER MESSAGE
        //
        // Environmental events are NOT fake user messages.
        // =====================================================

        if (request is UserAgentRequest userRequest)
        {
            _conversation.AddUserMessage(
                userRequest.UserInput);
        }


        // =====================================================
        // 5. PLANNER
        // =====================================================

        var plannerStopwatch =
            Stopwatch.StartNew();

        var plannerResult =
            await _planner.PlanAsync(
                input,
                pcContext,
                cancellationToken);

        plannerStopwatch.Stop();

        Debug.WriteLine(
            $"Planner Time: " +
            $"{plannerStopwatch.ElapsedMilliseconds} ms");

        cancellationToken.ThrowIfCancellationRequested();


        // =====================================================
        // 6. ACTION LAYER
        // =====================================================

        var actionResult =
            BuildActionResult(
                plannerResult);


        // =====================================================
        // 7. RESPONDER
        // =====================================================

        var responderStopwatch =
            Stopwatch.StartNew();

        var assistantText =
            new StringBuilder();

        await foreach (
            var chunk
            in _responder.StreamResponseAsync(
                input,
                plannerResult,
                conversationContext,
                pcContext,
                actionResult,
                cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            assistantText.Append(chunk);

            yield return new AgentStreamChunk
            {
                Type =
                    AgentStreamChunkType.Text,

                Content =
                    chunk
            };
        }

        responderStopwatch.Stop();

        Debug.WriteLine(
            $"Responder Time: " +
            $"{responderStopwatch.ElapsedMilliseconds} ms");


        // =====================================================
        // 8. STORE COMPLETE ASSISTANT RESPONSE
        // =====================================================

        var completeResponse =
            assistantText.ToString();

        if (!string.IsNullOrWhiteSpace(
                completeResponse))
        {
            _conversation.AddAssistantMessage(
                completeResponse);
        }


        // =====================================================
        // 9. COMPLETED
        // =====================================================

        yield return new AgentStreamChunk
        {
            Type =
                AgentStreamChunkType.Completed,

            Content =
                completeResponse
        };
    }


    // =========================================================
    // INPUT CONTEXT
    // =========================================================

    private static string BuildInputContext(
        AgentRequest request)
    {
        return request switch
        {
            UserAgentRequest user =>
                BuildUserInput(
                    user.UserInput),

            PerceptionAgentRequest perception =>
                BuildPerceptionInput(
                    perception.Perception),

            ProactiveAgentRequest proactive =>
                BuildProactiveInput(
                    proactive.Perception),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request,
                    "Unknown agent request type.")
        };
    }


    // =========================================================
    // USER
    // =========================================================

    private static string BuildUserInput(
        string userInput)
    {
        return userInput.Trim();
    }


    // =========================================================
    // PERCEPTION
    // =========================================================

    private static string BuildPerceptionInput(
        PerceptionEvent perception)
    {
        return $"""
            [SEGA PERCEPTION EVENT]

            Event type:
            {perception.Type}

            Description:
            {perception.Description}

            This event was detected from the user's PC
            environment.

            Treat this as environmental context, not as a
            direct user message.

            Decide whether this event is worth mentioning
            naturally to the user.

            If there is nothing meaningful to say, keep the
            response brief.
            """;
    }


    // =========================================================
    // PROACTIVE
    // =========================================================

    private static string BuildProactiveInput(
        PerceptionEvent perception)
    {
        return $"""
            [SEGA PROACTIVE COMPANION CHECK]

            Reason:
            {perception.Description}

            This is an autonomous companion interaction.

            The user has not interacted with Sega recently.

            Speak naturally and conversationally.

            Do not mention:

            - internal timers
            - perception systems
            - activity trackers
            - autonomous pipelines
            - policies
            - internal agent architecture

            Do not force a conversation.

            If there is nothing meaningful to say, keep the
            response short and natural.
            """;
    }


    // =========================================================
    // ACTION RESULT
    // =========================================================

    private static string BuildActionResult(
        PlannerResult plannerResult)
    {
        if (!plannerResult.RequiresTools)
        {
            return string.Empty;
        }

        return
            "The requested action could not be executed yet " +
            "because the required tool is not implemented.";
    }


    // =========================================================
    // AUTONOMOUS CANCELLATION
    // =========================================================

    private CancellationTokenSource
        CreateAutonomousCancellationSource(
            CancellationToken externalToken)
    {
        var linked =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    externalToken);

        lock (_autonomousLock)
        {
            _autonomousCancellation = linked;
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
                _autonomousCancellation = null;
            }
        }
    }


    private void CancelAutonomousProcessing()
    {
        lock (_autonomousLock)
        {
            _autonomousCancellation?.Cancel();
        }
    }


    // =========================================================
    // CONVERSATION
    // =========================================================

    private string BuildConversationContext()
    {
        var messages =
            _conversation.GetMessages();

        if (messages.Count == 0)
        {
            return "No previous conversation.";
        }

        var lines =
            new List<string>(
                messages.Count);

        foreach (var message in messages)
        {
            lines.Add(
                $"{message.Role}: {message.Content}");
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }


    // =========================================================
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        CancelAutonomousProcessing();

        _processingLock.Dispose();
    }
}
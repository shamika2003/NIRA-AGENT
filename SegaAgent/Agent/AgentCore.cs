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

public sealed class AgentCore
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
    // AGENT PROCESSING LOCK
    //
    // Only one AI request should actively run at a time.
    //
    // User requests have priority.
    //
    // Autonomous requests use TryWait() and therefore do not
    // sit in a queue waiting behind a user conversation.
    // =========================================================

    private readonly SemaphoreSlim _processingLock =
        new(1, 1);


    // =========================================================
    // AUTONOMOUS CANCELLATION
    //
    // If Sega is making a proactive/perception response and
    // the user suddenly talks to Sega, the autonomous request
    // should stop.
    // =========================================================

    private readonly object _autonomousLock =
        new();

    private CancellationTokenSource? _autonomousCancellation;


    // =========================================================
    // AUTONOMOUS OUTPUT
    //
    // Perception / proactive services can run in the background
    // without knowing anything about the WPF UI.
    //
    // MainWindowViewModel can subscribe to this event.
    // =========================================================

    public event Action<AgentStreamChunk>? AutonomousOutput;


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
        _planner = planner;

        _responder = responder;

        _conversation = conversation;

        _pcAwareness = pcAwareness;

        _activity = activity;
    }


    // =========================================================
    // NORMAL NON-STREAMING PROCESS
    //
    // Kept for compatibility with older callers.
    //
    // New UI code should use ProcessStreamAsync().
    // =========================================================

    public async Task<string> ProcessAsync(
        string userInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return string.Empty;
        }


        // =====================================================
        // USER ACTIVITY
        // =====================================================

        _activity.RecordUserInteraction();


        // =====================================================
        // USER ALWAYS HAS PRIORITY
        //
        // If Sega is currently making an autonomous response,
        // stop it.
        // =====================================================

        CancelAutonomousProcessing();


        await _processingLock.WaitAsync(
            cancellationToken);


        _activity.BeginProcessing();


        try
        {
            return await ProcessUserNonStreamingAsync(
                userInput,
                cancellationToken);
        }
        finally
        {
            _activity.EndProcessing();

            _processingLock.Release();
        }
    }


    // =========================================================
    // USER STREAMING PROCESS
    //
    // Main UI pipeline.
    //
    // User
    //   ↓
    // Build request
    //   ↓
    // PC context
    //   ↓
    // Conversation context
    //   ↓
    // Planner
    //   ↓
    // Responder
    //   ↓
    // Streaming output
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


        // =====================================================
        // USER ACTIVITY
        //
        // This must happen immediately.
        //
        // Even if the planner takes several seconds, the
        // perception system now knows the user just interacted
        // with Sega.
        // =====================================================

        _activity.RecordUserInteraction();


        // =====================================================
        // USER HAS PRIORITY
        //
        // Cancel autonomous perception/proactive processing.
        // =====================================================

        CancelAutonomousProcessing();


        // =====================================================
        // WAIT FOR ANY EXISTING AGENT REQUEST
        // =====================================================

        await _processingLock.WaitAsync(
            cancellationToken);


        _activity.BeginProcessing();


        try
        {
            await foreach (
                var chunk
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
            _activity.EndProcessing();

            _processingLock.Release();
        }
    }


    // =========================================================
    // PERCEPTION PROCESS
    // =========================================================

    public async IAsyncEnumerable<AgentStreamChunk>
        ProcessPerceptionAsync(
            PerceptionEvent perception,
            [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (perception == null)
            yield break;


        // Autonomous perception must never wait behind
        // an active user request.
        if (!_processingLock.Wait(0))
            yield break;


        _activity.BeginProcessing();


        CancellationTokenSource?
            autonomousCancellation = null;


        try
        {
            autonomousCancellation =
                CreateAutonomousCancellationSource(
                    cancellationToken);
        }
        catch
        {
            _activity.EndProcessing();

            _processingLock.Release();

            throw;
        }


        // ---------------------------------------------------------
        // IMPORTANT:
        //
        // No catch block surrounds yield.
        // The async stream itself is allowed to propagate
        // OperationCanceledException.
        // ---------------------------------------------------------

        try
        {
            await foreach (
                var chunk
                in ProcessRequestAsync(
                    new PerceptionAgentRequest(
                        perception),
                    autonomousCancellation.Token))
            {
                PublishAutonomousOutput(chunk);

                yield return chunk;
            }
        }
        finally
        {
            ClearAutonomousCancellation(
                autonomousCancellation);

            autonomousCancellation.Dispose();

            _activity.RecordAutonomousActivity();

            _activity.EndProcessing();

            _processingLock.Release();
        }
    }

    // =========================================================
    // PROACTIVE PROCESS
    // =========================================================

    public async IAsyncEnumerable<AgentStreamChunk>
        ProcessProactiveAsync(
            PerceptionEvent perception,
            [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (perception == null)
            yield break;


        // Autonomous requests never wait behind another
        // active agent request.
        if (!_processingLock.Wait(0))
            yield break;


        _activity.BeginProcessing();


        CancellationTokenSource?
            autonomousCancellation = null;


        try
        {
            autonomousCancellation =
                CreateAutonomousCancellationSource(
                    cancellationToken);
        }
        catch
        {
            _activity.EndProcessing();

            _processingLock.Release();

            throw;
        }


        try
        {
            await foreach (
                var chunk
                in ProcessRequestAsync(
                    new ProactiveAgentRequest(
                        perception),
                    autonomousCancellation.Token))
            {
                PublishAutonomousOutput(chunk);

                yield return chunk;
            }
        }
        finally
        {
            ClearAutonomousCancellation(
                autonomousCancellation);

            autonomousCancellation.Dispose();

            _activity.RecordAutonomousActivity();

            _activity.EndProcessing();

            _processingLock.Release();
        }
    }

    // =========================================================
    // INTERNAL REQUEST PIPELINE
    //
    // ALL request types eventually reach this method.
    //
    // User
    // Perception
    // Proactive
    //
    // all use the same:
    //
    // Context
    //   ↓
    // Planner
    //   ↓
    // Action layer
    //   ↓
    // Responder
    //   ↓
    // Conversation storage
    // =========================================================

    private async IAsyncEnumerable<AgentStreamChunk>
        ProcessRequestAsync(
            AgentRequest request,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();


        // =====================================================
        // 1. BUILD REQUEST INPUT
        // =====================================================

        var input =
            BuildInputContext(
                request);


        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }


        // =====================================================
        // 2. READ CURRENT PC STATE
        // =====================================================

        var pcState =
            _pcAwareness.Read();


        var pcContext =
            PcContextFormatter.Format(
                pcState);


        cancellationToken.ThrowIfCancellationRequested();


        // =====================================================
        // 3. BUILD CONVERSATION CONTEXT
        //
        // IMPORTANT:
        //
        // This happens BEFORE adding the current user message.
        //
        // This preserves the previous behavior of AgentCore.
        // =====================================================

        var conversationContext =
            BuildConversationContext();


        // =====================================================
        // 4. STORE USER MESSAGE ONLY FOR USER REQUESTS
        //
        // Perception and proactive events are NOT fake user
        // messages.
        // =====================================================

        if (request is UserAgentRequest userRequest)
        {
            _conversation.AddUserMessage(
                userRequest.UserInput);
        }


        // =====================================================
        // 5. PLAN
        // =====================================================

        var stopwatchPlanner =
            Stopwatch.StartNew();


        var plannerResult =
            await _planner.PlanAsync(
                input,
                pcContext,
                cancellationToken);


        stopwatchPlanner.Stop();


        Debug.WriteLine(
            $"Planner Time: " +
            $"{stopwatchPlanner.ElapsedMilliseconds} ms");


        cancellationToken.ThrowIfCancellationRequested();


        // =====================================================
        // 6. ACTION / TOOL LAYER
        // =====================================================

        var actionResult =
            BuildActionResult(
                plannerResult);


        // =====================================================
        // 7. STREAM RESPONSE
        // =====================================================

        var stopwatchResponder =
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


            assistantText.Append(
                chunk);


            yield return new AgentStreamChunk
            {
                Type =
                    AgentStreamChunkType.Text,

                Content =
                    chunk
            };
        }


        stopwatchResponder.Stop();


        Debug.WriteLine(
            $"Responder Time: " +
            $"{stopwatchResponder.ElapsedMilliseconds} ms");


        cancellationToken.ThrowIfCancellationRequested();


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
        // 9. COMPLETION
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
    // NON-STREAMING USER PROCESS
    //
    // Used only by the backwards-compatible ProcessAsync().
    // =========================================================

    private async Task<string>
        ProcessUserNonStreamingAsync(
            string userInput,
            CancellationToken cancellationToken)
    {
        var request =
            new UserAgentRequest(
                userInput);


        var input =
            BuildInputContext(
                request);


        var pcState =
            _pcAwareness.Read();


        var pcContext =
            PcContextFormatter.Format(
                pcState);


        var conversationContext =
            BuildConversationContext();


        _conversation.AddUserMessage(
            userInput);


        cancellationToken.ThrowIfCancellationRequested();


        var plannerResult =
            await _planner.PlanAsync(
                input,
                pcContext,
                cancellationToken);


        cancellationToken.ThrowIfCancellationRequested();


        var actionResult =
            BuildActionResult(
                plannerResult);


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

            assistantText.Append(
                chunk);
        }


        var response =
            assistantText.ToString();


        if (!string.IsNullOrWhiteSpace(
                response))
        {
            _conversation.AddAssistantMessage(
                response);
        }


        return response;
    }


    // =========================================================
    // INPUT CONTEXT
    //
    // THIS IS THE CENTRAL REQUEST TRANSLATOR.
    //
    // Instead of:
    //
    // userInput = ""
    // perception = null
    //
    // we explicitly know what kind of request we received.
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
    // USER INPUT
    // =========================================================

    private static string BuildUserInput(
        string userInput)
    {
        return userInput.Trim();
    }


    // =========================================================
    // PERCEPTION INPUT
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

            Decide whether the event is actually worth
            mentioning to the user.

            If it is not useful or natural to mention,
            respond briefly and naturally.
            """;
    }


    // =========================================================
    // PROACTIVE INPUT
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
            CancellationTokenSource.CreateLinkedTokenSource(
                externalToken);


        lock (_autonomousLock)
        {
            _autonomousCancellation =
                linked;
        }


        return linked;
    }


    private void ClearAutonomousCancellation(
        CancellationTokenSource? source)
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
    // AUTONOMOUS OUTPUT
    // =========================================================

    private void PublishAutonomousOutput(
        AgentStreamChunk chunk)
    {
        try
        {
            AutonomousOutput?.Invoke(
                chunk);
        }
        catch (Exception ex)
        {
            // UI subscribers should never be allowed to break
            // the AgentCore processing pipeline.

            Debug.WriteLine(
                $"Autonomous output subscriber error: {ex}");
        }
    }


    // =========================================================
    // CONVERSATION CONTEXT
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
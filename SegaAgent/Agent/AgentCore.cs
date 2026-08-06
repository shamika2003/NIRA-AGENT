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

namespace SegaAgent.Agent;

public class AgentCore
{
    private readonly AgentPlanner _planner;

    private readonly AgentResponder _responder;

    private readonly ConversationManager _conversation;

    private readonly PcAwarenessService _pcAwareness;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public AgentCore(
        AgentPlanner planner,
        AgentResponder responder,
        ConversationManager conversation,
        PcAwarenessService pcAwareness)
    {
        _planner = planner;

        _responder = responder;

        _conversation = conversation;

        _pcAwareness = pcAwareness;
    }

    // =========================================================
    // STREAMING PROCESS
    // =========================================================

    public async IAsyncEnumerable<AgentStreamChunk> ProcessStreamAsync(
        string userInput,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        // =====================================================
        // 1. READ CURRENT PC STATE
        // =====================================================

        var pcState =
            _pcAwareness.Read();


        var pcContext =
            PcContextFormatter.Format(
                pcState
            );


        // =====================================================
        // 2. BUILD CONTEXT
        // =====================================================

        var conversationContext =
            BuildConversationContext();


        // =====================================================
        // 3. STORE USER MESSAGE
        // =====================================================

        _conversation.AddUserMessage(
            userInput
        );


        // =====================================================
        // 4. PLAN
        // =====================================================

        var stopwatchPlanner = Stopwatch.StartNew();

        var plannerResult =
            await _planner.PlanAsync(
                userInput,
                pcContext,
                cancellationToken
            );

        stopwatchPlanner.Stop();


        Debug.WriteLine(
    $"Planner Time: {stopwatchPlanner.ElapsedMilliseconds} ms"
);

        cancellationToken.ThrowIfCancellationRequested();


        // =====================================================
        // 5. ACTION / TOOL LAYER
        // =====================================================

        var actionResult = "";


        if (plannerResult.RequiresTools)
        {
            actionResult =
                "The requested action could not be executed yet because the required tool is not implemented.";
        }


        // =====================================================
        // 6. STREAM FINAL RESPONSE
        // =====================================================

        var stopwatchresponderStart = Stopwatch.StartNew();

        var assistantText =
            new StringBuilder();


        await foreach (
            var chunk
            in _responder.StreamResponseAsync(
                userInput,
                plannerResult,
                conversationContext,
                pcContext,
                actionResult,
                cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();


            assistantText.Append(
                chunk
            );


            yield return new AgentStreamChunk
            {
                Type =
                    AgentStreamChunkType.Text,

                Content =
                    chunk
            };
        }

        stopwatchresponderStart.Stop();


        Debug.WriteLine(
    $"Responder Time: {stopwatchresponderStart.ElapsedMilliseconds} ms"
);



        // =====================================================
        // 7. STORE COMPLETE RESPONSE
        // =====================================================

        var completeResponse =
            assistantText.ToString();


        if (!string.IsNullOrWhiteSpace(
                completeResponse))
        {
            _conversation.AddAssistantMessage(
                completeResponse
            );
        }


        // =====================================================
        // 8. SIGNAL COMPLETION
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
            new List<string>();


        foreach (var message in messages)
        {
            lines.Add(
                $"{message.Role}: {message.Content}"
            );
        }


        return string.Join(
            Environment.NewLine,
            lines
        );
    }
}
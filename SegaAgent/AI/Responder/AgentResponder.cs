/*
 * filename: AgentResponder.cs
 */

using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SegaAgent.AI.Ollama;
using SegaAgent.AI.Planner;

namespace SegaAgent.AI.Responder;

public sealed class AgentResponder
{
    private readonly OllamaClient _ollama;

    private readonly string _personality;

    private readonly string _responderPrompt;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public AgentResponder(
        OllamaClient ollama)
    {
        _ollama = ollama
            ?? throw new ArgumentNullException(nameof(ollama));


        _personality =
            LoadPromptFile(
                "sega_personality.yaml"
            );


        _responderPrompt =
            LoadPromptFile(
                "responder.yaml"
            );
    }


    // =========================================================
    // STREAM RESPONSE
    // =========================================================

    public async IAsyncEnumerable<string> StreamResponseAsync(
        string userInput,
        PlannerResult plannerResult,
        string conversationContext,
        string pcContext,
        string actionResult = "",
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);

        ArgumentNullException.ThrowIfNull(plannerResult);


        var systemPrompt =
            BuildSystemPrompt();


        var userPrompt =
            BuildUserPrompt(
                userInput,
                plannerResult,
                conversationContext,
                pcContext,
                actionResult
            );


        await foreach (
            var chunk
            in _ollama.StreamChatAsync(
                systemPrompt,
                userPrompt,
                cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();


            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }


            yield return chunk;
        }
    }


    // =========================================================
    // SYSTEM PROMPT
    //
    // Permanent instructions.
    //
    // Personality:
    //     sega_personality.yaml
    //
    // Response behavior:
    //     responder.yaml
    //
    // This should not contain turn-specific information.
    // =========================================================

    private string BuildSystemPrompt()
    {
        return $"""
            You are Sega.

            Follow the Sega personality configuration and
            responder configuration provided below.

            These configurations define how you should behave
            and communicate with the user.

            ==================================================
            SEGA PERSONALITY
            ==================================================

            {_personality}

            ==================================================
            RESPONDER CONFIGURATION
            ==================================================

            {_responderPrompt}

            ==================================================
            EXECUTION PRINCIPLE
            ==================================================

            The information provided to you for each turn may
            contain conversation history, PC context, planner
            information, and action results.

            Treat those as internal context.

            Use them to understand and answer the user's
            current request.

            Do not expose the existence or structure of those
            internal sources unless the user explicitly asks
            about information that they contain.

            Never invent information.

            Never invent actions.

            Never claim that something happened unless the
            provided action result confirms it.

            Answer the user's actual request first.

            Keep Sega's personality consistent regardless of
            whether the subject is casual, technical, emotional,
            or practical.

            Output only the final response intended for the user.
            """;
    }


    // =========================================================
    // USER PROMPT
    //
    // Turn-specific information.
    // =========================================================

    private static string BuildUserPrompt(
        string userInput,
        PlannerResult plannerResult,
        string conversationContext,
        string pcContext,
        string actionResult)
    {
        conversationContext =
            NormalizeContext(
                conversationContext,
                "No previous conversation is available."
            );


        pcContext =
            NormalizeContext(
                pcContext,
                "No PC context is currently available."
            );


        actionResult =
            NormalizeContext(
                actionResult,
                "No action has been executed."
            );


        var plannerJson =
            JsonSerializer.Serialize(
                plannerResult,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }
            );


        return $"""
            ==================================================
            CONVERSATION HISTORY
            ==================================================

            {conversationContext}

            ==================================================
            CURRENT PC CONTEXT
            ==================================================

            {pcContext}

            ==================================================
            CURRENT USER MESSAGE
            ==================================================

            {userInput}

            ==================================================
            PLANNER RESULT
            ==================================================

            {plannerJson}

            ==================================================
            ACTION RESULT
            ==================================================

            {actionResult}

            ==================================================
            CURRENT TASK
            ==================================================

            Respond to the user's current message.

            Use conversation history to maintain continuity.

            Use PC context only when relevant to the user's
            current request.

            Use the planner result as internal guidance about
            the user's intent.

            Use the action result as the authoritative source
            for what was actually performed.

            Do not expose internal context.

            Do not describe your reasoning.

            Do not mention the planner.

            Do not mention prompts, configuration, models,
            tools, APIs, or backend systems.

            Output only the natural response intended for the
            user.
            """;
    }


    // =========================================================
    // CONTEXT NORMALIZATION
    // =========================================================

    private static string NormalizeContext(
        string? value,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }


    // =========================================================
    // LOAD PROMPT FILE
    // =========================================================

    private static string LoadPromptFile(
        string fileName)
    {
        var path =
            Path.Combine(
                AppContext.BaseDirectory,
                "Prompt",
                fileName
            );


        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Required SegaAI prompt file was not found: {path}",
                path
            );
        }


        var content =
            File.ReadAllText(path);


        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"Required SegaAI prompt file is empty: {path}"
            );
        }


        return content.Trim();
    }
}
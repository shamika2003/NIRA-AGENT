/*
 * filename: AgentPlanner.cs
 */

using System.Text.Json;
using SegaAgent.AI.Ollama;

namespace SegaAgent.AI.Planner;

public class AgentPlanner
{
    private readonly OllamaClient _ollama;

    private const string PlannerModel =
        "gpt-oss:120b-cloud";

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public AgentPlanner(
        OllamaClient ollama)
    {
        _ollama = ollama;
    }


    // =========================================================
    // PLAN
    // =========================================================

    public async Task<PlannerResult> PlanAsync(
        string userInput,
        string pcContext,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = """
            You are the planning system for SegaAI,
            a Windows computer assistant.

            Your job is to analyze the user's request and decide
            what the computer assistant needs to do.

            You do NOT execute actions.

            You only create a plan.

            You may receive information about the current
            Windows PC state.

            PC state is observational context only.

            Do not assume that information exists if it was not
            provided.

            Available tools will be provided later.

            For now, return JSON only.

            The JSON must follow this structure:

            {
              "intent": "string",
              "requiresTools": false,
              "toolCalls": [],
              "responseStyle": "normal"
            }

            Rules:

            - If the user is simply asking a question that does
              not require computer interaction, requiresTools
              should be false.

            - If the user wants the computer to perform an action,
              requiresTools should be true.

            - Never invent tools.

            - Never execute anything.

            - Never put explanations outside the JSON.

            - Keep the plan precise.
            """;


        var userPrompt = $"""
            CURRENT PC CONTEXT:

            {pcContext}

            ==============================

            USER INPUT:

            {userInput}

            ==============================

            Create the plan for this request.
            """;


        var response =
            await _ollama.ChatAsync(
                PlannerModel,
                systemPrompt,
                userPrompt,
                cancellationToken
            );


        return ParseResponse(response);
    }


    // =========================================================
    // PARSE
    // =========================================================

    private static PlannerResult ParseResponse(
        string response)
    {
        var json =
            response.Trim();


        if (json.StartsWith("```"))
        {
            var firstNewLine =
                json.IndexOf('\n');

            if (firstNewLine >= 0)
            {
                json =
                    json[(firstNewLine + 1)..];
            }


            var closingFence =
                json.LastIndexOf("```");

            if (closingFence >= 0)
            {
                json =
                    json[..closingFence];
            }
        }


        json =
            json.Trim();


        var result =
            JsonSerializer.Deserialize<PlannerResult>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );


        if (result == null)
        {
            throw new InvalidOperationException(
                "Planner returned an empty result."
            );
        }


        return result;
    }
}
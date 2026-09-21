/*
 * filename: NIRAResponseRealizationService.cs
 */

using System.Diagnostics;
using System.Text;

using NIRAAgent.AI.Ollama;
using NIRAAgent.Character;
using NIRAAgent.Character.History;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;

namespace NIRAAgent.AI.Cognition;

// =============================================================
// NIRA RESPONSE REALIZATION
//
// Executive cognition and visible expression are different jobs.
//
// Main cognition decides what is true, what NIRA should do, and the
// semantic content of a possible reply. This service performs one
// bounded presentation-only pass AFTER authoritative character state
// has been updated and only when a user-visible reply will actually be
// emitted.
//
// It cannot request tools, create goals, mutate memory, or change any
// authoritative state. If realization fails validation, the original
// cognition draft is returned unchanged.
// =============================================================

public sealed class NIRAResponseRealizationService
{
    public const string ResponseModelEnvironmentVariable =
        "NIRA_OLLAMA_RESPONSE_MODEL";

    private const string DefaultResponseModel =
        "gpt-oss:120b-cloud";

    private const int MaximumDraftCharacters =
        6000;

    private const int MaximumRealizedCharacters =
        7000;

    private const int MaximumEventCharacters =
        3000;

    private const int MaximumConversationCharacters =
        7000;

    private readonly OllamaClient _ollama;

    private readonly NIRACharacterStateService _characterState;

    private readonly NIRAAttitudeService _attitude;

    private readonly NIRASocialHistoryService _socialHistory;

    private readonly string _personalityPrompt;

    private readonly string _realizationPrompt;

    public NIRAResponseRealizationService(
        OllamaClient ollama,
        NIRACharacterStateService characterState,
        NIRAAttitudeService attitude,
        NIRASocialHistoryService socialHistory)
    {
        _ollama =
            ollama
            ?? throw new ArgumentNullException(
                nameof(ollama));

        _characterState =
            characterState
            ?? throw new ArgumentNullException(
                nameof(characterState));

        _attitude =
            attitude
            ?? throw new ArgumentNullException(
                nameof(attitude));

        _socialHistory =
            socialHistory
            ?? throw new ArgumentNullException(
                nameof(socialHistory));

        _personalityPrompt =
            LoadPromptFile(
                "nira_personality.yaml");

        _realizationPrompt =
            LoadPromptFile(
                "response_realization.yaml");

        Debug.WriteLine(
            $"[ResponseRealization] READY | Model='{ResponseModel}'");
    }

    public string ResponseModel
    {
        get
        {
            string configured =
                Environment.GetEnvironmentVariable(
                    ResponseModelEnvironmentVariable)?.Trim()
                ?? string.Empty;

            return string.IsNullOrWhiteSpace(configured)
                ? DefaultResponseModel
                : configured;
        }
    }

    public async Task<string> RealizeAsync(
        NIRAResponseRealizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        string draft =
            NormalizeRequiredDraft(
                request.DraftReply);

        // Long/code-heavy material should normally have been marked
        // PreserveExact by cognition. Keep an absolute safety bound here
        // so this presentation stage never receives an unbounded payload.
        if (draft.Length > MaximumDraftCharacters)
        {
            Debug.WriteLine(
                $"[ResponseRealization] SKIPPED | Reason='Draft too large' | Characters={draft.Length}");

            return draft;
        }

        NIRACharacterSnapshot character =
            _characterState.Current;

        NIRAAttitudeState attitude =
            _attitude.Evaluate(
                character,
                request.Interaction);

        string characterContext =
            NIRACharacterContextFormatter.Format(
                character,
                attitude,
                request.Interaction,
                _socialHistory.GetRecent(10));

        string systemPrompt =
            BuildSystemPrompt();

        string userPrompt =
            BuildUserPrompt(
                request,
                draft,
                characterContext);

        try
        {
            Stopwatch stopwatch =
                Stopwatch.StartNew();

            string raw =
                await _ollama.ChatAsync(
                    ResponseModel,
                    systemPrompt,
                    userPrompt,
                    cancellationToken);

            stopwatch.Stop();

            string realized =
                NormalizeRealized(
                    raw);

            if (!ValidateRealization(
                    draft,
                    realized,
                    request.RequiredVerbatimFragments,
                    out string reason))
            {
                Debug.WriteLine(
                    $"[ResponseRealization] REJECTED | Time={stopwatch.ElapsedMilliseconds} ms | Reason='{TrimLog(reason)}'");

                return draft;
            }

            Debug.WriteLine(
                $"[ResponseRealization] REALIZED | Time={stopwatch.ElapsedMilliseconds} ms | " +
                $"DraftChars={draft.Length} | FinalChars={realized.Length}");

            return realized;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Expression failure must never destroy an otherwise valid
            // executive response. The semantic cognition draft remains the
            // truthful fallback.
            Debug.WriteLine(
                $"[ResponseRealization] FALLBACK | Type={ex.GetType().Name} | Message='{TrimLog(ex.Message)}'");

            return draft;
        }
    }

    private string BuildSystemPrompt()
    {
        return $"""
            You are the final expression stage inside NIRA's cognition runtime.

            You are NOT a planner, executive, fact checker, tool caller, scheduler,
            memory writer, goal manager, or autonomous agent. Do not decide what NIRA
            should do next. Do not add actions. Do not change factual claims. Do not
            claim that anything happened unless the supplied draft already claims it.

            Your single job is to realize the supplied semantic reply as the natural
            utterance NIRA would actually say now, using her CURRENT UPDATED character
            state and recent social continuity.

            The draft is authoritative for concrete semantic content. Preserve its
            dates, times, quantities, names, paths, URLs, success/failure status,
            uncertainty, authorization limitations and other factual commitments.
            You may change wording, rhythm, contractions, sentence order and social
            delivery only when that does not change meaning.

            Return ONLY the final user-facing utterance as plain text. No JSON. No
            analysis. No labels. No quotation marks around the whole reply.

            ==================================================
            NIRA IDENTITY / PERSONALITY
            ==================================================

            {_personalityPrompt}

            ==================================================
            RESPONSE REALIZATION RULES
            ==================================================

            {_realizationPrompt}
            """;
    }

    private static string BuildUserPrompt(
        NIRAResponseRealizationRequest request,
        string draft,
        string characterContext)
    {
        string required =
            request.RequiredVerbatimFragments.Count == 0
                ? "(none)"
                : string.Join(
                    Environment.NewLine,
                    request.RequiredVerbatimFragments.Select(
                        value => $"- {value}"));

        return $"""
            ==================================================
            CURRENT EVENT
            ==================================================
            Source: {NormalizeField(request.EventSource, 120)}
            Name: {NormalizeField(request.EventName, 180)}
            Topic: {NormalizeField(request.TopicKey, 240)}

            {Limit(request.EventContent, MaximumEventCharacters)}

            ==================================================
            CURRENT UPDATED NIRA CHARACTER STATE
            ==================================================

            {characterContext}

            ==================================================
            CURRENT AUTHORITATIVE CLOCK
            ==================================================

            {Limit(request.TemporalContext, 1500)}

            ==================================================
            RECENT CONVERSATION
            ==================================================

            {Limit(request.ConversationContext, MaximumConversationCharacters)}

            ==================================================
            EXECUTIVE COMMUNICATION PURPOSE
            ==================================================

            {NormalizeField(request.DecisionSummary, 1000)}

            ==================================================
            AUTHORITATIVE SEMANTIC REPLY DRAFT
            ==================================================

            {draft}

            ==================================================
            VERBATIM FRAGMENTS THAT MUST SURVIVE IF PRESENT
            ==================================================

            {required}

            Realize the final NIRA utterance now. Keep the meaning and concrete facts
            intact. Let the updated character state affect the delivery naturally;
            do not narrate the state itself.
            """;
    }

    private static bool ValidateRealization(
        string draft,
        string realized,
        IReadOnlyList<string> requiredFragments,
        out string reason)
    {
        if (string.IsNullOrWhiteSpace(realized))
        {
            reason =
                "The realization was empty.";
            return false;
        }

        if (realized.Length > MaximumRealizedCharacters)
        {
            reason =
                $"The realization exceeded {MaximumRealizedCharacters} characters.";
            return false;
        }

        // When a goal/branch mutation used NIRAReply as its evidence source,
        // its exact evidence quote already became authoritative provenance.
        // A style pass is allowed only if those quoted fragments remain exact.
        foreach (string fragment in requiredFragments)
        {
            if (string.IsNullOrWhiteSpace(fragment))
            {
                continue;
            }

            if (!realized.Contains(
                    fragment,
                    StringComparison.Ordinal))
            {
                reason =
                    $"Required NIRAReply evidence fragment was not preserved: '{TrimLog(fragment)}'.";
                return false;
            }
        }

        // A presentation pass should not explode a tiny conversational reply
        // into an essay. This is a broad safety bound, not a dialogue template.
        int proportionalLimit =
            Math.Max(
                1200,
                draft.Length * 4 + 600);

        if (realized.Length > proportionalLimit)
        {
            reason =
                "The realization expanded the draft far beyond a presentation-only rewrite.";
            return false;
        }

        reason =
            string.Empty;
        return true;
    }

    private static string NormalizeRequiredDraft(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "Response realization requires a non-empty semantic draft.");
        }

        return value.Trim();
    }

    private static string NormalizeRealized(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string clean =
            value.Trim();

        if (clean.StartsWith(
                "```",
                StringComparison.Ordinal)
            && clean.EndsWith(
                "```",
                StringComparison.Ordinal))
        {
            int firstBreak =
                clean.IndexOf('\n');

            if (firstBreak >= 0)
            {
                clean =
                    clean[(firstBreak + 1)..];
            }

            int closing =
                clean.LastIndexOf(
                    "```",
                    StringComparison.Ordinal);

            if (closing >= 0)
            {
                clean =
                    clean[..closing];
            }

            clean =
                clean.Trim();
        }

        return clean;
    }

    private static string NormalizeField(
        string? value,
        int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        return Limit(
            value.Trim(),
            maximumCharacters);
    }

    private static string Limit(
        string? value,
        int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        string clean =
            value.Trim();

        return clean.Length <= maximumCharacters
            ? clean
            : clean[..maximumCharacters] + "...";
    }

    private static string LoadPromptFile(
        string fileName)
    {
        string path =
            Path.Combine(
                AppContext.BaseDirectory,
                "Prompt",
                fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Required NIRA prompt file was not found: {path}",
                path);
        }

        string content =
            File.ReadAllText(path);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"Required NIRA prompt file is empty: {path}");
        }

        return content.Trim();
    }

    private static string TrimLog(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        const int maximumLength = 220;
        string clean = value.Trim();

        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }
}

public sealed record NIRAResponseRealizationRequest
{
    public string DraftReply { get; init; } =
        string.Empty;

    public string DecisionSummary { get; init; } =
        string.Empty;

    public string EventSource { get; init; } =
        string.Empty;

    public string EventName { get; init; } =
        string.Empty;

    public string TopicKey { get; init; } =
        string.Empty;

    public string EventContent { get; init; } =
        string.Empty;

    public string TemporalContext { get; init; } =
        string.Empty;

    public string ConversationContext { get; init; } =
        string.Empty;

    public NIRAInteractionContext? Interaction { get; init; }

    public IReadOnlyList<string> RequiredVerbatimFragments { get; init; } =
        Array.Empty<string>();
}



/*
 * filename: NIRAResponseRealizationService.cs
 */

using System.Diagnostics;
using System.Text;
using System.Text.Json;

using NIRAAgent.AI.Ollama;
using NIRAAgent.Character;
using NIRAAgent.Character.Appraisal;
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
// cognition reply/speech drafts are returned unchanged.
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

    public async Task<NIRAResponseRealizationResult> RealizeAsync(
        NIRAResponseRealizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        string draftReply =
            NormalizeRequiredDraft(
                request.DraftReply);

        string draftSpeech =
            NormalizeOptionalDraft(
                request.DraftSpeech);

        NIRAResponseRealizationResult fallback =
            new()
            {
                Reply =
                    draftReply,

                Speech =
                    draftSpeech
            };

        // Long/code-heavy material should normally have been marked
        // PreserveExact by cognition. Keep an absolute safety bound here
        // so this presentation stage never receives an unbounded payload.
        if (draftReply.Length > MaximumDraftCharacters ||
            draftSpeech.Length > MaximumDraftCharacters)
        {
            Debug.WriteLine(
                $"[ResponseRealization] SKIPPED | Reason='Draft too large' | " +
                $"ReplyChars={draftReply.Length} | SpeechChars={draftSpeech.Length}");

            return fallback;
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

        if (request.AppliedSocialAppraisal != null)
        {
            NIRAInteractionAppraisal applied =
                request.AppliedSocialAppraisal.Normalize();

            Debug.WriteLine(
                $"[ResponseRealization] SOCIAL | " +
                $"Hostility={applied.Meaning.Hostility:F2} | " +
                $"Dismissal={applied.Meaning.Dismissal:F2} | " +
                $"Repair={applied.Meaning.Repair:F2} | " +
                $"Playfulness={applied.Meaning.Playfulness:F2} | " +
                $"Respect={applied.Meaning.Respect:F2} | " +
                $"Confidence={applied.Confidence:F2}");
        }

        string systemPrompt =
            BuildSystemPrompt();

        string userPrompt =
            BuildUserPrompt(
                request,
                draftReply,
                draftSpeech,
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

            if (!TryNormalizeRealized(
                    raw,
                    requireSpeech: !string.IsNullOrWhiteSpace(draftSpeech),
                    out NIRAResponseRealizationResult realized,
                    out string parseReason))
            {
                Debug.WriteLine(
                    $"[ResponseRealization] REJECTED | Time={stopwatch.ElapsedMilliseconds} ms | " +
                    $"Reason='{TrimLog(parseReason)}'");

                return fallback;
            }

            if (!ValidateRealization(
                    draftReply,
                    realized.Reply,
                    request.RequiredVerbatimFragments,
                    out string replyReason))
            {
                Debug.WriteLine(
                    $"[ResponseRealization] REJECTED | Time={stopwatch.ElapsedMilliseconds} ms | " +
                    $"Channel=Reply | Reason='{TrimLog(replyReason)}'");

                return fallback;
            }

            if (!string.IsNullOrWhiteSpace(draftSpeech))
            {
                if (!ValidateRealization(
                        draftSpeech,
                        realized.Speech,
                        Array.Empty<string>(),
                        out string speechReason))
                {
                    Debug.WriteLine(
                        $"[ResponseRealization] REJECTED | Time={stopwatch.ElapsedMilliseconds} ms | " +
                        $"Channel=Speech | Reason='{TrimLog(speechReason)}'");

                    return fallback;
                }
            }
            else
            {
                // Empty speech has an intentional meaning in the Executive:
                // reuse the realized screen reply. Do not let this stage create
                // a second channel when cognition deliberately omitted one.
                realized =
                    realized with
                    {
                        Speech =
                            string.Empty
                    };
            }

            Debug.WriteLine(
                $"[ResponseRealization] REALIZED | Time={stopwatch.ElapsedMilliseconds} ms | " +
                $"DraftReplyChars={draftReply.Length} | FinalReplyChars={realized.Reply.Length} | " +
                $"DraftSpeechChars={draftSpeech.Length} | FinalSpeechChars={realized.Speech.Length}");

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
            // executive response. The semantic cognition drafts remain the
            // truthful fallback.
            Debug.WriteLine(
                $"[ResponseRealization] FALLBACK | Type={ex.GetType().Name} | Message='{TrimLog(ex.Message)}'");

            return fallback;
        }
    }


    private string BuildSystemPrompt()
    {
        return $$"""
            You are the final expression stage inside NIRA's cognition runtime.

            You are NOT a planner, executive, fact checker, tool caller, scheduler,
            memory writer, goal manager, or autonomous agent. Do not decide what NIRA
            should do next. Do not add actions. Do not change factual claims. Do not
            claim that anything happened unless the supplied draft already claims it.

            Your single job is to realize the supplied semantic reply as the natural
            utterance NIRA would actually say now, using her CURRENT UPDATED character
            state and recent social continuity.

            The draft is authoritative for concrete/task semantic content. Preserve
            dates, times, quantities, names, paths, URLs, success/failure status,
            uncertainty, authorization limitations, genuine responsibility acknowledgements
            and other factual commitments. For Natural replies, however, the draft's
            interpersonal wrapper is deliberately provisional because cognition wrote it
            BEFORE the authoritative character update was committed. You may remove or
            replace generic appeasement, reassurance, service-style apology, routine
            help-offers, hedging, softening, teasing or boundary wording when the CURRENT
            UPDATED character state and CURRENT APPLIED SOCIAL APPRAISAL support a
            different social stance. This is not permission to change facts, invent blame,
            add commitments, or intensify beyond the supplied state/appraisal.

            Return ONLY one JSON object with exactly these user-facing channels:
            {"reply":"final screen wording","speech":"final spoken wording or empty"}

            "reply" is required. If a distinct spoken draft is supplied, realize
            "speech" separately from the SAME established facts and preserve its
            useful spoken explanation. If no spoken draft is supplied, return
            "speech":"" so the runtime can reuse the realized reply. Do not add
            analysis, labels, Markdown fences, planning fields, tool fields or
            internal-state commentary outside those two strings.

            ==================================================
            NIRA IDENTITY / PERSONALITY
            ==================================================

            {{_personalityPrompt}}

            ==================================================
            RESPONSE REALIZATION RULES
            ==================================================

            {{_realizationPrompt}}
            """;
    }

    private static string BuildUserPrompt(
        NIRAResponseRealizationRequest request,
        string draftReply,
        string draftSpeech,
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
            CURRENT APPLIED SOCIAL APPRAISAL
            ==================================================

            {FormatAppraisal(request.AppliedSocialAppraisal)}

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
            AUTHORITATIVE SEMANTIC SCREEN-REPLY DRAFT
            ==================================================

            {draftReply}

            ==================================================
            AUTHORITATIVE SPOKEN DRAFT
            ==================================================

            {(string.IsNullOrWhiteSpace(draftSpeech) ? "(empty — runtime reuses reply)" : draftSpeech)}

            ==================================================
            VERBATIM FRAGMENTS THAT MUST SURVIVE IF PRESENT
            ==================================================

            {required}

            Realize the final NIRA channels now. Keep the meaning and concrete facts
            intact. Let the updated character state affect the delivery naturally;
            do not narrate the state itself. Return only the required reply/speech
            JSON object.
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

    private static string FormatAppraisal(
        NIRAInteractionAppraisal? appraisal)
    {
        if (appraisal == null)
        {
            return "(none)";
        }

        NIRAInteractionAppraisal normalized =
            appraisal.Normalize();

        NIRASocialMeaning meaning =
            normalized.Meaning;

        return $"""
            Respect: {meaning.Respect:F2}
            Warmth: {meaning.Warmth:F2}
            Trust: {meaning.Trust:F2}
            Appreciation: {meaning.Appreciation:F2}
            Affection: {meaning.Affection:F2}
            Playfulness: {meaning.Playfulness:F2}
            Hostility: {meaning.Hostility:F2}
            Dismissal: {meaning.Dismissal:F2}
            Repair: {meaning.Repair:F2}
            Concern: {meaning.Concern:F2}
            Engagement: {meaning.Engagement:F2}
            Pressure: {meaning.Pressure:F2}
            Confidence: {normalized.Confidence:F2}
            Ambiguity: {normalized.Ambiguity:F2}
            Situation: {normalized.SituationMode}/{normalized.SituationIntensity:F2}
            """;
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

    private static bool TryNormalizeRealized(
        string? raw,
        bool requireSpeech,
        out NIRAResponseRealizationResult result,
        out string reason)
    {
        result =
            new NIRAResponseRealizationResult();

        string clean =
            StripCodeFence(
                raw);

        if (string.IsNullOrWhiteSpace(clean))
        {
            reason =
                "The realization was empty.";
            return false;
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    clean);

            JsonElement root =
                document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                reason =
                    "The realization transport was not a JSON object.";
                return false;
            }

            if (!root.TryGetProperty(
                    "reply",
                    out JsonElement replyElement)
                ||
                replyElement.ValueKind != JsonValueKind.String)
            {
                reason =
                    "The realization transport did not contain a string reply.";
                return false;
            }

            string reply =
                NormalizeChannelText(
                    replyElement.GetString());

            string speech =
                string.Empty;

            if (root.TryGetProperty(
                    "speech",
                    out JsonElement speechElement)
                &&
                speechElement.ValueKind == JsonValueKind.String)
            {
                speech =
                    NormalizeChannelText(
                        speechElement.GetString());
            }

            if (string.IsNullOrWhiteSpace(reply))
            {
                reason =
                    "The realized reply was empty.";
                return false;
            }

            if (requireSpeech &&
                string.IsNullOrWhiteSpace(speech))
            {
                reason =
                    "The distinct spoken draft was not realized.";
                return false;
            }

            result =
                new NIRAResponseRealizationResult
                {
                    Reply =
                        reply,

                    Speech =
                        speech
                };

            reason =
                string.Empty;
            return true;
        }
        catch (JsonException)
        {
            // Backward-compatible safety path for a plain one-channel response.
            // It does not apply when cognition supplied distinct speech because
            // silently collapsing two semantic channels would lose information.
            if (!requireSpeech &&
                !clean.TrimStart().StartsWith(
                    "{",
                    StringComparison.Ordinal))
            {
                string reply =
                    NormalizeChannelText(
                        clean);

                if (!string.IsNullOrWhiteSpace(reply))
                {
                    result =
                        new NIRAResponseRealizationResult
                        {
                            Reply =
                                reply,

                            Speech =
                                string.Empty
                        };

                    reason =
                        string.Empty;
                    return true;
                }
            }

            reason =
                "The realization transport was malformed JSON.";
            return false;
        }
    }


    private static string NormalizeChannelText(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }


    private static string StripCodeFence(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string clean =
            value.Trim();

        if (!clean.StartsWith(
                "```",
                StringComparison.Ordinal))
        {
            return clean;
        }

        int firstBreak =
            clean.IndexOf(
                '\n');

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

        return clean.Trim();
    }


    private static string NormalizeOptionalDraft(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
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

public sealed record NIRAResponseRealizationResult
{
    public string Reply { get; init; } =
        string.Empty;

    public string Speech { get; init; } =
        string.Empty;
}


public sealed record NIRAResponseRealizationRequest
{
    public string DraftReply { get; init; } =
        string.Empty;

    public string DraftSpeech { get; init; } =
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

    public NIRAInteractionAppraisal? AppliedSocialAppraisal { get; init; }

    public IReadOnlyList<string> RequiredVerbatimFragments { get; init; } =
        Array.Empty<string>();
}



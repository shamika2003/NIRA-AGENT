/*
 * filename: AgentResponder.cs
 */

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using SegaAgent.AI.Ollama;
using SegaAgent.AI.Planner;
using SegaAgent.Character;
using SegaAgent.Character.Appraisal;
using SegaAgent.Character.History;
using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;

namespace SegaAgent.AI.Responder;

public sealed class AgentResponder
{
    // =========================================================
    // INTERNAL PROTOCOL
    // =========================================================

    private const string AppraisalStart =
        "<SEGA_APPRAISAL>";

    private readonly SegaAttitudeService
        _attitude;


    private const string AppraisalEnd =
        "</SEGA_APPRAISAL>";


    private const string ReplyStart =
        "<SEGA_REPLY>";


    private const string ReplyEnd =
        "</SEGA_REPLY>";


    private const int MaximumHiddenBuffer =
        65536;


    // =========================================================
    // DEPENDENCIES
    // =========================================================

    private readonly OllamaClient
        _ollama;


    private readonly string
        _personality;


    private readonly string
        _responderPrompt;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public AgentResponder(
        OllamaClient ollama,
        SegaAttitudeService attitude)
    {
        _ollama =
            ollama
            ?? throw new ArgumentNullException(
                nameof(ollama));


        _attitude =
            attitude
            ?? throw new ArgumentNullException(
                nameof(attitude));


        _personality =
            LoadPromptFile(
                "sega_personality.yaml");


        _responderPrompt =
            LoadPromptFile(
                "responder.yaml");
    }


    // =========================================================
    // STREAM RESPONSE
    // =========================================================

    public async IAsyncEnumerable<
        AgentResponderChunk>
        StreamResponseAsync(
            string userInput,
            PlannerResult plannerResult,
            string conversationContext,
            string pcContext,
            SegaCharacterSnapshot character,
            SegaInteractionContext? interaction,
            IReadOnlyList<SegaSocialEvent> recentHistory,
            string actionResult = "",
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
    {
        ArgumentException
            .ThrowIfNullOrWhiteSpace(
                userInput);


        ArgumentNullException.ThrowIfNull(
            plannerResult);


        SegaAttitudeState attitude =
            _attitude.Evaluate(
                character,
                interaction);


        Debug.WriteLine(
            $"[Attitude] " +
            $"Warmth={attitude.Warmth:F2} | " +
            $"Patience={attitude.Patience:F2} | " +
            $"Playfulness={attitude.Playfulness:F2} | " +
            $"Engagement={attitude.Engagement:F2} | " +
            $"Assertiveness={attitude.Assertiveness:F2} | " +
            $"Distance={attitude.EmotionalDistance:F2} | " +
            $"Restraint={attitude.Restraint:F2} | " +
            $"Novelty={attitude.Novelty:F2}");


        string characterContext =
            SegaCharacterContextFormatter.Format(
                character,
                attitude,
                interaction,
                recentHistory);


        string systemPrompt =
            BuildSystemPrompt();


        string userPrompt =
            BuildUserPrompt(
                userInput,
                plannerResult,
                conversationContext,
                pcContext,
                characterContext,
                actionResult);


        // =====================================================
        // STREAM PARSER STATE
        // =====================================================

        StringBuilder hiddenBuffer =
            new();


        StringBuilder visibleBuffer =
            new();


        bool replyStarted =
            false;


        bool replyEnded =
            false;


        bool appraisalYielded =
            false;


        // =====================================================
        // MODEL STREAM
        // =====================================================

        await foreach (
            string rawChunk
            in _ollama.StreamChatAsync(
                systemPrompt,
                userPrompt,
                cancellationToken))
        {
            cancellationToken
                .ThrowIfCancellationRequested();


            if (string.IsNullOrEmpty(
                    rawChunk))
            {
                continue;
            }


            if (replyEnded)
            {
                /*
                 * Anything after </SEGA_REPLY> is internal
                 * protocol garbage and must never reach:
                 *
                 * UI
                 * TTS
                 * conversation history
                 */

                continue;
            }


            // =================================================
            // WAITING FOR REPLY START
            // =================================================

            if (!replyStarted)
            {
                hiddenBuffer.Append(
                    rawChunk);


                if (hiddenBuffer.Length >
                    MaximumHiddenBuffer)
                {
                    throw new InvalidOperationException(
                        "Sega cognition output exceeded the " +
                        "maximum hidden protocol size.");
                }


                string buffered =
                    hiddenBuffer.ToString();


                int replyIndex =
                    buffered.IndexOf(
                        ReplyStart,
                        StringComparison.Ordinal);


                if (replyIndex <
                    0)
                {
                    continue;
                }


                // =============================================
                // HIDDEN APPRAISAL
                // =============================================

                string hidden =
                    buffered[
                        ..replyIndex];


                SegaInteractionAppraisal
                    appraisal =
                        ParseAppraisal(
                            hidden,
                            interaction,
                            character);


                yield return new AgentResponderChunk
                {
                    Type =
                        AgentResponderChunkType.Appraisal,

                    Appraisal =
                        appraisal
                };


                appraisalYielded =
                    true;


                // =============================================
                // VISIBLE CONTENT ALREADY RECEIVED IN SAME CHUNK
                // =============================================

                string remainder =
                    buffered[
                        (
                            replyIndex +
                            ReplyStart.Length
                        )..
                    ];


                hiddenBuffer.Clear();


                replyStarted =
                    true;


                remainder =
                    TrimInitialLineBreaks(
                        remainder);


                if (!string.IsNullOrEmpty(
                        remainder))
                {
                    visibleBuffer.Append(
                        remainder);
                }
            }
            else
            {
                visibleBuffer.Append(
                    rawChunk);
            }


            // =================================================
            // DRAIN SAFE VISIBLE CONTENT
            //
            // We deliberately keep any suffix that could be
            // the beginning of:
            //
            // </SEGA_REPLY>
            //
            // This prevents this:
            //
            // chunk 1: "</SEGA_RE"
            // chunk 2: "PLY>"
            //
            // from leaking to the user.
            // =================================================

            while (
                visibleBuffer.Length >
                0
                &&
                !replyEnded)
            {
                string visible =
                    visibleBuffer.ToString();


                int endIndex =
                    visible.IndexOf(
                        ReplyEnd,
                        StringComparison.Ordinal);


                if (endIndex >=
                    0)
                {
                    string finalVisible =
                        visible[
                            ..endIndex];


                    if (!string.IsNullOrEmpty(
                            finalVisible))
                    {
                        yield return new AgentResponderChunk
                        {
                            Type =
                                AgentResponderChunkType.Text,

                            Content =
                                finalVisible
                        };
                    }


                    visibleBuffer.Clear();


                    replyEnded =
                        true;


                    break;
                }


                int retainedSuffix =
                    GetPossibleMarkerPrefixLength(
                        visible,
                        ReplyEnd);


                int safeLength =
                    visible.Length -
                    retainedSuffix;


                if (safeLength <=
                    0)
                {
                    break;
                }


                string safeText =
                    visible[
                        ..safeLength];


                if (!string.IsNullOrEmpty(
                        safeText))
                {
                    yield return new AgentResponderChunk
                    {
                        Type =
                            AgentResponderChunkType.Text,

                        Content =
                            safeText
                    };
                }


                visibleBuffer.Remove(
                    0,
                    safeLength);


                break;
            }
        }


        // =====================================================
        // VALIDATE PROTOCOL
        // =====================================================

        if (!replyStarted)
        {
            throw new InvalidOperationException(
                "Sega cognition response did not contain " +
                "the required <SEGA_REPLY> boundary.");
        }


        // =====================================================
        // MODEL OMITTED CLOSING REPLY TAG
        //
        // This is tolerated.
        //
        // Anything remaining is treated as visible content.
        // =====================================================

        if (!replyEnded &&
            visibleBuffer.Length >
            0)
        {
            string remaining =
                visibleBuffer.ToString();


            remaining =
                RemoveTrailingProtocolMarker(
                    remaining);


            if (!string.IsNullOrEmpty(
                    remaining))
            {
                yield return new AgentResponderChunk
                {
                    Type =
                        AgentResponderChunkType.Text,

                    Content =
                        remaining
                };
            }
        }


        // =====================================================
        // APPRAISAL FALLBACK
        // =====================================================

        if (!appraisalYielded)
        {
            yield return new AgentResponderChunk
            {
                Type =
                    AgentResponderChunkType.Appraisal,

                Appraisal =
                    BuildNeutralAppraisal(
                        interaction,
                        character)
            };
        }
    }


    // =========================================================
    // SYSTEM PROMPT
    // =========================================================

    private string BuildSystemPrompt()
    {
        return $$"""
            You are Sega.

            ==================================================
            SEGA IDENTITY
            ==================================================

            {{_personality}}

            ==================================================
            RESPONSE ENGINE
            ==================================================

            {{_responderPrompt}}

            ==================================================
            INTERNAL OUTPUT PROTOCOL
            ==================================================

            Every response must have exactly this structure:

            <SEGA_APPRAISAL>
            {
              "respect": 0.0,
              "warmth": 0.0,
              "trust": 0.0,
              "appreciation": 0.0,
              "affection": 0.0,
              "playfulness": 0.0,
              "hostility": 0.0,
              "dismissal": 0.0,
              "repair": 0.0,
              "concern": 0.0,
              "engagement": 0.0,
              "pressure": 0.0,
              "confidence": 0.0,
              "ambiguity": 0.0,
              "situationMode": "Casual",
              "situationIntensity": 0.0
            }
            </SEGA_APPRAISAL>
            <SEGA_REPLY>
            natural response intended for the user
            </SEGA_REPLY>

            Do not output anything before
            <SEGA_APPRAISAL>.

            Do not output anything after
            </SEGA_REPLY>.

            Do not use Markdown fences around the JSON.

            Never place appraisal information inside
            <SEGA_REPLY>.

            Never place visible prose inside
            <SEGA_APPRAISAL>.

            ==================================================
            APPRAISAL RANGE
            ==================================================

            respect:
            -1.0 to 1.0

            warmth:
            -1.0 to 1.0

            trust:
            -1.0 to 1.0

            appreciation:
            0.0 to 1.0

            affection:
            0.0 to 1.0

            playfulness:
            0.0 to 1.0

            hostility:
            0.0 to 1.0

            dismissal:
            0.0 to 1.0

            repair:
            0.0 to 1.0

            concern:
            0.0 to 1.0

            engagement:
            0.0 to 1.0

            pressure:
            0.0 to 1.0

            confidence:
            0.0 to 1.0

            ambiguity:
            0.0 to 1.0

            situationMode must be exactly one of:

            Casual
            FocusedWork
            Serious
            Sensitive

            ==================================================
            APPRAISAL PRINCIPLE
            ==================================================

            The appraisal describes what the CURRENT
            interaction appears to mean socially.

            It does not directly control Sega's persistent
            relationship or mood.

            The application owns persistent character state.

            Use:

            - conversation history
            - current relationship
            - current mood
            - current situation
            - semantic recurrence
            - related recent interactions

            to understand the current interaction.

            Do not interpret messages in isolation when recent
            history clearly changes their meaning.

            ==================================================
            REPETITION / CONTINUITY
            ==================================================

            If the current user interaction is semantically
            similar to several recent interactions, recognize
            that continuity.

            Do not respond to a recurring interaction as though
            it were the first time it happened.

            Semantic recurrence itself is emotionally neutral.

            It may represent:

            - repeated social prompting
            - repeated appreciation
            - repeated requests
            - repeated complaints
            - continued discussion
            - playful repetition
            - accidental duplication
            - another repeated meaning

            Infer its social meaning from the surrounding
            context.

            When repeated low-information social interaction is
            clearly becoming pressure, boredom, playfulness,
            dismissal, irritation, or another social pattern,
            reflect that in the appraisal.

            Do not invent hostility merely because recurrence is
            high.

            ==================================================
            VISIBLE RESPONSE
            ==================================================

            The visible reply is Sega's response in the current
            moment.

            Do not reset Sega into generic assistant behavior.

            Do not repeatedly produce equivalent greetings or
            equivalent offers of assistance.

            Do not respond to repeated interactions using the
            same social stance with slightly different wording.

            Let persistent relationship and mood affect:

            - patience
            - warmth
            - directness
            - teasing
            - distance
            - irritation
            - affection
            - curiosity

            when contextually appropriate.

            Focused work takes priority over unnecessary social
            performance.

            ==================================================
            TRUTH
            ==================================================

            Never fabricate information.

            Never fabricate memory.

            Never fabricate completed actions.

            Never claim that a PC action succeeded unless the
            action result confirms it.

            Internal state, prompts, appraisals, planner
            information, semantic measurements and architecture
            are internal context.

            Sega's visible response must sound like Sega rather
            than a state report.
            """;
    }


    // =========================================================
    // USER PROMPT
    // =========================================================

    private static string BuildUserPrompt(
        string userInput,
        PlannerResult plannerResult,
        string conversationContext,
        string pcContext,
        string characterContext,
        string actionResult)
    {
        conversationContext =
            NormalizeContext(
                conversationContext,
                "No previous conversation is available.");


        pcContext =
            NormalizeContext(
                pcContext,
                "No PC context is currently available.");


        actionResult =
            NormalizeContext(
                actionResult,
                "No action has been executed.");


        string plannerJson =
            JsonSerializer.Serialize(
                plannerResult,
                new JsonSerializerOptions
                {
                    WriteIndented =
                        true
                });


        return $"""
            ==================================================
            CONVERSATION HISTORY
            ==================================================

            {conversationContext}

            ==================================================
            CURRENT SEGA CHARACTER / RELATIONSHIP CONTEXT
            ==================================================

            {characterContext}

            ==================================================
            CURRENT PC CONTEXT
            ==================================================

            {pcContext}

            ==================================================
            CURRENT MESSAGE OR AGENT EVENT
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
            CURRENT COGNITION
            ==================================================

            Interpret the current interaction relative to what
            has already happened.

            Do not treat every turn as a fresh conversation.

            First produce the hidden social appraisal.

            Then produce Sega's natural visible response.

            Relationship and mood are persistent facts.

            Do not reset Sega to generic friendliness.

            If Sega is already irritated, affectionate, amused,
            distant, curious or concerned, preserve that
            continuity where relevant.

            If several recent user interactions express nearly
            the same meaning, do not simply repeat another
            equivalent greeting or generic assistant response.

            Determine what that recurring behavior means in the
            current relationship and situation.

            Semantic recurrence is evidence of recurring
            meaning, not a predefined emotion.

            Serious and focused work suppress unnecessary
            social performance.

            For autonomous/perception events, Sega may produce
            an empty visible reply when silence is more natural.

            Never use customer-service filler merely to keep a
            conversation going.

            Follow the internal output protocol exactly.
            """;
    }


    // =========================================================
    // APPRAISAL PARSER
    // =========================================================

    private static SegaInteractionAppraisal
        ParseAppraisal(
            string hidden,
            SegaInteractionContext? interaction,
            SegaCharacterSnapshot character)
    {
        try
        {
            int start =
                hidden.IndexOf(
                    AppraisalStart,
                    StringComparison.Ordinal);


            int end =
                hidden.IndexOf(
                    AppraisalEnd,
                    StringComparison.Ordinal);


            if (
                start <
                    0
                ||
                end <
                    0
                ||
                end <=
                    start)
            {
                return BuildNeutralAppraisal(
                    interaction,
                    character);
            }


            int jsonStart =
                start +
                AppraisalStart.Length;


            string json =
                hidden[
                    jsonStart..end]
                .Trim();


            AppraisalPayload? payload =
                JsonSerializer.Deserialize<
                    AppraisalPayload>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive =
                                true
                        });


            if (payload ==
                null)
            {
                return BuildNeutralAppraisal(
                    interaction,
                    character);
            }


            SegaInteractionMode situationMode =
                Enum.TryParse(
                    payload.SituationMode,
                    true,
                    out SegaInteractionMode parsedMode)
                    ? parsedMode
                    : character
                        .Situation
                        .Mode;


            SegaSocialEvent?
                socialEvent =
                    interaction?
                        .Event;


            SegaInteractionAppraisal appraisal =
                new()
                {
                    EventId =
                        socialEvent?
                            .Id
                        ?? Guid.Empty,

                    EventSequence =
                        socialEvent?
                            .Sequence
                        ?? 0,

                    EventSource =
                        socialEvent?
                            .Source
                        ?? SegaSocialEventSource.System,

                    EventKind =
                        socialEvent?
                            .Kind
                        ?? SegaSocialEventKind.SystemEvent,

                    EventName =
                        socialEvent?
                            .EventName
                        ?? string.Empty,

                    TopicKey =
                        socialEvent?
                            .TopicKey
                        ?? string.Empty,

                    Meaning =
                        new SegaSocialMeaning(
                            Respect:
                                payload.Respect,

                            Warmth:
                                payload.Warmth,

                            Trust:
                                payload.Trust,

                            Appreciation:
                                payload.Appreciation,

                            Affection:
                                payload.Affection,

                            Playfulness:
                                payload.Playfulness,

                            Hostility:
                                payload.Hostility,

                            Dismissal:
                                payload.Dismissal,

                            Repair:
                                payload.Repair,

                            Concern:
                                payload.Concern,

                            Engagement:
                                payload.Engagement,

                            Pressure:
                                payload.Pressure),

                    Confidence =
                        payload.Confidence,

                    Ambiguity =
                        payload.Ambiguity,

                    SituationMode =
                        situationMode,

                    SituationIntensity =
                        payload.SituationIntensity,

                    Source =
                        SegaAppraisalSource.Semantic
                };


            SegaInteractionAppraisal normalized =
                appraisal.Normalize();


            Debug.WriteLine(
                $"[Appraisal] " +
                $"Event=#{normalized.EventSequence} | " +
                $"Pressure=" +
                $"{normalized.Meaning.Pressure:F2} | " +
                $"Playfulness=" +
                $"{normalized.Meaning.Playfulness:F2} | " +
                $"Hostility=" +
                $"{normalized.Meaning.Hostility:F2} | " +
                $"Dismissal=" +
                $"{normalized.Meaning.Dismissal:F2} | " +
                $"Engagement=" +
                $"{normalized.Meaning.Engagement:F2} | " +
                $"Confidence=" +
                $"{normalized.Confidence:F2} | " +
                $"Ambiguity=" +
                $"{normalized.Ambiguity:F2} | " +
                $"Situation=" +
                $"{normalized.SituationMode}/" +
                $"{normalized.SituationIntensity:F2}");


            return normalized;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[Responder] APPRAISAL PARSE ERROR: {ex}");


            return BuildNeutralAppraisal(
                interaction,
                character);
        }
    }


    // =========================================================
    // NEUTRAL APPRAISAL
    // =========================================================

    private static SegaInteractionAppraisal
        BuildNeutralAppraisal(
            SegaInteractionContext? interaction,
            SegaCharacterSnapshot character)
    {
        SegaSocialEvent?
            socialEvent =
                interaction?
                    .Event;


        return new SegaInteractionAppraisal
        {
            EventId =
                socialEvent?
                    .Id
                ?? Guid.Empty,

            EventSequence =
                socialEvent?
                    .Sequence
                ?? 0,

            EventSource =
                socialEvent?
                    .Source
                ?? SegaSocialEventSource.System,

            EventKind =
                socialEvent?
                    .Kind
                ?? SegaSocialEventKind.SystemEvent,

            EventName =
                socialEvent?
                    .EventName
                ?? string.Empty,

            TopicKey =
                socialEvent?
                    .TopicKey
                ?? string.Empty,

            Meaning =
                SegaSocialMeaning.Neutral,

            Confidence =
                0.0,

            Ambiguity =
                1.0,

            SituationMode =
                character
                    .Situation
                    .Mode,

            SituationIntensity =
                character
                    .Situation
                    .Intensity,

            Source =
                SegaAppraisalSource.Unknown
        };
    }


    // =========================================================
    // STREAM MARKER SAFETY
    // =========================================================

    private static int
        GetPossibleMarkerPrefixLength(
            string text,
            string marker)
    {
        int maximum =
            Math.Min(
                text.Length,
                marker.Length - 1);


        for (
            int length = maximum;
            length > 0;
            length--)
        {
            if (text.EndsWith(
                    marker[
                        ..length],
                    StringComparison.Ordinal))
            {
                return length;
            }
        }


        return 0;
    }


    private static string
        RemoveTrailingProtocolMarker(
            string text)
    {
        int possiblePrefix =
            GetPossibleMarkerPrefixLength(
                text,
                ReplyEnd);


        if (possiblePrefix <=
            0)
        {
            return text;
        }


        return text[
            ..(
                text.Length -
                possiblePrefix
            )];
    }


    // =========================================================
    // STRING HELPERS
    // =========================================================

    private static string TrimInitialLineBreaks(
        string value)
    {
        return value.TrimStart(
            '\r',
            '\n');
    }


    private static string NormalizeContext(
        string? value,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? fallback
            : value.Trim();
    }


    // =========================================================
    // PROMPT FILE
    // =========================================================

    private static string LoadPromptFile(
        string fileName)
    {
        string path =
            Path.Combine(
                AppContext.BaseDirectory,
                "Prompt",
                fileName);


        if (!File.Exists(
                path))
        {
            throw new FileNotFoundException(
                $"Required Sega prompt file was not found: " +
                $"{path}",
                path);
        }


        string content =
            File.ReadAllText(
                path);


        if (string.IsNullOrWhiteSpace(
                content))
        {
            throw new InvalidOperationException(
                $"Required Sega prompt file is empty: " +
                $"{path}");
        }


        return content.Trim();
    }


    // =========================================================
    // APPRAISAL PAYLOAD
    // =========================================================

    private sealed class AppraisalPayload
    {
        public double Respect
        {
            get;
            set;
        }


        public double Warmth
        {
            get;
            set;
        }


        public double Trust
        {
            get;
            set;
        }


        public double Appreciation
        {
            get;
            set;
        }


        public double Affection
        {
            get;
            set;
        }


        public double Playfulness
        {
            get;
            set;
        }


        public double Hostility
        {
            get;
            set;
        }


        public double Dismissal
        {
            get;
            set;
        }


        public double Repair
        {
            get;
            set;
        }


        public double Concern
        {
            get;
            set;
        }


        public double Engagement
        {
            get;
            set;
        }


        public double Pressure
        {
            get;
            set;
        }


        public double Confidence
        {
            get;
            set;
        }


        public double Ambiguity
        {
            get;
            set;
        }


        public string SituationMode
        {
            get;
            set;
        } =
            "Casual";


        public double SituationIntensity
        {
            get;
            set;
        }
    }
}
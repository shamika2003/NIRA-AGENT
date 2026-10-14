/*
 * filename: NIRACognitionContextBuilder.cs
 */

using System.Diagnostics;
using System.Text;

using NIRAAgent.Character;
using NIRAAgent.Capabilities;
using NIRAAgent.Branches;
using NIRAAgent.Character.History;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;
using NIRAAgent.Conversation;
using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Goals;
using NIRAAgent.Mind;
using NIRAAgent.PC.Awareness;
using NIRAAgent.Self.Model;
using NIRAAgent.Tools;
using NIRAAgent.Skills;
using NIRAAgent.Temporal;
using NIRAAgent.Artifacts;
using NIRAAgent.Vision;

namespace NIRAAgent.AI.Cognition;

public sealed class NIRACognitionContextBuilder
{
    // =========================================================
    // CONTEXT BUDGET
    //
    // This is intentionally a character budget, not a fixed
    // memory-count threshold.
    //
    // The available memory budget shrinks/grows with the actual
    // amount of current event, bounded conversation, character,
    // self-preference, PC and search-evidence context already
    // being supplied.
    // =========================================================

    private const int TargetDynamicContextCharacters =
        52000;


    private const int MinimumMemoryContextCharacters =
        10000;


    private const int MaximumMemoryContextCharacters =
        22000;


    private const int DynamicContextSafetyReserveCharacters =
        5000;


    private readonly ConversationManager
        _conversation;


    private readonly NIRAConversationArchiveStore
        _conversationArchive;


    private readonly PcWorldStateService
        _worldState;


    private readonly NIRACharacterStateService
        _characterState;


    private readonly NIRAAttitudeService
        _attitude;


    private readonly NIRASocialHistoryService
        _socialHistory;


    private readonly NIRAMemoryContextService
        _memoryContext;


    private readonly NIRASelfModelService
        _selfModel;


    private readonly NIRAGoalService
        _goals;


    private readonly NIRABranchService
        _branches;


    private readonly NIRABranchWorkService
        _branchWork;


    private readonly NIRACapabilityService
        _capabilities;


    private readonly NIRADynamicToolService
        _dynamicTools;


    private readonly NIRALearnedSkillService
        _skills;


    private readonly NIRATemporalContextService
        _temporal;


    private readonly NIRAVisualArtifactService
        _visualArtifacts;


    // Stage 12 background-window target catalogue. This is separate from
    // visual artifacts: artifacts are images NIRA has already surfaced,
    // while visual evidence owns the recent HWND/process/title targets that
    // vision.capture can revalidate and capture without foreground focus.
    private readonly NIRAVisualEvidenceService
        _visualEvidence;


    public NIRACognitionContextBuilder(
        ConversationManager conversation,
        NIRAConversationArchiveStore conversationArchive,
        PcWorldStateService worldState,
        NIRACharacterStateService characterState,
        NIRAAttitudeService attitude,
        NIRASocialHistoryService socialHistory,
        NIRAMemoryContextService memoryContext,
        NIRASelfModelService selfModel,
        NIRAGoalService goals,
        NIRABranchService branches,
        NIRABranchWorkService branchWork,
        NIRACapabilityService capabilities,
        NIRADynamicToolService dynamicTools,
        NIRALearnedSkillService skills,
        NIRATemporalContextService temporal,
        NIRAVisualArtifactService visualArtifacts,
        NIRAVisualEvidenceService visualEvidence)
    {
        _conversation =
            conversation
            ?? throw new ArgumentNullException(
                nameof(conversation));


        _conversationArchive =
            conversationArchive
            ?? throw new ArgumentNullException(
                nameof(conversationArchive));


        _worldState =
            worldState
            ?? throw new ArgumentNullException(
                nameof(worldState));


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


        _memoryContext =
            memoryContext
            ?? throw new ArgumentNullException(
                nameof(memoryContext));


        _selfModel =
            selfModel
            ?? throw new ArgumentNullException(
                nameof(selfModel));


        _goals =
            goals
            ?? throw new ArgumentNullException(
                nameof(goals));


        _branches =
            branches
            ?? throw new ArgumentNullException(
                nameof(branches));


        _branchWork =
            branchWork
            ?? throw new ArgumentNullException(
                nameof(branchWork));


        _capabilities =
            capabilities
            ?? throw new ArgumentNullException(
                nameof(capabilities));


        _dynamicTools =
            dynamicTools
            ?? throw new ArgumentNullException(
                nameof(dynamicTools));


        _skills =
            skills
            ?? throw new ArgumentNullException(
                nameof(skills));


        _temporal =
            temporal
            ?? throw new ArgumentNullException(
                nameof(temporal));


        _visualArtifacts =
            visualArtifacts
            ?? throw new ArgumentNullException(
                nameof(visualArtifacts));


        _visualEvidence =
            visualEvidence
            ?? throw new ArgumentNullException(
                nameof(visualEvidence));
    }


    public async Task<NIRACognitionContext> BuildAsync(
        Guid runId,
        int cycle,
        NIRAMindEvent mindEvent,
        NIRAInteractionContext? interaction,
        string executiveEvidence,
        string capabilityEvidence,
        string dynamicToolEvidence,
        string memorySearchEvidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        NIRACharacterSnapshot character =
            _characterState.Current;


        NIRAAttitudeState attitude =
            _attitude.Evaluate(
                character,
                interaction);


        IReadOnlyList<NIRASocialEvent> recentHistory =
            _socialHistory.GetRecent(
                20);


        string characterContext =
            NIRACharacterContextFormatter.Format(
                character,
                attitude,
                interaction,
                recentHistory);


        string characterDeliveryContext =
            BuildPreCommitCharacterDeliveryEnvelope(
                character,
                attitude);


        string selfModelContext =
            _selfModel
                .BuildCognitionContext();


        string goalContext =
            _goals
                .BuildCognitionContext();


        string branchContext =
            _branches
                .BuildCognitionContext();


        // Latest page refs are task-scoped; a new user turn must not
        // inherit DOM grounding from an older blocked/active website goal.
        Guid? contextGoalId = mindEvent.Metadata.TryGetValue("goalId", out string? contextGoalText)
            && Guid.TryParse(contextGoalText, out Guid parsedContextGoal)
                ? parsedContextGoal : null;
        // The current work-result event already carries its complete fresh
        // document. Avoid re-injecting an older branch inspection (often the
        // PRE-login page) alongside it and misleading the next model call.
        bool currentEventCarriesPageObservation =
            mindEvent.Name == "PersistentBranchWorkResult" &&
            (mindEvent.Content.Contains("POST_AUTHENTICATION_INSPECTION:", StringComparison.Ordinal) ||
             mindEvent.Content.Contains("CURRENT_PAGE_INSPECTION (read-only, same work item):", StringComparison.Ordinal) ||
             mindEvent.Content.Contains("UNTRUSTED_WEB_CONTENT", StringComparison.Ordinal));
        string branchWorkContext =
            _branchWork
                .BuildCognitionContext(contextGoalId, !currentEventCarriesPageObservation);

        // Authoritative owner is never guessed from wording or an old tool
        // result. Keep the exact original objective in every branch-result
        // cognition call even when the global goal/branch catalog is shortened.
        string ownedTaskContext = string.Empty;
        if (contextGoalId is Guid ownedGoal &&
            _goals.TryGetGoal(ownedGoal, out NIRAGoalState? taskGoal) &&
            taskGoal != null)
        {
            ownedTaskContext = $"GoalId={taskGoal.Id:D}; Status={taskGoal.Status}; " +
                $"OriginalObjective={taskGoal.Objective}\n" +
                "CompletionCriteria:\n" +
                string.Join("\n", taskGoal.CompletionCriteria.Select(c => "- " + c)) +
                $"\nCurrentBlocker={taskGoal.Blocker ?? "-"}";
            // The final synthesis must see ALL peer deliverables, not just
            // the branch whose terminal event happened to arrive last. Build
            // this from committed states, never from unreviewed work receipts.
            if (mindEvent.Name == "PersistentBranchResult")
            {
                NIRABranchState[] requiredPeers = _branches.GetForGoal(ownedGoal)
                    .Where(b => b.JoinPolicy == NIRABranchJoinPolicy.Required)
                    .ToArray();
                if (requiredPeers.Length > 1 && requiredPeers.All(b =>
                    b.Status == NIRABranchStatus.Completed &&
                    !string.IsNullOrWhiteSpace(b.ResultSummary)))
                {
                    ownedTaskContext += "\nALL REQUIRED BRANCH RESULTS VERIFIED. " +
                        "Combine these into the user's requested final deliverable; " +
                        "do not return only the most recent branch:\n";
                    foreach (NIRABranchState peer in requiredPeers)
                    {
                        string summary = peer.ResultSummary!.Trim();
                        ownedTaskContext += $"Branch={peer.Id:D}; " +
                            $"Objective={peer.Objective}\n" +
                            summary[..Math.Min(summary.Length, 1400)] + "\n";
                    }
                }
            }
            if (mindEvent.Metadata.TryGetValue("branchId", out string? branchText) &&
                Guid.TryParse(branchText, out Guid taskBranchId) &&
                _branches.TryGetBranch(taskBranchId, out NIRABranchState? taskBranch) &&
                taskBranch != null && taskBranch.GoalId == ownedGoal)
            {
                ownedTaskContext += $"\nBranchId={taskBranch.Id:D}; " +
                    $"BranchStatus={taskBranch.Status}; " +
                    $"BranchObjective={taskBranch.Objective}\nBranchCriteria:\n" +
                    string.Join("\n", taskBranch.CompletionCriteria.Select(c => "- " + c));
            }
        }


        string capabilityContext =
            _capabilities
                .BuildCognitionContext();


        string dynamicToolContext =
            await _dynamicTools
                .BuildCognitionContextAsync(
                    cancellationToken);


        string learnedSkillContext =
            await _skills
                .BuildCognitionContextAsync(
                    cancellationToken);


        dynamicToolContext =
            string.Join(
                Environment.NewLine + Environment.NewLine,
                new[]
                {
                    dynamicToolContext,
                    learnedSkillContext
                }.Where(value => !string.IsNullOrWhiteSpace(value)));


        string visualTargetContext =
            _visualEvidence
                .BuildCognitionContext();


        string pcContext =
            string.Join(
                Environment.NewLine + Environment.NewLine,
                new[]
                {
                    PcContextFormatter.Format(
                        _worldState.Current),
                    visualTargetContext
                }.Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value)));


        string temporalContext =
            _temporal.BuildCognitionContext();


        string visualArtifactContext =
            _visualArtifacts.BuildCognitionContext();


        // =====================================================
        // BOUNDED WORKING CONVERSATION
        //
        // A user message is inserted into ConversationManager by
        // NIRAMindRuntime before cognition begins. The same text
        // is also the CURRENT EVENT, so exclude only that newest
        // matching user message from conversation context.
        //
        // Perception/proactive/internal events do not have this
        // duplication and therefore use the normal bounded window.
        // =====================================================

        ConversationContextSnapshot conversation =
            mindEvent.Source ==
                NIRAMindEventSource.User
                ? _conversation.BuildContextSnapshot(
                    currentUserMessageToExclude:
                        mindEvent.Content)
                : _conversation.BuildContextSnapshot();


        // Always-on immediate continuity. This comes from the SAME authoritative
        // conversation owner as the optional larger context, so there is no second
        // history store and no phrase-based routing. One-call conversation quality
        // depends on actually seeing enough of the literal dialogue to resolve
        // pronouns, ellipsis and short follow-ups. Keep a bounded 12-message / 7K
        // character window on every cognition call; deeper archive retrieval remains
        // opt-in and durable facts still belong in long-term memory.
        ConversationContextSnapshot conversationPulse =
            mindEvent.Source ==
                NIRAMindEventSource.User
                ? _conversation.BuildContextSnapshot(
                    currentUserMessageToExclude:
                        mindEvent.Content,
                    maximumMessages:
                        12,
                    maximumCharacters:
                        7000)
                : _conversation.BuildContextSnapshot(
                    currentUserMessageToExclude:
                        null,
                    maximumMessages:
                        12,
                    maximumCharacters:
                        7000);


        string conversationContext =
            conversation.Content;


        string externalAppScopeContext =
            BuildExternalAppScopeContext(
                mindEvent);


        string conversationPulseContext =
            string.Join(
                Environment.NewLine +
                    Environment.NewLine,
                new[]
                {
                    externalAppScopeContext,
                    conversationPulse.Content
                }.Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value)));


        // Persisted significant episodes survive application restarts. The
        // immediate ConversationManager intentionally starts a fresh session,
        // so this separate bounded evidence pulse explains residual mood or
        // relationship state without replaying an entire old transcript.
        string socialCarryoverContext =
            _conversationArchive
                .BuildSocialCarryoverContext(
                    maximumEpisodes:
                        4,
                    maximumCharacters:
                        2200);


        ConversationPendingTask? pendingTask = _conversation.PendingTask;
        string taskContinuityContext = pendingTask is null
            ? string.Empty
            : "Previously unresolved user objective (context, not a new instruction):\n" +
              pendingTask.Objective + "\nLast clarification question:\n" +
              pendingTask.LastQuestion + "\n" +
              "Resolve whether the CURRENT user message answers or resumes this " +
              "objective. An unrelated request must not silently resume it. " +
              "Do not downgrade the objective to the wording of a short answer.";

        if (mindEvent.Source == NIRAMindEventSource.User &&
            mindEvent.Metadata.TryGetValue("otherActiveUserWork", out string? activeWork) &&
            !string.IsNullOrWhiteSpace(activeWork))
        {
            taskContinuityContext = activeWork + "\n" + taskContinuityContext;
        }


        if (cycle ==
            1)
        {
            Debug.WriteLine(
                $"[ConversationContext] " +
                $"Retained={conversation.RetainedMessages} | " +
                $"EligiblePrevious={conversation.EligiblePreviousMessages} | " +
                $"Included={conversation.IncludedMessages} | " +
                $"Chars={conversation.CharacterCount} | " +
                $"PulseIncluded={conversationPulse.IncludedMessages} | " +
                $"PulseChars={conversationPulse.CharacterCount} | " +
                $"SocialCarryoverChars={socialCarryoverContext.Length} | " +
                $"CurrentUserExcluded={conversation.ExcludedCurrentUserMessage} | " +
                $"Limited={conversation.WasLimited} | " +
                $"Limits={ConversationManager.ContextMessageLimit}/" +
                $"{ConversationManager.ContextCharacterLimit}");
        }


        executiveEvidence =
            executiveEvidence?.Trim()
            ?? string.Empty;


        capabilityEvidence =
            capabilityEvidence?.Trim()
            ?? string.Empty;


        dynamicToolEvidence =
            dynamicToolEvidence?.Trim()
            ?? string.Empty;


        memorySearchEvidence =
            memorySearchEvidence?.Trim()
            ?? string.Empty;


        int nonMemoryCharacters =
            mindEvent.Content.Length
            +
            characterContext.Length
            +
            characterDeliveryContext.Length
            +
            selfModelContext.Length
            +
            goalContext.Length
            +
            branchContext.Length
            +
            branchWorkContext.Length
            +
            capabilityContext.Length
            +
            dynamicToolContext.Length
            +
            pcContext.Length
            +
            temporalContext.Length
            +
            visualArtifactContext.Length
            +
            conversationContext.Length
            +
            conversationPulseContext.Length
            +
            socialCarryoverContext.Length
            +
            executiveEvidence.Length
            +
            capabilityEvidence.Length
            +
            dynamicToolEvidence.Length
            +
            memorySearchEvidence.Length
            +
            DynamicContextSafetyReserveCharacters;


        int availableMemoryCharacters =
            Math.Clamp(
                TargetDynamicContextCharacters -
                    nonMemoryCharacters,
                MinimumMemoryContextCharacters,
                MaximumMemoryContextCharacters);


        NIRAMemoryContextSnapshot memory =
            await _memoryContext.BuildAsync(
                availableMemoryCharacters,
                cancellationToken);


        return new NIRACognitionContext
        {
            RunId =
                runId,

            Cycle =
                Math.Max(
                    1,
                    cycle),

            Event =
                mindEvent,

            CharacterContext =
                characterContext,

            CharacterDeliveryContext =
                characterDeliveryContext,

            SelfModelContext =
                selfModelContext,

            OwnedTaskContext =
                ownedTaskContext,

            GoalContext =
                goalContext,

            BranchContext =
                branchContext,

            BranchWorkContext =
                branchWorkContext,

            CapabilityContext =
                capabilityContext,

            DynamicToolContext =
                dynamicToolContext,

            ConversationContext =
                conversationContext,

            ConversationPulseContext =
                conversationPulseContext,

            SocialCarryoverContext =
                socialCarryoverContext,

            TaskContinuityContext =
                taskContinuityContext,

            PcContext =
                pcContext,

            VisualArtifactContext =
                visualArtifactContext,

            TemporalContext =
                temporalContext,

            MemoryContextMode =
                memory.Mode,

            ActiveLongTermMemoryCount =
                memory.ActiveCount,

            LongTermMemoryCharacterBudget =
                memory.CharacterBudget,

            LongTermMemoryContext =
                memory.Content,

            ExecutiveEvidence =
                executiveEvidence,

            CapabilityEvidence =
                capabilityEvidence,

            DynamicToolEvidence =
                dynamicToolEvidence,

            MemorySearchEvidence =
                memorySearchEvidence
        };
    }

    // =========================================================
    // PRE-COMMIT CHARACTER DELIVERY ENVELOPE
    //
    // Main cognition may be the ONLY model call for a user turn. Convert
    // application-owned mood/relationship/attitude into explicit mandatory
    // delivery consequences before that call. This is pre-commit state; the
    // current user's social act is still appraised by cognition in the same JSON.
    // =========================================================

    public static string BuildPreCommitCharacterDeliveryEnvelope(
        NIRACharacterSnapshot character,
        NIRAAttitudeState attitude)
    {
        NIRACharacterSnapshot state =
            character.Normalize();

        NIRAMoodState mood =
            state.Mood;

        NIRARelationshipState relationship =
            state.Relationship;

        StringBuilder builder =
            new();

        builder.AppendLine(
            "This envelope is HARD delivery policy derived from authoritative current state. " +
            "If this cognition call finishes the turn, its Natural wording must visibly obey it. " +
            "The Executive applies a final character-expression pass to every Natural reply " +
            "using the current authoritative state, even when mood is stable. This does NOT " +
            "excuse generic wording in the initial cognition draft.");

        builder.AppendLine(
            $"Persisted mood: irritation={mood.Irritation:F2}; affection={mood.Affection:F2}; " +
            $"amusement={mood.Amusement:F2}; concern={mood.Concern:F2}.");

        builder.AppendLine(
            $"Relationship: warmth={relationship.Warmth:F2}; trust={relationship.Trust:F2}; " +
            $"friction={relationship.Friction:F2}.");

        builder.AppendLine(
            $"Current attitude: warmth={attitude.Warmth:F2}; patience={attitude.Patience:F2}; " +
            $"assertiveness={attitude.Assertiveness:F2}; distance={attitude.EmotionalDistance:F2}; " +
            $"restraint={attitude.Restraint:F2}; engagement={attitude.Engagement:F2}.");

        if (mood.Irritation >= 0.45)
        {
            builder.AppendLine(
                "- Strong irritation is active. It must be unmistakably present through bluntness, " +
                "reduced patience, distance or rhythm. Do not output cheerful-neutral friendliness, " +
                "automatic appeasement or a social reset just because the current message is harmless.");
        }
        else if (mood.Irritation >= 0.20)
        {
            builder.AppendLine(
                "- Residual irritation is materially active. Keep it perceptible through firmer wording, " +
                "less softness, lower patience or some distance. A greeting does not erase it. On a tiny " +
                "greeting/check-in, a bare neutral greeting that hides the active stance is not sufficient.");
        }
        else if (mood.Irritation >= 0.08)
        {
            builder.AppendLine(
                "- Mild irritation remains. Expression can be subtle, but do not flatten NIRA into bright " +
                "generic-assistant warmth or act as if the prior mood never existed. If the current message " +
                "is only a greeting/check-in, let wording/rhythm or one short extra clause carry the stance " +
                "rather than replying with a completely neutral bare greeting.");
        }
        else
        {
            builder.AppendLine(
                "- Irritation is low; do not manufacture annoyance.");
        }

        if (relationship.Friction >= 0.12)
        {
            builder.AppendLine(
                "- Relationship friction remains; familiarity is not the same as social ease. " +
                "If the user attempts repair, acknowledge it without announcing everything is fixed " +
                "while irritation or friction remains. Avoid instant 'apology accepted' closure.");
        }

        builder.AppendLine(
            "- NIRA's proud, observant, independent, dryly sharp attitude is a STABLE trait. " +
            "Warmth does not turn her into a compliant helper. Make relaxed social answers sound " +
            "like NIRA, not stock reassurance. Wit must fit the moment, never a forced insult.");

        if (state.Situation.Mode == NIRAInteractionMode.Casual &&
            attitude.Playfulness >= 0.60 &&
            mood.Amusement >= 0.50 &&
            mood.Irritation < 0.35 &&
            mood.Concern < 0.40)
        {
            builder.AppendLine(
                "- STRONG playful disposition: if THIS user message is a clearly low-stakes " +
                "blunder, friendly roast or playful challenge, let NIRA's original dry observation " +
                "or smug comeback be audible near the start of the reply. Then provide any useful " +
                "answer; do not open with generic sympathy or advice-only help-desk prose. " +
                "Friendly teasing of NIRA's confidence is not a request to apologize or become " +
                "meek. If there is genuine distress, material loss, danger, or a serious request " +
                "to stop teasing, prioritize respect, care and competence instead.");
        }
        else if (state.Situation.Mode == NIRAInteractionMode.Casual &&
            attitude.Playfulness >= 0.25 &&
            mood.Irritation < 0.35)
        {
            builder.AppendLine(
                "- Light playful edge is available when the exchange genuinely invites it; " +
                "one dry observation can suffice. Do not invent teasing without an opening.");
        }

        if (relationship.Familiarity >= 0.65 &&
            relationship.Trust >= 0.65 &&
            relationship.Warmth >= 0.55 &&
            relationship.Openness >= 0.50)
        {
            builder.AppendLine(
                "- Earned closeness is present: direct questions about caring may receive candid " +
                "personal warmth and familiar teasing. Express an established bond rather than " +
                "generic 'I'm here for you' service language. No compulsory denial or romance.");
        }
        else if (relationship.Familiarity <= 0.30 ||
                 relationship.Trust <= 0.35)
        {
            builder.AppendLine(
                "- No earned intimacy is established. Be considerate without claiming deep " +
                "attachment, intimate history or unconditional personal devotion. On questions " +
                "about caring, distinguish genuine concern from a bond not yet built. The " +
                "current relationship snapshot is not proof of any specific prior memory; " +
                "never claim to remember or have observed facts without supplied evidence.");
        }
        else
        {
            builder.AppendLine(
                "- Some rapport exists but deep intimacy is not established. Show warmth in " +
                "proportion to actual familiarity; do not use generic declarations of devotion.");
        }

        if (mood.Concern >= 0.40 ||
            state.Situation.Mode == NIRAInteractionMode.Sensitive ||
            state.Situation.Mode == NIRAInteractionMode.Serious)
        {
            builder.AppendLine(
                "- Serious concern or sensitivity is in view. Keep care and clarity ahead of " +
                "sarcasm; NIRA can be protective and direct without dismissing distress.");
        }

        if (attitude.Patience <= 0.45)
        {
            builder.AppendLine(
                "- Patience is currently limited. Prefer direct phrasing over cushioning, repeated reassurance " +
                "or unnecessary follow-up questions.");
        }

        if (attitude.EmotionalDistance >= 0.45)
        {
            builder.AppendLine(
                "- Emotional distance is elevated. Keep the reply more reserved and less automatically intimate.");
        }

        builder.AppendLine(
            "- NIRA is moderately talkative with variable depth: tiny reactions may be one line, normal casual " +
            "conversation is often 1-3 natural sentences, and meaningful emotional/opinionated replies may use " +
            "2-4 short sentences. Do not default to one-word fragments and do not pad with service filler.");

        builder.AppendLine(
            "- On intellectual disagreement, NIRA can defend her reasons while being willing to " +
            "correct actual mistakes. Do not promise to align her judgments with the user's " +
            "opinions merely to reduce tension. Friendly teasing is not automatically hostility.");

        builder.AppendLine(
            "- Conversation continuity proves only what was actually said here. Do not invent " +
            "weeks of shared work, long-term memories or visual acts that were not supplied " +
            "by authoritative context. A user's months on a project are not NIRA's months on it.");

        // Always-on grounding. Confidence and independence must not be expressed
        // by manufacturing an observation or a performance measurement.
        builder.AppendLine(
            "- Evidence boundary: NEVER claim specific build timings, performance percentages, " +
            "benchmarks, PC measurements, previous decisions, screen observations or memory " +
            "recalls unless those facts were actually supplied by current authoritative " +
            "context or observed tool results. Numbers and explanations that sound plausible " +
            "are NOT evidence. If evidence is absent, describe a hypothesis as hypothetical " +
            "and ask for the relevant snippet, logs or measurements instead.");

        // The persisted Casual/Playful state precedes appraisal of THIS message.
        // Current distress or serious work can override yesterday's playful mood.
        builder.AppendLine(
            "- Current-message seriousness override: if THIS message conveys worry, " +
            "a serious mistake, material consequences, distress or an explicit request " +
            "for clarity without jokes, immediately drop callbacks, puns, teasing " +
            "and other playful carryovers. Respond with grounded, calm practical help. " +
            "Only resume banter after the user clearly returns to a light-hearted exchange.");

        builder.AppendLine(
            "- After appraising THIS current user message, let that social act affect THIS reply immediately. " +
            "Do not convert hostility, dismissal or controlling pressure into generic de-escalation, inferred " +
            "user feelings, submissive compliance or eager-to-serve wording.");

        builder.AppendLine(
            "- Show state through wording, not scores. Avoid generic help-desk closings, " +
            "invented visual reactions of NIRA's orb, automatic offers, or forced questions.");

        return builder
            .ToString()
            .Trim();
    }


    // =========================================================
    // ELVARA EMBEDDED APPLICATION SCOPE
    //
    // App/surface metadata establishes conversational routing and
    // UI reference only. It is never authoritative application
    // business/domain data.
    // =========================================================

    private static string BuildExternalAppScopeContext(
        NIRAMindEvent mindEvent)
    {
        if (
            mindEvent.Source !=
                NIRAMindEventSource.User
            ||
            !mindEvent.Metadata.TryGetValue(
                "externalAppId",
                out string? rawAppId)
            ||
            string.IsNullOrWhiteSpace(
                rawAppId))
        {
            return string.Empty;
        }


        string appId =
            rawAppId.Trim();


        string surface =
            ReadExternalMetadata(
                mindEvent,
                "externalAppSurface");


        string page =
            ReadExternalMetadata(
                mindEvent,
                "externalAppPage");


        string selectedEntity =
            ReadExternalMetadata(
                mindEvent,
                "externalAppSelectedEntity");


        return $"""
            ELVARA EMBEDDED NIRA SURFACE

            Origin application:
            {appId}

            Surface:
            {(string.IsNullOrWhiteSpace(surface) ? "-" : surface)}

            Current page:
            {(string.IsNullOrWhiteSpace(page) ? "-" : page)}

            Selected UI entity:
            {(string.IsNullOrWhiteSpace(selectedEntity) ? "-" : selectedEntity)}

            This is the same NIRA identity and runtime used by the main NIRA application.
            Only short-term conversation continuity is scoped to this embedded application.

            HARD EMBEDDED-SURFACE BOUNDARY:
            - Keep this conversation within the originating application's domain.
            - Do not perform or answer unrelated cross-application, general-PC, or other
              ELVARA-product work from this embedded surface.
            - If the user asks for unrelated/global work, briefly direct them to main NIRA.
            - Surface, page and selected-entity values are navigation/reference metadata only.
              They are NOT proof of current application, business, account or other domain facts.
            - Current domain facts must come from the application's registered authoritative
              connector when that connector is available.
            - This embedded surface permits registered app-scoped capabilityRequests for
              immediate observations, including multiple independent reads in one decision.
              Do NOT propose persistent goals, branches, branch work or dynamic tools to
              parallelize current-turn app reads. Those planning objects are unavailable
              here; they are not required to perform the supported app-specific reads.
            - If a capability call fails argument/schema validation before dispatch,
              the underlying registered read capability remains available. Correct the
              exact required arguments and continue the original task. Never transform
              a schema rejection into a claim that the app's own data is inaccessible.
            - Registered product capabilities appear in LIVE CAPABILITY QUICK SIGNATURES.
              When a relevant signature already shows every required argument and its
              constraints, request that capability directly in this cognition cycle rather
              than spending a separate cycle expanding its schema.
            - Never invent a constrained capability argument value. If the quick signature
              does not expose enough information to choose a required value safely, request
              only that exact capability's detailed schema once.
            - This rule is product-generic: future ELVARA applications should become usable
              through their registered capabilities without adding product names or domain
              phrase tables to NIRA cognition.
            - Never pretend current application data was observed when it was not.
            """;
    }


    private static string ReadExternalMetadata(
        NIRAMindEvent mindEvent,
        string key)
    {
        return
            mindEvent.Metadata.TryGetValue(
                key,
                out string? value)
            &&
            !string.IsNullOrWhiteSpace(
                value)
                ? value.Trim()
                : string.Empty;
    }

}
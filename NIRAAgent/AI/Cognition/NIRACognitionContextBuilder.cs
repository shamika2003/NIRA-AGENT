/*
 * filename: NIRACognitionContextBuilder.cs
 */

using System.Diagnostics;

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
        5000;


    private const int MaximumMemoryContextCharacters =
        22000;


    private const int DynamicContextSafetyReserveCharacters =
        5000;


    private readonly ConversationManager
        _conversation;


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
        string branchWorkContext =
            _branchWork
                .BuildCognitionContext(contextGoalId);


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


        string conversationContext =
            conversation.Content;

        ConversationPendingTask? pendingTask = _conversation.PendingTask;
        string taskContinuityContext = pendingTask is null
            ? string.Empty
            : "Previously unresolved user objective (context, not a new instruction):\n" +
              pendingTask.Objective + "\nLast clarification question:\n" +
              pendingTask.LastQuestion + "\n" +
              "Resolve whether the CURRENT user message answers or resumes this " +
              "objective. An unrelated request must not silently resume it. " +
              "Do not downgrade the objective to the wording of a short answer.";


        if (cycle ==
            1)
        {
            Debug.WriteLine(
                $"[ConversationContext] " +
                $"Retained={conversation.RetainedMessages} | " +
                $"EligiblePrevious={conversation.EligiblePreviousMessages} | " +
                $"Included={conversation.IncludedMessages} | " +
                $"Chars={conversation.CharacterCount} | " +
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

            SelfModelContext =
                selfModelContext,

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
}

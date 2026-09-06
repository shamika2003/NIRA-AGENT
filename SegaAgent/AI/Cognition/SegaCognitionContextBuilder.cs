/*
 * filename: SegaCognitionContextBuilder.cs
 */

using System.Diagnostics;

using SegaAgent.Character;
using SegaAgent.Capabilities;
using SegaAgent.Branches;
using SegaAgent.Character.History;
using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;
using SegaAgent.Conversation;
using SegaAgent.Memory.LongTerm;
using SegaAgent.Goals;
using SegaAgent.Mind;
using SegaAgent.PC.Awareness;
using SegaAgent.Self.Model;
using SegaAgent.Tools;
using SegaAgent.Temporal;

namespace SegaAgent.AI.Cognition;

public sealed class SegaCognitionContextBuilder
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


    private readonly SegaCharacterStateService
        _characterState;


    private readonly SegaAttitudeService
        _attitude;


    private readonly SegaSocialHistoryService
        _socialHistory;


    private readonly SegaMemoryContextService
        _memoryContext;


    private readonly SegaSelfModelService
        _selfModel;


    private readonly SegaGoalService
        _goals;


    private readonly SegaBranchService
        _branches;


    private readonly SegaBranchWorkService
        _branchWork;


    private readonly SegaCapabilityService
        _capabilities;


    private readonly SegaDynamicToolService
        _dynamicTools;


    private readonly SegaTemporalContextService
        _temporal;


    public SegaCognitionContextBuilder(
        ConversationManager conversation,
        PcWorldStateService worldState,
        SegaCharacterStateService characterState,
        SegaAttitudeService attitude,
        SegaSocialHistoryService socialHistory,
        SegaMemoryContextService memoryContext,
        SegaSelfModelService selfModel,
        SegaGoalService goals,
        SegaBranchService branches,
        SegaBranchWorkService branchWork,
        SegaCapabilityService capabilities,
        SegaDynamicToolService dynamicTools,
        SegaTemporalContextService temporal)
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


        _temporal =
            temporal
            ?? throw new ArgumentNullException(
                nameof(temporal));
    }


    public async Task<SegaCognitionContext> BuildAsync(
        Guid runId,
        int cycle,
        SegaMindEvent mindEvent,
        SegaInteractionContext? interaction,
        string executiveEvidence,
        string capabilityEvidence,
        string dynamicToolEvidence,
        string memorySearchEvidence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        SegaCharacterSnapshot character =
            _characterState.Current;


        SegaAttitudeState attitude =
            _attitude.Evaluate(
                character,
                interaction);


        IReadOnlyList<SegaSocialEvent> recentHistory =
            _socialHistory.GetRecent(
                20);


        string characterContext =
            SegaCharacterContextFormatter.Format(
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


        string branchWorkContext =
            _branchWork
                .BuildCognitionContext();


        string capabilityContext =
            _capabilities
                .BuildCognitionContext();


        string dynamicToolContext =
            await _dynamicTools
                .BuildCognitionContextAsync(
                    cancellationToken);


        string pcContext =
            PcContextFormatter.Format(
                _worldState.Current);


        string temporalContext =
            _temporal.BuildCognitionContext();


        // =====================================================
        // BOUNDED WORKING CONVERSATION
        //
        // A user message is inserted into ConversationManager by
        // SegaMindRuntime before cognition begins. The same text
        // is also the CURRENT EVENT, so exclude only that newest
        // matching user message from conversation context.
        //
        // Perception/proactive/internal events do not have this
        // duplication and therefore use the normal bounded window.
        // =====================================================

        ConversationContextSnapshot conversation =
            mindEvent.Source ==
                SegaMindEventSource.User
                ? _conversation.BuildContextSnapshot(
                    currentUserMessageToExclude:
                        mindEvent.Content)
                : _conversation.BuildContextSnapshot();


        string conversationContext =
            conversation.Content;


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


        SegaMemoryContextSnapshot memory =
            await _memoryContext.BuildAsync(
                availableMemoryCharacters,
                cancellationToken);


        return new SegaCognitionContext
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

            PcContext =
                pcContext,

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
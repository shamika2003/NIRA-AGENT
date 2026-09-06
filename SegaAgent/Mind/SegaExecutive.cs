/*
 * filename: SegaExecutive.cs
 */

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using SegaAgent.AI.Cognition;
using SegaAgent.Capabilities;
using SegaAgent.Branches;
using SegaAgent.Character.Appraisal;
using SegaAgent.Character.Dynamics;
using SegaAgent.Character.History;
using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;
using SegaAgent.Conversation;
using SegaAgent.Memory.LongTerm;
using SegaAgent.Goals;
using SegaAgent.Self.Preferences;
using SegaAgent.Self.Model;
using SegaAgent.Voice;
using SegaAgent.Tools;

namespace SegaAgent.Mind;

public sealed class SegaExecutive
{
    private readonly SegaCognitionService
        _cognition;


    private readonly SegaCognitionContextBuilder
        _contextBuilder;


    private readonly SegaInteractionObservationService
        _interactionObservation;


    private readonly SegaCharacterDynamicsService
        _characterDynamics;


    private readonly SegaMemoryConsolidator
        _memoryConsolidator;


    private readonly SegaLongTermMemoryService
        _longTermMemory;


    private readonly SegaMemoryAssociationService
        _memoryAssociations;


    private readonly SegaMemoryFormationService
        _memoryFormation;


    private readonly SegaSelfPreferenceService
        _selfPreferences;


    private readonly SegaSelfPreferenceMemorySyncService
        _selfPreferenceMemorySync;


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


    private readonly SegaVoiceExpressionService
        _voiceExpression;


    private readonly ConversationManager
        _conversation;


    private readonly SegaSocialHistoryService
        _socialHistory;


    public SegaExecutive(
        SegaCognitionService cognition,
        SegaCognitionContextBuilder contextBuilder,
        SegaInteractionObservationService interactionObservation,
        SegaCharacterDynamicsService characterDynamics,
        SegaMemoryConsolidator memoryConsolidator,
        SegaLongTermMemoryService longTermMemory,
        SegaMemoryAssociationService memoryAssociations,
        SegaMemoryFormationService memoryFormation,
        SegaSelfPreferenceService selfPreferences,
        SegaSelfPreferenceMemorySyncService selfPreferenceMemorySync,
        SegaSelfModelService selfModel,
        SegaGoalService goals,
        SegaBranchService branches,
        SegaBranchWorkService branchWork,
        SegaCapabilityService capabilities,
        SegaDynamicToolService dynamicTools,
        SegaVoiceExpressionService voiceExpression,
        ConversationManager conversation,
        SegaSocialHistoryService socialHistory)
    {
        _cognition =
            cognition
            ?? throw new ArgumentNullException(
                nameof(cognition));


        _contextBuilder =
            contextBuilder
            ?? throw new ArgumentNullException(
                nameof(contextBuilder));


        _interactionObservation =
            interactionObservation
            ?? throw new ArgumentNullException(
                nameof(interactionObservation));


        _characterDynamics =
            characterDynamics
            ?? throw new ArgumentNullException(
                nameof(characterDynamics));


        _memoryConsolidator =
            memoryConsolidator
            ?? throw new ArgumentNullException(
                nameof(memoryConsolidator));


        _longTermMemory =
            longTermMemory
            ?? throw new ArgumentNullException(
                nameof(longTermMemory));


        _memoryAssociations =
            memoryAssociations
            ?? throw new ArgumentNullException(
                nameof(memoryAssociations));


        _memoryFormation =
            memoryFormation
            ?? throw new ArgumentNullException(
                nameof(memoryFormation));


        _selfPreferences =
            selfPreferences
            ?? throw new ArgumentNullException(
                nameof(selfPreferences));


        _selfPreferenceMemorySync =
            selfPreferenceMemorySync
            ?? throw new ArgumentNullException(
                nameof(selfPreferenceMemorySync));


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


        _voiceExpression =
            voiceExpression
            ?? throw new ArgumentNullException(
                nameof(voiceExpression));


        _conversation =
            conversation
            ?? throw new ArgumentNullException(
                nameof(conversation));


        _socialHistory =
            socialHistory
            ?? throw new ArgumentNullException(
                nameof(socialHistory));
    }


    public async IAsyncEnumerable<SegaOutputChunk> RunAsync(
        SegaMindEvent mindEvent,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        Guid runId =
            Guid.NewGuid();


        if (TryGetStaleOwnedInternalEventReason(
                mindEvent,
                out string initialStaleReason))
        {
            Debug.WriteLine(
                $"[Executive] Run={runId} | STALE INTERNAL EVENT DROPPED | " +
                $"Event={mindEvent.Name} | Reason='{TrimLog(initialStaleReason)}'");

            yield return new SegaOutputChunk
            {
                RunId =
                    runId,

                Source =
                    mindEvent.Source,

                Type =
                    SegaOutputChunkType.Completed,

                Content =
                    string.Empty,

                VoiceExpression =
                    SegaVoiceExpression.Neutral
            };

            yield break;
        }


        SegaInteractionContext? interaction =
            ResolveInteractionContext(
                mindEvent);


        StringBuilder executiveEvidence =
            new();


        StringBuilder capabilityEvidence =
            new();


        StringBuilder dynamicToolEvidence =
            new();


        StringBuilder memorySearchEvidence =
            new();


        bool initialStateApplied =
            false;


        int cycle =
            0;


        int noProgressCycles =
            0;


        HashSet<string> executedMemorySearches =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<Guid> surfacedMemoryIds =
            new();


        HashSet<string> executedGoalProposals =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> executedBranchProposals =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> executedBranchWorkProposals =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> executedCapabilityRequests =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> executedDynamicToolProposals =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> executedDynamicToolInvocations =
            new(
                StringComparer.OrdinalIgnoreCase);


        Dictionary<string, SegaCapabilityResult> capabilityResultsBySignature =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> reportedDuplicateCapabilityRequests =
            new(
                StringComparer.OrdinalIgnoreCase);


        SegaCapabilityResult? lastCapabilityResult =
            null;


        SegaCognitionContext? latestContext =
            null;


        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();


            cycle++;


            SegaCognitionContext context =
                await _contextBuilder.BuildAsync(
                    runId,
                    cycle,
                    mindEvent,
                    interaction,
                    executiveEvidence.ToString(),
                    capabilityEvidence.ToString(),
                    dynamicToolEvidence.ToString(),
                    memorySearchEvidence.ToString(),
                    cancellationToken);


            latestContext =
                context;


            SegaCognitionDecision decision =
                await _cognition.ThinkAsync(
                    context,
                    cancellationToken);


            // Internal result/timer cognition may have been overtaken while
            // the LLM was thinking (for example the user cancelled the goal).
            // Revalidate ownership before applying any model proposal.
            if (TryGetStaleOwnedInternalEventReason(
                    mindEvent,
                    out string postThinkStaleReason))
            {
                Debug.WriteLine(
                    $"[Executive] Run={runId} | Cycle={cycle} | " +
                    $"STALE AFTER THINK -> DISCARD | Event={mindEvent.Name} | " +
                    $"Reason='{TrimLog(postThinkStaleReason)}'");

                yield return new SegaOutputChunk
                {
                    RunId =
                        runId,

                    Source =
                        mindEvent.Source,

                    Type =
                        SegaOutputChunkType.Completed,

                    Content =
                        string.Empty,

                    VoiceExpression =
                        SegaVoiceExpression.Neutral
                };

                yield break;
            }


            Debug.WriteLine(
                $"[Executive] Run={runId} | " +
                $"Cycle={cycle} | " +
                $"State={decision.State} | " +
                $"MemorySearches={decision.MemorySearches.Count} | " +
                $"GoalProposals={decision.GoalProposals.Count} | " +
                $"BranchProposals={decision.BranchProposals.Count} | " +
                $"BranchWorkProposals={decision.BranchWorkProposals.Count} | " +
                $"CapabilityRequests={decision.CapabilityRequests.Count} | " +
                $"ToolProposals={decision.DynamicToolProposals.Count} | " +
                $"ToolInvocations={decision.DynamicToolInvocations.Count} | " +
                $"Summary='{TrimLog(decision.DecisionSummary)}'");


            SegaVoiceExpression expression =
                SegaVoiceExpression.Neutral;


            if (!initialStateApplied)
            {
                initialStateApplied =
                    true;


                ApplyFirstCycleCharacterState(
                    interaction,
                    decision);


                expression =
                    _voiceExpression.Resolve(
                        decision.VocalIntent,
                        interaction);
            }
            else
            {
                expression =
                    _voiceExpression.Resolve(
                        decision.VocalIntent,
                        interaction);
            }


            SegaGoalProposal[] newGoalProposals =
                decision.GoalProposals
                    .Select(
                        proposal =>
                            proposal.Normalize())
                    .Where(
                        proposal =>
                            executedGoalProposals.Add(
                                proposal.BuildSignature()))
                    .ToArray();


            IReadOnlyList<SegaGoalApplyResult> goalResults =
                await _goals.ApplyProposalsAsync(
                    mindEvent,
                    decision.Reply ?? string.Empty,
                    capabilityEvidence.ToString(),
                    newGoalProposals,
                    cancellationToken);


            int goalChanges =
                goalResults.Count(
                    result =>
                        result.Changed);


            // =====================================================
            // HIERARCHICAL GOAL RESOLUTION
            //
            // A Completed or Cancelled persistent goal is the authoritative
            // lifetime boundary for every child branch/work item. Branches
            // are work baskets, not independent intentions. Once the parent
            // intention resolves, remaining queued/running child work is
            // obsolete and must not wake Sega later to reconstruct it.
            // =====================================================

            int cascadedBranchCancellations =
                0;

            SegaGoalState[] resolvedGoals =
                goalResults
                    .Where(
                        result =>
                            result.Changed
                            &&
                            result.Goal?.Status is
                                SegaGoalStatus.Completed
                                or SegaGoalStatus.Cancelled)
                    .Select(
                        result =>
                            result.Goal!)
                    .ToArray();

            foreach (SegaGoalState resolvedGoal in resolvedGoals)
            {
                string resolutionReason =
                    resolvedGoal.Status == SegaGoalStatus.Completed
                        ? $"Parent goal completed; remaining child work is obsolete: {resolvedGoal.Objective}"
                        : $"Parent goal was cancelled: {resolvedGoal.Objective}";

                IReadOnlyList<SegaBranchState> cancelledBranches =
                    await _branches.CancelOpenForGoalAsync(
                        resolvedGoal.Id,
                        mindEvent,
                        resolutionReason,
                        cancellationToken);

                cascadedBranchCancellations +=
                    cancelledBranches.Count;

                if (cancelledBranches.Count > 0)
                {
                    executiveEvidence.AppendLine(
                        $"[AUTHORITATIVE GOAL RESOLUTION] Goal {resolvedGoal.Id:D} " +
                        $"resolved as {resolvedGoal.Status} and {cancelledBranches.Count} open branch(es) " +
                        "were cascade-cancelled. A resolved goal is the terminal lifetime boundary; " +
                        "stale branch/tool/result events may not continue or recreate its work.");
                }
            }


            SegaBranchProposal[] newBranchProposals =
                decision.BranchProposals
                    .Select(
                        proposal =>
                            proposal.Normalize())
                    .Where(
                        proposal =>
                            executedBranchProposals.Add(
                                proposal.BuildSignature()))
                    .ToArray();


            IReadOnlyList<SegaBranchApplyResult> branchResults =
                await _branches.ApplyProposalsAsync(
                    mindEvent,
                    decision.Reply ?? string.Empty,
                    capabilityEvidence.ToString(),
                    newBranchProposals,
                    cancellationToken);


            int branchChanges =
                branchResults.Count(
                    result =>
                        result.Changed)
                +
                cascadedBranchCancellations;


            SegaDynamicToolProposal[] newDynamicToolProposals =
                decision.DynamicToolProposals
                    .Select(proposal => proposal.Normalize())
                    .Where(proposal => executedDynamicToolProposals.Add(proposal.BuildSignature()))
                    .ToArray();

            IReadOnlyList<SegaDynamicToolMutationResult> dynamicToolMutations =
                await _dynamicTools.ApplyProposalsAsync(
                    newDynamicToolProposals,
                    cancellationToken);

            int dynamicToolChanges = dynamicToolMutations.Count(result => result.Changed);
            int newDynamicToolEvidenceCount =
                AppendDynamicToolMutationEvidence(dynamicToolEvidence, dynamicToolMutations);


            SegaBranchWorkProposal[] newBranchWorkProposals =
                decision.BranchWorkProposals
                    .Select(
                        proposal =>
                            proposal.Normalize())
                    .Where(
                        proposal =>
                            executedBranchWorkProposals.Add(
                                proposal.BuildSignature()))
                    .ToArray();


            IReadOnlyList<SegaBranchWorkApplyResult> branchWorkResults =
                await _branchWork.ApplyProposalsAsync(
                    mindEvent,
                    newBranchWorkProposals,
                    cancellationToken);


            int branchWorkChanges =
                branchWorkResults.Count(
                    result =>
                        result.Changed);


            int newBranchWorkEvidenceCount =
                AppendBranchWorkMutationEvidence(
                    executiveEvidence,
                    branchWorkResults);


            // =====================================================
            // BRANCH OWNERSHIP ROUTING
            //
            // If Sega assigned an exact bounded capability/tool
            // invocation to a branch in this same decision, do not
            // also dispatch the identical request as top-level work.
            // The branch work item is the authoritative owner.
            // =====================================================

            HashSet<string> branchOwnedCapabilitySignatures =
                new(
                    StringComparer.OrdinalIgnoreCase);

            HashSet<string> branchOwnedDynamicToolSignatures =
                new(
                    StringComparer.OrdinalIgnoreCase);

            int branchWorkPairCount =
                Math.Min(
                    newBranchWorkProposals.Length,
                    branchWorkResults.Count);

            for (
                int index = 0;
                index < branchWorkPairCount;
                index++)
            {
                SegaBranchWorkApplyResult result =
                    branchWorkResults[index];

                if (result.Action ==
                    SegaBranchWorkApplyAction.Rejected)
                {
                    continue;
                }

                SegaBranchWorkProposal proposal =
                    newBranchWorkProposals[index];

                if (proposal.Kind ==
                    SegaBranchWorkKind.Capability)
                {
                    branchOwnedCapabilitySignatures.Add(
                        proposal.CapabilityRequest!
                            .BuildSignature());
                }
                else if (proposal.Kind ==
                    SegaBranchWorkKind.DynamicTool)
                {
                    branchOwnedDynamicToolSignatures.Add(
                        proposal.DynamicToolInvocation!
                            .BuildSignature());
                }
            }


            bool internalBranchLifecycleEvent =
                IsInternalBranchLifecycleEvent(
                    mindEvent);

            bool ownershipIdCommittedThisCycle =
                goalResults.Any(
                    result =>
                        result.Action == SegaGoalApplyAction.Created)
                ||
                branchResults.Any(
                    result =>
                        result.Action == SegaBranchApplyAction.Created);

            bool topLevelMachineActionsDisallowed =
                internalBranchLifecycleEvent
                ||
                ownershipIdCommittedThisCycle;


            int suppressedTopLevelToolInvocations =
                0;

            SegaDynamicToolInvocation[] newDynamicToolInvocations =
                decision.DynamicToolInvocations
                    .Select(
                        invocation =>
                            invocation.Normalize())
                    .Where(
                        invocation =>
                        {
                            string signature =
                                invocation.BuildSignature();

                            if (topLevelMachineActionsDisallowed)
                            {
                                suppressedTopLevelToolInvocations++;
                                executedDynamicToolInvocations.Add(
                                    signature);
                                return false;
                            }

                            if (branchOwnedDynamicToolSignatures.Contains(
                                    signature))
                            {
                                suppressedTopLevelToolInvocations++;
                                return false;
                            }

                            return executedDynamicToolInvocations.Add(
                                signature);
                        })
                    .ToArray();

            IReadOnlyList<SegaDynamicToolExecutionResult> dynamicToolExecutions =
                await _dynamicTools.ExecuteAsync(
                    newDynamicToolInvocations,
                    cancellationToken);

            newDynamicToolEvidenceCount +=
                AppendDynamicToolExecutionEvidence(dynamicToolEvidence, dynamicToolExecutions);

            List<SegaCapabilityResult> dynamicToolCapabilityResults =
                dynamicToolExecutions
                    .SelectMany(result => result.CapabilityResults)
                    .ToList();

            foreach (SegaDynamicToolExecutionResult toolResult in dynamicToolExecutions)
            {
                foreach (SegaDynamicToolStepExecutionResult stepResult in toolResult.StepResults)
                {
                    if (stepResult.Request == null || stepResult.CapabilityResult == null) continue;
                    string signature = stepResult.Request.BuildSignature();
                    executedCapabilityRequests.Add(signature);
                    capabilityResultsBySignature[signature] = stepResult.CapabilityResult;
                    lastCapabilityResult = stepResult.CapabilityResult;
                }
            }

            int dynamicToolCapabilityEvidenceCount =
                AppendCapabilityEvidence(
                    capabilityEvidence,
                    dynamicToolCapabilityResults);


            int suppressedTopLevelCapabilities =
                0;

            SegaCapabilityRequest[] requestedCapabilities =
                decision.CapabilityRequests
                    .Select(
                        request =>
                            request.Normalize())
                    .Where(
                        request =>
                        {
                            string signature =
                                request.BuildSignature();

                            if (topLevelMachineActionsDisallowed)
                            {
                                suppressedTopLevelCapabilities++;
                                executedCapabilityRequests.Add(
                                    signature);
                                return false;
                            }

                            if (branchOwnedCapabilitySignatures.Contains(
                                    signature))
                            {
                                suppressedTopLevelCapabilities++;
                                return false;
                            }

                            return true;
                        })
                    .ToArray();


            if (
                suppressedTopLevelCapabilities > 0
                ||
                suppressedTopLevelToolInvocations > 0)
            {
                executiveEvidence.AppendLine();
                executiveEvidence.AppendLine(
                    "EXECUTIVE BRANCH OWNERSHIP ROUTING");
                executiveEvidence.AppendLine(
                    $"Suppressed duplicate top-level capability requests: {suppressedTopLevelCapabilities}");
                executiveEvidence.AppendLine(
                    $"Suppressed duplicate top-level dynamic-tool invocations: {suppressedTopLevelToolInvocations}");
                executiveEvidence.AppendLine(
                    internalBranchLifecycleEvent
                        ? "This is an internal branch lifecycle event. Direct top-level machine actions are not allowed to escape the task graph here. Assign executable work to an open branch through branchWorkProposals and wait for its authoritative result."
                        : ownershipIdCommittedThisCycle
                            ? "A new authoritative goal/branch ID was committed in this cycle. Do not execute top-level machine actions beside that new ownership state. Continue, inspect the committed graph, and assign the bounded work through the correct branch on the next cycle."
                            : "The identical bounded work is already owned by an accepted branch-work assignment. Let the branch runtime execute it and wait for PersistentBranchWorkResult instead of dispatching it twice.");

                Debug.WriteLine(
                    $"[Executive] BRANCH OWNERSHIP ROUTING | " +
                    $"CapabilitiesSuppressed={suppressedTopLevelCapabilities} | " +
                    $"ToolsSuppressed={suppressedTopLevelToolInvocations}");
            }


            List<SegaCapabilityRequest> dispatchCapabilities =
                new();


            foreach (SegaCapabilityRequest request in requestedCapabilities)
            {
                string signature =
                    request.BuildSignature();


                if (executedCapabilityRequests.Add(
                        signature))
                {
                    dispatchCapabilities.Add(
                        request);


                    continue;
                }


                if (!reportedDuplicateCapabilityRequests.Add(
                        signature))
                {
                    continue;
                }


                capabilityResultsBySignature.TryGetValue(
                    signature,
                    out SegaCapabilityResult? previousResult);


                AppendRepeatedCapabilityRequestEvidence(
                    capabilityEvidence,
                    request,
                    previousResult);
            }


            SegaCapabilityRequest[] newCapabilityRequests =
                dispatchCapabilities.ToArray();


            IReadOnlyList<SegaCapabilityResult> capabilityResults =
                await _capabilities.ExecuteAsync(
                    newCapabilityRequests,
                    cancellationToken);


            for (
                int index = 0;
                index < capabilityResults.Count &&
                index < newCapabilityRequests.Length;
                index++)
            {
                string signature =
                    newCapabilityRequests[index]
                        .BuildSignature();


                SegaCapabilityResult result =
                    capabilityResults[index];


                capabilityResultsBySignature[signature] =
                    result;


                lastCapabilityResult =
                    result;
            }


            int newCapabilityEvidenceCount =
                dynamicToolCapabilityEvidenceCount +
                AppendCapabilityEvidence(
                    capabilityEvidence,
                    capabilityResults);


            int newExecutiveEvidenceCount =
                newBranchWorkEvidenceCount
                +
                AppendExecutiveMutationEvidence(
                    executiveEvidence,
                    goalResults,
                    branchResults);


            bool mutationRejected =
                goalResults.Any(
                    result =>
                        result.Action ==
                            SegaGoalApplyAction.Rejected)
                ||
                branchResults.Any(
                    result =>
                        result.Action ==
                            SegaBranchApplyAction.Rejected)
                ||
                branchWorkResults.Any(
                    result =>
                        result.Action ==
                            SegaBranchWorkApplyAction.Rejected)
                ||
                dynamicToolMutations.Any(
                    result =>
                        result.Action ==
                            SegaDynamicToolApplyAction.Rejected);


            bool ownershipRoutingCorrection =
                topLevelMachineActionsDisallowed
                &&
                (
                    suppressedTopLevelCapabilities > 0
                    ||
                    suppressedTopLevelToolInvocations > 0
                );


            bool continueForRuntimeCorrection =
                mutationRejected
                ||
                ownershipRoutingCorrection;


            bool continueForCapabilityResults =
                capabilityResults.Count >
                0
                ||
                dynamicToolCapabilityResults.Count >
                0;


            bool continueForDynamicToolResults =
                dynamicToolMutations.Count >
                0
                ||
                dynamicToolExecutions.Count >
                0;


            if (continueForRuntimeCorrection)
            {
                executiveEvidence.AppendLine();
                executiveEvidence.AppendLine(
                    ownershipRoutingCorrection
                        ? "The runtime rejected direct machine-action routing from an internal branch lifecycle event. Reconsider using authoritative branch-owned work instead of repeating the top-level request."
                        : "One or more proposed executive mutations were rejected. Reconsider the proposal using the authoritative result above before emitting a success claim.");
            }


            if (decision.State ==
                    SegaCognitionState.Continue
                ||
                continueForRuntimeCorrection
                ||
                continueForCapabilityResults
                ||
                continueForDynamicToolResults)
            {
                SegaMemorySearchRequest[] newSearches =
                    decision.MemorySearches
                        .Select(
                            request =>
                                request.Normalize())
                        .Where(
                            request =>
                            {
                                string signature =
                                    request.BuildSignature();


                                return executedMemorySearches.Add(
                                    signature);
                            })
                        .ToArray();


                int newEvidenceCount =
                    0;


                if (newSearches.Length >
                    0)
                {
                    newEvidenceCount =
                        await AppendMemorySearchEvidenceAsync(
                            memorySearchEvidence,
                            newSearches,
                            surfacedMemoryIds,
                            cancellationToken);
                }


                bool madeProgress =
                    newEvidenceCount >
                        0
                    ||
                    newExecutiveEvidenceCount >
                        0
                    ||
                    newCapabilityEvidenceCount >
                        0
                    ||
                    newDynamicToolEvidenceCount >
                        0
                    ||
                    dynamicToolChanges >
                        0
                    ||
                    goalChanges >
                        0
                    ||
                    branchChanges >
                        0
                    ||
                    branchWorkChanges >
                        0;


                if (!madeProgress)
                {
                    noProgressCycles++;


                    memorySearchEvidence.AppendLine();
                    memorySearchEvidence.AppendLine(
                        "The previous cognition cycle requested continuation but produced no new memory, executive, capability, dynamic-tool, goal, branch-state, or branch-work progress.");
                }
                else
                {
                    noProgressCycles =
                        0;
                }


                bool forceTerminal =
                    noProgressCycles >=
                    2;


                if (forceTerminal)
                {
                    string fallbackReply =
                        BuildNoProgressFallbackReply(
                            decision,
                            lastCapabilityResult);


                    Debug.WriteLine(
                        $"[Executive] Run={runId} | " +
                        $"Cycle={cycle} | " +
                        "NO-PROGRESS GUARD -> BLOCKED");


                    decision =
                        decision with
                        {
                            State =
                                SegaCognitionState.Blocked,

                            EmitReply =
                                true,

                            Reply =
                                fallbackReply,

                            DecisionSummary =
                                "Executive stopped repeated continuation after two cycles without authoritative progress."
                        };
                }


                if (!forceTerminal)
                {
                    continue;
                }
            }

            string reply =
                decision.EmitReply
                    ? decision.Reply.Trim()
                    : string.Empty;


            if (!ShouldEmitReplyForInternalOrchestrationEvent(
                    mindEvent,
                    decision,
                    goalResults,
                    branchResults))
            {
                if (!string.IsNullOrWhiteSpace(
                        reply))
                {
                    Debug.WriteLine(
                        $"[Executive] Run={runId} | Cycle={cycle} | " +
                        $"INTERNAL REPLY SUPPRESSED | Event={mindEvent.Name} | " +
                        $"State={decision.State}");
                }

                reply =
                    string.Empty;
            }


            if (!string.IsNullOrWhiteSpace(
                    reply))
            {
                await RecordResponseAsync(
                    mindEvent,
                    reply);


                yield return new SegaOutputChunk
                {
                    RunId =
                        runId,

                    Source =
                        mindEvent.Source,

                    Type =
                        SegaOutputChunkType.Text,

                    Content =
                        reply,

                    VoiceExpression =
                        expression
                };
            }


            if (latestContext !=
                null
                &&
                ShouldApplyMemoryFormation(
                    mindEvent,
                    goalResults,
                    branchResults))
            {
                await ApplyMemoryFormationAsync(
                    runId,
                    mindEvent,
                    interaction?.Event,
                    latestContext,
                    decision,
                    reply,
                    cancellationToken);
            }
            else if (latestContext != null)
            {
                Debug.WriteLine(
                    $"[MemoryFormation] SKIPPED | Run={runId} | Event='{mindEvent.Name}' | " +
                    "Reason='Low-level executive orchestration event is already persisted in authoritative goal/branch/work state.'");
            }


            yield return new SegaOutputChunk
            {
                RunId =
                    runId,

                Source =
                    mindEvent.Source,

                Type =
                    SegaOutputChunkType.Completed,

                Content =
                    reply,

                VoiceExpression =
                    expression
            };


            yield break;
        }
    }


    private bool TryGetStaleOwnedInternalEventReason(
        SegaMindEvent mindEvent,
        out string reason)
    {
        reason =
            string.Empty;

        if (mindEvent.Source !=
            SegaMindEventSource.Internal)
        {
            return false;
        }

        if (string.Equals(
                mindEvent.Name,
                "PersistentGoalWake",
                StringComparison.Ordinal))
        {
            if (!TryReadMetadataGuid(
                    mindEvent,
                    "goalId",
                    out Guid goalId))
            {
                reason =
                    "Goal wake event is missing its authoritative goal ID.";
                return true;
            }

            if (!_goals.TryGetGoal(
                    goalId,
                    out SegaGoalState? goal)
                ||
                goal == null
                ||
                goal.IsResolved)
            {
                reason =
                    "The goal referenced by this wake event is no longer open.";
                return true;
            }

            return false;
        }

        if (string.Equals(
                mindEvent.Name,
                "PersistentBranchWorkReconsideration",
                StringComparison.Ordinal))
        {
            if (!TryReadMetadataGuid(
                    mindEvent,
                    "goalId",
                    out Guid goalId))
            {
                reason =
                    "Branch-work reconsideration is missing its authoritative goal ID.";
                return true;
            }

            if (!_goals.TryGetGoal(
                    goalId,
                    out SegaGoalState? goal)
                ||
                goal == null
                ||
                goal.IsResolved)
            {
                reason =
                    "The goal referenced by this reconsideration event is no longer open.";
                return true;
            }

            bool stillRunning =
                _branchWork.CurrentWork.Any(
                    item =>
                        item.GoalId == goalId
                        &&
                        item.Status == SegaBranchWorkStatus.Running);

            if (!stillRunning)
            {
                reason =
                    "The work that caused this reconsideration event is no longer running.";
                return true;
            }

            return false;
        }

        if (string.Equals(
                mindEvent.Name,
                "PersistentBranchWorkResult",
                StringComparison.Ordinal))
        {
            if (!TryReadMetadataGuid(
                    mindEvent,
                    "goalId",
                    out Guid goalId)
                ||
                !TryReadMetadataGuid(
                    mindEvent,
                    "branchId",
                    out Guid branchId)
                ||
                !TryReadMetadataGuid(
                    mindEvent,
                    "workId",
                    out Guid workId))
            {
                reason =
                    "Branch-work result event is missing authoritative ownership IDs.";
                return true;
            }

            if (!_goals.TryGetGoal(
                    goalId,
                    out SegaGoalState? goal)
                ||
                goal == null
                ||
                goal.IsResolved)
            {
                reason =
                    "The owning goal was resolved before this work-result cognition could act.";
                return true;
            }

            if (!_branches.TryGetBranch(
                    branchId,
                    out SegaBranchState? branch)
                ||
                branch == null
                ||
                branch.GoalId != goalId
                ||
                branch.IsResolved)
            {
                reason =
                    "The owning branch was resolved or no longer matches this work result.";
                return true;
            }

            if (!_branchWork.TryGetWork(
                    workId,
                    out SegaBranchWorkItem? work)
                ||
                work == null
                ||
                work.BranchId != branchId
                ||
                work.GoalId != goalId
                ||
                !work.IsTerminal)
            {
                reason =
                    "The referenced work item is missing, mismatched, or no longer a valid terminal result.";
                return true;
            }

            return false;
        }

        if (string.Equals(
                mindEvent.Name,
                "PersistentBranchResult",
                StringComparison.Ordinal))
        {
            if (!TryReadMetadataGuid(
                    mindEvent,
                    "goalId",
                    out Guid goalId)
                ||
                !TryReadMetadataGuid(
                    mindEvent,
                    "branchId",
                    out Guid branchId))
            {
                reason =
                    "Branch result event is missing authoritative ownership IDs.";
                return true;
            }

            if (!_goals.TryGetGoal(
                    goalId,
                    out SegaGoalState? goal)
                ||
                goal == null
                ||
                goal.IsResolved)
            {
                reason =
                    "The parent goal is already resolved, so this branch result is cleanup-only.";
                return true;
            }

            if (!_branches.TryGetBranch(
                    branchId,
                    out SegaBranchState? branch)
                ||
                branch == null
                ||
                branch.GoalId != goalId)
            {
                reason =
                    "The branch result no longer matches authoritative branch state.";
                return true;
            }

            return false;
        }

        return false;
    }


    private static bool TryReadMetadataGuid(
        SegaMindEvent mindEvent,
        string key,
        out Guid value)
    {
        value =
            Guid.Empty;

        return mindEvent.Metadata.TryGetValue(
                   key,
                   out string? raw)
               &&
               Guid.TryParse(
                   raw,
                   out value)
               &&
               value != Guid.Empty;
    }


    private static bool IsInternalBranchLifecycleEvent(
        SegaMindEvent mindEvent)
    {
        if (mindEvent.Source !=
            SegaMindEventSource.Internal)
        {
            return false;
        }

        return string.Equals(
                   mindEvent.Name,
                   "PersistentBranchWorkResult",
                   StringComparison.Ordinal)
               ||
               string.Equals(
                   mindEvent.Name,
                   "PersistentBranchResult",
                   StringComparison.Ordinal)
               ||
               string.Equals(
                   mindEvent.Name,
                   "PersistentBranchWorkReconsideration",
                   StringComparison.Ordinal);
    }


    private static bool IsInternalOrchestrationEvent(
        SegaMindEvent mindEvent)
    {
        return IsInternalBranchLifecycleEvent(
                   mindEvent)
               ||
               (
                   mindEvent.Source == SegaMindEventSource.Internal
                   &&
                   string.Equals(
                       mindEvent.Name,
                       "PersistentGoalWake",
                       StringComparison.Ordinal)
               );
    }


    private static bool ShouldEmitReplyForInternalOrchestrationEvent(
        SegaMindEvent mindEvent,
        SegaCognitionDecision decision,
        IReadOnlyList<SegaGoalApplyResult> goalResults,
        IReadOnlyList<SegaBranchApplyResult> branchResults)
    {
        if (!decision.EmitReply)
        {
            return false;
        }

        if (!IsInternalOrchestrationEvent(
                mindEvent))
        {
            return true;
        }

        bool resolvedGoalMilestone =
            goalResults.Any(
                result =>
                    result.Changed
                    &&
                    result.Goal?.Status == SegaGoalStatus.Completed);

        if (resolvedGoalMilestone)
        {
            return true;
        }

        bool persistentAttentionTransition =
            goalResults.Any(
                result =>
                    result.Changed
                    &&
                    result.Goal?.Status is
                        SegaGoalStatus.Waiting
                        or SegaGoalStatus.Blocked)
            ||
            branchResults.Any(
                result =>
                    result.Changed
                    &&
                    result.Branch?.Status is
                        SegaBranchStatus.Waiting
                        or SegaBranchStatus.Blocked);

        if (decision.State is
            SegaCognitionState.NeedUser
            or SegaCognitionState.Blocked)
        {
            return persistentAttentionTransition;
        }

        // Low-level assigned-work/branch bookkeeping should not create a
        // stream of near-identical progress messages. Meaningful completion
        // is announced at the goal boundary; genuine blockers first become
        // persistent Waiting/Blocked state and can then be communicated once.
        return false;
    }


    private static bool ShouldApplyMemoryFormation(
        SegaMindEvent mindEvent,
        IReadOnlyList<SegaGoalApplyResult> goalResults,
        IReadOnlyList<SegaBranchApplyResult> branchResults)
    {
        if (!IsInternalOrchestrationEvent(
                mindEvent))
        {
            return true;
        }

        // Do not teach durable memory/self-preferences from every primitive
        // completion, permission rejection, timer tick, or scheduler result.
        // Authoritative goal/branch/work stores already preserve those facts.
        // A genuinely meaningful task outcome may still become lived experience
        // when the same cognition commits a higher-level terminal milestone.
        bool meaningfulGoalOutcome =
            goalResults.Any(
                result =>
                    result.Changed
                    &&
                    result.Goal?.Status == SegaGoalStatus.Completed);

        bool meaningfulBranchOutcome =
            branchResults.Any(
                result =>
                    result.Changed
                    &&
                    result.Branch?.Status is
                        SegaBranchStatus.Completed
                        or SegaBranchStatus.Failed);

        return meaningfulGoalOutcome
            || meaningfulBranchOutcome;
    }


    private static void AppendRepeatedCapabilityRequestEvidence(
        StringBuilder evidence,
        SegaCapabilityRequest request,
        SegaCapabilityResult? previousResult)
    {
        evidence.AppendLine();
        evidence.AppendLine(
            "EXECUTIVE CAPABILITY RETRY GUARD");
        evidence.AppendLine(
            $"CapabilityId: {request.CapabilityId}");
        evidence.AppendLine(
            "This exact capability request was already attempted during the current cognition run and will not be dispatched again unchanged.");


        if (previousResult !=
            null)
        {
            evidence.AppendLine(
                $"PreviousStatus: {previousResult.Status}");
            evidence.AppendLine(
                $"PreviousRisk: {previousResult.Risk}");
            evidence.AppendLine(
                $"PreviousSummary: {previousResult.Summary}");


            if (!string.IsNullOrWhiteSpace(
                    previousResult.Output))
            {
                evidence.AppendLine(
                    "PreviousOutput:");
                evidence.AppendLine(
                    previousResult.Output);
            }
        }


        evidence.AppendLine(
            "Use the previous authoritative result instead of repeating the same request. If more work is needed, choose a materially different capability request, request genuinely missing user input, or finish with the real blocker.");
    }


    private static string BuildNoProgressFallbackReply(
        SegaCognitionDecision decision,
        SegaCapabilityResult? lastCapabilityResult)
    {
        string proposedReply =
            decision.Reply?.Trim()
            ?? string.Empty;


        if (decision.EmitReply &&
            !string.IsNullOrWhiteSpace(
                proposedReply))
        {
            return proposedReply;
        }


        if (lastCapabilityResult ==
            null)
        {
            return "I couldn't make further progress on that request from the evidence I have, so I stopped instead of repeating the same step.";
        }


        string summary =
            string.IsNullOrWhiteSpace(
                lastCapabilityResult.Summary)
                ? "No additional runtime detail was available."
                : lastCapabilityResult.Summary.Trim();


        return lastCapabilityResult.Status switch
        {
            SegaCapabilityResultStatus.AuthorizationRequired =>
                $"I can't continue that action without authorization. {summary}",

            SegaCapabilityResultStatus.Rejected =>
                $"I couldn't complete that because the last action was rejected before it could run. {summary}",

            SegaCapabilityResultStatus.Failed =>
                $"I couldn't complete that because the last action failed. {summary}",

            SegaCapabilityResultStatus.Succeeded =>
                $"The last action succeeded, but I couldn't determine a reliable next step to finish the rest of the request, so I stopped instead of repeating it. {summary}",

            _ =>
                $"I couldn't make further progress on that request, so I stopped instead of repeating the same step. {summary}"
        };
    }


    private static int AppendCapabilityEvidence(
        StringBuilder evidence,
        IReadOnlyList<SegaCapabilityResult> results)
    {
        int count =
            0;


        foreach (SegaCapabilityResult result in results)
        {
            evidence.AppendLine();
            evidence.AppendLine(
                "AUTHORITATIVE CAPABILITY RESULT");
            evidence.AppendLine(
                $"RequestId: {result.RequestId:D}");
            evidence.AppendLine(
                $"CapabilityId: {result.CapabilityId}");
            evidence.AppendLine(
                $"Risk: {result.Risk}");
            evidence.AppendLine(
                $"Status: {result.Status}");
            evidence.AppendLine(
                $"ChangedSystemState: {result.ChangedSystemState}");
            evidence.AppendLine($"OutcomeUncertain: {result.OutcomeUncertain}");
            evidence.AppendLine($"ExitCode: {result.ExitCode?.ToString() ?? "-"}");
            evidence.AppendLine($"HttpStatusCode: {result.HttpStatusCode?.ToString() ?? "-"}");
            evidence.AppendLine($"AuthorizationScopeId: {result.AuthorizationScopeId?.ToString("D") ?? "-"}");
            evidence.AppendLine($"AuditAttemptId: {result.AuditAttemptId?.ToString("D") ?? "-"}");
            evidence.AppendLine($"AuditRecorded: {result.AuditRecorded}");
            evidence.AppendLine(
                $"Summary: {result.Summary}");


            if (!string.IsNullOrWhiteSpace(
                    result.Output))
            {
                evidence.AppendLine(
                    "Output:");
                evidence.AppendLine(
                    result.Output);
            }


            count++;
        }


        return count;
    }


    private static int AppendDynamicToolMutationEvidence(
        StringBuilder evidence,
        IReadOnlyList<SegaDynamicToolMutationResult> results)
    {
        int count = 0;
        foreach (SegaDynamicToolMutationResult result in results)
        {
            evidence.AppendLine();
            evidence.AppendLine("AUTHORITATIVE DYNAMIC TOOL MUTATION RESULT");
            evidence.AppendLine($"Action: {result.Action}");
            evidence.AppendLine($"ToolId: {result.Tool?.Id.ToString("D") ?? "-"}");
            evidence.AppendLine($"Name: {result.Tool?.Name ?? "-"}");
            evidence.AppendLine($"Version: {result.Tool?.Version.ToString() ?? "-"}");
            evidence.AppendLine($"Persistence: {result.Tool?.Persistence.ToString() ?? "-"}");
            evidence.AppendLine($"Status: {result.Tool?.Status.ToString() ?? "-"}");
            evidence.AppendLine($"Changed: {result.Changed}");
            evidence.AppendLine($"Reason: {result.Reason}");
            count++;
        }
        return count;
    }


    private static int AppendDynamicToolExecutionEvidence(
        StringBuilder evidence,
        IReadOnlyList<SegaDynamicToolExecutionResult> results)
    {
        int count = 0;
        foreach (SegaDynamicToolExecutionResult result in results)
        {
            evidence.AppendLine();
            evidence.AppendLine("AUTHORITATIVE DYNAMIC TOOL EXECUTION RESULT");
            evidence.AppendLine($"ToolId: {result.ToolId:D}");
            evidence.AppendLine($"Name: {result.ToolName}");
            evidence.AppendLine($"Version: {result.ToolVersion}");
            evidence.AppendLine($"Succeeded: {result.Succeeded}");
            evidence.AppendLine($"Aborted: {result.Aborted}");
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
                evidence.AppendLine($"FailureReason: {result.FailureReason}");
            evidence.AppendLine($"Summary: {result.Summary}");
            foreach (SegaDynamicToolStepExecutionResult step in result.StepResults)
            {
                evidence.AppendLine($"Step {step.StepId}: {step.Status} | {step.Summary}");
            }
            count++;
        }
        return count;
    }


    private static int AppendBranchWorkMutationEvidence(
        StringBuilder evidence,
        IReadOnlyList<SegaBranchWorkApplyResult> results)
    {
        int count =
            0;


        foreach (SegaBranchWorkApplyResult result in results)
        {
            evidence.AppendLine();
            evidence.AppendLine(
                "AUTHORITATIVE BRANCH WORK ASSIGNMENT RESULT");
            evidence.AppendLine(
                $"Action: {result.Action}");
            evidence.AppendLine(
                $"WorkId: {result.Work?.Id.ToString("D") ?? "-"}");
            evidence.AppendLine(
                $"BranchId: {result.Work?.BranchId.ToString("D") ?? "-"}");
            evidence.AppendLine(
                $"GoalId: {result.Work?.GoalId.ToString("D") ?? "-"}");
            evidence.AppendLine(
                $"Kind: {result.Work?.Kind.ToString() ?? "-"}");
            evidence.AppendLine(
                $"Status: {result.Work?.Status.ToString() ?? "-"}");
            evidence.AppendLine(
                $"Reason: {result.Reason}");
            count++;
        }


        return count;
    }


    private static int AppendExecutiveMutationEvidence(
        StringBuilder evidence,
        IReadOnlyList<SegaGoalApplyResult> goalResults,
        IReadOnlyList<SegaBranchApplyResult> branchResults)
    {
        int count =
            0;


        foreach (SegaGoalApplyResult result in goalResults)
        {
            evidence.AppendLine();
            evidence.AppendLine(
                "AUTHORITATIVE GOAL MUTATION RESULT");
            evidence.AppendLine(
                $"Action: {result.Action}");
            evidence.AppendLine(
                $"GoalId: {result.Goal?.Id.ToString("D") ?? "-"}");
            evidence.AppendLine(
                $"Status: {result.Goal?.Status.ToString() ?? "-"}");
            evidence.AppendLine(
                $"Objective: {result.Goal?.Objective ?? "-"}");
            evidence.AppendLine(
                $"Reason: {result.Reason}");
            count++;
        }


        foreach (SegaBranchApplyResult result in branchResults)
        {
            evidence.AppendLine();
            evidence.AppendLine(
                "AUTHORITATIVE BRANCH MUTATION RESULT");
            evidence.AppendLine(
                $"Action: {result.Action}");
            evidence.AppendLine(
                $"BranchId: {result.Branch?.Id.ToString("D") ?? "-"}");
            evidence.AppendLine(
                $"GoalId: {result.Branch?.GoalId.ToString("D") ?? "-"}");
            evidence.AppendLine(
                $"Status: {result.Branch?.Status.ToString() ?? "-"}");
            evidence.AppendLine(
                $"JoinPolicy: {result.Branch?.JoinPolicy.ToString() ?? "-"}");
            evidence.AppendLine(
                $"Objective: {result.Branch?.Objective ?? "-"}");
            evidence.AppendLine(
                $"Reason: {result.Reason}");
            count++;
        }


        return count;
    }


    private SegaInteractionContext? ResolveInteractionContext(
        SegaMindEvent mindEvent)
    {
        if (!mindEvent.SocialEventId.HasValue)
        {
            return null;
        }


        return _interactionObservation.GetForEvent(
            mindEvent.SocialEventId.Value);
    }


    private void ApplyFirstCycleCharacterState(
        SegaInteractionContext? interaction,
        SegaCognitionDecision decision)
    {
        if (
            interaction ==
                null
            ||
            decision.Appraisal ==
                null)
        {
            return;
        }


        SegaInteractionAppraisal appraisal =
            GroundAppraisal(
                interaction,
                decision.Appraisal);


        _characterDynamics.Apply(
            interaction,
            appraisal);
    }


    // =========================================================
    // UNIFIED MEMORY FORMATION
    //
    // Foreground cognition no longer forms durable memory.
    //
    // Every completed run is reviewed by one dedicated memory
    // formation reasoner. This prevents competing model paths
    // from storing different semantic interpretations of the
    // same experience.
    // =========================================================

    // =========================================================
    // MEMORY FORMATION
    //
    // The dedicated formation model proposes semantic memories
    // and evidence basis. C# validates the basis and attaches
    // authoritative provenance before consolidation.
    // =========================================================

    private async Task ApplyMemoryFormationAsync(
        Guid runId,
        SegaMindEvent mindEvent,
        SegaSocialEvent? sourceEvent,
        SegaCognitionContext context,
        SegaCognitionDecision finalDecision,
        string finalReply,
        CancellationToken cancellationToken)
    {
        try
        {
            Debug.WriteLine(
                $"[MemoryFormation] START | " +
                $"Run={runId} | " +
                $"Event='{mindEvent.Name}'");


            SegaMemoryFormationResult formation =
                await _memoryFormation.FormAsync(
                    new SegaMemoryFormationContext
                    {
                        RunId =
                            runId,

                        Event =
                            mindEvent,

                        FinalDecisionSummary =
                            finalDecision.DecisionSummary,

                        FinalReply =
                            finalReply,

                        CharacterContext =
                            context.CharacterContext,

                        SelfPreferenceContext =
                            _selfPreferences
                                .BuildFormationContext(),

                        SelfModelContext =
                            _selfModel
                                .BuildFormationContext(),

                        TemporalContext =
                            context.TemporalContext,

                        ConversationContext =
                            context.ConversationContext,

                        LongTermMemoryContext =
                            context.LongTermMemoryContext,

                        MemorySearchEvidence =
                            context.MemorySearchEvidence
                    },
                    cancellationToken);


            int appliedSelfPreferenceObservations =
                await ApplySelfPreferenceFormationObservationsAsync(
                    mindEvent,
                    sourceEvent,
                    formation.SelfPreferenceObservations,
                    cancellationToken);


            int appliedCommitmentChanges =
                await _selfModel.ApplyCommitmentProposalsAsync(
                    mindEvent,
                    finalReply,
                    formation.CommitmentProposals,
                    cancellationToken);


            IReadOnlyList<SegaMemoryCandidate> grounded =
                GroundMemoryFormationProposals(
                    sourceEvent,
                    formation.Proposals);


            if (grounded.Count ==
                0)
            {
                Debug.WriteLine(
                    $"[MemoryFormation] COUNT=0 | " +
                    $"No durable memory accepted from this experience. | " +
                    $"SelfPreferenceApplied={appliedSelfPreferenceObservations} | " +
                    $"CommitmentChanges={appliedCommitmentChanges}");


                return;
            }


            foreach (
                SegaMemoryCandidate candidate
                in grounded)
            {
                Debug.WriteLine(
                    $"[MemoryFormation] CANDIDATE | " +
                    $"Kind={candidate.Kind} | " +
                    $"Source={candidate.Provenance.SourceType} | " +
                    $"Canonical='{candidate.CanonicalKey ?? "-"}' | " +
                    $"Content='{TrimLog(candidate.Content)}'");
            }


            IReadOnlyList<SegaMemoryConsolidationResult> results =
                await _memoryConsolidator.ConsolidateAsync(
                    grounded,
                    cancellationToken);


            await _memoryAssociations.ApplyConsolidationAsync(
                results,
                cancellationToken);


            long activeCount =
                await _memoryConsolidator.CountActiveAsync(
                    cancellationToken);


            Debug.WriteLine(
                $"[MemoryFormation] BATCH COMPLETE | " +
                $"MemoryProposals={grounded.Count} | " +
                $"MemoryResults={results.Count} | " +
                $"SelfPreferenceApplied={appliedSelfPreferenceObservations} | " +
                $"CommitmentChanges={appliedCommitmentChanges} | " +
                $"Active={activeCount}");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[MemoryFormation] ERROR | {ex}");
        }
    }


    // =========================================================
    // SELF-PREFERENCE FORMATION OBSERVATIONS
    //
    // The formation model proposes bounded observations only.
    //
    // SegaMindEvent owns the fresh event identity. The optional
    // social event contributes sequence information when present.
    //
    // The authoritative SegaSelfPreferenceService owns gradual
    // accumulation, duplicate prevention and persistence.
    // =========================================================

    private async Task<int>
        ApplySelfPreferenceFormationObservationsAsync(
            SegaMindEvent mindEvent,
            SegaSocialEvent? sourceEvent,
            IReadOnlyList<SegaSelfPreferenceFormationProposal> proposals,
            CancellationToken cancellationToken)
    {
        if (
            proposals ==
                null
            ||
            proposals.Count ==
                0)
        {
            return 0;
        }


        int applied =
            0;


        foreach (
            SegaSelfPreferenceFormationProposal raw
            in proposals)
        {
            cancellationToken
                .ThrowIfCancellationRequested();


            SegaSelfPreferenceFormationProposal proposal;


            try
            {
                proposal =
                    raw.Normalize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[SelfPreferenceFormation] REJECTED | " +
                    $"Reason='Malformed proposal: {ex.Message}'");


                continue;
            }


            if (proposal.EvidenceBasis !=
                SegaMemoryEvidenceBasis.SegaInference)
            {
                Debug.WriteLine(
                    $"[SelfPreferenceFormation] REJECTED | " +
                    $"Key='{proposal.Key}' | " +
                    $"Basis={proposal.EvidenceBasis} | " +
                    $"Reason='Sega self-preference observations require SegaInference.'");


                continue;
            }


            SegaSelfPreferenceObservation observation =
                new SegaSelfPreferenceObservation
                {
                    Key =
                        proposal.Key,

                    Subject =
                        proposal.Subject,

                    TopicKey =
                        proposal.TopicKey,

                    Affinity =
                        proposal.Affinity,

                    EvidenceStrength =
                        proposal.EvidenceStrength,

                    Confidence =
                        proposal.Confidence,

                    SourceEventId =
                        mindEvent.Id,

                    SourceEventSequence =
                        sourceEvent?.Sequence,

                    SourceTimestamp =
                        sourceEvent?.Timestamp
                        ?? mindEvent.Timestamp,

                    EvidenceSummary =
                        proposal.EvidenceSummary
                };


            SegaSelfPreferenceApplyResult result =
                await _selfPreferences.ApplyObservationAsync(
                    observation,
                    cancellationToken);


            Debug.WriteLine(
                $"[SelfPreferenceFormation] {result.Action.ToString().ToUpperInvariant()} | " +
                $"Key='{proposal.Key}' | " +
                $"Subject='{proposal.Subject}' | " +
                $"Affinity={proposal.Affinity:F2} | " +
                $"Evidence={proposal.EvidenceStrength:F2} | " +
                $"Confidence={proposal.Confidence:F2} | " +
                $"Reason='{TrimLog(result.Reason)}'");


            if (
                result.Preference !=
                    null
                &&
                result.Action is
                    SegaSelfPreferenceApplyAction.Created
                    or SegaSelfPreferenceApplyAction.Updated
                    or SegaSelfPreferenceApplyAction.Established)
            {
                await _selfPreferenceMemorySync.SynchronizeAsync(
                    result.Preference,
                    observation,
                    cancellationToken);


                applied++;
            }
        }


        return applied;
    }


    // =========================================================
    // GROUND DURABLE MEMORY PROPOSALS
    //
    // SegaLearnedPreference is deliberately rejected here.
    // It may only be synchronized from authoritative established
    // self-preference state in the later Step 5D.
    // =========================================================

    private static IReadOnlyList<SegaMemoryCandidate>
        GroundMemoryFormationProposals(
            SegaSocialEvent? sourceEvent,
            IReadOnlyList<SegaMemoryFormationProposal> proposals)
    {
        if (
            proposals ==
                null
            ||
            proposals.Count ==
                0)
        {
            return Array.Empty<SegaMemoryCandidate>();
        }


        List<SegaMemoryCandidate> grounded =
            new(
                proposals.Count);


        foreach (
            SegaMemoryFormationProposal raw
            in proposals)
        {
            SegaMemoryFormationProposal proposal =
                raw.Normalize();


            if (proposal.Kind ==
                SegaMemoryKind.SegaLearnedPreference)
            {
                Debug.WriteLine(
                    $"[MemoryFormation] REJECTED | " +
                    $"Kind={proposal.Kind} | " +
                    $"Reason='Direct SegaLearnedPreference writes are disabled; use gradual self-preference observations.'");


                continue;
            }


            if (!IsEvidenceBasisAllowed(
                    proposal,
                    sourceEvent))
            {
                Debug.WriteLine(
                    $"[MemoryFormation] REJECTED | " +
                    $"Kind={proposal.Kind} | " +
                    $"Basis={proposal.EvidenceBasis} | " +
                    $"Reason='Evidence basis is invalid for this memory kind/event.'");


                continue;
            }


            SegaMemorySourceType sourceType =
                proposal.EvidenceBasis switch
                {
                    SegaMemoryEvidenceBasis.UserExplicit =>
                        SegaMemorySourceType.UserExplicit,

                    SegaMemoryEvidenceBasis.SegaInference =>
                        SegaMemorySourceType.SegaInference,

                    SegaMemoryEvidenceBasis.SharedExperience =>
                        SegaMemorySourceType.SharedExperience,

                    SegaMemoryEvidenceBasis.SystemDerived =>
                        SegaMemorySourceType.SystemDerived,

                    _ =>
                        SegaMemorySourceType.Unknown
                };


            grounded.Add(
                proposal.ToCandidate() with
                {
                    Provenance =
                        new SegaMemoryProvenance
                        {
                            SourceType =
                                sourceType,

                            SourceEventId =
                                sourceEvent?.Id,

                            SourceEventSequence =
                                sourceEvent?.Sequence,

                            SourceTimestamp =
                                sourceEvent?.Timestamp,

                            SourceExcerpt =
                                BuildMemorySourceExcerpt(
                                    sourceEvent?.Content)
                        }
                        .Normalize()
                });
        }


        return grounded;
    }


    private static bool IsEvidenceBasisAllowed(
        SegaMemoryFormationProposal proposal,
        SegaSocialEvent? sourceEvent)
    {
        bool userEvent =
            sourceEvent?.Source ==
                SegaSocialEventSource.User
            &&
            sourceEvent.Kind ==
                SegaSocialEventKind.UserMessage;


        bool nonUserEvent =
            sourceEvent !=
                null
            &&
            sourceEvent.Source !=
                SegaSocialEventSource.User;


        if (
            proposal.EvidenceBasis ==
                SegaMemoryEvidenceBasis.UserExplicit
            &&
            !userEvent)
        {
            return false;
        }


        if (
            proposal.EvidenceBasis ==
                SegaMemoryEvidenceBasis.SystemDerived
            &&
            !nonUserEvent)
        {
            return false;
        }


        return proposal.Kind switch
        {
            SegaMemoryKind.UserFact =>
                proposal.EvidenceBasis ==
                    SegaMemoryEvidenceBasis.UserExplicit,

            SegaMemoryKind.UserPreference =>
                proposal.EvidenceBasis ==
                    SegaMemoryEvidenceBasis.UserExplicit,

            SegaMemoryKind.SegaLearnedPreference =>
                false,

            SegaMemoryKind.SharedExperience =>
                proposal.EvidenceBasis ==
                    SegaMemoryEvidenceBasis.SharedExperience,

            SegaMemoryKind.ImportantEvent =>
                proposal.EvidenceBasis is
                    SegaMemoryEvidenceBasis.UserExplicit
                    or SegaMemoryEvidenceBasis.SharedExperience
                    or SegaMemoryEvidenceBasis.SystemDerived,

            SegaMemoryKind.ProjectKnowledge =>
                true,

            _ =>
                false
        };
    }


    private static SegaInteractionAppraisal GroundAppraisal(
        SegaInteractionContext interaction,
        SegaCognitionAppraisalProposal proposal)
    {
        SegaSocialMeaning meaning =
            new(
                Respect:
                    proposal.Respect,

                Warmth:
                    proposal.Warmth,

                Trust:
                    proposal.Trust,

                Appreciation:
                    proposal.Appreciation,

                Affection:
                    proposal.Affection,

                Playfulness:
                    proposal.Playfulness,

                Hostility:
                    proposal.Hostility,

                Dismissal:
                    proposal.Dismissal,

                Repair:
                    proposal.Repair,

                Concern:
                    proposal.Concern,

                Engagement:
                    proposal.Engagement,

                Pressure:
                    proposal.Pressure);


        return new SegaInteractionAppraisal
        {
            EventId =
                interaction.Event.Id,

            EventSequence =
                interaction.Event.Sequence,

            EventSource =
                interaction.Event.Source,

            EventKind =
                interaction.Event.Kind,

            EventName =
                interaction.Event.EventName,

            TopicKey =
                interaction.Event.TopicKey,

            Meaning =
                meaning,

            Confidence =
                proposal.Confidence,

            Ambiguity =
                proposal.Ambiguity,

            SituationMode =
                proposal.SituationMode,

            SituationIntensity =
                proposal.SituationIntensity,

            Source =
                SegaAppraisalSource.Composite
        }
        .Normalize();
    }


    private async Task<int> AppendMemorySearchEvidenceAsync(
        StringBuilder evidence,
        IReadOnlyList<SegaMemorySearchRequest> requests,
        HashSet<Guid> surfacedMemoryIds,
        CancellationToken cancellationToken)
    {
        int totalNewCandidates =
            0;


        foreach (
            SegaMemorySearchRequest rawRequest
            in requests)
        {
            SegaMemorySearchRequest request =
                rawRequest.Normalize();


            IReadOnlyList<SegaMemorySearchResult> results =
                await _longTermMemory.SearchCandidatesAsync(
                    request,
                    cancellationToken);


            SegaMemorySearchResult[] newResults =
                results
                    .Where(
                        result =>
                            surfacedMemoryIds.Add(
                                result.Memory.Id))
                    .ToArray();


            evidence.AppendLine();
            evidence.AppendLine(
                "MEMORY SEARCH REQUEST");


            evidence.AppendLine(
                $"Query: {request.Query}");


            if (request.Kinds.Count >
                0)
            {
                evidence.AppendLine(
                    $"Kinds: {string.Join(", ", request.Kinds)}");
            }


            if (request.CanonicalKeys.Count >
                0)
            {
                evidence.AppendLine(
                    $"CanonicalKeys: {string.Join(", ", request.CanonicalKeys)}");
            }


            if (request.TopicKeys.Count >
                0)
            {
                evidence.AppendLine(
                    $"TopicKeys: {string.Join(", ", request.TopicKeys)}");
            }


            if (request.Concepts.Count >
                0)
            {
                evidence.AppendLine(
                    $"Concepts: {string.Join(", ", request.Concepts)}");
            }


            if (request.Entities.Count >
                0)
            {
                evidence.AppendLine(
                    $"Entities: {string.Join(", ", request.Entities)}");
            }


            evidence.AppendLine(
                $"ExpandAssociations: {request.ExpandAssociations}");


            if (newResults.Length ==
                0)
            {
                evidence.AppendLine(
                    "This search produced no new candidate memories that were not already shown in this cognition run.");


                continue;
            }


            totalNewCandidates +=
                newResults.Length;


            await _longTermMemory.RecordSurfacedRecallsAsync(
                newResults
                    .Select(
                        result =>
                            result.Memory.Id)
                    .ToArray(),
                cancellationToken);


            evidence.AppendLine(
                SegaCognitionMemoryFormatter.FormatSearchResults(
                    newResults));
        }


        return totalNewCandidates;
    }

    private async Task RecordResponseAsync(
        SegaMindEvent mindEvent,
        string response)
    {
        if (mindEvent.Source ==
            SegaMindEventSource.User)
        {
            _conversation.AddAssistantMessage(
                response);
        }


        _socialHistory.Record(
            SegaSocialEventSource.Sega,
            SegaSocialEventKind.SegaResponse,
            ResolveResponseEventName(
                mindEvent),
            mindEvent.TopicKey,
            response);


        await Task.CompletedTask;
    }


    private static string ResolveResponseEventName(
        SegaMindEvent mindEvent)
    {
        return mindEvent.Source switch
        {
            SegaMindEventSource.User =>
                "UserResponse",

            SegaMindEventSource.Perception =>
                "PerceptionResponse",

            SegaMindEventSource.Proactive =>
                "ProactiveResponse",

            _ =>
                "SegaResponse"
        };
    }


    private static string? BuildMemorySourceExcerpt(
        string? content)
    {
        if (string.IsNullOrWhiteSpace(
                content))
        {
            return null;
        }


        string clean =
            string.Join(
                ' ',
                content.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));


        const int maximumLength =
            500;


        return clean.Length <=
                maximumLength
            ? clean
            : clean[..maximumLength] +
                "...";
    }


    private static string TrimLog(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }


        const int maximumLength =
            140;


        string clean =
            value.Trim();


        return clean.Length <=
                maximumLength
            ? clean
            : clean[..maximumLength] +
                "...";
    }
}

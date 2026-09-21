/*
 * filename: NIRAExecutive.cs
 */

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using NIRAAgent.AI.Cognition;
using NIRAAgent.Capabilities;
using NIRAAgent.Branches;
using NIRAAgent.Character.Appraisal;
using NIRAAgent.Character.Dynamics;
using NIRAAgent.Character.History;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;
using NIRAAgent.Conversation;
using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Goals;
using NIRAAgent.Self.Preferences;
using NIRAAgent.Self.Model;
using NIRAAgent.Voice;
using NIRAAgent.Tools;
using NIRAAgent.Skills;
using NIRAAgent.Artifacts;
using NIRAAgent.Authorization;
using NIRAAgent.Browser;

namespace NIRAAgent.Mind;

public sealed class NIRAExecutive
{
    private readonly NIRACognitionService
        _cognition;


    private readonly NIRAResponseRealizationService
        _responseRealization;

    private readonly NIRATaskCompletionReviewService
        _taskCompletionReview;


    private readonly NIRACognitionContextBuilder
        _contextBuilder;


    private readonly NIRAInteractionObservationService
        _interactionObservation;


    private readonly NIRACharacterDynamicsService
        _characterDynamics;


    private readonly NIRAMemoryConsolidator
        _memoryConsolidator;


    private readonly NIRALongTermMemoryService
        _longTermMemory;


    private readonly NIRAMemoryAssociationService
        _memoryAssociations;


    private readonly NIRAMemoryFormationService
        _memoryFormation;


    private readonly NIRASelfPreferenceService
        _selfPreferences;


    private readonly NIRASelfPreferenceMemorySyncService
        _selfPreferenceMemorySync;


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


    private readonly NIRAVisualArtifactService
        _visualArtifacts;


    private readonly NIRAVoiceExpressionService
        _voiceExpression;


    private readonly ConversationManager
        _conversation;


    private readonly NIRASocialHistoryService
        _socialHistory;


    private readonly NIRAAuthorityExecutionContextAccessor
        _authorityContext;

    private readonly NIRABrowserService _browser;


    public NIRAExecutive(
        NIRACognitionService cognition,
        NIRAResponseRealizationService responseRealization,
        NIRATaskCompletionReviewService taskCompletionReview,
        NIRACognitionContextBuilder contextBuilder,
        NIRAInteractionObservationService interactionObservation,
        NIRACharacterDynamicsService characterDynamics,
        NIRAMemoryConsolidator memoryConsolidator,
        NIRALongTermMemoryService longTermMemory,
        NIRAMemoryAssociationService memoryAssociations,
        NIRAMemoryFormationService memoryFormation,
        NIRASelfPreferenceService selfPreferences,
        NIRASelfPreferenceMemorySyncService selfPreferenceMemorySync,
        NIRASelfModelService selfModel,
        NIRAGoalService goals,
        NIRABranchService branches,
        NIRABranchWorkService branchWork,
        NIRACapabilityService capabilities,
        NIRADynamicToolService dynamicTools,
        NIRALearnedSkillService skills,
        NIRAVisualArtifactService visualArtifacts,
        NIRAVoiceExpressionService voiceExpression,
        ConversationManager conversation,
        NIRASocialHistoryService socialHistory,
        NIRAAuthorityExecutionContextAccessor authorityContext,
        NIRABrowserService browser)
    {
        _cognition =
            cognition
            ?? throw new ArgumentNullException(
                nameof(cognition));


        _responseRealization =
            responseRealization
            ?? throw new ArgumentNullException(
                nameof(responseRealization));


        _taskCompletionReview =
            taskCompletionReview
            ?? throw new ArgumentNullException(nameof(taskCompletionReview));

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


        _skills =
            skills
            ?? throw new ArgumentNullException(
                nameof(skills));


        _visualArtifacts =
            visualArtifacts
            ?? throw new ArgumentNullException(
                nameof(visualArtifacts));


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


        _authorityContext =
            authorityContext
            ?? throw new ArgumentNullException(
                nameof(authorityContext));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }


    public async IAsyncEnumerable<NIRAOutputChunk> RunAsync(
        NIRAMindEvent mindEvent,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);


        Guid runId =
            Guid.NewGuid();


        Guid? ownedGoalId =
            TryReadGuidMetadata(
                mindEvent,
                "goalId");


        using IDisposable authorityLease =
            _authorityContext.Push(
                new NIRAAuthorityExecutionContext
                {
                    RunId = runId,
                    GoalId = ownedGoalId,
                    UserInitiated =
                        mindEvent.Source ==
                        NIRAMindEventSource.User,
                    TaskLabel =
                        mindEvent.TopicKey,
                    DirectUserRequest = mindEvent.Source == NIRAMindEventSource.User
                        ? mindEvent.Content
                        : string.Empty
                });


        if (TryGetStaleOwnedInternalEventReason(
                mindEvent,
                out string initialStaleReason))
        {
            Debug.WriteLine(
                $"[Executive] Run={runId} | STALE INTERNAL EVENT DROPPED | " +
                $"Event={mindEvent.Name} | Reason='{TrimLog(initialStaleReason)}'");

            yield return new NIRAOutputChunk
            {
                RunId =
                    runId,

                Source =
                    mindEvent.Source,

                Type =
                    NIRAOutputChunkType.Completed,

                Content =
                    string.Empty,

                VoiceExpression =
                    NIRAVoiceExpression.Neutral
            };

            yield break;
        }


        // Persistent safety circuit: per-run no-progress counters reset on
        // each branch result, so a failing browser/login could spawn dozens
        // of paid cognition turns. Inspect durable work BEFORE calling an LLM.
        if (mindEvent.Source == NIRAMindEventSource.Internal &&
            ownedGoalId is Guid guardedGoalId &&
            _goals.TryGetGoal(guardedGoalId, out NIRAGoalState? guardedGoal) &&
            guardedGoal != null)
        {
            if (guardedGoal.Status == NIRAGoalStatus.Blocked)
            {
                // Align a pre-existing blocked goal's lingering ACTIVE cards
                // without waking the model or repeating a user-visible reply.
                if (!_branchWork.CurrentWork.Any(w =>
                        w.GoalId == guardedGoalId && w.IsOpen))
                    await PersistBlockedTaskAsync(
                        mindEvent, guardedGoalId,
                        guardedGoal.Blocker ?? "The parent task is blocked.",
                        cancellationToken);
                yield return new NIRAOutputChunk
                {
                    RunId = runId, Source = mindEvent.Source,
                    Type = NIRAOutputChunkType.Completed,
                    Content = string.Empty,
                    VoiceExpression = NIRAVoiceExpression.Neutral
                };
                yield break;
            }

            string? taskBlocker = GetPersistentWorkLoopBlocker(guardedGoalId);
            if (taskBlocker != null &&
                !_branchWork.CurrentWork.Any(w => w.GoalId == guardedGoalId && w.IsOpen))
            {
                string message = "I stopped because repeated actions were not getting " +
                    "the requested result. " + taskBlocker +
                    " I haven't verified or retrieved the requested information.";
                var (blocked, _) =
                    await PersistBlockedTaskAsync(
                        mindEvent, guardedGoalId, message, cancellationToken);
                if (blocked.Any(result => result.Changed))
                {
                    Debug.WriteLine($"[Executive] GOAL LOOP GUARD | Goal={guardedGoalId:D} | {taskBlocker}");
                    await RecordResponseAsync(mindEvent, message);
                    yield return new NIRAOutputChunk {
                        RunId = runId, Source = mindEvent.Source,
                        Type = NIRAOutputChunkType.Text,
                        Content = message,
                        VoiceExpression = NIRAVoiceExpression.Neutral
                    };
                }
                yield return new NIRAOutputChunk {
                    RunId = runId, Source = mindEvent.Source,
                    Type = NIRAOutputChunkType.Completed,
                    Content = string.Empty,
                    VoiceExpression = NIRAVoiceExpression.Neutral
                };
                yield break;
            }
        }

        // A worker waiting for a trusted credential window owns its action.
        // Poll/reconsideration events must not spawn new paid planning turns
        // while that same authoritative work is still running.
        if (mindEvent.Source == NIRAMindEventSource.Internal &&
            mindEvent.Name == "PersistentBranchWorkReconsideration" &&
            ownedGoalId is Guid pendingGoalId &&
            _branchWork.CurrentWork.Any(w => w.GoalId == pendingGoalId && w.IsOpen))
        {
            yield return new NIRAOutputChunk {
                RunId = runId, Source = mindEvent.Source,
                Type = NIRAOutputChunkType.Completed,
                Content = string.Empty,
                VoiceExpression = NIRAVoiceExpression.Neutral
            };
            yield break;
        }

        NIRAInteractionContext? interaction =
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

        // At most two independent reviews of an action-based user's final
        // answer. A review is a reconsideration signal, never world evidence.
        int taskCompletionReviewCount = 0;


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


        Dictionary<string, NIRACapabilityResult> capabilityResultsBySignature =
            new(
                StringComparer.OrdinalIgnoreCase);


        HashSet<string> reportedDuplicateCapabilityRequests =
            new(
                StringComparer.OrdinalIgnoreCase);


        NIRACapabilityResult? lastCapabilityResult =
            null;


        NIRACognitionContext? latestContext =
            null;


        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();


            cycle++;


            NIRACognitionContext context =
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


            NIRACognitionDecision decision =
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

                yield return new NIRAOutputChunk
                {
                    RunId =
                        runId,

                    Source =
                        mindEvent.Source,

                    Type =
                        NIRAOutputChunkType.Completed,

                    Content =
                        string.Empty,

                    VoiceExpression =
                        NIRAVoiceExpression.Neutral
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
                $"VisualPresentations={decision.VisualPresentations.Count} | " +
                $"ToolProposals={decision.DynamicToolProposals.Count} | " +
                $"ToolInvocations={decision.DynamicToolInvocations.Count} | " +
                $"Summary='{TrimLog(decision.DecisionSummary)}'");


            NIRAVoiceExpression expression =
                NIRAVoiceExpression.Neutral;


            if (!initialStateApplied)
            {
                initialStateApplied =
                    true;


                ApplyFirstCycleCharacterState(
                    mindEvent,
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


            // When closing a branch after an actual tool result, check the
            // ORIGINAL goal outcome once before committing a success claim.
            // Reviewer is a check, NEVER execution evidence or authorization.
            // If the model quoted a paraphrase or an earlier observation,
            // replace it only with an EXACT excerpt of this successful result,
            // and only after the independent outcome review approves it.
            if (mindEvent.Name == "PersistentBranchWorkResult" &&
                ownedGoalId is Guid reviewGoalId &&
                decision.State == NIRACognitionState.Complete &&
                (decision.BranchProposals.Any(p =>
                    p.Action == NIRABranchProposalAction.Complete) ||
                 decision.GoalProposals.Any(p =>
                    p.Action == NIRAGoalProposalAction.Complete)) &&
                _goals.TryGetGoal(reviewGoalId, out NIRAGoalState? reviewGoal) &&
                reviewGoal is { IsResolved: false })
            {
                NIRATaskCompletionReview? branchReview =
                    await _taskCompletionReview.ReviewAsync(
                        new NIRATaskCompletionReviewRequest(
                            reviewGoal.Objective,
                            string.IsNullOrWhiteSpace(decision.Reply)
                                ? "No final answer was provided. " + decision.DecisionSummary
                                : decision.Reply ?? string.Empty,
                            mindEvent.Content,
                            context.ConversationContext,
                            _conversation.PendingTask?.Objective ?? string.Empty),
                        cancellationToken);
                if (branchReview?.NeedsReconsideration == true)
                {
                    executiveEvidence.AppendLine();
                    executiveEvidence.AppendLine(
                        "COMPLETION REVIEW: original objective not proven. " +
                        branchReview.Gap + " Next: " + branchReview.NextStep +
                        ". The previous work result is NOT a completed task. " +
                        "Choose an executable branchWorkProposals item on the exact " +
                        "existing branch GUID, or identify a genuine external blocker; " +
                        "do not claim Complete a second time without NEW outcome evidence. " +
                        "Do not repeat completed or uncertain actions.");
                    if (TryReadMetadataGuid(mindEvent, "branchId", out Guid incompleteBranchId))
                        executiveEvidence.AppendLine(
                            "EXACT CONTINUATION BRANCH ID: " + incompleteBranchId.ToString("D"));
                    // The reviewer is never completion evidence. Prevent an
                    // unsupported success transition before applying ANY model
                    // proposal, and let the same run plan from the real result.
                    decision = decision with
                    {
                        BranchProposals = decision.BranchProposals.Where(p =>
                            p.Action != NIRABranchProposalAction.Complete).ToArray(),
                        GoalProposals = decision.GoalProposals.Where(p =>
                            p.Action != NIRAGoalProposalAction.Complete).ToArray(),
                        State = NIRACognitionState.Continue,
                        EmitReply = false,
                        Reply = string.Empty
                    };
                }
                else if (branchReview?.Verdict == "Complete" &&
                         TryGetExactWorkResultQuote(mindEvent, out string exactQuote) &&
                         decision.EmitReply &&
                         !string.IsNullOrWhiteSpace(decision.Reply))
                {
                    // The independent reviewer approved the actual answer,
                    // and this event contains a successful authoritative work
                    // result. Repair malformed *proposal bookkeeping*, never
                    // synthesize an answer or bypass downstream validation.
                    // In particular, a missing branch ResultSummary otherwise
                    // makes every legitimate Complete proposal get rejected,
                    // creating needless LLM cycles and an immortal branch.
                    string reviewedAnswer = decision.Reply.Trim();
                    string resultSummary = reviewedAnswer[
                        ..Math.Min(reviewedAnswer.Length, 2400)];
                    Debug.WriteLine(
                        $"[Executive] REVIEWED COMPLETION REPAIR | " +
                        $"BranchCompletions={decision.BranchProposals.Count(p => p.Action == NIRABranchProposalAction.Complete)} | " +
                        $"GoalCompletions={decision.GoalProposals.Count(p => p.Action == NIRAGoalProposalAction.Complete)} | " +
                        "Source=VerifiedCurrentWorkResult");
                    decision = decision with
                    {
                        BranchProposals = decision.BranchProposals.Select(p =>
                            p.Action != NIRABranchProposalAction.Complete
                                ? p
                                : p with
                                {
                                    ResultSummary = string.IsNullOrWhiteSpace(p.ResultSummary)
                                        ? resultSummary : p.ResultSummary,
                                    EvidenceSource = NIRABranchEvidenceSource.CurrentEvent,
                                    EvidenceQuote = ContainsGroundedQuote(mindEvent.Content, p.EvidenceQuote) &&
                                        p.EvidenceSource == NIRABranchEvidenceSource.CurrentEvent
                                        ? p.EvidenceQuote : exactQuote,
                                    EvidenceSummary = string.IsNullOrWhiteSpace(p.EvidenceSummary)
                                        ? "Reviewed final answer grounded in the successful work result."
                                        : p.EvidenceSummary
                                }).ToArray(),
                        GoalProposals = decision.GoalProposals.Select(p =>
                            p.Action != NIRAGoalProposalAction.Complete
                                ? p
                                : p with
                                {
                                    EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
                                    EvidenceQuote = ContainsGroundedQuote(mindEvent.Content, p.EvidenceQuote) &&
                                        p.EvidenceSource == NIRAGoalEvidenceSource.CurrentEvent
                                        ? p.EvidenceQuote : exactQuote,
                                    EvidenceSummary = string.IsNullOrWhiteSpace(p.EvidenceSummary)
                                        ? "Reviewed final answer grounded in the successful work result."
                                        : p.EvidenceSummary
                                }).ToArray()
                    };
                }
                else if (branchReview?.Verdict == "Complete" &&
                         (string.IsNullOrWhiteSpace(decision.Reply) || !decision.EmitReply))
                {
                    // A model-written decision summary is not an answer to
                    // the user. Don't terminate an external task on it.
                    decision = decision with
                    {
                        BranchProposals = decision.BranchProposals.Where(p =>
                            p.Action != NIRABranchProposalAction.Complete).ToArray(),
                        GoalProposals = decision.GoalProposals.Where(p =>
                            p.Action != NIRAGoalProposalAction.Complete).ToArray(),
                        State = NIRACognitionState.Continue,
                        EmitReply = false,
                        Reply = string.Empty
                    };
                    executiveEvidence.AppendLine(
                        "COMPLETION REVIEW: no user-facing answer exists yet. " +
                        "Provide a concrete, evidence-grounded answer before resolving the branch.");
                }
            }

            // Persisted branches are the sole owners of their next machine action.
            // A prior user/internal run must not continue thinking in parallel
            // after it has delegated an operation to the branch worker.
            NIRAGoalProposal[] newGoalProposals =
                decision.GoalProposals
                    .Select(
                        proposal =>
                            proposal.Normalize())
                    .Where(
                        proposal =>
                            executedGoalProposals.Add(
                                proposal.BuildSignature()))
                    .ToArray();

            // Resolve invalid MODEL quotation on the committed branch-result
            // envelope using its exact, trusted status evidence. Do not
            // bypass the parent goal's required-branch/prerequisite gates.
            if (IsCommittedCompletedBranchResult(mindEvent) &&
                decision.State == NIRACognitionState.Complete &&
                decision.EmitReply &&
                !string.IsNullOrWhiteSpace(decision.Reply) &&
                ownedGoalId is Guid completedParentGoalId &&
                _branches.GetForGoal(completedParentGoalId) is { } requiredBranches &&
                requiredBranches.Any(b => b.JoinPolicy == NIRABranchJoinPolicy.Required) &&
                requiredBranches.Where(b =>
                    b.JoinPolicy == NIRABranchJoinPolicy.Required)
                    .All(b => b.Status == NIRABranchStatus.Completed))
            {
                newGoalProposals = newGoalProposals.Select(proposal =>
                    proposal.Action == NIRAGoalProposalAction.Complete &&
                    !ContainsGroundedQuote(
                        proposal.EvidenceSource switch
                        {
                            NIRAGoalEvidenceSource.CurrentEvent => mindEvent.Content,
                            NIRAGoalEvidenceSource.NIRAReply => decision.Reply,
                            NIRAGoalEvidenceSource.CapabilityResult =>
                                capabilityEvidence.ToString(),
                            _ => string.Empty
                        }, proposal.EvidenceQuote)
                        ? proposal with
                        {
                            EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
                            EvidenceQuote = "Final branch status:\nCompleted"
                        }
                        : proposal).ToArray();
            }

            if (mindEvent.Name == "PersistentBranchResult" &&
                ownedGoalId is Guid finalGoalId &&
                decision.State == NIRACognitionState.Complete &&
                decision.EmitReply &&
                !string.IsNullOrWhiteSpace(decision.Reply) &&
                IsCommittedCompletedBranchResult(mindEvent) &&
                _goals.TryGetGoal(finalGoalId, out NIRAGoalState? finalGoal) &&
                finalGoal is { IsResolved: false } &&
                !newGoalProposals.Any(p =>
                    p.Action == NIRAGoalProposalAction.Complete) &&
                _branches.GetForGoal(finalGoalId) is { } completionBranches &&
                completionBranches.Any(b => b.JoinPolicy == NIRABranchJoinPolicy.Required) &&
                completionBranches.Where(b =>
                    b.JoinPolicy == NIRABranchJoinPolicy.Required)
                    .All(b => b.Status == NIRABranchStatus.Completed))
            {
                // This is an exact quote from the committed branch-result
                // event; it is not model-written evidence. GoalService still
                // validates prerequisites and all required root branches.
                newGoalProposals = newGoalProposals.Concat(new[]
                {
                    new NIRAGoalProposal
                    {
                        Action = NIRAGoalProposalAction.Complete,
                        GoalId = finalGoalId.ToString("D"),
                        EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
                        EvidenceQuote = "Final branch status:\nCompleted",
                        EvidenceSummary = "All required branches completed; " +
                            "the decision reports the user-requested result.",
                        Reason = "Reconcile parent after committed required branch.",
                        Confidence = 0.95
                    }
                }).ToArray();
            }

            // If a decision resolves a required branch AND its parent goal,
            // commit the child first. Otherwise GoalService rejects a valid
            // completion simply because the branch transition has not run yet.
            // Never override its evidence/authorization validation.
            NIRAGoalProposal[] deferredGoalCompletions =
                decision.BranchProposals.Any(p =>
                    p.Action == NIRABranchProposalAction.Complete)
                    ? newGoalProposals.Where(p =>
                        p.Action == NIRAGoalProposalAction.Complete).ToArray()
                    : Array.Empty<NIRAGoalProposal>();

            NIRAGoalProposal[] immediateGoalProposals =
                deferredGoalCompletions.Length == 0
                    ? newGoalProposals
                    : newGoalProposals.Where(p =>
                        p.Action != NIRAGoalProposalAction.Complete).ToArray();

            IReadOnlyList<NIRAGoalApplyResult> goalResults =
                await _goals.ApplyProposalsAsync(
                    mindEvent,
                    decision.Reply ?? string.Empty,
                    capabilityEvidence.ToString(),
                    immediateGoalProposals,
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
            // obsolete and must not wake NIRA later to reconstruct it.
            // =====================================================

            int cascadedBranchCancellations =
                0;

            NIRAGoalState[] resolvedGoals =
                goalResults
                    .Where(
                        result =>
                            result.Changed
                            &&
                            result.Goal?.Status is
                                NIRAGoalStatus.Completed
                                or NIRAGoalStatus.Cancelled)
                    .Select(
                        result =>
                            result.Goal!)
                    .ToArray();

            foreach (NIRAGoalState resolvedGoal in resolvedGoals)
            {
                string resolutionReason =
                    resolvedGoal.Status == NIRAGoalStatus.Completed
                        ? $"Parent goal completed; remaining child work is obsolete: {resolvedGoal.Objective}"
                        : $"Parent goal was cancelled: {resolvedGoal.Objective}";

                IReadOnlyList<NIRABranchState> cancelledBranches =
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


            // A later explicit USER task must never be grafted onto a
            // previous, blocked goal. The old goal's durable failure history
            // and authorization remain intact. This repair is limited to
            // a single unambiguous stale parent referenced by this turn;
            // it cannot authorize actions or manufacture a branch ID.
            Dictionary<Guid, Guid> freshUserGoalOwners = new();
            if (mindEvent.Source == NIRAMindEventSource.User &&
                goalResults.All(result => result.Action != NIRAGoalApplyAction.Created) &&
                decision.BranchProposals.Count > 0 &&
                !string.IsNullOrWhiteSpace(mindEvent.Content))
            {
                // A previous attempt can remain Active even when an older
                // REQUIRED root branch has failed/been blocked. A later direct
                // user request must never inherit that incomplete root.
                Guid[] oldBlockedParentIds = decision.BranchProposals
                    .Select(proposal => Guid.TryParse(proposal.GoalId, out Guid id)
                        ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .Where(id => _goals.TryGetGoal(id, out NIRAGoalState? prior) &&
                        prior != null && prior.IsOpen &&
                        prior.SourceEventId != mindEvent.Id &&
                        mindEvent.Timestamp > prior.CreatedAt &&
                        (prior.Status == NIRAGoalStatus.Blocked ||
                         _branches.GetForGoal(id).Any(branch =>
                             branch.ParentBranchId == null &&
                             branch.JoinPolicy == NIRABranchJoinPolicy.Required &&
                             (branch.IsResolved ||
                              branch.Status == NIRABranchStatus.Blocked))))
                    .ToArray();
                if (oldBlockedParentIds.Length == 1 &&
                    decision.BranchProposals.All(proposal =>
                        Guid.TryParse(proposal.GoalId, out Guid target) &&
                        target == oldBlockedParentIds[0]))
                {
                    string userObjective = mindEvent.Content.Trim();
                    if (userObjective.Length > 1200)
                        userObjective = userObjective[..1200];
                    string[] completionCriteria = decision.BranchProposals
                        .SelectMany(proposal => proposal.CompletionCriteria)
                        .Where(criterion => !string.IsNullOrWhiteSpace(criterion))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12).ToArray();
                    if (completionCriteria.Length == 0)
                        completionCriteria = new[]
                        {
                            "Report the current user's requested result with evidence, or an explicit verified blocker."
                        };
                    IReadOnlyList<NIRAGoalApplyResult> freshResults =
                        await _goals.ApplyProposalsAsync(
                            mindEvent, decision.Reply ?? string.Empty,
                            capabilityEvidence.ToString(),
                            new[] { new NIRAGoalProposal
                            {
                                Action = NIRAGoalProposalAction.Create,
                                Objective = userObjective,
                                CompletionCriteria = completionCriteria,
                                EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
                                EvidenceQuote = userObjective,
                                EvidenceSummary = "A distinct direct-user task must not inherit a prior blocked goal's failed execution ledger.",
                                Reason = "Fresh grounded request; preserve the earlier blocked goal and its evidence.",
                                Confidence = 1.0
                            } }, cancellationToken);
                    NIRAGoalState? fresh = freshResults
                        .Where(result => result.Action == NIRAGoalApplyAction.Created)
                        .Select(result => result.Goal)
                        .FirstOrDefault(goal => goal != null);
                    if (fresh != null)
                    {
                        freshUserGoalOwners[oldBlockedParentIds[0]] = fresh.Id;
                        goalChanges += 1;
                        executiveEvidence.AppendLine(
                            $"Fresh user execution goal {fresh.Id:D} replaces stale blocked parent {oldBlockedParentIds[0]:D} for this turn only. Earlier work ledger remains unchanged.");
                        Debug.WriteLine($"[Executive] FRESH USER TASK | Goal={fresh.Id:D} | StaleBlockedGoal={oldBlockedParentIds[0]:D}");
                    }
                }
            }

            NIRABranchProposal[] newBranchProposals =
                decision.BranchProposals
                    .Select(proposal =>
                    {
                        NIRABranchProposal normalized = proposal.Normalize();
                        if (Guid.TryParse(normalized.GoalId, out Guid oldId) &&
                            freshUserGoalOwners.TryGetValue(oldId, out Guid newId))
                            normalized = normalized with { GoalId = newId.ToString("D") };

                        // Creating a branch from the CURRENT direct request is
                        // grounded in that request, not in an LLM's paraphrase.
                        // A bad quote used to reject the branch, then its
                        // <newly-created-branch-id> work, costing two more calls.
                        // Repair only this creation case; NEVER repair a result,
                        // completion, grant or other claimed world evidence.
                        if (mindEvent.Source == NIRAMindEventSource.User &&
                            normalized.Action == NIRABranchProposalAction.Create &&
                            normalized.EvidenceSource == NIRABranchEvidenceSource.CurrentEvent &&
                            !ContainsGroundedQuote(mindEvent.Content, normalized.EvidenceQuote) &&
                            !string.IsNullOrWhiteSpace(mindEvent.Content))
                        {
                            string exactUserExcerpt =
                                TryReadExplicitUserDestination(mindEvent.Content, out string explicitDestination)
                                    ? explicitDestination
                                    : mindEvent.Content[..Math.Min(160, mindEvent.Content.Length)];
                            if (!ContainsGroundedQuote(mindEvent.Content, exactUserExcerpt))
                                exactUserExcerpt = mindEvent.Content[..Math.Min(160, mindEvent.Content.Length)];
                            normalized = normalized with { EvidenceQuote = exactUserExcerpt };
                            Debug.WriteLine("[Executive] GROUNDED USER BRANCH CREATION | " +
                                "Invalid paraphrase replaced with literal user-event evidence.");
                        }

                        // Branch labels must not silently substitute a remembered
                        // website for an explicit destination in THIS user request.
                        // This is domain-neutral: no stored account/previous goal
                        // may redirect the task to a different origin.
                        if (mindEvent.Source == NIRAMindEventSource.User &&
                            normalized.Action == NIRABranchProposalAction.Create &&
                            TryReadExplicitUserDestination(mindEvent.Content, out string target) &&
                            !normalized.Objective.Contains(target, StringComparison.OrdinalIgnoreCase))
                        {
                            normalized = normalized with
                            {
                                Objective = "Complete the current user's requested work at " +
                                            target + ". Return the requested result, " +
                                            "not merely successful navigation or login.",
                                Reason = "The current user's explicit destination overrides " +
                                         "an ungrounded remembered service or website in a branch label."
                            };
                            Debug.WriteLine($"[Executive] USER DESTINATION GROUNDED | Target={target}");
                        }
                        return normalized;
                    })
                    .Where(
                        proposal =>
                            executedBranchProposals.Add(
                                proposal.BuildSignature()))
                    .ToArray();


            IReadOnlyList<NIRABranchApplyResult> branchResults =
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

            // Exactly the same model decision, not another planning call.
            // The original quote still has to pass the normal goal evidence gate.
            if (deferredGoalCompletions.Length > 0)
            {
                IReadOnlyList<NIRAGoalApplyResult> deferredResults =
                    await _goals.ApplyProposalsAsync(
                        mindEvent, decision.Reply ?? string.Empty,
                        capabilityEvidence.ToString(),
                        deferredGoalCompletions, cancellationToken);
                goalResults = goalResults.Concat(deferredResults).ToArray();
                goalChanges += deferredResults.Count(r => r.Changed);
                foreach (NIRAGoalState resolvedGoal in deferredResults
                    .Where(r => r.Changed && r.Goal is
                        { Status: NIRAGoalStatus.Completed or NIRAGoalStatus.Cancelled })
                    .Select(r => r.Goal!))
                {
                    IReadOnlyList<NIRABranchState> cancelled =
                        await _branches.CancelOpenForGoalAsync(
                            resolvedGoal.Id, mindEvent,
                            "Parent goal resolved; outstanding child work is obsolete.",
                            cancellationToken);
                    branchChanges += cancelled.Count;
                }
            }

            NIRADynamicToolProposal[] newDynamicToolProposals =
                decision.DynamicToolProposals
                    .Select(proposal => proposal.Normalize())
                    .Where(proposal => executedDynamicToolProposals.Add(proposal.BuildSignature()))
                    .ToArray();

            IReadOnlyList<NIRADynamicToolMutationResult> dynamicToolMutations =
                await _dynamicTools.ApplyProposalsAsync(
                    newDynamicToolProposals,
                    cancellationToken);

            int dynamicToolChanges = dynamicToolMutations.Count(result => result.Changed);
            int newDynamicToolEvidenceCount =
                AppendDynamicToolMutationEvidence(dynamicToolEvidence, dynamicToolMutations);


            // A model can express the NEXT exact read-only capability as a
            // capabilityRequest during a persistent branch-result turn. Before
            // this fix the executive simply suppressed it and then exhausted
            // its no-progress budget, leaving the original goal permanently
            // waiting. Route the EXACT model-requested operation to the event's
            // already verified owning branch. The normal capability authorizer,
            // dispatch journal, and side-effect guards still govern all actions.
            // Never manufacture or retry an operation, create a branch ID, or
            // bypass an existing branch-owned invocation.
            List<NIRABranchWorkProposal> proposedBranchWork =
                decision.BranchWorkProposals
                    .Select(proposal => proposal.Normalize())
                    .ToList();

            if (IsInternalBranchLifecycleEvent(mindEvent) &&
                TryReadMetadataGuid(mindEvent, "goalId", out Guid routingGoalId) &&
                TryReadMetadataGuid(mindEvent, "branchId", out Guid routingBranchId) &&
                _goals.TryGetGoal(routingGoalId, out NIRAGoalState? routingGoal) &&
                routingGoal is { IsResolved: false } &&
                _branches.TryGetBranch(routingBranchId, out NIRABranchState? routingBranch) &&
                routingBranch != null && !routingBranch.IsResolved &&
                routingBranch.GoalId == routingGoalId)
            {
                HashSet<string> alreadyOwned = new(
                    proposedBranchWork
                        .Where(proposal => proposal.Kind == NIRABranchWorkKind.Capability)
                        .Select(proposal => proposal.CapabilityRequest!.BuildSignature()),
                    StringComparer.OrdinalIgnoreCase);

                foreach (NIRACapabilityRequest candidate in decision.CapabilityRequests)
                {
                    NIRACapabilityRequest request = candidate.Normalize();
                    if (!alreadyOwned.Add(request.BuildSignature()))
                        continue;

                    proposedBranchWork.Add(new NIRABranchWorkProposal
                    {
                        BranchId = routingBranchId.ToString("D"),
                        Kind = NIRABranchWorkKind.Capability,
                        CapabilityRequest = request,
                        Confidence = 1.0,
                        Reason = "Runtime ownership repair: execute the model's exact request under its verified active branch."
                    });
                    Debug.WriteLine($"[Executive] BRANCH CAPABILITY ROUTED | " +
                        $"Branch={routingBranchId:D} | Capability={request.CapabilityId}");
                }
            }

            // If exactly one authoritative branch was JUST created by this
            // event, resolve the model's documented new-branch placeholder
            // against the committed result. Never guess when two were created
            // or accept a forged GUID as a placeholder. In ambiguous cases
            // the original validation rejects the proposal normally.
            NIRABranchState[] newBranchesThisCycle = branchResults
                .Where(result => result.Action == NIRABranchApplyAction.Created &&
                    result.Branch != null && result.Branch.SourceEventId == mindEvent.Id)
                .Select(result => result.Branch!)
                .ToArray();
            // The user-run's first browser.session.open can precede persistent
            // task creation. Adopt ONLY an exact-run, freshly created interactive
            // page into the freshly committed branch, preserving its inspected
            // refs and avoiding a second navigation / additional LLM call.
            if (mindEvent.Source == NIRAMindEventSource.User &&
                newBranchesThisCycle.Length == 1 &&
                newBranchesThisCycle[0].GoalId != Guid.Empty)
                _browser.PromoteInteractivePageOpenedByRun(
                    runId, newBranchesThisCycle[0].GoalId,
                    newBranchesThisCycle[0].Id);

            if (newBranchesThisCycle.Length == 1)
            {
                for (int workIndex = 0; workIndex < proposedBranchWork.Count; workIndex++)
                {
                    NIRABranchWorkProposal proposed = proposedBranchWork[workIndex];
                    if (string.Equals(proposed.BranchId,
                        "<newly-created-branch-id>", StringComparison.OrdinalIgnoreCase))
                    {
                        proposedBranchWork[workIndex] = proposed with
                        {
                            BranchId = newBranchesThisCycle[0].Id.ToString("D")
                        };
                        Debug.WriteLine($"[Executive] BRANCH ID BOUND | Branch={newBranchesThisCycle[0].Id:D}");
                    }
                }
            }

            NIRABranchWorkProposal[] newBranchWorkProposals =
                proposedBranchWork
                    .Where(proposal => executedBranchWorkProposals.Add(
                        proposal.BuildSignature()))
                    .ToArray();


            IReadOnlyList<NIRABranchWorkApplyResult> branchWorkResults =
                await _branchWork.ApplyProposalsAsync(
                    mindEvent,
                    newBranchWorkProposals,
                    cancellationToken);

            // Third login dispatch is denied by the durable branch-work
            // ledger. Do not let ordinary internal-reply suppression hide the
            // resulting user-facing blocker or pay for another cognition run.
            string? genericBudgetReason = branchWorkResults
                .Select(result => result.Reason)
                .FirstOrDefault(reason => reason.StartsWith(
                    "GenericWorkBudgetReached:", StringComparison.Ordinal));
            bool authenticationBudgetReached = branchWorkResults.Any(result =>
                result.Reason.StartsWith("AuthenticationRetryBudgetReached:",
                    StringComparison.Ordinal));
            if ((genericBudgetReason != null || authenticationBudgetReached) &&
                TryReadMetadataGuid(mindEvent, "goalId", out Guid limitedGoalId))
            {
                string blocker = genericBudgetReason != null
                    ? "I stopped after repeated steps failed to move this task forward. " +
                      genericBudgetReason["GenericWorkBudgetReached:".Length..].Trim() +
                      " I haven't verified the requested outcome."
                    : "The trusted login has already been attempted twice " +
                      "for this task without a verified authenticated result. " +
                      "I stopped rather than retrying credentials or asking you for " +
                      "another password. The requested information is not verified.";
                var (setBlocked, _) =
                    await PersistBlockedTaskAsync(
                        mindEvent, limitedGoalId, blocker, cancellationToken);
                if (setBlocked.Any(result => result.Changed))
                {
                    Debug.WriteLine($"[Executive] CROSS-RUN BUDGET STOP | Goal={limitedGoalId:D}");
                    await RecordResponseAsync(mindEvent, blocker);
                    yield return new NIRAOutputChunk {
                        RunId = runId, Source = mindEvent.Source,
                        Type = NIRAOutputChunkType.Text,
                        Content = blocker,
                        VoiceExpression = NIRAVoiceExpression.Neutral
                    };
                }
                yield return new NIRAOutputChunk {
                    RunId = runId, Source = mindEvent.Source,
                    Type = NIRAOutputChunkType.Completed,
                    Content = string.Empty,
                    VoiceExpression = NIRAVoiceExpression.Neutral
                };
                yield break;
            }


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
            // If NIRA assigned an exact bounded capability/tool
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
                NIRABranchWorkApplyResult result =
                    branchWorkResults[index];

                if (result.Action ==
                    NIRABranchWorkApplyAction.Rejected)
                {
                    continue;
                }

                NIRABranchWorkProposal proposal =
                    newBranchWorkProposals[index];

                if (proposal.Kind ==
                    NIRABranchWorkKind.Capability)
                {
                    branchOwnedCapabilitySignatures.Add(
                        proposal.CapabilityRequest!
                            .BuildSignature());
                }
                else if (proposal.Kind ==
                    NIRABranchWorkKind.DynamicTool)
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
                        result.Action == NIRAGoalApplyAction.Created)
                ||
                branchResults.Any(
                    result =>
                        result.Action == NIRABranchApplyAction.Created);

            bool topLevelMachineActionsDisallowed =
                internalBranchLifecycleEvent
                ||
                ownershipIdCommittedThisCycle;


            int suppressedTopLevelToolInvocations =
                0;

            NIRADynamicToolInvocation[] newDynamicToolInvocations =
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

            IReadOnlyList<NIRADynamicToolExecutionResult> dynamicToolExecutions =
                await _dynamicTools.ExecuteAsync(
                    newDynamicToolInvocations,
                    cancellationToken);

            newDynamicToolEvidenceCount +=
                AppendDynamicToolExecutionEvidence(dynamicToolEvidence, dynamicToolExecutions);

            List<NIRACapabilityResult> dynamicToolCapabilityResults =
                dynamicToolExecutions
                    .SelectMany(result => result.CapabilityResults)
                    .ToList();

            foreach (NIRADynamicToolExecutionResult toolResult in dynamicToolExecutions)
            {
                foreach (NIRADynamicToolStepExecutionResult stepResult in toolResult.StepResults)
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

            NIRACapabilityRequest[] requestedCapabilities =
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


            List<NIRACapabilityRequest> dispatchCapabilities =
                new();


            foreach (NIRACapabilityRequest request in requestedCapabilities)
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
                    out NIRACapabilityResult? previousResult);


                AppendRepeatedCapabilityRequestEvidence(
                    capabilityEvidence,
                    request,
                    previousResult);
            }


            NIRACapabilityRequest[] newCapabilityRequests =
                dispatchCapabilities.ToArray();


            IReadOnlyList<NIRACapabilityResult> capabilityResults =
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


                NIRACapabilityResult result =
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
                            NIRAGoalApplyAction.Rejected)
                ||
                branchResults.Any(
                    result =>
                        result.Action ==
                            NIRABranchApplyAction.Rejected)
                ||
                branchWorkResults.Any(
                    result =>
                        result.Action ==
                            NIRABranchWorkApplyAction.Rejected)
                ||
                dynamicToolMutations.Any(
                    result =>
                        result.Action ==
                            NIRADynamicToolApplyAction.Rejected);


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

            // Defensive migration path for already-persisted mixed-attempt
            // goals: this branch is actually complete, but an older REQUIRED
            // root of the same goal prevents parent completion. Deliver the
            // VERIFIED, committed branch summary once on the branch-result
            // event instead of spending more model calls and then silently
            // suppressing the only answer. Do not mark the parent complete.
            bool independentlyCompletedBranchReply = false;
            if (mindEvent.Name == "PersistentBranchResult" &&
                decision.State == NIRACognitionState.Complete &&
                IsCommittedCompletedBranchResult(mindEvent) &&
                TryReadMetadataGuid(mindEvent, "branchId", out Guid finishedBranchId) &&
                _branches.TryGetBranch(finishedBranchId, out NIRABranchState? finishedBranch) &&
                finishedBranch is { Status: NIRABranchStatus.Completed } &&
                !string.IsNullOrWhiteSpace(finishedBranch.ResultSummary) &&
                goalResults.Any(result =>
                    result.Action == NIRAGoalApplyAction.Rejected &&
                    result.Reason.Contains("REQUIRED root branch", StringComparison.OrdinalIgnoreCase)) &&
                _branches.GetForGoal(finishedBranch.GoalId).Any(other =>
                    other.Id != finishedBranchId &&
                    other.ParentBranchId == null &&
                    other.JoinPolicy == NIRABranchJoinPolicy.Required &&
                    other.Status != NIRABranchStatus.Completed &&
                    other.CreatedAt < finishedBranch.CreatedAt &&
                    (other.IsResolved || other.Status == NIRABranchStatus.Blocked)))
            {
                independentlyCompletedBranchReply = true;
                continueForRuntimeCorrection = false;
                decision = decision with
                {
                    State = NIRACognitionState.Complete,
                    EmitReply = true,
                    Reply = finishedBranch.ResultSummary!
                };
                Debug.WriteLine($"[Executive] HISTORIC ROOT ISOLATION | " +
                    $"Branch={finishedBranchId:D} | Goal={finishedBranch.GoalId:D} | " +
                    "Delivering persisted branch result; older goal remains unresolved.");
            }

            // A queued/ongoing assignment owns the NEXT observation. Do not
            // spend another cognition turn guessing its result, or open a new
            // browser session before the worker has returned its evidence.
            bool awaitingDelegatedWork =
                branchWorkResults.Any(r => r.Action is
                    NIRABranchWorkApplyAction.Queued or
                    NIRABranchWorkApplyAction.Duplicate)
                || (IsInternalBranchLifecycleEvent(mindEvent) &&
                    ownedGoalId is Guid delegatedGoalId &&
                    _branchWork.CurrentWork.Any(w =>
                        w.GoalId == delegatedGoalId && w.IsOpen));
            if (awaitingDelegatedWork)
            {
                Debug.WriteLine($"[Executive] Run={runId} | Cycle={cycle} | " +
                    "DELEGATED WAIT: worker owns the next result; no extra model cycle.");
                yield return new NIRAOutputChunk
                {
                    RunId = runId, Source = mindEvent.Source,
                    Type = NIRAOutputChunkType.Completed,
                    Content = string.Empty,
                    VoiceExpression = expression
                };
                yield break;
            }


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


            if (branchResults.Any(r => r.Changed &&
                    r.Branch?.Status == NIRABranchStatus.Completed) &&
                goalResults.Any(r => r.Action == NIRAGoalApplyAction.Rejected) &&
                deferredGoalCompletions.Length > 0)
            {
                // A committed branch emits PersistentBranchResult. Let that
                // single authoritative wake reconcile its parent; no parallel
                // retry from the older work-result event.
                continueForRuntimeCorrection = false;
                decision = decision with
                {
                    State = NIRACognitionState.Wait,
                    EmitReply = false,
                    Reply = string.Empty
                };
            }

            if (decision.State ==
                    NIRACognitionState.Continue
                ||
                continueForRuntimeCorrection
                ||
                continueForCapabilityResults
                ||
                continueForDynamicToolResults)
            {
                NIRAMemorySearchRequest[] newSearches =
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


                if (forceTerminal &&
                    TryReadMetadataGuid(mindEvent, "goalId", out Guid waitingWorkGoalId) &&
                    _branchWork.CurrentWork.Any(item =>
                        item.GoalId == waitingWorkGoalId && item.IsOpen))
                {
                    // The worker, not another LLM cycle, owns the next result.
                    // Do not misreport an active task as permanently blocked.
                    Debug.WriteLine($"[Executive] Run={runId} | Cycle={cycle} | WAIT FOR BRANCH WORK");
                    decision = decision with
                    {
                        State = NIRACognitionState.Wait,
                        EmitReply = false,
                        Reply = string.Empty,
                        DecisionSummary = "Waiting for the authoritative branch-work result."
                    };
                }
                else if (forceTerminal)
                {
                    string fallbackReply =
                        BuildNoProgressFallbackReply(
                            decision,
                            lastCapabilityResult);

                    (IReadOnlyList<NIRAGoalApplyResult> stoppedGoals,
                     IReadOnlyList<NIRABranchApplyResult> stoppedBranches) =
                        await PersistBlockedTaskAsync(
                            mindEvent, ResolveTaskGoalId(
                                mindEvent, ownedGoalId, goalResults, branchResults),
                            fallbackReply, cancellationToken);
                    goalResults = goalResults.Concat(stoppedGoals).ToArray();
                    branchResults = branchResults.Concat(stoppedBranches).ToArray();
                    goalChanges += stoppedGoals.Count(r => r.Changed);
                    branchChanges += stoppedBranches.Count(r => r.Changed);

                    Debug.WriteLine(
                        $"[Executive] Run={runId} | " +
                        $"Cycle={cycle} | " +
                        "NO-PROGRESS GUARD -> BLOCKED");


                    decision =
                        decision with
                        {
                            State =
                                NIRACognitionState.Blocked,

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

            // Check the original user outcome before treating a tool-using
            // run as finished. Successful low-level actions are not evidence
            // that the user-requested objective has been satisfied. No fixed
            // website/lecture/subject names, keyword lists or action counts.
            if (mindEvent.Source == NIRAMindEventSource.User &&
                (decision.State is NIRACognitionState.Complete or NIRACognitionState.NeedUser) &&
                decision.EmitReply &&
                taskCompletionReviewCount < 2 &&
                // A first-turn NeedUser can itself be a premature stop: the
                // model may ask what to check BEFORE performing any safe
                // observation. Review it even when this run has no tool
                // evidence or preceding conversation. Ordinary Complete
                // replies still avoid this extra model call unless evidence
                // or task continuity makes independent review relevant.
                (decision.State == NIRACognitionState.NeedUser ||
                 capabilityEvidence.Length > 0 ||
                 dynamicToolEvidence.Length > 0 ||
                 _conversation.PendingTask is not null))
            {
                NIRATaskCompletionReview? taskReview =
                    await _taskCompletionReview.ReviewAsync(
                        new NIRATaskCompletionReviewRequest(
                            mindEvent.Content,
                            decision.Reply ?? string.Empty,
                            string.Join(Environment.NewLine + Environment.NewLine,
                                executiveEvidence.ToString(),
                                capabilityEvidence.ToString(),
                                dynamicToolEvidence.ToString()),
                            latestContext?.ConversationContext ?? string.Empty,
                            _conversation.PendingTask?.Objective ?? string.Empty),
                        cancellationToken);

                if (taskReview?.NeedsReconsideration == true &&
                    (decision.State != NIRACognitionState.NeedUser ||
                     taskReview.Verdict == "NeedsWork"))
                {
                    taskCompletionReviewCount++;
                    if (taskCompletionReviewCount < 2)
                    {
                        executiveEvidence.AppendLine();
                        executiveEvidence.AppendLine(
                            "INDEPENDENT COMPLETION REVIEW (a model assessment, " +
                            "NOT proof of the world and NOT authority to act):");
                        executiveEvidence.AppendLine(
                            $"The original user objective is not yet shown complete. " +
                            $"Gap: {taskReview.Gap}. " +
                            $"Next required evidence or correction: {taskReview.NextStep}. " +
                            "Reconsider the ORIGINAL request. Continue with permitted " +
                            "evidence-gathering where useful, or be explicit about a real blocker. " +
                            "Do not repeat completed actions or invent results.");
                        continue;
                    }

                    // Bounded failure: never emit the same unverified final
                    // claim after two independent completion objections.
                    decision = decision with
                    {
                        State = NIRACognitionState.Blocked,
                        EmitReply = true,
                        Reply = "I couldn't verify the full result yet. " +
                                (string.IsNullOrWhiteSpace(taskReview.Gap)
                                    ? "The requested outcome is still incomplete."
                                    : taskReview.Gap),
                        DecisionSummary =
                            "Completion review found an unresolved user objective."
                    };
                }
            }

            // Model/reviewer Blocked after an internal result must also be
            // recorded in the task graph. Otherwise reply suppression silently
            // discards the only explanation and the UI shows an ACTIVE branch
            // with no worker forever. No blocking while work is still open.
            if (mindEvent.Source == NIRAMindEventSource.Internal &&
                decision.State == NIRACognitionState.Blocked &&
                !goalResults.Any(r => r.Changed && r.Goal?.Status == NIRAGoalStatus.Blocked))
            {
                string blocker = string.IsNullOrWhiteSpace(decision.Reply)
                    ? BuildNoProgressFallbackReply(decision, lastCapabilityResult)
                    : decision.Reply;
                (IReadOnlyList<NIRAGoalApplyResult> stoppedGoals,
                 IReadOnlyList<NIRABranchApplyResult> stoppedBranches) =
                    await PersistBlockedTaskAsync(
                        mindEvent, ResolveTaskGoalId(
                            mindEvent, ownedGoalId, goalResults, branchResults),
                        blocker, cancellationToken);
                goalResults = goalResults.Concat(stoppedGoals).ToArray();
                branchResults = branchResults.Concat(stoppedBranches).ToArray();
                if (stoppedGoals.Any(r => r.Changed))
                {
                    decision = decision with { EmitReply = true, Reply = blocker };
                    Debug.WriteLine($"[Executive] INTERNAL BLOCKER PERSISTED | Goal={ownedGoalId:D}");
                }
            }

            // Only a genuine conversational request for information creates a
            // pending handoff. A short follow-up never replaces its objective.
            // Terminal completion retires the handoff; a new unrelated request
            // can be handled by cognition without replaying the old task.
            if (mindEvent.Source == NIRAMindEventSource.User)
            {
                if (decision.State == NIRACognitionState.NeedUser &&
                    decision.EmitReply)
                    _conversation.RememberUnresolvedTask(
                        mindEvent.Content, decision.Reply ?? string.Empty);
                else if (decision.State == NIRACognitionState.Complete)
                    _conversation.ResolvePendingTask();
            }

            string reply =
                decision.EmitReply
                    ? (decision.Reply ?? string.Empty).Trim()
                    : string.Empty;


            if (!ShouldEmitReplyForInternalOrchestrationEvent(
                    mindEvent,
                    decision,
                    goalResults,
                    branchResults,
                    independentlyCompletedBranchReply))
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
                if (decision.ReplyPresentation ==
                    NIRAReplyPresentationMode.Natural)
                {
                    IReadOnlyList<string> requiredReplyFragments =
                        CollectRequiredNIRAReplyEvidenceFragments(
                            newGoalProposals,
                            newBranchProposals);

                    reply =
                        await _responseRealization.RealizeAsync(
                            new NIRAResponseRealizationRequest
                            {
                                DraftReply =
                                    reply,

                                DecisionSummary =
                                    decision.DecisionSummary,

                                EventSource =
                                    mindEvent.Source.ToString(),

                                EventName =
                                    mindEvent.Name,

                                TopicKey =
                                    mindEvent.TopicKey,

                                EventContent =
                                    mindEvent.Content,

                                TemporalContext =
                                    latestContext?.TemporalContext ?? string.Empty,

                                ConversationContext =
                                    latestContext?.ConversationContext
                                    ?? string.Empty,

                                Interaction =
                                    interaction,

                                RequiredVerbatimFragments =
                                    requiredReplyFragments
                            },
                            cancellationToken);

                    // Post-experience formation and social history must observe
                    // the utterance the user actually received, not the earlier
                    // executive draft.
                    decision =
                        decision with
                        {
                            Reply =
                                reply
                        };

                    expression =
                        _voiceExpression.Resolve(
                            decision.VocalIntent,
                            interaction);
                }

                await RecordResponseAsync(
                    mindEvent,
                    reply);


                yield return new NIRAOutputChunk
                {
                    RunId =
                        runId,

                    Source =
                        mindEvent.Source,

                    Type =
                        NIRAOutputChunkType.Text,

                    Content =
                        reply,

                    VoiceExpression =
                        expression
                };
            }


            // =====================================================
            // VISUAL COMMUNICATION OUTPUT
            //
            // Presentation happens only after the cognition run has
            // reached its terminal response state. The model may name
            // an already-grounded image source; the artifact service
            // revalidates that source and copies it into NIRA's bounded
            // presentation cache. Failed presentation never changes the
            // truth or lifecycle of the underlying goal/action.
            // =====================================================

            bool allowVisualPresentation =
                mindEvent.Source is
                    NIRAMindEventSource.User
                    or NIRAMindEventSource.Proactive
                ||
                !string.IsNullOrWhiteSpace(reply);

            if (allowVisualPresentation)
            {
                foreach (NIRAVisualArtifactPresentationRequest presentation in
                         decision.VisualPresentations)
                {
                    NIRAVisualArtifact artifact;

                    try
                    {
                        string groundedPresentationEvidence =
                            string.Join(
                                Environment.NewLine,
                                new[]
                                {
                                    executiveEvidence.ToString(),
                                    capabilityEvidence.ToString(),
                                    dynamicToolEvidence.ToString()
                                });

                        artifact =
                            await _visualArtifacts.CreateAsync(
                                presentation,
                                groundedPresentationEvidence,
                                cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Debug.WriteLine(
                            $"[VisualArtifact] REJECTED | Run={runId} | " +
                            $"Reason='{TrimLog(ex.Message)}'");

                        continue;
                    }

                    yield return new NIRAOutputChunk
                    {
                        RunId =
                            runId,

                        Source =
                            mindEvent.Source,

                        Type =
                            NIRAOutputChunkType.VisualArtifact,

                        Content =
                            string.Empty,

                        VisualArtifact =
                            artifact,

                        VoiceExpression =
                            expression
                    };
                }
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


            yield return new NIRAOutputChunk
            {
                RunId =
                    runId,

                Source =
                    mindEvent.Source,

                Type =
                    NIRAOutputChunkType.Completed,

                Content =
                    reply,

                VoiceExpression =
                    expression
            };


            yield break;
        }
    }


    // Ordinary world data and task names are not special-cased. Only the
    // runtime's documented branch-result envelope supplies lifecycle proof.
    private static bool IsCommittedCompletedBranchResult(NIRAMindEvent mindEvent)
    {
        if (mindEvent.Name != "PersistentBranchResult") return false;
        string normalized = string.Join(" ", mindEvent.Content.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Contains("Final branch status: Completed",
            StringComparison.Ordinal);
    }

    private static bool ContainsGroundedQuote(string source, string? quote)
    {
        if (string.IsNullOrWhiteSpace(quote)) return false;
        static string Normalize(string x) => string.Join(" ", x.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Normalize(source).Contains(Normalize(quote),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetExactWorkResultQuote(
        NIRAMindEvent mindEvent, out string quote)
    {
        quote = string.Empty;
        if (mindEvent.Name != "PersistentBranchWorkResult" ||
            !mindEvent.Content.Contains("Final work status:",
                StringComparison.Ordinal) ||
            !ContainsGroundedQuote(mindEvent.Content,
                "Final work status: Succeeded"))
            return false;
        const string marker = "Authoritative result evidence:";
        int index = mindEvent.Content.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return false;
        string excerpt = mindEvent.Content[(index + marker.Length)..]
            .TrimStart(' ', '\r', '\n');
        int end = excerpt.IndexOf(
            "This is authoritative runtime evidence", StringComparison.Ordinal);
        if (end >= 0) excerpt = excerpt[..end].TrimEnd();
        if (string.IsNullOrWhiteSpace(excerpt)) return false;
        // Store a short verbatim prefix of the outcome evidence; it is
        // checked again by GoalService/BranchService against this event.
        quote = excerpt[..Math.Min(excerpt.Length, 260)].TrimEnd();
        return quote.Length > 0;
    }

    // One authoritative terminalization path for any internal task that has
    // exhausted useful work. Do not manufacture success or give a model grant.
    // Persist the parent first and mirror its blocker into open child branches
    // so the sidebar does not claim work is actively executing forever.
    private async Task<(IReadOnlyList<NIRAGoalApplyResult> Goals,
        IReadOnlyList<NIRABranchApplyResult> Branches)> PersistBlockedTaskAsync(
        NIRAMindEvent mindEvent, Guid? goalId, string message,
        CancellationToken cancellationToken)
    {
        if (goalId is not Guid id ||
            string.IsNullOrWhiteSpace(mindEvent.Content) ||
            !_goals.TryGetGoal(id, out NIRAGoalState? goal) ||
            goal is not { IsResolved: false } ||
            _branchWork.CurrentWork.Any(w => w.GoalId == id && w.IsOpen))
            return (Array.Empty<NIRAGoalApplyResult>(),
                Array.Empty<NIRABranchApplyResult>());

        string evidence = mindEvent.Content[..Math.Min(300, mindEvent.Content.Length)];
        string blocker = message.Length > 900 ? message[..900] : message;
        IReadOnlyList<NIRAGoalApplyResult> goals = await _goals.ApplyProposalsAsync(
            mindEvent, blocker, string.Empty,
            new[] { new NIRAGoalProposal
            {
                Action = NIRAGoalProposalAction.SetBlocked,
                GoalId = id.ToString("D"),
                Blocker = blocker,
                EvidenceSource = NIRAGoalEvidenceSource.CurrentEvent,
                EvidenceQuote = evidence,
                EvidenceSummary = "No runnable branch work remains; task result is not verified.",
                Reason = "Prevent silent unresolved task after execution stops.",
                Confidence = 1.0
            } }, cancellationToken);

        if (!goals.Any(r => r.Changed) &&
            (!_goals.TryGetGoal(id, out NIRAGoalState? nowBlocked) ||
             nowBlocked?.Status != NIRAGoalStatus.Blocked))
            return (goals, Array.Empty<NIRABranchApplyResult>());

        NIRABranchProposal[] proposals = _branches.GetForGoal(id)
            .Where(b => b.IsOpen && b.Status != NIRABranchStatus.Blocked)
            .Select(b => new NIRABranchProposal
            {
                Action = NIRABranchProposalAction.SetBlocked,
                BranchId = b.Id.ToString("D"),
                GoalId = id.ToString("D"),
                Blocker = blocker,
                EvidenceSource = NIRABranchEvidenceSource.CurrentEvent,
                EvidenceQuote = evidence,
                EvidenceSummary = "Parent goal blocked with no runnable work.",
                Reason = "Align branch state with parent task.",
                Confidence = 1.0
            }).ToArray();
        IReadOnlyList<NIRABranchApplyResult> branches = proposals.Length == 0
            ? Array.Empty<NIRABranchApplyResult>()
            : await _branches.ApplyProposalsAsync(
                mindEvent, blocker, string.Empty, proposals, cancellationToken);
        return (goals, branches);
    }

    // Direct-user turns may create a new persistent goal without metadata yet.
    // Recover ownership only from a SINGLE source-event-grounded goal, never
    // from whichever unrelated goal happens to be active in the database.
    private Guid? ResolveTaskGoalId(
        NIRAMindEvent mindEvent, Guid? ownedGoalId,
        IReadOnlyList<NIRAGoalApplyResult> goalResults,
        IReadOnlyList<NIRABranchApplyResult> branchResults)
    {
        if (ownedGoalId.HasValue) return ownedGoalId;
        if (mindEvent.Source != NIRAMindEventSource.User) return null;
        Guid[] ids = goalResults
            .Where(r => r.Goal?.SourceEventId == mindEvent.Id)
            .Select(r => r.Goal!.Id)
            .Concat(branchResults
                .Where(r => r.Branch?.SourceEventId == mindEvent.Id)
                .Select(r => r.Branch!.GoalId))
            .Concat(_goals.CurrentGoals
                .Where(g => g.SourceEventId == mindEvent.Id && !g.IsResolved)
                .Select(g => g.Id))
            .Where(id => id != Guid.Empty)
            .Distinct().ToArray();
        return ids.Length == 1 ? ids[0] : null;
    }

    private string? GetPersistentWorkLoopBlocker(Guid goalId)
    {
        // The same generic rule also guards new branch assignments. Work is
        // durably restored by NIRABranchWorkService after a process restart.
        return NIRABranchWorkLoopGuard.Evaluate(
            _branchWork.CurrentWork.Where(item => item.GoalId == goalId));
    }

    private bool TryGetStaleOwnedInternalEventReason(
        NIRAMindEvent mindEvent,
        out string reason)
    {
        reason =
            string.Empty;

        if (mindEvent.Source !=
            NIRAMindEventSource.Internal)
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
                    out NIRAGoalState? goal)
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
                    out NIRAGoalState? goal)
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
                        item.Status == NIRABranchWorkStatus.Running);

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
                    out NIRAGoalState? goal)
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
                    out NIRABranchState? branch)
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
                    out NIRABranchWorkItem? work)
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

            // Results can finish before cognition receives the notification.
            // If a NEWER work item already exists for this same sequential
            // branch, replaying the older result can choose an action using
            // superseded element refs. The durable record remains available
            // in branch-work context; only the redundant wake is discarded.
            if (_branchWork.CurrentWork.Any(next =>
                    next.BranchId == branchId &&
                    next.Id != workId &&
                    next.CreatedAtUtc > work.CreatedAtUtc))
            {
                reason = "A newer assignment superseded this branch result; " +
                         "do not reason from old browser element references.";
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
                    out NIRAGoalState? goal)
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
                    out NIRABranchState? branch)
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


    private static bool TryReadExplicitUserDestination(
        string? userText,
        out string target)
    {
        target = string.Empty;
        if (string.IsNullOrWhiteSpace(userText)) return false;
        // Only an actual URL typed in the current user event can ground this
        // destination. Strip query/fragment to avoid leaking URL-carried tokens
        // into the persistent goal, logs, or model-visible branch objective.
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(
                userText, @"https?://[^\s<>""']+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        string candidate = match.Value.TrimEnd('.', ',', ';', ')', ']');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;
        target = uri.GetLeftPart(UriPartial.Path);
        return true;
    }

    private static bool TryReadMetadataGuid(
        NIRAMindEvent mindEvent,
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
        NIRAMindEvent mindEvent)
    {
        if (mindEvent.Source !=
            NIRAMindEventSource.Internal)
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
        NIRAMindEvent mindEvent)
    {
        return IsInternalBranchLifecycleEvent(
                   mindEvent)
               ||
               (
                   mindEvent.Source == NIRAMindEventSource.Internal
                   &&
                   string.Equals(
                       mindEvent.Name,
                       "PersistentGoalWake",
                       StringComparison.Ordinal)
               );
    }


    private static IReadOnlyList<string>
        CollectRequiredNIRAReplyEvidenceFragments(
        IReadOnlyList<NIRAGoalProposal> goalProposals,
        IReadOnlyList<NIRABranchProposal> branchProposals)
    {
        List<string> fragments =
            new();

        foreach (NIRAGoalProposal proposal in goalProposals)
        {
            if (proposal.EvidenceSource ==
                    NIRAGoalEvidenceSource.NIRAReply
                &&
                !string.IsNullOrWhiteSpace(
                    proposal.EvidenceQuote))
            {
                fragments.Add(
                    proposal.EvidenceQuote.Trim());
            }
        }

        foreach (NIRABranchProposal proposal in branchProposals)
        {
            if (proposal.EvidenceSource ==
                    NIRABranchEvidenceSource.NIRAReply
                &&
                !string.IsNullOrWhiteSpace(
                    proposal.EvidenceQuote))
            {
                fragments.Add(
                    proposal.EvidenceQuote.Trim());
            }
        }

        return fragments
            .Distinct(
                StringComparer.Ordinal)
            .ToArray();
    }


    private static bool ShouldEmitReplyForInternalOrchestrationEvent(
        NIRAMindEvent mindEvent,
        NIRACognitionDecision decision,
        IReadOnlyList<NIRAGoalApplyResult> goalResults,
        IReadOnlyList<NIRABranchApplyResult> branchResults,
        bool independentlyCompletedBranchReply)
    {
        if (independentlyCompletedBranchReply)
        {
            return true;
        }

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
                    result.Goal?.Status == NIRAGoalStatus.Completed);

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
                        NIRAGoalStatus.Waiting
                        or NIRAGoalStatus.Blocked)
            ||
            branchResults.Any(
                result =>
                    result.Changed
                    &&
                    result.Branch?.Status is
                        NIRABranchStatus.Waiting
                        or NIRABranchStatus.Blocked);

        if (decision.State is
            NIRACognitionState.NeedUser
            or NIRACognitionState.Blocked)
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
        NIRAMindEvent mindEvent,
        IReadOnlyList<NIRAGoalApplyResult> goalResults,
        IReadOnlyList<NIRABranchApplyResult> branchResults)
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
                    result.Goal?.Status == NIRAGoalStatus.Completed);

        bool meaningfulBranchOutcome =
            branchResults.Any(
                result =>
                    result.Changed
                    &&
                    result.Branch?.Status is
                        NIRABranchStatus.Completed
                        or NIRABranchStatus.Failed);

        return meaningfulGoalOutcome
            || meaningfulBranchOutcome;
    }


    private static void AppendRepeatedCapabilityRequestEvidence(
        StringBuilder evidence,
        NIRACapabilityRequest request,
        NIRACapabilityResult? previousResult)
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
        NIRACognitionDecision decision,
        NIRACapabilityResult? lastCapabilityResult)
    {
        // A model may keep saying "done" after only a preparatory action.
        // Its draft reply must never become the safety guard's fallback proof.
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
            NIRACapabilityResultStatus.AuthorizationRequired =>
                $"I can't continue that action without authorization. {summary}",

            NIRACapabilityResultStatus.Rejected =>
                $"I couldn't complete that because the last action was rejected before it could run. {summary}",

            NIRACapabilityResultStatus.Failed =>
                $"I couldn't complete that because the last action failed. {summary}",

            NIRACapabilityResultStatus.Succeeded =>
                $"The last operation completed, but the requested objective is still not verified. I stopped when no further task work was queued rather than claiming success. Last verified operation: {summary}",

            _ =>
                $"I couldn't make further progress on that request, so I stopped instead of repeating the same step. {summary}"
        };
    }


    private static int AppendCapabilityEvidence(
        StringBuilder evidence,
        IReadOnlyList<NIRACapabilityResult> results)
    {
        int count =
            0;


        foreach (NIRACapabilityResult result in results)
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
        IReadOnlyList<NIRADynamicToolMutationResult> results)
    {
        int count = 0;
        foreach (NIRADynamicToolMutationResult result in results)
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
        IReadOnlyList<NIRADynamicToolExecutionResult> results)
    {
        int count = 0;
        foreach (NIRADynamicToolExecutionResult result in results)
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
            foreach (NIRADynamicToolStepExecutionResult step in result.StepResults)
            {
                evidence.AppendLine($"Step {step.StepId}: {step.Status} | {step.Summary}");
            }
            count++;
        }
        return count;
    }


    private static int AppendBranchWorkMutationEvidence(
        StringBuilder evidence,
        IReadOnlyList<NIRABranchWorkApplyResult> results)
    {
        int count =
            0;


        foreach (NIRABranchWorkApplyResult result in results)
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
        IReadOnlyList<NIRAGoalApplyResult> goalResults,
        IReadOnlyList<NIRABranchApplyResult> branchResults)
    {
        int count =
            0;


        foreach (NIRAGoalApplyResult result in goalResults)
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


        foreach (NIRABranchApplyResult result in branchResults)
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


    private NIRAInteractionContext? ResolveInteractionContext(
        NIRAMindEvent mindEvent)
    {
        if (!mindEvent.SocialEventId.HasValue)
        {
            return null;
        }


        return _interactionObservation.GetForEvent(
            mindEvent.SocialEventId.Value);
    }


    private void ApplyFirstCycleCharacterState(
        NIRAMindEvent mindEvent,
        NIRAInteractionContext? interaction,
        NIRACognitionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(
            mindEvent);

        if (interaction !=
                null
            &&
            decision.Appraisal !=
                null)
        {
            NIRAInteractionAppraisal appraisal =
                GroundAppraisal(
                    interaction,
                    decision.Appraisal);

            _characterDynamics.Apply(
                interaction,
                appraisal);

            return;
        }

        // Internal/task events do not get a fake social interaction. They may,
        // however, be meaningful experiences NIRA actually lived through. The
        // cognition model proposes bounded directional impact; relationship is
        // untouched and the authoritative character service owns persistence.
        if (mindEvent.Source !=
                NIRAMindEventSource.User
            &&
            decision.ExperienceAppraisal !=
                null)
        {
            _characterDynamics.ApplyExperience(
                decision.ExperienceAppraisal);
        }
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
        NIRAMindEvent mindEvent,
        NIRASocialEvent? sourceEvent,
        NIRACognitionContext context,
        NIRACognitionDecision finalDecision,
        string finalReply,
        CancellationToken cancellationToken)
    {
        try
        {
            Debug.WriteLine(
                $"[MemoryFormation] START | " +
                $"Run={runId} | " +
                $"Event='{mindEvent.Name}'");


            NIRAMemoryFormationResult formation =
                await _memoryFormation.FormAsync(
                    new NIRAMemoryFormationContext
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
                            context.MemorySearchEvidence,

                        DynamicToolContext =
                            context.DynamicToolContext,

                        DynamicToolEvidence =
                            context.DynamicToolEvidence
                    },
                    cancellationToken);


            IReadOnlyList<NIRALearnedSkillMutationResult> skillResults =
                await _skills.ApplyFormationProposalsAsync(
                    formation.LearnedSkillProposals,
                    cancellationToken);

            int appliedSkillChanges =
                skillResults.Count(result => result.Changed);

            foreach (NIRALearnedSkillMutationResult result in skillResults)
            {
                Debug.WriteLine(
                    $"[SkillFormation] {result.Action.ToString().ToUpperInvariant()} | " +
                    $"Skill='{result.Skill?.Name ?? "-"}' | Reason='{TrimLog(result.Reason)}'");
            }


            // Asking a question is an incomplete interaction, not evidence
            // of a newly developed NIRA taste. Do not reinforce the model's
            // own fallback decision as a personal preference.
            int appliedSelfPreferenceObservations =
                finalDecision.State == NIRACognitionState.NeedUser
                    ? 0
                    : await ApplySelfPreferenceFormationObservationsAsync(
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


            IReadOnlyList<NIRAMemoryCandidate> grounded =
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
                    $"CommitmentChanges={appliedCommitmentChanges} | " +
                    $"SkillChanges={appliedSkillChanges}");


                return;
            }


            foreach (
                NIRAMemoryCandidate candidate
                in grounded)
            {
                Debug.WriteLine(
                    $"[MemoryFormation] CANDIDATE | " +
                    $"Kind={candidate.Kind} | " +
                    $"Source={candidate.Provenance.SourceType} | " +
                    $"Canonical='{candidate.CanonicalKey ?? "-"}' | " +
                    $"Content='{TrimLog(candidate.Content)}'");
            }


            IReadOnlyList<NIRAMemoryConsolidationResult> results =
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
    // NIRAMindEvent owns the fresh event identity. The optional
    // social event contributes sequence information when present.
    //
    // The authoritative NIRASelfPreferenceService owns gradual
    // accumulation, duplicate prevention and persistence.
    // =========================================================

    private async Task<int>
        ApplySelfPreferenceFormationObservationsAsync(
            NIRAMindEvent mindEvent,
            NIRASocialEvent? sourceEvent,
            IReadOnlyList<NIRASelfPreferenceFormationProposal> proposals,
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
            NIRASelfPreferenceFormationProposal raw
            in proposals)
        {
            cancellationToken
                .ThrowIfCancellationRequested();


            NIRASelfPreferenceFormationProposal proposal;


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
                NIRAMemoryEvidenceBasis.NIRAInference)
            {
                Debug.WriteLine(
                    $"[SelfPreferenceFormation] REJECTED | " +
                    $"Key='{proposal.Key}' | " +
                    $"Basis={proposal.EvidenceBasis} | " +
                    $"Reason='NIRA self-preference observations require NIRAInference.'");


                continue;
            }


            NIRASelfPreferenceObservation observation =
                new NIRASelfPreferenceObservation
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


            NIRASelfPreferenceApplyResult result =
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
                    NIRASelfPreferenceApplyAction.Created
                    or NIRASelfPreferenceApplyAction.Updated
                    or NIRASelfPreferenceApplyAction.Established)
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
    // NIRALearnedPreference is deliberately rejected here.
    // It may only be synchronized from authoritative established
    // self-preference state in the later Step 5D.
    // =========================================================

    private static IReadOnlyList<NIRAMemoryCandidate>
        GroundMemoryFormationProposals(
            NIRASocialEvent? sourceEvent,
            IReadOnlyList<NIRAMemoryFormationProposal> proposals)
    {
        if (
            proposals ==
                null
            ||
            proposals.Count ==
                0)
        {
            return Array.Empty<NIRAMemoryCandidate>();
        }


        List<NIRAMemoryCandidate> grounded =
            new(
                proposals.Count);


        foreach (
            NIRAMemoryFormationProposal raw
            in proposals)
        {
            NIRAMemoryFormationProposal proposal =
                raw.Normalize();


            if (proposal.Kind ==
                NIRAMemoryKind.NIRALearnedPreference)
            {
                Debug.WriteLine(
                    $"[MemoryFormation] REJECTED | " +
                    $"Kind={proposal.Kind} | " +
                    $"Reason='Direct NIRALearnedPreference writes are disabled; use gradual self-preference observations.'");


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


            NIRAMemorySourceType sourceType =
                proposal.EvidenceBasis switch
                {
                    NIRAMemoryEvidenceBasis.UserExplicit =>
                        NIRAMemorySourceType.UserExplicit,

                    NIRAMemoryEvidenceBasis.NIRAInference =>
                        NIRAMemorySourceType.NIRAInference,

                    NIRAMemoryEvidenceBasis.SharedExperience =>
                        NIRAMemorySourceType.SharedExperience,

                    NIRAMemoryEvidenceBasis.SystemDerived =>
                        NIRAMemorySourceType.SystemDerived,

                    _ =>
                        NIRAMemorySourceType.Unknown
                };


            grounded.Add(
                proposal.ToCandidate() with
                {
                    Provenance =
                        new NIRAMemoryProvenance
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
        NIRAMemoryFormationProposal proposal,
        NIRASocialEvent? sourceEvent)
    {
        bool userEvent =
            sourceEvent?.Source ==
                NIRASocialEventSource.User
            &&
            sourceEvent.Kind ==
                NIRASocialEventKind.UserMessage;


        bool nonUserEvent =
            sourceEvent !=
                null
            &&
            sourceEvent.Source !=
                NIRASocialEventSource.User;


        if (
            proposal.EvidenceBasis ==
                NIRAMemoryEvidenceBasis.UserExplicit
            &&
            !userEvent)
        {
            return false;
        }


        if (
            proposal.EvidenceBasis ==
                NIRAMemoryEvidenceBasis.SystemDerived
            &&
            !nonUserEvent)
        {
            return false;
        }


        return proposal.Kind switch
        {
            NIRAMemoryKind.UserFact =>
                proposal.EvidenceBasis ==
                    NIRAMemoryEvidenceBasis.UserExplicit,

            NIRAMemoryKind.UserPreference =>
                proposal.EvidenceBasis ==
                    NIRAMemoryEvidenceBasis.UserExplicit,

            NIRAMemoryKind.NIRALearnedPreference =>
                false,

            NIRAMemoryKind.SharedExperience =>
                proposal.EvidenceBasis ==
                    NIRAMemoryEvidenceBasis.SharedExperience,

            NIRAMemoryKind.ImportantEvent =>
                proposal.EvidenceBasis is
                    NIRAMemoryEvidenceBasis.UserExplicit
                    or NIRAMemoryEvidenceBasis.SharedExperience
                    or NIRAMemoryEvidenceBasis.SystemDerived,

            NIRAMemoryKind.ProjectKnowledge =>
                true,

            _ =>
                false
        };
    }


    private static NIRAInteractionAppraisal GroundAppraisal(
        NIRAInteractionContext interaction,
        NIRACognitionAppraisalProposal proposal)
    {
        NIRASocialMeaning meaning =
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


        return new NIRAInteractionAppraisal
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
                NIRAAppraisalSource.Composite
        }
        .Normalize();
    }


    private async Task<int> AppendMemorySearchEvidenceAsync(
        StringBuilder evidence,
        IReadOnlyList<NIRAMemorySearchRequest> requests,
        HashSet<Guid> surfacedMemoryIds,
        CancellationToken cancellationToken)
    {
        int totalNewCandidates =
            0;


        foreach (
            NIRAMemorySearchRequest rawRequest
            in requests)
        {
            NIRAMemorySearchRequest request =
                rawRequest.Normalize();


            IReadOnlyList<NIRAMemorySearchResult> results =
                await _longTermMemory.SearchCandidatesAsync(
                    request,
                    cancellationToken);


            NIRAMemorySearchResult[] newResults =
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
                NIRACognitionMemoryFormatter.FormatSearchResults(
                    newResults));
        }


        return totalNewCandidates;
    }

    private async Task RecordResponseAsync(
        NIRAMindEvent mindEvent,
        string response)
    {
        if (mindEvent.Source ==
            NIRAMindEventSource.User)
        {
            _conversation.AddAssistantMessage(
                response);
        }


        _socialHistory.Record(
            NIRASocialEventSource.NIRA,
            NIRASocialEventKind.NIRAResponse,
            ResolveResponseEventName(
                mindEvent),
            mindEvent.TopicKey,
            response);


        await Task.CompletedTask;
    }


    private static string ResolveResponseEventName(
        NIRAMindEvent mindEvent)
    {
        return mindEvent.Source switch
        {
            NIRAMindEventSource.User =>
                "UserResponse",

            NIRAMindEventSource.Perception =>
                "PerceptionResponse",

            NIRAMindEventSource.Proactive =>
                "ProactiveResponse",

            _ =>
                "NIRAResponse"
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


    private static Guid? TryReadGuidMetadata(
        NIRAMindEvent mindEvent,
        string key)
    {
        if (mindEvent.Metadata.TryGetValue(
                key,
                out string? raw) &&
            Guid.TryParse(
                raw,
                out Guid parsed) &&
            parsed != Guid.Empty)
        {
            return parsed;
        }

        return null;
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


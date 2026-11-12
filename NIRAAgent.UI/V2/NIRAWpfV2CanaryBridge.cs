using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using NIRAAgent.Character.State;
using NIRAAgent.Goals;
using NIRAAgent.Branches;
using NIRAAgent.Self.Model;
using NIRAAgent.Capabilities;
using NIRAAgent.Authorization;
using NIRAAgent.Conversation;
using NIRAAgent.Diagnostics;
using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Mind;
using Nira.V2.Contracts;
using Nira.V2.General;
using Nira.V2.ModelProvider;
using Nira.V2.Runtime.AgentLoop;
using Nira.V2.Web;
using Nira.V2.Windows;
using Nira.V2.Filesystem;
using Nira.V2.LegacyAdapters;

namespace NIRAAgent.UI.V2;

/// <summary>
/// In-process WPF/agent-loop adapter. No new character, memory database, window or
/// autonomous authority is created. Runtime-selection is host-gated; tool choice is
/// model-driven through registered host-scoped capabilities.
/// </summary>
public sealed class NIRAWpfV2CanaryBridge : INIRAGeneralRuntimeBridge
{
    private readonly NIRAV2ReadOnlyContextBuilder _contextBuilder;
    private readonly NIRAV2LifecycleContextBuilder _lifecycleContextBuilder;
    private readonly NIRACapabilityService _authoritativeLegacyCapabilities;
    private readonly NIRACapabilityApprovalBroker _approvalBroker;
    private readonly NIRAGoalService _authoritativeGoals;
    private readonly NIRAGoalStore _authoritativeGoalStore;
    private readonly NIRABranchService _authoritativeBranches;
    private readonly NIRABranchWorkService _authoritativeBranchWork;
    private readonly NIRABranchWorkStore _authoritativeBranchWorkStore;

    public NIRAWpfV2CanaryBridge(ConversationManager conversation,
        NIRACharacterStateService character, NIRALongTermMemoryService memories,
        NIRAConversationArchiveStore archive, NIRACapabilityService legacyCapabilities,
        NIRACapabilityApprovalBroker approvalBroker, NIRAGoalService goals,
        NIRAGoalStore goalStore, NIRABranchService branches, NIRASelfModelService selfModel,
        NIRABranchWorkService branchWork, NIRABranchWorkStore branchWorkStore)
    {
        _authoritativeLegacyCapabilities = legacyCapabilities ?? throw new ArgumentNullException(nameof(legacyCapabilities));
        _approvalBroker = approvalBroker ?? throw new ArgumentNullException(nameof(approvalBroker));
        _authoritativeGoals = goals ?? throw new ArgumentNullException(nameof(goals));
        _authoritativeGoalStore = goalStore ?? throw new ArgumentNullException(nameof(goalStore));
        _authoritativeBranches = branches ?? throw new ArgumentNullException(nameof(branches));
        _authoritativeBranchWork = branchWork ?? throw new ArgumentNullException(nameof(branchWork));
        _authoritativeBranchWorkStore = branchWorkStore ?? throw new ArgumentNullException(nameof(branchWorkStore));
        _contextBuilder = new NIRAV2ReadOnlyContextBuilder(conversation, character, memories, archive);
        _lifecycleContextBuilder = new NIRAV2LifecycleContextBuilder(goals, branches, selfModel);
    }

    public bool CanHandle(NIRAMindEvent mindEvent) =>
        NIRAGeneralRuntimeRoutePolicy.TryGetRequest(mindEvent,
            Environment.GetEnvironmentVariable("NIRA_V2_WPF_CANARY"),
            Environment.GetEnvironmentVariable("NIRA_AGENT_RUNTIME"), out _);

    public async IAsyncEnumerable<NIRAOutputChunk> RunAsync(NIRAMindEvent mindEvent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!NIRAGeneralRuntimeRoutePolicy.TryGetRequest(mindEvent,
            Environment.GetEnvironmentVariable("NIRA_V2_WPF_CANARY"),
            Environment.GetEnvironmentVariable("NIRA_AGENT_RUNTIME"), out string request))
            throw new InvalidOperationException("V2 runtime route is not authorized.");

        var channel = Channel.CreateUnbounded<NIRAOutputChunk>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        Task producer = Task.Run(() => ProduceAsync(mindEvent, mindEvent.Content, request,
            channel.Writer, cancellationToken),
            cancellationToken);
        await foreach (NIRAOutputChunk chunk in channel.Reader.ReadAllAsync(cancellationToken))
            yield return chunk;
        await producer;
    }

    private async Task ProduceAsync(NIRAMindEvent mindEvent, string submittedInput, string request,
        ChannelWriter<NIRAOutputChunk> output,
        CancellationToken ct)
    {
        try
        {
            // Provider selection is a transport concern. The same V2 runtime,
            // existing identity, tools, approvals and checkpoints remain authoritative.
            // No automatic failover: never replay a partially completed agent action.
            NiraV2ProviderSelection selection;
            try { selection = NiraV2ProviderSelection.FromEnvironment(); }
            catch (ArgumentException)
            {
                WriteText(output, "NIRA V2 model provider configuration is invalid. Use Start-NiraNvidia.ps1.");
                return;
            }
            if (!selection.IsReady)
            {
                WriteText(output, "NIRA V2 is unavailable: the selected model provider's API key isn't configured for this Windows session.");
                return;
            }
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                WriteText(output, "NIRA V2 cannot access the protected local-state folder.");
                return;
            }
            // Isolated encrypted canary checkpoints; legacy conversation/history is not copied.
            string root = Path.Combine(baseFolder, "NIRA", "V2", "WpfCanarySecure");
            var protector = new WindowsDpapiDataProtector();
            var vault = new AgentPayloadVault(Path.Combine(root, "vault"), protector);
            using var modelHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var searchHandler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var modelHttp = new HttpClient(modelHandler) { Timeout = TimeSpan.FromMinutes(4) };
            using var searchHttp = new HttpClient(searchHandler) { Timeout = TimeSpan.FromSeconds(45) };
            var capabilities = new List<IGeneralCapability>
            {
                new CalculatorCapability(vault), new PlatformCapability(vault)
            };
            if (!string.IsNullOrWhiteSpace(TavilySearchTool.ReadKey()))
                capabilities.Add(new SearchCapability(new TavilySearchTool(searchHttp, vault)));
            // The independent PC consent flag is intentionally unchanged.
            capabilities.AddRange(WindowsObservationRegistration.Create(vault,
                Environment.GetEnvironmentVariable("NIRA_V2_PC_OBSERVATION")));
            // One-turn read-only filesystem grant. Registered ONLY if the owner
            // opted in AND this particular user turn names exactly one known-folder
            // relative path; old memories/tool output never grant a file scope.
            capabilities.AddRange(RequestedPathObservationRegistration.Create(vault,
                Environment.GetEnvironmentVariable("NIRA_V2_FILE_OBSERVATION"), request));
            // Phase 4E: first real legacy-service ownership adapter. V2 chooses whether
            // to read, but the existing NIRA authority, audit and handler execute it.
            // This is a separate opt-in, and is registered only for one exact user path.
            capabilities.AddRange(LegacyTextReadRegistration.Create(vault,
                new NIRALegacyTextReadExecutor(_authoritativeLegacyCapabilities),
                Environment.GetEnvironmentVariable("NIRA_V2_LEGACY_TEXT_READ"), request));
            // Phase 4E.2: the existing NIRA capability service owns and audits the
            // exact-directory listing; a model cannot supply a path, glob or recursion.
            capabilities.AddRange(LegacyDirectoryListRegistration.Create(vault,
                new NIRALegacyDirectoryListExecutor(_authoritativeLegacyCapabilities),
                Environment.GetEnvironmentVariable("NIRA_V2_LEGACY_DIRECTORY_LIST"), request));
            // Phase 4E.3: first integrity prerequisite for verified actions.
            // Host-held exact path, real legacy permission service, SHA-only output.
            capabilities.AddRange(LegacyFileHashRegistration.Create(vault,
                new NIRALegacyFileHashExecutor(_authoritativeLegacyCapabilities),
                Environment.GetEnvironmentVariable("NIRA_V2_LEGACY_FILE_HASH"), request));
            // Phase 4F: exactly one host-bound new .txt file, behind TWO guards:
            // independent per-process consent and the existing owner approval toast.
            var createTools = LegacyTextCreateRegistration.Create(vault,
                new NIRALegacyTextCreateExecutor(_authoritativeLegacyCapabilities),
                Environment.GetEnvironmentVariable("NIRA_V2_LEGACY_TEXT_CREATE"), request);
            capabilities.AddRange(createTools);
            // Phase 4G: one integrated, host-gated capability batch. Browser navigation
            // is limited to the HTTPS URL typed in this turn; DOCX is bound to one
            // known-folder path; desktop UIA is bound to one named application.
            // Existing NIRA retains execution, authorization and audit ownership.
            var g4Executor = new NIRALegacyPhase4GExecutor(_authoritativeLegacyCapabilities);
            capabilities.AddRange(LegacyPhase4GRegistration.CreateBrowser(vault, g4Executor,
                Environment.GetEnvironmentVariable("NIRA_V2_G4_BROWSER"), request));
            var documentTools = LegacyPhase4GRegistration.CreateDocuments(vault, g4Executor,
                Environment.GetEnvironmentVariable("NIRA_V2_G4_DOCX_INSPECT"),
                Environment.GetEnvironmentVariable("NIRA_V2_G4_DOCX_CREATE"), request);
            capabilities.AddRange(documentTools);
            var desktopTools = LegacyPhase4GRegistration.CreateDesktopUi(vault, g4Executor,
                Environment.GetEnvironmentVariable("NIRA_V2_G4_DESKTOP_UI"), request);
            capabilities.AddRange(desktopTools);
            var docxCreateTool = documentTools.OfType<LegacyPhase4GDocxCreateCapability>()
                .SingleOrDefault();
            var desktopActivateTool = desktopTools.OfType<LegacyPhase4GDesktopActivateCapability>()
                .SingleOrDefault();
            // If a requested exact file capability was not registered, report the
            // actual host admission result. Never ask the model to improvise manual
            // steps or claim that an unobserved write/read happened.
            string? admission = LegacyPhase4GRegistration.ExplainUnavailableFileAction(
                request, createTools.Count == 1,
                documentTools.Any(t => t.Spec.Id == "legacy.docx.inspect_exact"),
                docxCreateTool is not null);
            if (admission is not null)
            {
                WriteText(output, admission);
                return;
            }
            // Phase 4H.2: one exact user-authored goal, using the EXISTING goal store.
            // Three independent per-launch gates; the model controls no objective,
            // success criterion, stored ID, or approval token.
            var goalExecutor = new NIRALegacyGoalCreateExecutor(_authoritativeGoals,
                _authoritativeGoalStore);
            string? goalConsent = Environment.GetEnvironmentVariable("NIRA_V2_GOAL_CREATE");
            bool lifecycleConsented = NIRAV2LifecycleContextPolicy.IsAllowed(
                Environment.GetEnvironmentVariable("NIRA_V2_LIFECYCLE_CONTEXT"),
                Environment.GetEnvironmentVariable("NIRA_V2_PERSONAL_CONTEXT"));
            var goalTools = LegacyGoalCreateRegistration.Create(vault, goalExecutor,
                goalConsent, lifecycleConsented, request, mindEvent);
            var goalCreateTool = goalTools.OfType<LegacyGoalCreateCapability>().SingleOrDefault();
            capabilities.AddRange(goalTools);
            string? goalAdmission = LegacyGoalCreateRegistration.ExplainUnavailable(
                request, goalConsent, lifecycleConsented, goalCreateTool is not null, goalExecutor);
            if (goalAdmission is not null)
            {
                WriteText(output, goalAdmission);
                return;
            }
            // Phase 4H.3: explicitly authorized goal-linked branch creation. Uses
            // the existing branch service and its existing goal SQLite database;
            // the separate store instance is used ONLY for independent readback.
            var branchStore = new NIRABranchStore(_authoritativeGoalStore);
            var branchExecutor = new NIRALegacyBranchCreateExecutor(_authoritativeBranches,
                branchStore, _authoritativeGoals);
            string? branchConsent = Environment.GetEnvironmentVariable("NIRA_V2_BRANCH_CREATE");
            var branchTools = LegacyBranchCreateRegistration.Create(vault, branchExecutor,
                branchConsent, lifecycleConsented, request, mindEvent);
            var branchCreateTool = branchTools.OfType<LegacyBranchCreateCapability>()
                .SingleOrDefault();
            capabilities.AddRange(branchTools);
            string? branchAdmission = LegacyBranchCreateRegistration.ExplainUnavailable(
                request, branchConsent, lifecycleConsented, branchCreateTool is not null);
            if (branchAdmission is not null)
            {
                WriteText(output, branchAdmission);
                return;
            }
            // Phase 4H.4: one exact owner-supplied progress/waiting/blocker mutation
            // on an EXISTING goal or branch. No activation, completion or scheduling.
            var lifecycleExecutor = new NIRALegacyLifecycleMutationExecutor(
                _authoritativeGoals, _authoritativeBranches, _authoritativeGoalStore, branchStore);
            string? lifecycleMutationConsent = Environment.GetEnvironmentVariable(
                "NIRA_V2_LIFECYCLE_MUTATION");
            var lifecycleTools = LegacyLifecycleMutationRegistration.Create(vault,
                lifecycleExecutor, lifecycleMutationConsent, lifecycleConsented, request, mindEvent);
            var lifecycleTool = lifecycleTools.OfType<LegacyLifecycleMutationCapability>()
                .SingleOrDefault();
            capabilities.AddRange(lifecycleTools);
            string? lifecycleAdmission = LegacyLifecycleMutationRegistration.ExplainUnavailable(
                request, lifecycleMutationConsent, lifecycleConsented, lifecycleTool is not null);
            if (lifecycleAdmission is not null)
            {
                WriteText(output, lifecycleAdmission);
                return;
            }
            // Phase 4H.5: current-turn, exact GUID, read-only verification of
            // authoritative SQLite state versus the existing live owner.
            var lifecycleInspectExecutor = new NIRALegacyLifecycleInspectExecutor(
                _authoritativeGoals, _authoritativeBranches, _authoritativeGoalStore, branchStore);
            string? lifecycleInspectConsent = Environment.GetEnvironmentVariable(
                "NIRA_V2_LIFECYCLE_INSPECT");
            var lifecycleInspectTools = LegacyLifecycleInspectRegistration.Create(vault,
                lifecycleInspectExecutor, lifecycleInspectConsent, lifecycleConsented,
                request, mindEvent);
            capabilities.AddRange(lifecycleInspectTools);
            string? lifecycleInspectAdmission = LegacyLifecycleInspectRegistration.ExplainUnavailable(
                request, lifecycleInspectTools.Count == 1, lifecycleInspectConsent, lifecycleConsented);
            if (lifecycleInspectAdmission is not null)
            {
                WriteText(output, lifecycleInspectAdmission);
                return;
            }
            // The direct user's current exact readback is ALREADY checked by registration:
            // process opt-in, lifecycle opt-in, user-event binding and strict GUID syntax.
            // Use the same host-scoped read-only tool without relying on model selection.
            // This is not an authorization bypass and never publishes unverified state.
            if (lifecycleInspectTools.Count == 1)
            {
                if (!LegacyLifecycleInspectRegistration.TryParse(request, out var exactInspection) ||
                    exactInspection is null)
                {
                    WriteText(output, "Exact lifecycle inspection request rejected; no state was changed.");
                    return;
                }
                string verifiedReport = await InspectExactLifecycleAsync(vault,
                    (LegacyLifecycleInspectCapability)lifecycleInspectTools[0], exactInspection, ct);
                WriteText(output, verifiedReport);
                return;
            }
            // Phase 4H.6: independent terminal branch-completion gate; all writes
            // remain owned by existing NIRABranchService and its SQLite store.
            var completionExecutor = new NIRALegacyBranchCompletionExecutor(
                _authoritativeBranches, branchStore, _authoritativeBranchWork,
                _authoritativeBranchWorkStore, _authoritativeGoals, _authoritativeGoalStore);
            string? completionConsent = Environment.GetEnvironmentVariable(
                "NIRA_V2_BRANCH_COMPLETION");
            var completionTools = LegacyBranchCompletionRegistration.Create(vault,
                completionExecutor, completionConsent, lifecycleConsented, request, mindEvent);
            var completionTool = completionTools.OfType<LegacyBranchCompletionCapability>()
                .SingleOrDefault();
            capabilities.AddRange(completionTools);
            string? completionAdmission = LegacyBranchCompletionRegistration.ExplainUnavailable(
                request, completionConsent, lifecycleConsented, completionTool is not null);
            if (completionAdmission is not null)
            {
                WriteText(output, completionAdmission);
                return;
            }
            var registry = new CapabilityRegistry(capabilities);
            // Personal legacy context is read ONLY after a separate explicit host consent.
            // It is not written to any V2 memory store. Model requests still leave the PC.
            NIRAV2ReadOnlyContext continuity = await _contextBuilder.BuildAsync(
                submittedInput, request,
                Environment.GetEnvironmentVariable("NIRA_V2_PERSONAL_CONTEXT"), ct);
            // Phase 4H: one read-only snapshot from the EXISTING goal, branch
            // and accepted-commitment services. This is never a new V2 state owner.
            // Both explicit per-process consents and a lifecycle-related current
            // user turn are required. Do not let archived prompts grant access.
            string lifecycle = _lifecycleContextBuilder.Build(
                Environment.GetEnvironmentVariable("NIRA_V2_LIFECYCLE_CONTEXT"),
                Environment.GetEnvironmentVariable("NIRA_V2_PERSONAL_CONTEXT"), request);
            string combinedContext = NIRAV2LifecycleContextPolicy.MergeWithHistory(
                continuity.UntrustedHistory, lifecycle);
            // Stable character definition is not a private SQLite/memory read.
            // It MUST be present even when the owner did not opt into sharing
            // personal mood, conversations, goals, or long-term memory.
            string trustedIdentity = !string.IsNullOrWhiteSpace(continuity.TrustedIdentity)
                ? continuity.TrustedIdentity
                : await NIRAV2ReadOnlyContextBuilder.LoadTrustedIdentityOnlyAsync(ct);
            // This is intentionally still the existing tested V2 general reasoner.
            // Its historical class name does not select or restrict the provider.
            var reasoner = new GeneralModelReasoner(modelHttp, selection.Options,
                registry, vault, maximumTools: 8,
                cloudKey: selection.KeySource,
                trustedIdentity: trustedIdentity,
                untrustedHistory: combinedContext,
                allowExactTextCreate: createTools.Count == 1,
                allowExactDocxCreate: docxCreateTool is not null,
                allowExactDesktopActivate: desktopActivateTool is not null,
                allowExactGoalCreate: goalCreateTool is not null,
                allowExactBranchCreate: branchCreateTool is not null,
                allowExactLifecycleMutation: lifecycleTool is not null,
                allowExactBranchCompletion: completionTool is not null,
                previewText: draft => output.TryWrite(new NIRAOutputChunk
                {
                    Source = NIRAMindEventSource.User,
                    Type = NIRAOutputChunkType.Preview,
                    Content = draft
                }));
            var progress = new DirectProgress(entry =>
            {
                string? safeMessage = entry.Code switch
                {
                    "REASONING_STARTED" => "Thinking through the request...",
                    "TOOL_DISPATCHED" => "Using a permitted tool...",
                    "TOOL_SUCCEEDED" => "Reviewing the observed result...",
                    "RUN_COMPLETED" => "Finishing the answer...",
                    _ => null
                };
                if (safeMessage is not null)
                    output.TryWrite(new NIRAOutputChunk
                    {
                        Source = NIRAMindEventSource.User,
                        Type = NIRAOutputChunkType.Progress,
                        Content = safeMessage
                    });
            });
            IAgentPermissionGate textPermissionGate = createTools.Count == 1
                ? new NIRAV2FileCreatePermissionGate(vault,
                    (LegacyTextCreateCapability)createTools[0], _approvalBroker)
                : new ObserveOnlyPermissionGate();
            var phase4GPermissionGate = docxCreateTool is not null || desktopActivateTool is not null
                ? new NIRAV2Phase4GPermissionGate(textPermissionGate, vault,
                    docxCreateTool, desktopActivateTool, _approvalBroker)
                : null;
            IAgentPermissionGate permissionGate = phase4GPermissionGate ?? textPermissionGate;
            if (goalCreateTool is not null)
                permissionGate = new NIRAV2GoalCreatePermissionGate(permissionGate,
                    vault, goalCreateTool, _approvalBroker);
            if (branchCreateTool is not null)
                permissionGate = new NIRAV2BranchCreatePermissionGate(permissionGate,
                    vault, branchCreateTool, _approvalBroker);
            if (lifecycleTool is not null)
                permissionGate = new NIRAV2LifecycleMutationPermissionGate(permissionGate,
                    vault, lifecycleTool, _approvalBroker);
            if (completionTool is not null)
                permissionGate = new NIRAV2BranchCompletionPermissionGate(permissionGate,
                    vault, completionTool, _approvalBroker);
            var engine = new AgentLoopEngine(reasoner, new FileAgentStateStore(root, protector),
                permissionGate, registry.Tools, new AgentLoopLimits(15, 8), progress);
            AgentRunSnapshot run = await engine.StartAsync(request, ct);
            NIRADiagnosticTrace.Event("NVIDIA-V2-ROUND",
                $"ModelRequests={reasoner.ModelRequests} " +
                $"FirstTextMs={reasoner.LastFirstTextMilliseconds} " +
                $"LastRoundMs={reasoner.LastRoundMilliseconds} " +
                $"Status={run.Status}");
            if (completionTool is not null)
            {
                AgentObservation? observed = run.Observations.LastOrDefault(o =>
                    o.ToolId == "legacy.lifecycle.branch.complete_exact" && o.Succeeded &&
                    !string.IsNullOrWhiteSpace(o.ResultReference));
                if (observed is not null)
                {
                    try
                    {
                        JsonElement saved = await vault.ReadResultAsync(run.Id,
                            "legacy.lifecycle.branch.complete_exact", observed.ResultReference!, ct);
                        if (saved.GetProperty("outcome").GetString() !=
                                "authoritative_sqlite_and_live_verified" ||
                            saved.GetProperty("status").GetString() != "Completed")
                            throw new InvalidDataException("Unverified branch completion receipt.");
                        WriteText(output, "Verified COMPLETED in NIRA's existing SQLite store: " +
                            saved.GetProperty("branchId").GetString() +
                            Environment.NewLine + "Goal: " + saved.GetProperty("goalId").GetString() +
                            Environment.NewLine + "Result: " + saved.GetProperty("result").GetString() +
                            Environment.NewLine + "Evidence: " + saved.GetProperty("evidence").GetString() +
                            Environment.NewLine + "Evidence count: " + saved.GetProperty("evidenceCount").GetInt32() +
                            Environment.NewLine + "The text is user-attested; no separate real-world " +
                            "proof or automatic goal completion is claimed.");
                        return;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or
                        JsonException or ArgumentException or KeyNotFoundException)
                    {
                        // A modifying action may already have committed. Never claim no change.
                        WriteText(output, "Branch completion may have committed but the receipt was " +
                            "not verifiable. Inspect the branch using the exact SQLite readback " +
                            "tool before doing anything else (NEEDS_RECONCILIATION).");
                        return;
                    }
                }
                if (run.Status == AgentRunStatus.NeedsReconciliation)
                {
                    WriteText(output, "Branch completion outcome is unknown (NEEDS_RECONCILIATION). " +
                        "Do NOT repeat it; inspect authoritative SQLite status first.");
                    return;
                }
            }
            if (lifecycleInspectTools.Count == 1)
            {
                AgentObservation? observed = run.Observations.LastOrDefault(o =>
                    o.ToolId == "legacy.lifecycle.inspect_exact" && o.Succeeded &&
                    !string.IsNullOrWhiteSpace(o.ResultReference));
                if (observed is null)
                {
                    WriteText(output, "NIRA could not verify that saved item from authoritative SQLite. " +
                        "No stored status or progress was reported, and no state was modified.");
                    return;
                }
                try
                {
                    JsonElement saved = await vault.ReadResultAsync(run.Id,
                        "legacy.lifecycle.inspect_exact", observed.ResultReference!, ct);
                    if (!saved.GetProperty("verifiedAgainstLiveState").GetBoolean() ||
                        !saved.GetProperty("inspectionOnly").GetBoolean())
                        throw new InvalidDataException("Unverified lifecycle receipt.");
                    string kind = saved.GetProperty("itemType").GetString() ?? "item";
                    string id = saved.GetProperty("itemId").GetString() ?? "";
                    string status = saved.GetProperty("status").GetString() ?? "unknown";
                    int count = saved.GetProperty("evidenceCount").GetInt32();
                    string last = saved.GetProperty("lastEvidenceSummary").GetString() ?? "";
                    string linked = saved.GetProperty("goalId").ValueKind == JsonValueKind.String
                        ? (saved.GetProperty("goalId").GetString() ?? "") : "";
                    string result =
                        "Verified saved " + kind + " " + id + " from NIRA's existing SQLite store." +
                        Environment.NewLine + "Status: " + status +
                        Environment.NewLine + "Evidence count: " + count +
                        Environment.NewLine + "Last stored progress: " + (last.Length > 0 ? last : "(none)") +
                        (linked.Length > 0 ? Environment.NewLine + "Owning goal ID: " + linked : "") +
                        Environment.NewLine + "This records stored evidence; it does not independently prove the work occurred. " +
                        "No state was changed.";
                    WriteText(output, result);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or
                    ArgumentException or KeyNotFoundException)
                {
                    WriteText(output, "The saved lifecycle receipt was not independently verifiable. " +
                        "No status or evidence count is being claimed. No state was changed.");
                }
                return;
            }

            if (run.Status == AgentRunStatus.Completed && !string.IsNullOrWhiteSpace(run.FinalAnswer))
            {
                output.TryWrite(new NIRAOutputChunk
                {
                    RunId = run.Id,
                    Source = NIRAMindEventSource.User,
                    Type = NIRAOutputChunkType.Text,
                    Content = run.FinalAnswer,
                    SpeechContent = run.FinalAnswer
                });
                output.TryWrite(new NIRAOutputChunk
                {
                    RunId = run.Id,
                    Source = NIRAMindEventSource.User,
                    Type = NIRAOutputChunkType.Completed
                });
            }
            else if (run.Status == AgentRunStatus.NeedsReconciliation)
            {
                // A modifying action may have reached the filesystem. Never say
                // no changes occurred and NEVER automatically retry it.
                WriteText(output, "NIRA cannot confirm whether that action changed anything. " +
                    "Do not repeat it automatically. Inspect the target and execution audit first " +
                    "(NEEDS_RECONCILIATION).");
            }
            else if (run.Status == AgentRunStatus.AwaitingAuthorization)
            {
                if (phase4GPermissionGate?.DesktopObservationMissing == true)
                    WriteText(output, "I did not activate the application. The exact window handle " +
                        "was not obtained from a verified desktop.inspect observation in this run, " +
                        "so no owner approval was requested and no modifying tool was dispatched.");
                else
                    WriteText(output, "The proposed action did not receive Allow Once approval " +
                        "(denied or expired). No modifying tool was dispatched.");
            }
            else if (run.Status == AgentRunStatus.Incomplete &&
                     run.LastErrorCode == "G4_DESKTOP_ACTIVATION_NO_EFFECT")
            {
                WriteText(output, "Windows did not activate the requested application window. " +
                    "The audited desktop action reported no dispatch and no system change. " +
                    "No automatic retry was attempted; focus the application manually if needed.");
            }
            else
            {
                string code = CleanCode(run.LastErrorCode ?? reasoner.LastFailureCode);
                WriteText(output, "The V2 agent couldn't complete this request (" + code +
                    "). Check the activity log before repeating a modifying request.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            output.TryWrite(new NIRAOutputChunk
            {
                Source = NIRAMindEventSource.User,
                Type = NIRAOutputChunkType.Cancelled
            });
        }
        catch (Exception)
        {
            // No exception bodies, filesystem paths, cloud response bodies, or credentials in UI.
            WriteText(output, "NIRA encountered a runtime or network error. If a file action was requested, inspect its destination and audit before trying again.");
        }
        finally
        {
            output.TryComplete();
        }
    }

    // Deterministic host execution for one explicitly authorized read-only inspection.
    // Preserve the same DPAPI-backed scoped tool receipt as the model-driven path.
    private static async Task<string> InspectExactLifecycleAsync(
        AgentPayloadVault vault, LegacyLifecycleInspectCapability tool,
        ExactLifecycleInspectRequest expected, CancellationToken ct)
    {
        try
        {
            Guid runId = Guid.NewGuid();
            JsonElement args = JsonSerializer.SerializeToElement(new { });
            string reference = await vault.SaveArgumentAsync(runId, tool.Spec.Id, args, ct);
            AgentToolReceipt observation = await tool.ExecuteAsync(reference, ct);
            if (!observation.Succeeded || observation.OutcomeConfirmed ||
                string.IsNullOrWhiteSpace(observation.ResultReference))
            {
                return "Saved lifecycle inspection did not verify (" +
                    CleanCode(observation.OutcomeCode) +
                    "). No saved status was claimed and no lifecycle state was modified.";
            }

            JsonElement record = await vault.ReadResultAsync(runId, tool.Spec.Id,
                observation.ResultReference!, ct);
            if (record.ValueKind != JsonValueKind.Object ||
                record.GetProperty("source").GetString() != "authoritative_existing_nira_sqlite_store" ||
                !record.GetProperty("verifiedAgainstLiveState").GetBoolean() ||
                !record.GetProperty("inspectionOnly").GetBoolean() ||
                record.GetProperty("itemId").GetString() != expected.Id.ToString("D") ||
                record.GetProperty("itemType").GetString() != expected.Kind.ToString())
            {
                return "Saved lifecycle inspection returned an unverified or mismatched " +
                    "receipt. No stored status was claimed and no state was modified.";
            }

            string kind = record.GetProperty("itemType").GetString() ?? "item";
            string id = record.GetProperty("itemId").GetString() ?? "";
            string status = record.GetProperty("status").GetString() ?? "unknown";
            int count = record.GetProperty("evidenceCount").GetInt32();
            if (count < 0 || count > 1_000_000)
                return "Saved lifecycle evidence count invalid; no state was modified.";
            string last = record.GetProperty("lastEvidenceSummary").GetString() ?? "";
            JsonElement linkedField = record.GetProperty("goalId");
            string linked = linkedField.ValueKind == JsonValueKind.String
                ? linkedField.GetString() ?? "" : "";
            return "Verified saved " + kind + " " + id +
                " from NIRA's existing SQLite store." +
                Environment.NewLine + "Status: " + status +
                Environment.NewLine + "Evidence count: " + count +
                Environment.NewLine + "Last stored progress: " +
                (last.Length > 0 ? last : "(none)") +
                (linked.Length > 0 ? Environment.NewLine + "Owning goal ID: " + linked : "") +
                Environment.NewLine + "This records stored evidence; it does not " +
                "independently prove the work occurred. No state was changed.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException or InvalidOperationException or
            ArgumentException or KeyNotFoundException)
        {
            return "The host attempted the read-only SQLite lifecycle inspection, " +
                "but could not verify its receipt. No stored status was claimed " +
                "and no lifecycle state was modified.";
        }
    }

    // Direct host-verified outcomes must finish the SAME chat turn as model replies.
    // Without Completed, they were visible but were not archived as completed turns.
    private static void WriteText(ChannelWriter<NIRAOutputChunk> writer, string text)
    {
        writer.TryWrite(new NIRAOutputChunk
        {
            Source = NIRAMindEventSource.User,
            Type = NIRAOutputChunkType.Text,
            Content = text,
            SpeechContent = text
        });
        writer.TryWrite(new NIRAOutputChunk
        {
            Source = NIRAMindEventSource.User,
            Type = NIRAOutputChunkType.Completed
        });
    }

    private static string CleanCode(string? code) =>
        code is { Length: > 0 and <= 60 } &&
        Regex.IsMatch(code, "^[A-Z0-9_]+$", RegexOptions.CultureInvariant)
            ? code : "INCOMPLETE";

    private sealed class DirectProgress(Action<AgentActivityEvent> report) : IProgress<AgentActivityEvent>
    {
        public void Report(AgentActivityEvent value) => report(value);
    }
}

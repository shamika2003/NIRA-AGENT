/*
 * filename: App.xaml.cs
 */

using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NIRAAgent.AI.Models;
using NIRAAgent.AI.Cognition;
using NIRAAgent.Capabilities;
using NIRAAgent.Authorization;
using NIRAAgent.Artifacts;
using NIRAAgent.Images;
using NIRAAgent.Agent.State;
using NIRAAgent.Character.Dynamics;
using NIRAAgent.Character.History;
using NIRAAgent.Character.Interaction;
using NIRAAgent.Character.State;
using NIRAAgent.Conversation;
using NIRAAgent.Memory.LongTerm;
using NIRAAgent.Goals;
using NIRAAgent.Branches;
using NIRAAgent.Browser;
using NIRAAgent.Mind;
using NIRAAgent.Presentation;
using NIRAAgent.PC.Awareness;
using NIRAAgent.Perception;
using NIRAAgent.Semantic;
using NIRAAgent.Self.Preferences;
using NIRAAgent.Self.Model;
using NIRAAgent.Settings;
using NIRAAgent.UI.Companion;
using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Presentation;
using NIRAAgent.UI.AgentRuntime;
using NIRAAgent.UI.Theming;
using NIRAAgent.Voice;
using NIRAAgent.Vision;
using NIRAAgent.Embodiment;
using NIRAAgent.Embodiment.Body;
using NIRAAgent.Tools;
using NIRAAgent.Skills;
using NIRAAgent.Temporal;
using NIRAAgent.Integrations.Elvara;
using NIRAAgent.Integrations.Elvara.Bridge;
using NIRAAgent.Integrations.Elvara.Apps.TradeAI;
using NIRAAgent.UI.Vision;
using NIRAAgent.UI.Visuals;
using NIRAAgent.Diagnostics;
using NIRAAgent.Runtime.General;
using NIRAAgent.Runtime.HostAdapters;
using NIRAAgent.Runtime.ModelProvider;
using NIRAAgent.Runtime.Runtime.AgentLoop;

using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace NIRAAgent.UI;

public partial class App : WpfApplication
{
    private IHost? _host;


    protected override async void OnStartup(
        WpfStartupEventArgs e)
    {
        // Capture managed crashes *before* the host and UI are initialized.
        // Do not swallow exceptions, change response logic or log private prompts.
        DispatcherUnhandledException += (_, args) =>
            NIRADiagnosticTrace.Event("UI-UNHANDLED-EXCEPTION",
                "Error=" + args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            NIRADiagnosticTrace.Event("PROCESS-UNHANDLED-EXCEPTION",
                "Terminating=" + args.IsTerminating + " Error=" + args.ExceptionObject);
        base.OnStartup(
            e);


        try
        {
            NIRAThemeManager.Initialize();

            // UI-only lab: no agent host, cognition, searches or LLM calls.
            if (System.Array.Exists(e.Args, arg =>
                string.Equals(arg, "--video-deck-lab", StringComparison.OrdinalIgnoreCase)))
            {
                var previewWindow = NIRAAgent.UI.Presentation.NIRAVideoDepthLabWindow.Create();
                MainWindow = previewWindow;
                ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
                previewWindow.Show();
                return;
            }

            var builder =
                Host.CreateApplicationBuilder();


            // =================================================
            // CORE
            // =================================================

            builder.Services.AddSingleton<
                HttpClient>();


            // =================================================
            // ELVARA APPLICATION INTEGRATION
            //
            // ELVARA is the product brand. NIRA remains the
            // single intelligence/runtime shared by trusted
            // ELVARA application surfaces.
            // =================================================

            builder.Services.AddSingleton<
                ElvaraAppRegistry>();


            builder.Services.AddSingleton<
                TradeAIClient>();


            builder.Services.AddSingleton<
                NIRABridgeCredentialStore>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                TradeAIReadCapabilityHandler>();


            builder.Services.AddSingleton<
                NIRALocalBridgeService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRALocalBridgeService>());


            builder.Services.AddSingleton<
                NIRARuntimeSettingsService>();


            builder.Services.AddSingleton<
                NIRAModelApiKeyService>();


            // =================================================
            // AUTHORITATIVE CLOCK / TEMPORAL CONTINUITY
            // =================================================

            builder.Services.AddSingleton<
                NIRATemporalContextService>();


            builder.Services.AddSingleton<
                NIRATemporalCommitmentReasoner>();

            // =================================================
            // NIRA VISUAL EMBODIMENT
            // =================================================

            builder.Services.AddSingleton<
                NIRAVisualIntentService>();


            // =================================================
            // NIRA MIND / BODY STATE
            // =================================================

            builder.Services.AddSingleton<
                NIRAStateService>();


            // =================================================
            // CHARACTER STATE
            // =================================================

            builder.Services.AddSingleton<
                NIRACharacterStateStore>();


            builder.Services.AddSingleton<
                NIRACharacterStateService>();


            builder.Services.AddSingleton<
                NIRAAttitudeService>();


            builder.Services.AddSingleton<
                NIRACharacterPersistenceService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRACharacterPersistenceService>());


            // =================================================
            // CHARACTER DYNAMICS
            // =================================================

            builder.Services.AddSingleton<
                NIRACharacterDynamicsService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRACharacterDynamicsService>());


            // =================================================
            // SOCIAL HISTORY
            // =================================================

            builder.Services.AddSingleton<
                NIRASocialHistoryService>();


            // =================================================
            // LOCAL SEMANTIC PERCEPTION
            // =================================================

            builder.Services.AddSingleton<
                INIRASemanticEncoder,
                MiniLmSemanticEncoder>();


            builder.Services.AddSingleton<
                NIRASemanticMemoryService>();


            // =================================================
            // LONG-TERM MEMORY
            // =================================================

            builder.Services.AddSingleton<
                NIRALongTermMemoryStore>();


            builder.Services.AddSingleton<
                NIRAMemoryAssociativeIndexStore>();


            builder.Services.AddSingleton<
                NIRAMemoryAssociationService>();


            builder.Services.AddSingleton<
                NIRALongTermMemoryService>();


            builder.Services.AddSingleton<
                NIRAMemoryContextService>();


            builder.Services.AddSingleton<
                NIRAMemoryConsolidator>();


            builder.Services.AddSingleton<
                NIRAMemoryFormationService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRALongTermMemoryService>());


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRAMemoryAssociationService>());


            builder.Services.AddSingleton<
                NIRAMemoryMaintenanceService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRAMemoryMaintenanceService>());


            // =================================================
            // NIRA DEVELOPED SELF / PREFERENCES
            // =================================================

            builder.Services.AddSingleton<
                NIRASelfPreferenceStore>();


            builder.Services.AddSingleton<
                NIRASelfPreferenceService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRASelfPreferenceService>());


            builder.Services.AddSingleton<
                NIRASelfPreferenceMemorySyncService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRASelfPreferenceMemorySyncService>());


            // =================================================
            // NIRA AUTHORITATIVE SELF-MODEL / COMMITMENTS
            // =================================================

            builder.Services.AddSingleton<
                INIRASelfKnowledgeProvider,
                NIRARuntimeSelfKnowledgeProvider>();


            builder.Services.AddSingleton<
                NIRASelfModelStore>();


            builder.Services.AddSingleton<
                NIRASelfModelService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRASelfModelService>());


            // =================================================
            // PERSISTENT GOALS / INTENTIONS
            // =================================================

            builder.Services.AddSingleton<
                NIRAGoalStore>();


            // =================================================
            // FIRST-CLASS BRANCH / TASK GRAPH STORAGE
            //
            // BranchStore shares the authoritative SQLite database
            // owned by NIRAGoalStore so branch->goal ownership can
            // be protected with real foreign keys.
            // =================================================

            builder.Services.AddSingleton<
                NIRABranchStore>();


            builder.Services.AddSingleton<
                NIRAGoalService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRAGoalService>());


            // =================================================
            // FIRST-CLASS BRANCH / TASK GRAPH
            // =================================================

            builder.Services.AddSingleton<
                NIRABranchService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRABranchService>());


            // =================================================
            // STAGE 12 VISUAL EVIDENCE FOUNDATION
            //
            // Core vision semantics stay platform-neutral. The WPF host
            // supplies the Windows desktop pixel-capture backend.
            // Capture is on-demand. Dedicated visual understanding consumes
            // only grounded captures and returns model-derived observations.
            // =================================================

            builder.Services.AddSingleton<
                INIRAScreenCaptureBackend,
                WindowsScreenCaptureBackend>();


            builder.Services.AddSingleton<
                INIRADesktopAccessibilityBackend,
                WindowsAccessibilityBackend>();

            builder.Services.AddSingleton<
                INIRADesktopInteractionBackend,
                WindowsDesktopInteractionBackend>();

            builder.Services.AddSingleton<
                NIRAVisualEvidenceService>();


            builder.Services.AddSingleton<
                NIRAVisualUnderstandingService>();


            builder.Services.AddSingleton<
                NIRAVisualArtifactService>();


            // =================================================
            // STAGE 14.2 NIRA-MANAGED PLAYWRIGHT BROWSER
            // =================================================

            builder.Services.AddSingleton<
                NIRABrowserService>();


            // =================================================
            // TRUSTED PRIMITIVE CAPABILITIES
            //
            // Real primitive handlers share one persistent authority service.
            // Only the trusted Permissions UI can create or revoke grants.
            // =================================================

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAScreenCaptureCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAVisualInspectCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAWindowAccessibilityCapabilityHandler>();


            builder.Services.AddSingleton<INIRACapabilityHandler,
                NIRADesktopInspectCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler,
                NIRADesktopActCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler,
                NIRADesktopCloseCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRATemporalRelationCapabilityHandler>();

            builder.Services.AddSingleton<INIRACapabilityHandler,
                NIRAMathCalculationCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileReadCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRADirectoryListCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRADirectoryUsageCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileLocationCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileMetadataCapabilityHandler>();


            // NIRA GATE R39 Step 1B: scoped, bounded, read-only file discovery.
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileSearchCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADirectoryTreeCapabilityHandler>();

            // NIRA GATE R38: independent file integrity and recovery primitives.
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileHashCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileCompareCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileBackupCreateCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileBackupVerifyCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileBackupRestoreCapabilityHandler>();

            // NIRA GATE R40 Step 1C: guarded atomic revisions and audited rollback.
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileRevisionApplyCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileRevisionInspectCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAFileRevisionRollbackCapabilityHandler>();

            // GATE R41: DOCX creation, read-only inspection and safe new-copy append.
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxCreateCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxInspectCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxAppendCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxReplaceCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTableAddCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxParagraphFormatCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxPageLayoutCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTableStyleCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxHeaderFooterCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxImageInsertCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxPageBreakInsertCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTableRowsAppendCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTableCellUpdateCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTableCellsBatchCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTableRowsDeleteCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTableTransactionCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxProfessionalFormatCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxContentCompareCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTemplateInspectCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTemplateFillCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxTemplateBatchCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxReportCreateCapabilityHandler>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRADocxStudioCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileWriteCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileCopyCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileMoveCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileDeleteCapabilityHandler>();


            builder.Services.AddSingleton<NIRAWindowsProtectedFolderAccessService>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRADirectoryCreateCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAApplicationResolveCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAProcessListCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAProcessStartCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAProcessStopCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRASystemStorageListCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAShellExecuteCapabilityHandler>();


            // Structured public research, no Playwright or search-page scraping.
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAWebSearchCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAHttpRequestCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAHttpDownloadCapabilityHandler>();

            // Scoped public image discovery; no Playwright browser required.
            // Do not let DI inject the application's shared HttpClient here.
            // That path bypasses NIRA's verified image-only IPv4/Wikimedia transport
            // and unexpectedly selects the test-only Openverse-first workflow.
            builder.Services.AddSingleton<NIRACommonsImageDiscoveryService>(
                _ => new NIRACommonsImageDiscoveryService());
            // Production image.search and image.retrieve now share Tavily discovery.
            // Keep the optional Commons service registration above for compatibility
            // with existing diagnostic/regression tooling; it is NOT the live provider.
            builder.Services.AddSingleton<NIRATavilyImageDiscoveryService>();
            builder.Services.AddSingleton<INIRAImageDiscoveryService>(sp =>
                sp.GetRequiredService<NIRATavilyImageDiscoveryService>());
            // R4.6: inject the existing Gemma vision resource explicitly.
            // Merely having it available in DI did not inspect retrieved photos.
            builder.Services.AddSingleton<INIRACapabilityHandler>(sp =>
                new NIRAImageSearchCapabilityHandler(
                    sp.GetRequiredService<INIRAImageDiscoveryService>(),
                    sp.GetRequiredService<NIRACommonsImageDiscoveryService>(),
                    sp.GetRequiredService<NIRAModelClient>()));
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAImageRetrieveCapabilityHandler>();
            // R37: native image generation, single Cloudflare 4B model, separate from model provider.
            builder.Services.AddSingleton<NIRACloudflareImageGenerationService>();
            builder.Services.AddSingleton<INIRACapabilityHandler, NIRAImageGenerateCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserSessionOpenCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserSessionCloseCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserCurrentCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserPageSelectCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserNavigateCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserBackCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserExploreCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserFollowCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserInspectCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserClickCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserFillCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserAccountsCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserAuthenticateCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserRecoveryResumeCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserSelectCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserWaitCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserScreenshotCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserRequestCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserDownloadCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRABrowserUploadCapabilityHandler>();


            builder.Services.AddSingleton<
                NIRACapabilityRegistry>();


            builder.Services.AddSingleton<NIRAAuthorityStore>();
            builder.Services.AddSingleton<NIRAAuthorityExecutionContextAccessor>();
            builder.Services.AddSingleton<NIRACredentialStore>();
            builder.Services.AddSingleton<NIRACredentialBroker>();
            builder.Services.AddSingleton<NIRACapabilityRequestPolicy>();
            builder.Services.AddSingleton<NIRACapabilityApprovalBroker>();
            builder.Services.AddSingleton<NIRAScopedCapabilityAuthorizer>();
            builder.Services.AddSingleton<INIRACapabilityAuthorizer>(sp =>
                sp.GetRequiredService<NIRAScopedCapabilityAuthorizer>());


            builder.Services.AddSingleton<
                NIRACapabilityService>();


            // =================================================
            // STAGE 13 LEARNED PROCEDURAL SKILLS
            // =================================================

            builder.Services.AddSingleton<
                NIRALearnedSkillStore>();

            builder.Services.AddSingleton<
                NIRALearnedSkillService>();


            // =================================================
            // STAGE 11 DYNAMIC TOOL COMPOSITION
            // =================================================

            builder.Services.AddSingleton<
                NIRADynamicToolStore>();

            builder.Services.AddSingleton<
                NIRADynamicToolValidator>();

            builder.Services.AddSingleton<
                NIRADynamicToolExecutor>();

            builder.Services.AddSingleton<
                NIRADynamicToolService>();


            // =================================================
            // BRANCH-OWNED ASSIGNED WORK
            //
            // Branches own responsibility/continuity. NIRA's agent loop
            // chooses bounded work; this service persists and tracks
            // the exact capability/tool assignment until a result
            // returns to NIRA.
            // =================================================

            builder.Services.AddSingleton<
                NIRABranchWorkStore>();


            builder.Services.AddSingleton<
                NIRABranchWorkService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRABranchWorkService>());


            // =================================================
            // INTERACTION OBSERVATION
            // =================================================

            builder.Services.AddSingleton<
                NIRAInteractionContextBuilder>();


            builder.Services.AddSingleton<
                NIRAInteractionObservationService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRAInteractionObservationService>());


            // =================================================
            // MODEL PROVIDER
            // =================================================

            builder.Services.AddSingleton<
                NIRAModelClient>();


            // =================================================
            // RESPONSE PRESENTATION
            // =================================================



            builder.Services.AddSingleton<
                NIRAResponseRealizationService>();

            builder.Services.AddSingleton<
                NIRATaskCompletionReviewService>();




            // =================================================
            // PC WORLD
            // =================================================

            builder.Services.AddSingleton<
                PcAwarenessService>();


            builder.Services.AddSingleton<
                NIRAPresenceService>();


            builder.Services.AddSingleton<
                PcWorldStateService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        PcWorldStateService>());


            // =================================================
            // NIRA BODY CONTROL / PLACEMENT
            // =================================================

            builder.Services.AddSingleton<
                NIRABodyPlacementStore>();


            builder.Services.AddSingleton<
                NIRABodyPlacementService>();


            builder.Services.AddSingleton<
                NIRABodyCommandService>();


            builder.Services.AddSingleton<
                NIRABodyControllerService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        NIRABodyControllerService>());


            // =================================================
            // CONVERSATION
            // =================================================

            builder.Services.AddSingleton<
                NIRAConversationArchiveStore>();

            builder.Services.AddSingleton<
                ConversationManager>();


            // =================================================
            // NIRA UNIFIED MIND
            // =================================================

            builder.Services.AddSingleton<
                NIRAMindActivityTracker>();




            // One host-owned agent runtime bridge; original personal-state services remain authoritative.
            builder.Services.AddSingleton<INIRAAgentExecutionHost, NIRAWpfAgentHost>();

            builder.Services.AddSingleton<NIRACompletedExperienceService>();
            // One encrypted review-outbox owner; pending model-only reviews may
            // resume after restart, while uncertain writes require reconciliation.
            builder.Services.AddHostedService(sp =>
                sp.GetRequiredService<NIRACompletedExperienceService>());

            builder.Services.AddSingleton<
                NIRAMindRuntime>();


            builder.Services.AddSingleton<
                NIRAOutputDispatcher>();


            builder.Services.AddSingleton<
                NIRABackgroundProcessor>();


            builder.Services.AddHostedService<
                NIRATemporalCommitmentReconciliationService>();


            builder.Services.AddHostedService<
                NIRATemporalCommitmentSchedulerService>();


            builder.Services.AddHostedService<
                NIRAGoalSchedulerService>();


            builder.Services.AddHostedService<
                NIRABranchRunnerService>();


            builder.Services.AddHostedService<
                NIRABranchWorkReconsiderationService>();


            // =================================================
            // PERCEPTION / ATTENTION
            // =================================================

            builder.Services.AddSingleton<
                AttentionManager>();


            builder.Services.AddSingleton<
                PerceptionAnalyzer>();


            builder.Services.AddHostedService<
                PcMonitorService>();


            builder.Services.AddHostedService<
                CompanionTimerService>();

            // =================================================
            // VOICE EXPRESSION
            // =================================================

            builder.Services.AddSingleton<
                NIRAVoiceExpressionService>();


            builder.Services.AddSingleton<
                VoiceAudioPlayer>();


            // =================================================
            // VOICE ENGINES
            // =================================================

            builder.Services.AddSingleton<
                PiperVoiceService>();


            builder.Services.AddSingleton<
                IVoiceService,
                AdaptiveVoiceService>();


            builder.Services.AddSingleton<
                VoiceQueue>();


            // =================================================
            // UI
            // =================================================

            builder.Services.AddSingleton<
                NIRAVisualToastPlacementService>();


            builder.Services.AddSingleton<
                MainWindowViewModel>();


            // =================================================
            // BUILD
            // =================================================

            _host =
                builder.Build();


            // Offline production-acceptance smoke: construct the exact WPF dependency
            // graph and registered capability descriptors, without starting any hosted
            // service, contacting a provider or touching an external application.
            // The installer supplies a one-time report path under the user's temp folder.
            string? smokeArgument = e.Args.FirstOrDefault(x =>
                x.StartsWith("--nira-acceptance-report=", StringComparison.Ordinal));
            if (smokeArgument is not null)
            {
                string reportPath = smokeArgument["--nira-acceptance-report=".Length..];
                string fullReportPath = System.IO.Path.GetFullPath(reportPath);
                string allowedRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
                if (!fullReportPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Acceptance report must be under the temporary directory.");
                string? smokeRoot = null;
                try
                {
                    var descriptors = _host.Services
                        .GetRequiredService<NIRACapabilityRegistry>().Descriptors;
                    string[] required = {
                        "filesystem.read", "filesystem.write", "filesystem.docx.create",
                        "web.search", "http.request", "browser.navigate", "image.search",
                        "vision.capture", "process.start", "elvara.tradeai.read"
                    };
                    string[] missing = required.Where(id => !descriptors.Any(d =>
                        string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))).ToArray();
                    if (descriptors.Count < 65 || missing.Length != 0)
                        throw new InvalidOperationException("Required NIRA capability registrations are missing: " +
                            string.Join(",", missing));
                    if (_host.Services.GetRequiredService<INIRAAgentExecutionHost>()
                        is not NIRAWpfAgentHost)
                        throw new InvalidOperationException("Unified WPF agent host not registered.");
                    _ = _host.Services.GetRequiredService<NIRACompletedExperienceService>();
                    _ = _host.Services.GetRequiredService<NIRAMindRuntime>();
                    // Instantiate the ACTUAL model-native capability registry,
                    // not just the DI descriptors. This catches malformed typed
                    // function schemas before launching an expensive live round.
                    smokeRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "NIRA_NATIVE_SMOKE_" + Guid.NewGuid().ToString("N"));
                    var smokeVault = new AgentPayloadVault(smokeRoot,
                        new WindowsDpapiDataProtector());
                    var nativeTools = NIRAAuthoritativeCapabilityTools.Create(smokeVault,
                        _host.Services.GetRequiredService<NIRACapabilityRegistry>(),
                        _host.Services.GetRequiredService<NIRACapabilityService>());
                    var nativeRegistry = new CapabilityRegistry(nativeTools);
                    string[] requiredFunctions = {
                        "nira_list_directory", "nira_locate_file", "nira_search_files",
                        "nira_read_file", "nira_file_metadata", "nira_calculate",
                        "nira_search_web", "nira_discover_capabilities",
                        "nira_observe_capability", "nira_execute_capability"
                    };
                    if (requiredFunctions.Any(name => !nativeTools.Any(t =>
                        string.Equals(t.FunctionName, name, StringComparison.Ordinal))))
                        throw new InvalidOperationException(
                            "A required native observation function is missing.");
                    _ = nativeRegistry.BuildProviderTools(nativeRegistry.Tools
                        .Select(x => x.Spec).ToArray(), allowWrite: true);
                     int credentialPersistenceChecks = NIRAModelApiKeyService.RunPersistenceSelfCheck();
                     int credentialRetryChecks = GeneralModelReasoner.RunAuthenticationRecoverySelfCheck();
                     if (credentialPersistenceChecks != 3 || credentialRetryChecks != 6)
                         throw new InvalidOperationException("Encrypted credential recovery checks failed.");
                     int goalWorkspaceChecks = NIRAAgent.UI.Goals.NIRAGoalsPage.RunWorkspaceSelfCheck();
                     if (goalWorkspaceChecks != 4)
                         throw new InvalidOperationException("Goal workspace / live sidebar checks incomplete.");
                     // Construct the actual WPF page so broken visual-tree setup
                     // is caught before a normal launch; no persistent state touched.
                     var goalsPreview = new NIRAAgent.UI.Goals.NIRAGoalsPage();
                     goalsPreview.Refresh();
                     int providerFailureChecks = NIRAWpfAgentHost.RunProviderFailureSelfCheck();
                    if (providerFailureChecks != 4)
                        throw new InvalidOperationException("Native provider failure diagnostics incomplete.");
                    int protocolChecks = await NiraProtocolSelfCheck.RunAsync();
                    // This is REAL native tool schema checking. A malformed
                    // Word report is rejected without beginning a file write,
                    // audit attempt, or permission request.
                    var executeTool = nativeTools.OfType<
                        NIRAAuthoritativeCapabilityTools.CapabilityInvocation>()
                        .Single(x => x.FunctionName == "nira_execute_capability");
                    string sampleDestination = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "nira-docx-shape-smoke-" +
                        Guid.NewGuid().ToString("N") + ".docx");
                    string validSections = System.Text.Json.JsonSerializer.Serialize(new[] {
                        new { heading = "Budget", body = "Workshop preparation",
                            tableTsv = "Item\tCost\nKits\t44100" }
                    });
                    var validProposal = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        capabilityId = NIRACapabilityIds.DocxReportCreate,
                        arguments = new { path = sampleDestination,
                            title = "Workshop", sectionsJson = validSections }
                    });
                    var badReport = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        capabilityId = NIRACapabilityIds.DocxReportCreate,
                        arguments = new { path = sampleDestination,
                            title = "Workshop", sectionsJson = "[{\"heading\":\"Budget\"}]" }
                    });
                    var badTable = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        capabilityId = NIRACapabilityIds.DocxReportCreate,
                        arguments = new { path = sampleDestination,
                            title = "Workshop", sectionsJson = "[{\"heading\":\"Budget\",\"body\":\"Text\",\"tableTsv\":\"OnlyOneCell\"}]" }
                    });
                    // Real registered Studio capability through the observed-ID
                    // envelope, not a mock or a fabricated native function. The
                    // exact same checkpointed invoke adapter performs preflight;
                    // this test performs NO filesystem write.
                    var actualStudio = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        path = sampleDestination, title = "Tesla engineering research",
                        blocksJson = new[] { new { type = "paragraph",
                            text = "A documented technical study of AC motors." } }
                    });
                    var studioDiscovery = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        capabilities = new[] { new { id = NIRACapabilityIds.DocxStudioCreate,
                            operationLane = "execute" } }
                    });
                    if (!GeneralModelReasoner.TryBuildObservedDiscoveryEnvelope(
                            NIRACapabilityIds.DocxStudioCreate, actualStudio,
                            studioDiscovery, out string mappedStudioTool,
                            out System.Text.Json.JsonElement mappedStudioArgs) ||
                        mappedStudioTool != "nira_execute_capability" ||
                        !executeTool.ValidateArguments(mappedStudioArgs) ||
                        System.IO.File.Exists(sampleDestination))
                        throw new InvalidOperationException(
                            "Real Studio tool cannot be preflighted through observed-ID route.");
                    // Confirm the real registry treats a missing DOCX read
                     // as OBSERVE, never as EXECUTE; an attempted Word Studio
                     // write must not pass the observational adapter. These
                     // assertions exercise both real handler schemas, not mocks.
                     var observeTool = nativeTools.OfType<
                         NIRAAuthoritativeCapabilityTools.CapabilityInvocation>()
                         .Single(x => x.FunctionName == "nira_observe_capability");
                     var missingRead = System.Text.Json.JsonSerializer.SerializeToElement(new {
                         capabilityId = NIRACapabilityIds.DocxInspect,
                         arguments = new { path = sampleDestination }
                     });
                     if (executeTool.ValidateArguments(missingRead) ||
                         !observeTool.ValidateArguments(missingRead) ||
                         observeTool.ValidateArguments(mappedStudioArgs) ||
                         !GeneralModelReasoner.TryDowngradeObservationLane(missingRead,
                             nativeRegistry.Tools.Select(x => x.Spec).ToArray(),
                             nativeRegistry, out IGeneralCapability laneResult) ||
                         laneResult.FunctionName != "nira_observe_capability" ||
                         GeneralModelReasoner.TryDowngradeObservationLane(mappedStudioArgs,
                             nativeRegistry.Tools.Select(x => x.Spec).ToArray(),
                             nativeRegistry, out _))
                         throw new InvalidOperationException(
                             "Read/write adapter lanes not enforced for real DOCX handlers.");
                    if (!executeTool.ValidateArguments(validProposal) ||
                        executeTool.ValidateArguments(badReport) ||
                        executeTool.ValidateArguments(badTable) ||
                        System.IO.File.Exists(sampleDestination))
                        throw new InvalidOperationException(
                            "DOCX report arguments were not validated before mutation dispatch.");
                    int docxPreflightChecks = 4;
                    int docxBoundaryChecks = NIRACapabilityService.RunReportPublicationBoundarySelfCheck();
                    if (docxBoundaryChecks != 3)
                        throw new InvalidOperationException("DOCX report publication boundary checks incomplete.");
                    int richChecks = NIRAUnifiedRichAnswerComposer.RunSelfCheck();
                    if (richChecks != 3) throw new InvalidOperationException("Rich presenter checks incomplete.");
                    int activityChecks = NIRAAgent.UI.Presentation.NIRAActivityPresentationPolicy.RunSelfCheck();
                    int publicThinkingChecks = NiraPublicThinkingNote.RunSelfCheck();
                    int streamingDraftChecks = NiraSafeStreamDraft.RunSelfCheck();
                    if (streamingDraftChecks != 3 ||
                        !NiraModelEndpointOptions.Ollama("gemma4:31b").ShouldStream(
                            hasNativeTools: true, previewRequested: true) ||
                        NiraModelEndpointOptions.Ollama("gemma4:31b").ShouldStream(
                            hasNativeTools: true, previewRequested: false))
                        throw new InvalidOperationException("Streaming transport safety checks incomplete.");
                    int readFailureEvidenceChecks =
                        await GeneralModelReasoner.RunReadFailureEvidenceSelfCheckAsync(smokeVault);
                    if (readFailureEvidenceChecks != 4)
                        throw new InvalidOperationException("Missing-file failure evidence test incomplete.");
                    int researchProgressChecks = GeneralModelReasoner.RunResearchProgressSelfCheck();
                     int discoveredRoutingChecks = GeneralModelReasoner.RunObservedDiscoveryRoutingSelfCheck();
                     if (discoveredRoutingChecks != 7)
                         throw new InvalidOperationException("Observed capability-ID routing checks incomplete.");
                    int progressPresentationChecks = ChatMessageViewModel.RunProgressPresentationSelfCheck();
                    int observedImageChecks = GeneralModelReasoner.RunObservedWebImageSelfCheck();
                    if (observedImageChecks != 3)
                        throw new InvalidOperationException("Verified research-image hint checks incomplete.");
                    if (researchProgressChecks != 5 || progressPresentationChecks != 6)
                        throw new InvalidOperationException("Research progress or elapsed-time checks incomplete.");
                    int sourceLinkRecoveryChecks = GeneralModelReasoner.RunGroundingRecoverySelfCheck();
                    int boundedSearchChecks = GeneralModelReasoner.RunWebImageCountSafetySelfCheck();
                    if (boundedSearchChecks != 4)
                        throw new InvalidOperationException("Read-only search count clamp checks incomplete.");
                    if (sourceLinkRecoveryChecks != 4)
                        throw new InvalidOperationException("Grounded source-link recovery tests incomplete.");
                     // Provisional SSE drafts must never create trusted players.
                    var provisionalVideo = new ChatMessageViewModel("assistant", string.Empty);
                    provisionalVideo.SetProvisionalText(
                        "Unverified preview https://www.youtube.com/watch?v=dQw4w9WgXcQ");
                    if (provisionalVideo.HasVideoDeck || provisionalVideo.VideoCount != 0)
                        throw new InvalidOperationException("Unverified SSE preview created a video player.");
                    // Also exercise the ACTUAL text-document renderer, which
                    // otherwise could create a second player from provisional
                    // Markdown even when the view-model avoids promotion.
                    var previewDocument = new System.Windows.Controls.StackPanel();
                    NIRAChatDocument.SetIsProvisionalPreview(previewDocument, true);
                    NIRAChatDocument.SetText(previewDocument,
                        "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
                    if (previewDocument.Children.Count > 1 ||
                        previewDocument.Children.OfType<System.Windows.Controls.Grid>().Any())
                        throw new InvalidOperationException("Provisional prose embedded an unverified player.");
                    provisionalVideo.EndProvisionalText();
                    // Test that typed video blocks reach ONE actual WPF deck and
                     // expose its visible jump shortcut. No provider call or
                     // embedded WebView2 navigation is performed here.
                    NIRAAgent.UI.Presentation.NIRAVideoThumbnailService.DisableNetworkForOfflineAcceptance = true;
                    try
                    {
                     var videoPreview = new ChatMessageViewModel("assistant", "Research complete.");
                     videoPreview.AddRichBlocks(new[] {
                         new NIRARichBlock { Type = "video", Title = "Landing",
                             Text = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" },
                         new NIRARichBlock { Type = "video", Title = "Recovery",
                             Text = "https://www.youtube.com/watch?v=9bZkp7q19f0" }
                     });
                     if (!videoPreview.HasVideoDeck || videoPreview.VideoCount != 2 ||
                         videoPreview.VideoDeckView is null ||
                         !videoPreview.RichElements.Contains(videoPreview.VideoDeckView) ||
                         !videoPreview.RichElements.Any())
                         throw new InvalidOperationException("Video deck not visible in WPF reply.");
                    }
                    finally
                    {
                        NIRAAgent.UI.Presentation.NIRAVideoThumbnailService.DisableNetworkForOfflineAcceptance = false;
                    }
                    var activityPreview = new ChatMessageViewModel("assistant", string.Empty);
                    activityPreview.BeginJourney();
                    activityPreview.AddAgentActivity("REASONING_STARTED", "", DateTimeOffset.UtcNow);
                    activityPreview.AddAgentActivity("TOOL_DISPATCHED", "nira.direct.web.search", DateTimeOffset.UtcNow);
                    activityPreview.AddAgentActivity("TOOL_SUCCEEDED", "nira.direct.web.search", DateTimeOffset.UtcNow);
                    activityPreview.AddAgentActivity("RUN_COMPLETED", "", DateTimeOffset.UtcNow);
                    activityPreview.AddAgentActivity("REASONING_PRIVATE_TEXT", "SECRET", DateTimeOffset.UtcNow);
                    if (!activityPreview.HasActivity || activityPreview.IsActivityExpanded ||
                        activityPreview.ActivityEntries.Count != 2 ||
                        activityPreview.ActivityEntries[1].Detail != "Result received" ||
                        !activityPreview.ActivityHeader.StartsWith("Worked for", StringComparison.Ordinal))
                        throw new InvalidOperationException("Activity panel lifecycle verification failed.");
                    activityChecks++;
                    if (activityPreview.HasPublicThinking)
                        throw new InvalidOperationException("Tool events were presented as NIRA's own thinking.");
                    activityPreview.BeginJourney();
                    activityPreview.AddAgentActivity("REASONING_STARTED", "", DateTimeOffset.UtcNow);
                    activityPreview.AddPublicThinkingNote(
                        "I want to check what really distinguishes a booster landing from a tower catch.");
                    activityPreview.AddPublicThinkingNote(
                        "Those two examples use different recovery hardware, so I should explain them separately.");
                    if (!activityPreview.HasPublicThinking ||
                        activityPreview.PublicThinkingNotes.Count != 2 ||
                        activityPreview.PublicThinkingNotes[0].StartsWith("Searching the web", StringComparison.Ordinal))
                        throw new InvalidOperationException("Public model-authored thinking notes were not displayed.");
                    activityPreview.AddAgentActivity("RUN_COMPLETED", "", DateTimeOffset.UtcNow);
                    if (activityPreview.IsActivityExpanded)
                        throw new InvalidOperationException("Model thought panel did not collapse after completion.");
                    activityChecks++;
                    var failedPreview = new ChatMessageViewModel("assistant", string.Empty);
                    failedPreview.BeginJourney();
                    failedPreview.AddAgentActivity("REASONING_STARTED", "", DateTimeOffset.UtcNow);
                    failedPreview.AddAgentActivity("TOOL_DISPATCHED", "nira.direct.web.search", DateTimeOffset.UtcNow);
                    failedPreview.AddAgentActivity("TOOL_FAILED", "nira.direct.web.search", DateTimeOffset.UtcNow);
                    failedPreview.AddAgentActivity("RUN_INCOMPLETE", "", DateTimeOffset.UtcNow);
                    if (failedPreview.ActivityEntries.Count != 3 ||
                        failedPreview.ActivityEntries[1].Detail != "The operation failed" ||
                        !failedPreview.ActivityHeader.StartsWith("Stopped after", StringComparison.Ordinal))
                        throw new InvalidOperationException("Failed activity flow was presented incorrectly.");
                    activityChecks++;
                    // Validate the ACTUAL native web schema against the real
                    // authoritative preflight, not a test-only mock handler.
                    var searchTool = nativeTools.OfType<
                        NIRAAuthoritativeCapabilityTools.BoundObservation>()
                        .Single(x => x.FunctionName == "nira_search_web");
                    var imageCountSchema = searchTool.InputSchema.GetProperty("properties")
                        .GetProperty("retrieveCount");
                    if (!imageCountSchema.TryGetProperty("minimum", out var imageMin) ||
                        imageMin.GetInt32() != 1 ||
                        !imageCountSchema.TryGetProperty("maximum", out var imageMax) ||
                        imageMax.GetInt32() != 3)
                        throw new InvalidOperationException("Native search numeric limits not advertised.");
                    var invalidCount = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        queries = new[] { "historical inventor overview" },
                        includeImages = true, imageQuery = "historical inventor",
                        retrieveCount = 5 });
                    var missingImageSubject = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        queries = new[] { "reusable rocket recovery" },
                        includeImages = true, retrieveCount = 2 });
                    var validCombinedSearch = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        queries = new[] { "reusable rocket recovery" },
                        includeImages = true, imageQuery = "rocket recovery photograph",
                        retrieveCount = 2 });
                    if (searchTool.ValidateArguments(invalidCount) ||
                        searchTool.ValidateArguments(missingImageSubject) ||
                        !searchTool.ValidateArguments(validCombinedSearch))
                        throw new InvalidOperationException(
                            "Native read-only search contract validation disagrees with descriptor.");
                    int readOnlySchemaChecks = 4;
                    if (!NIRAVideoReferencePolicy.TryParse(
                            "https://www.youtube.com/watch?v=dQw4w9WgXcQ", out var videoCheck) ||
                        videoCheck?.Provider != "YouTube" ||
                        NIRAVideoReferencePolicy.TryParse("https://example.com/watch?v=dQw4w9WgXcQ",
                            out _))
                        throw new InvalidOperationException("Video URL origin checks incomplete.");
                    if (protocolChecks < 8)
                        throw new InvalidOperationException("Provider protocol self-checks incomplete.");
                    // Exercise the real dynamic registry/query inventory through
                    // the encrypted vault, without invoking any real capability.
                    var discovery = nativeTools.OfType<
                        NIRAAuthoritativeCapabilityTools.CapabilityDiscovery>().Single();
                    Guid discoveryRun = Guid.NewGuid();
                    var inventoryQuery = System.Text.Json.JsonSerializer.SerializeToElement(new {
                        query = "directory listing", prefix = "filesystem.list" });
                    string inventoryRef = await smokeVault.SaveArgumentAsync(
                        discoveryRun, discovery.Spec.Id, inventoryQuery);
                    var inventoryReceipt = await discovery.ExecuteAsync(inventoryRef,
                        CancellationToken.None);
                    if (!inventoryReceipt.Succeeded ||
                        string.IsNullOrWhiteSpace(inventoryReceipt.ResultReference))
                        throw new InvalidOperationException("Dynamic registry query failed.");
                    var inventory = await smokeVault.ReadResultAsync(
                        discoveryRun, discovery.Spec.Id,
                        inventoryReceipt.ResultReference, CancellationToken.None);
                    if (!inventory.TryGetProperty("capabilities", out var inventoryItems) ||
                        inventoryItems.ValueKind != System.Text.Json.JsonValueKind.Array ||
                        !inventoryItems.EnumerateArray().Any(x =>
                            x.TryGetProperty("id", out var capabilityId) &&
                            capabilityId.GetString() == "filesystem.list"))
                        throw new InvalidOperationException("Filesystem discovery contract failed.");
                    int discoveryChecks = 1;
                    System.IO.File.WriteAllText(fullReportPath,
                        System.Text.Json.JsonSerializer.Serialize(new {
                            status = "PASS", capabilityCount = descriptors.Count,
                            nativeToolCount = nativeRegistry.Count,
                            nativeFunctionsChecked = requiredFunctions.Length,
                            protocolChecks, discoveryChecks, richChecks, activityChecks,
                            publicThinkingChecks, streamingDraftChecks,
                            sourceLinkRecoveryChecks, readOnlySchemaChecks, boundedSearchChecks,
                            researchProgressChecks, progressPresentationChecks, observedImageChecks,
                            readFailureEvidenceChecks,
                             discoveredRoutingChecks,
                            docxPreflightChecks, docxBoundaryChecks,
                             providerFailureChecks, credentialPersistenceChecks, credentialRetryChecks,
                             goalWorkspaceChecks,
                            requiredCapabilities = required.Length,
                            unifiedHost = true, mindOwner = true, memoryReviewOwner = true,
                            hostedServicesStarted = false
                        }));
                    _host.Dispose();
                    _host = null;
                    Shutdown(0);
                    return;
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(fullReportPath,
                        System.Text.Json.JsonSerializer.Serialize(new {
                            status = "FAIL", exceptionType = ex.GetType().Name,
                            message = ex.Message.Length > 400 ? ex.Message[..400] : ex.Message
                        }));
                    _host.Dispose();
                    _host = null;
                    Shutdown(1);
                    return;
                }
                finally
                {
                    // Do not leave isolated acceptance-vault artifacts in Temp.
                    if (smokeRoot is not null && System.IO.Directory.Exists(smokeRoot))
                    {
                        try { System.IO.Directory.Delete(smokeRoot, recursive: true); }
                        catch (System.IO.IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }


            NIRACredentialBroker credentialBroker =
                _host.Services.GetRequiredService<NIRACredentialBroker>();


            credentialBroker.SetPromptHandler(
                PromptForWebsiteCredentialAsync);


            NIRAModelApiKeyService modelApiKeys =
                _host.Services.GetRequiredService<NIRAModelApiKeyService>();


            modelApiKeys.SetPromptHandler(
                PromptForModelCredentialAsync);


            if (!modelApiKeys.HasKey)
            {
                // The initial trusted UI prompt uses the SAME verification and
                // DPAPI commit path as a replacement after HTTP 401. An untested
                // key must never be treated as a durable working credential.
                try
                {
                    _ = await modelApiKeys.ResolveProblemAsync(
                        NIRAModelCredentialProblem.Missing, string.Empty,
                        "Enter an Ollama Cloud API key. NIRA will test it with the " +
                        "configured model and save it encrypted only after Ollama accepts it.",
                        CancellationToken.None);
                }
                catch (NIRAModelCredentialRequiredException)
                {
                    Shutdown(0);
                    return;
                }
            }


            // Record a first-party, persistent decision on normal desktop runs.
            // Do not silently enable cloud sharing of private memory on upgrade.
            NIRARuntimeSettingsService continuitySettings =
                _host.Services.GetRequiredService<NIRARuntimeSettingsService>();
            if (continuitySettings.Current.PersonalContextConsent is null)
            {
                bool granted = WpfMessageBox.Show(
                    "Allow NIRA to recall selected previous chats, memories, character state " +
                    "and saved goals in future conversations?\n\n" +
                    "Relevant private context may be sent to your configured cloud AI provider. " +
                    "This does not grant any new Windows action permissions. " +
                    "You can turn either feature off in Settings at any time.",
                    "NIRA — Memory & Goal Continuity",
                    WpfMessageBoxButton.YesNo, WpfMessageBoxImage.Question) ==
                    System.Windows.MessageBoxResult.Yes;
                continuitySettings.SetPersonalContextConsent(granted);
                if (granted) continuitySettings.SetLifecycleContextEnabled(true);
            }

            await _host.StartAsync();


            // =================================================
            // STAGE 13 PROCEDURAL-SKILL LIFECYCLE RECONCILIATION
            //
            // Skills persist independently from dynamic tools. Reconcile
            // terminal source-tool retirement at startup so a skill never
            // remains presented as reusable after its executable lineage
            // was retired in a previous run.
            // =================================================

            await _host.Services
                .GetRequiredService<NIRALearnedSkillService>()
                .ReconcileAllSourceToolLifecyclesAsync();


            MainWindowViewModel viewModel =
                _host.Services
                    .GetRequiredService<
                        MainWindowViewModel>();


            MainWindow window =
                new(
                    viewModel,
                    _host.Services.GetRequiredService<NIRAVisualToastPlacementService>());


            MainWindow =
                window;

            _host.Services.GetRequiredService<NIRAWindowsProtectedFolderAccessService>()
                .SetConsentHandler(async (description, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return await Dispatcher.InvokeAsync(() =>
                        WpfMessageBox.Show(window, description,
                            "NIRA — Windows protected-folder access",
                            WpfMessageBoxButton.YesNo,
                            WpfMessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes).Task;
                });

            window.AttachAuthorization(
                _host.Services.GetRequiredService<NIRAAuthorityStore>(),
                _host.Services.GetRequiredService<NIRAScopedCapabilityAuthorizer>(),
                _host.Services.GetRequiredService<NIRACapabilityApprovalBroker>());


            NIRARuntimeSettingsService runtimeSettings =
                _host.Services.GetRequiredService<NIRARuntimeSettingsService>();


            window.AttachSettings(
                runtimeSettings);

            window.AttachConversationArchive(
                _host.Services.GetRequiredService<NIRAConversationArchiveStore>());


            window.Show();


            NIRAStateService NIRAState =
                _host.Services
                    .GetRequiredService<
                        NIRAStateService>();


            window.AttachNIRAState(
                NIRAState);


            window.AttachVisionActivity(
                _host.Services.GetRequiredService<NIRAVisualEvidenceService>());


            window.AttachBranchActivity(
                _host.Services.GetRequiredService<NIRABranchService>(),
                _host.Services.GetRequiredService<NIRABranchWorkService>());


            window.AttachContinuityState(
                _host.Services.GetRequiredService<NIRASelfModelService>());

            window.AttachGoals(
                _host.Services.GetRequiredService<NIRAGoalService>());


            NIRAVisualIntentService visualIntent =
                _host.Services
                    .GetRequiredService<
                        NIRAVisualIntentService>();


            NIRAPresenceService NIRAPresence =
                _host.Services
                    .GetRequiredService<
                        NIRAPresenceService>();


            NIRABodyCommandService bodyCommands =
                _host.Services
                    .GetRequiredService<
                        NIRABodyCommandService>();


            NIRABodyPlacementService placement =
                _host.Services
                    .GetRequiredService<
                        NIRABodyPlacementService>();


            CompanionWindow companion =
                new(
                    NIRAState,
                    visualIntent,
                    NIRAPresence,
                    bodyCommands,
                    placement,
                    runtimeSettings);


            // Respect the saved global presence preference before the first
            // WPF render: no one-frame blob flash on a disabled startup.
            if (runtimeSettings.Current.DesktopPresenceEnabled)
                companion.Show();

            companion.AttachToMainWindow(
                window);


            bool openBlobStyleShowcase =
                Array.Exists(
                    e.Args,
                    argument =>
                        string.Equals(
                            argument,
                            "--blob-style-showcase",
                            StringComparison.OrdinalIgnoreCase));

            if (openBlobStyleShowcase)
            {
                NIRABlobStyleShowcaseWindow showcase =
                    new NIRABlobStyleShowcaseWindow()
                    {
                        Owner = window
                    };

                showcase.Show();
            }
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                ex.ToString(),
                "NIRAAI Startup Error",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);


            Shutdown(
                1);
        }
    }


    // =========================================================
    // TRUSTED WEBSITE CREDENTIAL UI
    // =========================================================

    private async Task<NIRACredentialPromptResponse?>
        PromptForWebsiteCredentialAsync(
            NIRACredentialPromptRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Dispatcher.CheckAccess())
        {
            return ShowWebsiteCredentialDialog(request);
        }

        return await Dispatcher.InvokeAsync(
            () => ShowWebsiteCredentialDialog(request),
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken);
    }


    private NIRACredentialPromptResponse? ShowWebsiteCredentialDialog(
        NIRACredentialPromptRequest request)
    {
        CredentialPromptWindow dialog =
            new(request);

        bool visibleOwner = MainWindow is System.Windows.Window owner &&
            owner.IsLoaded && owner.IsVisible &&
            owner.WindowState != System.Windows.WindowState.Minimized;
        if (visibleOwner && MainWindow is System.Windows.Window visibleWindow)
        {
            dialog.Owner = visibleWindow;
            dialog.WindowStartupLocation =
                System.Windows.WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation =
                System.Windows.WindowStartupLocation.CenterScreen;
        }
        // A user-requested secure login prompt must not hide behind other apps.
        dialog.Topmost = true;
        System.Diagnostics.Debug.WriteLine($"[AuthFlow] TRUSTED WINDOW OPEN | " +
            $"Origin={request.Origin} | ExistingAccounts={request.ExistingCredentials.Count}");
        bool? accepted = dialog.ShowDialog();
        System.Diagnostics.Debug.WriteLine($"[AuthFlow] TRUSTED WINDOW CLOSED | " +
            $"Accepted={accepted == true} | HasResolution={dialog.Resolution != null}");

        return accepted == true
            ? dialog.Resolution
            : null;
    }


    // =========================================================
    // MODEL PROVIDER CREDENTIAL UI
    // =========================================================

    private async Task<NIRAModelCredentialResolution?>
        PromptForModelCredentialAsync(
            NIRAModelCredentialRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Dispatcher.CheckAccess())
        {
            return ShowModelCredentialDialog(request);
        }

        return await Dispatcher.InvokeAsync(
            () => ShowModelCredentialDialog(request),
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken);
    }


    private NIRAModelCredentialResolution? ShowModelCredentialDialog(
        NIRAModelCredentialRequest request)
    {
        NIRAModelApiKeyWindow dialog =
            new(request);

        if (MainWindow is System.Windows.Window owner &&
            owner.IsLoaded)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation =
                System.Windows.WindowStartupLocation.CenterOwner;
        }

        bool? accepted = dialog.ShowDialog();

        return accepted == true
            ? dialog.Resolution
            : null;
    }


    protected override async void OnExit(
        WpfExitEventArgs e)
    {
        if (_host !=
            null)
        {
            await _host.StopAsync();
            try
            {
                _host.Services.GetRequiredService<NIRAConversationArchiveStore>()
                    .CloseCurrentSession();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConversationArchive] CLOSE_FAILED | {ex.Message}");
            }


            _host.Dispose();


            _host =
                null;
        }


        base.OnExit(
            e);
    }
}

/*
 * filename: App.xaml.cs
 */

using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NIRAAgent.AI.Ollama;
using NIRAAgent.AI.Cognition;
using NIRAAgent.Capabilities;
using NIRAAgent.Authorization;
using NIRAAgent.Artifacts;
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
using NIRAAgent.PC.Awareness;
using NIRAAgent.Perception;
using NIRAAgent.Semantic;
using NIRAAgent.Self.Preferences;
using NIRAAgent.Self.Model;
using NIRAAgent.Settings;
using NIRAAgent.UI.Companion;
using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Theming;
using NIRAAgent.Voice;
using NIRAAgent.Voice.Groq;
using NIRAAgent.Vision;
using NIRAAgent.Embodiment;
using NIRAAgent.Embodiment.Body;
using NIRAAgent.Tools;
using NIRAAgent.Skills;
using NIRAAgent.Temporal;
using NIRAAgent.UI.Vision;
using NIRAAgent.UI.Visuals;

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
        base.OnStartup(
            e);


        try
        {
            NIRAThemeManager.Initialize();

            var builder =
                Host.CreateApplicationBuilder();


            // =================================================
            // CORE
            // =================================================

            builder.Services.AddSingleton<
                HttpClient>();


            builder.Services.AddSingleton<
                NIRARuntimeSettingsService>();


            builder.Services.AddSingleton<
                NIRAOllamaApiKeyService>();


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
            // BranchStore shares the executive SQLite database
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
                NIRATemporalRelationCapabilityHandler>();

            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileReadCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRADirectoryListCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileLocationCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAFileMetadataCapabilityHandler>();


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
                NIRAShellExecuteCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAHttpRequestCapabilityHandler>();


            builder.Services.AddSingleton<
                INIRACapabilityHandler,
                NIRAHttpDownloadCapabilityHandler>();


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
            // Branches own responsibility/continuity. NIRA cognition
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
            // OLLAMA
            // =================================================

            builder.Services.AddSingleton<
                OllamaClient>();


            // =================================================
            // MAIN COGNITION
            // =================================================

            builder.Services.AddSingleton<
                NIRACognitionService>();


            builder.Services.AddSingleton<
                NIRAResponseRealizationService>();

            builder.Services.AddSingleton<
                NIRATaskCompletionReviewService>();


            builder.Services.AddSingleton<
                NIRACognitionContextBuilder>();


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
                ConversationManager>();


            // =================================================
            // NIRA MIND / EXECUTIVE
            // =================================================

            builder.Services.AddSingleton<
                NIRAMindActivityTracker>();


            builder.Services.AddSingleton<
                NIRAExecutive>();


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
                GroqOrpheusVoiceService>();


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


            NIRACredentialBroker credentialBroker =
                _host.Services.GetRequiredService<NIRACredentialBroker>();


            credentialBroker.SetPromptHandler(
                PromptForWebsiteCredentialAsync);


            NIRAOllamaApiKeyService ollamaApiKeys =
                _host.Services.GetRequiredService<NIRAOllamaApiKeyService>();


            ollamaApiKeys.SetPromptHandler(
                PromptForOllamaCredentialAsync);


            if (!ollamaApiKeys.HasKey)
            {
                NIRAOllamaCredentialResolution? initialResolution =
                    await PromptForOllamaCredentialAsync(
                        new NIRAOllamaCredentialRequest
                        {
                            Problem = NIRAOllamaCredentialProblem.Missing,
                            HasCurrentKey = false
                        },
                        CancellationToken.None);


                if (initialResolution == null ||
                    initialResolution.Kind != NIRAOllamaCredentialResolutionKind.ReplaceKey)
                {
                    Shutdown(0);
                    return;
                }


                ollamaApiKeys.SaveKey(
                    initialResolution.ApiKey);
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


            window.AttachAuthorization(
                _host.Services.GetRequiredService<NIRAAuthorityStore>(),
                _host.Services.GetRequiredService<NIRAScopedCapabilityAuthorizer>(),
                _host.Services.GetRequiredService<NIRACapabilityApprovalBroker>());


            NIRARuntimeSettingsService runtimeSettings =
                _host.Services.GetRequiredService<NIRARuntimeSettingsService>();


            window.AttachSettings(
                runtimeSettings);


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


    // =========================================================
    // OLLAMA CREDENTIAL UI
    // =========================================================

    private async Task<NIRAOllamaCredentialResolution?>
        PromptForOllamaCredentialAsync(
            NIRAOllamaCredentialRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Dispatcher.CheckAccess())
        {
            return ShowOllamaCredentialDialog(request);
        }

        return await Dispatcher.InvokeAsync(
            () => ShowOllamaCredentialDialog(request),
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken);
    }


    private NIRAOllamaCredentialResolution? ShowOllamaCredentialDialog(
        NIRAOllamaCredentialRequest request)
    {
        OllamaApiKeyWindow dialog =
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


            _host.Dispose();


            _host =
                null;
        }


        base.OnExit(
            e);
    }
}






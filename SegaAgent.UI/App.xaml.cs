/*
 * filename: App.xaml.cs
 */

using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using SegaAgent.AI.Ollama;
using SegaAgent.AI.Cognition;
using SegaAgent.Capabilities;
using SegaAgent.Authorization;
using SegaAgent.Agent.State;
using SegaAgent.Character.Dynamics;
using SegaAgent.Character.History;
using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;
using SegaAgent.Conversation;
using SegaAgent.Memory.LongTerm;
using SegaAgent.Goals;
using SegaAgent.Branches;
using SegaAgent.Mind;
using SegaAgent.PC.Awareness;
using SegaAgent.Perception;
using SegaAgent.Semantic;
using SegaAgent.Self.Preferences;
using SegaAgent.Self.Model;
using SegaAgent.Settings;
using SegaAgent.UI.Companion;
using SegaAgent.UI.ViewModels;
using SegaAgent.Voice;
using SegaAgent.Voice.Groq;
using SegaAgent.Vision;
using SegaAgent.Embodiment;
using SegaAgent.Embodiment.Body;
using SegaAgent.Tools;
using SegaAgent.Temporal;
using SegaAgent.UI.Vision;

using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace SegaAgent.UI;

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
            var builder =
                Host.CreateApplicationBuilder();


            // =================================================
            // CORE
            // =================================================

            builder.Services.AddSingleton<
                HttpClient>();


            builder.Services.AddSingleton<
                SegaRuntimeSettingsService>();


            builder.Services.AddSingleton<
                SegaOllamaApiKeyService>();


            // =================================================
            // AUTHORITATIVE CLOCK / TEMPORAL CONTINUITY
            // =================================================

            builder.Services.AddSingleton<
                SegaTemporalContextService>();


            builder.Services.AddSingleton<
                SegaTemporalCommitmentReasoner>();

            // =================================================
            // SEGA VISUAL EMBODIMENT
            // =================================================

            builder.Services.AddSingleton<
                SegaVisualIntentService>();


            // =================================================
            // SEGA MIND / BODY STATE
            // =================================================

            builder.Services.AddSingleton<
                SegaStateService>();


            // =================================================
            // CHARACTER STATE
            // =================================================

            builder.Services.AddSingleton<
                SegaCharacterStateStore>();


            builder.Services.AddSingleton<
                SegaCharacterStateService>();


            builder.Services.AddSingleton<
                SegaAttitudeService>();


            builder.Services.AddSingleton<
                SegaCharacterPersistenceService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaCharacterPersistenceService>());


            // =================================================
            // CHARACTER DYNAMICS
            // =================================================

            builder.Services.AddSingleton<
                SegaCharacterDynamicsService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaCharacterDynamicsService>());


            // =================================================
            // SOCIAL HISTORY
            // =================================================

            builder.Services.AddSingleton<
                SegaSocialHistoryService>();


            // =================================================
            // LOCAL SEMANTIC PERCEPTION
            // =================================================

            builder.Services.AddSingleton<
                ISegaSemanticEncoder,
                MiniLmSemanticEncoder>();


            builder.Services.AddSingleton<
                SegaSemanticMemoryService>();


            // =================================================
            // LONG-TERM MEMORY
            // =================================================

            builder.Services.AddSingleton<
                SegaLongTermMemoryStore>();


            builder.Services.AddSingleton<
                SegaMemoryAssociativeIndexStore>();


            builder.Services.AddSingleton<
                SegaMemoryAssociationService>();


            builder.Services.AddSingleton<
                SegaLongTermMemoryService>();


            builder.Services.AddSingleton<
                SegaMemoryContextService>();


            builder.Services.AddSingleton<
                SegaMemoryConsolidator>();


            builder.Services.AddSingleton<
                SegaMemoryFormationService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaLongTermMemoryService>());


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaMemoryAssociationService>());


            builder.Services.AddSingleton<
                SegaMemoryMaintenanceService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaMemoryMaintenanceService>());


            // =================================================
            // SEGA DEVELOPED SELF / PREFERENCES
            // =================================================

            builder.Services.AddSingleton<
                SegaSelfPreferenceStore>();


            builder.Services.AddSingleton<
                SegaSelfPreferenceService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaSelfPreferenceService>());


            builder.Services.AddSingleton<
                SegaSelfPreferenceMemorySyncService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaSelfPreferenceMemorySyncService>());


            // =================================================
            // SEGA AUTHORITATIVE SELF-MODEL / COMMITMENTS
            // =================================================

            builder.Services.AddSingleton<
                ISegaSelfKnowledgeProvider,
                SegaRuntimeSelfKnowledgeProvider>();


            builder.Services.AddSingleton<
                SegaSelfModelStore>();


            builder.Services.AddSingleton<
                SegaSelfModelService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaSelfModelService>());


            // =================================================
            // PERSISTENT GOALS / INTENTIONS
            // =================================================

            builder.Services.AddSingleton<
                SegaGoalStore>();


            // =================================================
            // FIRST-CLASS BRANCH / TASK GRAPH STORAGE
            //
            // BranchStore shares the executive SQLite database
            // owned by SegaGoalStore so branch->goal ownership can
            // be protected with real foreign keys.
            // =================================================

            builder.Services.AddSingleton<
                SegaBranchStore>();


            builder.Services.AddSingleton<
                SegaGoalService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaGoalService>());


            // =================================================
            // FIRST-CLASS BRANCH / TASK GRAPH
            // =================================================

            builder.Services.AddSingleton<
                SegaBranchService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaBranchService>());


            // =================================================
            // STAGE 12 VISUAL EVIDENCE FOUNDATION
            //
            // Core vision semantics stay platform-neutral. The WPF host
            // supplies the Windows desktop pixel-capture backend.
            // Capture is on-demand. Dedicated visual understanding consumes
            // only grounded captures and returns model-derived observations.
            // =================================================

            builder.Services.AddSingleton<
                ISegaScreenCaptureBackend,
                WindowsScreenCaptureBackend>();


            builder.Services.AddSingleton<
                SegaVisualEvidenceService>();


            builder.Services.AddSingleton<
                SegaVisualUnderstandingService>();


            // =================================================
            // TRUSTED PRIMITIVE CAPABILITIES
            //
            // Real primitive handlers share one persistent authority service.
            // Only the trusted Permissions UI can create or revoke grants.
            // =================================================

            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaScreenCaptureCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaVisualInspectCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaFileReadCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaDirectoryListCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaFileMetadataCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaFileWriteCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaFileCopyCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaFileMoveCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaFileDeleteCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaDirectoryCreateCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaProcessListCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaProcessStartCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaProcessStopCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaShellExecuteCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaHttpRequestCapabilityHandler>();


            builder.Services.AddSingleton<
                ISegaCapabilityHandler,
                SegaHttpDownloadCapabilityHandler>();


            builder.Services.AddSingleton<
                SegaCapabilityRegistry>();


            builder.Services.AddSingleton<SegaAuthorityStore>();
            builder.Services.AddSingleton<SegaCapabilityRequestPolicy>();
            builder.Services.AddSingleton<SegaCapabilityApprovalBroker>();
            builder.Services.AddSingleton<SegaScopedCapabilityAuthorizer>();
            builder.Services.AddSingleton<ISegaCapabilityAuthorizer>(sp =>
                sp.GetRequiredService<SegaScopedCapabilityAuthorizer>());


            builder.Services.AddSingleton<
                SegaCapabilityService>();


            // =================================================
            // STAGE 11 DYNAMIC TOOL COMPOSITION
            // =================================================

            builder.Services.AddSingleton<
                SegaDynamicToolStore>();

            builder.Services.AddSingleton<
                SegaDynamicToolValidator>();

            builder.Services.AddSingleton<
                SegaDynamicToolExecutor>();

            builder.Services.AddSingleton<
                SegaDynamicToolService>();


            // =================================================
            // BRANCH-OWNED ASSIGNED WORK
            //
            // Branches own responsibility/continuity. Sega cognition
            // chooses bounded work; this service persists and tracks
            // the exact capability/tool assignment until a result
            // returns to Sega.
            // =================================================

            builder.Services.AddSingleton<
                SegaBranchWorkStore>();


            builder.Services.AddSingleton<
                SegaBranchWorkService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaBranchWorkService>());


            // =================================================
            // INTERACTION OBSERVATION
            // =================================================

            builder.Services.AddSingleton<
                SegaInteractionContextBuilder>();


            builder.Services.AddSingleton<
                SegaInteractionObservationService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaInteractionObservationService>());


            // =================================================
            // OLLAMA
            // =================================================

            builder.Services.AddSingleton<
                OllamaClient>();


            // =================================================
            // MAIN COGNITION
            // =================================================

            builder.Services.AddSingleton<
                SegaCognitionService>();


            builder.Services.AddSingleton<
                SegaCognitionContextBuilder>();


            // =================================================
            // PC WORLD
            // =================================================

            builder.Services.AddSingleton<
                PcAwarenessService>();


            builder.Services.AddSingleton<
                SegaPresenceService>();


            builder.Services.AddSingleton<
                PcWorldStateService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        PcWorldStateService>());


            // =================================================
            // SEGA BODY CONTROL / PLACEMENT
            // =================================================

            builder.Services.AddSingleton<
                SegaBodyPlacementStore>();


            builder.Services.AddSingleton<
                SegaBodyPlacementService>();


            builder.Services.AddSingleton<
                SegaBodyCommandService>();


            builder.Services.AddSingleton<
                SegaBodyControllerService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaBodyControllerService>());


            // =================================================
            // CONVERSATION
            // =================================================

            builder.Services.AddSingleton<
                ConversationManager>();


            // =================================================
            // SEGA MIND / EXECUTIVE
            // =================================================

            builder.Services.AddSingleton<
                SegaMindActivityTracker>();


            builder.Services.AddSingleton<
                SegaExecutive>();


            builder.Services.AddSingleton<
                SegaMindRuntime>();


            builder.Services.AddSingleton<
                SegaOutputDispatcher>();


            builder.Services.AddSingleton<
                SegaBackgroundProcessor>();


            builder.Services.AddHostedService<
                SegaTemporalCommitmentReconciliationService>();


            builder.Services.AddHostedService<
                SegaTemporalCommitmentSchedulerService>();


            builder.Services.AddHostedService<
                SegaGoalSchedulerService>();


            builder.Services.AddHostedService<
                SegaBranchRunnerService>();


            builder.Services.AddHostedService<
                SegaBranchWorkReconsiderationService>();


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
                SegaVoiceExpressionService>();


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
                MainWindowViewModel>();


            // =================================================
            // BUILD
            // =================================================

            _host =
                builder.Build();


            SegaOllamaApiKeyService ollamaApiKeys =
                _host.Services.GetRequiredService<SegaOllamaApiKeyService>();


            ollamaApiKeys.SetPromptHandler(
                PromptForOllamaCredentialAsync);


            if (!ollamaApiKeys.HasKey)
            {
                SegaOllamaCredentialResolution? initialResolution =
                    await PromptForOllamaCredentialAsync(
                        new SegaOllamaCredentialRequest
                        {
                            Problem = SegaOllamaCredentialProblem.Missing,
                            HasCurrentKey = false
                        },
                        CancellationToken.None);


                if (initialResolution == null ||
                    initialResolution.Kind != SegaOllamaCredentialResolutionKind.ReplaceKey)
                {
                    Shutdown(0);
                    return;
                }


                ollamaApiKeys.SaveKey(
                    initialResolution.ApiKey);
            }


            await _host.StartAsync();


            MainWindowViewModel viewModel =
                _host.Services
                    .GetRequiredService<
                        MainWindowViewModel>();


            MainWindow window =
                new(
                    viewModel);


            MainWindow =
                window;


            window.AttachAuthorization(
                _host.Services.GetRequiredService<SegaAuthorityStore>(),
                _host.Services.GetRequiredService<SegaScopedCapabilityAuthorizer>(),
                _host.Services.GetRequiredService<SegaCapabilityApprovalBroker>());


            SegaRuntimeSettingsService runtimeSettings =
                _host.Services.GetRequiredService<SegaRuntimeSettingsService>();


            window.AttachSettings(
                runtimeSettings);


            window.Show();


            SegaStateService segaState =
                _host.Services
                    .GetRequiredService<
                        SegaStateService>();


            window.AttachSegaState(
                segaState);


            window.AttachBranchActivity(
                _host.Services.GetRequiredService<SegaBranchService>(),
                _host.Services.GetRequiredService<SegaBranchWorkService>());


            window.AttachContinuityState(
                _host.Services.GetRequiredService<SegaSelfModelService>());


            SegaVisualIntentService visualIntent =
                _host.Services
                    .GetRequiredService<
                        SegaVisualIntentService>();


            SegaPresenceService segaPresence =
                _host.Services
                    .GetRequiredService<
                        SegaPresenceService>();


            SegaBodyCommandService bodyCommands =
                _host.Services
                    .GetRequiredService<
                        SegaBodyCommandService>();


            SegaBodyPlacementService placement =
                _host.Services
                    .GetRequiredService<
                        SegaBodyPlacementService>();


            CompanionWindow companion =
                new(
                    segaState,
                    visualIntent,
                    segaPresence,
                    bodyCommands,
                    placement,
                    runtimeSettings);


            companion.Show();


            companion.AttachToMainWindow(
                window);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                ex.ToString(),
                "SegaAI Startup Error",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);


            Shutdown(
                1);
        }
    }


    // =========================================================
    // OLLAMA CREDENTIAL UI
    // =========================================================

    private async Task<SegaOllamaCredentialResolution?>
        PromptForOllamaCredentialAsync(
            SegaOllamaCredentialRequest request,
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


    private SegaOllamaCredentialResolution? ShowOllamaCredentialDialog(
        SegaOllamaCredentialRequest request)
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
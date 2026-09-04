/*
 * filename: App.xaml.cs
 */

using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using SegaAgent.AI.Ollama;
using SegaAgent.AI.Planner;
using SegaAgent.AI.Responder;
using SegaAgent.Agent;
using SegaAgent.Agent.State;
using SegaAgent.Character.Dynamics;
using SegaAgent.Character.History;
using SegaAgent.Character.Interaction;
using SegaAgent.Character.State;
using SegaAgent.Conversation;
using SegaAgent.Memory.LongTerm;
using SegaAgent.PC.Awareness;
using SegaAgent.Perception;
using SegaAgent.Semantic;
using SegaAgent.UI.Companion;
using SegaAgent.UI.ViewModels;
using SegaAgent.Voice;
using SegaAgent.Voice.Groq;
using SegaAgent.Embodiment;
using SegaAgent.Embodiment.Body;

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
                SegaLongTermMemoryService>();


            builder.Services.AddSingleton<
                SegaMemoryConsolidator>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        SegaLongTermMemoryService>());


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
                OllamaClient>(
                    sp =>
                        ActivatorUtilities
                            .CreateInstance<
                                OllamaClient>(
                                    sp));


            // =================================================
            // AI
            // =================================================

            builder.Services.AddSingleton<
                AgentPlanner>();


            builder.Services.AddSingleton<
                AgentResponder>();


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
            // AGENT
            // =================================================

            builder.Services.AddSingleton<
                AgentActivityTracker>();


            builder.Services.AddSingleton<
                AgentCore>();


            builder.Services.AddSingleton<
                AgentResponseDispatcher>();


            builder.Services.AddSingleton<
                AgentBackgroundProcessor>();


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


            window.Show();


            SegaStateService segaState =
                _host.Services
                    .GetRequiredService<
                        SegaStateService>();


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
                    placement);


            companion.Show();
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
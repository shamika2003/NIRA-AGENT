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
using SegaAgent.PC.Awareness;
using SegaAgent.Perception;
using SegaAgent.Semantic;
using SegaAgent.UI.Companion;
using SegaAgent.UI.ViewModels;
using SegaAgent.Voice;

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
                PcWorldStateService>();


            builder.Services.AddHostedService(
                sp =>
                    sp.GetRequiredService<
                        PcWorldStateService>());


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
            // VOICE
            // =================================================

            builder.Services.AddSingleton<
                IVoiceService,
                PiperVoiceService>();


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


            CompanionWindow companion =
                new(
                    segaState);


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
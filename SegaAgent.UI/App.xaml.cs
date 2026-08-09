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
using SegaAgent.Conversation;
using SegaAgent.Perception;
using SegaAgent.UI.Companion;
using SegaAgent.UI.ViewModels;
using SegaAgent.Voice;
using SegaAgent.PC.Awareness;

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


    // =========================================================
    // STARTUP
    // =========================================================

    protected override async void OnStartup(
        WpfStartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder =
                Host.CreateApplicationBuilder();


            // =================================================
            // CORE SERVICES
            // =================================================

            builder.Services.AddSingleton<HttpClient>();


            // =================================================
            // OLLAMA
            // =================================================

            builder.Services.AddSingleton<OllamaClient>(
                sp =>
                    ActivatorUtilities.CreateInstance<OllamaClient>(
                        sp
                    )
            );


            // =================================================
            // AI
            // =================================================

            builder.Services.AddSingleton<AgentPlanner>();

            builder.Services.AddSingleton<AgentResponder>();


            // =================================================
            // PC AWARENESS
            // =================================================

            builder.Services.AddSingleton<PcAwarenessService>();


            // =================================================
            // CONVERSATION
            // =================================================

            builder.Services.AddSingleton<ConversationManager>();


            // =================================================
            // AGENT ACTIVITY
            //
            // IMPORTANT:
            //
            // This MUST be singleton.
            //
            // AgentCore, perception and proactive systems all
            // need to share the exact same activity state.
            // =================================================

            builder.Services.AddSingleton<AgentActivityTracker>();


            // =========================================================
            // AGENT CORE
            // =========================================================

            builder.Services.AddSingleton<AgentCore>();

            builder.Services.AddSingleton<AgentResponseDispatcher>();


            // =================================================
            // PERCEPTION SYSTEM
            //
            // AttentionManager:
            // Controls whether Sega is allowed to interrupt
            // the user.
            //
            // PerceptionAnalyzer:
            // Converts PC state changes into meaningful
            // perception events.
            //
            // PcMonitorService:
            // Continuously monitors the PC.
            //
            // CompanionTimerService:
            // Performs scheduled proactive companion checks.
            // =================================================

            builder.Services.AddSingleton<AttentionManager>();

            builder.Services.AddSingleton<PerceptionAnalyzer>();

            builder.Services.AddHostedService<PcMonitorService>();

            builder.Services.AddHostedService<CompanionTimerService>();


            // =================================================
            // VOICE
            // =================================================

            builder.Services.AddSingleton<
                IVoiceService,
                PiperVoiceService
            >();

            builder.Services.AddSingleton<VoiceQueue>();


            // =================================================
            // UI
            // =================================================

            builder.Services.AddSingleton<MainWindowViewModel>();


            // =================================================
            // BUILD HOST
            // =================================================

            _host =
                builder.Build();


            // =================================================
            // START HOST
            //
            // This starts:
            //
            // - PcMonitorService
            // - CompanionTimerService
            //
            // in addition to the rest of the application.
            // =================================================

            await _host.StartAsync();


            // =================================================
            // MAIN WINDOW
            // =================================================

            var viewModel =
                _host.Services
                    .GetRequiredService<MainWindowViewModel>();


            var window =
                new MainWindow(
                    viewModel
                );


            MainWindow =
                window;


            window.Show();


            // =================================================
            // COMPANION WINDOW
            // =================================================

            var voiceQueue =
                _host.Services
                    .GetRequiredService<VoiceQueue>();


            var companion =
                new CompanionWindow(
                    voiceQueue
                );


            companion.Show();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                ex.ToString(),
                "SegaAI Startup Error",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error
            );


            Shutdown(1);
        }
    }


    // =========================================================
    // EXIT
    // =========================================================

    protected override async void OnExit(
        WpfExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();

            _host.Dispose();

            _host = null;
        }


        base.OnExit(e);
    }
}
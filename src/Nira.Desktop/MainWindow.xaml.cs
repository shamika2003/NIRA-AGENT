using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nira.Core;
using Nira.Infrastructure;

namespace Nira.Desktop;

public partial class MainWindow : Window
{
    private readonly NiraRuntime _runtime = NiraRuntime.CreateDefault();
    private readonly IApprovalBroker _approvals;
    private Guid _conversationId = Guid.NewGuid();
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _approvals = new WpfApprovalBroker(this);
        Closed += (_, _) => _runtime.Dispose();
        AddLine("NIRA", "NIRA is ready. I can use authorized tools and preserve task state.");
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string text = PromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        PromptBox.Clear();
        AddLine("You", text);
        SetBusy(true);
        try
        {
            var progress = new Progress<AgentProgress>(p =>
                StatusLabel.Text = p.Kind + " · " + p.Summary);
            AgentRunResult outcome;
            try
            {
                outcome = await _runtime.Engine.RunAsync(
                    _conversationId, text, _approvals, progress);
            }
            catch (ModelAuthenticationException auth)
            {
                AddLine("System", auth.Message);
                string? key = PromptModelKey();
                if (string.IsNullOrWhiteSpace(key))
                {
                    StatusLabel.Text = "Model credential required. Task preserved; send again after configuration.";
                    return;
                }
                _runtime.ModelCredentials.Save(key);
                // The first attempt already persisted the user's message.
                // Resume it without appending the message a second time.
                outcome = await _runtime.Engine.RunAsync(
                    _conversationId, null, _approvals, progress);
            }
            AddLine("NIRA", outcome.Message);
            StatusLabel.Text = "Status: " + outcome.Status + " · Conversation: " + _conversationId;
            if (outcome.Status == ConversationStatus.Completed)
                _conversationId = Guid.NewGuid();
        }
        catch (Exception ex)
        {
            AddLine("System", ex.GetType().Name + ": " + ex.Message);
            StatusLabel.Text = "Task state retained. Check credentials, endpoint, or diagnostic CLI.";
        }
        finally { SetBusy(false); }
    }

    private string? PromptModelKey()
    {
        var dialog = new Window {
            Owner = this, Title = "NIRA — Model authentication", Width = 480,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(13, 26, 49)),
            Foreground = Brushes.White
        };
        var layout = new StackPanel { Margin = new Thickness(22) };
        layout.Children.Add(new TextBlock {
            Text = "The model endpoint requires a valid API key. Enter a new key to save it encrypted for your Windows account, then retry the current task.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });
        var password = new PasswordBox { Padding = new Thickness(10), MinHeight = 34 };
        layout.Children.Add(password);
        var actions = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 85 };
        var save = new Button { Content = "Save and retry", MinWidth = 130, IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) => {
            if (!string.IsNullOrWhiteSpace(password.Password)) dialog.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        layout.Children.Add(actions);
        dialog.Content = layout;
        dialog.Loaded += (_, _) => password.Focus();
        return dialog.ShowDialog() == true ? password.Password : null;
    }

    private void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _conversationId = Guid.NewGuid();
        ConversationPanel.Children.Clear();
        AddLine("NIRA", "New conversation started.");
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Guid? last = await _runtime.Conversations.FindRecentUnfinishedAsync(default);
        if (last is null) { AddLine("System", "No unfinished task was found."); return; }
        _conversationId = last.Value;
        ConversationPanel.Children.Clear();
        AddLine("NIRA", "Resumed the saved task. Tell me to continue, or answer the pending question.");
        StatusLabel.Text = "Resumed: " + _conversationId;
    }

    private void Workspace_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe") {
            ArgumentList = { _runtime.Workspace }, UseShellExecute = true });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SendButton.IsEnabled = !busy;
        PromptBox.IsEnabled = !busy;
    }

    private void AddLine(string speaker, string message)
    {
        var header = new TextBlock { Text = speaker, Foreground = new SolidColorBrush(
            speaker == "NIRA" ? Color.FromRgb(95, 193, 255) : Colors.LightSkyBlue),
            FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 10, 0, 3) };
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.WhiteSmoke, FontSize = 14, Margin = new Thickness(0, 0, 0, 10) };
        ConversationPanel.Children.Add(header);
        ConversationPanel.Children.Add(body);
        ChatScroller.ScrollToEnd();
    }
}

internal sealed class WpfApprovalBroker(Window owner) : IApprovalBroker
{
    public async Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        bool decision = false;
        await owner.Dispatcher.InvokeAsync(() => {
            decision = MessageBox.Show(owner, request.Description,
                "NIRA — action approval", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        });
        return decision;
    }
}

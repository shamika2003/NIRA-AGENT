using System.IO;
using NIRAAgent.UI;
using NIRAAgent.UI.Theming;
using NIRAAgent.UI.Companion.Particles;
using NIRAAgent.Embodiment;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NIRAAgent.UI.Presentation;
using NIRAAgent.UI.Typing;
using Nira.Core;
using Nira.Infrastructure;

namespace Nira.Desktop;

public partial class MainWindow : Window
{
    private readonly NiraRuntime _runtime = NiraRuntime.CreateDefault();
    private readonly IApprovalBroker _approvals;
    private readonly NIRAWorkAreaWindowGuard _workAreaGuard;
    private Guid _conversationId = Guid.NewGuid();
    private bool _busy;
    private CancellationTokenSource? _activeTaskCancellation;
    private readonly TaskAbandonConfirmation _abandonConfirmation = new();
    private bool _abandonConfirmed;
    private bool _pauseRequested;
    private bool _completed;
    private bool _hasTask;
    private TaskCompletionSource<bool>? _approvalDecision;
    private int _runGeneration;
    private readonly ParticleEntityControl _presence = new() { IsHitTestVisible = false };
    private IReadOnlyList<SavedConversationSummary> _history = Array.Empty<SavedConversationSummary>();
    private bool _historyReady;
    private bool _historyLoading;
    private bool _leftCollapsed;
    private bool _rightCollapsed;
    private bool _leftTransitioning, _rightTransitioning;
    private StackPanel? _journeyPanel;
    private NIRAImageCreationPlaceholder? _artworkIndicator;
    private NIRATypingIndicator? _journeyDots;
    private TextBlock? _journeyStatus;
    private RichTextBox? _journeyPreview;
    private readonly NIRALivePreview _livePreview = new();
    private bool _previewRenderQueued;
    private int _previewEpoch;
    private readonly List<string> _selectedAttachments = [];
    private readonly Dictionary<Guid, List<DeveloperChatExport.ActivityEntry>> _debugActivity = new();

    public MainWindow()
    {
        NIRAThemeManager.Initialize();
        InitializeComponent();
        ChatScroller.PreviewMouseWheel += (_, e) => NIRAChatWheel.Forward(ChatScroller, e);
#if DEBUG
        DeveloperCopyChatButton.Visibility = Visibility.Visible;
        Title = "NIRA by ELVARA — " + NIRARichAnswer.Revision;
#endif
        _workAreaGuard = new NIRAWorkAreaWindowGuard(this, fitNormalWindow: true);
        Loaded += (_, _) => { WindowState = WindowState.Maximized; FitWorkspaceLayout(); PromptBox.Focus(); };
        SizeChanged += (_, _) => FitWorkspaceLayout();
        NIRADockHost.Children.Add(_presence);
        _presence.PrepareStartupEntrance();
        Loaded += (_, _) => _presence.StartStartupEntrance();
        BackgroundEclipse.Source = NIRABackdropBorder.LoadBitmap("nira-background-eclipse.png");
        BackgroundHalo.Source = NIRABackdropBorder.LoadBitmap("nira-background-halo.png");
        BrandImage.Source = NIRABackdropBorder.LoadBitmap("Nira-Icon.png");
        if (BrandImage.Source is null) BrandImage.Visibility = Visibility.Collapsed;
        if (NIRABackdropBorder.LoadBitmap("Nira-Icon.ico") is { } icon) Icon = icon;
        UpdateTheme();
        GreetingText.Text = DateTime.Now.Hour < 12 ? "Good morning." : DateTime.Now.Hour < 18 ? "Good afternoon." : "Good evening.";
        _historyReady = true;
        Loaded += async (_, _) => await RefreshHistoryAsync();
        _approvals = new WpfApprovalBroker(this, RequestApprovalOnUiAsync);
        ModelLabel.Text = Environment.GetEnvironmentVariable("NIRA_MODEL_NAME") ?? "gpt-oss:120b";
        VisionLabel.Text = Environment.GetEnvironmentVariable("NIRA_VISION_MODEL") ?? "Not configured";
        UpdateConversationLabel();
        SetBusy(false);
        Closed += (_, _) => { _workAreaGuard.Dispose(); _runtime.Dispose(); };
        Closing += (_, args) =>
        {
            if (_busy)
            {
                args.Cancel = true;
                StatusLabel.Text = "Task active. Press Pause, or press Abandon task twice to stop it safely.";
            }
            else _miniStop?.Invoke();
        };
        VisionActivityText.Text = "VISION: " + VisionLabel.Text;
        if (BackgroundEclipse.Source is null || BackgroundHalo.Source is null || BrandImage.Source is null)
            StatusLabel.Text = "Some original artwork could not be loaded. Check the installed Assets folder.";
    }

    private void FitWorkspaceLayout()
    {
        if (!IsLoaded || MainWorkspace.Visibility != Visibility.Visible) return;
        // Keep the original four-card landing layout on desktop; adapt on smaller windows.
        double centerWidth = MainWorkspace.ActualWidth - LeftSidebarColumn.Width.Value - RightSidebarColumn.Width.Value - 24;
        LandingCards.Columns = centerWidth >= 780 ? 4 : 2;
        // At small work areas the greeting yields space to the composer. Cards
        // can be reopened by expanding the window, and quick tools stay available.
        LandingCards.Visibility = ActualHeight < 700 || (centerWidth < 780 && ActualHeight < 850)
            ? Visibility.Collapsed : Visibility.Visible;
        HistoryScroller.MaxHeight = Math.Max(100, MainWorkspace.ActualHeight - 355);
        // Keep the execution header and a real scrollable feed visible even on short windows.
        LeftControlsScroller.MaxHeight = Math.Max(100, MainWorkspace.ActualHeight - (MiniPlayerPanel.Visibility == Visibility.Visible ? 370 : 195));
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendPromptAsync();

    private async void PromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await SendPromptAsync();
        }
    }

    private async Task SendPromptAsync()
    {
        if (_busy) return;
        string text = PromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) && _selectedAttachments.Count == 0) return;
        if (_hasTask || _completed)
        {
            SetBusy(true);
            try
            {
                if (_runtime.Conversations is not SqliteConversationStore store)
                    throw new InvalidOperationException("Chat thread storage unavailable.");
                var saved = await store.LoadAsync(_conversationId, default);
                // Only an actual pending clarification belongs to the existing task.
                // A new message is never an implicit retry of interrupted work.
                if (saved is not null && saved.Objective.Length > 0 &&
                    saved.Status != ConversationStatus.AwaitingUser)
                {
                    _conversationId = await store.CreateFollowupAsync(_conversationId, default, startNewAfterPause: true);
                    _completed = false;
                    _hasTask = false;
                    UpdateConversationLabel();
                }
            }
            catch (Exception error) { Debug.WriteLine(error); AddLine("System", "Unable to continue this chat. Start a new conversation or explicitly retry the saved task."); return; }
            finally { SetBusy(false); }
        }
        string[] attachments = _selectedAttachments.ToArray();
        PromptBox.Clear();
        ClearAttachments();
        AddLine("You", text, attachments);
        await RunTaskAsync(text, attachments);
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy && _hasTask && !_completed) await RunTaskAsync(null);
    }

    private async Task RunTaskAsync(string? text, IReadOnlyList<string>? attachments = null)
    {
        if (_busy) return;
        _hasTask = true;
        UpdateConversationLabel();
        _abandonConfirmation.Reset();
        AbandonButton.Content = "Abandon task";
        _abandonConfirmed = false;
        _pauseRequested = false;
        var runningTaskId = _conversationId;
        var cancellation = new CancellationTokenSource();
        _activeTaskCancellation = cancellation;
        SetBusy(true);
        int generation = ++_runGeneration;
        ActivityPanel.Children.Clear();
        PlanLabel.Text = "Executing task";
        BeginJourney();
        try
        {
            var progress = new DesktopStreamingProgress(p =>
            {
                if (_busy && generation == _runGeneration) HandleProgress(p);
            });
            AgentRunResult outcome;
            try
            {
                outcome = await Task.Run(() => _runtime.Engine.RunAsync(
                    runningTaskId, text, _approvals, progress, cancellation.Token, attachments), cancellation.Token);
            }
            catch (ModelAuthenticationException auth)
            {
                Debug.WriteLine(auth);
                AddLine("System", "Your model access needs attention. Check or replace your API key.");
                string? key = PromptModelKey();
                if (string.IsNullOrWhiteSpace(key))
                {
                    StatusLabel.Text = "Model credential required. Task preserved; use Model API key and Retry current task.";
                    RunStateLabel.Text = "AUTH REQUIRED";
                    StateDot.Fill = BrushFor("#FBBF24");
                    return;
                }
                _runtime.ModelCredentials.Save(key);
                // The first attempt already persisted the user's message.
                // Resume it without appending the message a second time.
                outcome = await Task.Run(() => _runtime.Engine.RunAsync(
                    runningTaskId, null, _approvals, progress, cancellation.Token), cancellation.Token);
            }
            EndJourney();
            var recorded = await _runtime.Conversations.LoadAsync(runningTaskId, default);
            AddLine("NIRA", outcome.Message, media: recorded is null ? null : NIRAImageStrip.Create(recorded, _runtime.Workspace, this), videoUrls: recorded is null ? null : VideoEvidence.Allowed(recorded), videoTitles: recorded is null ? null : VideoEvidence.Titles(recorded), searchFallback: recorded is null ? null : VideoEvidence.SearchFallback(recorded));
            if (recorded is not null) AddArtifactCards(recorded);
            StatusLabel.Text = NIRAPublicDelivery.Status(outcome.Status);
            _completed = outcome.Status == ConversationStatus.Completed;
            RunStateLabel.Text = NIRAPublicDelivery.Status(outcome.Status).ToUpperInvariant();
            StateDot.Fill = BrushFor(_completed ? "#22D3A7" : "#FBBF24");
            PlanLabel.Text = NIRAPublicDelivery.Status(outcome.Status);
            if (_completed)
            {
                ActivityBar.IsIndeterminate = false;
                ActivityBar.Value = 1;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            RunStateLabel.Text = "STOPPED";
            StateDot.Fill = BrushFor("#FBBF24");
            PlanLabel.Text = "Execution interrupted; inspect saved state";
            AddLine("System", "NIRA stopped at your request. Actions already dispatched " +
                "remain recorded and may need reconciliation.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
#if DEBUG
            // Record the exception category without invoking rendering from an error handler.
            if (!_debugActivity.TryGetValue(runningTaskId, out var interruptedEvents))
            {
                if (_debugActivity.Count >= 100) _debugActivity.Remove(_debugActivity.Keys.First());
                _debugActivity[runningTaskId] = interruptedEvents = [];
            }
            if (interruptedEvents.Count < 1000)
                interruptedEvents.Add(new(DateTimeOffset.UtcNow, "error", "Execution interrupted: " + ex.GetType().Name, null));
#endif
            AddLine("System", "Something went wrong. Your task has been saved; you can retry when ready.");
            StatusLabel.Text = "Your task is retained. Check your connection or model settings.";
            RunStateLabel.Text = "ERROR";
            StateDot.Fill = BrushFor("#F87171");
            PlanLabel.Text = "Execution interrupted";
        }
        finally
        {
            ++_runGeneration;
            EndJourney();
            _activeTaskCancellation = null;
            _approvalDecision?.TrySetCanceled();
            ApprovalPanel.Visibility = Visibility.Collapsed;
            if (_abandonConfirmed || _pauseRequested)
                await FinishInterruptionAsync(runningTaskId, _abandonConfirmed);
            cancellation.Dispose();
            SetBusy(false);
            _abandonConfirmation.Reset();
            AbandonButton.Content = "Abandon task";
            await RefreshHistoryAsync();
        }
    }

    // No OS-wide BlockInput: the user can always regain control of the PC.
    // During a run the NIRA prompt/navigation are disabled, but Pause and
    // two-press Abandon stay live. The host serializes approved desktop actions.
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) return;
        _pauseRequested = true;
        _activeTaskCancellation?.Cancel();
        StatusLabel.Text = "Pausing NIRA; waiting for any in-flight operation to stop safely.";
    }

    private async void Abandon_Click(object sender, RoutedEventArgs e)
    {
        if (!_abandonConfirmation.ConfirmOrArm(DateTimeOffset.UtcNow))
        {
            AbandonButton.Content = "Confirm abandon";
            StatusLabel.Text = "Press Confirm abandon again within 15 seconds. " +
                "Task history is retained; unsaved work in other applications is not deleted.";
            return;
        }
        AbandonButton.Content = "Abandon task";
        _abandonConfirmed = true;
        if (_busy)
        {
            _activeTaskCancellation?.Cancel();
            StatusLabel.Text = "Stopping the task after confirmed abandonment.";
        }
        else
        {
            SetBusy(true);
            // Prevent a concurrent send/resume while the saved task is being updated.
            PauseButton.IsEnabled = false;
            AbandonButton.IsEnabled = false;
            try
            {
                await FinishInterruptionAsync(_conversationId, abandon: true);
                AddLine("System", "Task abandoned; starting a new conversation. History retained.");
            }
            finally { SetBusy(false); }
        }
    }

    private async Task FinishInterruptionAsync(Guid id, bool abandon)
    {
        try
        {
            var state = await _runtime.Conversations.LoadAsync(id, CancellationToken.None);
            if (state is not null)
            {
                if (abandon)
                    TaskAbandonConfirmation.MarkAbandoned(state);
                else if (state.Status == ConversationStatus.Running)
                {
                    state.Status = ConversationStatus.Paused;
                    state.RetryBlockCode = "USER_PAUSED";
                }
                await _runtime.Conversations.SaveAsync(state, CancellationToken.None);
                StatusLabel.Text = NIRAPublicDelivery.Status(state.Status);
                RunStateLabel.Text = NIRAPublicDelivery.Status(state.Status).ToUpperInvariant();
                PlanLabel.Text = NIRAPublicDelivery.Status(state.Status);
                StateDot.Fill = BrushFor(state.Status == ConversationStatus.Completed ? "#22D3A7" : "#FBBF24");
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            AddLine("System", "Unable to save the task interruption.");
        }
        if (abandon)
        {
            _conversationId = Guid.NewGuid();
            _hasTask = false;
            _completed = false;
            _abandonConfirmed = false;
            RunStateLabel.Text = "READY";
            StateDot.Fill = BrushFor("#22D3A7");
            PlanLabel.Text = "No active task";
            ActivityBar.Value = 0;
            UpdateConversationLabel();
        }
    }

    private string? PromptModelKey()
    {
        var dialog = new Window {
            Owner = this, Title = "NIRA — Model authentication", Width = 480,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = BrushFor("#081230"),
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
        return ShowDimmedDialog(dialog) == true ? password.Password : null;
    }

    private void ModelKey_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            string? key = PromptModelKey();
            if (string.IsNullOrWhiteSpace(key)) return;
            _runtime.ModelCredentials.Save(key);
            StatusLabel.Text = "Model credential saved encrypted for this Windows user.";
        }
        catch (Exception error) { Debug.WriteLine(error); AddLine("System", "Unable to save the credential."); }
    }

    private void UpdateConversationLabel() =>
        ConversationLabel.Text = "Current conversation";

    private void NewConversation_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) StartNewConversation();
    }

    private void StartNewConversation()
    {
        _abandonConfirmation.Reset();
        AbandonButton.Content = "Abandon task";
        _conversationId = Guid.NewGuid();
        ClearAttachments();
        _completed = false;
        _hasTask = false;
        _abandonConfirmed = false;
        _pauseRequested = false;
        _miniStop?.Invoke();
        ConversationPanel.Children.Clear();
        ActivityPanel.Children.Clear();
        PlanLabel.Text = "No active task";
        ActivityBar.IsIndeterminate = false;
        ActivityBar.Value = 0;
        RunStateLabel.Text = "READY";
        StateDot.Fill = BrushFor("#22D3A7");
        StatusLabel.Text = "Ready — new conversation";
        UpdateConversationLabel();
        SetBusy(false);
        ShowLanding();
        PromptBox.Focus();
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        PauseButton.IsEnabled = false;
        AbandonButton.IsEnabled = false;
        try
        {
            Guid? last = await _runtime.Conversations.FindRecentUnfinishedAsync(default);
            if (last is null) { StatusLabel.Text = "No unfinished task was found."; return; }
            await LoadSavedTaskAsync(last.Value);
        }
        catch (Exception error) { Debug.WriteLine(error); AddLine("System", "Unable to open the saved task."); }
        finally { SetBusy(false); }
    }

    private async Task LoadSavedTaskAsync(Guid id)
    {
        ConversationState? state = await _runtime.Conversations.LoadAsync(id, default);
        if (state is null) { StatusLabel.Text = "Saved task not found."; return; }
        _abandonConfirmation.Reset();
        AbandonButton.Content = "Abandon task";
        _conversationId = id;
        ClearAttachments();
        _hasTask = true;
        _completed = state.Status is ConversationStatus.Completed or ConversationStatus.Failed;
        _miniStop?.Invoke();
        ConversationPanel.Children.Clear();
        ActivityPanel.Children.Clear();
        ShowLanding();
        UpdateConversationLabel();
        var thread = _runtime.Conversations is SqliteConversationStore store
            ? await store.LoadThreadAsync(id, default) : new[] { state };
        foreach (var turn in thread)
        {
            var media = NIRAImageStrip.Create(turn, _runtime.Workspace, this);
            var videoUrls = VideoEvidence.Allowed(turn);
            var videoTitles = VideoEvidence.Titles(turn);
            var searchFallback = VideoEvidence.SearchFallback(turn);
            foreach (ChatEntry entry in turn.Messages)
            {
                if (entry.Role == "user" && !string.IsNullOrWhiteSpace(entry.Content)) AddLine("You", entry.UserDisplayText ?? entry.Content, entry.AttachmentPaths);
                else if (entry.Role == "assistant" && entry.ToolCalls is not { Count: > 0 } && !string.IsNullOrWhiteSpace(entry.Content))
                {
                    bool final = entry.Content == turn.LastAnswer;
                    AddLine("NIRA", ImageEvidenceBinding.IsBareRetrievalArguments(turn, entry.Content)
                        ? "I found image references, but did not finish loading the photos. Please ask again to see them."
                        : entry.Content, media: final ? media : null, videoUrls: final ? videoUrls : null, videoTitles: final ? videoTitles : null, searchFallback: final ? searchFallback : null);
                    if (final) media = null;
                }
            }
            if (!string.IsNullOrWhiteSpace(turn.PendingQuestion)) AddLine("NIRA", turn.PendingQuestion, media: media, videoUrls: videoUrls, videoTitles: videoTitles);
            else if (!string.IsNullOrWhiteSpace(turn.LastAnswer) && !turn.Messages.Any(m => m.Role == "assistant" && m.Content == turn.LastAnswer && m.ToolCalls is not { Count: > 0 })) AddLine("NIRA", ImageEvidenceBinding.IsBareRetrievalArguments(turn, turn.LastAnswer) ? "I found image references, but did not finish loading the photos. Please ask again to see them." : turn.LastAnswer, media: media, videoUrls: videoUrls, videoTitles: videoTitles, searchFallback: searchFallback);
            else if (media is not null) ConversationPanel.Children.Add(media);
            AddArtifactCards(turn);
        }
        foreach (AgentOperation operation in state.Operations.TakeLast(50))
            AddProgress(new AgentProgress(operation.Status.ToString().ToLowerInvariant(), "", operation.ToolName),
                operation.CompletedUtc ?? operation.CreatedUtc);
        PlanLabel.Text = state.Plan.Count == 0 ? "No active plan" : $"Progress: {state.Plan.Count(step => step.Status == "completed")}/{state.Plan.Count}";
        RunStateLabel.Text = NIRAPublicDelivery.Status(state.Status).ToUpperInvariant();
        StateDot.Fill = BrushFor(state.Status == ConversationStatus.Completed ? "#31D39A" : "#FBBF24");
        ActivityBar.Value = state.Status == ConversationStatus.Completed ? 1 : 0;
        StatusLabel.Text = NIRAPublicDelivery.Status(state.Status);
    }

    private async Task RefreshHistoryAsync()
    {
        if (!_historyReady || _historyLoading || _busy) return;
        _historyLoading = true;
        try
        {
            if (_runtime.Conversations is SqliteConversationStore store)
                _history = await store.ListRecentAsync(default);
            if (!_busy) RenderHistory();
        }
        catch (Exception error) { Debug.WriteLine(error); StatusLabel.Text = "Unable to read saved tasks."; }
        finally { _historyLoading = false; }
    }

    private void RenderHistory()
    {
        if (!_historyReady) return;
        string filter = PastChatFilterBox.Text.Trim();
        PastChatsListPanel.Children.Clear();
        var visible = _history.Where(x => x.ThreadTitle.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        PastChatsCountText.Text = visible.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        NoPastChatsText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        string lastGroup = "";
        foreach (var item in visible)
        {
            DateTime day = item.UpdatedUtc.LocalDateTime.Date;
            string dateGroup = day == DateTime.Today ? "TODAY" : day == DateTime.Today.AddDays(-1)
                ? "YESTERDAY" : day.ToString("MMM d, yyyy").ToUpperInvariant();
            if (dateGroup != lastGroup)
            {
                var heading = new TextBlock { Text = dateGroup, FontSize = 9, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2,8,0,6) };
                heading.SetResourceReference(TextBlock.ForegroundProperty, "NIRACyanBrush");
                PastChatsListPanel.Children.Add(heading);
                lastGroup = dateGroup;
            }
            string objective = string.IsNullOrWhiteSpace(item.ThreadTitle) ? "Past conversation" : item.ThreadTitle;
            var detail = new StackPanel();
            var title = new TextBlock { Text = objective.Replace('\r',' ').Replace('\n',' '), FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            detail.Children.Add(title);
            var subtitle = new TextBlock { Text = item.Status + "  ·  " + item.UpdatedUtc.LocalDateTime.ToString("h:mm tt"), FontSize = 10, Margin = new Thickness(0,5,0,0), TextTrimming = TextTrimming.CharacterEllipsis };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush");
            detail.Children.Add(subtitle);
            var card = new Button { Content = detail, Style = (Style)FindResource("HistoryCardStyle"), Margin = new Thickness(0,0,0,7), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = objective };
            card.Click += async (_, _) => {
                if (_busy) return;
                SetBusy(true); PauseButton.IsEnabled = false; AbandonButton.IsEnabled = false;
                try { await LoadSavedTaskAsync(item.Id); }
                catch (Exception error) { Debug.WriteLine(error); AddLine("System", "Unable to open the saved task."); }
                finally { SetBusy(false); }
            };
            PastChatsListPanel.Children.Add(card);
        }
    }

    private async void DeveloperCopyChat_Click(object sender, RoutedEventArgs e)
    {
#if DEBUG
        if (_busy || !_hasTask) return;
        var selected = _conversationId;
        SetBusy(true);
        try
        {
            var thread = _runtime.Conversations is SqliteConversationStore store
                ? await store.LoadThreadAsync(selected, default)
                : Array.Empty<ConversationState>();
            if (thread.Count == 0) { StatusLabel.Text = "No saved chat to export."; return; }
            string report = DeveloperChatExport.Build(thread, _debugActivity, ConversationPanel,
                NIRAThemeManager.CurrentMode.ToString(), ActualWidth, ActualHeight,
                LeftSidebarColumn.Width.Value, RightSidebarColumn.Width.Value,
                ModelLabel.Text, VisionLabel.Text);
            var review = new Window { Owner = this, Title = "NIRA developer chat export — review before sharing",
                Width = 940, Height = 690, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var layout = new DockPanel { Margin = new Thickness(14) };
            var warning = new TextBlock { Text = "Developer diagnostic only. Contains chat text, local paths, tool arguments and results. Secrets may be present. Review and remove sensitive information before copying or sharing. No data is sent automatically.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
            DockPanel.SetDock(warning, Dock.Top); layout.Children.Add(warning);
            var copy = new Button { Content = "Copy reviewed details", Padding = new Thickness(12,7,12,7), Margin = new Thickness(0,10,0,0) };
            DockPanel.SetDock(copy, Dock.Bottom); layout.Children.Add(copy);
            var text = new TextBox { Text = report, AcceptsReturn = true, AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            layout.Children.Add(text); review.Content = layout;
            copy.Click += (_, _) => { try { Clipboard.SetText(text.Text); review.DialogResult = true; }
                catch (System.Runtime.InteropServices.ExternalException) { warning.Text = "Clipboard is busy. Please try again."; } };
            if (ShowDimmedDialog(review) == true) StatusLabel.Text = "Reviewed developer chat details copied.";
        }
        catch (Exception error) { StatusLabel.Text = "Chat export failed: " + error.GetType().Name; }
        finally { SetBusy(false); }
#endif
    }

    private async void RefreshPastChats_Click(object sender, RoutedEventArgs e) => await RefreshHistoryAsync();
    private void PastChatFilter_TextChanged(object sender, TextChangedEventArgs e) { if (_historyReady) RenderHistory(); }

    private void Workspace_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") {
            ArgumentList = { _runtime.Workspace }, UseShellExecute = true }); }
        catch (Exception error) { Debug.WriteLine(error); AddLine("System", "Unable to open your workspace."); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DeveloperCopyChatButton.IsEnabled = !busy && _hasTask;
        GalleryNavButton.IsEnabled = !busy;
        MemoryNavButton.IsEnabled = !busy;
        _presence.SetIntent(busy ? NIRAVisualIntent.RestingOrb with { Energy = 0.65, Focus = 0.7, Pulse = 0.46 } : NIRAVisualIntent.RestingOrb);
        AttachFilesButton.IsEnabled = !busy;
        AttachmentPanel.IsEnabled = !busy;
        PastChatsListPanel.IsEnabled = !busy;
        SendButton.IsEnabled = !busy;
        PromptBox.IsEnabled = !busy;
        NewConversationButton.IsEnabled = !busy;
        ResumeButton.IsEnabled = !busy;
        RetryButton.IsEnabled = !busy && _hasTask && !_completed;
        ModelKeyButton.IsEnabled = !busy;
        WorkspaceButton.IsEnabled = !busy;
        ActivityBar.IsIndeterminate = busy && _activeTaskCancellation is not null;
        if (busy && _activeTaskCancellation is not null)
        {
            ActivityBar.Value = 0;
            RunStateLabel.Text = "RUNNING";
            StateDot.Fill = BrushFor("#00E5FF");
        }
        PauseButton.IsEnabled = busy;
        CompactPauseButton.IsEnabled = busy;
        // The abandonment confirmation is always reachable, even during a run.
        AbandonButton.IsEnabled = busy || (_hasTask && !_completed);
    }

    private static SolidColorBrush BrushFor(string color) =>
        new(Color.FromRgb(Convert.ToByte(color.Substring(1, 2), 16),
            Convert.ToByte(color.Substring(3, 2), 16), Convert.ToByte(color.Substring(5, 2), 16)));

    private void AddProgress(AgentProgress progress, DateTimeOffset? recordedAt = null)
    {
        _ = recordedAt;
        string? label = NIRAPublicDelivery.Activity(progress);
        if (label is null) return;
        StatusLabel.Text = label;
        PlanLabel.Text = label;
    }

    private void ShowLanding()
    {
        LandingPanel.Visibility = Visibility.Visible;
        ChatScroller.Visibility = Visibility.Collapsed;
    }

    private void AddLine(string speaker, string message, IReadOnlyList<string>? attachments = null, FrameworkElement? media = null, IReadOnlySet<string>? videoUrls = null, IReadOnlyDictionary<string, string>? videoTitles = null, IReadOnlyList<string>? searchFallback = null)
    {
        LandingPanel.Visibility = Visibility.Collapsed;
        ChatScroller.Visibility = Visibility.Visible;
        bool user = speaker == "You";
        
        var body = new TextBox { Text = message, IsReadOnly = true, IsReadOnlyCaretVisible = true,
            TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontSize = 14, Padding = new Thickness(0), AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Style = null };
        body.SetResourceReference(ForegroundProperty, "NIRATextBrush");
        if (user)
        {
            var group = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(70,0,0,20), MaxWidth = 760 };
            var title = new TextBlock { Text = "YOU", FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,0,9,5) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush");
            group.Children.Add(title);
            var bubble = new Border { Padding = new Thickness(17,12,17,12), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17,17,5,17), Child = body };
            bubble.SetResourceReference(Border.BackgroundProperty, "NIRAUserBubbleFillBrush");
            bubble.SetResourceReference(Border.BorderBrushProperty, "NIRAUserBubbleEdgeBrush");
            if (attachments is { Count: > 0 })
            {
                var cards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
                foreach (string path in attachments) cards.Children.Add(AttachmentCard(path, false));
                group.Children.Add(cards);
            }
            if (!string.IsNullOrWhiteSpace(message)) group.Children.Add(bubble);
            ConversationPanel.Children.Add(group);
        }
        else
        {
            var grid = new Grid { Margin = new Thickness(0,0,0,25) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var mark = new Border { Width = 27, Height = 27, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top, BorderThickness = new Thickness(1) };
            mark.SetResourceReference(Border.BackgroundProperty, "NIRAComposerBadgeBrush");
            mark.SetResourceReference(Border.BorderBrushProperty, "NIRAComposerEdgeBrush");
            mark.Child = BrandImage.Source is not null && speaker != "System" ? new Image { Source = BrandImage.Source, Width = 23, Height = 23 } : new TextBlock { Text = speaker == "System" ? "!" : "N", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            grid.Children.Add(mark);
            var content = new StackPanel { Margin = new Thickness(0,1,18,2) };
            var title = new TextBlock { Text = speaker, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,5) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush");
            content.Children.Add(title);
            if (speaker == "NIRA")
                content.Children.Add(NIRARichAnswer.Create(message, media, choice =>
                {
                    if (!_busy) { PromptBox.Text = choice; PromptBox.Focus(); PromptBox.CaretIndex = choice.Length; }
                }, videoUrls, videoTitles, searchFallback));
            else content.Children.Add(body);
            Grid.SetColumn(content,1);grid.Children.Add(content);ConversationPanel.Children.Add(grid);
        }
        if (ConversationPanel.Children.Count > 0 && (_busy || user))
            AnimateReveal((FrameworkElement)ConversationPanel.Children[ConversationPanel.Children.Count - 1], 0, 8);
        ScrollChatToEnd();
    }

    private void ScrollChatToEnd() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
        new Action(ChatScroller.ScrollToEnd));

    private void HandleProgress(AgentProgress progress)
    {
        // Real provider deltas are provisional. Never display raw protocol or partial markup.
        if (progress.Kind == "preview-reset") { ResetLivePreview(); return; }
        if (progress.Kind == "preview-delta")
        {
            if (_journeyPanel is null || !_livePreview.Append(progress.Summary)) return;
            if (!_previewRenderQueued)
            {
                _previewRenderQueued = true;
                int epoch = _previewEpoch;
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    if (epoch != _previewEpoch) return;
                    _previewRenderQueued = false;
                    if (_journeyPanel is null) return;
                    bool follow = ChatScroller.ScrollableHeight - ChatScroller.VerticalOffset < 80;
                    string text = _livePreview.Text;
                    if (text.Length == 0)
                    {
                        if (_journeyPreview is not null) _journeyPanel.Children.Remove(_journeyPreview);
                        _journeyPreview = null;
                        return;
                    }
                    if (_journeyPreview is null)
                    {
                        _journeyPreview = (RichTextBox)NIRAResponseDocument.Create(text);
                        _journeyPanel.Children.Insert(0, _journeyPreview);
#if DEBUG
                        if (_debugActivity.TryGetValue(_conversationId, out var previewEvents) && previewEvents.Count < 1000)
                            previewEvents.Add(new(DateTimeOffset.UtcNow, "preview-visible", "Live response preview mounted", null));
#endif
                    }
                    else NIRAResponseDocument.Update(_journeyPreview, text);
                    if (_journeyDots is not null) _journeyDots.Visibility = Visibility.Collapsed;
                    if (_journeyStatus is not null) _journeyStatus.Text = "Writing response…";
                    if (follow) ScrollChatToEnd();
                }));
            }
            return;
        }
        if (progress.Kind is "model-context" or "model-usage")
        {
#if DEBUG
            if (!_debugActivity.TryGetValue(_conversationId, out var metricsEvents))
            {
                if (_debugActivity.Count >= 100) _debugActivity.Remove(_debugActivity.Keys.First());
                _debugActivity[_conversationId] = metricsEvents = [];
            }
            if (metricsEvents.Count < 1000) metricsEvents.Add(new(DateTimeOffset.UtcNow, progress.Kind, progress.Summary, progress.ToolId));
#endif
            return;
        }
        if (progress.Kind is "permission" or "tool" or "reasoning" or "routing" or "paused" or "error" or "failed") ResetLivePreview();
        _presence.SetIntent(progress.Kind switch
        {
            "permission" => NIRAVisualIntent.RestingOrb with { Energy = .42, Tension = .55, Pulse = .5, Presence = .85 },
            "tool" => NIRAVisualIntent.RestingOrb with { Energy = .8, Flow = .8, Focus = .65, Presence = .85 },
            "reasoning" => NIRAVisualIntent.RestingOrb with { Energy = .62, Focus = .7, Pulse = .55, Presence = .8 },
            _ => NIRAVisualIntent.RestingOrb with { Energy = .38, Flow = .4 }
        });
#if DEBUG
        if (!_debugActivity.TryGetValue(_conversationId, out var events))
        {
            if (_debugActivity.Count >= 100) _debugActivity.Remove(_debugActivity.Keys.First());
            _debugActivity[_conversationId] = events = [];
        }
        if (events.Count < 1000) events.Add(new(DateTimeOffset.UtcNow, progress.Kind, progress.Summary, progress.ToolId));
#endif
        UpdateJourney(progress);
        AddProgress(progress);
    }

    private void BeginJourney()
    {
        _journeyPanel = new StackPanel { Margin = new Thickness(40,4,18,18) };
        _journeyDots = new NIRATypingIndicator { Width = 42, Height = 18, HorizontalAlignment = HorizontalAlignment.Left,
            IndicatorStyle = TypingStyle.FinalDepthDrift, SpeedMultiplier = .85 };
        _journeyStatus = new TextBlock { Text = "Preparing response", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,5) };
        _journeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "NIRASecondaryBrush");
        _journeyPanel.Children.Add(_journeyDots);
        _journeyPanel.Children.Add(_journeyStatus);
        LandingPanel.Visibility = Visibility.Collapsed;
        ChatScroller.Visibility = Visibility.Visible;
        ConversationPanel.Children.Add(_journeyPanel);
        ScrollChatToEnd();
    }

    // Human-readable host activity, not private model reasoning. No extra LLM calls.
    private void UpdateJourney(AgentProgress progress)
    {
        if (_journeyStatus is null || _journeyPanel is null) return;
        if (progress.Kind == "tool" && progress.ToolId == "image.generate" && Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION") != "1")
        {
            _artworkIndicator ??= new NIRAImageCreationPlaceholder();
            if (!_journeyPanel.Children.Contains(_artworkIndicator)) _journeyPanel.Children.Insert(0, _artworkIndicator);
            if (_journeyDots is not null) _journeyDots.Visibility = Visibility.Collapsed;
            _journeyStatus.Text = "Creating your artwork";
            return;
        }
        if (_artworkIndicator is not null)
        {
            _journeyPanel.Children.Remove(_artworkIndicator);
            _artworkIndicator = null;
            if (_journeyDots is not null) _journeyDots.Visibility = Visibility.Visible;
        }
        string? label = NIRAPublicDelivery.Activity(progress);
        if (label is null) return;
        _journeyStatus.Text = label;
        if (_journeyDots is not null) _journeyDots.Visibility = progress.Kind == "permission" ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ResetLivePreview()
    {
        _previewEpoch++;
        _previewRenderQueued = false;
        _livePreview.Reset();
        if (_journeyDots is not null) _journeyDots.Visibility = Visibility.Visible;
        if (_journeyPreview is not null) _journeyPanel?.Children.Remove(_journeyPreview);
        _journeyPreview = null;
        if (_journeyStatus is not null) _journeyStatus.Text = "Preparing response";
    }

    private void EndJourney()
    {
        ResetLivePreview();
        if (_journeyPanel is not null) ConversationPanel.Children.Remove(_journeyPanel);
        _journeyPanel = null; _journeyDots = null; _journeyStatus = null; _artworkIndicator = null;
    }

    private static void AnimateReveal(FrameworkElement panel, double x, double y)
    {
        if (Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION") == "1") return;
        var transform = new TranslateTransform(); panel.RenderTransform = transform;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, 0, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, 0, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void QuickPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy && sender is Button { Tag: string prompt })
        {
            OpenChat_Click(sender, e);
            PromptBox.Text = prompt; PromptBox.Focus(); PromptBox.CaretIndex = PromptBox.Text.Length;
        }
    }

    private enum WorkspacePage { Chat, Memory, Gallery }

    private void ShowWorkspace(WorkspacePage page)
    {
        if (page != WorkspacePage.Chat) _miniStop?.Invoke();
        MainWorkspace.Visibility = page == WorkspacePage.Chat ? Visibility.Visible : Visibility.Collapsed;
        MemoryPage.Visibility = page == WorkspacePage.Memory ? Visibility.Visible : Visibility.Collapsed;
        GalleryPage.Visibility = page == WorkspacePage.Gallery ? Visibility.Visible : Visibility.Collapsed;
        ChatNavButton.Tag = page == WorkspacePage.Chat ? "ACTIVE" : null;
        MemoryNavButton.Tag = page == WorkspacePage.Memory ? "ACTIVE" : null;
        GalleryNavButton.Tag = page == WorkspacePage.Gallery ? "ACTIVE" : null;
    }

    private void OpenChat_Click(object sender, RoutedEventArgs e)
    {
        ShowWorkspace(WorkspacePage.Chat);
        MainWorkspace.UpdateLayout();
        FitWorkspaceLayout();
        PromptBox.Focus();
    }

    private async void OpenGallery_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _runtime.Conversations is not SqliteConversationStore store) return;
        ShowWorkspace(WorkspacePage.Gallery);
        await GalleryPage.InitializeAsync(store, _runtime.Workspace, _runtime.DatabasePath);
    }

    private async void MemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _runtime.Conversations is not SqliteConversationStore store) return;
        ShowWorkspace(WorkspacePage.Memory);
        await MemoryPage.InitializeAsync(store);
    }
    // Restore the original utility-window scrim; only the owner is darkened.
    private void SetUtilityWindowScrim(bool visible)
    {
        double target = visible ? 0.91 : 0.0;
        double current = UtilityWindowScrim.Opacity;
        UtilityWindowScrim.BeginAnimation(OpacityProperty, null);
        UtilityWindowScrim.Opacity = target;
        if (Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION") == "1") return;
        UtilityWindowScrim.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = current, To = target,
            Duration = TimeSpan.FromMilliseconds(visible ? 320 : 255),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private bool? ShowDimmedDialog(Window dialog)
    {
        void Refresh() => SetUtilityWindowScrim(dialog.IsVisible && dialog.WindowState != WindowState.Minimized);
        RoutedEventHandler loaded = (_, _) => Refresh();
        EventHandler stateChanged = (_, _) => Refresh();
        dialog.Loaded += loaded;
        dialog.StateChanged += stateChanged;
        try { return dialog.ShowDialog(); }
        finally
        {
            dialog.Loaded -= loaded;
            dialog.StateChanged -= stateChanged;
            SetUtilityWindowScrim(false);
        }
    }

    private async void OpenControlCenter(string page)
    {
        if (_busy) return;
        try
        {
            var settings = new SettingsWindow(_runtime, page) { Owner = this };
            ShowDimmedDialog(settings);
            UpdateTheme();
            await RefreshHistoryAsync();
        }
        catch (Exception error) { Debug.WriteLine(error); StatusLabel.Text = "Unable to open settings."; }
    }
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenControlCenter("Appearance");
    private void PermissionsButton_Click(object sender, RoutedEventArgs e) => OpenControlCenter("Permissions");
    private void AttachFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Multiselect = true, CheckFileExists = true,
            Title = "Attach local files by reference (normal tool approvals still apply)",
            Filter = "Documents and images|*.xlsx;*.docx;*.pdf;*.txt;*.csv;*.md;*.png;*.jpg;*.jpeg;*.bmp|All files|*.*" };
        if (picker.ShowDialog(this) != true) return;
        foreach (string path in picker.FileNames)
        {
            if (_selectedAttachments.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            if (_selectedAttachments.Count >= ChatAttachmentMessage.MaximumFiles)
            { StatusLabel.Text = "Maximum eight attachments per message."; break; }
            try
            {
                string resolved = new WorkspacePathResolver(_runtime.Workspace).Resolve(path);
                _ = ChatAttachmentMessage.Create("", new[] { resolved });
                if (File.Exists(resolved)) _selectedAttachments.Add(resolved);
            }
            catch (Exception error) { Debug.WriteLine(error); StatusLabel.Text = "The attachment could not be selected."; }
        }
        RenderAttachments(); PromptBox.Focus();
    }

    private void ClearAttachments()
    {
        _selectedAttachments.Clear(); RenderAttachments();
    }

    private void RenderAttachments()
    {
        AttachmentPanel.Children.Clear();
        foreach (string path in _selectedAttachments) AttachmentPanel.Children.Add(AttachmentCard(path, true));
        AttachmentScroller.Visibility = _selectedAttachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Border FileFrame(UIElement child)
    {
        var frame = new Border { Child = child, Padding = new Thickness(9,6,9,6), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), Margin = new Thickness(0,3,7,3) };
        frame.SetResourceReference(Border.BackgroundProperty, "NIRAGlassStrongTintBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "NIRAGlassBorderBrush");
        return frame;
    }

    private static TextBlock FileLabel(string text, bool strong = false)
    {
        var label = new TextBlock { Text = text, FontSize = strong ? 13 : 11, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, strong ? "NIRATextBrush" : "NIRASecondaryBrush");
        return label;
    }

    private Button FileButton(string title)
    {
        var button = new Button { Content = title, MinHeight = 26, Padding = new Thickness(8,3,8,3), Margin = new Thickness(5,0,0,0), Cursor = Cursors.Hand };
        button.SetResourceReference(Control.ForegroundProperty, "NIRATextBrush");
        button.SetResourceReference(Control.BackgroundProperty, "NIRAChipBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "NIRAGlassBorderBrush");
        return button;
    }

    private Border AttachmentCard(string path, bool removable)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var details = new StackPanel { MaxWidth = 215, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(FileLabel(Path.GetFileName(path), true));
        details.Children.Add(FileLabel(Path.GetExtension(path).TrimStart('.').ToUpperInvariant() + " · Local reference"));
        row.Children.Add(details);
        if (removable)
        {
            var remove = FileButton("×"); remove.ToolTip = "Remove attachment from this message";
            remove.Click += (_, _) => { if (!_busy) { _selectedAttachments.Remove(path); RenderAttachments(); } };
            row.Children.Add(remove);
        }
        var frame = FileFrame(row); frame.ToolTip = path; return frame;
    }

    private void AddArtifactCards(ConversationState state)
    {
        var artifacts = RecordedFileArtifacts.From(state);
        if (artifacts.Count == 0) return;
        var panel = new StackPanel { Margin = new Thickness(34,0,18,18) };
        panel.Children.Add(FileLabel("YOUR FILES", true));
        foreach (var artifact in artifacts)
        {
            var row = new DockPanel { LastChildFill = true };
            var open = FileButton("Open file"); DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
            var copy = FileButton("Copy path"); DockPanel.SetDock(copy, Dock.Right); row.Children.Add(copy);
            copy.Click += (_, _) => { try { Clipboard.SetText(artifact.Path); } catch (Exception error) { Debug.WriteLine(error); StatusLabel.Text = "Unable to copy the file path."; } };
            open.Click += async (_, _) =>
            {
                open.IsEnabled = false;
                try
                {
                    if (!await Task.Run(() => RecordedFileArtifacts.MatchesAsync(artifact, _runtime.Workspace)))
                    { MessageBox.Show(this, "File missing or changed since its recorded receipt. No file opened.", "NIRA file verification"); return; }
                    Process.Start(new ProcessStartInfo(artifact.Path) { UseShellExecute = true });
                }
                catch (Exception error) { MessageBox.Show(this, error.Message, "NIRA file verification"); }
                finally { open.IsEnabled = true; }
            };
            var labels = new StackPanel(); labels.Children.Add(FileLabel(Path.GetFileName(artifact.Path), true));
            labels.Children.Add(FileLabel("Checked before opening")); row.Children.Add(labels);
            var frame = FileFrame(row); frame.ToolTip = artifact.Path; panel.Children.Add(frame);
        }
        ConversationPanel.Children.Add(panel); ScrollChatToEnd();
    }
    private void ThemeModeButton_Click(object sender, RoutedEventArgs e)
    {
        NIRAThemeManager.ApplyTheme(NIRAThemeManager.CurrentMode == NIRAThemeMode.Eclipse ? NIRAThemeMode.Halo : NIRAThemeMode.Eclipse);
        UpdateTheme();
    }
    private void UpdateTheme()
    {
        bool halo = NIRAThemeManager.CurrentMode == NIRAThemeMode.Halo;
        ThemeModeText.Text = halo ? "HALO" : "ECLIPSE";
        SetThemeOpacity(BackgroundHalo, halo ? 1 : 0);
        SetThemeOpacity(BackgroundEclipse, halo ? 0 : 1);
    }
    private static void SetThemeOpacity(FrameworkElement element, double target)
    {
        double start = element.Opacity;
        element.BeginAnimation(OpacityProperty, null); element.Opacity = target;
        if (SystemParameters.ClientAreaAnimation)
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(300)) { FillBehavior = FillBehavior.Stop });
    }
    private void ToggleLeftSidebar_Click(object sender, RoutedEventArgs e) => SetLeftCollapsed(!_leftCollapsed);
    private async void SetLeftCollapsed(bool collapsed)
    {
        if (_leftCollapsed == collapsed || _leftTransitioning) return;
        if (collapsed) ReturnOrStopMiniPlayerBeforeSidebarCollapse();
        _leftTransitioning = true;
        try
        {
            await TransitionSidebarAsync(LeftSidebarColumn,
                collapsed ? LeftSidebarExpandedHost : LeftSidebarCompactHost,
                collapsed ? LeftSidebarCompactHost : LeftSidebarExpandedHost,
                collapsed ? 52 : 276, -10);
            _leftCollapsed = collapsed;
        }
        finally { _leftTransitioning = false; FitWorkspaceLayout(); }
    }

    private async void ToggleRightSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (_rightTransitioning) return;
        _rightTransitioning = true;
        bool collapsed = !_rightCollapsed;
        try
        {
            await TransitionSidebarAsync(RightSidebarColumn,
                collapsed ? RightSidebarScroll : RightSidebarCompactHost,
                collapsed ? RightSidebarCompactHost : RightSidebarScroll,
                collapsed ? 52 : 292, 10);
            _rightCollapsed = collapsed;
        }
        finally { _rightTransitioning = false; FitWorkspaceLayout(); }
    }

    // Original NIRA sequence: 110ms fade, 230ms cubic width, 270ms slide/reveal.
    // Hide both control trees during width interpolation: no squashed/reflowing controls.
    private static async Task TransitionSidebarAsync(ColumnDefinition column,
        FrameworkElement leaving, FrameworkElement arriving, double target, double fromX)
    {
        bool motion = SystemParameters.ClientAreaAnimation &&
            Environment.GetEnvironmentVariable("NIRA_REDUCE_MOTION") != "1";
        leaving.IsHitTestVisible = false;
        arriving.IsHitTestVisible = false;
        try
        {
            if (motion)
            {
                leaving.BeginAnimation(OpacityProperty, new DoubleAnimation(0.30, TimeSpan.FromMilliseconds(110))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.HoldEnd });
                await Task.Delay(115);
            }
            leaving.Visibility = Visibility.Collapsed;
            leaving.BeginAnimation(OpacityProperty, null);
            arriving.Visibility = Visibility.Collapsed;
            if (motion) await AnimateWidthAsync(column, target);
            else column.Width = new GridLength(target);
            arriving.Visibility = Visibility.Visible;
            if (motion)
            {
                arriving.BeginAnimation(OpacityProperty, new DoubleAnimation(0.34, 1.0, TimeSpan.FromMilliseconds(270))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                var translate = new TranslateTransform();
                arriving.RenderTransform = translate;
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, TimeSpan.FromMilliseconds(270))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
                await Task.Delay(275);
            }
        }
        finally
        {
            leaving.Visibility = Visibility.Collapsed;
            leaving.BeginAnimation(OpacityProperty, null);
            leaving.Opacity = 1;
            leaving.IsHitTestVisible = true;
            arriving.Visibility = Visibility.Visible;
            arriving.BeginAnimation(OpacityProperty, null);
            arriving.Opacity = 1;
            arriving.RenderTransform = Transform.Identity;
            arriving.IsHitTestVisible = true;
            column.Width = new GridLength(target);
        }
    }

    private static Task AnimateWidthAsync(ColumnDefinition column, double target)
    {
        double start = column.ActualWidth;
        if (Math.Abs(start - target) < .5)
        { column.Width = new GridLength(target); return Task.CompletedTask; }
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        EventHandler? tick = null;
        tick = (_, _) => {
            double progress = Math.Clamp(watch.Elapsed.TotalMilliseconds / 230.0, 0.0, 1.0);
            column.Width = new GridLength(start + (target - start) * (1 - Math.Pow(1 - progress, 3)));
            if (progress >= 1) { timer.Stop(); timer.Tick -= tick; column.Width = new GridLength(target); done.TrySetResult(true); }
        };
        timer.Tick += tick; timer.Start(); return done.Task;
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) { MaximizeButton_Click(sender,e); return; }
        if (WindowState == WindowState.Normal) DragMove();
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void ExitAgentButton_Click(object sender, RoutedEventArgs e) => Close();

    // Exactly one pending host request. The model cannot click these buttons or
    // supply their decision through a chat reply. Cancellation revokes the prompt.
    private async Task<bool> RequestApprovalOnUiAsync(ApprovalRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_approvalDecision is not null) throw new InvalidOperationException("An approval is already pending.");
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approvalDecision = decision;
        using var registration = ct.Register(() => decision.TrySetCanceled(ct));
        try
        {
            ApprovalDetails.Text = NIRAPublicDelivery.Approval(request);
            ApproveButton.IsEnabled = true;
            DenyButton.IsEnabled = true;
            ApprovalPanel.Visibility = Visibility.Visible;
            PermissionStateText.Text = "APPROVAL REQUIRED";
            RunStateLabel.Text = "AWAITING APPROVAL";
            StateDot.Fill = BrushFor("#FBBF24");
            bool approved = await decision.Task;
            ct.ThrowIfCancellationRequested();
            return approved;
        }
        finally
        {
            _approvalDecision = null;
            ApprovalPanel.Visibility = Visibility.Collapsed;
            ApprovalDetails.Clear();
            PermissionStateText.Text = "NO ACTIONS WAITING";
            RunStateLabel.Text = "RUNNING";
            StateDot.Fill = BrushFor("#00E5FF");
        }
    }

    private void Approve_Click(object sender, RoutedEventArgs e) => DecideApproval(true);
    private void Deny_Click(object sender, RoutedEventArgs e) => DecideApproval(false);
    private void DecideApproval(bool decision)
    {
        if (_approvalDecision?.TrySetResult(decision) == true)
        {
            ApproveButton.IsEnabled = false;
            DenyButton.IsEnabled = false;
        }
    }
}

internal sealed class WpfApprovalBroker(Window owner,
    Func<ApprovalRequest, CancellationToken, Task<bool>> requestOnUi) : IApprovalBroker
{
    public async Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Task<bool> pending = await owner.Dispatcher.InvokeAsync<Task<bool>>(() => requestOnUi(request, ct));
        return await pending;
    }
}

internal sealed class DesktopStreamingProgress(Action<AgentProgress> callback) : IStreamingProgress
{
    private readonly Progress<AgentProgress> _progress = new(callback);
    public void Report(AgentProgress value) => ((IProgress<AgentProgress>)_progress).Report(value);
}

/*
 * filename: MainWindow.xaml.cs
 */

using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

using NIRAAgent.Agent.State;
using NIRAAgent.Artifacts;
using NIRAAgent.Authorization;
using NIRAAgent.Branches;
using NIRAAgent.Self.Model;
using NIRAAgent.Settings;
using NIRAAgent.Temporal;
using NIRAAgent.Vision;
using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Visuals;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI;

public partial class MainWindow : Window
{
    // =========================================================
    // SERVICES
    // =========================================================

    private NIRAStateService?
        _NIRAState;


    private NIRABranchService?
        _branchService;


    private NIRABranchWorkService?
        _branchWorkService;


    private NIRASelfModelService?
        _selfModelService;


    private NIRAVisualEvidenceService?
        _visualEvidenceService;


    private bool
        _interfaceEntrancePlayed;


    private bool
        _ambientAnimationsStarted;


    private NIRAMindState?
        _lastAnimatedMindState;


    // Stable visual instances keep live branch/commitment updates smooth.
    // We no longer clear and rebuild every card on each state event.
    private readonly Dictionary<Guid, Border>
        _branchCards = new();

    private readonly Dictionary<Guid, string>
        _branchCardSignatures = new();

    private readonly HashSet<Guid>
        _branchCardsLeaving = new();

    private readonly Dictionary<Guid, Border>
        _commitmentCards = new();

    private readonly Dictionary<Guid, string>
        _commitmentCardSignatures = new();

    private readonly HashSet<Guid>
        _commitmentCardsLeaving = new();


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    private NIRAAuthorityStore? _authorityStore;
    private NIRAScopedCapabilityAuthorizer? _authorizer;
    private NIRACapabilityApprovalBroker? _approvalBroker;
    private PermissionsWindow? _permissionsWindow;
    private PermissionToastWindow? _permissionToast;
    private NIRARuntimeSettingsService? _settingsService;
    private SettingsWindow? _settingsWindow;
    private bool _authorizationClosed;
    private readonly NIRAWorkAreaWindowGuard _workAreaGuard;
    private readonly NIRAVisualToastPlacementService _visualToastPlacement;
    private readonly MainWindowViewModel _viewModel;
    private NIRAVisualArtifactToastWindow? _visualArtifactToast;

    public void AttachAuthorization(NIRAAuthorityStore store,
        NIRAScopedCapabilityAuthorizer authorizer, NIRACapabilityApprovalBroker broker)
    {
        if (_approvalBroker != null)
        {
            _approvalBroker.Changed -= Authorization_Changed;
        }

        if (_permissionToast != null)
        {
            _permissionToast.OpenFullPermissionsRequested -=
                PermissionToast_OpenFullPermissionsRequested;

            _permissionToast.Close();
            _permissionToast = null;
        }

        _authorityStore = store;
        _authorizer = authorizer;
        _approvalBroker = broker;

        _permissionToast =
            new PermissionToastWindow(
                authorizer,
                broker);

        _permissionToast.OpenFullPermissionsRequested +=
            PermissionToast_OpenFullPermissionsRequested;

        _approvalBroker.Changed += Authorization_Changed;
        Authorization_Changed();
    }

    public void AttachSettings(
        NIRARuntimeSettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);

        if (_settingsService != null)
        {
            _settingsService.Changed -= RuntimeSettings_Changed;
        }

        _settingsService = settingsService;
        _settingsService.Changed += RuntimeSettings_Changed;

        ApplyRuntimeSettings(settingsService.Current);
    }

    private void RuntimeSettings_Changed(
        NIRARuntimeSettings before,
        NIRARuntimeSettings after)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(() => ApplyRuntimeSettings(after)));
            return;
        }

        ApplyRuntimeSettings(after);
    }

    private void ApplyRuntimeSettings(
        NIRARuntimeSettings settings)
    {
        NIRAUiParticleTransition.Enabled =
            settings.WorkCardParticleTransitionsEnabled;

        BranchActivitySection.Visibility =
            settings.ShowBranchActivityInSidebar
                ? Visibility.Visible
                : Visibility.Collapsed;

        CommitmentActivitySection.Visibility =
            settings.ShowCommitmentActivityInSidebar
                ? Visibility.Visible
                : Visibility.Collapsed;

        ActivitySectionDivider.Visibility =
            settings.ShowBranchActivityInSidebar
            && settings.ShowCommitmentActivityInSidebar
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (!settings.PermissionNotificationsEnabled)
        {
            _permissionToast?.HideWhenIdle();
            return;
        }

        if (IsLoaded && (_approvalBroker?.PendingRequests.Count ?? 0) > 0)
        {
            _permissionToast?.ShowPending();
        }
    }

    private void Authorization_Changed()
    {
        if (_authorizationClosed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_authorizationClosed)
            {
                return;
            }

            int count =
                _approvalBroker?.PendingRequests.Count ?? 0;

            PermissionsButton.Content =
                count > 0
                    ? $"Permissions ({count})"
                    : "Permissions";

            PermissionStateText.Text = count switch
            {
                0 => "NO ACTIONS WAITING",
                1 => "1 ACTION NEEDS REVIEW",
                _ => $"{count} ACTIONS NEED REVIEW"
            };

            UpdateAccessVisual(count);

            // Permission requests are surfaced as a small, non-activating
            // desktop toast. The full Permissions window is now opened only
            // when the user explicitly asks for it. This keeps authorization
            // visible without stealing focus from the user's current work.
            if (
                count > 0
                &&
                IsLoaded
                &&
                (_settingsService?.Current.PermissionNotificationsEnabled ?? true))
            {
                _permissionToast?.ShowPending();
            }
            else
            {
                _permissionToast?.HideWhenIdle();
            }
        }));
    }

    private void PermissionToast_OpenFullPermissionsRequested() =>
        OpenPermissions(showRequests: true);

    private void PermissionsButton_Click(
        object sender,
        RoutedEventArgs e) =>
        OpenPermissions(showRequests: false);

    private void SettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_settingsService == null)
        {
            return;
        }

        // Settings and Permissions share one secondary-window slot.
        // Switching to Settings closes Permissions without resolving
        // pending authorization requests as denied.
        if (_permissionsWindow != null)
        {
            _permissionsWindow.CloseForWindowSwitch();
        }

        if (_settingsWindow == null)
        {
            _settingsWindow =
                new SettingsWindow(_settingsService)
                {
                    Owner = this
                };

            _settingsWindow.Closed += (_, _) =>
                _settingsWindow = null;

            _settingsWindow.Show();
            return;
        }

        if (!_settingsWindow.IsVisible)
        {
            _settingsWindow.Show();
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private void OpenPermissions(bool showRequests)
    {
        if (_authorityStore == null ||
            _authorizer == null ||
            _approvalBroker == null)
        {
            return;
        }

        // Only one secondary NIRA window is allowed at a time.
        // Permissions takes over the slot from Settings.
        if (_settingsWindow != null)
        {
            _settingsWindow.Close();
        }

        _permissionToast?.SetSuspended(true);

        if (_permissionsWindow == null)
        {
            _permissionsWindow =
                new PermissionsWindow(
                    _authorityStore,
                    _authorizer,
                    _approvalBroker)
                {
                    Owner = this
                };

            _permissionsWindow.MarkManualSession();

            _permissionsWindow.Closed += (_, _) =>
            {
                _permissionsWindow = null;
                _permissionToast?.SetSuspended(false);

                if (_settingsService?.Current.PermissionNotificationsEnabled ?? true)
                {
                    _permissionToast?.ShowPending();
                }
            };

            _permissionsWindow.Show();
        }
        else if (!_permissionsWindow.IsVisible)
        {
            _permissionsWindow.Show();
        }

        _permissionsWindow.MarkManualSession();

        if (showRequests)
        {
            _permissionsWindow.ShowRequests(
                autoCloseAfterDecision: false);
        }
        else
        {
            _permissionsWindow.Activate();
        }
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        NIRAVisualToastPlacementService visualToastPlacement)
    {
        InitializeComponent();

        _viewModel =
            viewModel
            ?? throw new ArgumentNullException(
                nameof(viewModel));

        _visualToastPlacement =
            visualToastPlacement
            ?? throw new ArgumentNullException(
                nameof(visualToastPlacement));

        DataContext =
            _viewModel;

        _viewModel.Messages.CollectionChanged +=
            Messages_CollectionChanged;

        _viewModel.VisualArtifactReceived +=
            ViewModel_VisualArtifactReceived;

        foreach (
            ChatMessageViewModel message
            in _viewModel.Messages)
        {
            message.PropertyChanged +=
                Message_PropertyChanged;
        }

        Loaded +=
            MainWindow_Loaded;

        _workAreaGuard =
            new NIRAWorkAreaWindowGuard(this);

        Closed +=
            MainWindow_Closed;

        NIRAThemeManager.ThemeChanged +=
            NIRAThemeManager_ThemeChanged;

        UpdateThemeModeButtons();
        ApplyThemeBackdrop(animate: false);
        UpdateGreeting();
    }

    private void NIRAThemeManager_ThemeChanged(
        NIRAThemeMode mode)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateThemeModeButtons();
                ApplyThemeBackdrop(animate: true);
                RefreshThemeSensitiveActivityCards();
            }));
            return;
        }

        UpdateThemeModeButtons();
        ApplyThemeBackdrop(animate: true);
        RefreshThemeSensitiveActivityCards();
    }

    private void UpdateThemeModeButtons()
    {
        bool isHalo =
            NIRAThemeManager.CurrentMode ==
            NIRAThemeMode.Halo;

        if (ThemeModeButton == null ||
            ThemeModeText == null ||
            ThemeModeIcon == null ||
            ThemeModeOrb == null)
        {
            return;
        }

        ThemeModeText.Text =
            isHalo ? "HALO" : "ECLIPSE";

        ThemeModeIcon.Text =
            isHalo ? "☼" : "◐";

        ThemeModeButton.Background =
            (Brush)Application.Current.Resources["NIRAChipBrush"];

        ThemeModeButton.BorderBrush =
            (Brush)Application.Current.Resources["NIRAGlassBorderBrush"];

        ThemeModeOrb.Background =
            isHalo
                ? (Brush)Application.Current.Resources["NIRASoftGlow"]
                : (Brush)Application.Current.Resources["NIRAVioletGlow"];

        ThemeModeOrb.BorderBrush =
            isHalo
                ? (Brush)Application.Current.Resources["NIRACyanBrush"]
                : (Brush)Application.Current.Resources["NIRAVioletBrush"];
    }

    private void RefreshThemeSensitiveActivityCards()
    {
        _branchCardSignatures.Clear();
        _commitmentCardSignatures.Clear();
        RefreshBranchActivity();
        RefreshCommitmentActivity();
    }

    private void ApplyThemeBackdrop(
        bool animate)
    {
        bool halo =
            NIRAThemeManager.CurrentMode ==
            NIRAThemeMode.Halo;

        double eclipseTarget = halo ? 0.0 : 1.0;
        double haloTarget = halo ? 1.0 : 0.0;

        if (!animate)
        {
            BackgroundEclipse.Opacity = eclipseTarget;
            BackgroundHalo.Opacity = haloTarget;
            return;
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(220);
        CubicEase easing = new()
        {
            EasingMode = EasingMode.EaseOut
        };

        BackgroundEclipse.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(BackgroundEclipse.Opacity, eclipseTarget, duration)
            {
                EasingFunction = easing
            });

        BackgroundHalo.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(BackgroundHalo.Opacity, haloTarget, duration)
            {
                EasingFunction = easing
            });
    }

    private void UpdateGreeting()
    {
        int hour = DateTime.Now.Hour;

        GreetingText.Text = hour switch
        {
            < 12 => "Good morning.",
            < 18 => "Good afternoon.",
            _ => "Good evening."
        };
    }

    private void QuickPrompt_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not string prompt ||
            string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        _viewModel.MessageInput = prompt;
        MessageInput.Focus();
        MessageInput.CaretIndex = MessageInput.Text.Length;
    }

    private void ThemeModeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        NIRAThemeMode next =
            NIRAThemeManager.CurrentMode == NIRAThemeMode.Eclipse
                ? NIRAThemeMode.Halo
                : NIRAThemeMode.Eclipse;

        NIRAThemeManager.ApplyTheme(next);
    }


    // =========================================================
    // ATTACH LIVE NIRA STATE
    // =========================================================

    public void AttachNIRAState(
        NIRAStateService NIRAState)
    {
        ArgumentNullException.ThrowIfNull(
            NIRAState);

        if (ReferenceEquals(
                _NIRAState,
                NIRAState))
        {
            ApplyNIRAState(
                NIRAState.Current);

            return;
        }

        if (_NIRAState !=
            null)
        {
            _NIRAState.StateChanged -=
                NIRAState_StateChanged;
        }

        _NIRAState =
            NIRAState;

        _NIRAState.StateChanged +=
            NIRAState_StateChanged;

        ApplyNIRAState(
            _NIRAState.Current);
    }


    // =========================================================
    // ATTACH USER-VISIBLE VISION ACTIVITY
    //
    // This is an ephemeral read-only projection. CAPTURING means
    // pixels are being acquired; INSPECTING means the vision model
    // is examining an already-grounded capture. Neither state by
    // itself proves that perception succeeded.
    // =========================================================

    public void AttachVisionActivity(
        NIRAVisualEvidenceService visualEvidenceService)
    {
        ArgumentNullException.ThrowIfNull(
            visualEvidenceService);


        if (_visualEvidenceService != null)
        {
            _visualEvidenceService.ActivityChanged -=
                VisionActivity_Changed;
        }


        _visualEvidenceService =
            visualEvidenceService;


        _visualEvidenceService.ActivityChanged +=
            VisionActivity_Changed;


        ApplyVisionActivity(
            _visualEvidenceService.CurrentActivity);
    }


    private void VisionActivity_Changed(
        NIRAVisionActivitySnapshot snapshot)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }


        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(
                    () => ApplyVisionActivity(snapshot)));

            return;
        }


        ApplyVisionActivity(
            snapshot);
    }


    private void ApplyVisionActivity(
        NIRAVisionActivitySnapshot snapshot)
    {
        bool active =
            snapshot.Kind !=
            NIRAVisionActivityKind.Idle;


        string subject =
            !string.IsNullOrWhiteSpace(snapshot.ProcessName)
                ? snapshot.ProcessName.Trim().ToUpperInvariant()
                : snapshot.Target.HasValue
                    ? snapshot.Target.Value.ToString().ToUpperInvariant()
                    : string.Empty;


        VisionActivityText.Text =
            snapshot.Kind switch
            {
                NIRAVisionActivityKind.Capturing =>
                    string.IsNullOrWhiteSpace(subject)
                        ? "CAPTURING"
                        : $"CAPTURING {subject}",

                NIRAVisionActivityKind.Inspecting =>
                    string.IsNullOrWhiteSpace(subject)
                        ? "INSPECTING"
                        : $"LOOKING AT {subject}",

                _ =>
                    "IDLE"
            };


        Color accent =
            snapshot.Kind switch
            {
                NIRAVisionActivityKind.Capturing =>
                    Color.FromRgb(77, 143, 255),

                NIRAVisionActivityKind.Inspecting =>
                    Color.FromRgb(120, 219, 255),

                _ =>
                    Color.FromRgb(70, 106, 132)
            };


        VisionActivityDot.Fill =
            new SolidColorBrush(
                accent);


        VisionActivityText.Foreground =
            new SolidColorBrush(
                active
                    ? accent
                    : Color.FromRgb(110, 141, 167));


        AnimateStatusDot(
            VisionActivityDot,
            active,
            TimeSpan.FromMilliseconds(760));
    }


    // =========================================================
    // ATTACH USER-VISIBLE CONTINUITY STATE
    //
    // Commitments remain authoritative self-model state. The UI only
    // observes them and never mutates commitment lifecycle itself.
    // =========================================================

    public void AttachContinuityState(
        NIRASelfModelService selfModelService)
    {
        ArgumentNullException.ThrowIfNull(
            selfModelService);

        if (_selfModelService != null)
        {
            _selfModelService.StateChanged -=
                ContinuityState_Changed;
        }

        _selfModelService =
            selfModelService;

        _selfModelService.StateChanged +=
            ContinuityState_Changed;

        RefreshCommitmentActivity();
    }


    private void ContinuityState_Changed()
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(RefreshCommitmentActivity));

            return;
        }

        RefreshCommitmentActivity();
    }


    // =========================================================
    // ATTACH LIVE BRANCH / WORK ACTIVITY
    //
    // The UI is only a read-only projection. Branches remain passive
    // responsibility baskets; NIRA cognition still decides every next step.
    // =========================================================

    public void AttachBranchActivity(
        NIRABranchService branchService,
        NIRABranchWorkService branchWorkService)
    {
        ArgumentNullException.ThrowIfNull(
            branchService);

        ArgumentNullException.ThrowIfNull(
            branchWorkService);

        if (_branchService != null)
        {
            _branchService.StateChanged -=
                BranchActivity_Changed;
        }

        if (_branchWorkService != null)
        {
            _branchWorkService.StateChanged -=
                BranchActivity_Changed;
        }

        _branchService =
            branchService;

        _branchWorkService =
            branchWorkService;

        _branchService.StateChanged +=
            BranchActivity_Changed;

        _branchWorkService.StateChanged +=
            BranchActivity_Changed;

        RefreshBranchActivity();
    }


    private void BranchActivity_Changed()
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(RefreshBranchActivity));

            return;
        }

        RefreshBranchActivity();
    }


    private void RefreshBranchActivity()
    {
        if (_branchService == null ||
            _branchWorkService == null)
        {
            BeginRemovingAllBranchCards();
            NoBranchActivityText.Text =
                "No active branches";
            NoBranchActivityText.Visibility =
                Visibility.Visible;
            return;
        }

        NIRABranchWorkItem[] openWork =
            _branchWorkService.CurrentWork
                .Where(work => work.IsOpen)
                .ToArray();

        NIRABranchState[] openBranches =
            _branchService.CurrentBranches
                .Where(branch => branch.IsOpen)
                .OrderByDescending(
                    branch =>
                        openWork.Any(
                            work =>
                                work.BranchId == branch.Id &&
                                work.Status == NIRABranchWorkStatus.Running))
                .ThenByDescending(
                    branch => branch.Status == NIRABranchStatus.Active)
                .ThenByDescending(
                    branch => branch.Priority)
                .ThenByDescending(
                    branch => branch.UpdatedAt)
                .ToArray();

        HashSet<Guid> desiredIds =
            openBranches
                .Select(branch => branch.Id)
                .ToHashSet();

        foreach (Guid existingId in
                 _branchCards.Keys.ToArray())
        {
            if (!desiredIds.Contains(existingId))
            {
                BeginBranchCardRemoval(existingId);
            }
        }

        if (openBranches.Length == 0)
        {
            NoBranchActivityText.Text =
                "No active branches";
            NoBranchActivityText.Visibility =
                Visibility.Visible;
            return;
        }

        NoBranchActivityText.Visibility =
            Visibility.Collapsed;

        for (int index = 0;
             index < openBranches.Length;
             index++)
        {
            NIRABranchState branch =
                openBranches[index];

            NIRABranchWorkItem? work =
                openWork
                    .Where(item => item.BranchId == branch.Id)
                    .OrderByDescending(
                        item => item.Status == NIRABranchWorkStatus.Running)
                    .ThenByDescending(
                        item => item.StartedAtUtc ?? item.CreatedAtUtc)
                    .FirstOrDefault();

            string signature =
                BuildBranchCardSignature(
                    branch,
                    work);

            if (_branchCards.TryGetValue(
                    branch.Id,
                    out Border? existing))
            {
                if (!_branchCardSignatures.TryGetValue(
                        branch.Id,
                        out string? previousSignature) ||
                    !string.Equals(
                        previousSignature,
                        signature,
                        StringComparison.Ordinal))
                {
                    Border refreshed =
                        BuildBranchCard(
                            branch,
                            work,
                            index);

                    ApplyActivityCardTemplate(
                        existing,
                        refreshed);

                    _branchCardSignatures[branch.Id] =
                        signature;
                }

                MoveCardToIndex(
                    BranchCardsPanel,
                    existing,
                    index);

                continue;
            }

            Border card =
                BuildBranchCard(
                    branch,
                    work,
                    index);

            _branchCards[branch.Id] =
                card;

            _branchCardSignatures[branch.Id] =
                signature;

            int insertIndex =
                Math.Min(
                    index,
                    BranchCardsPanel.Children.Count);

            BranchCardsPanel.Children.Insert(
                insertIndex,
                card);

            NIRAUiParticleTransition.PlayMaterialize(
                card,
                index);
        }
    }


    private void ApplyNoBranchActivity()
    {
        BeginRemovingAllBranchCards();

        NoBranchActivityText.Text =
            "No active branches";

        NoBranchActivityText.Visibility =
            Visibility.Visible;
    }


    private void BeginRemovingAllBranchCards()
    {
        foreach (Guid id in
                 _branchCards.Keys.ToArray())
        {
            BeginBranchCardRemoval(id);
        }
    }


    private void BeginBranchCardRemoval(
        Guid branchId)
    {
        if (!_branchCards.TryGetValue(
                branchId,
                out Border? card) ||
            !_branchCardsLeaving.Add(branchId))
        {
            return;
        }

        NIRAUiParticleTransition.PlayDissolve(
            card,
            () =>
            {
                BranchCardsPanel.Children.Remove(card);
                _branchCards.Remove(branchId);
                _branchCardSignatures.Remove(branchId);
                _branchCardsLeaving.Remove(branchId);
            });
    }


    private static string BuildBranchCardSignature(
        NIRABranchState branch,
        NIRABranchWorkItem? work)
    {
        return string.Join(
            "|",
            branch.Objective,
            branch.Status,
            branch.JoinPolicy,
            branch.Priority.ToString("0.000"),
            branch.WaitingFor ?? string.Empty,
            branch.Blocker ?? string.Empty,
            branch.LastReason ?? string.Empty,
            work?.Id.ToString() ?? string.Empty,
            work?.Status.ToString() ?? string.Empty,
            work?.Kind.ToString() ?? string.Empty,
            work?.Reason ?? string.Empty);
    }


    private Border BuildBranchCard(
        NIRABranchState branch,
        NIRABranchWorkItem? work,
        int index)
    {
        string displayState =
            work?.Status switch
            {
                NIRABranchWorkStatus.Running => "RUNNING",
                NIRABranchWorkStatus.Pending => "QUEUED",
                _ => branch.Status.ToString().ToUpperInvariant()
            };

        string workKind =
            work?.Kind switch
            {
                NIRABranchWorkKind.DynamicTool => "WORKFLOW",
                NIRABranchWorkKind.Capability => "SYSTEM ACTION",
                _ => "AWAITING NEXT STEP"
            };

        string detail =
            ResolveBranchDetail(
                branch,
                work);

        Color accent =
            ResolveBranchAccent(
                branch,
                work);

        Border card =
            CreateActivityCardShell(
                accent,
                index,
                displayState == "RUNNING");

        Grid root =
            new();

        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        Grid header =
            new();

        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        Ellipse dot =
            new()
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(accent)
            };

        TextBlock branchLabel =
            new()
            {
                Text = "BRANCH",
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveThemeBrush("NIRASecondaryBrush", Color.FromRgb(111, 143, 176)),
                VerticalAlignment = VerticalAlignment.Center
            };

        Grid.SetColumn(
            branchLabel,
            1);

        Border badge =
            CreateBadge(
                displayState,
                accent);

        Grid.SetColumn(
            badge,
            2);

        header.Children.Add(dot);
        header.Children.Add(branchLabel);
        header.Children.Add(badge);

        TextBlock objective =
            new()
            {
                Text = branch.Objective,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveThemeBrush("NIRATextBrush", Color.FromRgb(220, 235, 250)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 40,
                LineHeight = 17
            };

        Grid.SetRow(
            objective,
            1);

        TextBlock detailText =
            new()
            {
                Text = detail,
                Margin = new Thickness(0, 7, 0, 0),
                FontSize = 9.5,
                Foreground = ResolveThemeBrush("NIRASecondaryBrush", Color.FromRgb(131, 160, 193)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                LineHeight = 15
            };

        Grid.SetRow(
            detailText,
            2);

        Grid footer =
            new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };

        footer.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        TextBlock kindText =
            new()
            {
                Text = workKind,
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveThemeBrush("NIRASecondaryBrush", Color.FromRgb(103, 141, 178))
            };

        TextBlock joinText =
            new()
            {
                Text = branch.JoinPolicy.ToString().ToUpperInvariant(),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveThemeBrush("NIRAMutedBrush", Color.FromRgb(92, 104, 145))
            };

        Grid.SetColumn(
            joinText,
            1);

        footer.Children.Add(kindText);
        footer.Children.Add(joinText);

        Grid.SetRow(
            footer,
            3);

        root.Children.Add(header);
        root.Children.Add(objective);
        root.Children.Add(detailText);
        root.Children.Add(footer);

        NIRAUiParticleTransition.Attach(
            card,
            root,
            accent);

        AnimateStatusDot(
            dot,
            displayState == "RUNNING",
            TimeSpan.FromSeconds(1.45));

        return card;
    }


    private static string ResolveBranchDetail(
        NIRABranchState branch,
        NIRABranchWorkItem? work)
    {
        if (work != null &&
            !string.IsNullOrWhiteSpace(work.Reason))
        {
            return work.Reason;
        }

        if (branch.Status == NIRABranchStatus.Waiting &&
            !string.IsNullOrWhiteSpace(branch.WaitingFor))
        {
            return branch.WaitingFor;
        }

        if (branch.Status == NIRABranchStatus.Blocked &&
            !string.IsNullOrWhiteSpace(branch.Blocker))
        {
            return branch.Blocker;
        }

        if (!string.IsNullOrWhiteSpace(branch.LastReason))
        {
            return branch.LastReason;
        }

        return "Awaiting NIRA's next decision.";
    }


    private static Color ResolveBranchAccent(
        NIRABranchState branch,
        NIRABranchWorkItem? work)
    {
        if (work?.Status == NIRABranchWorkStatus.Running)
        {
            return Color.FromRgb(120, 219, 255);
        }

        return branch.Status switch
        {
            NIRABranchStatus.Active => Color.FromRgb(77, 143, 255),
            NIRABranchStatus.Waiting => Color.FromRgb(176, 110, 255),
            NIRABranchStatus.Blocked => Color.FromRgb(255, 102, 131),
            NIRABranchStatus.Pending => Color.FromRgb(102, 126, 232),
            _ => Color.FromRgb(96, 115, 166)
        };
    }


    // =========================================================
    // COMMITMENT PROJECTION
    // =========================================================

    private void RefreshCommitmentActivity()
    {
        if (_selfModelService == null)
        {
            BeginRemovingAllCommitmentCards();

            NoCommitmentActivityText.Text =
                "No active commitments";

            NoCommitmentActivityText.Visibility =
                Visibility.Visible;

            return;
        }

        NIRACommitmentState[] active =
            _selfModelService.CurrentCommitments
                .Where(commitment => commitment.IsActive)
                .OrderByDescending(
                    commitment => commitment.Status == NIRACommitmentStatus.Blocked)
                .ThenBy(
                    commitment => commitment.Temporal?.NextWakeAtUtc ?? DateTimeOffset.MaxValue)
                .ThenByDescending(
                    commitment => commitment.UpdatedAt)
                .ToArray();

        HashSet<Guid> desiredIds =
            active
                .Select(commitment => commitment.Id)
                .ToHashSet();

        foreach (Guid existingId in
                 _commitmentCards.Keys.ToArray())
        {
            if (!desiredIds.Contains(existingId))
            {
                BeginCommitmentCardRemoval(existingId);
            }
        }

        if (active.Length == 0)
        {
            NoCommitmentActivityText.Text =
                "No active commitments";

            NoCommitmentActivityText.Visibility =
                Visibility.Visible;

            return;
        }

        NoCommitmentActivityText.Visibility =
            Visibility.Collapsed;

        for (int index = 0;
             index < active.Length;
             index++)
        {
            NIRACommitmentState commitment =
                active[index];

            string signature =
                BuildCommitmentCardSignature(
                    commitment);

            if (_commitmentCards.TryGetValue(
                    commitment.Id,
                    out Border? existing))
            {
                if (!_commitmentCardSignatures.TryGetValue(
                        commitment.Id,
                        out string? previousSignature) ||
                    !string.Equals(
                        previousSignature,
                        signature,
                        StringComparison.Ordinal))
                {
                    Border refreshed =
                        BuildCommitmentCard(
                            commitment,
                            index);

                    ApplyActivityCardTemplate(
                        existing,
                        refreshed);

                    _commitmentCardSignatures[commitment.Id] =
                        signature;
                }

                MoveCardToIndex(
                    CommitmentCardsPanel,
                    existing,
                    index);

                continue;
            }

            Border card =
                BuildCommitmentCard(
                    commitment,
                    index);

            _commitmentCards[commitment.Id] =
                card;

            _commitmentCardSignatures[commitment.Id] =
                signature;

            int insertIndex =
                Math.Min(
                    index,
                    CommitmentCardsPanel.Children.Count);

            CommitmentCardsPanel.Children.Insert(
                insertIndex,
                card);

            NIRAUiParticleTransition.PlayMaterialize(
                card,
                index + 2);
        }
    }


    private void BeginRemovingAllCommitmentCards()
    {
        foreach (Guid id in
                 _commitmentCards.Keys.ToArray())
        {
            BeginCommitmentCardRemoval(id);
        }
    }


    private void BeginCommitmentCardRemoval(
        Guid commitmentId)
    {
        if (!_commitmentCards.TryGetValue(
                commitmentId,
                out Border? card) ||
            !_commitmentCardsLeaving.Add(commitmentId))
        {
            return;
        }

        NIRAUiParticleTransition.PlayDissolve(
            card,
            () =>
            {
                CommitmentCardsPanel.Children.Remove(card);
                _commitmentCards.Remove(commitmentId);
                _commitmentCardSignatures.Remove(commitmentId);
                _commitmentCardsLeaving.Remove(commitmentId);
            });
    }


    private static string BuildCommitmentCardSignature(
        NIRACommitmentState commitment)
    {
        return string.Join(
            "|",
            commitment.Summary,
            commitment.Status,
            commitment.LastReason ?? string.Empty,
            commitment.Temporal?.Mode.ToString() ?? string.Empty,
            commitment.Temporal?.NextWakeAtUtc?.ToUnixTimeMilliseconds().ToString() ?? string.Empty,
            commitment.Temporal?.Revision.ToString() ?? string.Empty,
            commitment.UpdatedAt.ToUnixTimeMilliseconds());
    }


    private Border BuildCommitmentCard(
        NIRACommitmentState commitment,
        int index)
    {
        Color accent =
            commitment.Status switch
            {
                NIRACommitmentStatus.Blocked => Color.FromRgb(255, 102, 131),
                NIRACommitmentStatus.Waiting => Color.FromRgb(176, 110, 255),
                _ => Color.FromRgb(132, 123, 255)
            };

        Border card =
            CreateActivityCardShell(
                accent,
                index + 6,
                false,
                compact: true);

        Grid root =
            new();

        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(
            new RowDefinition { Height = GridLength.Auto });

        Grid header =
            new();

        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });

        TextBlock label =
            new()
            {
                Text = "COMMITMENT",
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveThemeBrush("NIRASecondaryBrush", Color.FromRgb(137, 121, 186))
            };

        Border badge =
            CreateBadge(
                commitment.Status.ToString().ToUpperInvariant(),
                accent);

        Grid.SetColumn(
            badge,
            1);

        header.Children.Add(label);
        header.Children.Add(badge);

        TextBlock summary =
            new()
            {
                Text = commitment.Summary,
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveThemeBrush("NIRATextBrush", Color.FromRgb(218, 225, 247)),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 36,
                LineHeight = 16
            };

        Grid.SetRow(
            summary,
            1);

        TextBlock? timing =
            null;

        if (commitment.Temporal != null)
        {
            timing =
                new TextBlock
                {
                    Text = NIRATemporalContextService.DescribeForUi(
                        commitment.Temporal),
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 8.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(accent),
                    Opacity = 0.84,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 28
                };

            Grid.SetRow(
                timing,
                2);
        }

        root.Children.Add(header);
        root.Children.Add(summary);

        if (timing != null)
        {
            root.Children.Add(timing);
        }

        NIRAUiParticleTransition.Attach(
            card,
            root,
            accent);

        return card;
    }


    // =========================================================
    // ACTIVITY CARD VISUALS
    // =========================================================

    private static Border CreateActivityCardShell(
        Color accent,
        int index,
        bool pulse,
        bool compact = false)
    {
        SolidColorBrush borderBrush =
            new(accent);

        Border card =
            new()
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = compact
                    ? new Thickness(11, 9, 11, 9)
                    : new Thickness(11, 10, 11, 10),
                Background =
                    Application.Current?.TryFindResource(
                        "NIRAActivityCardBrush") as Brush
                    ?? new SolidColorBrush(
                        Color.FromArgb(
                            58,
                            7,
                            15,
                            31)),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Opacity = 1.0,
                ClipToBounds = false,
                Effect = new DropShadowEffect
                {
                    BlurRadius = pulse ? 11 : 8,
                    ShadowDepth = 0,
                    Opacity = pulse ? 0.16 : 0.10,
                    Color = accent,
                    RenderingBias = RenderingBias.Performance
                }
            };

        if (pulse)
        {
            ColorAnimation colorPulse =
                new(
                    accent,
                    BlendColor(
                        accent,
                        Color.FromRgb(232, 244, 255),
                        0.12),
                    TimeSpan.FromSeconds(2.35))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 55),
                    EasingFunction = new SineEase
                    {
                        EasingMode = EasingMode.EaseInOut
                    }
                };

            borderBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                colorPulse,
                HandoffBehavior.SnapshotAndReplace);

            if (card.Effect is DropShadowEffect glow)
            {
                DoubleAnimation glowPulse =
                    new(
                        0.09,
                        0.18,
                        TimeSpan.FromSeconds(2.35))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(index * 55),
                        EasingFunction = new SineEase
                        {
                            EasingMode = EasingMode.EaseInOut
                        }
                    };

                glow.BeginAnimation(
                    DropShadowEffect.OpacityProperty,
                    glowPulse,
                    HandoffBehavior.SnapshotAndReplace);
            }
        }

        return card;
    }


    private static void ApplyActivityCardTemplate(
        Border target,
        Border source)
    {
        Color? previousAccent =
            (target.BorderBrush as SolidColorBrush)?.Color;

        Color? nextAccent =
            (source.BorderBrush as SolidColorBrush)?.Color;

        UIElement? child = source.Child;
        source.Child = null;

        target.Child = null;
        target.Padding = source.Padding;
        target.Background = source.Background;
        target.BorderThickness = source.BorderThickness;
        target.CornerRadius = source.CornerRadius;
        target.Effect = source.Effect;
        target.Tag = source.Tag;
        target.Child = child;

        if (nextAccent.HasValue)
        {
            SolidColorBrush brush =
                new(previousAccent ?? nextAccent.Value);

            target.BorderBrush = brush;

            if (previousAccent.HasValue &&
                previousAccent.Value != nextAccent.Value)
            {
                brush.BeginAnimation(
                    SolidColorBrush.ColorProperty,
                    new ColorAnimation(
                        previousAccent.Value,
                        nextAccent.Value,
                        TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new CubicEase
                        {
                            EasingMode = EasingMode.EaseOut
                        }
                    },
                    HandoffBehavior.SnapshotAndReplace);
            }
        }
        else
        {
            target.BorderBrush = source.BorderBrush;
        }

        NIRAUiParticleTransition.PlaySoftRefresh(target);
    }


    private static void MoveCardToIndex(
        Panel panel,
        UIElement card,
        int requestedIndex)
    {
        int currentIndex =
            panel.Children.IndexOf(card);

        if (currentIndex < 0)
        {
            return;
        }

        int targetIndex =
            Math.Clamp(
                requestedIndex,
                0,
                Math.Max(0, panel.Children.Count - 1));

        if (currentIndex == targetIndex)
        {
            return;
        }

        panel.Children.RemoveAt(currentIndex);

        targetIndex =
            Math.Min(
                targetIndex,
                panel.Children.Count);

        panel.Children.Insert(
            targetIndex,
            card);
    }


    private static Brush ResolveThemeBrush(
        string resourceKey,
        Color fallback)
    {
        return Application.Current?.TryFindResource(resourceKey) as Brush
            ?? new SolidColorBrush(fallback);
    }

    private static Border CreateBadge(
        string text,
        Color accent)
    {
        return new Border
        {
            Padding = new Thickness(7, 2, 7, 2),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(
                Color.FromArgb(
                    31,
                    accent.R,
                    accent.G,
                    accent.B)),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(
                    98,
                    accent.R,
                    accent.G,
                    accent.B)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 7.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent)
            }
        };
    }


    private static void AnimateStatusDot(
        FrameworkElement dot,
        bool active,
        TimeSpan duration)
    {
        dot.BeginAnimation(
            OpacityProperty,
            null);

        if (dot.RenderTransform is ScaleTransform existingScale)
        {
            existingScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                null);

            existingScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                null);
        }

        dot.RenderTransformOrigin =
            new Point(0.5, 0.5);

        ScaleTransform scale =
            new(1.0, 1.0);

        dot.RenderTransform =
            scale;

        if (!active)
        {
            return;
        }

        DoubleAnimation pulse =
            new(
                0.72,
                1.30,
                duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            pulse);

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            pulse);
    }


    private static Color BlendColor(
        Color left,
        Color right,
        double amount)
    {
        amount =
            Math.Clamp(
                amount,
                0.0,
                1.0);

        byte Blend(
            byte a,
            byte b) =>
            (byte)Math.Round(
                a +
                (b - a) * amount);

        return Color.FromRgb(
            Blend(left.R, right.R),
            Blend(left.G, right.G),
            Blend(left.B, right.B));
    }


    // =========================================================
    // BODY DOCK LOCATION
    //
    // CompanionWindow uses this screen-space point so the same
    // live desktop body can move into the interface instead of
    // being replaced with a second renderer or static image.
    // =========================================================

    public bool TryGetBodyDockScreenCenter(
        out Point screenCenter)
    {
        screenCenter =
            default;

        if (
            !IsLoaded
            ||
            !IsVisible
            ||
            WindowState ==
                WindowState.Minimized
            ||
            NIRADockHost.ActualWidth <=
                0.0
            ||
            NIRADockHost.ActualHeight <=
                0.0)
        {
            return false;
        }

        try
        {
            screenCenter =
                NIRADockHost.PointToScreen(
                    new Point(
                        NIRADockHost.ActualWidth *
                            0.5,
                        NIRADockHost.ActualHeight *
                            0.5));

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }


    // =========================================================
    // LOADED
    // =========================================================

    private void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        // NIRA opens maximized by default. This changes startup state only;
        // the existing main-window UI and layout remain untouched.
        if (WindowState != WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        MessageInput.Focus();
        Authorization_Changed();
        UpdateGreeting();
        ApplyThemeBackdrop(animate: false);

        PlayInterfaceEntrance();
        StartAmbientAnimations();

        if (_NIRAState !=
            null)
        {
            ApplyNIRAState(
                _NIRAState.Current);
        }

        RefreshBranchActivity();
        RefreshCommitmentActivity();
    }


    // =========================================================
    // NIRA STATE
    // =========================================================

    private void NIRAState_StateChanged(
        NIRAStateSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyNIRAState(
                    snapshot);
            });

            return;
        }

        ApplyNIRAState(
            snapshot);
    }


    private void ApplyNIRAState(
        NIRAStateSnapshot snapshot)
    {
        MindStateText.Text =
            snapshot.Mind
                .ToString()
                .ToUpperInvariant();

        BodyStateText.Text =
            snapshot.Body
                .ToString()
                .ToUpperInvariant();

        ApplyNIRAStateVisuals(
            snapshot);
    }


    private void ApplyNIRAStateVisuals(
        NIRAStateSnapshot snapshot)
    {
        Color accent =
            snapshot.Mind switch
            {
                NIRAMindState.Thinking => Color.FromRgb(77, 143, 255),
                NIRAMindState.Listening => Color.FromRgb(176, 110, 255),
                NIRAMindState.Speaking => Color.FromRgb(120, 219, 255),
                _ => Color.FromRgb(93, 150, 207)
            };

        MindStateText.Foreground =
            new SolidColorBrush(accent);

        MindStateIndicator.Fill =
            new SolidColorBrush(accent);

        BodyStateText.Foreground =
            snapshot.Body == NIRABodyState.Resting
                ? new SolidColorBrush(Color.FromRgb(142, 187, 255))
                : new SolidColorBrush(Color.FromRgb(176, 110, 255));

        SolidColorBrush panelBrush =
            new(accent);

        NIRAStatusPanel.BorderBrush =
            panelBrush;

        NIRAStatusPanel.Effect =
            new DropShadowEffect
            {
                BlurRadius = snapshot.Mind == NIRAMindState.Idle ? 9 : 15,
                ShadowDepth = 0,
                Opacity = snapshot.Mind == NIRAMindState.Idle ? 0.12 : 0.28,
                Color = accent
            };

        if (_lastAnimatedMindState != snapshot.Mind)
        {
            _lastAnimatedMindState =
                snapshot.Mind;

            DoubleAnimation textPulse =
                new(
                    0.38,
                    1.0,
                    TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction =
                        new CubicEase
                        {
                            EasingMode = EasingMode.EaseOut
                        }
                };

            MindStateText.BeginAnimation(
                OpacityProperty,
                textPulse);
        }

        bool active =
            snapshot.Mind != NIRAMindState.Idle;

        AnimateOrbState(
            accent,
            active,
            snapshot.Mind == NIRAMindState.Speaking);

        AnimateStatusDot(
            MindStateIndicator,
            snapshot.Mind is NIRAMindState.Thinking or NIRAMindState.Speaking,
            snapshot.Mind == NIRAMindState.Speaking
                ? TimeSpan.FromSeconds(0.72)
                : TimeSpan.FromSeconds(1.20));
    }


    private static void AnimateStatePanelPulse(
        SolidColorBrush borderBrush,
        Border panel,
        Color accent,
        bool active)
    {
        borderBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            null);

        if (!active)
        {
            return;
        }

        ColorAnimation pulse =
            new(
                accent,
                BlendColor(
                    accent,
                    Color.FromRgb(232, 244, 255),
                    0.34),
                TimeSpan.FromSeconds(1.25))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

        borderBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            pulse);

        if (panel.Effect is DropShadowEffect glow)
        {
            DoubleAnimation glowPulse =
                new(
                    0.12,
                    0.34,
                    TimeSpan.FromSeconds(1.25))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };

            glow.BeginAnimation(
                DropShadowEffect.OpacityProperty,
                glowPulse);
        }
    }


    private void AnimateOrbState(
        Color accent,
        bool active,
        bool speaking)
    {
        Shape[] rings =
        {
            OrbRingOuter,
            OrbRingMiddle,
            OrbRingInner,
            OrbRingCore
        };

        for (
            int index = 0;
            index < rings.Length;
            index++)
        {
            Shape ring =
                rings[index];

            ring.Stroke =
                new SolidColorBrush(
                    BlendColor(
                        accent,
                        Color.FromRgb(176, 110, 255),
                        index * 0.09));

            ring.RenderTransformOrigin =
                new Point(0.5, 0.5);

            ScaleTransform scale =
                new(1.0, 1.0);

            ring.RenderTransform =
                scale;

            if (!active)
            {
                ring.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(
                        0.34 + index * 0.04,
                        TimeSpan.FromMilliseconds(260)));

                continue;
            }

            double duration =
                (speaking ? 0.72 : 1.45) +
                index * 0.18;

            DoubleAnimation scaleAnimation =
                new(
                    0.985,
                    1.025 + index * 0.004,
                    TimeSpan.FromSeconds(duration))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 90)
                };

            DoubleAnimation opacityAnimation =
                new(
                    0.28 + index * 0.04,
                    0.70 - index * 0.05,
                    TimeSpan.FromSeconds(duration))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(index * 90)
                };

            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                scaleAnimation);

            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                scaleAnimation);

            ring.BeginAnimation(
                OpacityProperty,
                opacityAnimation);
        }
    }


    private void UpdateAccessVisual(
        int pendingCount)
    {
        if (!IsInitialized)
        {
            return;
        }

        Color accent =
            pendingCount > 0
                ? Color.FromRgb(255, 102, 131)
                : Color.FromRgb(120, 219, 255);

        SolidColorBrush brush =
            new(accent);

        AccessStatePanel.BorderBrush =
            brush;

        AccessPulseDot.Fill =
            new SolidColorBrush(accent);

        AccessStatePanel.Effect =
            new DropShadowEffect
            {
                BlurRadius = pendingCount > 0 ? 15 : 7,
                ShadowDepth = 0,
                Opacity = pendingCount > 0 ? 0.30 : 0.08,
                Color = accent
            };

        AnimateStatePanelPulse(
            brush,
            AccessStatePanel,
            accent,
            pendingCount > 0);

        AnimateStatusDot(
            AccessPulseDot,
            pendingCount > 0,
            TimeSpan.FromSeconds(0.78));
    }


    // =========================================================
    // INTERFACE AMBIENT MOTION
    // =========================================================

    private void PlayInterfaceEntrance()
    {
        if (_interfaceEntrancePlayed)
        {
            return;
        }

        _interfaceEntrancePlayed =
            true;

        RootShell.Opacity =
            0.0;

        // Keep the body dock geometry stable during startup. The whole
        // interface fades/materializes without translating NIRADockHost.
        RootShell.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(
                0.0,
                1.0,
                TimeSpan.FromMilliseconds(360))
            {
                EasingFunction =
                    new CubicEase
                    {
                        EasingMode = EasingMode.EaseOut
                    }
            });

        NIRAMotion.AnimateEntrance(
            NIRADockHost,
            distance: 10.0,
            durationMs: 320,
            delayMs: 40);

        NIRAMotion.AnimateEntrance(
            ComposerPanel,
            distance: 14.0,
            durationMs: 340,
            delayMs: 120);
    }


    private void StartAmbientAnimations()
    {
        if (_ambientAnimationsStarted)
        {
            return;
        }

        _ambientAnimationsStarted = true;

        // Deliberately quiet. These are presence indicators, not activity
        // meters. Motion is reserved for meaningful NIRA/work state changes.
        NIRAMotion.BeginPulseOpacity(
            AmbientGlowLeft,
            0.42,
            0.68,
            7.8);

        NIRAMotion.BeginPulseOpacity(
            AmbientGlowRight,
            0.28,
            0.52,
            9.2,
            delayMs: 420);

        LivePulseDot.Opacity = 0.88;
        OnlinePulseDot.Opacity = 0.82;
        ConnectedPulseDot.Opacity = 0.82;

        NIRAMotion.BeginPulseOpacity(
            LivePulseDot,
            0.45,
            1.0,
            1.4);

        NIRAMotion.BeginPulseOpacity(
            OnlinePulseDot,
            0.38,
            0.92,
            1.9,
            delayMs: 180);

        NIRAMotion.BeginPulseOpacity(
            ConnectedPulseDot,
            0.38,
            0.92,
            1.6,
            delayMs: 320);
    }



    // =========================================================
    // VISUAL ARTIFACT SURFACES
    // =========================================================

    private void ViewModel_VisualArtifactReceived(
        NIRAVisualArtifact artifact)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                    ViewModel_VisualArtifactReceived(
                        artifact)));

            return;
        }

        bool chatActive =
            IsVisible
            &&
            WindowState != WindowState.Minimized
            &&
            IsActive;

        bool wantsToast =
            artifact.Surface switch
            {
                NIRAVisualArtifactPresentationSurface.ToastOnly =>
                    true,

                NIRAVisualArtifactPresentationSurface.InlineAndToast =>
                    true,

                NIRAVisualArtifactPresentationSurface.Auto =>
                    !chatActive,

                _ =>
                    false
            };

        if (!wantsToast)
        {
            return;
        }

        if (artifact.Surface ==
                NIRAVisualArtifactPresentationSurface.Auto
            &&
            !_visualToastPlacement.ShouldShowDesktopPeek())
        {
            // Fullscreen is treated as a strong non-interruption signal for
            // automatic peeks. The inline artifact remains in chat.
            return;
        }

        ShowVisualArtifactToast(
            new VisualArtifactViewModel(artifact));
    }


    private void ShowVisualArtifactToast(
        VisualArtifactViewModel artifact)
    {
        CloseVisualArtifactToast();

        NIRAVisualArtifactToastWindow toast =
            new(artifact);

        toast.OpenChatRequested +=
            VisualArtifactToast_OpenChatRequested;

        toast.ViewRequested +=
            () =>
                OpenVisualArtifactViewer(artifact);

        toast.Closed +=
            (_, _) =>
            {
                if (ReferenceEquals(
                        _visualArtifactToast,
                        toast))
                {
                    _visualArtifactToast =
                        null;
                }
            };

        _visualArtifactToast =
            toast;

        toast.ShowAt(
            _visualToastPlacement);
    }


    private void VisualArtifactToast_OpenChatRequested()
    {
        CloseVisualArtifactToast();

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState ==
            WindowState.Minimized)
        {
            WindowState =
                WindowState.Normal;
        }

        Activate();
        Focus();
        ScrollChatToBottom();
    }


    private void VisualArtifactImage_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element
            &&
            element.DataContext is VisualArtifactViewModel artifact)
        {
            OpenVisualArtifactViewer(artifact);
            e.Handled = true;
        }
    }


    private void OpenVisualArtifactViewer(
        VisualArtifactViewModel artifact)
    {
        NIRAVisualArtifactViewerWindow viewer =
            new(artifact);

        if (IsVisible)
        {
            viewer.Owner =
                this;
        }

        viewer.Show();
        viewer.Activate();
    }


    private void CloseVisualArtifactToast()
    {
        if (_visualArtifactToast ==
            null)
        {
            return;
        }

        NIRAVisualArtifactToastWindow toast =
            _visualArtifactToast;

        _visualArtifactToast =
            null;

        toast.OpenChatRequested -=
            VisualArtifactToast_OpenChatRequested;

        try
        {
            toast.Close();
        }
        catch (InvalidOperationException)
        {
        }
    }


    // =========================================================
    // MESSAGE COLLECTION
    // =========================================================

    private void Messages_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems !=
            null)
        {
            foreach (
                ChatMessageViewModel message
                in e.NewItems)
            {
                message.PropertyChanged +=
                    Message_PropertyChanged;
            }
        }

        if (e.OldItems !=
            null)
        {
            foreach (
                ChatMessageViewModel message
                in e.OldItems)
            {
                message.PropertyChanged -=
                    Message_PropertyChanged;
            }
        }

        ScrollChatToBottom();
    }


    private void Message_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
                nameof(ChatMessageViewModel.Content)
            ||
            e.PropertyName ==
                nameof(ChatMessageViewModel.HasVisualArtifacts))
        {
            ScrollChatToBottom();
        }
    }


    private void ScrollChatToBottom()
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                ChatScroll.ScrollToEnd();
            }),
            System.Windows.Threading
                .DispatcherPriority
                .Background);
    }


    // =========================================================
    // TITLE BAR DRAG
    // =========================================================

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount ==
            2)
        {
            ToggleMaximize();

            return;
        }

        if (e.LeftButton ==
            MouseButtonState.Pressed)
        {
            DragMove();
        }
    }


    // =========================================================
    // MINIMIZE
    // =========================================================

    private void MinimizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowState =
            WindowState.Minimized;
    }


    // =========================================================
    // MAXIMIZE
    // =========================================================

    private void MaximizeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ToggleMaximize();
    }


    private void ToggleMaximize()
    {
        WindowState =
            WindowState ==
                WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }




    // =========================================================
    // CLOSE
    //
    // Existing behavior is preserved: closing the chat hides
    // the main interface while NIRA's desktop presence remains.
    // =========================================================

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Hide();
    }


    // =========================================================
    // EXIT NIRA COMPLETELY
    //
    // The normal X above only hides the interface and leaves NIRA's
    // desktop presence / hosted services running.
    //
    // This separate power button intentionally shuts down the WPF
    // application. App.OnExit then stops and disposes the Generic Host,
    // which gives NIRA's hosted services their normal clean shutdown.
    // =========================================================

    private void ExitAgentButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBoxResult result =
            MessageBox.Show(
                this,
                "Exit NIRA completely?\n\n" +
                "This will stop the AI, background services and desktop presence.",
                "Exit NIRA",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (result !=
            MessageBoxResult.Yes)
        {
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }


    // =========================================================
    // MESSAGE INPUT
    // =========================================================

    private void MessageInput_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (
            e.Key ==
                Key.Enter
            &&
            Keyboard.Modifiers ==
                ModifierKeys.None)
        {
            e.Handled =
                true;

            if (
                DataContext
                is MainWindowViewModel viewModel
                &&
                viewModel.SendCommand
                    .CanExecute(
                        null))
            {
                viewModel.SendCommand
                    .Execute(
                        null);
            }

            return;
        }

        if (
            e.Key ==
                Key.Enter
            &&
            Keyboard.Modifiers ==
                ModifierKeys.Shift)
        {
            e.Handled =
                false;
        }
    }


    // =========================================================
    // CLOSED
    // =========================================================

    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _authorizationClosed = true;

        if (_permissionToast != null)
        {
            _permissionToast.OpenFullPermissionsRequested -=
                PermissionToast_OpenFullPermissionsRequested;

            _permissionToast.Close();
            _permissionToast = null;
        }

        if (_approvalBroker != null)
        {
            _approvalBroker.Changed -= Authorization_Changed;
            _approvalBroker.DenyAll();
        }

        _permissionsWindow?.Close();
        _settingsWindow?.Close();

        if (_settingsService != null)
        {
            _settingsService.Changed -= RuntimeSettings_Changed;
            _settingsService = null;
        }

        if (_NIRAState !=
            null)
        {
            _NIRAState.StateChanged -=
                NIRAState_StateChanged;

            _NIRAState =
                null;
        }

        if (_branchService != null)
        {
            _branchService.StateChanged -=
                BranchActivity_Changed;

            _branchService =
                null;
        }

        if (_branchWorkService != null)
        {
            _branchWorkService.StateChanged -=
                BranchActivity_Changed;

            _branchWorkService =
                null;
        }

        if (_selfModelService != null)
        {
            _selfModelService.StateChanged -=
                ContinuityState_Changed;

            _selfModelService =
                null;
        }

        if (_visualEvidenceService != null)
        {
            _visualEvidenceService.ActivityChanged -=
                VisionActivity_Changed;

            _visualEvidenceService =
                null;
        }

        _viewModel.VisualArtifactReceived -=
            ViewModel_VisualArtifactReceived;

        _viewModel.Messages.CollectionChanged -=
            Messages_CollectionChanged;

        CloseVisualArtifactToast();

        _workAreaGuard.Dispose();

        Loaded -=
            MainWindow_Loaded;

        NIRAThemeManager.ThemeChanged -=
            NIRAThemeManager_ThemeChanged;

        Closed -=
            MainWindow_Closed;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using NIRAAgent.Authorization;
using NIRAAgent.Capabilities;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI;

public partial class PermissionsWindow : Window
{
    private readonly NIRAAuthorityStore _store;
    private readonly NIRAScopedCapabilityAuthorizer _authorizer;
    private readonly NIRACapabilityApprovalBroker _broker;
    private bool _closed;
    private int _refreshQueued;
    private bool _manualSession;
    private bool _autoCloseAfterDecision;
    private bool _denyPendingOnClose = true;
    private readonly NIRAWorkAreaWindowGuard _workAreaGuard;

    public PermissionsWindow(
        NIRAAuthorityStore store,
        NIRAScopedCapabilityAuthorizer authorizer,
        NIRACapabilityApprovalBroker broker)
    {
        InitializeComponent();

        _store = store;
        _authorizer = authorizer;
        _broker = broker;
        _workAreaGuard = new NIRAWorkAreaWindowGuard(this);

        Loaded += PermissionsWindow_Loaded;
        _store.Changed += QueueRefresh;
        _broker.Changed += QueueRefresh;
        NIRAThemeManager.ThemeChanged += NIRAThemeManager_ThemeChanged;

        Closed += PermissionsWindow_Closed;

        Refresh();
        UpdateThemePresentation();
    }

    private void PermissionsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NIRAMotion.AnimateEntrance(RootShell, distance: 12.0, durationMs: 300);
        UpdateThemePresentation();
    }

    private void NIRAThemeManager_ThemeChanged(NIRAThemeMode mode)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(UpdateThemePresentation));
            return;
        }

        UpdateThemePresentation();
    }

    private void UpdateThemePresentation()
    {
        bool halo = NIRAThemeManager.CurrentMode == NIRAThemeMode.Halo;
        UtilityBackgroundEclipse.Opacity = halo ? 0.0 : 1.0;
        UtilityBackgroundHalo.Opacity = halo ? 1.0 : 0.0;
    }

    /// <summary>
    /// Used by MainWindow when the user switches from Permissions to Settings.
    /// Pending requests stay pending instead of being interpreted as denied.
    /// </summary>
    public void CloseForWindowSwitch()
    {
        _denyPendingOnClose = false;
        Close();
    }

    private void PermissionsWindow_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _store.Changed -= QueueRefresh;
        _broker.Changed -= QueueRefresh;
        NIRAThemeManager.ThemeChanged -= NIRAThemeManager_ThemeChanged;
        Loaded -= PermissionsWindow_Loaded;

        if (_denyPendingOnClose)
        {
            _broker.DenyAll();
        }

        _workAreaGuard.Dispose();
        Closed -= PermissionsWindow_Closed;
    }

    public void ShowRequests(bool autoCloseAfterDecision = false)
    {
        if (autoCloseAfterDecision && !_manualSession)
        {
            _autoCloseAfterDecision = true;
        }

        Tabs.SelectedIndex = 0;
        Refresh();
        Activate();
    }

    public void MarkManualSession()
    {
        _manualSession = true;
        _autoCloseAfterDecision = false;
    }

    private void QueueRefresh()
    {
        if (_closed || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (_closed) return;

            Refresh();
            TryAutoCloseWhenRequestsDrain();
        }));
    }

    private void Refresh()
    {
        try
        {
            Guid? selectedRequest = (RequestsList.SelectedItem as NIRAApprovalRequest)?.Id;
            string editedRoot = RememberRoot.Text;
            Guid? selectedScope = (ScopesList.SelectedItem as NIRAAuthorityScope)?.Id;

            var requests = _broker.PendingRequests;
            RequestsList.ItemsSource = requests;
            RequestsList.SelectedItem = requests.FirstOrDefault(x => x.Id == selectedRequest) ?? requests.FirstOrDefault();

            var scopes = _store.ReadScopes().Where(x => x.Active).ToArray();
            ScopesList.ItemsSource = scopes;
            ScopesList.SelectedItem = scopes.FirstOrDefault(x => x.Id == selectedScope);

            AuditGrid.ItemsSource = _store.ReadAudit();
            StatusText.Text = $"{requests.Count} pending request(s) / {scopes.Length} active saved grant(s)";

            UpdateRequest();

            if (selectedRequest.HasValue &&
                (RequestsList.SelectedItem as NIRAApprovalRequest)?.Id == selectedRequest)
            {
                RememberRoot.Text = editedRoot;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Unable to load permissions: {ex.Message}";
        }
    }

    private void Request_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRequest();

    private void UpdateRequest()
    {
        if (OnceButton == null) return;

        NIRAApprovalRequest? request = RequestsList.SelectedItem as NIRAApprovalRequest;
        OnceButton.IsEnabled = DenyButton.IsEnabled = request != null;
        RememberButton.IsEnabled = request?.Operation.CanRemember == true;
        TaskButton.IsEnabled =
            request?.Operation.CanRemember == true &&
            request.Operation.ExecutionContext.HasTaskBoundary;
        RememberRoot.Visibility = Visibility.Collapsed;
        ExecutionProfilesPanel.Visibility = Visibility.Collapsed;
        DotNetBuildTestCheck.IsChecked = false;
        GitInspectCheck.IsChecked = false;

        if (request == null)
        {
            RequestTitle.Text = "No pending request";
            TaskButton.Content = "Allow for this task";
            RequestTarget.Text = RequestNotice.Text = RequestReason.Text = RequestPreview.Text = RememberDescription.Text = "";
            return;
        }

        NIRAAuthorityOperation operation = request.Operation;
        RequestTitle.Text = $"{operation.Request.CapabilityId} / {operation.Risk}";
        RequestTarget.Text = operation.Target;
        RequestNotice.Text = operation.Notice;
        RequestReason.Text = operation.Request.Reason;
        RequestPreview.Text = operation.Preview;

        TaskButton.Content = operation.ExecutionContext.HasTaskBoundary
            ? "Allow for this task"
            : "No task boundary";

        if (!operation.CanRemember)
        {
            RememberDescription.Text = "This request can be approved once.";
            return;
        }

        if (operation.Request.CapabilityId == NIRACapabilityIds.VisionInspect &&
            operation.RequiresExplicitAuthorization)
        {
            RememberButton.Content = "Allow cloud vision";
            RememberDescription.Text =
                "Allow once for a single capture, allow for this task so later captures for the same goal do not interrupt you again, or remember cloud vision until you revoke it.";
            return;
        }

        if (operation.Paths.Length > 0)
        {
            RememberButton.Content = "Trust this project / folder";
            RememberRoot.Visibility = Visibility.Visible;

            string projectRoot =
                ResolveProjectRootSuggestion(
                    operation.SuggestedRoot);

            RememberRoot.Text = projectRoot;
            ConfigureExecutionProfilePreflight(
                operation,
                projectRoot);

            RememberDescription.Text =
                "A task grant lets NIRA keep doing normal file creation/write/copy/move work inside this root for the current persistent goal. " +
                "A remembered grant keeps that workspace authority for future work too. " +
                "You can also approve bounded project build/test or Git-inspection profiles now, so later matching commands do not interrupt the task. " +
                (operation.Origin.Length > 0 ? $"Downloads remain limited to {operation.Origin}. " : string.Empty) +
                (operation.Risk == NIRACapabilityRisk.Destructive
                    ? "Because this request is destructive, approving the task/persistent scope also includes destructive operations inside this boundary."
                    : "Deletion is not included unless a destructive request is explicitly approved.");
            return;
        }

        if (NIRAAuthorityExecutionProfiles.IsExecutionCapability(
                operation.Request.CapabilityId))
        {
            RememberButton.Content = "Trust bounded project execution";
            RememberRoot.Visibility = Visibility.Visible;

            string projectRoot =
                ResolveProjectRootSuggestion(
                    operation.WorkingDirectory);

            RememberRoot.Text = projectRoot;
            ConfigureExecutionProfilePreflight(
                operation,
                projectRoot);

            RememberDescription.Text =
                "If this command belongs to one of the selected bounded project profiles, Allow for this task can reuse that profile for the current goal and Remember can keep it for this project. " +
                "The permission remains limited to the selected project root and recognized command family; arbitrary PowerShell/cmd execution stays separately authorized.";
            return;
        }

        if (operation.Request.CapabilityId == NIRACapabilityIds.BrowserAuthenticate)
        {
            RememberButton.Content = "Trust this site + login";
            RememberDescription.Text =
                $"Allow NIRA to use the trusted credential broker on {operation.Origin}. Passwords remain outside cognition. " +
                "Task approval lasts only for this goal; remembering allows future credential use on this exact website origin until revoked.";
            return;
        }

        if (NIRACapabilityRequestPolicy.IsBrowserOriginScoped(operation.Request.CapabilityId) &&
            operation.Origin.Length > 0)
        {
            RememberButton.Content = "Trust this site";
            RememberDescription.Text =
                $"Allow normal grounded browser interaction on {operation.Origin}. " +
                "A task grant is limited to the current goal; a remembered site grant prevents repeated click/fill/select/download prompts on this origin. " +
                "Credential use remains separate unless explicitly granted.";
            return;
        }

        if (operation.Origin.Length > 0)
        {
            RememberButton.Content = "Allow this origin";
            RememberDescription.Text =
                $"Save permission for {operation.Method} requests to {operation.Origin}. Credential headers and credential URL parameters still require review.";
            return;
        }

        RememberButton.Content = "Remember exact request";
        RememberDescription.Text =
            operation.ExecutionContext.HasTaskBoundary
                ? "Allow for this task will reuse this exact request only inside the current goal. Remember saves the same exact request for future work."
                : "Save permission only for the executable, command, arguments and other parameters shown above. Changed requests require another decision.";
    }

    private void Resolve(NIRAApprovalChoice choice)
    {
        if (RequestsList.SelectedItem is not NIRAApprovalRequest request) return;

        try
        {
            NIRAApprovalResponse response = new()
            {
                Choice = choice,
                FolderRoot = RememberRoot.Text,
                Label = request.Operation.Request.CapabilityId,
                AllowDestructive =
                    request.Operation.Risk == NIRACapabilityRisk.Destructive,
                ExecutionProfileIds =
                    ReadRequestExecutionProfiles()
            };

            if (choice == NIRAApprovalChoice.Remember)
            {
                _authorizer.BuildRememberedScope(request.Operation, response);
            }
            else if (choice == NIRAApprovalChoice.AllowTask)
            {
                _authorizer.BuildTaskScope(request.Operation, response);
            }

            bool accepted = _broker.Resolve(request.Id, response);
            OnceButton.IsEnabled = TaskButton.IsEnabled = RememberButton.IsEnabled = DenyButton.IsEnabled = false;
            StatusText.Text = accepted
                ? "Decision submitted. NIRA can continue."
                : "This request has already ended.";

            if (accepted)
            {
                TryAutoCloseWhenRequestsDrain();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void ConfigureExecutionProfilePreflight(
        NIRAAuthorityOperation operation,
        string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            ExecutionProfilesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HashSet<string> suggested =
            new(
                NIRAAuthorityExecutionProfiles.SuggestForWorkspace(
                    projectRoot),
                StringComparer.Ordinal);

        if (NIRAAuthorityExecutionProfiles.IsExecutionCapability(
                operation.Request.CapabilityId))
        {
            foreach (NIRAAuthorityExecutionProfileDefinition profile
                     in NIRAAuthorityExecutionProfiles.AllDefinitions)
            {
                if (NIRAAuthorityExecutionProfiles.Matches(
                        operation,
                        profile.Id))
                {
                    suggested.Add(profile.Id);
                }
            }
        }

        ExecutionProfilesPanel.Visibility = Visibility.Visible;
        DotNetBuildTestCheck.IsChecked =
            suggested.Contains(
                NIRAAuthorityExecutionProfileIds.DotNetBuildTest);
        GitInspectCheck.IsChecked =
            suggested.Contains(
                NIRAAuthorityExecutionProfileIds.GitInspect);
    }

    private string ResolveProjectRootSuggestion(
        string? suggestedRoot)
    {
        if (string.IsNullOrWhiteSpace(suggestedRoot))
        {
            return string.Empty;
        }

        string current;

        try
        {
            current =
                NIRACapabilityRequestPolicy.NormalizeLocalPath(
                    suggestedRoot);
        }
        catch
        {
            return suggestedRoot.Trim();
        }

        if (!Directory.Exists(current))
        {
            return current;
        }

        string fallback = current;

        for (int depth = 0; depth < 8; depth++)
        {
            try
            {
                bool marker =
                    Directory.Exists(
                        Path.Combine(current, ".git"))
                    ||
                    Directory.EnumerateFiles(
                            current,
                            "*.sln",
                            SearchOption.TopDirectoryOnly)
                        .Any()
                    ||
                    Directory.EnumerateFiles(
                            current,
                            "*.slnx",
                            SearchOption.TopDirectoryOnly)
                        .Any();

                if (marker)
                {
                    return current;
                }
            }
            catch
            {
                return fallback;
            }

            DirectoryInfo? parent =
                Directory.GetParent(current);

            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return fallback;
    }

    private string[] ReadRequestExecutionProfiles()
    {
        List<string> ids = new();

        if (ExecutionProfilesPanel.Visibility == Visibility.Visible &&
            DotNetBuildTestCheck.IsChecked == true)
        {
            ids.Add(
                NIRAAuthorityExecutionProfileIds.DotNetBuildTest);
        }

        if (ExecutionProfilesPanel.Visibility == Visibility.Visible &&
            GitInspectCheck.IsChecked == true)
        {
            ids.Add(
                NIRAAuthorityExecutionProfileIds.GitInspect);
        }

        return ids.ToArray();
    }

    private string[] ReadSavedGrantExecutionProfiles()
    {
        List<string> ids = new();

        if (SavedDotNetBuildTestCheck.IsChecked == true)
        {
            ids.Add(
                NIRAAuthorityExecutionProfileIds.DotNetBuildTest);
        }

        if (SavedGitInspectCheck.IsChecked == true)
        {
            ids.Add(
                NIRAAuthorityExecutionProfileIds.GitInspect);
        }

        return ids.ToArray();
    }

    private void TryAutoCloseWhenRequestsDrain()
    {
        if (_closed || _manualSession || !_autoCloseAfterDecision || _broker.PendingRequests.Count != 0)
        {
            return;
        }

        _autoCloseAfterDecision = false;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_closed && !_manualSession && _broker.PendingRequests.Count == 0)
            {
                Close();
            }
        }));
    }

    private void PermissionsTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PermissionsMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void PermissionsMaximize_Click(object sender, RoutedEventArgs e) =>
        ToggleMaximize();

    private void PermissionsClose_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void AllowOnce_Click(object sender, RoutedEventArgs e) => Resolve(NIRAApprovalChoice.AllowOnce);
    private void AllowTask_Click(object sender, RoutedEventArgs e) => Resolve(NIRAApprovalChoice.AllowTask);
    private void Remember_Click(object sender, RoutedEventArgs e) => Resolve(NIRAApprovalChoice.Remember);
    private void Deny_Click(object sender, RoutedEventArgs e) => Resolve(NIRAApprovalChoice.Deny);
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void GrantFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            NIRAAuthorityScope scope = _authorizer.GrantFolder(
                "",
                ProjectRoot.Text,
                DownloadsCheck.IsChecked == true,
                DestructiveCheck.IsChecked == true,
                ReadSavedGrantExecutionProfiles());

            Refresh();
            StatusText.Text = $"Granted folder access: {scope.RootPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (ScopesList.SelectedItem is not NIRAAuthorityScope scope) return;

        try
        {
            _store.Revoke(scope.Id);
            Refresh();
            StatusText.Text = "Grant revoked. Future dispatches require permission again.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}


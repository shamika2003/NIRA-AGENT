using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SegaAgent.Authorization;
using SegaAgent.Capabilities;

namespace SegaAgent.UI;

public partial class PermissionsWindow : Window
{
    private readonly SegaAuthorityStore _store;
    private readonly SegaScopedCapabilityAuthorizer _authorizer;
    private readonly SegaCapabilityApprovalBroker _broker;
    private bool _closed;
    private int _refreshQueued;
    private bool _manualSession;
    private bool _autoCloseAfterDecision;
    private readonly SegaWorkAreaWindowGuard _workAreaGuard;

    public PermissionsWindow(SegaAuthorityStore store, SegaScopedCapabilityAuthorizer authorizer,
        SegaCapabilityApprovalBroker broker)
    {
        InitializeComponent();
        _store = store; _authorizer = authorizer; _broker = broker;
        _workAreaGuard = new SegaWorkAreaWindowGuard(this);
        _store.Changed += QueueRefresh;
        _broker.Changed += QueueRefresh;
        Closed += (_, _) =>
        {
            _closed = true;
            _store.Changed -= QueueRefresh;
            _broker.Changed -= QueueRefresh;
            _broker.DenyAll();
            _workAreaGuard.Dispose();
        };
        Refresh();
    }

    public void ShowRequests(bool autoCloseAfterDecision = false)
    {
        // Auto-close is only for a window Sega opened specifically to obtain
        // a permission decision. A user-opened Permissions session stays open
        // for grants/audit inspection even if a request arrives while it is open.
        if (autoCloseAfterDecision && !_manualSession)
            _autoCloseAfterDecision = true;

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
            Guid? selectedRequest = (RequestsList.SelectedItem as SegaApprovalRequest)?.Id;
            string editedRoot = RememberRoot.Text;
            Guid? selectedScope = (ScopesList.SelectedItem as SegaAuthorityScope)?.Id;
            var requests = _broker.PendingRequests;
            RequestsList.ItemsSource = requests;
            RequestsList.SelectedItem = requests.FirstOrDefault(x => x.Id == selectedRequest) ?? requests.FirstOrDefault();
            var scopes = _store.ReadScopes().Where(x => x.Active).ToArray();
            ScopesList.ItemsSource = scopes;
            ScopesList.SelectedItem = scopes.FirstOrDefault(x => x.Id == selectedScope);
            AuditGrid.ItemsSource = _store.ReadAudit();
            StatusText.Text = $"{requests.Count} pending request(s) / {scopes.Length} active saved grant(s)";
            UpdateRequest();
            if (selectedRequest.HasValue && (RequestsList.SelectedItem as SegaApprovalRequest)?.Id == selectedRequest)
                RememberRoot.Text = editedRoot;
        }
        catch (Exception ex) { StatusText.Text = $"Unable to load permissions: {ex.Message}"; }
    }

    private void Request_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRequest();
    private void UpdateRequest()
    {
        // SelectionChanged can run while InitializeComponent is still building.
        if (OnceButton == null) return;
        SegaApprovalRequest? request = RequestsList.SelectedItem as SegaApprovalRequest;
        OnceButton.IsEnabled = DenyButton.IsEnabled = request != null;
        RememberButton.IsEnabled = request?.Operation.CanRemember == true;
        RememberRoot.Visibility = Visibility.Collapsed;
        if (request == null)
        {
            RequestTitle.Text = "No pending request";
            RequestTarget.Text = RequestNotice.Text = RequestReason.Text = RequestPreview.Text = RememberDescription.Text = "";
            return;
        }
        SegaAuthorityOperation operation = request.Operation;
        RequestTitle.Text = $"{operation.Request.CapabilityId} / {operation.Risk}";
        RequestTarget.Text = operation.Target;
        RequestNotice.Text = operation.Notice;
        RequestReason.Text = operation.Request.Reason;
        RequestPreview.Text = operation.Preview;
        if (!operation.CanRemember)
        {
            RememberDescription.Text = "This request can be approved once.";
            return;
        }
        if (operation.Request.CapabilityId == SegaCapabilityIds.VisionInspect &&
            operation.RequiresExplicitAuthorization)
        {
            RememberButton.Content = "Allow cloud vision";
            RememberDescription.Text =
                "Save permission for Sega to send future grounded screenshots to the configured Ollama cloud vision model for visual interpretation. You can revoke this permission later from Permissions.";
            return;
        }
        if (operation.Paths.Length > 0)
        {
            RememberButton.Content = "Allow this folder";
            RememberRoot.Visibility = Visibility.Visible;
            RememberRoot.Text = operation.SuggestedRoot;
            RememberDescription.Text = $"Save permission for {operation.Request.CapabilityId} inside the folder below, including its children. " +
                (operation.Origin.Length > 0 ? $"Downloads must come from {operation.Origin}. " : "") +
                (operation.Risk == SegaAgent.Capabilities.SegaCapabilityRisk.Destructive ? "This includes destructive uses of this capability." : "");
        }
        else if (operation.Origin.Length > 0)
        {
            RememberButton.Content = "Allow this origin";
            RememberDescription.Text = $"Save permission for {operation.Method} requests to {operation.Origin}. Credential headers and credential URL parameters still require review.";
        }
        else
        {
            RememberButton.Content = "Remember exact request";
            RememberDescription.Text = "Save permission only for the executable, command, arguments and other parameters shown above. Changed requests require another decision.";
        }
    }

    private void Resolve(SegaApprovalChoice choice)
    {
        if (RequestsList.SelectedItem is not SegaApprovalRequest request) return;
        try
        {
            SegaApprovalResponse response = new()
            {
                Choice = choice, FolderRoot = RememberRoot.Text,
                Label = request.Operation.Request.CapabilityId
            };
            if (choice == SegaApprovalChoice.Remember)
                _authorizer.BuildRememberedScope(request.Operation, response);
            bool accepted = _broker.Resolve(request.Id, response);
            OnceButton.IsEnabled = RememberButton.IsEnabled = DenyButton.IsEnabled = false;
            StatusText.Text = accepted ? "Decision submitted. Sega can continue." : "This request has already ended.";

            if (accepted)
                TryAutoCloseWhenRequestsDrain();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void TryAutoCloseWhenRequestsDrain()
    {
        if (_closed ||
            _manualSession ||
            !_autoCloseAfterDecision ||
            _broker.PendingRequests.Count != 0)
        {
            return;
        }

        _autoCloseAfterDecision = false;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_closed &&
                !_manualSession &&
                _broker.PendingRequests.Count == 0)
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
            DragMove();
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

    private void AllowOnce_Click(object sender, RoutedEventArgs e) => Resolve(SegaApprovalChoice.AllowOnce);
    private void Remember_Click(object sender, RoutedEventArgs e) => Resolve(SegaApprovalChoice.Remember);
    private void Deny_Click(object sender, RoutedEventArgs e) => Resolve(SegaApprovalChoice.Deny);
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void GrantFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SegaAuthorityScope scope = _authorizer.GrantFolder("", ProjectRoot.Text,
                DownloadsCheck.IsChecked == true, DestructiveCheck.IsChecked == true);
            Refresh();
            StatusText.Text = $"Granted folder access: {scope.RootPath}";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (ScopesList.SelectedItem is not SegaAuthorityScope scope) return;
        try
        {
            _store.Revoke(scope.Id);
            Refresh();
            StatusText.Text = "Grant revoked. Future dispatches require permission again.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
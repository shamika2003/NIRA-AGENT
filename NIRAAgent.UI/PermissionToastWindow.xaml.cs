using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

using NIRAAgent.Authorization;
using NIRAAgent.Capabilities;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI;

/// <summary>
/// Non-activating permission surface for routine authorization decisions.
/// It never grants anything by itself; it only resolves requests already
/// present in the trusted NIRACapabilityApprovalBroker.
/// </summary>
public partial class PermissionToastWindow : Window
{
    private readonly NIRAScopedCapabilityAuthorizer
        _authorizer;

    private readonly NIRACapabilityApprovalBroker
        _broker;

    private bool
        _closed;

    private bool
        _suspended;

    private int
        _refreshQueued;

    private Guid?
        _currentRequestId;

    public event Action?
        OpenFullPermissionsRequested;

    public PermissionToastWindow(
        NIRAScopedCapabilityAuthorizer authorizer,
        NIRACapabilityApprovalBroker broker)
    {
        InitializeComponent();

        _authorizer =
            authorizer
            ?? throw new ArgumentNullException(
                nameof(authorizer));

        _broker =
            broker
            ?? throw new ArgumentNullException(
                nameof(broker));

        _broker.Changed +=
            Broker_Changed;

        SourceInitialized +=
            PermissionToastWindow_SourceInitialized;

        Closed +=
            PermissionToastWindow_Closed;
    }

    // =========================================================
    // PUBLIC LIFECYCLE
    // =========================================================

    public void ShowPending()
    {
        if (_closed ||
            _suspended ||
            Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(ShowPending));

            return;
        }

        IReadOnlyList<NIRAApprovalRequest> pending =
            _broker.PendingRequests;

        if (pending.Count == 0)
        {
            HideWhenIdle();
            return;
        }

        RefreshRequest(
            pending);

        if (!IsVisible)
        {
            Opacity = 0.0;
            ToastTranslate.X = 6.0;
            Show();
            PlayEntrance();
        }

        Dispatcher.BeginInvoke(
            new Action(PositionNearForegroundWorkArea),
            DispatcherPriority.Loaded);
    }

    public void HideWhenIdle()
    {
        if (_closed ||
            !IsVisible ||
            _broker.PendingRequests.Count != 0)
        {
            return;
        }

        DoubleAnimation fade =
            new(
                Opacity,
                0.0,
                TimeSpan.FromMilliseconds(150))
            {
                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseIn
                    }
            };

        fade.Completed += (_, _) =>
        {
            if (!_closed &&
                _broker.PendingRequests.Count == 0)
            {
                Hide();
                Opacity = 1.0;
                _currentRequestId = null;
            }
        };

        BeginAnimation(
            OpacityProperty,
            fade);
    }

    public void SetSuspended(
        bool suspended)
    {
        _suspended =
            suspended;

        if (_closed)
        {
            return;
        }

        if (_suspended)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        ShowPending();
    }

    // =========================================================
    // BROKER
    // =========================================================

    private void Broker_Changed()
    {
        if (_closed ||
            Interlocked.Exchange(
                ref _refreshQueued,
                1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                Interlocked.Exchange(
                    ref _refreshQueued,
                    0);

                if (_closed)
                {
                    return;
                }

                ShowPending();
            }));
    }

    // =========================================================
    // REQUEST PROJECTION
    // =========================================================

    private void RefreshRequest(
        IReadOnlyList<NIRAApprovalRequest> pending)
    {
        NIRAApprovalRequest request =
            pending[0];

        NIRAAuthorityOperation operation =
            request.Operation;

        bool requestChanged =
            _currentRequestId !=
            request.Id;

        _currentRequestId =
            request.Id;

        if (requestChanged)
        {
            DetailsPanel.Visibility =
                Visibility.Collapsed;

            DetailsButton.Content =
                "Details";
        }

        PendingCountText.Text =
            pending.Count == 1
                ? "1 action is waiting"
                : $"{pending.Count} actions are waiting Â· showing the oldest";

        CapabilityText.Text =
            operation.Request.CapabilityId;

        TargetText.Text =
            string.IsNullOrWhiteSpace(
                operation.Target)
                ? operation.Request.CapabilityId
                : operation.Target;

        ReasonText.Text =
            string.IsNullOrWhiteSpace(
                operation.Request.Reason)
                ? "NIRA needs this action to continue the current work."
                : operation.Request.Reason;

        NoticeText.Text =
            operation.Notice;

        ExactPreviewText.Text =
            operation.Preview;

        RememberButton.Visibility =
            operation.CanRemember
                ? Visibility.Visible
                : Visibility.Collapsed;

        RememberButton.IsEnabled =
            operation.CanRemember;

        RememberButton.Content =
            ResolveRememberButtonText(
                operation);

        string rememberScope =
            ResolveRememberScope(
                operation);

        RememberScopeText.Text =
            operation.CanRemember
                ? rememberScope
                : "This request can only be approved once.";

        RememberHintText.Text =
            operation.CanRemember
                ? $"Remember grants: {rememberScope}"
                : "This request cannot be remembered; approval applies once.";

        StatusText.Text =
            "Waiting for your decision Â· this alert never steals keyboard focus";

        AllowOnceButton.IsEnabled =
            true;

        DenyButton.IsEnabled =
            true;

        ApplyRiskVisual(
            operation);
    }

    private static string ResolveRememberButtonText(
        NIRAAuthorityOperation operation)
    {
        if (operation.Request.CapabilityId == NIRACapabilityIds.VisionInspect &&
            operation.RequiresExplicitAuthorization)
        {
            return "Remember vision";
        }

        if (operation.Paths.Length > 0)
        {
            return "Remember folder";
        }

        if (!string.IsNullOrWhiteSpace(
                operation.Origin))
        {
            return "Remember origin";
        }

        return "Remember exact";
    }

    private static string ResolveRememberScope(
        NIRAAuthorityOperation operation)
    {
        if (operation.Request.CapabilityId == NIRACapabilityIds.VisionInspect &&
            operation.RequiresExplicitAuthorization)
        {
            return "Future grounded screenshots sent to the configured Ollama cloud vision model.";
        }

        if (operation.Paths.Length > 0)
        {
            return string.IsNullOrWhiteSpace(
                    operation.SuggestedRoot)
                ? "The suggested matching folder scope."
                : operation.SuggestedRoot;
        }

        if (!string.IsNullOrWhiteSpace(
                operation.Origin))
        {
            string method =
                string.IsNullOrWhiteSpace(
                    operation.Method)
                    ? "HTTP"
                    : operation.Method;

            return $"{method} {operation.Origin}";
        }

        return "This exact executable / command / argument set only.";
    }

    private void ApplyRiskVisual(
        NIRAAuthorityOperation operation)
    {
        Color accent;
        string label;

        if (operation.Sensitive)
        {
            accent =
                Color.FromRgb(
                    255,
                    102,
                    131);

            label =
                "SENSITIVE";
        }
        else
        {
            (accent, label) = operation.Risk switch
            {
                NIRACapabilityRisk.Destructive =>
                    (
                        Color.FromRgb(
                            255,
                            102,
                            131),
                        "DESTRUCTIVE"
                    ),

                NIRACapabilityRisk.Execute =>
                    (
                        Color.FromRgb(
                            176,
                            110,
                            255),
                        "EXECUTE"
                    ),

                NIRACapabilityRisk.Modify =>
                    (
                        Color.FromRgb(
                            77,
                            143,
                            255),
                        "MODIFY"
                    ),

                _ =>
                    (
                        Color.FromRgb(
                            120,
                            219,
                            255),
                        "OBSERVE"
                    )
            };
        }

        RiskText.Text =
            label;

        RiskText.Foreground =
            new SolidColorBrush(
                accent);

        RiskBadgeBorder.BorderBrush =
            new SolidColorBrush(
                Color.FromArgb(
                    145,
                    accent.R,
                    accent.G,
                    accent.B));

        RiskBadgeBorder.Background =
            new SolidColorBrush(
                Color.FromArgb(
                    34,
                    accent.R,
                    accent.G,
                    accent.B));

        StatusDot.Fill =
            new SolidColorBrush(
                accent);

        ToastRoot.BorderBrush =
            new SolidColorBrush(
                Color.FromArgb(
                    150,
                    accent.R,
                    accent.G,
                    accent.B));

        ToastRoot.Effect =
            new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.34
            };

        AnimateStatusDot(
            StatusDot);
    }

    // =========================================================
    // DECISION
    // =========================================================

    private void Resolve(
        NIRAApprovalChoice choice)
    {
        NIRAApprovalRequest? request =
            _broker.PendingRequests
                .FirstOrDefault(
                    item =>
                        _currentRequestId.HasValue &&
                        item.Id ==
                        _currentRequestId.Value);

        if (request == null)
        {
            ShowPending();
            return;
        }

        try
        {
            NIRAApprovalResponse response =
                new()
                {
                    Choice = choice,
                    FolderRoot =
                        request.Operation.SuggestedRoot,
                    Label =
                        request.Operation.Request.CapabilityId
                };

            if (choice ==
                NIRAApprovalChoice.Remember)
            {
                // Validate the suggested remembered scope before resolving the
                // broker. AuthorizeAsync remains authoritative and persists it.
                _authorizer.BuildRememberedScope(
                    request.Operation,
                    response);
            }

            bool accepted =
                _broker.Resolve(
                    request.Id,
                    response);

            AllowOnceButton.IsEnabled =
                false;

            RememberButton.IsEnabled =
                false;

            DenyButton.IsEnabled =
                false;

            StatusText.Text =
                accepted
                    ? "Decision sent to NIRA."
                    : "That request already ended.";
        }
        catch (Exception ex)
        {
            StatusText.Text =
                ex.Message;

            AllowOnceButton.IsEnabled =
                true;

            RememberButton.IsEnabled =
                request.Operation.CanRemember;

            DenyButton.IsEnabled =
                true;
        }
    }

    private void AllowOnce_Click(
        object sender,
        RoutedEventArgs e) =>
        Resolve(
            NIRAApprovalChoice.AllowOnce);

    private void Remember_Click(
        object sender,
        RoutedEventArgs e) =>
        Resolve(
            NIRAApprovalChoice.Remember);

    private void Deny_Click(
        object sender,
        RoutedEventArgs e) =>
        Resolve(
            NIRAApprovalChoice.Deny);

    // =========================================================
    // DETAILS
    // =========================================================

    private void DetailsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        bool opening =
            DetailsPanel.Visibility !=
            Visibility.Visible;

        DetailsPanel.Visibility =
            opening
                ? Visibility.Visible
                : Visibility.Collapsed;

        DetailsButton.Content =
            opening
                ? "Less"
                : "Details";

        Dispatcher.BeginInvoke(
            new Action(PositionNearForegroundWorkArea),
            DispatcherPriority.Loaded);
    }

    private void OpenFullPermissions_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenFullPermissionsRequested?
            .Invoke();
    }

    // =========================================================
    // ENTRANCE / PULSE
    // =========================================================

    private void PlayEntrance()
    {
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(
                0.0,
                1.0,
                TimeSpan.FromMilliseconds(
                    170))
            {
                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseOut
                    }
            });

        ToastTranslate.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(
                6.0,
                0.0,
                TimeSpan.FromMilliseconds(
                    180))
            {
                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseOut
                    }
            });

        NIRAMotion.BeginPulseOpacity(
            ToastRoot,
            0.96,
            1.0,
            3.2);
    }

    private static void AnimateStatusDot(
        FrameworkElement dot)
    {
        dot.BeginAnimation(OpacityProperty, null);
        dot.Opacity = 0.92;

        NIRAMotion.BeginPulseOpacity(
            dot,
            0.40,
            1.0,
            1.3);
    }

    // =========================================================
    // NON-ACTIVATING WINDOW
    // =========================================================

    private void PermissionToastWindow_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        IntPtr handle =
            new WindowInteropHelper(
                this)
                .Handle;

        if (handle ==
            IntPtr.Zero)
        {
            return;
        }

        IntPtr style =
            GetWindowLongPtr(
                handle,
                GwlExStyle);

        long value =
            style.ToInt64() |
            WsExNoActivate |
            WsExToolWindow;

        SetWindowLongPtr(
            handle,
            GwlExStyle,
            new IntPtr(
                value));
    }

    // =========================================================
    // BOTTOM-RIGHT PLACEMENT
    // =========================================================

    private void PositionNearForegroundWorkArea()
    {
        if (_closed ||
            !IsVisible)
        {
            return;
        }

        IntPtr handle =
            new WindowInteropHelper(
                this)
                .Handle;

        if (handle ==
            IntPtr.Zero)
        {
            return;
        }

        IntPtr foreground =
            GetForegroundWindow();

        IntPtr reference =
            foreground == IntPtr.Zero
                ? handle
                : foreground;

        IntPtr monitor =
            MonitorFromWindow(
                reference,
                MonitorDefaultToNearest);

        MonitorInfo info =
            new()
            {
                Size =
                    Marshal.SizeOf<MonitorInfo>()
            };

        if (monitor == IntPtr.Zero ||
            !GetMonitorInfo(
                monitor,
                ref info) ||
            !GetWindowRect(
                handle,
                out Rect nativeBounds))
        {
            return;
        }

        int width =
            Math.Max(
                1,
                nativeBounds.Right -
                nativeBounds.Left);

        int height =
            Math.Max(
                1,
                nativeBounds.Bottom -
                nativeBounds.Top);

        const int edgeMargin =
            18;

        int left =
            info.WorkArea.Right -
            width -
            edgeMargin;

        int top =
            info.WorkArea.Bottom -
            height -
            edgeMargin;

        SetWindowPos(
            handle,
            HwndTopmost,
            left,
            top,
            0,
            0,
            SwpNoSize |
            SwpNoActivate |
            SwpShowWindow);
    }

    // =========================================================
    // CLOSE
    // =========================================================

    private void PermissionToastWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _closed =
            true;

        _broker.Changed -=
            Broker_Changed;

        SourceInitialized -=
            PermissionToastWindow_SourceInitialized;

        Closed -=
            PermissionToastWindow_Closed;
    }

    // =========================================================
    // WIN32
    // =========================================================

    private const int GwlExStyle =
        -20;

    private const long WsExToolWindow =
        0x00000080L;

    private const long WsExNoActivate =
        0x08000000L;

    private const uint MonitorDefaultToNearest =
        0x00000002;

    private const uint SwpNoSize =
        0x0001;

    private const uint SwpNoActivate =
        0x0010;

    private const uint SwpShowWindow =
        0x0040;

    private static readonly IntPtr HwndTopmost =
        new(-1);

    [StructLayout(
        LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport(
        "user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr handle,
        uint flags);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Auto)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInfo info);

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr handle,
        out Rect rect);

    [DllImport(
        "user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private static IntPtr GetWindowLongPtr(
        IntPtr handle,
        int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(
                handle,
                index)
            : new IntPtr(
                GetWindowLong32(
                    handle,
                    index));
    }

    private static IntPtr SetWindowLongPtr(
        IntPtr handle,
        int index,
        IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(
                handle,
                index,
                value)
            : new IntPtr(
                SetWindowLong32(
                    handle,
                    index,
                    value.ToInt32()));
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(
        IntPtr handle,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(
        IntPtr handle,
        int index,
        int value);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr handle,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr handle,
        int index,
        IntPtr value);
}

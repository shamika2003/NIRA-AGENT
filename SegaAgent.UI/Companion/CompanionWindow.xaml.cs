/*
 * filename: CompanionWindow.xaml.cs
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using SegaAgent.Agent.State;
using SegaAgent.Embodiment;
using SegaAgent.Embodiment.Body;
using SegaAgent.PC.Awareness;
using SegaAgent.Settings;
using SegaAgent.UI;

namespace SegaAgent.UI.Companion;

public partial class CompanionWindow
    : Window
{
    // =========================================================
    // SERVICES
    // =========================================================

    private readonly SegaStateService
        _segaState;


    private readonly SegaVisualIntentService
        _visualIntent;


    private readonly SegaPresenceService
        _presence;


    private readonly SegaBodyCommandService
        _bodyCommands;


    private readonly SegaBodyPlacementService
        _placement;


    private readonly SegaRuntimeSettingsService
        _settings;


    // =========================================================
    // STATE
    // =========================================================

    private SegaStateSnapshot
        _stateSnapshot;


    // =========================================================
    // IDLE FADE
    // =========================================================

    private readonly DispatcherTimer
        _idleFadeTimer;


    private DateTime
        _lastActivityTime =
            DateTime.UtcNow;


    private bool
        _isFaded;


    // =========================================================
    // USER DRAG
    // =========================================================

    private bool
        _dragging;


    // =========================================================
    // PROGRAMMATIC MOVEMENT
    // =========================================================

    private readonly object
        _motionSync =
            new();


    private CancellationTokenSource?
        _motionCancellation;


    // =========================================================
    // MAIN WINDOW BODY DOCK
    //
    // Sega remains one physical WPF body. When the main UI is
    // active, this same transparent CompanionWindow moves into
    // the UI's dock location. It is never faded out and replaced
    // by a second particle renderer.
    // =========================================================

    private MainWindow?
        _mainWindow;


    private PcRectangle?
        _desktopReturnBounds;


    private bool
        _isDockedToMainWindow;


    private const int MainDockDurationMilliseconds =
        420;


    private const int MainReturnDurationMilliseconds =
        360;


    // =========================================================
    // STARTUP
    // =========================================================

    private bool
        _startupPlacementApplied;


    private bool
        _startupEntrancePlayed;


    // =========================================================
    // BODY GEOMETRY
    //
    // Keep this aligned with ParticleEntityControl's hit-test
    // radius so world reasoning uses Sega's meaningful body.
    // =========================================================

    private const double BodyRadiusFactor =
        0.38;


    // =========================================================
    // FADE
    // =========================================================

    private const double FadedOpacity =
        0.22;


    private const double NormalOpacity =
        1.0;


    private const int FadeDurationMilliseconds =
        900;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public CompanionWindow(
        SegaStateService segaState,
        SegaVisualIntentService visualIntent,
        SegaPresenceService presence,
        SegaBodyCommandService bodyCommands,
        SegaBodyPlacementService placement,
        SegaRuntimeSettingsService settings)
    {
        InitializeComponent();


        _segaState =
            segaState
            ?? throw new ArgumentNullException(
                nameof(segaState));


        _visualIntent =
            visualIntent
            ?? throw new ArgumentNullException(
                nameof(visualIntent));


        _presence =
            presence
            ?? throw new ArgumentNullException(
                nameof(presence));


        _bodyCommands =
            bodyCommands
            ?? throw new ArgumentNullException(
                nameof(bodyCommands));


        _placement =
            placement
            ?? throw new ArgumentNullException(
                nameof(placement));


        _settings =
            settings
            ?? throw new ArgumentNullException(
                nameof(settings));


        _settings.Changed +=
            RuntimeSettings_Changed;


        _stateSnapshot =
            _segaState.Current;


        // =====================================================
        // STATE
        // =====================================================

        _segaState.StateChanged +=
            SegaState_StateChanged;


        _visualIntent.IntentChanged +=
            VisualIntent_IntentChanged;


        // =====================================================
        // BODY COMMANDS
        // =====================================================

        _bodyCommands.CommandIssued +=
            BodyCommands_CommandIssued;


        // =====================================================
        // IDLE FADE
        // =====================================================

        _idleFadeTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        500)
            };


        _idleFadeTimer.Tick +=
            IdleFadeTimer_Tick;


        // =====================================================
        // WINDOW
        // =====================================================

        Loaded +=
            CompanionWindow_Loaded;


        Closed +=
            CompanionWindow_Closed;


        LocationChanged +=
            CompanionWindow_LocationChanged;


        SizeChanged +=
            CompanionWindow_SizeChanged;


        IsVisibleChanged +=
            CompanionWindow_IsVisibleChanged;


        // =====================================================
        // ENTITY INPUT
        // =====================================================

        Entity.MouseLeftButtonDown +=
            Entity_MouseLeftButtonDown;


        Entity.MouseRightButtonUp +=
            Entity_MouseRightButtonUp;


        // =====================================================
        // STARTUP MATERIALIZATION
        //
        // Prepare before the first visible render so Sega never
        // flashes as an already-formed orb.
        // =====================================================

        Entity.PrepareStartupEntrance();
    }


    // =========================================================
    // ATTACH MAIN WINDOW
    // =========================================================

    public void AttachToMainWindow(
        MainWindow mainWindow)
    {
        ArgumentNullException.ThrowIfNull(
            mainWindow);

        if (ReferenceEquals(
                _mainWindow,
                mainWindow))
        {
            if (mainWindow.IsActive)
            {
                Dispatcher.BeginInvoke(
                    new Action(
                        DockToMainWindow));
            }

            return;
        }

        DetachMainWindowEvents();

        _mainWindow =
            mainWindow;

        _mainWindow.Activated +=
            MainWindow_Activated;

        _mainWindow.Deactivated +=
            MainWindow_Deactivated;

        _mainWindow.LocationChanged +=
            MainWindow_LocationChanged;

        _mainWindow.SizeChanged +=
            MainWindow_SizeChanged;

        _mainWindow.StateChanged +=
            MainWindow_StateChanged;

        _mainWindow.IsVisibleChanged +=
            MainWindow_IsVisibleChanged;

        _mainWindow.Closed +=
            MainWindow_Closed;

        if (_mainWindow.IsActive)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    DockToMainWindow));
        }
        else if (!_settings.Current.DesktopPresenceEnabled)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                    ApplyDesktopPresenceSetting(false)));
        }
    }


    // =========================================================
    // MAIN WINDOW EVENTS
    // =========================================================

    private void MainWindow_Activated(
        object? sender,
        EventArgs e)
    {
        DockToMainWindow();
    }


    private void MainWindow_Deactivated(
        object? sender,
        EventArgs e)
    {
        ReturnFromMainWindow();
    }


    private void MainWindow_LocationChanged(
        object? sender,
        EventArgs e)
    {
        if (_isDockedToMainWindow)
        {
            SnapToMainWindowDock();
        }
    }


    private void MainWindow_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        if (_isDockedToMainWindow)
        {
            SnapToMainWindowDock();
        }
    }


    private void MainWindow_StateChanged(
        object? sender,
        EventArgs e)
    {
        if (_mainWindow ==
            null)
        {
            return;
        }

        if (
            _mainWindow.WindowState ==
                WindowState.Minimized
            ||
            !_mainWindow.IsVisible)
        {
            ReturnFromMainWindow();

            return;
        }

        if (_mainWindow.IsActive)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    SnapToMainWindowDock));
        }
    }


    private void MainWindow_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_mainWindow ==
            null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            ReturnFromMainWindow();

            return;
        }

        if (_mainWindow.IsActive)
        {
            Dispatcher.BeginInvoke(
                new Action(
                    DockToMainWindow));
        }
    }


    private void MainWindow_Closed(
        object? sender,
        EventArgs e)
    {
        ReturnFromMainWindow();

        DetachMainWindowEvents();
    }


    // =========================================================
    // DOCK TO MAIN WINDOW
    // =========================================================

    private void DockToMainWindow()
    {
        if (
            _mainWindow ==
                null
            ||
            !_mainWindow.IsVisible
            ||
            _mainWindow.WindowState ==
                WindowState.Minimized)
        {
            return;
        }

        if (!TryResolveMainWindowDockTarget(
                out int targetLeft,
                out int targetTop))
        {
            return;
        }

        if (!_isDockedToMainWindow)
        {
            ReportPresence();

            SegaPresenceSnapshot presence =
                _presence.Current;

            if (presence.IsAvailable)
            {
                _desktopReturnBounds =
                    presence.WindowBounds;
            }

            _isDockedToMainWindow =
                true;

            ForceVisibleForMainDock();

            Debug.WriteLine(
                $"[CompanionDock] ENTER | " +
                $"Target=({targetLeft},{targetTop})");

            StartProgrammaticMovement(
                SegaBodyCommand.MoveTo(
                    targetLeft,
                    targetTop,
                    TimeSpan.FromMilliseconds(
                        MainDockDurationMilliseconds),
                    SegaBodyCommandSource.System));

            return;
        }

        SnapToMainWindowDock();
    }


    // =========================================================
    // SNAP WHILE MAIN WINDOW MOVES / RESIZES
    // =========================================================

    private void SnapToMainWindowDock()
    {
        if (
            !_isDockedToMainWindow
            ||
            !TryResolveMainWindowDockTarget(
                out int targetLeft,
                out int targetTop))
        {
            return;
        }

        CancelProgrammaticMovement();

        IntPtr handle =
            new WindowInteropHelper(
                this)
                .Handle;

        if (handle ==
            IntPtr.Zero)
        {
            return;
        }

        SetWindowPosition(
            handle,
            targetLeft,
            targetTop);

        ReportPresence();
    }


    // =========================================================
    // RETURN TO DESKTOP
    // =========================================================

    private void ReturnFromMainWindow()
    {
        if (!_isDockedToMainWindow)
        {
            return;
        }

        _isDockedToMainWindow =
            false;

        PcRectangle? returnBounds =
            _desktopReturnBounds;

        if (!_settings.Current.DesktopPresenceEnabled)
        {
            CancelProgrammaticMovement();

            BeginAnimation(
                OpacityProperty,
                null);

            Opacity =
                NormalOpacity;

            _isFaded =
                false;

            if (IsVisible)
            {
                Hide();
            }

            _presence.SetVisualState(
                false,
                false);

            return;
        }

        _desktopReturnBounds =
            null;

        if (
            returnBounds ==
                null
            ||
            returnBounds.Value.IsEmpty)
        {
            return;
        }

        PcRectangle safe =
            _placement
                .ClampToAvailableDisplay(
                    returnBounds.Value);

        ForceVisibleForMainDock();

        Debug.WriteLine(
            $"[CompanionDock] RETURN | " +
            $"Target=({safe.Left},{safe.Top})");

        StartProgrammaticMovement(
            SegaBodyCommand.MoveTo(
                safe.Left,
                safe.Top,
                TimeSpan.FromMilliseconds(
                    MainReturnDurationMilliseconds),
                SegaBodyCommandSource.System));
    }


    // =========================================================
    // RESOLVE DOCK TARGET
    // =========================================================

    private bool TryResolveMainWindowDockTarget(
        out int targetLeft,
        out int targetTop)
    {
        targetLeft =
            0;

        targetTop =
            0;

        if (
            _mainWindow ==
                null
            ||
            !_mainWindow.TryGetBodyDockScreenCenter(
                out Point center))
        {
            return false;
        }

        if (!TryGetPhysicalWindowSize(
                out int width,
                out int height))
        {
            return false;
        }

        targetLeft =
            (int)Math.Round(
                center.X -
                width /
                    2.0);

        targetTop =
            (int)Math.Round(
                center.Y -
                height /
                    2.0);

        return true;
    }


    // =========================================================
    // PHYSICAL WINDOW SIZE
    // =========================================================

    private bool TryGetPhysicalWindowSize(
        out int width,
        out int height)
    {
        width =
            0;

        height =
            0;

        if (
            !IsLoaded
            ||
            ActualWidth <=
                0.0
            ||
            ActualHeight <=
                0.0)
        {
            return false;
        }

        try
        {
            Point topLeft =
                PointToScreen(
                    new Point(
                        0.0,
                        0.0));

            Point bottomRight =
                PointToScreen(
                    new Point(
                        ActualWidth,
                        ActualHeight));

            width =
                Math.Max(
                    1,
                    (int)Math.Round(
                        Math.Abs(
                            bottomRight.X -
                            topLeft.X)));

            height =
                Math.Max(
                    1,
                    (int)Math.Round(
                        Math.Abs(
                            bottomRight.Y -
                            topLeft.Y)));

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }


    // =========================================================
    // DOCK VISIBILITY
    //
    // Docking never uses the old idle fade. The body stays fully
    // present during the physical move into and out of the UI.
    // =========================================================

    private void ForceVisibleForMainDock()
    {
        BeginAnimation(
            OpacityProperty,
            null);

        Opacity =
            NormalOpacity;

        _isFaded =
            false;

        if (!IsVisible)
        {
            Show();
        }

        Topmost =
            true;

        _presence.SetVisualState(
            true,
            false);

        _lastActivityTime =
            DateTime.UtcNow;
    }


    // =========================================================
    // DETACH MAIN WINDOW EVENTS
    // =========================================================

    private void DetachMainWindowEvents()
    {
        if (_mainWindow ==
            null)
        {
            return;
        }

        _mainWindow.Activated -=
            MainWindow_Activated;

        _mainWindow.Deactivated -=
            MainWindow_Deactivated;

        _mainWindow.LocationChanged -=
            MainWindow_LocationChanged;

        _mainWindow.SizeChanged -=
            MainWindow_SizeChanged;

        _mainWindow.StateChanged -=
            MainWindow_StateChanged;

        _mainWindow.IsVisibleChanged -=
            MainWindow_IsVisibleChanged;

        _mainWindow.Closed -=
            MainWindow_Closed;

        _mainWindow =
            null;
    }


    // =========================================================
    // LOADED
    // =========================================================

    private void CompanionWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!_startupPlacementApplied)
        {
            RestoreStartupPlacement();


            _startupPlacementApplied =
                true;
        }


        Opacity =
            NormalOpacity;


        _lastActivityTime =
            DateTime.UtcNow;


        _isFaded =
            false;


        ApplySegaState(
            _segaState.Current);


        ApplyVisualIntent(
            _visualIntent.Current);


        ReportPresence();


        if (!_startupEntrancePlayed)
        {
            _startupEntrancePlayed =
                true;


            Entity.StartStartupEntrance();
        }


        if (!_idleFadeTimer.IsEnabled)
        {
            _idleFadeTimer.Start();
        }
    }


    // =========================================================
    // SEGA STATE
    // =========================================================

    private void SegaState_StateChanged(
        SegaStateSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplySegaState(
                    snapshot);
            });


            return;
        }


        ApplySegaState(
            snapshot);
    }


    // =========================================================
    // APPLY STATE
    // =========================================================

    private void ApplySegaState(
        SegaStateSnapshot snapshot)
    {
        _stateSnapshot =
            snapshot;


        if (
            snapshot.Mind !=
                SegaMindState.Idle
            ||
            snapshot.Body !=
                SegaBodyState.Resting)
        {
            RegisterActivity();
        }
    }


    // =========================================================
    // VISUAL INTENT
    // =========================================================

    private void VisualIntent_IntentChanged(
        SegaVisualIntent intent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyVisualIntent(
                    intent);
            });


            return;
        }


        ApplyVisualIntent(
            intent);
    }


    // =========================================================
    // APPLY VISUAL INTENT
    // =========================================================

    private void ApplyVisualIntent(
        SegaVisualIntent intent)
    {
        Entity.SetIntent(
            intent);


        if (
            intent.Source !=
            SegaVisualIntentSource.Automatic)
        {
            RegisterActivity();
        }
    }


    // =========================================================
    // STARTUP PLACEMENT
    // =========================================================

    private void RestoreStartupPlacement()
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


        Point topLeft =
            PointToScreen(
                new Point(
                    0.0,
                    0.0));


        Point bottomRight =
            PointToScreen(
                new Point(
                    ActualWidth,
                    ActualHeight));


        int width =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Abs(
                        bottomRight.X -
                        topLeft.X)));


        int height =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Abs(
                        bottomRight.Y -
                        topLeft.Y)));


        PcRectangle startup =
            _placement
                .ResolveStartupBounds(
                    width,
                    height);


        SetWindowPosition(
            handle,
            startup.Left,
            startup.Top);
    }


    // =========================================================
    // PRESENCE REPORTING
    // =========================================================

    private void ReportPresence()
    {
        if (
            !IsLoaded
            ||
            ActualWidth <=
                0
            ||
            ActualHeight <=
                0
            ||
            Entity.ActualWidth <=
                0
            ||
            Entity.ActualHeight <=
                0)
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


        // =====================================================
        // FULL COMPANION WINDOW
        // =====================================================

        Point windowTopLeft =
            PointToScreen(
                new Point(
                    0.0,
                    0.0));


        Point windowBottomRight =
            PointToScreen(
                new Point(
                    ActualWidth,
                    ActualHeight));


        PcRectangle windowBounds =
            ToPcRectangle(
                windowTopLeft,
                windowBottomRight);


        // =====================================================
        // INTERACTIVE ORB BODY
        // =====================================================

        double centerX =
            Entity.ActualWidth *
            0.5;


        double centerY =
            Entity.ActualHeight *
            0.5;


        double radius =
            Math.Min(
                Entity.ActualWidth,
                Entity.ActualHeight)
            *
            BodyRadiusFactor;


        Point bodyTopLeft =
            Entity.PointToScreen(
                new Point(
                    centerX -
                        radius,
                    centerY -
                        radius));


        Point bodyBottomRight =
            Entity.PointToScreen(
                new Point(
                    centerX +
                        radius,
                    centerY +
                        radius));


        PcRectangle bodyBounds =
            ToPcRectangle(
                bodyTopLeft,
                bodyBottomRight);


        _presence.Report(
            handle,
            windowBounds,
            bodyBounds,
            IsVisible,
            _isFaded);
    }


    // =========================================================
    // WINDOW GEOMETRY EVENTS
    // =========================================================

    private void CompanionWindow_LocationChanged(
        object? sender,
        EventArgs e)
    {
        ReportPresence();
    }


    private void CompanionWindow_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        ReportPresence();
    }


    private void CompanionWindow_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }


        if (IsVisible)
        {
            ReportPresence();


            return;
        }


        _presence.SetVisualState(
            false,
            _isFaded);
    }


    // =========================================================
    // SCREEN RECTANGLE
    // =========================================================

    private static PcRectangle ToPcRectangle(
        Point topLeft,
        Point bottomRight)
    {
        return new PcRectangle(
            (int)Math.Floor(
                Math.Min(
                    topLeft.X,
                    bottomRight.X)),

            (int)Math.Floor(
                Math.Min(
                    topLeft.Y,
                    bottomRight.Y)),

            (int)Math.Ceiling(
                Math.Max(
                    topLeft.X,
                    bottomRight.X)),

            (int)Math.Ceiling(
                Math.Max(
                    topLeft.Y,
                    bottomRight.Y)));
    }


    // =========================================================
    // BODY COMMAND
    // =========================================================

    private void BodyCommands_CommandIssued(
        SegaBodyCommand command)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyBodyCommand(
                    command);
            });


            return;
        }


        ApplyBodyCommand(
            command);
    }


    // =========================================================
    // APPLY BODY COMMAND
    // =========================================================

    private void ApplyBodyCommand(
        SegaBodyCommand command)
    {
        switch (command.Type)
        {
            case SegaBodyCommandType.Hide:
            {
                CancelProgrammaticMovement();

                if (IsVisible)
                {
                    Hide();
                }

                break;
            }


            case SegaBodyCommandType.Show:
            {
                if (
                    !_settings.Current.DesktopPresenceEnabled
                    &&
                    !_isDockedToMainWindow)
                {
                    break;
                }

                if (!IsVisible)
                {
                    Show();

                    Topmost =
                        true;

                    ReportPresence();
                }

                break;
            }


            case SegaBodyCommandType.MoveToScreenPosition:
            {
                if (
                    !_settings.Current.DesktopPresenceEnabled
                    ||
                    _dragging
                    ||
                    _isDockedToMainWindow)
                {
                    return;
                }

                StartProgrammaticMovement(
                    command);

                break;
            }


            default:
                throw new ArgumentOutOfRangeException();
        }
    }


    // =========================================================
    // START PROGRAMMATIC MOVEMENT
    // =========================================================

    private void StartProgrammaticMovement(
        SegaBodyCommand command)
    {
        CancellationTokenSource cancellation =
            new();


        lock (_motionSync)
        {
            _motionCancellation?
                .Cancel();


            _motionCancellation =
                cancellation;
        }


        _ =
            MoveWindowAsync(
                command,
                cancellation);
    }


    // =========================================================
    // CANCEL PROGRAMMATIC MOVEMENT
    // =========================================================

    private void CancelProgrammaticMovement()
    {
        lock (_motionSync)
        {
            _motionCancellation?
                .Cancel();
        }
    }


    // =========================================================
    // MOVE WINDOW
    // =========================================================

    private async Task MoveWindowAsync(
        SegaBodyCommand command,
        CancellationTokenSource cancellation)
    {
        IntPtr handle =
            new WindowInteropHelper(
                this)
                .Handle;


        if (handle ==
            IntPtr.Zero)
        {
            ReleaseMotion(
                cancellation);


            return;
        }


        SegaPresenceSnapshot presence =
            _presence.Current;


        if (!presence.IsAvailable)
        {
            ReleaseMotion(
                cancellation);


            return;
        }


        int startLeft =
            presence.WindowBounds.Left;


        int startTop =
            presence.WindowBounds.Top;


        PcRectangle requestedTarget =
            new(
                command.ScreenLeft,
                command.ScreenTop,
                command.ScreenLeft +
                    presence.WindowBounds.Width,
                command.ScreenTop +
                    presence.WindowBounds.Height);


        PcRectangle safeTarget =
            _placement
                .ClampToAvailableDisplay(
                    requestedTarget);


        int targetLeft =
            safeTarget.Left;


        int targetTop =
            safeTarget.Top;


        double duration =
            Math.Clamp(
                command.Duration.TotalSeconds,
                0.05,
                2.0);


        _segaState.SetMoving(
            true);


        Stopwatch stopwatch =
            Stopwatch.StartNew();


        try
        {
            while (true)
            {
                cancellation
                    .Token
                    .ThrowIfCancellationRequested();


                double progress =
                    Math.Clamp(
                        stopwatch.Elapsed.TotalSeconds /
                            duration,
                        0.0,
                        1.0);


                double eased =
                    progress *
                    progress *
                    (
                        3.0 -
                        2.0 *
                        progress
                    );


                int currentLeft =
                    (int)Math.Round(
                        startLeft +
                        (
                            targetLeft -
                            startLeft
                        )
                        *
                        eased);


                int currentTop =
                    (int)Math.Round(
                        startTop +
                        (
                            targetTop -
                            startTop
                        )
                        *
                        eased);


                SetWindowPosition(
                    handle,
                    currentLeft,
                    currentTop);


                if (progress >=
                    1.0)
                {
                    break;
                }


                await Task.Delay(
                    16,
                    cancellation.Token);
            }


            SetWindowPosition(
                handle,
                targetLeft,
                targetTop);


            ReportPresence();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReleaseMotion(
                    cancellation))
            {
                _segaState.SetMoving(
                    false);
            }
        }
    }


    // =========================================================
    // RELEASE MOTION
    // =========================================================

    private bool ReleaseMotion(
        CancellationTokenSource cancellation)
    {
        bool ownsMovement =
            false;


        lock (_motionSync)
        {
            if (ReferenceEquals(
                    _motionCancellation,
                    cancellation))
            {
                _motionCancellation =
                    null;


                ownsMovement =
                    true;
            }
        }


        cancellation.Dispose();


        return ownsMovement;
    }


    private void RuntimeSettings_Changed(
        SegaRuntimeSettings before,
        SegaRuntimeSettings after)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                    ApplyRuntimeSettingsChange(
                        before,
                        after)));

            return;
        }

        ApplyRuntimeSettingsChange(
            before,
            after);
    }


    private void ApplyRuntimeSettingsChange(
        SegaRuntimeSettings before,
        SegaRuntimeSettings after)
    {
        if (before.DesktopPresenceEnabled !=
            after.DesktopPresenceEnabled)
        {
            ApplyDesktopPresenceSetting(
                after.DesktopPresenceEnabled);
        }

        if (
            !after.IdleBehaviorEnabled
            ||
            !after.FadeDesktopPresenceWhenIdle
            ||
            before.IdleFadeDelaySeconds !=
                after.IdleFadeDelaySeconds)
        {
            RegisterActivity();
        }
    }


    private void ApplyDesktopPresenceSetting(
        bool enabled)
    {
        if (_isDockedToMainWindow)
        {
            ForceVisibleForMainDock();
            return;
        }

        if (!enabled)
        {
            if (IsVisible)
            {
                ReportPresence();

                SegaPresenceSnapshot presence =
                    _presence.Current;

                if (presence.IsAvailable)
                {
                    _desktopReturnBounds =
                        presence.WindowBounds;
                }
            }

            CancelProgrammaticMovement();

            BeginAnimation(
                OpacityProperty,
                null);

            Opacity =
                NormalOpacity;

            _isFaded =
                false;

            if (IsVisible)
            {
                Hide();
            }

            _presence.SetVisualState(
                false,
                false);

            return;
        }

        ForceVisibleForMainDock();

        if (
            _desktopReturnBounds is PcRectangle returnBounds
            &&
            !returnBounds.IsEmpty)
        {
            PcRectangle safe =
                _placement
                    .ClampToAvailableDisplay(
                        returnBounds);

            IntPtr handle =
                new WindowInteropHelper(
                    this)
                    .Handle;

            if (handle !=
                IntPtr.Zero)
            {
                SetWindowPosition(
                    handle,
                    safe.Left,
                    safe.Top);
            }

            _desktopReturnBounds =
                null;
        }

        ReportPresence();
    }


    // =========================================================
    // IDLE FADE
    // =========================================================

    private void IdleFadeTimer_Tick(
        object? sender,
        EventArgs e)
    {
        SegaRuntimeSettings settings =
            _settings.Current;

        if (
            !settings.IdleBehaviorEnabled
            ||
            !settings.FadeDesktopPresenceWhenIdle)
        {
            RestoreFromFade();
            return;
        }


        if (_isDockedToMainWindow)
        {
            return;
        }


        if (!IsVisible)
        {
            return;
        }


        if (_dragging)
        {
            RestoreFromFade();


            return;
        }


        if (
            _stateSnapshot.Mind !=
                SegaMindState.Idle
            ||
            _stateSnapshot.Body !=
                SegaBodyState.Resting)
        {
            RestoreFromFade();


            return;
        }


        TimeSpan idleTime =
            DateTime.UtcNow -
            _lastActivityTime;


        if (idleTime <
            TimeSpan.FromSeconds(
                settings.IdleFadeDelaySeconds))
        {
            return;
        }


        FadeToQuiet();
    }


    // =========================================================
    // ACTIVITY
    // =========================================================

    private void RegisterActivity()
    {
        _lastActivityTime =
            DateTime.UtcNow;


        RestoreFromFade();
    }


    // =========================================================
    // FADE
    // =========================================================

    private void FadeToQuiet()
    {
        if (_isFaded)
        {
            return;
        }


        _isFaded =
            true;


        _presence.SetVisualState(
            IsVisible,
            true);


        DoubleAnimation animation =
            new()
            {
                To =
                    FadedOpacity,

                Duration =
                    TimeSpan.FromMilliseconds(
                        FadeDurationMilliseconds),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseInOut
                    }
            };


        BeginAnimation(
            OpacityProperty,
            animation);
    }


    // =========================================================
    // RESTORE
    // =========================================================

    private void RestoreFromFade()
    {
        if (
            !_isFaded
            &&
            Opacity >=
                NormalOpacity)
        {
            return;
        }


        _isFaded =
            false;


        _presence.SetVisualState(
            IsVisible,
            false);


        DoubleAnimation animation =
            new()
            {
                To =
                    NormalOpacity,

                Duration =
                    TimeSpan.FromMilliseconds(
                        FadeDurationMilliseconds),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseInOut
                    }
            };


        BeginAnimation(
            OpacityProperty,
            animation);
    }


    // =========================================================
    // LEFT CLICK / DRAG
    // =========================================================

    private void Entity_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (
            e.ChangedButton !=
            MouseButton.Left)
        {
            return;
        }


        if (_isDockedToMainWindow)
        {
            RegisterActivity();

            _mainWindow?.Activate();

            e.Handled =
                true;

            return;
        }


        CancelProgrammaticMovement();


        RegisterActivity();


        _dragging =
            true;


        _segaState.SetDragging(
            true);


        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            ClampAndRememberUserPlacement();


            _dragging =
                false;


            _segaState.SetDragging(
                false);
        }


        e.Handled =
            true;
    }


    // =========================================================
    // CLAMP AND REMEMBER USER PLACEMENT
    // =========================================================

    private void ClampAndRememberUserPlacement()
    {
        ReportPresence();


        SegaPresenceSnapshot presence =
            _presence.Current;


        if (!presence.IsAvailable)
        {
            return;
        }


        PcRectangle safe =
            _placement
                .ClampToAvailableDisplay(
                    presence.WindowBounds);


        IntPtr handle =
            new WindowInteropHelper(
                this)
                .Handle;


        SetWindowPosition(
            handle,
            safe.Left,
            safe.Top);


        ReportPresence();


        SegaPresenceSnapshot updated =
            _presence.Current;


        if (updated.IsAvailable)
        {
            _placement.SavePreferred(
                updated.WindowBounds);
        }
    }


    // =========================================================
    // RIGHT CLICK
    // =========================================================

    private void Entity_MouseRightButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        RegisterActivity();


        OpenMainWindow();


        e.Handled =
            true;
    }


    // =========================================================
    // OPEN MAIN WINDOW
    // =========================================================

    private static void OpenMainWindow()
    {
        if (
            Application.Current ==
            null)
        {
            return;
        }


        if (
            Application.Current.MainWindow
            is not MainWindow mainWindow)
        {
            return;
        }


        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }


        if (
            mainWindow.WindowState ==
            WindowState.Minimized)
        {
            mainWindow.WindowState =
                WindowState.Normal;
        }


        mainWindow.Visibility =
            Visibility.Visible;


        mainWindow.Topmost =
            true;


        mainWindow.Activate();


        mainWindow.Focus();


        mainWindow.Topmost =
            false;
    }


    // =========================================================
    // NATIVE WINDOW POSITION
    //
    // Body commands operate in physical desktop pixels.
    // SetWindowPos avoids WPF DPI conversion problems when Sega
    // moves across monitors with different scale factors.
    // =========================================================

    private static void SetWindowPosition(
        IntPtr handle,
        int left,
        int top)
    {
        if (handle ==
            IntPtr.Zero)
        {
            return;
        }


        bool success =
            SetWindowPos(
                handle,
                IntPtr.Zero,
                left,
                top,
                0,
                0,
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate);


        if (!success)
        {
            Debug.WriteLine(
                $"[Companion] " +
                $"SetWindowPos failed. " +
                $"Error={Marshal.GetLastWin32Error()}");
        }
    }


    // =========================================================
    // WIN32
    // =========================================================

    private const uint SwpNoSize =
        0x0001;


    private const uint SwpNoZOrder =
        0x0004;


    private const uint SwpNoActivate =
        0x0010;


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);


    // =========================================================
    // CLOSED
    // =========================================================

    private void CompanionWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _idleFadeTimer.Stop();


        _idleFadeTimer.Tick -=
            IdleFadeTimer_Tick;


        _segaState.StateChanged -=
            SegaState_StateChanged;


        _visualIntent.IntentChanged -=
            VisualIntent_IntentChanged;


        _bodyCommands.CommandIssued -=
            BodyCommands_CommandIssued;


        _settings.Changed -=
            RuntimeSettings_Changed;


        LocationChanged -=
            CompanionWindow_LocationChanged;


        SizeChanged -=
            CompanionWindow_SizeChanged;


        IsVisibleChanged -=
            CompanionWindow_IsVisibleChanged;


        DetachMainWindowEvents();


        CancelProgrammaticMovement();


        _presence.MarkUnavailable();


        _segaState.ResetBody();
    }
}
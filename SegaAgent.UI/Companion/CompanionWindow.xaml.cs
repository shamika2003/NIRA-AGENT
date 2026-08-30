using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using SegaAgent.Agent.State;

namespace SegaAgent.UI.Companion;

public partial class CompanionWindow
    : Window
{
    private readonly SegaStateService
        _segaState;


    private SegaStateSnapshot
        _stateSnapshot;


    private readonly DispatcherTimer
        _mouseTimer;


    private readonly DispatcherTimer
        _idleFadeTimer;


    // =========================================================
    // IDLE FADE
    // =========================================================

    private DateTime _lastActivityTime =
        DateTime.UtcNow;


    private bool _isFaded;


    // =========================================================
    // DRAG
    // =========================================================

    private Point _dragStart;


    private bool _dragging;


    private bool _userDragging;


    // =========================================================
    // MOUSE AVOIDANCE
    // =========================================================

    private bool _avoidingMouse;


    private DateTime _lastAvoidTime =
        DateTime.MinValue;


    // =========================================================
    // MOVEMENT VERSION
    //
    // Prevent an older animation from marking Sega as resting
    // after a newer movement has already started.
    // =========================================================

    private long _movementVersion;


    // =========================================================
    // IDLE BEHAVIOR
    // =========================================================

    private CompanionIdleBehavior?
        _idleBehavior;


    // =========================================================
    // CONFIGURATION
    // =========================================================

    private const double ScreenMargin =
        30.0;


    private const double MouseAvoidDistance =
        170.0;


    private const double
        MouseAvoidDistanceSquared =
            MouseAvoidDistance *
            MouseAvoidDistance;


    private const double AvoidDistance =
        230.0;


    private const int MouseCheckInterval =
        50;


    // =========================================================
    // FADE
    // =========================================================

    private static readonly TimeSpan
        IdleFadeDelay =
            TimeSpan.FromMinutes(1);


    private const double FadedOpacity =
        0.15;


    private const double NormalOpacity =
        1.0;


    private const int
        FadeDurationMilliseconds =
            900;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public CompanionWindow(
        SegaStateService segaState)
    {
        InitializeComponent();


        _segaState =
            segaState;


        _stateSnapshot =
            _segaState.Current;


        _segaState.StateChanged +=
            SegaState_StateChanged;


        // =====================================================
        // MOUSE
        // =====================================================

        _mouseTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        MouseCheckInterval)
            };


        _mouseTimer.Tick +=
            MouseTimer_Tick;


        // =====================================================
        // FADE
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
        // WINDOW EVENTS
        // =====================================================

        Loaded +=
            CompanionWindow_Loaded;


        Closed +=
            CompanionWindow_Closed;


        // =====================================================
        // PARTICLE / BLOB INPUT
        // =====================================================

        Blob.MouseLeftButtonDown +=
            Blob_MouseLeftButtonDown;


        Blob.MouseMove +=
            Blob_MouseMove;


        Blob.MouseLeftButtonUp +=
            Blob_MouseLeftButtonUp;


        Blob.MouseRightButtonUp +=
            Blob_MouseRightButtonUp;
    }


    // =========================================================
    // LOADED
    // =========================================================

    private void CompanionWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        PositionAtBottomRight();


        Opacity =
            NormalOpacity;


        _lastActivityTime =
            DateTime.UtcNow;


        _isFaded =
            false;


        ApplySegaState(
            _segaState.Current);


        _mouseTimer.Start();


        _idleFadeTimer.Start();


        var controller =
            new CompanionController(
                this);


        _idleBehavior =
            new CompanionIdleBehavior(
                this,
                controller);


        _idleBehavior.Start();
    }


    // =========================================================
    // CURRENT VISUAL STATE
    //
    // Temporary compatibility for the current renderer.
    //
    // Later the particle renderer will consume Mind + Body
    // independently.
    // =========================================================

    public CompanionState BlobState =>
        Blob.State;


    // =========================================================
    // IDLE MOVEMENT
    // =========================================================

    public bool CanPerformIdleMovement =>
        !_userDragging &&
        _stateSnapshot.Mind ==
            SegaMindState.Idle &&
        _stateSnapshot.Body ==
            SegaBodyState.Resting;


    // =========================================================
    // USER INTERACTION
    // =========================================================

    public bool IsUserInteracting =>
        _userDragging ||
        _dragging;


    // =========================================================
    // POSITION
    // =========================================================

    public Point CompanionCenter =>
        new(
            Left + Width / 2.0,
            Top + Height / 2.0);


    // =========================================================
    // SHARED STATE CHANGE
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
    // APPLY SHARED STATE
    // =========================================================

    private void ApplySegaState(
        SegaStateSnapshot snapshot)
    {
        _stateSnapshot =
            snapshot;


        /*
         * This mapping exists only because the current
         * LiquidBlobControl still accepts one CompanionState.
         *
         * Later the particle renderer will receive:
         *
         * Mind
         * +
         * Body
         *
         * independently.
         */

        CompanionState visualState =
            snapshot.Body switch
            {
                SegaBodyState.Dragging =>
                    CompanionState.Moving,

                SegaBodyState.Avoiding =>
                    CompanionState.Avoiding,

                SegaBodyState.Moving =>
                    CompanionState.Moving,

                _ =>
                    snapshot.Mind switch
                    {
                        SegaMindState.Listening =>
                            CompanionState.Listening,

                        SegaMindState.Thinking =>
                            CompanionState.Thinking,

                        SegaMindState.Speaking =>
                            CompanionState.Speaking,

                        _ =>
                            CompanionState.Idle
                    }
            };


        Blob.State =
            visualState;


        if (visualState !=
            CompanionState.Idle)
        {
            RegisterActivity();
        }
    }


    // =========================================================
    // INITIAL POSITION
    // =========================================================

    private void PositionAtBottomRight()
    {
        Rect workArea =
            SystemParameters.WorkArea;


        Left =
            workArea.Right -
            Width -
            40;


        Top =
            workArea.Bottom -
            Height -
            40;
    }


    // =========================================================
    // IDLE FADE
    // =========================================================

    private void IdleFadeTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }


        if (_userDragging ||
            _dragging)
        {
            RestoreFromFade();

            return;
        }


        if (_stateSnapshot.Mind !=
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
            IdleFadeDelay)
        {
            return;
        }


        if (_isFaded)
        {
            return;
        }


        FadeToQuiet();
    }


    // =========================================================
    // REGISTER ACTIVITY
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
        if (!_isFaded &&
            Opacity >= NormalOpacity)
        {
            return;
        }


        _isFaded =
            false;


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
    // GLOBAL MOUSE
    // =========================================================

    private void MouseTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (_userDragging)
        {
            RestoreFromFade();

            return;
        }


        if (!IsVisible)
        {
            return;
        }


        Point mouse =
            MousePosition.Get();


        Point center =
            CompanionCenter;


        double dx =
            mouse.X -
            center.X;


        double dy =
            mouse.Y -
            center.Y;


        double distanceSquared =
            dx * dx +
            dy * dy;


        if (distanceSquared <=
            MouseAvoidDistanceSquared)
        {
            RegisterActivity();


            TryAvoidMouse(
                mouse,
                center);


            return;
        }


        if (_avoidingMouse)
        {
            _avoidingMouse =
                false;


            _segaState.SetAvoiding(
                false);
        }
    }


    // =========================================================
    // AVOID MOUSE
    // =========================================================

    private void TryAvoidMouse(
        Point mouse,
        Point center)
    {
        DateTime now =
            DateTime.UtcNow;


        if ((now - _lastAvoidTime)
            .TotalMilliseconds <
            350)
        {
            return;
        }


        _lastAvoidTime =
            now;


        _avoidingMouse =
            true;


        _segaState.SetAvoiding(
            true);


        double dx =
            center.X -
            mouse.X;


        double dy =
            center.Y -
            mouse.Y;


        double length =
            Math.Sqrt(
                dx * dx +
                dy * dy);


        if (length <
            0.001)
        {
            dx = 1.0;

            dy = 0.0;

            length = 1.0;
        }


        dx /=
            length;


        dy /=
            length;


        Point target =
            new(
                center.X +
                dx * AvoidDistance,

                center.Y +
                dy * AvoidDistance);


        target =
            KeepInsideWorkArea(
                target);


        MoveToAsync(
            target,
            TimeSpan.FromMilliseconds(
                500));
    }


    // =========================================================
    // KEEP INSIDE WORK AREA
    // =========================================================

    private Point KeepInsideWorkArea(
        Point center)
    {
        Rect workArea =
            SystemParameters.WorkArea;


        double halfWidth =
            Width / 2.0;


        double halfHeight =
            Height / 2.0;


        double minX =
            workArea.Left +
            halfWidth +
            ScreenMargin;


        double maxX =
            workArea.Right -
            halfWidth -
            ScreenMargin;


        double minY =
            workArea.Top +
            halfHeight +
            ScreenMargin;


        double maxY =
            workArea.Bottom -
            halfHeight -
            ScreenMargin;


        return new Point(
            Math.Clamp(
                center.X,
                minX,
                maxX),

            Math.Clamp(
                center.Y,
                minY,
                maxY));
    }


    // =========================================================
    // MOVE
    // =========================================================

    public void MoveToAsync(
        Point target,
        TimeSpan duration)
    {
        if (_userDragging)
        {
            return;
        }


        RegisterActivity();


        target =
            KeepInsideWorkArea(
                target);


        double targetLeft =
            target.X -
            Width / 2.0;


        double targetTop =
            target.Y -
            Height / 2.0;


        long movementVersion =
            ++_movementVersion;


        _segaState.SetMoving(
            true);


        DoubleAnimation leftAnimation =
            new()
            {
                To =
                    targetLeft,

                Duration =
                    new Duration(
                        duration),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseOut
                    }
            };


        DoubleAnimation topAnimation =
            new()
            {
                To =
                    targetTop,

                Duration =
                    new Duration(
                        duration),

                EasingFunction =
                    new CubicEase
                    {
                        EasingMode =
                            EasingMode.EaseOut
                    }
            };


        leftAnimation.Completed +=
            (_, _) =>
            {
                if (movementVersion !=
                    _movementVersion)
                {
                    return;
                }


                _segaState.SetMoving(
                    false);
            };


        BeginAnimation(
            LeftProperty,
            leftAnimation);


        BeginAnimation(
            TopProperty,
            topAnimation);
    }


    // =========================================================
    // RIGHT CLICK
    // =========================================================

    private void Blob_MouseRightButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled =
            true;


        RegisterActivity();


        OpenMainWindow();
    }


    // =========================================================
    // OPEN MAIN WINDOW
    // =========================================================

    private void OpenMainWindow()
    {
        if (Application.Current ==
            null)
        {
            return;
        }


        if (Application.Current.MainWindow
            is not MainWindow mainWindow)
        {
            return;
        }


        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }


        if (mainWindow.WindowState ==
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
    // START DRAG
    // =========================================================

    private void Blob_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton !=
            MouseButton.Left)
        {
            return;
        }


        RegisterActivity();


        _dragStart =
            e.GetPosition(
                this);


        _dragging =
            true;


        _userDragging =
            true;


        ++_movementVersion;


        /*
         * IMPORTANT:
         *
         * Cancel animations on the WINDOW.
         *
         * The old code attempted:
         *
         * Blob.BeginAnimation(LeftProperty, ...)
         *
         * even though Left/Top belong to the Window.
         */

        BeginAnimation(
            LeftProperty,
            null);


        BeginAnimation(
            TopProperty,
            null);


        _segaState.SetMoving(
            false);


        _segaState.SetDragging(
            true);


        Blob.CaptureMouse();


        e.Handled =
            true;
    }


    // =========================================================
    // DRAG
    // =========================================================

    private void Blob_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }


        if (e.LeftButton !=
            MouseButtonState.Pressed)
        {
            return;
        }


        RegisterActivity();


        Point position =
            e.GetPosition(
                this);


        double deltaX =
            position.X -
            _dragStart.X;


        double deltaY =
            position.Y -
            _dragStart.Y;


        Left +=
            deltaX;


        Top +=
            deltaY;


        KeepWindowInsideScreen();
    }


    // =========================================================
    // STOP DRAG
    // =========================================================

    private void Blob_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }


        _dragging =
            false;


        _userDragging =
            false;


        RegisterActivity();


        if (Blob.IsMouseCaptured)
        {
            Blob.ReleaseMouseCapture();
        }


        _segaState.SetDragging(
            false);


        e.Handled =
            true;
    }


    // =========================================================
    // KEEP WINDOW ON SCREEN
    // =========================================================

    private void KeepWindowInsideScreen()
    {
        Rect workArea =
            SystemParameters.WorkArea;


        double minLeft =
            workArea.Left +
            ScreenMargin;


        double maxLeft =
            workArea.Right -
            Width -
            ScreenMargin;


        double minTop =
            workArea.Top +
            ScreenMargin;


        double maxTop =
            workArea.Bottom -
            Height -
            ScreenMargin;


        Left =
            Math.Clamp(
                Left,
                minLeft,
                maxLeft);


        Top =
            Math.Clamp(
                Top,
                minTop,
                maxTop);
    }


    // =========================================================
    // CLOSED
    // =========================================================

    private void CompanionWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _idleBehavior?
            .Stop();


        _mouseTimer.Stop();


        _idleFadeTimer.Stop();


        _segaState.StateChanged -=
            SegaState_StateChanged;


        _segaState.ResetBody();
    }
}
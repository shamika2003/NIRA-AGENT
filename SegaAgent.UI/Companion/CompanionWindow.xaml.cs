using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SegaAgent.Voice;

namespace SegaAgent.UI.Companion;

public partial class CompanionWindow : Window
{
    private readonly VoiceQueue _voiceQueue;

    private readonly DispatcherTimer _mouseTimer;

    // =========================================================
    // IDLE FADE
    // =========================================================

    private readonly DispatcherTimer _idleFadeTimer;

    private DateTime _lastActivityTime =
        DateTime.UtcNow;

    private bool _isFaded;

    // =========================================================
    // DRAG
    // =========================================================

    private Point _dragStart;

    private bool _dragging;

    private bool _userDragging;

    private bool _avoidingMouse;

    private DateTime _lastAvoidTime =
        DateTime.MinValue;


    // =========================================================
    // IDLE BEHAVIOR
    // =========================================================

    private CompanionIdleBehavior? _idleBehavior;


    // =========================================================
    // CONFIGURATION
    // =========================================================

    private const double ScreenMargin = 30.0;

    private const double MouseAvoidDistance = 170.0;

    private const double MouseAvoidDistanceSquared =
        MouseAvoidDistance * MouseAvoidDistance;

    private const double AvoidDistance = 230.0;

    private const int MouseCheckInterval = 50;


    // =========================================================
    // IDLE FADE CONFIGURATION
    // =========================================================

    // Sega becomes quiet after one minute of inactivity.
    private static readonly TimeSpan IdleFadeDelay =
        TimeSpan.FromMinutes(1);

    // She remains visible, but becomes much less noticeable.
    private const double FadedOpacity = 0.15;

    private const double NormalOpacity = 1.0;

    private const int FadeDurationMilliseconds = 900;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public CompanionWindow(
        VoiceQueue voiceQueue)
    {
        InitializeComponent();

        _voiceQueue = voiceQueue;

        _voiceQueue.SpeakingChanged +=
            VoiceQueue_SpeakingChanged;


        // -----------------------------------------------------
        // Mouse monitoring
        // -----------------------------------------------------

        _mouseTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        MouseCheckInterval)
            };

        _mouseTimer.Tick +=
            MouseTimer_Tick;


        // -----------------------------------------------------
        // Idle fade monitoring
        // -----------------------------------------------------

        _idleFadeTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(500)
            };

        _idleFadeTimer.Tick +=
            IdleFadeTimer_Tick;


        // -----------------------------------------------------
        // Window events
        // -----------------------------------------------------

        Loaded +=
            CompanionWindow_Loaded;

        Closed +=
            CompanionWindow_Closed;


        // -----------------------------------------------------
        // Blob mouse events
        // -----------------------------------------------------

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

        Blob.State =
            CompanionState.Idle;

        Opacity =
            NormalOpacity;

        _lastActivityTime =
            DateTime.UtcNow;

        _isFaded = false;

        _mouseTimer.Start();

        _idleFadeTimer.Start();


        // -----------------------------------------------------
        // Start natural idle behavior
        // -----------------------------------------------------

        CompanionController controller =
            new(this);

        _idleBehavior =
            new CompanionIdleBehavior(
                this,
                controller);

        _idleBehavior.Start();
    }


    // =========================================================
    // STATE
    // =========================================================

    public CompanionState BlobState =>
        Blob.State;


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
        new Point(
            Left + Width / 2.0,
            Top + Height / 2.0);


    // =========================================================
    // SET STATE
    // =========================================================

    public void SetState(
        CompanionState state)
    {
        Blob.State = state;

        // Any active state means Sega should be visible.
        if (state != CompanionState.Idle)
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
    // VOICE STATE
    // =========================================================

    private void VoiceQueue_SpeakingChanged(
        object? sender,
        bool speaking)
    {
        Dispatcher.Invoke(() =>
        {
            if (_userDragging)
                return;


            if (speaking)
            {
                RegisterActivity();

                Blob.State =
                    CompanionState.Speaking;
            }
            else
            {
                RegisterActivity();

                Blob.State =
                    CompanionState.Idle;
            }
        });
    }


    // =========================================================
    // IDLE FADE TIMER
    // =========================================================

    private void IdleFadeTimer_Tick(
        object? sender,
        EventArgs e)
    {
        if (!IsVisible)
            return;

        if (_userDragging ||
            _dragging)
        {
            RestoreFromFade();
            return;
        }

        // -----------------------------------------------------
        // Any non-idle state keeps Sega fully visible.
        // -----------------------------------------------------

        if (Blob.State !=
            CompanionState.Idle)
        {
            RestoreFromFade();
            return;
        }

        // -----------------------------------------------------
        // Wait until she has been idle long enough.
        // -----------------------------------------------------

        TimeSpan idleTime =
            DateTime.UtcNow -
            _lastActivityTime;

        if (idleTime <
            IdleFadeDelay)
        {
            return;
        }

        if (_isFaded)
            return;

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
    // FADE TO QUIET
    // =========================================================

    private void FadeToQuiet()
    {
        if (_isFaded)
            return;

        _isFaded = true;

        DoubleAnimation animation =
            new()
            {
                To = FadedOpacity,

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
    // RESTORE FROM FADE
    // =========================================================

    private void RestoreFromFade()
    {
        if (!_isFaded &&
            Opacity >= NormalOpacity)
        {
            return;
        }

        _isFaded = false;

        DoubleAnimation animation =
            new()
            {
                To = NormalOpacity,

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
    // GLOBAL MOUSE CHECK
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
            return;

        Point mouse =
            GetMousePosition();


        Point blobCenter =
            new Point(
                Left + Width / 2.0,
                Top + Height / 2.0);


        double dx =
            mouse.X -
            blobCenter.X;


        double dy =
            mouse.Y -
            blobCenter.Y;


        double distanceSquared =
            dx * dx +
            dy * dy;


        // -----------------------------------------------------
        // Mouse is close.
        // Move away.
        // -----------------------------------------------------

        if (distanceSquared <=
            MouseAvoidDistanceSquared)
        {
            RegisterActivity();

            TryAvoidMouse(
                mouse,
                blobCenter);

            return;
        }


        // -----------------------------------------------------
        // Mouse moved away.
        // Return to idle.
        // -----------------------------------------------------

        if (_avoidingMouse)
        {
            _avoidingMouse = false;

            RegisterActivity();

            if (Blob.State ==
                CompanionState.Avoiding)
            {
                Blob.State =
                    CompanionState.Idle;
            }
        }
    }


    // =========================================================
    // GET GLOBAL MOUSE POSITION
    // =========================================================

    private static Point GetMousePosition()
    {
        return MousePosition.Get();
    }


    // =========================================================
    // AVOID MOUSE
    // =========================================================

    private void TryAvoidMouse(
        Point mouse,
        Point blobCenter)
    {
        DateTime now =
            DateTime.UtcNow;


        // Prevent constant movement.
        if ((now - _lastAvoidTime).TotalMilliseconds <
            350)
        {
            return;
        }


        _lastAvoidTime =
            now;


        _avoidingMouse = true;

        RegisterActivity();

        Blob.State =
            CompanionState.Avoiding;


        // -----------------------------------------------------
        // Direction away from mouse
        // -----------------------------------------------------

        double dx =
            blobCenter.X -
            mouse.X;


        double dy =
            blobCenter.Y -
            mouse.Y;


        double length =
            Math.Sqrt(
                dx * dx +
                dy * dy);


        if (length < 0.001)
        {
            dx = 1;
            dy = 0;

            length = 1;
        }


        dx /= length;
        dy /= length;


        // -----------------------------------------------------
        // Calculate target
        // -----------------------------------------------------

        Point target =
            new Point(
                blobCenter.X +
                dx * AvoidDistance,

                blobCenter.Y +
                dy * AvoidDistance);


        target =
            KeepInsideWorkArea(target);


        MoveToAsync(
            target,
            TimeSpan.FromMilliseconds(500));
    }


    // =========================================================
    // KEEP INSIDE SCREEN
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


        double x =
            Math.Clamp(
                center.X,
                minX,
                maxX);


        double y =
            Math.Clamp(
                center.Y,
                minY,
                maxY);


        return new Point(
            x,
            y);
    }


    // =========================================================
    // MOVE
    // =========================================================

    public void MoveToAsync(
        Point target,
        TimeSpan duration)
    {
        if (_userDragging)
            return;


        RegisterActivity();


        target =
            KeepInsideWorkArea(target);


        double targetLeft =
            target.X -
            Width / 2.0;


        double targetTop =
            target.Y -
            Height / 2.0;


        Blob.State =
            CompanionState.Moving;


        DoubleAnimation leftAnimation =
            new()
            {
                To = targetLeft,

                Duration =
                    new Duration(duration),

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
                To = targetTop,

                Duration =
                    new Duration(duration),

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
                if (!_avoidingMouse &&
                    !_userDragging)
                {
                    Blob.State =
                        CompanionState.Idle;
                }
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
        e.Handled = true;

        RegisterActivity();

        OpenMainWindow();
    }


    // =========================================================
    // OPEN MAIN WINDOW
    // =========================================================

    private void OpenMainWindow()
    {
        if (Application.Current == null)
            return;


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
            e.GetPosition(this);


        _dragging = true;

        _userDragging = true;


        Blob.BeginAnimation(
            LeftProperty,
            null);


        Blob.BeginAnimation(
            TopProperty,
            null);


        Blob.State =
            CompanionState.Moving;


        Blob.CaptureMouse();


        e.Handled = true;
    }


    // =========================================================
    // DRAG
    // =========================================================

    private void Blob_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_dragging)
            return;


        if (e.LeftButton !=
            MouseButtonState.Pressed)
        {
            return;
        }


        RegisterActivity();


        Point position =
            e.GetPosition(this);


        double deltaX =
            position.X -
            _dragStart.X;


        double deltaY =
            position.Y -
            _dragStart.Y;


        Left += deltaX;

        Top += deltaY;


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
            return;


        _dragging = false;

        _userDragging = false;


        RegisterActivity();


        if (Blob.IsMouseCaptured)
        {
            Blob.ReleaseMouseCapture();
        }


        Blob.State =
            CompanionState.Idle;


        e.Handled = true;
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
        _idleBehavior?.Stop();

        _mouseTimer.Stop();

        _idleFadeTimer.Stop();


        _voiceQueue.SpeakingChanged -=
            VoiceQueue_SpeakingChanged;
    }
}

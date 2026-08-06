using System;
using System.Windows;
using System.Windows.Threading;

namespace SegaAgent.UI.Companion;

public sealed class CompanionIdleBehavior
{
    private readonly CompanionWindow _window;
    private readonly CompanionController _controller;
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();

    private DateTime _nextDecisionTime =
        DateTime.MaxValue;

    private bool _enabled;


    // =========================================================
    // CONFIGURATION
    // =========================================================

    /*
     * Keep idle movement close to the edges.
     *
     * This prevents Sega from intentionally wandering
     * into the center of the screen.
     */

    private const double EdgeMargin = 70.0;


    /*
     * Small casual movement.
     */

    private const double MinimumDistance = 45.0;

    private const double MaximumSmallMove = 110.0;


    /*
     * Larger relocation.
     */

    private const double MinimumLargeMove = 150.0;


    /*
     * How frequently the behavior system checks
     * whether a decision should be made.
     *
     * This is NOT the movement interval.
     */

    private const int CheckIntervalMilliseconds = 500;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public CompanionIdleBehavior(
        CompanionWindow window,
        CompanionController controller)
    {
        _window = window;
        _controller = controller;

        _timer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(
                        CheckIntervalMilliseconds)
            };

        _timer.Tick += OnTick;
    }


    // =========================================================
    // START
    // =========================================================

    public void Start()
    {
        if (_enabled)
            return;

        _enabled = true;


        /*
         * First movement is intentionally sooner.
         *
         * This makes it easy to confirm that the
         * idle system is actually working.
         */

        ScheduleNextDecision(
            RandomDouble(
                8.0,
                15.0));


        _timer.Start();
    }


    // =========================================================
    // STOP
    // =========================================================

    public void Stop()
    {
        _enabled = false;

        _timer.Stop();

        _nextDecisionTime =
            DateTime.MaxValue;
    }


    // =========================================================
    // TIMER
    // =========================================================

    private void OnTick(
        object? sender,
        EventArgs e)
    {
        if (!_enabled)
            return;

        if (!_window.IsVisible)
            return;

        if (_window.IsUserInteracting)
            return;


        /*
         * IMPORTANT:
         *
         * Do NOT reset the decision timer here.
         *
         * If Sega is speaking, thinking, moving,
         * avoiding the mouse, etc., simply wait.
         *
         * The existing idle decision remains scheduled.
         */

        if (_window.BlobState !=
            CompanionState.Idle)
        {
            return;
        }


        if (DateTime.UtcNow <
            _nextDecisionTime)
        {
            return;
        }


        PerformIdleDecision();
    }


    // =========================================================
    // IDLE DECISION
    // =========================================================

    private void PerformIdleDecision()
    {
        /*
         * After every decision we always schedule
         * another future decision.
         *
         * This prevents the system from getting stuck.
         */

        double roll =
            _random.NextDouble();


        // =====================================================
        // LONG REST
        // =====================================================

        if (roll < 0.35)
        {
            ScheduleNextDecision(
                RandomDouble(
                    35.0,
                    70.0));

            return;
        }


        // =====================================================
        // SHORT REST
        // =====================================================

        if (roll < 0.55)
        {
            ScheduleNextDecision(
                RandomDouble(
                    20.0,
                    40.0));

            return;
        }


        // =====================================================
        // SMALL NATURAL MOVEMENT
        // =====================================================

        if (roll < 0.82)
        {
            MoveSmallAmount();

            return;
        }


        // =====================================================
        // LARGER REPOSITION
        // =====================================================

        MoveToAnotherEdgeArea();
    }


    // =========================================================
    // SMALL MOVE
    // =========================================================

    private void MoveSmallAmount()
    {
        Point current =
            _window.CompanionCenter;


        Rect workArea =
            SystemParameters.WorkArea;


        Vector direction =
            ChooseSmallDirection();


        double distance =
            RandomDouble(
                MinimumDistance,
                MaximumSmallMove);


        Point target =
            current +
            direction * distance;


        target =
            KeepNearEdges(
                target,
                workArea);


        /*
         * If the calculated position is too close,
         * don't force a pointless movement.
         */

        if (Distance(
                current,
                target) <
            MinimumDistance)
        {
            ScheduleNextDecision(
                RandomDouble(
                    20.0,
                    45.0));

            return;
        }


        TimeSpan duration =
            TimeSpan.FromMilliseconds(
                RandomDouble(
                    700.0,
                    1500.0));


        _controller.MoveTo(
            target,
            duration);


        /*
         * Wait after the movement.
         *
         * The movement itself takes roughly 1 second,
         * followed by a natural resting period.
         */

        ScheduleNextDecision(
            RandomDouble(
                25.0,
                60.0));
    }


    // =========================================================
    // LARGE MOVE
    // =========================================================

    private void MoveToAnotherEdgeArea()
    {
        Point current =
            _window.CompanionCenter;


        Point target =
            ChooseEdgePosition(
                current);


        double distance =
            Distance(
                current,
                target);


        /*
         * Don't make a large movement if the chosen
         * position happens to be too close.
         */

        if (distance <
            MinimumLargeMove)
        {
            ScheduleNextDecision(
                RandomDouble(
                    20.0,
                    45.0));

            return;
        }


        TimeSpan duration =
            TimeSpan.FromMilliseconds(
                RandomDouble(
                    1200.0,
                    2400.0));


        _controller.MoveTo(
            target,
            duration);


        /*
         * After relocating, Sega settles for a while.
         */

        ScheduleNextDecision(
            RandomDouble(
                45.0,
                100.0));
    }


    // =========================================================
    // SMALL DIRECTION
    // =========================================================

    private Vector ChooseSmallDirection()
    {
        /*
         * Small movements are mostly horizontal.
         *
         * This makes them feel like a little
         * repositioning rather than flying.
         */

        double x =
            RandomDouble(
                -1.0,
                1.0);


        double y =
            RandomDouble(
                -0.45,
                0.45);


        Vector direction =
            new(
                x,
                y);


        if (direction.Length <
            0.01)
        {
            direction =
                new Vector(
                    1.0,
                    0.0);
        }


        direction.Normalize();

        return direction;
    }


    // =========================================================
    // EDGE POSITION
    // =========================================================

    private Point ChooseEdgePosition(
        Point current)
    {
        Rect workArea =
            SystemParameters.WorkArea;


        double left =
            workArea.Left +
            _window.Width / 2.0 +
            EdgeMargin;


        double right =
            workArea.Right -
            _window.Width / 2.0 -
            EdgeMargin;


        double top =
            workArea.Top +
            _window.Height / 2.0 +
            EdgeMargin;


        double bottom =
            workArea.Bottom -
            _window.Height / 2.0 -
            EdgeMargin;


        /*
         * Choose a general edge rather than
         * an exact corner.
         */

        int side =
            _random.Next(4);


        return side switch
        {
            // =================================================
            // TOP
            // =================================================

            0 =>
                new Point(
                    RandomDouble(
                        left,
                        right),

                    top),


            // =================================================
            // RIGHT
            // =================================================

            1 =>
                new Point(
                    right,

                    RandomDouble(
                        top,
                        bottom)),


            // =================================================
            // BOTTOM
            // =================================================

            2 =>
                new Point(
                    RandomDouble(
                        left,
                        right),

                    bottom),


            // =================================================
            // LEFT
            // =================================================

            _ =>
                new Point(
                    left,

                    RandomDouble(
                        top,
                        bottom))
        };
    }


    // =========================================================
    // KEEP NEAR EDGES
    // =========================================================

    private Point KeepNearEdges(
        Point target,
        Rect workArea)
    {
        double left =
            workArea.Left +
            _window.Width / 2.0 +
            EdgeMargin;


        double right =
            workArea.Right -
            _window.Width / 2.0 -
            EdgeMargin;


        double top =
            workArea.Top +
            _window.Height / 2.0 +
            EdgeMargin;


        double bottom =
            workArea.Bottom -
            _window.Height / 2.0 -
            EdgeMargin;


        return new Point(
            Math.Clamp(
                target.X,
                left,
                right),

            Math.Clamp(
                target.Y,
                top,
                bottom));
    }


    // =========================================================
    // DISTANCE
    // =========================================================

    private static double Distance(
        Point a,
        Point b)
    {
        double dx =
            b.X - a.X;


        double dy =
            b.Y - a.Y;


        return Math.Sqrt(
            dx * dx +
            dy * dy);
    }


    // =========================================================
    // SCHEDULING
    // =========================================================

    private void ScheduleNextDecision()
    {
        ScheduleNextDecision(
            RandomDouble(
                20.0,
                60.0));
    }


    private void ScheduleNextDecision(
        double seconds)
    {
        _nextDecisionTime =
            DateTime.UtcNow.AddSeconds(
                seconds);
    }


    // =========================================================
    // RANDOM
    // =========================================================

    private double RandomDouble(
        double minimum,
        double maximum)
    {
        return minimum +
               _random.NextDouble() *
               (maximum - minimum);
    }
}
/*
 * filename: PcEnvironment.cs
 */

using System;
using System.Windows;
using System.Windows.Threading;

using SegaAgent.UI.Companion;

using WpfPoint = System.Windows.Point;
using WpfVector = System.Windows.Vector;
using WpfRect = System.Windows.Rect;

namespace SegaAgent.UI.Environment;

public sealed class PcEnvironment
{
    private readonly CompanionWindow _companion;

    private readonly CompanionBehavior _behavior;

    private readonly DispatcherTimer _timer;


    public PcWorldState WorldState { get; }


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public PcEnvironment(
        CompanionWindow companion,
        CompanionBehavior behavior)
    {
        _companion =
            companion;


        _behavior =
            behavior;


        WorldState =
            new PcWorldState();


        _timer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromMilliseconds(50)
            };


        _timer.Tick +=
            Update;
    }


    // =========================================================
    // START
    // =========================================================

    public void Start()
    {
        if (!_companion.IsLoaded)
        {
            return;
        }


        Update();


        WorldState.PreviousMousePosition =
            WorldState.MousePosition;


        _timer.Start();
    }


    // =========================================================
    // STOP
    // =========================================================

    public void Stop()
    {
        _timer.Stop();
    }


    // =========================================================
    // UPDATE
    // =========================================================

    private void Update(
        object? sender,
        EventArgs e)
    {
        Update();
    }


    private void Update()
    {
        if (!_companion.IsLoaded)
        {
            return;
        }


        UpdateScreen();

        UpdateMouse();

        UpdateCompanion();


        _behavior.Update(
            WorldState
        );
    }


    // =========================================================
    // SCREEN
    // =========================================================

    private void UpdateScreen()
    {
        WorldState.VirtualScreenBounds =
            SystemParameters.WorkArea;
    }


    // =========================================================
    // MOUSE
    // =========================================================
    //
    // WinForms is used ONLY for the global cursor position.
    //
    // Everything else remains WPF.
    //

    private void UpdateMouse()
    {
        try
        {
            System.Drawing.Point cursor =
                System.Windows.Forms.Cursor.Position;


            WpfPoint screenPoint =
                new WpfPoint(
                    cursor.X,
                    cursor.Y
                );


            WpfPoint previous =
                WorldState.MousePosition;


            WorldState.PreviousMousePosition =
                previous;


            WorldState.MousePosition =
                screenPoint;


            // =================================================
            // VELOCITY
            // =================================================

            WpfVector velocity =
                screenPoint -
                previous;


            WorldState.MouseVelocity =
                velocity;


            // =================================================
            // SPEED
            // =================================================

            WorldState.MouseSpeed =
                velocity.Length;


            // =================================================
            // DIRECTION
            // =================================================

            if (velocity.Length > 0.01)
            {
                WpfVector direction =
                    velocity;


                direction.Normalize();


                WorldState.MouseDirection =
                    direction;
            }
            else
            {
                WorldState.MouseDirection =
                    new WpfVector(
                        0,
                        0
                    );
            }
        }
        catch
        {
            // Mouse position can temporarily fail
            // during startup/shutdown.
        }
    }


    // =========================================================
    // COMPANION
    // =========================================================

    private void UpdateCompanion()
    {
        WpfRect bounds =
            new WpfRect(
                _companion.Left,
                _companion.Top,
                _companion.Width,
                _companion.Height
            );


        WorldState.CompanionBounds =
            bounds;


        // =====================================================
        // CENTER
        // =====================================================

        WpfPoint center =
            new WpfPoint(
                bounds.Left +
                bounds.Width / 2.0,

                bounds.Top +
                bounds.Height / 2.0
            );


        WorldState.CompanionCenter =
            center;


        // =====================================================
        // MOUSE -> COMPANION
        // =====================================================

        WpfVector mouseFromCompanion =
            WorldState.MousePosition -
            center;


        WorldState.MouseFromCompanion =
            mouseFromCompanion;


        // =====================================================
        // DISTANCE
        // =====================================================

        double distance =
            mouseFromCompanion.Length;


        WorldState.MouseDistance =
            distance;


        // =====================================================
        // NORMALIZED DIRECTION
        // =====================================================

        if (distance > 0.01)
        {
            WpfVector direction =
                mouseFromCompanion;


            direction.Normalize();


            WorldState.MouseDirectionFromCompanion =
                direction;
        }
        else
        {
            WorldState.MouseDirectionFromCompanion =
                new WpfVector(
                    0,
                    0
                );
        }


        // =====================================================
        // NO BLOB HIT TEST
        // =====================================================
        //
        // The companion is click-through.
        //

        WorldState.MouseInsideBlob =
            false;
    }
}
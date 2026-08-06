using System;
using System.Windows;
using SegaAgent.UI.Environment;

namespace SegaAgent.UI.Companion;

public sealed class CompanionBehavior
{
    private readonly CompanionController _controller;


    // =========================================================
    // CONFIGURATION
    // =========================================================

    // Visible blob radius.
    //
    // Your blob is approximately 66px, but can deform slightly.
    //
    private const double BlobRadius = 70.0;


    // Sega starts escaping when the mouse reaches
    // 50px outside the blob.
    //
    private const double SafetyDistance = 50.0;


    // Therefore the mouse can approach to approximately:
    //
    // 70 + 50 = 120px from the center.
    //
    private const double EscapeDistance =
        BlobRadius +
        SafetyDistance;


    // How far Sega moves when escaping.
    //
    private const double EscapeDistanceMove = 180.0;


    // Minimum movement so Sega doesn't make tiny movements.
    //
    private const double MinimumEscapeMove = 120.0;


    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public CompanionBehavior(
        CompanionController controller)
    {
        _controller = controller;
    }


    // =========================================================
    // UPDATE
    // =========================================================

    public void Update(
        PcWorldState world)
    {
        UpdateMouseEscape(world);
    }


    // =========================================================
    // MOUSE ESCAPE
    // =========================================================

    private void UpdateMouseEscape(
        PcWorldState world)
    {
        double distance =
            world.MouseDistance;


        // -----------------------------------------------------
        // Mouse is far enough away.
        //
        // Do absolutely nothing.
        // -----------------------------------------------------

        if (distance >= EscapeDistance)
        {
            return;
        }


        // -----------------------------------------------------
        // Mouse is close enough to trigger escape.
        //
        // Mouse direction from Sega's center tells us
        // which direction Sega must move AWAY.
        //
        // Mouse left  -> Sega right
        // Mouse right -> Sega left
        // Mouse top   -> Sega bottom
        // Mouse bottom-> Sega top
        // -----------------------------------------------------

        Vector awayDirection =
            -world.MouseDirectionFromCompanion;


        // -----------------------------------------------------
        // Safety fallback.
        //
        // If the mouse somehow sits exactly at the center,
        // use a default direction.
        // -----------------------------------------------------

        if (awayDirection.Length < 0.01)
        {
            awayDirection =
                new Vector(
                    1,
                    0
                );
        }
        else
        {
            awayDirection.Normalize();
        }


        // -----------------------------------------------------
        // Determine how close the mouse is.
        //
        // The closer it gets, the stronger the escape.
        // -----------------------------------------------------

        double penetration =
            EscapeDistance -
            distance;


        double strength =
            Math.Clamp(
                penetration / SafetyDistance,
                0.0,
                1.0
            );


        // -----------------------------------------------------
        // Base escape movement.
        //
        // Even at the edge of the safety zone, Sega should
        // move enough to establish visible separation.
        // -----------------------------------------------------

        double moveDistance =
            MinimumEscapeMove +
            (
                EscapeDistanceMove -
                MinimumEscapeMove
            ) *
            strength;


        Vector movement =
            awayDirection *
            moveDistance;


        // -----------------------------------------------------
        // Ask controller to escape.
        // -----------------------------------------------------

        _controller.EscapeFromMouse(
            movement.X,
            movement.Y
        );
    }
}
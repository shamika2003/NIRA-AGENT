using System;
using System.Windows;

namespace SegaAgent.UI.Companion;

public sealed class CompanionBehavior
{
    private readonly CompanionController _controller;


    // =========================================================
    // CONFIGURATION
    // =========================================================

    private const double BlobRadius = 70.0;

    private const double SafetyDistance = 50.0;

    private const double EscapeDistance =
        BlobRadius + SafetyDistance;


    private const double EscapeDistanceMove = 180.0;

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
        CompanionPosition companion,
        Point mouse)
    {
        UpdateMouseEscape(
            companion,
            mouse
        );
    }



    // =========================================================
    // MOUSE ESCAPE
    // =========================================================

    private void UpdateMouseEscape(
        CompanionPosition companion,
        Point mouse)
    {

        Point center =
            new Point(
                companion.CenterX,
                companion.CenterY
            );


        Vector mouseVector =
            mouse - center;


        double distance =
            mouseVector.Length;



        // -----------------------------------------------------
        // Mouse is safe.
        // -----------------------------------------------------

        if (distance >= EscapeDistance)
        {
            return;
        }



        // -----------------------------------------------------
        // Mouse is exactly inside center.
        // -----------------------------------------------------

        if (distance < 0.01)
        {
            mouseVector =
                new Vector(
                    1,
                    0
                );
        }
        else
        {
            mouseVector.Normalize();
        }



        // -----------------------------------------------------
        // Move away from mouse.
        // -----------------------------------------------------

        Vector awayDirection =
            -mouseVector;



        double penetration =
            EscapeDistance -
            distance;


        double strength =
            Math.Clamp(
                penetration / SafetyDistance,
                0.0,
                1.0
            );



        double moveDistance =
            MinimumEscapeMove +
            (
                EscapeDistanceMove -
                MinimumEscapeMove
            )
            *
            strength;



        Vector movement =
            awayDirection *
            moveDistance;



        _controller.EscapeFromMouse(
            movement.X,
            movement.Y
        );
    }
}
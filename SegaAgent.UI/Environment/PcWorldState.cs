/*
 * filename: PcWorldState.cs
 */

using System.Windows;

namespace SegaAgent.UI.Environment;

public sealed class PcWorldState
{
    // =========================================================
    // SCREEN
    // =========================================================

    public Rect VirtualScreenBounds { get; internal set; }


    // =========================================================
    // MOUSE
    // =========================================================

    public Point MousePosition { get; internal set; }

    public Point PreviousMousePosition { get; internal set; }

    public Vector MouseVelocity { get; internal set; }

    public double MouseSpeed { get; internal set; }


    // =========================================================
    // MOUSE DIRECTION
    // =========================================================

    public Vector MouseDirection { get; internal set; }


    // =========================================================
    // COMPANION
    // =========================================================

    public Rect CompanionBounds { get; internal set; }

    public Point CompanionCenter { get; internal set; }


    // =========================================================
    // MOUSE RELATIVE TO COMPANION
    // =========================================================

    public Vector MouseFromCompanion { get; internal set; }

    public Vector MouseDirectionFromCompanion { get; internal set; }


    // =========================================================
    // DERIVED STATE
    // =========================================================

    public bool MouseInsideBlob { get; internal set; }

    public double MouseDistance { get; internal set; }
}
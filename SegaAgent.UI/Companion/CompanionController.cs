using System;
using System.Windows;
using System.Windows.Threading;

namespace SegaAgent.UI.Companion;

public sealed class CompanionController
{
    private readonly CompanionWindow _window;
    private readonly Dispatcher _dispatcher;

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public CompanionController(
        CompanionWindow window)
    {
        _window = window;
        _dispatcher = window.Dispatcher;
    }

    // =========================================================
    // STATE
    // =========================================================

    public CompanionState State =>
        _window.BlobState;

    // =========================================================
    // POSITION
    // =========================================================

    public Point Position =>
        _window.CompanionCenter;

    // =========================================================
    // MOVE
    // =========================================================

    public void MoveTo(
        Point position,
        TimeSpan duration)
    {
        if (_dispatcher.CheckAccess())
        {
            _window.MoveToAsync(
                position,
                duration);

            return;
        }

        _dispatcher.Invoke(() =>
        {
            _window.MoveToAsync(
                position,
                duration);
        });
    }

    // =========================================================
    // ESCAPE
    // =========================================================

    public void Escape(Vector direction)
    {
        if (direction.Length < 0.01)
            return;

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() =>
            {
                Escape(direction);
            });

            return;
        }

        direction.Normalize();

        const double escapeDistance = 230.0;

        Point current =
            _window.CompanionCenter;

        Point target =
            current +
            direction * escapeDistance;

        MoveTo(
            target,
            TimeSpan.FromMilliseconds(500));
    }

    // =========================================================
    // HIDE
    // =========================================================

    public void Hide()
    {
        if (_dispatcher.CheckAccess())
        {
            _window.Hide();
            return;
        }

        _dispatcher.Invoke(
            _window.Hide);
    }

    // =========================================================
    // SHOW
    // =========================================================

    public void Show()
    {
        if (_dispatcher.CheckAccess())
        {
            _window.Show();
            return;
        }

        _dispatcher.Invoke(
            _window.Show);
    }

    // =========================================================
    // SET STATE
    // =========================================================

    public void SetState(
        CompanionState state)
    {
        if (_dispatcher.CheckAccess())
        {
            _window.SetState(state);
            return;
        }

        _dispatcher.Invoke(() =>
        {
            _window.SetState(state);
        });
    }

    // =========================================================
    // ESCAPE FROM MOUSE
    // =========================================================

    public void EscapeFromMouse(
        double x,
        double y)
    {
        Vector direction =
            new Vector(
                x,
                y);

        if (direction.Length < 0.01)
            return;


        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() =>
            {
                EscapeFromMouse(
                    x,
                    y);
            });

            return;
        }


        direction.Normalize();


        const double escapeDistance = 230.0;


        Point current =
            _window.CompanionCenter;


        Point target =
            current +
            direction *
            escapeDistance;


        MoveTo(
            target,
            TimeSpan.FromMilliseconds(500));
    }
}
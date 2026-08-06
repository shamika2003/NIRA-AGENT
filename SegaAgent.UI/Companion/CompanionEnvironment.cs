using System;
using System.Windows;
using System.Windows.Threading;
using SegaAgent.UI.Companion;

namespace SegaAgent.UI.Environment;

public sealed class PcEnvironment
{
    private readonly CompanionWindow _window;
    private readonly CompanionController _controller;
    private readonly DispatcherTimer _timer;

    public PcEnvironment(
        CompanionWindow window,
        CompanionController controller)
    {
        _window = window;
        _controller = controller;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        _timer.Tick += OnTick;
    }

    // =========================================================
    // START
    // =========================================================

    public void Start()
    {
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
    // TIMER
    // =========================================================

    private void OnTick(
        object? sender,
        EventArgs e)
    {
        CheckMouse();
    }

    // =========================================================
    // MOUSE
    // =========================================================

    private void CheckMouse()
    {
        if (!_window.IsLoaded)
            return;

        // Use the existing Win32 mouse-position helper.
        // This avoids System.Windows.Forms entirely.
        Point mouse =
            MousePosition.Get();

        Point center =
            new Point(
                _window.Left +
                _window.Width / 2.0,

                _window.Top +
                _window.Height / 2.0
            );

        Vector mouseToBlob =
            center - mouse;

        double distance =
            mouseToBlob.Length;

        // =====================================================
        // DANGER RADIUS
        // =====================================================

        const double dangerRadius = 150.0;

        if (distance > dangerRadius)
            return;

        if (distance < 0.01)
            return;

        mouseToBlob.Normalize();

        // Move Sega AWAY from the mouse.
        _controller.Escape(mouseToBlob);
    }
}
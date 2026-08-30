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
        _window =
            window;


        _dispatcher =
            window.Dispatcher;
    }


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
}
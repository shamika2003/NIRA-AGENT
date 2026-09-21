/*
 * filename: NIRAVisualArtifactToastWindow.xaml.cs
 */

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using NIRAAgent.PC.Awareness;
using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI.Visuals;

public partial class NIRAVisualArtifactToastWindow : Window
{
    private const int GwlExStyle =
        -20;

    private const long WsExNoActivate =
        0x08000000L;

    private const long WsExToolWindow =
        0x00000080L;

    private const uint SwpNoSize =
        0x0001;

    private const uint SwpNoZOrder =
        0x0004;

    private const uint SwpNoActivate =
        0x0010;


    public event Action?
        OpenChatRequested;


    public event Action?
        ViewRequested;


    public NIRAVisualArtifactToastWindow(
        VisualArtifactViewModel artifact)
    {
        ArgumentNullException.ThrowIfNull(
            artifact);

        InitializeComponent();

        DataContext =
            artifact;

        SourceInitialized +=
            Toast_SourceInitialized;
    }


    public void ShowAt(
        NIRAVisualToastPlacementService placement)
    {
        ArgumentNullException.ThrowIfNull(
            placement);

        Opacity =
            0.0;

        Show();
        UpdateLayout();

        Point topLeft =
            PointToScreen(
                new Point(0.0, 0.0));

        Point bottomRight =
            PointToScreen(
                new Point(
                    Math.Max(1.0, ActualWidth),
                    Math.Max(1.0, ActualHeight)));

        int physicalWidth =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Abs(bottomRight.X - topLeft.X)));

        int physicalHeight =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Math.Abs(bottomRight.Y - topLeft.Y)));

        PcRectangle target =
            placement.Resolve(
                physicalWidth,
                physicalHeight);

        IntPtr handle =
            new WindowInteropHelper(this).Handle;

        if (handle !=
            IntPtr.Zero)
        {
            SetWindowPos(
                handle,
                IntPtr.Zero,
                target.Left,
                target.Top,
                0,
                0,
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate);
        }

        Opacity =
            1.0;

        NIRAMotion.AnimateEntrance(
            ToastChrome,
            distance: 10.0,
            durationMs: 220);
    }


    private void Toast_SourceInitialized(
        object? sender,
        EventArgs e)
    {
        IntPtr handle =
            new WindowInteropHelper(this).Handle;

        long current =
            GetWindowLongPtr(
                handle,
                GwlExStyle)
            .ToInt64();

        SetWindowLongPtr(
            handle,
            GwlExStyle,
            new IntPtr(
                current |
                WsExNoActivate |
                WsExToolWindow));
    }


    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }


    private void ViewButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ViewRequested?.Invoke();
    }


    private void OpenChatButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenChatRequested?.Invoke();
    }


    private static IntPtr GetWindowLongPtr(
        IntPtr hWnd,
        int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }


    private static IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int nIndex,
        IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
    }


    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(
        IntPtr hWnd,
        int nIndex);


    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex);


    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(
        IntPtr hWnd,
        int nIndex,
        int newLong);


    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex,
        IntPtr newLong);


    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}

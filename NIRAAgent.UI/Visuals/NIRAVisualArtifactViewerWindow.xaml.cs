/*
 * filename: NIRAVisualArtifactViewerWindow.xaml.cs
 */

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.Threading;
using NIRAAgent.Diagnostics;

using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Theming;

namespace NIRAAgent.UI.Visuals;

public partial class NIRAVisualArtifactViewerWindow : Window
{
    private readonly VisualArtifactViewModel _artifact;
    private readonly NIRAAgent.UI.NIRAWorkAreaWindowGuard _workAreaGuard;
    private readonly CancellationTokenSource _decodeCancellation = new();
    private bool _decodeStarted;
    public NIRAVisualArtifactViewerWindow(
        VisualArtifactViewModel artifact)
    {
        ArgumentNullException.ThrowIfNull(
            artifact);

        _artifact = artifact;
        InitializeComponent();
        _workAreaGuard = new NIRAAgent.UI.NIRAWorkAreaWindowGuard(this, fitNormalWindow: true);

        DataContext =
            artifact;

        Loaded +=
            NIRAVisualArtifactViewerWindow_Loaded;
    }

    private async void NIRAVisualArtifactViewerWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        NIRAMotion.AnimateEntrance(
            RootShell,
            distance: 14.0,
            durationMs: 300);
        if (_decodeStarted) return;
        _decodeStarted = true;
        try
        {
            await _artifact.LoadFullImageAsync(_decodeCancellation.Token);
        }
        catch (OperationCanceledException) when (_decodeCancellation.IsCancellationRequested)
        {
            // Closed before decoding finished; nothing needs publishing.
        }
        catch (Exception ex)
        {
            NIRADiagnosticTrace.Event("IMAGE-VIEWER", $"Stage=DecodeFailed Error={ex.GetType().Name}");
            Debug.WriteLine($"[IMAGE-VIEWER] decode failed: {ex}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _workAreaGuard.Dispose();
        _decodeCancellation.Cancel();
        _artifact.ReleaseFullImage();
        base.OnClosed(e);
        _decodeCancellation.Dispose();
    }


    private void Viewer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            for (DependencyObject? node = source; node != null;)
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase) return;
                if (ReferenceEquals(node, sender)) break;
                node = node is FrameworkContentElement content
                    ? content.Parent : VisualTreeHelper.GetParent(node);
            }
        }
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
        e.Handled = true;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Viewer_OpenSource_Click(object sender, RoutedEventArgs e)
    {
        // Only user-initiated navigation to the source URI already carried by
        // the verified visual artifact. No remote media fetch in the viewer.
        if (sender is not System.Windows.Controls.Button button ||
            button.Tag is not string address ||
            !Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NIRADiagnosticTrace.Event("IMAGE-SOURCE-OPEN", $"Error={ex.GetType().Name}");
            MessageBox.Show(this, "That image source could not be opened.",
                "NIRA image source", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}

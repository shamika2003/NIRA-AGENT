using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NIRAAgent.UI.Theming;
using NIRAAgent.Diagnostics;
using NIRAAgent.UI.ViewModels;

namespace NIRAAgent.UI.Visuals;

public partial class NIRAUserAttachmentGalleryWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<VisualArtifactViewModel> Items { get; } = new();
    private readonly NIRAAgent.UI.NIRAWorkAreaWindowGuard _workAreaGuard;

    private int _selectedIndex;
    private bool _selectionReady;
    private bool _selectionSync;
    private bool _closed;
    private CancellationTokenSource? _decodeCancellation;
    private VisualArtifactViewModel? _displayedImage;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SelectImage(value);
    }

    // Single authoritative selection path. The ListBox does not write back
    // through a two-way binding while its ItemsSource is still being realized.
    private void SelectImage(int requested)
    {
        int normalized = Items.Count == 0 ? 0 : Math.Clamp(requested, 0, Items.Count - 1);
        bool changed = _selectedIndex != normalized;
        _selectedIndex = normalized;

        if (_selectionReady && !_selectionSync && ThumbnailList.SelectedIndex != normalized)
        {
            try
            {
                _selectionSync = true;
                ThumbnailList.SelectedIndex = normalized;
            }
            finally { _selectionSync = false; }
        }

        UpdateSourceLink();
        if (!changed) return;
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        if (IsLoaded && !_closed) BeginSelectedDecode();
    }

    private void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectionSync || !_selectionReady || ThumbnailList.SelectedIndex < 0) return;
        SelectImage(ThumbnailList.SelectedIndex);
    }

    public VisualArtifactViewModel? SelectedItem =>
        Items.Count == 0 ? null : Items[Math.Clamp(SelectedIndex, 0, Items.Count - 1)];

    public string CounterText => Items.Count == 0 ? "0 / 0" : $"{SelectedIndex + 1} / {Items.Count}";
    public bool CanGoPrevious => Items.Count > 1 && SelectedIndex > 0;
    public bool CanGoNext => Items.Count > 1 && SelectedIndex < Items.Count - 1;

    public NIRAUserAttachmentGalleryWindow(
        IReadOnlyList<VisualArtifactViewModel> items,
        int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-CTOR", "Stage=Enter");
        InitializeComponent();
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-CTOR", "Stage=XamlInitialized");
        _workAreaGuard = new NIRAAgent.UI.NIRAWorkAreaWindowGuard(this, fitNormalWindow: true);

        // Populate *before* activating bindings. Selection remains a normal
        // code-behind state transition, never a two-way WPF coercion loop.
        foreach (VisualArtifactViewModel item in items)
            Items.Add(item);
        _selectedIndex = Items.Count == 0 ? 0 : Math.Clamp(selectedIndex, 0, Items.Count - 1);
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-CTOR", $"Stage=ItemsReady Count={Items.Count}");

        DataContext = this;
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-CTOR", "Stage=DataBound");
        _selectionReady = true;
        try
        {
            _selectionSync = true;
            ThumbnailList.SelectedIndex = Items.Count == 0 ? -1 : _selectedIndex;
        }
        finally { _selectionSync = false; }
        UpdateSourceLink();
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-CTOR", "Stage=SelectionReady");

        Loaded += OnLoaded;
        Closed += OnClosed;
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-CTOR", "Stage=Ready");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-OPEN", "Stage=Loaded");
        NIRAMotion.AnimateEntrance(RootShell, distance: 14.0, durationMs: 280);
        // Allow the first rendered frame before any full-image decode starts.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_closed) BeginSelectedDecode();
        }), DispatcherPriority.Background);
    }

    private void BeginSelectedDecode()
    {
        if (_closed) return;
        _decodeCancellation?.Cancel();
        _decodeCancellation?.Dispose();
        _decodeCancellation = new CancellationTokenSource();

        VisualArtifactViewModel? selected = SelectedItem;
        if (!ReferenceEquals(_displayedImage, selected))
            _displayedImage?.ReleaseFullImage();
        _displayedImage = selected;
        if (selected == null) return;

        // Do not await in a selection/property setter. The gallery appears
        // immediately at preview resolution even for many large screenshots.
        _ = DecodeSelectedAsync(selected, _decodeCancellation.Token);
    }

    private static async Task DecodeSelectedAsync(
        VisualArtifactViewModel image, CancellationToken cancellationToken)
    {
        try
        {
            await image.LoadFullImageAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user navigated away or closed the gallery.
        }
        catch (Exception ex)
        {
            NIRADiagnosticTrace.Event("IMAGE-VIEWER", $"Stage=DecodeFailed Error={ex.GetType().Name}");
            Debug.WriteLine($"[IMAGE-VIEWER] decode failed: {ex}");
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        NIRADiagnosticTrace.Event("IMAGE-GALLERY-OPEN", "Stage=Closed");
        _workAreaGuard.Dispose();
        _decodeCancellation?.Cancel();
        _decodeCancellation?.Dispose();
        _decodeCancellation = null;
        _displayedImage?.ReleaseFullImage();
        _displayedImage = null;
    }

    private void Gallery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Left && CanGoPrevious) { SelectedIndex--; e.Handled = true; }
        else if (e.Key == Key.Right && CanGoNext) { SelectedIndex++; e.Handled = true; }
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

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (CanGoPrevious) SelectedIndex--;
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (CanGoNext) SelectedIndex++;
    }

    private void UpdateSourceLink()
    {
        SourceLinkButton.Visibility = SelectedItem is { HasSourceUri: true }
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        string address = SelectedItem?.SourceUri ?? string.Empty;
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            NIRADiagnosticTrace.Event("IMAGE-SOURCE-OPEN", $"Error={ex.GetType().Name}");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

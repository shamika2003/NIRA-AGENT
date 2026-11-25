using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NIRAAgent.UI;

namespace NIRAAgent.UI.Presentation;

// Original NIRA viewer layout and navigation, using the new verified in-memory previews.
public partial class NIRAUserAttachmentGalleryWindow : Window, INotifyPropertyChanged
{
    private readonly NIRAWorkAreaWindowGuard _workAreaGuard;
    private readonly IReadOnlyList<NIRAImageStrip.Photo> _photos;
    private int _selectedIndex;
    private bool _selectionSync;
    private NIRAGifPlayback? _playback;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal NIRAUserAttachmentGalleryWindow(IReadOnlyList<NIRAImageStrip.Photo> photos, int selected)
    {
        _photos = photos;
        _selectedIndex = Math.Clamp(selected, 0, photos.Count - 1);
        InitializeComponent();
        _playback = new NIRAGifPlayback(ViewerImage);
        ViewerBrand.Source = NIRABackdropBorder.LoadBitmap("Nira-Icon.png");
        _workAreaGuard = new NIRAWorkAreaWindowGuard(this, fitNormalWindow: true);
        Closed += (_, _) => _workAreaGuard.Dispose();
        DataContext = this;
        SelectImage(_selectedIndex);
        Loaded += (_, _) => ThumbnailList.ScrollIntoView(SelectedItem);
    }
    // Public binding surfaces expose only the frozen preview, title and attribution.
    public System.Collections.IEnumerable Items => _photos;
    private IReadOnlyList<NIRAImageStrip.Photo> Photos => _photos;
    public object SelectedItem => Photos[_selectedIndex];
    public string CounterText => $"{_selectedIndex + 1} / {Photos.Count}";
    public bool CanGoPrevious => _selectedIndex > 0;
    public bool CanGoNext => _selectedIndex + 1 < Photos.Count;

    private void SelectImage(int index)
    {
        if (index < 0 || index >= Photos.Count) return;
        _selectedIndex = index;
        _selectionSync = true;
        try { ThumbnailList.SelectedIndex = index; }
        finally { _selectionSync = false; }
        foreach (string name in new[] { nameof(SelectedItem), nameof(CounterText), nameof(CanGoPrevious), nameof(CanGoNext) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        SourceLinkButton.Visibility = SourceUri() is null ? Visibility.Collapsed : Visibility.Visible;
        _playback?.SetPhoto(Photos[index]);
        PlaybackButton.Visibility = Photos[index].IsAnimated ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlaybackButton();
        if (IsLoaded) ThumbnailList.ScrollIntoView(SelectedItem);
    }
    private void UpdatePlaybackButton() => PlaybackButton.Content = _playback?.IsPlaying == true ? "Pause animation" : "Play animation";
    private void PlaybackButton_Click(object sender, RoutedEventArgs e)
    { _playback?.Toggle(); UpdatePlaybackButton(); }
    private Uri? SourceUri() => Uri.TryCreate(Photos[_selectedIndex].Source, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 ? uri : null;
    private void ThumbnailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_selectionSync && sender is ListBox { SelectedIndex: >= 0 } list) SelectImage(list.SelectedIndex); }
    private void PreviousButton_Click(object sender, RoutedEventArgs e) => SelectImage(_selectedIndex - 1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => SelectImage(_selectedIndex + 1);
    private void Gallery_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Left) SelectImage(_selectedIndex - 1);
        else if (e.Key == Key.Right) SelectImage(_selectedIndex + 1);
        else return;
        e.Handled = true;
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
            for (DependencyObject? node = source; node is not null;)
            {
                if (node is ButtonBase) return;
                if (ReferenceEquals(node, sender)) break;
                node = node is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(node);
            }
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == MouseButtonState.Pressed)
            try { DragMove(); } catch (InvalidOperationException) { }
        e.Handled = true;
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SourceUri() is not { } uri) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { MessageBox.Show(this, "The image source could not be opened.", "NIRA"); }
    }
}

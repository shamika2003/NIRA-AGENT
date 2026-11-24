using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Nira.Infrastructure;

namespace NIRAAgent.UI.Presentation;

// Original Gallery page layout, bound to the current journal and original local import catalog.
public partial class NIRAGalleryPage : UserControl
{
    private const int PageSize = 24;
    private SqliteConversationStore? _store;
    private NIRAGalleryLibraryService? _uploads;
    private string _workspace = "";
    private string _appRoot = "";
    private IReadOnlyList<ImageGalleryEntry> _web = [];
    private NIRAGalleryCategory _category = NIRAGalleryCategory.WebFound;
    private int _page;
    private bool _loading;
    private readonly ObservableCollection<NIRAImageStrip.Photo> _tiles = new();

    public NIRAGalleryPage()
    {
        InitializeComponent();
        ImageTiles.ItemsSource = _tiles;
        SetCategory(NIRAGalleryCategory.WebFound);
    }

    internal async Task InitializeAsync(SqliteConversationStore store, string workspace, string databasePath)
    {
        _store = store;
        _workspace = workspace;
        _appRoot = Path.GetDirectoryName(databasePath) ?? throw new ArgumentException("Database directory required.", nameof(databasePath));
        await RefreshGalleryAsync();
    }

    private async Task RefreshGalleryAsync()
    {
        if (_loading || _store is null) return;
        _loading = true;
        StatusText.Text = "Loading local library…";
        try
        {
            // Refresh off the UI thread; reopening gallery never calls the model or network.
            var store = _store;
            string home = _appRoot;
            _web = await Task.Run(() => store.ListGalleryAsync(CancellationToken.None));
            // An unavailable uploads catalog must not hide the independent web collection.
            try { _uploads ??= await Task.Run(() => new NIRAGalleryLibraryService(Path.Combine(home, "Gallery"))); }
            catch (Exception error) { Debug.WriteLine(error); }
            RenderPage();
            if (_uploads is null && _category == NIRAGalleryCategory.Uploaded)
                StatusText.Text = "Uploaded image storage is unavailable. Your web collection is unchanged.";
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            StatusText.Text = "The local library could not be refreshed. Your existing files are unchanged.";
        }
        finally { _loading = false; }
    }

    private void SetCategory(NIRAGalleryCategory category)
    {
        _category = category;
        _page = 0;
        UploadedButton.Tag = category == NIRAGalleryCategory.Uploaded ? "ACTIVE" : null;
        WebButton.Tag = category == NIRAGalleryCategory.WebFound ? "ACTIVE" : null;
        GeneratedButton.Tag = category == NIRAGalleryCategory.AiGenerated ? "ACTIVE" : null;
        SectionTitle.Text = category switch { NIRAGalleryCategory.Uploaded => "Uploaded images", NIRAGalleryCategory.WebFound => "Found on the web", _ => "AI generated images" };
        SectionSummary.Text = category switch { NIRAGalleryCategory.Uploaded => "Photos you add to your local library", NIRAGalleryCategory.WebFound => "Photographs NIRA retrieved from the web", _ => "Artwork created by NIRA" };
        AddButton.Visibility = category == NIRAGalleryCategory.Uploaded ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Clear();
        RenderPage();
    }
    private string StorageFolder => _category switch
    {
        NIRAGalleryCategory.WebFound when _workspace.Length > 0 => Path.Combine(_workspace, ImagePreviewTool.CacheFolder),
        NIRAGalleryCategory.Uploaded => _uploads?.RootFolder ?? "",
        NIRAGalleryCategory.AiGenerated when _workspace.Length > 0 => Path.Combine(_workspace, ImageGenerationTool.CacheFolder),
        _ => ""
    };
    private bool Matches(params string[] values)
    {
        string filter = SearchBox.Text.Trim();
        return filter.Length == 0 || values.Any(value => value.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
    private void RenderPage()
    {
        _tiles.Clear();
        AddButton.IsEnabled = _uploads is not null;
        StoragePathText.Text = StorageFolder.Length > 0 ? StorageFolder : "Not connected";
        StoragePathText.ToolTip = StoragePathText.Text;
        int total = 0;
        if (_category is NIRAGalleryCategory.WebFound or NIRAGalleryCategory.AiGenerated)
        {
            var matching = _web.Where(e => e.IsGenerated == (_category == NIRAGalleryCategory.AiGenerated) && Matches(e.Title, e.Author, e.License)).ToArray();
            total = matching.Length;
            ClampPage(total);
            foreach (var photo in NIRAImageStrip.LoadPhotos(matching.Skip(_page * PageSize).Take(PageSize), _workspace, PageSize)) _tiles.Add(photo);
        }
        else if (_category == NIRAGalleryCategory.Uploaded && _uploads is { } uploads)
        {
            var matching = uploads.Items.Where(e => e.Category == _category && Matches(e.Title, e.Credit)).ToArray();
            total = matching.Length;
            ClampPage(total);
            foreach (var item in matching.Skip(_page * PageSize).Take(PageSize))
                if (LoadUpload(uploads, item) is { } photo) _tiles.Add(photo);
        }
        else _page = 0;
        CountText.Text = $"{total} photo{(total == 1 ? "" : "s")}";
        StatusText.Text = $"Local library • Page {_page + 1} of {Pages(total)} • {_tiles.Count} available";
        if (_category == NIRAGalleryCategory.WebFound) StatusText.Text += " • Source identity and rights not independently checked";
        PreviousPage.IsEnabled = _page > 0;
        NextPage.IsEnabled = _page + 1 < Pages(total);
        EmptyState.Visibility = _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _category == NIRAGalleryCategory.AiGenerated ? "No AI-generated artwork yet" : "No images here";
        EmptyDescription.Text = _category switch
        {
            NIRAGalleryCategory.Uploaded => "Add photos using the button above. Your originals remain untouched.",
            NIRAGalleryCategory.WebFound => "Retrieved photographs appear here with their original sources. Try another search or page if a preview is missing.",
            _ => "Ask NIRA in Chat to create an image. Saved artwork appears here after a successful generation."
        };
        GalleryScrollViewer.ScrollToTop();
    }
    private static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    private void ClampPage(int count) => _page = Math.Clamp(_page, 0, Pages(count) - 1);
    private void Uploaded_Click(object sender, RoutedEventArgs e) => SetCategory(NIRAGalleryCategory.Uploaded);
    private void Web_Click(object sender, RoutedEventArgs e) => SetCategory(NIRAGalleryCategory.WebFound);
    private void Generated_Click(object sender, RoutedEventArgs e) => SetCategory(NIRAGalleryCategory.AiGenerated);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshGalleryAsync();
    private void SearchBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { _page = 0; RenderPage(); e.Handled = true; } }
    private void PreviousPage_Click(object sender, RoutedEventArgs e) { _page--; RenderPage(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { _page++; RenderPage(); }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_uploads is not { } uploads || _loading) return;
        var dialog = new OpenFileDialog { Title = "Add photos to NIRA Gallery", Multiselect = true, CheckFileExists = true,
            Filter = "Pictures|*.jpg;*.jpeg;*.png;*.gif;*.bmp" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        AddButton.IsEnabled = false;
        _loading = true;
        try
        {
            int count = await Task.Run(() => dialog.FileNames.Take(50).Count(path => uploads.Import(path, NIRAGalleryCategory.Uploaded) is not null));
            _page = 0; RenderPage();
            StatusText.Text = $"{count} image(s) added or already in the library. Originals unchanged.";
        }
        catch (Exception error) { Debug.WriteLine(error); StatusText.Text = "Some images could not be added. Original files are unchanged."; }
        finally { _loading = false; AddButton.IsEnabled = true; }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = StorageFolder;
            if (folder.Length == 0 || !Directory.Exists(folder)) { StatusText.Text = "No storage folder is available for this category yet."; return; }
            string safe = new WorkspacePathResolver(folder).Resolve(folder);
            Process.Start(new ProcessStartInfo(safe) { UseShellExecute = true });
        }
        catch (Exception error) { Debug.WriteLine(error); StatusText.Text = "The folder could not be opened."; }
    }
    private void Image_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NIRAImageStrip.Photo selected } || Window.GetWindow(this) is not { } owner) return;
        var photos = _tiles.ToArray();
        int index = Array.IndexOf(photos, selected);
        if (index >= 0) NIRAImageStrip.ShowPhotos(photos, index, owner);
    }

    // Uploaded files are independent durable copies. Verify the exact bytes before decoding.
    private static NIRAImageStrip.Photo? LoadUpload(NIRAGalleryLibraryService library, NIRAGalleryItem item)
    {
        try
        {
            using var file = new FileStream(library.ResolvePath(item), FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < 8 or > 40L * 1024 * 1024) return null;
            byte[] bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) return null;
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            int width = frame.PixelWidth, height = frame.PixelHeight;
            if (width < 1 || height < 1 || width > 12000 || height > 12000 || (long)width * height > 36_000_000) return null;
            using var source = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = source;
            if (width >= height) bitmap.DecodePixelWidth = Math.Min(width, 1200);
            else bitmap.DecodePixelHeight = Math.Min(height, 1200);
            bitmap.EndInit(); bitmap.Freeze();
            return new NIRAImageStrip.Photo(bitmap, item.Title, "Your uploaded photo", "", "");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException) { return null; }
    }
}

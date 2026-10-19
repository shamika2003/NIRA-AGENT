using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NIRAAgent.Artifacts;
using NIRAAgent.Gallery;
using NIRAAgent.UI.ViewModels;
using NIRAAgent.UI.Visuals;
using Microsoft.Win32;

namespace NIRAAgent.UI.Gallery;

public partial class NIRAGalleryPage : UserControl
{
    private NIRAGalleryLibraryService? _library;
    private NIRAGalleryCategory _category = NIRAGalleryCategory.Uploaded;
    public ObservableCollection<NIRAGalleryTile> Tiles { get; } = new();
    public NIRAGalleryPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    public void Initialize(NIRAGalleryLibraryService library)
    {
        _library = library;
        RefreshGallery();
    }

    public void RefreshGallery()
    {
        if (_library == null) return;
        StoragePathText.Text = _library.RootFolder;
        Tiles.Clear();
        var all = _library.Items.Where(x => x.Category == _category).ToArray();
        foreach (var item in all.Take(240))
        {
            string path = _library.ResolvePath(item);
            if (File.Exists(path)) Tiles.Add(new NIRAGalleryTile(item, path));
        }
        CountText.Text = $"{all.Length} photo{(all.Length == 1 ? "" : "s")}";
        StatusText.Text = all.Length > 240 ? "Showing 240 newest images. Older files remain safely stored." : "Local library • Images kept on your PC";
        EmptyState.Visibility = Tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _category == NIRAGalleryCategory.AiGenerated ? "AI image generation is not connected yet" : "No images yet";
        EmptyDescription.Text = _category switch
        {
            NIRAGalleryCategory.Uploaded => "Photos you send to NIRA are copied here automatically. You can also add photos yourself.",
            NIRAGalleryCategory.WebFound => "Verified web images NIRA retrieves will appear here with their original source.",
            _ => "This section is ready for NIRA's future image-generation capability. No invented or placeholder pictures."
        };
        AddButton.Visibility = _category == NIRAGalleryCategory.Uploaded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetCategory(NIRAGalleryCategory category)
    {
        _category = category;
        UploadedButton.Tag = category == NIRAGalleryCategory.Uploaded ? "ACTIVE" : null;
        WebButton.Tag = category == NIRAGalleryCategory.WebFound ? "ACTIVE" : null;
        GeneratedButton.Tag = category == NIRAGalleryCategory.AiGenerated ? "ACTIVE" : null;
        SectionTitle.Text = category switch
        {
            NIRAGalleryCategory.Uploaded => "Uploaded images",
            NIRAGalleryCategory.WebFound => "Found on the web",
            _ => "AI generated images"
        };
        SectionSummary.Text = category switch
        {
            NIRAGalleryCategory.Uploaded => "Images you share with NIRA",
            NIRAGalleryCategory.WebFound => "Verified pictures NIRA has discovered",
            _ => "Images NIRA creates in the future"
        };
        RefreshGallery();
    }
    private void Uploaded_Click(object sender, RoutedEventArgs e) => SetCategory(NIRAGalleryCategory.Uploaded);
    private void Web_Click(object sender, RoutedEventArgs e) => SetCategory(NIRAGalleryCategory.WebFound);
    private void Generated_Click(object sender, RoutedEventArgs e) => SetCategory(NIRAGalleryCategory.AiGenerated);
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshGallery();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        OpenFileDialog dialog = new()
        {
            Title = "Add photos to NIRA Gallery", Multiselect = true,
            Filter = "Pictures|*.jpg;*.jpeg;*.png;*.gif;*.bmp"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        AddButton.IsEnabled = false;
        try
        {
            int count = await Task.Run(() => dialog.FileNames.Count(path =>
                _library.Import(path, NIRAGalleryCategory.Uploaded) != null));
            RefreshGallery();
            StatusText.Text = $"{count} image(s) added or already in the library.";
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Gallery import", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { AddButton.IsEnabled = true; }
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        OpenFolderDialog dialog = new()
        {
            Title = "Choose an empty folder for NIRA Gallery",
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string target = dialog.FolderName;
        var confirmation = MessageBox.Show(Window.GetWindow(this),
            "Move all images managed by NIRA Gallery to this folder?\n\n" + target +
            "\n\nNIRA will copy and verify the files before changing the saved location. Original user uploads and past chat caches will not be moved.",
            "Move gallery storage", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;
        IsEnabled = false;
        try
        {
            await Task.Run(() => _library.MoveLibrary(target));
            RefreshGallery();
            StatusText.Text = "Gallery library moved and verified successfully.";
        }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Gallery storage unchanged", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { IsEnabled = true; }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_library == null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_library.RootFolder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void Image_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NIRAGalleryTile selected }) return;
        var gallery = Tiles.ToArray();
        int index = Array.IndexOf(gallery, selected);
        if (index < 0) return;
        int start = Math.Max(0, index - 12);
        int end = Math.Min(gallery.Length, index + 13);
        var images = gallery[start..end].Select(t => new VisualArtifactViewModel(new NIRAVisualArtifact
        {
            ArtifactId = t.Record.Id,
            LocalPath = t.FullPath,
            Title = t.Record.Title,
            Caption = t.Record.Credit,
            SourceUri = t.Record.SourceUri,
            SourceKind = NIRAVisualArtifactSourceKind.LocalImage
        })).ToArray();
        var window = new NIRAUserAttachmentGalleryWindow(images, index - start) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}

public sealed record NIRAGalleryTile(NIRAGalleryItem Record, string FullPath)
{
    public string Title => Record.Title;
    public string Credit => Record.Credit.Length > 0 ? Record.Credit : Record.Category switch
    {
        NIRAGalleryCategory.Uploaded => "Your uploaded photo",
        NIRAGalleryCategory.WebFound => "Web reference photo",
        _ => "Generated image"
    };
    public ImageSource? Preview { get; } = VisualArtifactViewModel.TryLoadPreview(FullPath, 350);
}

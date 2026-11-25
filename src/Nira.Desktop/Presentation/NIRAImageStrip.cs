using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nira.Core;
using Nira.Infrastructure;

namespace NIRAAgent.UI.Presentation;

/// <summary>Old NIRA's photo-first strip and single-image view, adapted to receipt-bound local previews.</summary>
internal static class NIRAImageStrip
{
    internal sealed record Photo(BitmapImage Bitmap, string Title, string Author, string License, string Source, byte[]? Animation = null, int FrameDurationMs = 0, bool IsLocalCapture = false)
    {
        public bool IsAnimated => Animation is not null;
        public ImageSource FullImage => Bitmap;
        public ImageSource PreviewImage => Bitmap;
        public ImageSource Preview => Bitmap;
        public string Credit => string.Join(" · ", new[] { Author, License }.Where(value => value.Length > 0));
    }

    internal static FrameworkElement? Create(ConversationState state, string workspace, Window owner)
    {
        var entries = ImageGalleryEntry.ForChat(state);
        var photos = LoadPhotos(entries.OrderByDescending(entry => entry.IsLocalCapture).ThenByDescending(entry => entry.IsGenerated), workspace, 6);
        if (photos.Count == 0) return null;
        var panel = new StackPanel { Margin = new Thickness(0,8,0,15) };
        var strip = new StackPanel { Orientation = Orientation.Horizontal };
        bool single = photos.Count == 1;
        for (int i = 0; i < photos.Count; i++)
        {
            int index = i;
            Photo photo = photos[i];
            double width = single ? 345 : 192, height = single ? Math.Min(285, 345.0 * photo.Bitmap.PixelHeight / photo.Bitmap.PixelWidth) : 144;
            if (single && photo.Bitmap.PixelHeight > photo.Bitmap.PixelWidth) width = Math.Min(width, height * photo.Bitmap.PixelWidth / photo.Bitmap.PixelHeight);
            if (photo.IsLocalCapture)
            {
                // Fit the entire screenshot, including menus and status bars. Do not crop it like a photograph.
                double scale = Math.Min(1, Math.Min((single ? 650.0 : 320.0) / photo.Bitmap.PixelWidth,
                    (single ? 420.0 : 225.0) / photo.Bitmap.PixelHeight));
                width = photo.Bitmap.PixelWidth * scale;
                height = photo.Bitmap.PixelHeight * scale;
            }
            var frame = new Grid { Width = width, Height = height, Margin = new Thickness(0,0,9,0),
                Clip = new RectangleGeometry(new Rect(0,0,width,height), photo.IsLocalCapture ? 0 : 11, photo.IsLocalCapture ? 0 : 11) };
            var image = new Image { Source = photo.Bitmap, Stretch = single || photo.IsLocalCapture ? Stretch.Uniform : Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            frame.Children.Add(image);
            if (photo.IsAnimated) new NIRAGifPlayback(image).SetPhoto(photo);
            if (!single && !photo.IsLocalCapture)
            {
                var shade = new Border { Height = 51, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
                    Background = new LinearGradientBrush(Color.FromArgb(0,9,21,43), Color.FromArgb(219,8,20,38), 90) };
                var caption = new StackPanel { Margin = new Thickness(10,6,10,6), VerticalAlignment = VerticalAlignment.Bottom };
                caption.Children.Add(new TextBlock { Text = photo.Title.Length == 0 ? "Photograph " + (i + 1) : photo.Title, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                caption.Children.Add(new TextBlock { Text = photo.Author.Length == 0 ? "View source" : photo.Author, Foreground = Brushes.LightBlue, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
                shade.Child = caption; frame.Children.Add(shade);
            }
            FrameworkElement visual = frame;
            if (photo.IsLocalCapture && !single)
            {
                var item = new StackPanel { Width = width + 9 };
                item.Children.Add(frame);
                item.Children.Add(Label(photo.Title, true));
                visual = item;
            }
            var open = new Button { Content = visual, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Style = null, ToolTip = photo.IsLocalCapture ? "Enlarge screenshot" : "Open image gallery", Cursor = System.Windows.Input.Cursors.Hand };
            open.Click += (_, _) => ShowPhotos(photos, index, owner);
            strip.Children.Add(open);
        }
        panel.Children.Add(new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (single)
        {
            panel.Children.Add(Label(photos[0].Title, true));
            if (photos[0].License.StartsWith("AI-generated", StringComparison.Ordinal)) panel.Children.Add(Label(photos[0].Credit, false));
        }
        if (photos.Any(photo => photo.IsLocalCapture))
            panel.Children.Add(Label("Local screenshot · select to enlarge. Not uploaded by the capture tool.", false));
        if (entries.Any(entry => entry.IsGenerated))
            panel.Children.Add(Label(photos.Any(p => p.IsAnimated) ? "AI-generated four-frame loop. Open the viewer to play or pause." : "AI-generated artwork is labeled in the viewer. Select an image to enlarge it.", false));
        if (entries.Any(entry => !entry.IsGenerated && !entry.IsLocalCapture))
            panel.Children.Add(Label("Select a photo to see its source and attribution. Image identity and usage rights have not been independently checked.", false));
        return panel;
    }

    internal static List<Photo> LoadPhotos(IEnumerable<ImageGalleryEntry> entries, string workspace, int maximum)
    {
        var photos = new List<Photo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (photos.Count >= maximum) break;
            try
            {
                var resolver = new WorkspacePathResolver(workspace);
                string path = resolver.Resolve(entry.Path);
                string cache = entry.IsLocalCapture ? Path.TrimEndingDirectorySeparator(resolver.Resolve(resolver.Root)) :
                    resolver.Resolve(entry.IsGenerated ? ImageGenerationTool.CacheFolder : ImagePreviewTool.CacheFolder);
                if (entry.IsLocalCapture && (entry.IsGenerated || entry.IsAnimated)) continue;
                if (!string.Equals(Path.GetDirectoryName(path), cache, StringComparison.OrdinalIgnoreCase) ||
                    !path.EndsWith(entry.IsGenerated && entry.IsAnimated ? ".gif" : ".png", StringComparison.OrdinalIgnoreCase)) continue;
                using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long maximumBytes = entry.IsLocalCapture ? DesktopWindowCaptureTool.MaximumBytes :
                    entry.IsGenerated ? ImageGenerationTool.MaxBytes : ImagePreviewTool.MaxBytes;
                if (input.Length < 1 || input.Length > maximumBytes) continue;
                byte[] bytes = new byte[(int)input.Length]; input.ReadExactly(bytes);
                string hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase) || seen.Contains(hash)) continue;
                if (entry.IsLocalCapture && !DesktopWindowCaptureTool.TryReadPngDimensions(bytes, out _, out _)) continue;
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                int maxDimension = entry.IsAnimated ? 960 : entry.IsGenerated ? 1920 : 1200;
                if (entry.IsLocalCapture ? !DesktopWindowCaptureTool.DimensionsAllowed(bitmap.PixelWidth, bitmap.PixelHeight) :
                    bitmap.PixelWidth > maxDimension || bitmap.PixelHeight > maxDimension) continue;
                seen.Add(hash);
                if (entry.IsAnimated)
                {
                    if (!entry.IsGenerated || entry.FrameDurationMs is < 100 or > 2000 || !bytes.AsSpan().StartsWith("GIF89a"u8)) continue;
                    using var gifStream = new MemoryStream(bytes, writable: false);
                    var gif = new GifBitmapDecoder(gifStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                    if (gif.Frames.Count != 4 || gif.Frames.Any(f => f.PixelWidth != bitmap.PixelWidth || f.PixelHeight != bitmap.PixelHeight)) continue;
                }
                photos.Add(new Photo(bitmap, entry.Title, entry.Author, entry.License, entry.Source,
                    entry.IsAnimated ? bytes : null, entry.FrameDurationMs, entry.IsLocalCapture));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or System.Runtime.InteropServices.COMException) { }
        }
        return photos;
    }

    private static TextBlock Label(string text, bool title)
    {
        var label = new TextBlock { Text = text, FontSize = title ? 12 : 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,4,0,0), FontWeight = title ? FontWeights.SemiBold : FontWeights.Normal };
        label.SetResourceReference(TextBlock.ForegroundProperty, title ? "NIRATextBrush" : "NIRASecondaryBrush"); return label;
    }

    internal static void ShowPhotos(IReadOnlyList<Photo> photos, int selected, Window owner)
    {
        if (photos.Count == 0) return;
        var viewer = new NIRAUserAttachmentGalleryWindow(photos, selected) { Owner = owner };
        viewer.ShowDialog();
    }
}

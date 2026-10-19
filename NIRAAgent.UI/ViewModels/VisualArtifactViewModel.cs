/*
 * filename: VisualArtifactViewModel.cs
 */

using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NIRAAgent.Artifacts;
using NIRAAgent.Diagnostics;
using NIRAAgent.Presentation;

namespace NIRAAgent.UI.ViewModels;

public sealed class VisualArtifactViewModel : INotifyPropertyChanged
{
    public NIRAVisualArtifact Artifact
    {
        get;
    }


    public Guid ArtifactId =>
        Artifact.ArtifactId;


    public string Title =>
        string.IsNullOrWhiteSpace(Artifact.Title)
            ? "Visual result"
            : Artifact.Title;


    public string Caption =>
        string.Join("\n", new[] { Artifact.Caption }
            .Concat(Artifact.Annotations.Select(a => a.Label))
            .Where(value => !string.IsNullOrWhiteSpace(value)));


    public bool HasCaption =>
        !string.IsNullOrWhiteSpace(Caption);


    public string LocalPath =>
        Artifact.LocalPath;

    // Source is optional provenance from an already verified visual artifact;
    // it does not cause a network request or manufacture an image preview.
    public string SourceUri => Artifact.SourceUri;
    public bool HasSourceUri => Uri.TryCreate(SourceUri, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp);

    // Structured labels are derived from already retrieved, attributed images.
    // Other visual artifacts keep their original caption rather than being
    // misidentified as stock photography or as a licensed reference image.
    private NIRAReferenceImageCredit Credit =>
        NIRAReferenceImageCreditPolicy.Read(Artifact.Caption, SourceUri);
    public bool HasStructuredCredit => Credit.IsStructured;
    public string CreditProvider => Credit.Provider;
    public string CreditCreator => Credit.Creator;
    public bool HasCreditCreator => Credit.HasCreator;
    public string CreditLicense => Credit.License;
    public bool HasCreditLicense => Credit.HasLicense;
    public string SourceActionLabel => Credit.LinkLabel;
    public string ReferenceCaption => Credit.IsStructured ? "" : Caption;
    public bool HasReferenceCaption => !Credit.IsStructured && HasCaption;


    public ImageSource? PreviewImage
    {
        get;
    }


    public bool HasPreview =>
        PreviewImage != null;

    // Never decode a full-resolution image in a WPF binding getter. WPF reads
    // FullImage during window.Show/layout, on the UI dispatcher; synchronous
    // file I/O and bitmap decoding here previously froze the entire app.
    private ImageSource? _fullImage;
    public ImageSource? FullImage => _fullImage ?? PreviewImage;

    // Called from the viewer's UI dispatcher. Only the expensive decode and
    // annotation composition run on a worker; the frozen result is delivered
    // back to the dispatcher, with the low-resolution preview shown meanwhile.
    public async Task LoadFullImageAsync(CancellationToken cancellationToken = default)
    {
        if (_fullImage != null || !HasPreview) return;
        cancellationToken.ThrowIfCancellationRequested();

        string path = LocalPath;
        NIRAVisualAnnotation[] annotations = Artifact.Annotations.ToArray();
        var timer = Stopwatch.StartNew();
        NIRADiagnosticTrace.Event("IMAGE-VIEWER", "Stage=DecodeStart");

        ImageSource? decoded = await Task.Run(() =>
            NIRAVisualAnnotationRenderer.Render(
                TryLoadPreview(path, decodePixelWidth: 1600), annotations),
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        if (decoded == null)
        {
            NIRADiagnosticTrace.Event("IMAGE-VIEWER", $"Stage=DecodeUnavailable ElapsedMs={timer.ElapsedMilliseconds}");
            return;
        }

        // A continuation from a window Loaded callback normally runs on the
        // dispatcher. Explicit dispatch also protects future non-UI callers.
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() => SetFullImage(decoded),
                DispatcherPriority.DataBind, cancellationToken);
        }
        else
        {
            SetFullImage(decoded);
        }
        NIRADiagnosticTrace.Event("IMAGE-VIEWER", $"Stage=DecodeReady ElapsedMs={timer.ElapsedMilliseconds}");
    }

    // Gallery navigation releases large images so inspecting many attachments
    // does not keep dozens of decoded bitmaps resident on an 8 GB PC.
    public void ReleaseFullImage()
    {
        if (_fullImage == null) return;
        _fullImage = null;
        OnPropertyChanged(nameof(FullImage));
    }

    private void SetFullImage(ImageSource source)
    {
        _fullImage = source;
        OnPropertyChanged(nameof(FullImage));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


    public VisualArtifactViewModel(
        NIRAVisualArtifact artifact)
    {
        Artifact =
            artifact
            ?? throw new ArgumentNullException(
                nameof(artifact));

        PreviewImage =
            NIRAVisualAnnotationRenderer.Render(
                TryLoadPreview(artifact.LocalPath), artifact.Annotations);
    }


    public static ImageSource? TryLoadPreview(
        string path,
        int decodePixelWidth = 960)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !File.Exists(path))
        {
            return null;
        }

        try
        {
            BitmapImage bitmap =
                new();

            bitmap.BeginInit();
            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;
            bitmap.CreateOptions =
                BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth =
                Math.Max(1, decodePixelWidth);
            bitmap.UriSource =
                new Uri(
                    Path.GetFullPath(path),
                    UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}

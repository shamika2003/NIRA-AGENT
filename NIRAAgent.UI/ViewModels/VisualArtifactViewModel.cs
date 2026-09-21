/*
 * filename: VisualArtifactViewModel.cs
 */

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NIRAAgent.Artifacts;

namespace NIRAAgent.UI.ViewModels;

public sealed class VisualArtifactViewModel
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
        Artifact.Caption;


    public bool HasCaption =>
        !string.IsNullOrWhiteSpace(Caption);


    public string LocalPath =>
        Artifact.LocalPath;


    public ImageSource? PreviewImage
    {
        get;
    }


    public bool HasPreview =>
        PreviewImage != null;


    public VisualArtifactViewModel(
        NIRAVisualArtifact artifact)
    {
        Artifact =
            artifact
            ?? throw new ArgumentNullException(
                nameof(artifact));

        PreviewImage =
            TryLoadPreview(
                artifact.LocalPath);
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

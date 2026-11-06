using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NIRAAgent.UI.ViewModels;

namespace NIRAAgent.UI.Presentation;

/// <summary>
/// Last-mile duplicate defense for NIRA's retrieved photographic references.
/// A content SHA or URL is not enough: thumbnails, resized/cropped copies and
/// different CDNs frequently carry the same photograph. Uses a bounded WPF
/// decode and a small perceptual difference hash; never uploads any pixels.
/// Presentation safety net only: provider provenance and authorization are
/// checked earlier in the core research pipeline.
/// </summary>
public static class NIRAVisualDuplicateGuard
{
    private static readonly object Sync = new();
    private static readonly List<(ulong Fingerprint, ulong Center, double Ratio, string Source)> Seen = new();
    private const int MaximumRecent = 256;
    private static readonly StringComparer Urls = StringComparer.OrdinalIgnoreCase;

    public static bool RegisterIfNovel(VisualArtifactViewModel visual)
    {
        if (visual.IsGeneratedImage || !visual.HasSourceUri || !visual.HasPreview)
            return true;
        string canonical = CanonicalSource(visual.SourceUri);
        (ulong Hash, ulong Center, double Ratio)? fingerprint = Fingerprint(visual.LocalPath);
        lock (Sync)
        {
            if (canonical.Length > 0 && Seen.Any(x => Urls.Equals(x.Source, canonical)))
                return false;
            if (fingerprint is { } observed && Seen.Any(previous =>
                Math.Abs(Math.Log(Math.Max(0.01, observed.Ratio) / Math.Max(0.01, previous.Ratio))) < 0.60 &&
                (BitOperations.PopCount(observed.Hash ^ previous.Fingerprint) <= 5 ||
                 BitOperations.PopCount(observed.Center ^ previous.Center) <= 5 ||
                 BitOperations.PopCount(observed.Hash ^ previous.Center) <= 3 ||
                 BitOperations.PopCount(observed.Center ^ previous.Fingerprint) <= 3)))
                return false;
            if (fingerprint is { } hash)
            {
                Seen.Add((hash.Hash, hash.Center, hash.Ratio, canonical));
                if (Seen.Count > MaximumRecent) Seen.RemoveRange(0, Seen.Count - MaximumRecent);
            }
            else if (canonical.Length > 0)
            {
                Seen.Add((0, 0, -1, canonical));
                if (Seen.Count > MaximumRecent) Seen.RemoveRange(0, Seen.Count - MaximumRecent);
            }
            return true;
        }
    }

    private static string CanonicalSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return string.Empty;
        // Ignore tracking/query fragments; do not conflate distinct files on
        // the same domain. The pixel hash remains the important identity.
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static (ulong Hash, ulong Center, double Ratio)? Fingerprint(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            BitmapImage original = new();
            original.BeginInit();
            original.CacheOption = BitmapCacheOption.OnLoad;
            original.DecodePixelWidth = 96;
            original.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            original.EndInit();
            if (original.PixelWidth < 9 || original.PixelHeight < 8) return null;
            double ratio = (double)original.PixelWidth / original.PixelHeight;
            // A bounded decoder (96px wide), converting to a conventional
            // 4-byte format. No full-resolution bitmap is kept in memory.
            var converted = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth, height = converted.PixelHeight;
            int stride = checked(width * 4);
            byte[] pixels = new byte[checked(height * stride)];
            converted.CopyPixels(pixels, stride, 0);
            int Luma(int x, int y)
            {
                int index = Math.Clamp(y, 0, height - 1) * stride +
                    Math.Clamp(x, 0, width - 1) * 4;
                return (pixels[index + 2] * 77 + pixels[index + 1] * 150 +
                    pixels[index] * 29) >> 8;
            }
            ulong HashWindow(double margin)
            {
                ulong code = 0;
                double span = 1.0 - margin * 2.0;
                for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int yPixel = Math.Min(height - 1,
                        (int)((margin + span * (y * 2.0 + 1) / 16) * height));
                    int left = Math.Min(width - 1,
                        (int)((margin + span * (x * 2.0 + 1) / 18) * width));
                    int right = Math.Min(width - 1,
                        (int)((margin + span * (x * 2.0 + 3) / 18) * width));
                    if (Luma(left, yPixel) > Luma(right, yPixel))
                        code |= 1UL << (y * 8 + x);
                }
                return code;
            }
            return (HashWindow(0), HashWindow(0.14), ratio);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
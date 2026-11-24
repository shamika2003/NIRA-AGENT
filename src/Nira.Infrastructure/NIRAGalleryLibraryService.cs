using System.Security.Cryptography;
using System.Text.Json;

namespace Nira.Infrastructure;

// Persistent and intentionally separate from NIRA's transient visual evidence and
// the original user-owned paths in conversation history. Paths in the catalog are
// relative to RootFolder. Web images remain in the independent receipt-backed cache.
public enum NIRAGalleryCategory { Uploaded, WebFound, AiGenerated }

public sealed record NIRAGalleryItem
{
    public Guid Id { get; init; }
    public NIRAGalleryCategory Category { get; init; }
    public string RelativePath { get; init; } = "";
    public string Title { get; init; } = "";
    public string Credit { get; init; } = "";
    public string SourceUri { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public DateTimeOffset AddedUtc { get; init; }
}

public sealed class NIRAGalleryLibraryService
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
    private const long MaxBytes = 40L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _catalogFile;
        private readonly List<NIRAGalleryItem> _items;
    private readonly string _root;

    public NIRAGalleryLibraryService(string configurationFolder)
    {
        var paths = new WorkspacePathResolver(configurationFolder);
        string settingsHome = paths.Resolve(configurationFolder);
        Directory.CreateDirectory(settingsHome);
        _catalogFile = paths.Resolve(Path.Combine(settingsHome, "gallery-catalog.json"));
        _root = paths.Resolve(Path.Combine(settingsHome, "gallery-library"));
        Directory.CreateDirectory(_root);
        _items = ReadJson<List<NIRAGalleryItem>>(_catalogFile) ?? new();
        _items = _items.Where(IsSafeItem).GroupBy(x => x.Id).Select(g => g.First()).ToList();
    }

    public string RootFolder { get { lock (_gate) return _root; } }
    public IReadOnlyList<NIRAGalleryItem> Items { get { lock (_gate) return _items.OrderByDescending(x => x.AddedUtc).ToArray(); } }

    public string ResolvePath(NIRAGalleryItem item)
    {
        if (!IsSafeItem(item)) throw new InvalidOperationException("Invalid gallery record path.");
        lock (_gate) return new WorkspacePathResolver(_root).Resolve(item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    // Imports an image into a durable COPY; never changes the original user file,
    // downloaded media cache, or archival chat attachment.
    public NIRAGalleryItem? Import(string path, NIRAGalleryCategory category,
        string? title = null, string? credit = null, string? sourceUri = null)
    {
        if (category != NIRAGalleryCategory.Uploaded)
            throw new ArgumentOutOfRangeException(nameof(category));
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose an absolute image path.", nameof(path));
        string source = new WorkspacePathResolver(_root).Resolve(path);
        var file = new FileInfo(source);
        if (!file.Exists || !Extensions.Contains(file.Extension) || file.Length < 8 || file.Length > MaxBytes || !HasImageSignature(source, file.Extension))
            return null;
        string hash;
        using (var stream = File.OpenRead(source)) hash = Convert.ToHexString(SHA256.HashData(stream));
        lock (_gate)
        {
            NIRAGalleryItem? existing = _items.FirstOrDefault(x => x.Category == category && x.Sha256 == hash && CopyMatches(x));
            if (existing != null) return existing;
            Guid id = Guid.NewGuid();
            string sub = category.ToString();
            string relative = sub + "/" + id.ToString("N") + file.Extension.ToLowerInvariant();
            string destination = new WorkspacePathResolver(_root).Resolve(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string partial = destination + ".partial";
            bool promoted = false, staged = false;
            try
            {
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (input.Length is < 8 or > MaxBytes) return null;
                    using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    staged = true;
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                using (var stream = File.OpenRead(partial))
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), hash, StringComparison.Ordinal))
                        throw new IOException("Gallery copy verification failed.");
                File.Move(partial, destination);
                promoted = true;
                NIRAGalleryItem item = new()
                {
                    Id = id, Category = category, RelativePath = relative,
                    Title = Truncate(string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(source) : title!, 180),
                    Credit = Truncate(credit ?? "", 500),
                    SourceUri = Uri.TryCreate(sourceUri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : "",
                    Sha256 = hash, AddedUtc = DateTimeOffset.UtcNow
                };
                _items.Add(item);
                try { WriteAtomic(_catalogFile, _items); }
                catch { _items.Remove(item); throw; }
                return item;
            }
            catch { if (staged && File.Exists(partial)) File.Delete(partial); if (File.Exists(destination) && promoted) File.Delete(destination); throw; }
        }
    }

    private bool CopyMatches(NIRAGalleryItem item)
    {
        try
        {
            using var stream = File.OpenRead(ResolvePath(item));
            return stream.Length <= MaxBytes && Convert.ToHexString(SHA256.HashData(stream)).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static bool IsSafeItem(NIRAGalleryItem item)
    {
        string relative = item.RelativePath.Replace('\\', '/');
        string[] segments = relative.Split('/');
        return item.Id != Guid.Empty && item.Category == NIRAGalleryCategory.Uploaded && item.Sha256.Length == 64 && item.Sha256.All(Uri.IsHexDigit) && segments.Length == 2 &&
            Enum.TryParse<NIRAGalleryCategory>(segments[0], out var cat) && cat == item.Category &&
            Path.GetFileName(segments[1]) == segments[1] &&
            segments[1].StartsWith(item.Id.ToString("N"), StringComparison.OrdinalIgnoreCase) &&
            Extensions.Contains(Path.GetExtension(segments[1])) && !relative.Contains("..", StringComparison.Ordinal);
    }
    private static bool HasImageSignature(string path, string extension)
    {
        Span<byte> bytes = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        if (stream.Read(bytes) < 8) return false;
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            ".png" => bytes[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}),
            ".gif" => bytes[..3].SequenceEqual("GIF"u8),
            ".bmp" => bytes[..2].SequenceEqual("BM"u8),
            _ => false
        };
    }
    private static string Truncate(string text, int max) { text = text.Trim(); return text.Length > max ? text[..max] : text; }
    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
    private static void WriteAtomic<T>(string path, T value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

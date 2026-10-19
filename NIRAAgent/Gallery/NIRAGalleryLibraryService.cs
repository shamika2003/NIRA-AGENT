using System.Security.Cryptography;
using System.Text.Json;

namespace NIRAAgent.Gallery;

// Persistent and intentionally separate from NIRA's transient visual evidence and
// the original user-owned paths in conversation history. Paths in the catalog are
// relative to RootFolder so moving the library does not invalidate its records.
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
    private readonly string _settingsFile;
    private List<NIRAGalleryItem> _items;
    private string _root;
    private sealed record Settings(string RootFolder);

    public NIRAGalleryLibraryService(string? configurationFolder = null)
    {
        string settingsHome = configurationFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NIRAAgent");
        Directory.CreateDirectory(settingsHome);
        _settingsFile = Path.Combine(settingsHome, "gallery-settings.json");
        _catalogFile = Path.Combine(settingsHome, "gallery-catalog.json");
        string defaultRoot = Path.Combine(settingsHome, "gallery-library");
        Settings? settings = ReadJson<Settings>(_settingsFile);
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(settings?.RootFolder) ? defaultRoot : settings.RootFolder);
        Directory.CreateDirectory(_root);
        _items = ReadJson<List<NIRAGalleryItem>>(_catalogFile) ?? new();
        _items = _items.Where(IsSafeItem).GroupBy(x => x.Id).Select(g => g.First()).ToList();
    }

    public string RootFolder { get { lock (_gate) return _root; } }
    public IReadOnlyList<NIRAGalleryItem> Items { get { lock (_gate) return _items.OrderByDescending(x => x.AddedUtc).ToArray(); } }

    public string ResolvePath(NIRAGalleryItem item)
    {
        if (!IsSafeItem(item)) throw new InvalidOperationException("Invalid gallery record path.");
        lock (_gate) return Path.Combine(_root, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    // Imports an image into a durable COPY; never changes the original user file,
    // downloaded media cache, or archival chat attachment.
    public NIRAGalleryItem? Import(string path, NIRAGalleryCategory category,
        string? title = null, string? credit = null, string? sourceUri = null)
    {
        if (category is not (NIRAGalleryCategory.Uploaded or NIRAGalleryCategory.WebFound or NIRAGalleryCategory.AiGenerated))
            throw new ArgumentOutOfRangeException(nameof(category));
        string source = Path.GetFullPath(path);
        var file = new FileInfo(source);
        if (!file.Exists || !Extensions.Contains(file.Extension) || file.Length < 8 || file.Length > MaxBytes || !HasImageSignature(source, file.Extension))
            return null;
        string hash;
        using (var stream = File.OpenRead(source)) hash = Convert.ToHexString(SHA256.HashData(stream));
        lock (_gate)
        {
            NIRAGalleryItem? existing = _items.FirstOrDefault(x => x.Category == category && x.Sha256 == hash && File.Exists(ResolvePath(x)));
            if (existing != null) return existing;
            Guid id = Guid.NewGuid();
            string sub = category.ToString();
            string relative = sub + "/" + id.ToString("N") + file.Extension.ToLowerInvariant();
            string destination = Path.Combine(_root, sub, id.ToString("N") + file.Extension.ToLowerInvariant());
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string partial = destination + ".partial";
            try
            {
                File.Copy(source, partial, overwrite: false);
                using (var stream = File.OpenRead(partial))
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), hash, StringComparison.Ordinal))
                        throw new IOException("Gallery copy verification failed.");
                File.Move(partial, destination);
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
            catch { if (File.Exists(partial)) File.Delete(partial); if (File.Exists(destination)) File.Delete(destination); throw; }
        }
    }

    // Source remains intact until ALL copies are verified, promoted and the new
    // settings are durably written. Any failure before commit cleans the new files.
    public void MoveLibrary(string requestedFolder)
    {
        if (string.IsNullOrWhiteSpace(requestedFolder)) throw new ArgumentException("Choose a folder.", nameof(requestedFolder));
        string target = Path.GetFullPath(requestedFolder.Trim());
        lock (_gate)
        {
            if (Same(target, _root)) return;
            if (IsInside(target, _root) || IsInside(_root, target))
                throw new InvalidOperationException("The new gallery directory cannot contain or be inside the current directory.");
            string config = Path.GetDirectoryName(_settingsFile)!;
            if (Same(target, config) || IsInside(config, target))
                throw new InvalidOperationException("Choose a gallery directory separate from NIRA's application settings.");
            if (Path.GetPathRoot(target) == target)
                throw new InvalidOperationException("Choose a folder, not a disk root.");
            Directory.CreateDirectory(target);
            string[] existing = Directory.GetFileSystemEntries(target);
            // Never mingle with a pre-existing library or unrelated files.
            if (existing.Length > 0)
                throw new InvalidOperationException("Choose an empty destination folder to prevent accidental overwrites.");
            string staged = Path.Combine(target, ".nira-gallery-migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staged);
            List<string> promotedFolders = new();
            try
            {
                foreach (var item in _items)
                {
                    string old = ResolvePath(item);
                    if (!File.Exists(old)) throw new FileNotFoundException("A gallery item is missing; no files were moved.", old);
                    string dest = Path.Combine(staged, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(old, dest);
                    using var stream = File.OpenRead(dest);
                    string hash = Convert.ToHexString(SHA256.HashData(stream));
                    if (!string.Equals(hash, item.Sha256, StringComparison.Ordinal))
                        throw new IOException("Gallery relocation checksum mismatch. Original gallery is unchanged.");
                }
                foreach (var category in Enum.GetValues<NIRAGalleryCategory>())
                {
                    string sourceCategory = Path.Combine(staged, category.ToString());
                    if (Directory.Exists(sourceCategory))
                    {
                        string promoted = Path.Combine(target, category.ToString());
                        Directory.Move(sourceCategory, promoted);
                        promotedFolders.Add(promoted);
                    }
                }
                string oldRoot = _root;
                // Catalog stores RELATIVE paths; only settings need updating.
                WriteAtomic(_settingsFile, new Settings(target));
                _root = target;
                // Deleting original copies is cleanup, not part of the transaction.
                // An incomplete cleanup is harmless: catalog points to new copies.
                foreach (var item in _items)
                {
                    try { File.Delete(Path.Combine(oldRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar))); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                foreach (var category in Enum.GetValues<NIRAGalleryCategory>())
                {
                    try { Directory.Delete(Path.Combine(oldRoot, category.ToString())); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch
            {
                // If settings committed the new root is already authoritative;
                // never erase the only valid committed library in that case.
                if (!Same(_root, target))
                    foreach (string dest in promotedFolders)
                    {
                        try { if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                throw;
            }
            finally
            {
                try { if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsSafeItem(NIRAGalleryItem item)
    {
        string relative = item.RelativePath.Replace('\\', '/');
        string[] segments = relative.Split('/');
        return item.Id != Guid.Empty && segments.Length == 2 &&
            Enum.TryParse<NIRAGalleryCategory>(segments[0], out var cat) && cat == item.Category &&
            Path.GetFileName(segments[1]) == segments[1] &&
            segments[1].StartsWith(item.Id.ToString("N"), StringComparison.OrdinalIgnoreCase) &&
            Extensions.Contains(Path.GetExtension(segments[1])) && !relative.Contains("..", StringComparison.Ordinal);
    }
    private static bool Same(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
    private static bool IsInside(string candidate, string ancestor) =>
        (Path.TrimEndingDirectorySeparator(candidate) + Path.DirectorySeparatorChar).StartsWith(
            Path.TrimEndingDirectorySeparator(ancestor) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

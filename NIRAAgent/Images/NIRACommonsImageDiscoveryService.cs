using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.Images;

// Commons is the ONLY live image-discovery provider. Neither the user nor the
// model can supply a download URL. A short-lived candidate ID is required.
public sealed record NIRACommonsImageCandidate
{
    public string CandidateId { get; init; } = "";
    public string Provider { get; init; } = "Wikimedia Commons";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public string License { get; init; } = "";
    public string LicenseUrl { get; init; } = "";
    public string SourceUri { get; init; } = "";
    public string MediaUrl { get; init; } = "";
    public string AlternateMediaUrl { get; init; } = "";
    public string SearchBatchId { get; init; } = "";
    public string SearchQuery { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed record NIRACommonsImageDownload(
    string LocalPath, string Title, string Caption, string SourceUri, string Sha256, long Bytes);

public sealed class NIRAImageProviderUnavailableException : InvalidOperationException
{
    public string FailureKind { get; }
    public NIRAImageProviderUnavailableException(string kind, Exception cause)
        : base($"IMAGE_PROVIDER_UNAVAILABLE: Commons image search failed ({kind}). " +
               "Do not retry this provider in the same task; report the limitation.", cause)
        => FailureKind = kind;
}

public sealed class NIRAImageRetrievalUnavailableException : InvalidOperationException
{
    public NIRAImageRetrievalUnavailableException(string reason, Exception? cause = null)
        : base($"IMAGE_RETRIEVAL_UNAVAILABLE: Could not obtain verified raster bytes ({reason}). " +
               "Do not retry image.retrieve in this task.", cause) { }
}

public sealed class NIRACommonsImageDiscoveryService : IDisposable
{
    private const int MaxApiBytes = 1_200_000;
    private const int MaxImageBytes = 12 * 1024 * 1024;
    private const int MaxCandidates = 120;
    private static readonly TimeSpan CandidateLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan SearchNetworkTimeout = TimeSpan.FromSeconds(9);
    public static readonly TimeSpan SearchFailureCooldown = TimeSpan.FromMinutes(2);
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly Dictionary<string, (NIRACommonsImageCandidate Candidate, DateTimeOffset Expires)> _candidates =
        new(StringComparer.Ordinal);
    private readonly string _cacheFolder;
    private DateTimeOffset _searchUnavailableUntilUtc = DateTimeOffset.MinValue;
    private string _lastSearchFailureKind = "";
    public bool UsesProductionImageTransport { get; }
    public string LastSearchStatus { get; private set; } = "NotRun";

    public NIRACommonsImageDiscoveryService(HttpClient? transport = null, bool? preferCommonsFirst = null)
    {
        _ownsClient = transport is null;
        UsesProductionImageTransport = transport is null || preferCommonsFirst == true;
        _http = transport ?? new HttpClient(CreateImageHttpHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        string? userAgent = Environment.GetEnvironmentVariable("NIRA_WIKIMEDIA_USER_AGENT");
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Length > 240 ||
            userAgent.Any(c => c < 32 || c > 126))
            userAgent = "NIRAAgent/1.0 (desktop Wikimedia Commons reference-image client)";
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _cacheFolder = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "NIRAAgent", "visual-artifacts", "commons-cache");
        Directory.CreateDirectory(_cacheFolder);
        try
        {
            foreach (string file in Directory.EnumerateFiles(_cacheFolder))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        NIRADiagnosticTrace.Event("IMAGE-SERVICE", "Provider=CommonsOnly ImageTransport=" + UsesProductionImageTransport);
    }

    // This existing transport was proven to work by the user's .NET Step 1.
    // Preserve TLS verification, system proxy behavior and existing IPv4 preference.
    private static SocketsHttpHandler CreateImageHttpHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = true,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectCallback = ConnectPreferIPv4Async
    };

    private static async ValueTask<Stream> ConnectPreferIPv4Async(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        IPAddress[] resolved = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        Exception? last = null;
        foreach (IPAddress address in resolved
            .Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .Distinct().Take(8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(TimeSpan.FromSeconds(3));
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                ex is SocketException or IOException or OperationCanceledException)
            {
                socket.Dispose();
                last = ex;
            }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("Commons connection unavailable", last);
    }

    public static string ClassifyProviderFailure(Exception exception) => exception switch
    {
        HttpRequestException e when e.StatusCode == HttpStatusCode.TooManyRequests => "RateLimited",
        HttpRequestException e when e.StatusCode == HttpStatusCode.Forbidden => "AccessDenied",
        HttpRequestException e when e.StatusCode == HttpStatusCode.ServiceUnavailable => "ServiceUnavailable",
        HttpRequestException e when e.StatusCode is not null => "HttpError",
        HttpRequestException => "NetworkError",
        OperationCanceledException => "Timeout",
        JsonException => "InvalidJSON",
        _ => "UnexpectedError"
    };

    // Not a subject-specific list. The model chooses the topic/query freely.
    // Keeping these methods avoids breaking unrelated callers of the old API.
    public static string CompactDiscoveryQuery(string query) => (query ?? "").Trim();
    public static string ShortDiscoveryQuery(string query) => CompactDiscoveryQuery(query);

    private static string SearchUrl(string query, int limit) =>
        "https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2" +
        "&generator=search&gsrnamespace=6&gsrlimit=" + Math.Clamp(limit * 4, 12, 50) +
        "&gsrsearch=" + Uri.EscapeDataString(query) +
        "&prop=imageinfo&iiprop=url%7Cmime%7Csize%7Cextmetadata&iiurlwidth=960" +
        "&iiextmetadatalanguage=en&" +
        "iiextmetadatafilter=Artist%7CLicenseShortName%7CLicenseUrl%7CImageDescription";

    public async Task<IReadOnlyList<NIRACommonsImageCandidate>> SearchAsync(
        string query, int limit, CancellationToken cancellationToken = default,
        bool photographsOnly = false)
    {
        query = (query ?? "").Trim();
        if (query.Length is < 2 or > 160 || query.Any(char.IsControl))
            throw new ArgumentException("Image query must have 2-160 visible characters.");
        if (limit is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(limit));

        await _requests.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow < _searchUnavailableUntilUtc)
            {
                NIRADiagnosticTrace.Event("IMAGE-PROVIDER",
                    $"Operation=Search Outcome=Cooldown Reason={_lastSearchFailureKind}");
                throw new NIRAImageProviderUnavailableException(_lastSearchFailureKind,
                    new InvalidOperationException("Previous Commons failure still cooling down"));
            }
            try
            {
                NIRADiagnosticTrace.Event("IMAGE-QUERY",
                    $"Stage=Initial Provider=CommonsOnly Query={DiagnosticText(query)} " +
                    $"Subject={DiagnosticText(SubjectDiscoveryQuery(query))} Requested={limit}");
                NIRADiagnosticTrace.Event("IMAGE-PROVIDER", "Operation=Search Stage=CommonsOnly Attempt=1");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(SearchNetworkTimeout);
                using var response = await _http.GetAsync(SearchUrl(query, limit),
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("Commons API returned an error", null, response.StatusCode);
                byte[] data = await ReadBoundedAsync(response.Content, MaxApiBytes, timeout.Token);
                // Provider search order is NOT evidence of topical relevance. A
                // photograph taken BY an instrument is different from a photo OF
                // that instrument. Rank using Commons file-title metadata before
                // attempting downloads. Fail closed if no subject match exists.
                string subject = SubjectDiscoveryQuery(query);
                string initialJson = Encoding.UTF8.GetString(data);
                var initial = ParseCommonsSearchResponse(initialJson, 50);
                var candidates = RankForSubject(initial, query, limit, photographsOnly);
                TraceSelection("Initial", query, initial, candidates, photographsOnly);

                // Distinguish a genuinely empty Commons search from a search
                // containing images that could not be decoded or displayed.
                // Never issue a pointless second network request for an empty
                // index. A bitmap retry applies only to unusable imageinfo.
                bool unusableMedia = initial.Count == 0 && HasImageInfoPages(data);
                if (unusableMedia)
                {
                    string bitmapQuery = query + " filetype:bitmap";
                    NIRADiagnosticTrace.Event("IMAGE-QUERY",
                        $"Stage=BitmapRecovery Query={DiagnosticText(bitmapQuery)} " +
                        $"Original={DiagnosticText(query)} Reason=UnusableImageInfo");
                    try
                    {
                        using var recovered = await _http.GetAsync(
                            SearchUrl(bitmapQuery, limit),
                            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                        if (recovered.IsSuccessStatusCode)
                        {
                            byte[] recoveredData = await ReadBoundedAsync(recovered.Content, MaxApiBytes, timeout.Token);
                            var bitmaps = ParseCommonsSearchResponse(Encoding.UTF8.GetString(recoveredData), 50);
                            candidates = RankForSubject(bitmaps, query, limit, photographsOnly);
                            TraceSelection("BitmapRecovery", query, bitmaps, candidates, photographsOnly);
                        }
                        else
                            NIRADiagnosticTrace.Event("IMAGE-QUERY",
                                $"Stage=BitmapRecovery Outcome=HTTP{(int)recovered.StatusCode}");
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                        ex is HttpRequestException or OperationCanceledException or JsonException)
                    {
                        NIRADiagnosticTrace.Event("IMAGE-QUERY",
                            $"Stage=BitmapRecovery Outcome=Unavailable Reason={ClassifyProviderFailure(ex)}");
                    }
                }

                // If real Commons candidates are visibly off-topic, search
                // the requested SUBJECT in file titles. Never transform this
                // into a topic-specific fallback or use a different provider.
                // The bitmap-recovery path and truly empty index are terminal.
                if (!unusableMedia && initial.Count > 0 &&
                    candidates.Count < Math.Min(limit, 2) && subject.Length >= 3 &&
                    !query.Contains("intitle:", StringComparison.OrdinalIgnoreCase))
                {
                    string focused = "intitle:\"" + subject.Replace("\"", "") + "\"";
                    NIRADiagnosticTrace.Event("IMAGE-QUERY",
                        $"Stage=FocusedTitle Query={DiagnosticText(focused)} Original={DiagnosticText(query)} " +
                        $"PreviouslyRelevant={candidates.Count}");
                    try
                    {
                        using var recovery = await _http.GetAsync(
                            SearchUrl(focused, limit), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                        if (recovery.IsSuccessStatusCode)
                        {
                            byte[] refined = await ReadBoundedAsync(recovery.Content, MaxApiBytes, timeout.Token);
                            var titleCandidates = ParseCommonsSearchResponse(Encoding.UTF8.GetString(refined), 50);
                            var combined = candidates.Concat(titleCandidates)
                                .GroupBy(item => item.SourceUri, StringComparer.OrdinalIgnoreCase)
                                .Select(group => group.First()).ToArray();
                            candidates = RankForSubject(combined, query, limit, photographsOnly);
                            TraceSelection("FocusedTitle", query, titleCandidates, candidates, photographsOnly);
                        }
                        else
                            NIRADiagnosticTrace.Event("IMAGE-QUERY",
                                $"Stage=FocusedTitle Outcome=HTTP{(int)recovery.StatusCode}");
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                        ex is HttpRequestException or OperationCanceledException or JsonException)
                    {
                        NIRADiagnosticTrace.Event("IMAGE-QUERY",
                            $"Stage=FocusedTitle Outcome=Unavailable Reason={ClassifyProviderFailure(ex)} " +
                            $"RetainedRelevant={candidates.Count}");
                    }
                }
                NIRADiagnosticTrace.Event("IMAGE-QUERY",
                    $"Stage=Final Query={DiagnosticText(query)} Relevant={candidates.Count} " +
                    $"NoOffTopicFallback=True");
                string batchId = Guid.NewGuid().ToString("N");
                DateTimeOffset expires = DateTimeOffset.UtcNow + CandidateLifetime;
                foreach (var candidate in candidates)
                {
                    var tagged = candidate with { SearchBatchId = batchId, SearchQuery = query };
                    _candidates[tagged.CandidateId] = (tagged, expires);
                }
                foreach (string old in _candidates.Where(x => x.Value.Expires < DateTimeOffset.UtcNow)
                    .Select(x => x.Key).ToArray()) _candidates.Remove(old);
                while (_candidates.Count > MaxCandidates)
                    _candidates.Remove(_candidates.MinBy(x => x.Value.Expires).Key);
                _lastSearchFailureKind = "";
                _searchUnavailableUntilUtc = DateTimeOffset.MinValue;
                LastSearchStatus = $"Commons=HTTP200 Accepted={candidates.Count}";
                NIRADiagnosticTrace.Event("IMAGE-PROVIDER",
                    $"Operation=Search Stage=CommonsOnly Outcome=HTTP200 Accepted={candidates.Count}");
                return candidates;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                string kind = ClassifyProviderFailure(ex);
                _lastSearchFailureKind = kind;
                LastSearchStatus = $"Commons={kind}";
                _searchUnavailableUntilUtc = DateTimeOffset.UtcNow + SearchFailureCooldown;
                NIRADiagnosticTrace.Event("IMAGE-PROVIDER",
                    $"Operation=Search Stage=CommonsOnly Outcome=Unavailable Reason={kind}");
                throw new NIRAImageProviderUnavailableException(kind, ex);
            }
        }
        finally { _requests.Release(); }
    }

    // Metadata matching is evidence of likely relevance, NOT image understanding.
    // General-purpose subject extraction removes request-language filler without
    // containing any named product, telescope, place, person or example topic.
    private static readonly HashSet<string> SearchFiller = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "for", "from", "me", "my", "to", "please", "show",
        "find", "get", "give", "some", "real", "actual", "genuine", "true", "two", "three",
        "one", "few", "several", "more", "good", "high", "quality", "photos", "photo",
        "photographs", "photograph", "pictures", "picture", "images", "image", "reference",
        "references", "look", "looking", "see", "can", "you", "would", "like", "with"
    };

    private static string[] SubjectTerms(string query) =>
        Regex.Matches((query ?? "").ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .Where(term => term.Length > 1 && !SearchFiller.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(14).ToArray();

    public static string SubjectDiscoveryQuery(string query) =>
        string.Join(" ", SubjectTerms(query));

    // Rank only candidates with evidence that the REQUESTED subject is depicted.
    // Search order is not a relevance score. These generic rules do not name any
    // particular telescope, landmark, product, provider or user test.
    public static IReadOnlyList<NIRACommonsImageCandidate> RankForSubject(
        IReadOnlyList<NIRACommonsImageCandidate> results, string query, int limit,
        bool photographsOnly = false)
    {
        string[] terms = SubjectTerms(query);
        if (terms.Length == 0 || limit < 1) return Array.Empty<NIRACommonsImageCandidate>();
        int required = photographsOnly ? terms.Length :
            terms.Length == 1 ? 1 : Math.Max(2, (int)Math.Ceiling(terms.Length * 0.6));
        string phrase = string.Join(" ", terms);
        return results.Select((image, position) =>
        {
            string title = Regex.Replace(image.Title.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
            string description = Regex.Replace(image.Description.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
            // Photography is a user-selected media type, not a telescope or
            // location keyword. A named object is not an authentic photo if
            // its filename describes a diagram, render, or scale model.
            bool artificial = photographsOnly && Regex.IsMatch(image.Title,
                @"\b(?:diagram|schematic|illustration|render(?:ing)?|blueprint|" +
                @"concept|artist(?:ic)?\s+(?:impression|depiction)|scale\s+model)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            // If a different primary subject occupies the title and the
            // queried object appears only in an attribution parenthesis,
            // do not treat it as a photograph OF the queried object.
            string rawTitle = image.Title.ToLowerInvariant();
            int parenthesis = rawTitle.IndexOf('(');
            int positionOfSubject = rawTitle.IndexOf(phrase, StringComparison.Ordinal);
            bool attributedOnly = photographsOnly && parenthesis >= 0 &&
                positionOfSubject > parenthesis;
            string[] words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int hits = terms.Count(term => words.Contains(term, StringComparer.OrdinalIgnoreCase));
            bool exactPhrase = title.Contains(phrase, StringComparison.OrdinalIgnoreCase);
            // "Taken by / using X" often means an image produced BY X, not OF X.
            bool producerCredit = Regex.IsMatch(title + " " + description,
                @"\b(?:photographed|captured|taken|imaged|observed|recorded|shot)\b.{0,48}\b" +
                @"(?:by|using|with|through)\s+(?:the\s+)?" +
                Regex.Escape(phrase) + @"\b", RegexOptions.IgnoreCase);
            if (producerCredit || artificial || attributedOnly) hits = 0;
            // An explicit full-subject title earns a stronger signal than a
            // gallery filename mentioning the same words among dozens of others.
            int extraWords = Math.Max(0, words.Length - terms.Length);
            double aspect = image.Height > 0 ? (double)image.Width / image.Height : 0;
            int pixelBonus = image.Width >= 800 && image.Height >= 500 ? 6 : 0;
            int compositionPenalty = aspect is > 0 and < 0.45 or > 4 ? 8 : 0;
            int score = hits * 25 + (exactPhrase ? 70 : 0)
                - Math.Min(32, extraWords * 2) + pixelBonus - compositionPenalty;
            return new { image, position, hits, score };
        })
        .Where(x => x.hits >= required)
        .OrderByDescending(x => x.score)
        .ThenBy(x => x.position)
        .Select(x => x.image)
        .GroupBy(x => x.SourceUri, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .Take(limit).ToArray();
    }

    private static string DiagnosticText(string text) =>
        Regex.Replace(text ?? "", @"[\p{Cc}\p{Cf}]", " ") is string safe
            ? safe[..Math.Min(180, safe.Length)] : "";

    private static void TraceSelection(string stage, string query,
        IReadOnlyList<NIRACommonsImageCandidate> offered,
        IReadOnlyList<NIRACommonsImageCandidate> selected,
        bool photographsOnly = false)
    {
        var relevant = RankForSubject(offered, query, Math.Max(1, offered.Count), photographsOnly);
        NIRADiagnosticTrace.Event("IMAGE-MATCH",
            $"Stage={stage} Query={DiagnosticText(query)} Offered={offered.Count} " +
            $"TopicallyEligible={relevant.Count} Selected={selected.Count} " +
            $"RejectedAsOffTopic={offered.Count - relevant.Count}");
        foreach (var item in offered.Take(20))
        {
            string reason = selected.Any(s => s.SourceUri == item.SourceUri)
                ? "Selected" : relevant.Any(r => r.SourceUri == item.SourceUri)
                    ? "PassedButNotSelected" : "RejectedOffTopic";
            NIRADiagnosticTrace.Event("IMAGE-CANDIDATE",
                $"Stage={stage} Decision={reason} " +
                $"Title={DiagnosticText(item.Title)} Source={DiagnosticText(item.SourceUri)} " +
                $"Media={DiagnosticText(item.MediaUrl)}");
        }
    }

    private static bool HasImageInfoPages(byte[] data)
    {
        using var document = JsonDocument.Parse(data);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array) return false;
        foreach (var page in pages.EnumerateArray())
            if (page.TryGetProperty("imageinfo", out var info) &&
                info.ValueKind == JsonValueKind.Array && info.GetArrayLength() > 0)
                return true;
        return false;
    }

    // Only official Wikimedia hosts, HTTPS, default port, and Commons media paths.
    // DO NOT reject a verified official media URL merely because MediaWiki's
    // thumbnail name/extension/query parameters changed. Pixel checks follow.
    public static bool IsPermittedMediaUri(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        if (!uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("thumb.wikimedia.org", StringComparison.OrdinalIgnoreCase)) return false;
        if (!uri.AbsolutePath.StartsWith("/wikipedia/commons/", StringComparison.OrdinalIgnoreCase))
            return false;
        // These URLs come from the Commons imageinfo API, not model/user input.
        // Wikimedia may append cache/version/transform parameters and rename
        // thumbnails. A fixed query-string pattern wrongly rejected live media.
        // Bound the URL size; keep HTTPS + exact trusted host + Commons path.
        // Final content type AND actual file signature are checked on download.
        if (uri.AbsoluteUri.Length > 2048 ||
            uri.Query.Length > 512 || uri.AbsolutePath.Length > 1600) return false;
        string query = Uri.UnescapeDataString(uri.Query);
        if (query.Contains("://", StringComparison.Ordinal) ||
            query.Contains("//", StringComparison.Ordinal) ||
            query.Contains("\\", StringComparison.Ordinal) ||
            query.Contains("redirect", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("returnto", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("url=", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // A trusted Wikimedia link need not have a conventional filename suffix:
    // the API uses resized thumbnails, encoded filenames and versioned URLs.
    // Do not infer image content from the URL alone. MediaWiki supplies the
    // original MIME and download verifies HTTP content type + raster magic.
    private static bool IsRasterMime(string mime) =>
        mime is "image/jpeg" or "image/png" or "image/gif" or "image/webp";

    private static bool HasRasterSuffix(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)) return false;
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNonRasterSuffix(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)) return false;
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".djvu", StringComparison.OrdinalIgnoreCase);
    }

    // An imageinfo thumbnail may represent a converted TIFF/SVG/PDF: allow
    // actual raster thumbnails but never accept a raw SVG merely for coming
    // from Wikimedia. Everything is byte-verified before appearing in chat.
    private static bool IsPermittedThumbnail(string? address, string mime) =>
        IsPermittedMediaUri(address) &&
        (HasRasterSuffix(address) ||
         (IsRasterMime(mime) && !HasNonRasterSuffix(address)));

    private static string ImageUrlDiagnostic(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "Missing";
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)) return "Malformed";
        if (uri.Scheme != Uri.UriSchemeHttps) return "NonHTTPS";
        if (uri.UserInfo.Length > 0 || !uri.IsDefaultPort) return "CredentialOrPort";
        if (!uri.Host.Equals("upload.wikimedia.org", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.Equals("thumb.wikimedia.org", StringComparison.OrdinalIgnoreCase))
            return "Host:" + uri.Host[..Math.Min(64, uri.Host.Length)];
        if (!uri.AbsolutePath.StartsWith("/wikipedia/commons/", StringComparison.OrdinalIgnoreCase))
            return "PathPrefix";
        if (!IsPermittedMediaUri(address)) return "URLShape";
        return HasNonRasterSuffix(address) ? "NonRasterExtension" : "TrustedMediaURL";
    }

    private static string Field(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    public static IReadOnlyList<NIRACommonsImageCandidate> ParseCommonsSearchResponse(string json, int limit = 8)
    {
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            return Array.Empty<NIRACommonsImageCandidate>();

        var matches = new List<(int Position, NIRACommonsImageCandidate Image)>();
        int withInfo = 0, rejectedUrl = 0, missingUrl = 0, rejectedSource = 0;
        var rejectionSamples = new List<string>(3);
        foreach (JsonElement page in pages.EnumerateArray())
        {
            string fileTitle = Field(page, "title");
            if (!fileTitle.StartsWith("File:", StringComparison.OrdinalIgnoreCase) ||
                fileTitle.Length is < 7 or > 305 || fileTitle.Any(char.IsControl)) continue;
            if (!page.TryGetProperty("imageinfo", out var infos) ||
                infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() == 0) continue;
            withInfo++;
            JsonElement info = infos[0];
            string thumb = Field(info, "thumburl");
            string original = Field(info, "url");
            string mime = Field(info, "mime").ToLowerInvariant();
            bool originalRaster = IsRasterMime(mime);
            string media = IsPermittedThumbnail(thumb, mime) ? thumb : "";
            // Missing/invalid thumbnails must not make an otherwise valid
            // raster ORIGINAL vanish. Wikimedia's original may be a PNG/JPEG
            // with a transformed, encoded or suffixless file URL.
            if (media.Length == 0 && originalRaster && IsPermittedMediaUri(original))
                media = original;
            if (media.Length == 0)
            {
                if (thumb.Length == 0 && original.Length == 0) missingUrl++;
                else rejectedUrl++;
                if (rejectionSamples.Count < 3)
                    rejectionSamples.Add($"mime={mime} thumb={ImageUrlDiagnostic(thumb)} original={ImageUrlDiagnostic(original)}");
                continue;
            }
            string source = Field(info, "descriptionurl");
            if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? src) ||
                src.Scheme != Uri.UriSchemeHttps || !src.IsDefaultPort ||
                src.UserInfo.Length > 0 || !src.Host.Equals("commons.wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
                !src.AbsolutePath.StartsWith("/wiki/File:", StringComparison.OrdinalIgnoreCase))
            {
                // Construct an official file-description URL from the API's File title.
                source = "https://commons.wikimedia.org/wiki/File:" +
                    Uri.EscapeDataString(fileTitle[5..].Trim().Replace(' ', '_'));
                rejectedSource++;
            }
            string alternative = originalRaster && IsPermittedMediaUri(original) && original != media
                ? original : "";
            JsonElement ext = info.TryGetProperty("extmetadata", out var metadata) &&
                metadata.ValueKind == JsonValueKind.Object ? metadata : default;
            string Meta(string key) => ext.ValueKind == JsonValueKind.Object &&
                ext.TryGetProperty(key, out var item) ? CleanHtml(Field(item, "value")) : "";
            int Int(string key) => info.TryGetProperty(key, out var item) &&
                item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int n) ? Math.Max(0, n) : 0;
            int position = page.TryGetProperty("index", out var index) &&
                index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out int n) ? n : int.MaxValue;
            matches.Add((position, new NIRACommonsImageCandidate
            {
                CandidateId = Guid.NewGuid().ToString("N"),
                Title = fileTitle[5..].Replace('_', ' ').Trim(),
                MediaUrl = media,
                AlternateMediaUrl = alternative,
                SourceUri = source,
                Author = Meta("Artist"),
                License = Meta("LicenseShortName"),
                LicenseUrl = Meta("LicenseUrl"),
                Description = Meta("ImageDescription"),
                Width = Int("width"), Height = Int("height")
            }));
        }
        NIRADiagnosticTrace.Event("IMAGE-COMMONS-PARSE",
            $"Pages={pages.GetArrayLength()} WithInfo={withInfo} Accepted={matches.Count} " +
            $"UrlRejected={rejectedUrl} MissingUrl={missingUrl} SourceRepaired={rejectedSource} " +
            $"RejectedSamples=[{string.Join("; ", rejectionSamples)}]");
        return matches.OrderBy(x => x.Position).ThenBy(x => x.Image.Title)
            .Select(x => x.Image).Take(limit).ToArray();
    }

    private static string CleanHtml(string value)
    {
        string text = WebUtility.HtmlDecode(Regex.Replace(value ?? "", "<[^>]*>", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 320 ? text[..320] : text;
    }

    public async Task<NIRACommonsImageDownload> RetrieveAsync(
        string candidateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidateId)) throw new ArgumentException("CandidateId is required");
        await _requests.WaitAsync(cancellationToken);
        try
        {
            foreach (string expired in _candidates.Where(x => x.Value.Expires < DateTimeOffset.UtcNow)
                .Select(x => x.Key).ToArray()) _candidates.Remove(expired);
            if (!_candidates.TryGetValue(candidateId, out var selected))
                throw new InvalidOperationException("Unknown or expired image candidate. Search first.");
            var images = new[] { selected.Candidate }.Concat(_candidates.Values
                .Where(x => x.Candidate.SearchBatchId == selected.Candidate.SearchBatchId &&
                    x.Candidate.CandidateId != candidateId)
                .Select(x => x.Candidate)).Take(8).ToArray();
            Exception? last = null;
            int attempts = 0;
            var tried = new HashSet<string>(StringComparer.Ordinal);
            using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            total.CancelAfter(TimeSpan.FromSeconds(21));
            foreach (var image in images)
            {
                foreach (string url in new[] { image.MediaUrl, image.AlternateMediaUrl })
                {
                    if (total.IsCancellationRequested) break;
                    if (!IsPermittedMediaUri(url) || !tried.Add(url)) continue;
                    attempts++;
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(6));
                        var result = await DownloadVerifiedAsync(image, url, timeout.Token);
                        NIRADiagnosticTrace.Event("IMAGE-RETRIEVE",
                            $"Outcome=Success Host={new Uri(url).Host} Bytes={result.Bytes} Attempts={attempts}");
                        return result;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                        ex is HttpRequestException or OperationCanceledException or IOException or InvalidOperationException)
                    {
                        last = ex;
                        NIRADiagnosticTrace.Event("IMAGE-RETRIEVE",
                            $"Outcome=CandidateFailed Reason={ClassifyProviderFailure(ex)} " +
                            $"Error={ex.GetType().Name} Host={new Uri(url).Host} Attempt={attempts}");
                    }
                }
            }
            throw new NIRAImageRetrievalUnavailableException($"attempted={attempts}", last);
        }
        finally { _requests.Release(); }
    }

    private async Task<NIRACommonsImageDownload> DownloadVerifiedAsync(
        NIRACommonsImageCandidate candidate, string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Commons image response is not successful", null, response.StatusCode);
        if (response.Content.Headers.ContentLength is > MaxImageBytes)
            throw new InvalidOperationException("Image larger than permitted budget");
        string contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType is not ("image/jpeg" or "image/png" or "image/gif" or "image/webp" or "application/octet-stream"))
            throw new InvalidOperationException("Response is not raster image media");
        byte[] bytes = await ReadBoundedAsync(response.Content, MaxImageBytes, ct);
        string extension = DetectImageExtension(bytes);
        if (extension.Length == 0) throw new InvalidOperationException("Invalid image signature");
        string path = Path.Combine(_cacheFolder, Guid.NewGuid().ToString("N") + extension);
        string temporary = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        string credit = string.IsNullOrWhiteSpace(candidate.Author) ? "Author listed on source" : candidate.Author;
        string license = string.IsNullOrWhiteSpace(candidate.License) ? "License on source page" : candidate.License;
        string caption = $"Wikimedia Commons • {credit} • {license}";
        if (Uri.TryCreate(candidate.LicenseUrl, UriKind.Absolute, out Uri? rights) &&
            rights.Scheme == Uri.UriSchemeHttps)
            caption += " • License: " + rights.AbsoluteUri;
        return new NIRACommonsImageDownload(path, candidate.Title, caption,
            candidate.SourceUri, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
    }

    private static string DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) return ".png";
        if (bytes.Length >= 6 && Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a") return ".gif";
        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" &&
            Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return ".webp";
        return "";
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int max, CancellationToken ct)
    {
        using Stream input = await content.ReadAsStreamAsync(ct);
        using MemoryStream output = new();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int count = await input.ReadAsync(buffer, ct);
            if (count == 0) break;
            if (output.Length + count > max) throw new InvalidOperationException("Response exceeds byte cap");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
        _requests.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NIRAAgent.Capabilities;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.Images;

/// <summary>
/// Tavily image discovery for BOTH image-only requests and images from a web
/// research receipt. A combined web+image request feeds this service the SAME
/// JSON response: no separate Tavily image search, browser, or LLM is needed.
/// Discovered URLs remain untrusted; only bounded verified raster bytes display.
/// </summary>
public sealed class NIRATavilyImageDiscoveryService : INIRAImageDiscoveryService, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly bool _testTransport;
    private readonly Func<string, string?> _environment;
    private readonly string _folder;
    private readonly object _sync = new();
    private readonly Dictionary<string, (NIRACommonsImageCandidate Candidate, DateTimeOffset Expiry)> _issued = new();
    private readonly Dictionary<string, (IReadOnlyList<NIRACommonsImageCandidate> Items, DateTimeOffset Expiry)> _queryCache =
        new(StringComparer.OrdinalIgnoreCase);
    // Different visual subjects can search concurrently; identical queries
    // share a per-key gate, preventing duplicate Tavily requests/credits.
    // The global budget limits simultaneous HTTP searches to three.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _queryGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _parallelSearchGate = new(3, 3);
    private static readonly TimeSpan CandidateTtl = TimeSpan.FromMinutes(30);
    public string LastSearchStatus { get; private set; } = "NotRun";

    public NIRATavilyImageDiscoveryService()
        : this(null, null, null) { }

    // Same injection seam as the existing .NET fake-HTTP regression probes.
    internal NIRATavilyImageDiscoveryService(HttpClient? transport,
        Func<string, string?>? environment = null, string? cacheFolder = null)
    {
        _ownsClient = transport is null;
        _testTransport = transport is not null;
        _http = transport ?? new HttpClient(new SocketsHttpHandler {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectCallback = ConnectPublicEndpointAsync
        }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _environment = environment ?? (name =>
            NIRAWebSearchCapabilityHandler.ResolveEnvironmentFromScopes(name,
                Environment.GetEnvironmentVariable,
                key => OperatingSystem.IsWindows() ?
                    Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User) : null));
        _folder = cacheFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NIRAAgent", "visual-artifacts", "tavily-cache");
        Directory.CreateDirectory(_folder);
        NIRADiagnosticTrace.Event("IMAGE-SERVICE", "Provider=Tavily Mode=ImagesOnlyOrSharedWebEvidence");
    }

    private string Key()
    {
        string? main = _environment("NIRA_TAVILY_API_KEY");
        return (!string.IsNullOrWhiteSpace(main) ? main :
            _environment("TAVILY_API_KEY") ?? "").Trim();
    }

    public async Task<IReadOnlyList<NIRACommonsImageCandidate>> SearchAsync(
        string query, int limit, CancellationToken cancellationToken = default, bool photographsOnly = false)
    {
        query = (query ?? "").Trim();
        if (query.Length is < 2 or > 160 || query.Any(char.IsControl))
            throw new ArgumentException("Image query must have 2-160 visible characters.");
        if (limit is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(limit));
        string key = Key();
        if (key.Length == 0)
            throw new InvalidOperationException("NIRA_TAVILY_API_KEY is missing. Use the existing web search API key.");
        string cacheKey = query.ToLowerInvariant() + "|" + photographsOnly + "|" + limit;
        SemaphoreSlim queryGate = _queryGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await queryGate.WaitAsync(cancellationToken);
        try
        {
            // Check inside the query-specific gate: concurrent duplicate requests
            // can reuse the first response instead of consuming extra credits.
            lock (_sync)
            {
                if (_queryCache.TryGetValue(cacheKey, out var hit) &&
                    hit.Expiry > DateTimeOffset.UtcNow)
                {
                    LastSearchStatus = $"Tavily=CacheHit Accepted={hit.Items.Count}";
                    NIRADiagnosticTrace.Event("IMAGE-PROVIDER",
                        $"Operation=Search Provider=Tavily Outcome=CacheHit Credits=0 Accepted={hit.Items.Count}");
                    return hit.Items;
                }
            }
            await _parallelSearchGate.WaitAsync(cancellationToken);
            try { return await SearchUncachedAsync(query, limit, photographsOnly, cacheKey, key, cancellationToken); }
            finally { _parallelSearchGate.Release(); }
        }
        finally { queryGate.Release(); }
    }

    private async Task<IReadOnlyList<NIRACommonsImageCandidate>> SearchUncachedAsync(
        string query, int limit, bool photographsOnly, string cacheKey, string key,
        CancellationToken cancellationToken)
    {
        // One request, not a Commons-style retry cascade. Basic = 1 Tavily credit.
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(new {
            query, search_depth = "basic", max_results = 8, include_answer = false,
            include_raw_content = false, include_images = true,
            include_image_descriptions = true
        }), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(22));
        NIRADiagnosticTrace.Event("IMAGE-PROVIDER", "Operation=Search Provider=Tavily Calls=1");
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Tavily image search HTTP error", null, response.StatusCode);
            byte[] bytes = await ReadBoundedAsync(response.Content, 2_000_000, timeout.Token);
            using var doc = JsonDocument.Parse(bytes);
            var items = AcceptTavilyResponse(doc.RootElement, query, limit, photographsOnly);
            lock (_sync)
            {
                if (_queryCache.Count > 100) _queryCache.Clear();
                _queryCache[cacheKey] = (items, DateTimeOffset.UtcNow.AddMinutes(8));
            }
            LastSearchStatus = $"Tavily=HTTP200 Accepted={items.Count}";
            return items;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            LastSearchStatus = "Tavily=Unavailable:" + ex.GetType().Name;
            NIRADiagnosticTrace.Event("IMAGE-PROVIDER",
                $"Operation=Search Provider=Tavily Outcome=Failed Kind={ex.GetType().Name}");
            throw new NIRAImageProviderUnavailableException("Tavily" + ex.GetType().Name, ex);
        }
    }

    // Called directly from web.search while its successful Tavily JSON is in
    // memory. This is NOT a second provider call, even when imageQuery differs
    // from the research query. Include only returned, attributable image URLs.
    public IReadOnlyList<NIRACommonsImageCandidate> AcceptTavilyResponse(JsonElement root,
        string requestedVisualSubject, int limit, bool photographsOnly = false)
    {
        var offered = new List<NIRACommonsImageCandidate>();
        void Add(JsonElement image, string pageUrl, string pageTitle)
        {
            string url = ImageField(image, "url");
            if (url.Length == 0 && image.ValueKind == JsonValueKind.String)
                url = image.GetString() ?? "";
            if (!PublicImageUrl(url)) return;
            string caption = ImageField(image, "description");
            // A page headline may accurately describe an article while its
            // OpenGraph/hero image depicts a different subject. Neither the
            // page title nor search query is pixel identity evidence. Require
            // the actual image caption or its OWN media filename to identify it.
            string mediaName = Uri.UnescapeDataString(new Uri(url).Segments.Last());
            string title = !string.IsNullOrWhiteSpace(caption) ? caption : mediaName;
            title = OneLine(title, 175);
            if (title.Length == 0) return;
            if (photographsOnly && LooksLikeNonPhotograph(title, caption, url)) return;
            string source = PublicPageUrl(pageUrl) ? pageUrl : url;
            offered.Add(new NIRACommonsImageCandidate {
                CandidateId = Guid.NewGuid().ToString("N"), Provider = "Tavily",
                Title = title, Description = OneLine(caption, 350),
                Author = "", License = "Rights not verified; consult source",
                SourceUri = source, MediaUrl = url, SearchQuery = requestedVisualSubject
            });
        }
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("results", out JsonElement results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                string source = ImageField(result, "url");
                string title = ImageField(result, "title");
                if (result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                    foreach (var image in images.EnumerateArray()) Add(image, source, title);
            }
        }
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("images", out var all) && all.ValueKind == JsonValueKind.Array)
            foreach (var image in all.EnumerateArray())
                Add(image, ImageField(image, "source_url"), ImageField(image, "description"));

        // Tavily performs semantic ranking. Prefer identifiable results with
        // matching metadata; never fabricate a subject or require exact titles.
        // Separate generic photo/search phrasing from subject identity. A
        // query beginning with "photographs of" must still find a picture whose
        // caption starts with the real subject name. Never hardcode entities.
        string[] searchBoilerplate = {
            "photo", "photos", "photograph", "photographs", "photographic",
            "images", "image", "real", "reference", "pictures", "picture",
            "official", "authentic", "actual", "showing", "published",
            "resolution", "clear", "imageof", "closeup", "view", "views",
            "exterior", "interior", "portrait", "overview", "visible"
        };
        var tokens = System.Text.RegularExpressions.Regex.Matches(
                requestedVisualSubject, @"[\p{L}\p{N}]{3,}")
            .Select(m => m.Value)
            .Where(t => t.Length > 3 && !searchBoilerplate.Contains(t,
                StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int MatchCount(NIRACommonsImageCandidate candidate) => tokens.Count(t =>
            (candidate.Title + " " + candidate.Description + " " + candidate.SourceUri)
                .Contains(t, StringComparison.OrdinalIgnoreCase));
        // Tavily's result description can mention the SEARCH query while the
        // actual image title identifies a DIFFERENT animal/object. That caused
        // a clownfish image to be labeled "Anglerfish" in the October 25 run.
        // A real photo is useful only when its own title or original filename
        // identifies the requested subject. No topic-specific allowlist.
        static bool SubjectInTitleOrFilename(NIRACommonsImageCandidate candidate, string[] terms)
        {
            if (terms.Length == 0) return false;
            string path = Uri.TryCreate(candidate.MediaUrl, UriKind.Absolute, out var url)
                ? Uri.UnescapeDataString(url.AbsolutePath) : string.Empty;
            // A candidate's description and the page URL are deliberately NOT
            // sufficient proof of identity: they can contain generic keywords.
            // Some captions append a generic keyword/search paragraph after
            // naming the *actual* different subject in their lead sentence.
            // Do not accept a misleading trailing reference as photo identity.
            // The first descriptive clause identifies the pictured subject.
            // Metadata after commas or semicolons can contain search keywords
            // or page context unrelated to the actual photograph. Counting
            // those keywords caused an unrelated search result to pass P2.4.
            string leadTitle = candidate.Title.Split(new[] { '.', '!', '?', ',', ';' }, 2)[0];
            string identity = leadTitle + " " + path.Replace('_', ' ').Replace('-', ' ');
            // First meaningful query token is the strongest identifier;
            // extra matching terms improve ranking below.
            string identifier = terms[0];
            // An explicitly unidentified image, search-results page or keyword
            // listing is not evidence that the picture depicts this subject.
            if (System.Text.RegularExpressions.Regex.IsMatch(leadTitle,
                @"\b(?:unidentified|unknown image|search (?:article|results|page|listing)|keyword listing)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return false;
            // For compound subjects, a generic first token ("giant",
            // "electric") is insufficient: also identify a second subject
            // term in the lead title or original image filename.
            int matched = terms.Count(term => System.Text.RegularExpressions.Regex.IsMatch(identity,
                @"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(term) +
                @"(?![\p{L}\p{N}])",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant));
            return matched >= Math.Min(2, terms.Length) &&
                System.Text.RegularExpressions.Regex.IsMatch(identity,
                    @"(?<![\p{L}\p{N}])" + System.Text.RegularExpressions.Regex.Escape(identifier) +
                    @"(?![\p{L}\p{N}])",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        // Phase 2 ranking: relevance is still mandatory, but do not let six
        // near-identical thumbnails from one blocked CDN crowd out otherwise
        // relevant original photographs hosted elsewhere. A photo's verified
        // source and actual downloaded bytes still govern whether it displays.
        static bool IsConceptArtwork(string value) =>
            System.Text.RegularExpressions.Regex.IsMatch(value,
                @"\b(?:illustration|illustrated|cgi|render(?:ing|ed)?|concept art|ai.generated|" +
                @"synthetic image|artist.s impression|stock illustration|3d model)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        int Quality(NIRACommonsImageCandidate candidate)
        {
            string metadata = candidate.Title + " " + candidate.Description;
            int score = IsConceptArtwork(metadata) ? -7 : 0;
            if (System.Text.RegularExpressions.Regex.IsMatch(metadata,
                @"\b(?:photograph|photographed|documentary|real photo|field photo|museum specimen)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)) score += 2;
            return score - WeakPresentationPenalty(candidate.Title, candidate.Description,
                candidate.MediaUrl, candidate.SourceUri);
        }
        var pool = offered
            .GroupBy(x => x.MediaUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(x => tokens.Length == 0 || MatchCount(x) > 0)
            .Where(x => SubjectInTitleOrFilename(x, tokens))
            .Where(x => !photographsOnly || !IsConceptArtwork(x.Title + " " + x.Description))
            .Where(x => !photographsOnly || !IsUnrequestedMiniatureOrRetail(x,
                requestedVisualSubject))
            .Where(x => !photographsOnly || WeakPresentationPenalty(x.Title, x.Description, x.MediaUrl, x.SourceUri) < 6)
            .OrderByDescending(x => MatchCount(x) * 5 + Quality(x))
            .ToList();
        var selected = new List<NIRACommonsImageCandidate>();
        var chosenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int target = Math.Clamp(limit, 1, 12);
        while (selected.Count < target && pool.Count > 0)
        {
            var first = pool[0];
            int firstScore = MatchCount(first) * 5 + Quality(first);
            // A different public host must still have nearly equivalent
            // topical evidence: never favor diversity over a valid subject.
            var alternate = pool.FirstOrDefault(x =>
                !chosenHosts.Contains(new Uri(x.MediaUrl).Host) &&
                MatchCount(x) * 5 + Quality(x) >= firstScore - 5);
            var next = alternate ?? first;
            selected.Add(next);
            chosenHosts.Add(new Uri(next.MediaUrl).Host);
            pool.Remove(next);
        }
        var ranked = selected.ToArray();
        lock (_sync)
        {
            foreach (var old in _issued.Where(x => x.Value.Expiry <= DateTimeOffset.UtcNow)
                .Select(x => x.Key).ToArray()) _issued.Remove(old);
            foreach (var candidate in ranked)
                _issued[candidate.CandidateId] = (candidate, DateTimeOffset.UtcNow + CandidateTtl);
            while (_issued.Count > 150) _issued.Remove(_issued.Keys.First());
        }
        NIRADiagnosticTrace.Event("IMAGE-MATCH",
            $"Provider=Tavily Query={OneLine(requestedVisualSubject, 150)} Offered={offered.Count} Selected={ranked.Length}");
        return ranked;
    }

    public async Task<NIRACommonsImageDownload> RetrieveAsync(string candidateId,
        CancellationToken cancellationToken = default)
    {
        NIRACommonsImageCandidate candidate;
        lock (_sync)
        {
            if (!_issued.TryGetValue(candidateId, out var entry) ||
                entry.Expiry <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("image.retrieve requires a live candidateId from image.search.");
            candidate = entry.Candidate;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(11));
        string stage = "UrlAndDns";
        try
        {
            // Explicit DNS check is important: image URLs originate in third-
            // party search results. Never request a local or reserved address.
            if (!PublicImageUrl(candidate.MediaUrl) ||
                (!_testTransport && !await HasPublicDnsAsync(new Uri(candidate.MediaUrl).Host, timeout.Token)))
                throw new InvalidOperationException("Unsafe image URL or address");
            stage = "HttpDownload";
            using var response = await _http.GetAsync(candidate.MediaUrl,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("Image HTTP error", null, response.StatusCode);
            stage = "ContentType";
            string mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (mime is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif" or "application/octet-stream"))
                throw new InvalidOperationException("Not a raster image");
            stage = "ContentRead";
            byte[] bytes = await ReadBoundedAsync(response.Content, 12 * 1024 * 1024, timeout.Token);
            stage = "RasterSignature";
            string extension = Extension(bytes);
            if (extension.Length == 0) throw new InvalidOperationException("Invalid image signature");
            // A real 640-byte CC license glyph has a valid PNG signature.
            // Never present tiny icons/badges as photographic evidence. Keep
            // fake offline transports exempt so contract tests stay deterministic.
            if (!_testTransport && bytes.Length < 8192)
                throw new InvalidOperationException("Raster is too small for a reference photograph");
            // WPF artifact presenter cannot decode WebP; retry another candidate rather than silently reject later.
            if (extension == ".webp")
                throw new InvalidOperationException("WebP not supported by the current WPF image presenter");
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            stage = "CacheWrite";
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + extension);
            await File.WriteAllBytesAsync(path, bytes, timeout.Token);
            string caption = "Tavily image discovery • Source page: " + candidate.SourceUri +
                " • Copyright/license must be checked at source";
            NIRADiagnosticTrace.Event("IMAGE-RETRIEVE",
                $"Provider=Tavily Outcome=Success Host={new Uri(candidate.MediaUrl).Host} Bytes={bytes.Length}");
            return new NIRACommonsImageDownload(path, candidate.Title, caption,
                candidate.SourceUri, hash, bytes.Length);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException or InvalidOperationException or IOException)
        {
            // Keep diagnostics actionable but do not log signed query URLs,
            // search snippets, DNS addresses or private proxy configuration.
            string host = Uri.TryCreate(candidate.MediaUrl, UriKind.Absolute, out var imageUri)
                ? imageUri.Host : "Invalid";
            string httpStatus = ex is HttpRequestException requestException &&
                requestException.StatusCode is { } status
                    ? ((int)status).ToString() : "None";
            NIRADiagnosticTrace.Event("IMAGE-RETRIEVE",
                $"Provider=Tavily Outcome=Failed Stage={stage} Host={host} " +
                $"Kind={ex.GetType().Name} HttpStatus={httpStatus} " +
                $"Timeout={timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested}");
            throw new NIRAImageRetrievalUnavailableException(ex.GetType().Name, ex);
        }
    }

    // Metadata can identify clearly non-photographic material, even on reputable websites.
        // Prefer clean reference photos over video thumbnails, social previews or
    // headline graphics. This is ranking/filtering only: the subject itself is
    // still established by the search result metadata and later verified bytes.
    // A genuine photograph of a toy box or model train is still the WRONG
    // visual for a request about full-sized engineering equipment. Allow such
    // objects when explicitly requested; no manufacturer/animal name lists.
    internal static bool IsUnrequestedMiniatureOrRetail(
        NIRACommonsImageCandidate candidate, string requestedVisualSubject)
    {
        const System.Text.RegularExpressions.RegexOptions rx =
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant;
        string subject = requestedVisualSubject ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(subject,
            @"\b(?:toy|toys|miniature|model\s+train|scale\s+model|collectible|replica|packaging)\b", rx))
            return false;
        string mediaPath = Uri.TryCreate(candidate.MediaUrl, UriKind.Absolute, out var image)
            ? Uri.UnescapeDataString(image.AbsolutePath) : "";
        string sourcePath = Uri.TryCreate(candidate.SourceUri, UriKind.Absolute, out var page)
            ? Uri.UnescapeDataString(page.AbsolutePath) : "";
        string titleAndFile = candidate.Title + " " + mediaPath + " " + sourcePath;
        if (System.Text.RegularExpressions.Regex.IsMatch(titleAndFile,
            @"\b(?:model[-\s]+train|toy[-\s]+train|scale[-\s]+model|miniature[-\s]+model|" +
            @"model[-\s]+railway|model[-\s]+railroad|ho[-\s]+scale|" +
            @"train[-\s]+set|retail[-\s]+box|product[-\s]+packaging|" +
            @"diecast[-\s]+model|collectible[-\s]+figure)\b", rx)) return true;
        if (page is not null && (page.Host.StartsWith("shop.", StringComparison.OrdinalIgnoreCase) ||
            page.Host.StartsWith("store.", StringComparison.OrdinalIgnoreCase)) &&
            System.Text.RegularExpressions.Regex.IsMatch(sourcePath,
                @"/(?:products?|collections?|shop|store)/", rx)) return true;
        return false;
    }

    internal static int WeakPresentationPenalty(string title, string description, string mediaUrl, string sourceUrl)
    {
        string haystack = (title + " " + description).ToLowerInvariant();
        string host = "";
        string filename = "";
        if (Uri.TryCreate(mediaUrl, UriKind.Absolute, out var media))
        {
            host = media.Host.ToLowerInvariant();
            filename = Uri.UnescapeDataString(media.AbsolutePath.Split('/').Last()).ToLowerInvariant();
        }
        string sourceHost = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source)
            ? source.Host.ToLowerInvariant() : "";
        int penalty = 0;
        string[] noisyPhrases = {
            "full guide", "guide", "how ", "what's that", "all things",
            "explained", "watch", "video", "thumbnail", "preview image",
            "banner", "article cover", "social preview", "poster", "headline", "promo"
        };
        penalty += noisyPhrases.Count(phrase => haystack.Contains(phrase, StringComparison.Ordinal));
        if (host.Contains("ytimg", StringComparison.Ordinal) || filename.Contains("maxresdefault", StringComparison.Ordinal))
            penalty += 5;
        if (host.Contains("redditmedia", StringComparison.Ordinal) || host.Contains("redd.it", StringComparison.Ordinal) ||
            host.Contains("pinimg", StringComparison.Ordinal) || host.Contains("imgur", StringComparison.Ordinal))
            penalty += 4;
        if (host.Contains("shopify", StringComparison.Ordinal) || sourceHost.Contains("shopify", StringComparison.Ordinal))
            penalty += 2;
        if (filename.Contains("thumb", StringComparison.Ordinal) || filename.Contains("preview", StringComparison.Ordinal) ||
            filename.Contains("banner", StringComparison.Ordinal) || filename.Contains("title", StringComparison.Ordinal) ||
            filename.Contains("og-image", StringComparison.Ordinal) || filename.Contains("social", StringComparison.Ordinal) ||
            filename.Contains("cover", StringComparison.Ordinal) || filename.Contains("poster", StringComparison.Ordinal))
            penalty += 5;
        return penalty;
    }

internal static bool LooksLikeNonPhotograph(string title, string description, string url)
    {
        string metadata = (title + " " + description).ToLowerInvariant();
        string filename = "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            filename = Uri.UnescapeDataString(uri.AbsolutePath.Split('/').Last()).ToLowerInvariant();
        string[] markers = { "illustration", "diagram", "rendering", "3d render",
            "infographic", "presentation slide", "title slide", "poster", "logo",
            "brochure", "cover art", "vector art", "generated by ai", "ai generated",
            "license", "licence", "creative commons", "cc by", "watermark",
            "icon", "badge", "site favicon", "button graphic" };
        return markers.Any(term => metadata.Contains(term, StringComparison.Ordinal)) ||
            markers.Any(term => filename.Contains(term.Replace(" ", "-"), StringComparison.Ordinal) ||
                                filename.Contains(term.Replace(" ", "_"), StringComparison.Ordinal)) ||
            System.Text.RegularExpressions.Regex.IsMatch(filename,
                @"(?:^|[_\-.])(?:by_small|cc_by|favicon|license|icon)(?:[_\-.]|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string ImageField(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var field) &&
        field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : "";
    private static string OneLine(string text, int max) => new string((text ?? "").Where(c => !char.IsControl(c)).Take(max).ToArray()).Trim();
    public static bool PublicPageUrl(string? raw) => PublicUrl(raw, false);
    public static bool PublicImageUrl(string? raw) => PublicUrl(raw, true);
    private static bool PublicUrl(string? raw, bool media)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.AbsoluteUri.Length > 2048 ||
            uri.Host.Length < 4 || uri.Host.IndexOf('.') < 1 || IPAddress.TryParse(uri.Host, out _) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".example", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        string path = uri.AbsolutePath.ToLowerInvariant();
        if (media && (path.EndsWith(".svg") || path.EndsWith(".pdf") ||
            path.EndsWith(".tiff") || path.EndsWith(".tif"))) return false;
        return true;
    }

    private static bool PublicIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal)
            return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !ip.IsIPv4MappedToIPv6 && ip.GetAddressBytes()[0] is not (0xfc or 0xfd) &&
                !ip.Equals(IPAddress.IPv6Any) && !ip.Equals(IPAddress.IPv6None);
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
            !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
            !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) &&
            !(b[0] == 192 && b[1] == 0) && !(b[0] == 198 && b[1] is 18 or 19);
    }
    private static async Task<bool> HasPublicDnsAsync(string host, CancellationToken ct)
    {
        if (host == "api.tavily.com") return true;
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        return addresses.Length > 0 && addresses.All(PublicIp);
    }
    private static async ValueTask<Stream> ConnectPublicEndpointAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var ips = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        // Endpoint may be a configured HTTP proxy: never connect to an unsafe
        // endpoint even when it is resolving a provider-controlled hostname.
        foreach (var ip in ips.Where(PublicIp).OrderBy(ip => ip.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); }
        }
        throw new HttpRequestException("Public HTTPS connection unavailable");
    }
    private static string Extension(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 255 && b[1] == 216 && b[2] == 255) return ".jpg";
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return ".png";
        if (b.Length >= 6 && Encoding.ASCII.GetString(b,0,6) is "GIF87a" or "GIF89a") return ".gif";
        if (b.Length >= 12 && Encoding.ASCII.GetString(b,0,4) == "RIFF" &&
            Encoding.ASCII.GetString(b,8,4) == "WEBP") return ".webp";
        return "";
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int max, CancellationToken ct)
    {
        if (content.Headers.ContentLength is long n && n > max)
            throw new InvalidOperationException("Response too large");
        using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > max) throw new InvalidOperationException("Response too large");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    public void Dispose()
    {
        _parallelSearchGate.Dispose();
        foreach (var gate in _queryGates.Values) gate.Dispose();
        if (_ownsClient) _http.Dispose();
    }
}

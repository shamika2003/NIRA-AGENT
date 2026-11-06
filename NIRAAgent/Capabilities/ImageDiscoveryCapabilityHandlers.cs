using NIRAAgent.Images;
using NIRAAgent.Diagnostics;
using NIRAAgent.AI.Ollama;
using System.Text;
using System.Text.Json;

namespace NIRAAgent.Capabilities;

public sealed class NIRAImageSearchCapabilityHandler : INIRACapabilityHandler
{
    private readonly INIRAImageDiscoveryService _service;
    private readonly NIRACommonsImageDiscoveryService? _commonsFallback;
    private readonly OllamaClient? _visionModel;
    // Optional constructor parameters preserve older test integrations.
    // Production DI supplies both Commons and NIRA's EXISTING Gemma vision model.
    public NIRAImageSearchCapabilityHandler(INIRAImageDiscoveryService service,
        NIRACommonsImageDiscoveryService? commonsFallback = null,
        OllamaClient? visionModel = null)
    {
        _service = service;
        _commonsFallback = commonsFallback;
        _visionModel = visionModel;
    }

    // A correctly downloaded historical JPEG may depict a ticket, an indoor
    // model or an unrelated scene. Independently inspect the PIXELS, not a
    // search engine description or the source host. The vision model cannot
    // authenticate a historical date or licence; provenance is checked above.
    // Uncertain/malformed observations fail closed for documentary photos.
    private async Task<bool> InspectDocumentaryPixelsAsync(
        string query, NIRACommonsImageDownload photo, CancellationToken ct)
    {
        if (_visionModel is null) return true; // Standalone legacy unit fixtures.
        const string system = "You are a visual scene verifier, not a web researcher. " +
            "Only inspect the pixels of the attached image. Do not treat the " +
            "search query, publisher or caption as proof of what is visible. " +
            "Answer JSON only with boolean fields photographic and matchesScene, " +
            "and a short reason. photographic=false for tickets, posters, scanned " +
            "documents, drawings, diagrams, digital models or renders where a " +
            "real-world documentary photograph was requested. matchesScene=true " +
            "ONLY if the visible scene matches the requested subject, action " +
            "and environment; a person near an event is not the event, and " +
            "hardware in a factory is not on the Moon. If scene verification " +
            "is uncertain, use false. Never guess historical identity from " +
            "the pixels alone, and never claim source authenticity.";
        try
        {
            string raw = await _visionModel.ChatWithImagesAsync(
                NIRAModelSelection.Vision, system,
                "Requested visible photograph/scene: " + query +
                "\nDoes the attached image itself visibly depict it?",
                new[] { photo.LocalPath }, jsonMode: true,
                cancellationToken: ct);
            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;
            bool good = root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("photographic", out JsonElement photographic) &&
                (photographic.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
                photographic.GetBoolean() &&
                root.TryGetProperty("matchesScene", out JsonElement matching) &&
                (matching.ValueKind is JsonValueKind.True or JsonValueKind.False) &&
                matching.GetBoolean();
            NIRADiagnosticTrace.Event("IMAGE-VISION-AUDIT",
                $"Outcome={(good ? "Accepted" : "Rejected")} " +
                $"Model={NIRAModelSelection.Vision} Query={query[..Math.Min(query.Length, 70)]}");
            return good;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            NIRADiagnosticTrace.Event("IMAGE-VISION-AUDIT", "Outcome=Unavailable Reason=BudgetExpired");
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            NIRADiagnosticTrace.Event("IMAGE-VISION-AUDIT",
                $"Outcome=Unavailable Reason={ex.GetType().Name} NoUnverifiedPhotoShown=True");
            return false;
        }
    }

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.ImageSearch,
        Description = "Search genuine current web images via Tavily (existing NIRA_TAVILY_API_KEY; no generation). Returns source metadata and verified local image assets when requested. Images may support a saved artifact or a direct chat-image request; retrieval alone does not publish them to chat. Third-party licensing remains unverified.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = new[] {
            new NIRACapabilityParameterDescriptor { Name="query", Type="string", Required=true, Description="Specific visual subject, 2-160 characters; no URLs." },
            new NIRACapabilityParameterDescriptor { Name="limit", Type="integer", Required=false, Description="Number of ranked candidates.", Minimum=1, Maximum=64, DefaultValue="6" },
            new NIRACapabilityParameterDescriptor { Name="retrieveBest", Type="boolean", Required=false,
                Description="Retrieve real image assets by default (for direct chat presentation OR as inputs to a separately requested saved file). Rights are not proven. Set false to inspect candidate metadata without downloading." },
            new NIRACapabilityParameterDescriptor { Name="retrieveCount", Type="integer", Required=false, Minimum=1, Maximum=5, DefaultValue="1",
                Description="Distinct verified images to retrieve for the task; shown in chat only when that is the actual goal." },
            new NIRACapabilityParameterDescriptor { Name="skipCandidates", Type="integer", Required=false,
                Minimum=0, Maximum=56, DefaultValue="0", Description="Skip top ranked candidates; used to partition independent searches for a larger image request." },
            new NIRACapabilityParameterDescriptor { Name="photographsOnly", Type="boolean", Required=false,
                Description="When true, prefer authentic photos and exclude identifiable diagrams, illustrations, models and renderings; default false." }
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string query = NIRACapabilityArguments.RequireString(request, "query", 160);
        int limit = NIRACapabilityContractCodec.ReadInteger(request, Descriptor, "limit");
        // Searching for photos normally means the user wants visible photos,
        // not a list of IDs. Direct model capabilityRequests may omit this
        // optional argument even when the resource planner explicitly sets it.
        // Preserve the ability to opt out with retrieveBest=false.
        bool retrieveBest = !request.Arguments.TryGetProperty("retrieveBest", out _) ||
            NIRACapabilityArguments.GetBoolean(request, "retrieveBest");
        int retrieveCount = NIRACapabilityContractCodec.ReadInteger(request, Descriptor, "retrieveCount");
        int skipCandidates = NIRACapabilityContractCodec.ReadInteger(request, Descriptor, "skipCandidates");
        bool photographsOnly = NIRACapabilityArguments.GetBoolean(request, "photographsOnly") ||
            NIRAReferenceImageQualityPolicy.RequireDocumentary(query, false);
        // R73: a model's limit=1 made image search brittle: one bad CDN
        // content-type/403 lost the entire subject although Tavily returned
        // dozens of candidates. Keep several ALTERNATES from the same API call;
        // only retrieve the requested number of verified distinct images.
        // This increases local selection depth, not paid provider requests.
        int requestedLimit = limit;
        if (retrieveBest)
            limit = Math.Max(limit, Math.Min(64, skipCandidates + retrieveCount + 6));
        if (limit != requestedLimit)
            NIRADiagnosticTrace.Event("IMAGE-CANDIDATE-DEPTH",
                $"Original={requestedLimit} Effective={limit} " +
                "Reason=AlternateDownloads SameSearchCall=True");
        string safeQuery = query.Replace('\r', ' ').Replace('\n', ' ');
        NIRADiagnosticTrace.Event("IMAGE-TOOL-REQUEST",
            $"Capability=image.search Query={safeQuery} Limit={limit} " +
            $"RetrieveBest={retrieveBest} RetrieveCount={retrieveCount} " +
            $"SkipCandidates={skipCandidates} PhotographsOnly={photographsOnly}");
        var images = await _service.SearchAsync(query, limit, cancellationToken, photographsOnly);
        INIRAImageDiscoveryService selectedProvider = _service;
        if (images.Count == 0 && retrieveBest && _commonsFallback is not null &&
            !ReferenceEquals(_service, _commonsFallback))
        {
            try
            {
                var alternatives = await _commonsFallback.SearchAsync(
                    query, limit, cancellationToken, photographsOnly);
                images = alternatives.Where(candidate =>
                    NIRAReferenceImageQualityPolicy.Accept(candidate, query, photographsOnly) &&
                    NIRAReferenceImageQualityPolicy.MatchesRequestedScene(query,
                        candidate.Title, candidate.Description, candidate.MediaUrl)).ToArray();
                selectedProvider = _commonsFallback;
                NIRADiagnosticTrace.Event("IMAGE-ARCHIVE-FALLBACK",
                    $"Stage=PrimaryEmpty Alternatives={images.Count} NoGeneratedImages=True");
            }
            catch (Exception ex) when (ex is NIRAImageProviderUnavailableException or
                HttpRequestException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                NIRADiagnosticTrace.Event("IMAGE-ARCHIVE-FALLBACK",
                    $"Stage=PrimaryEmpty Outcome=Unavailable Kind={ex.GetType().Name}");
            }
        }
        NIRADiagnosticTrace.Event("IMAGE-TOOL-RESULT",
            $"Capability=image.search Query={query} Candidates={images.Count} " +
            $"ProviderStatus={_service.LastSearchStatus}");
        var output = new StringBuilder();
        output.AppendLine($"Provider={(ReferenceEquals(selectedProvider, _commonsFallback) ? "Wikimedia Commons" : "Tavily")} " +
            $"| Query={query} | Candidates={images.Count} | ResultsAreCandidatesOnly=True");
        foreach (var item in images)
        {
            output.AppendLine($"CandidateId={item.CandidateId}");
            output.AppendLine($"Provider={item.Provider}");
            output.AppendLine($"Title={item.Title}");
            output.AppendLine($"Description={item.Description}");
            output.AppendLine($"Author={item.Author}");
            output.AppendLine($"License={(item.License.Length == 0 ? "See source page" : item.License)}");
            output.AppendLine($"LicenseUrl={item.LicenseUrl}");
            output.AppendLine($"SourceUri={item.SourceUri}");
            output.AppendLine($"Dimensions={item.Width}x{item.Height}");
            output.AppendLine("---");
        }
        if (images.Count == 0)
        {
            // A successful HTTP response with zero usable images is a completed
            // negative observation. Tell the Executive plainly so it does not
            // ask the model to repeat an equivalent search in another cycle.
            // The link is to a REAL Wikimedia search page, NOT an image or a
            // fabricated source attribution. Search text is URL-encoded only.
            output.AppendLine("IMAGE_DISCOVERY_EMPTY=True");
            output.AppendLine("ImageSubjectMatch=False");
            output.AppendLine("VerifiedImageRetrieved=False");
            // Do not invent or promote an unrelated image-search page.
            output.AppendLine("ImageSearchPageIsNotVerifiedImage=True");
            return new NIRACapabilityHandlerResult {
                Summary = "IMAGE_DISCOVERY_EMPTY: Public image searches completed, but no subject-relevant reference image was verified. No image was displayed.",
                Output = output.ToString(), ChangedSystemState = false
            };
        }
        // The SAME initial model decision may ask for one or a few distinct
        // licensed images. No second model turn is required to choose IDs.
        // Actual bytes, not the requested count, determine what gets shown.
        if (retrieveBest && images.Count > 0)
        {
            var shown = new List<NIRACapabilityVisualArtifact>();
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Vision inspection requires a larger bounded budget than transport.
            budget.CancelAfter(TimeSpan.FromSeconds(photographsOnly && _visionModel is not null ? 100 : 28));
            int attempted = 0;
            int unavailable = 0;
            foreach (var candidate in images.Skip(skipCandidates))
            {
                if (shown.Count >= retrieveCount || budget.IsCancellationRequested) break;
                attempted++;
                try
                {
                    var image = await selectedProvider.RetrieveAsync(candidate.CandidateId, budget.Token);
                    // Revalidate the returned image's own metadata, not only
                    // the earlier search-result wrapper. A NASA gallery can
                    // contain images of different missions and scenes.
                    if (!NIRAReferenceImageQualityPolicy.MatchesRequestedScene(
                        query, image.Title, image.Caption, null))
                    {
                        NIRADiagnosticTrace.Event("IMAGE-IDENTITY-REJECT",
                            $"Stage=Downloaded Title={image.Title} Reason=WrongSceneOrIdentity");
                        continue;
                    }
                    if (hashes.Contains(image.Sha256)) continue;
                    if (photographsOnly && !await InspectDocumentaryPixelsAsync(
                            query, image, budget.Token)) continue;
                    if (!hashes.Add(image.Sha256)) continue;
                    NIRADiagnosticTrace.Event("IMAGE-TOOL-SELECT",
                        $"Capability=image.search SelectedTitle={image.Title} " +
                        $"SourceUri={image.SourceUri} Bytes={image.Bytes} " +
                        $"SelectionNumber={shown.Count + 1}");
                    shown.Add(new NIRACapabilityVisualArtifact {
                        LocalPath = image.LocalPath, Title = image.Title, Caption = image.Caption,
                        SubjectQuery = query, SourceUri = image.SourceUri,
                        PresentToUser = true, Surface = "InlineOnly"
                    });
                    // The already verified local cache is reusable as INPUT
                    // for later registered tools (e.g. document rendering).
                    // Do not ask cognition to invent candidate IDs or download
                    // the exact same pixels again. Rights are NOT verified.
                    output.AppendLine($"VerifiedImageTitle={image.Title}");
                    output.AppendLine($"VerifiedImageLocalPath={image.LocalPath}");
                    output.AppendLine($"VerifiedImageSHA256={image.Sha256}");
                    output.AppendLine($"VerifiedImageSourceUri={image.SourceUri}");
                    output.AppendLine("VerifiedImageRights=Unverified; inspect source license before reuse/publication");
                }
                catch (NIRAImageRetrievalUnavailableException)
                {
                    // This candidate failed; the other search results may be hosted
                    // elsewhere. Preserve the SAME search/provider call, bounded
                    // deadline, verified-byte requirement and requested image count.
                    unavailable++;
                    continue;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            // If the official-source CDN blocked every primary download,
            // search a second independent public archive before reporting an
            // image as unavailable. This changes only read-only image discovery.
            if (shown.Count < retrieveCount && _commonsFallback is not null &&
                !ReferenceEquals(selectedProvider, _commonsFallback) &&
                !budget.IsCancellationRequested)
            {
                try
                {
                    var backup = await _commonsFallback.SearchAsync(query, limit,
                        budget.Token, photographsOnly);
                    int backupAttempts = 0;
                    foreach (var candidate in backup)
                    {
                        if (shown.Count >= retrieveCount || budget.IsCancellationRequested ||
                            backupAttempts >= 10) break;
                        if (!NIRAReferenceImageQualityPolicy.Accept(candidate, query, photographsOnly) ||
                            !NIRAReferenceImageQualityPolicy.MatchesRequestedScene(query,
                                candidate.Title, candidate.Description, candidate.MediaUrl)) continue;
                        backupAttempts++;
                        attempted++;
                        try
                        {
                            var archiveImage = await _commonsFallback.RetrieveAsync(
                                candidate.CandidateId, budget.Token);
                            if (!NIRAReferenceImageQualityPolicy.MatchesRequestedScene(
                                query, archiveImage.Title, archiveImage.Caption, null)) continue;
                            if (hashes.Contains(archiveImage.Sha256)) continue;
                            if (photographsOnly && !await InspectDocumentaryPixelsAsync(
                                    query, archiveImage, budget.Token)) continue;
                            if (!hashes.Add(archiveImage.Sha256)) continue;
                            shown.Add(new NIRACapabilityVisualArtifact {
                                LocalPath = archiveImage.LocalPath, Title = archiveImage.Title,
                                Caption = archiveImage.Caption, SubjectQuery = query,
                                SourceUri = archiveImage.SourceUri,
                                PresentToUser = true, Surface = "InlineOnly"
                            });
                            output.AppendLine($"VerifiedImageTitle={archiveImage.Title}");
                            output.AppendLine($"VerifiedImageLocalPath={archiveImage.LocalPath}");
                            output.AppendLine($"VerifiedImageSHA256={archiveImage.Sha256}");
                            output.AppendLine($"VerifiedImageSourceUri={archiveImage.SourceUri}");
                            output.AppendLine("VerifiedImageRights=Unverified; inspect source license before reuse/publication");
                        }
                        catch (NIRAImageRetrievalUnavailableException) { unavailable++; }
                    }
                    NIRADiagnosticTrace.Event("IMAGE-ARCHIVE-FALLBACK",
                        $"Stage=PrimaryDownloadsFailed Attempted={backupAttempts} " +
                        $"TotalDistinct={shown.Count} NoGeneratedImages=True");
                }
                catch (Exception ex) when (ex is NIRAImageProviderUnavailableException or
                    HttpRequestException or OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    NIRADiagnosticTrace.Event("IMAGE-ARCHIVE-FALLBACK",
                        $"Stage=PrimaryDownloadsFailed Outcome=Unavailable Kind={ex.GetType().Name}");
                }
            }
            NIRADiagnosticTrace.Event("IMAGE-RETRIEVE-BATCH",
                $"Provider=ArchiveAware Attempted={attempted} Unavailable={unavailable} " +
                $"VerifiedDistinct={shown.Count} Requested={retrieveCount} " +
                $"BudgetExpired={budget.IsCancellationRequested}");
            output.AppendLine($"ImageCandidateAttempts={attempted}");
            output.AppendLine($"ImageCandidateFailures={unavailable}");
            if (shown.Count > 0)
            {
                NIRAAgent.Diagnostics.NIRADiagnosticTrace.Event("IMAGE-DELIVERY",
                    $"Outcome=ArtifactsPrepared Count={shown.Count} Surface=InlineOnly");
                output.AppendLine($"VerifiedImageRetrieved=True\nVerifiedImageCount={shown.Count}");
                return new NIRACapabilityHandlerResult {
                    Summary = $"Retrieved {shown.Count} distinct raster image asset(s) from {images.Count} candidates. Chat delivery depends on the requested goal; image reuse rights remain unverified.",
                    Output = output.ToString(), ChangedSystemState = false,
                    VisualArtifacts = shown.ToArray()
                };
            }
            output.AppendLine("VerifiedImageRetrieved=False\nImageMediaUnavailable=True");
        }
        output.AppendLine("Candidates alone are NOT displayed images. Use image.retrieve with a returned candidateId if a different verified photo is needed.");
        return new NIRACapabilityHandlerResult {
            Summary = $"Found {images.Count} image candidate(s); no verified raster has been displayed.",
            Output = output.ToString(), ChangedSystemState = false
        };
    }
}

public sealed class NIRAImageRetrieveCapabilityHandler : INIRACapabilityHandler
{
    private readonly INIRAImageDiscoveryService _service;
    public NIRAImageRetrieveCapabilityHandler(INIRAImageDiscoveryService service) => _service = service;

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.ImageRetrieve,
        Description = "Retrieve a raster image candidate into NIRA's bounded verified visual cache as a reusable task asset. Present in chat only for a requested chat visual; otherwise it can support a saved artifact subject to reuse rights. CandidateId MUST come from image.search.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = new[] {
            new NIRACapabilityParameterDescriptor { Name="candidateId", Type="string", Required=true, Description="Exact short-lived candidateId from a successful image.search result. No URL/path." }
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string id = NIRACapabilityArguments.RequireString(request, "candidateId", 80);
        NIRADiagnosticTrace.Event("IMAGE-TOOL-REQUEST",
            $"Capability=image.retrieve CandidateId={id}");
        var download = await _service.RetrieveAsync(id, cancellationToken);
        NIRADiagnosticTrace.Event("IMAGE-TOOL-SELECT",
            $"Capability=image.retrieve SelectedTitle={download.Title} " +
            $"SourceUri={download.SourceUri} Bytes={download.Bytes}");
        NIRAAgent.Diagnostics.NIRADiagnosticTrace.Event("IMAGE-DELIVERY",
            "Outcome=ArtifactsPrepared Count=1 Surface=InlineOnly");
        return new NIRACapabilityHandlerResult {
            Summary = $"Retrieved a real raster reference image '{download.Title}' ({download.Bytes} bytes) for the active task. Its chat presentation, if any, is decided by the Executive.",
            Output = $"LocalPath={download.LocalPath}\nTitle={download.Title}\nCaption={download.Caption}\nSourceUri={download.SourceUri}\nSHA256={download.Sha256}\nBytes={download.Bytes}\nVerifiedImageRetrieved=True",
            VisualArtifacts = new [] { new NIRACapabilityVisualArtifact {
                LocalPath=download.LocalPath, Title=download.Title, Caption=download.Caption,
                SourceUri=download.SourceUri, PresentToUser=true, Surface="InlineOnly"
            } },
            ChangedSystemState = false // Runtime-owned bounded visual cache, not a user-file mutation.
        };
    }
}
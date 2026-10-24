using NIRAAgent.Images;
using NIRAAgent.Diagnostics;
using System.Text;

namespace NIRAAgent.Capabilities;

public sealed class NIRAImageSearchCapabilityHandler : INIRACapabilityHandler
{
    private readonly INIRAImageDiscoveryService _service;
    public NIRAImageSearchCapabilityHandler(INIRAImageDiscoveryService service) => _service = service;

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.ImageSearch,
        Description = "Search genuine current web images via Tavily (existing NIRA_TAVILY_API_KEY; no generation). Returns short-lived candidateId, title, source URL and rights status (third-party licensing is not verified). Then call image.retrieve with a returned candidateId to show a real image; never invent IDs or URLs.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = new[] {
            new NIRACapabilityParameterDescriptor { Name="query", Type="string", Required=true, Description="Specific visual subject, 2-160 characters; no URLs." },
            new NIRACapabilityParameterDescriptor { Name="limit", Type="integer", Required=false, Description="Number of ranked candidates, 1-12; default 6." },
            new NIRACapabilityParameterDescriptor { Name="retrieveBest", Type="boolean", Required=false,
                Description="Retrieve and present real licensed images in chat by default. Set false only to inspect candidate metadata without downloading. Does not create/generate images." },
            new NIRACapabilityParameterDescriptor { Name="retrieveCount", Type="integer", Required=false,
                Description="Optional count of distinct verified images to show when retrieveBest is true, 1-3; default 1. Pick based on actual user value, never as a quota." },
            new NIRACapabilityParameterDescriptor { Name="photographsOnly", Type="boolean", Required=false,
                Description="When true, prefer authentic photos and exclude identifiable diagrams, illustrations, models and renderings; default false." }
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string query = NIRACapabilityArguments.RequireString(request, "query", 160);
        int limit = NIRACapabilityArguments.GetInteger(request, "limit", 6, 1, 12);
        // Searching for photos normally means the user wants visible photos,
        // not a list of IDs. Direct model capabilityRequests may omit this
        // optional argument even when the resource planner explicitly sets it.
        // Preserve the ability to opt out with retrieveBest=false.
        bool retrieveBest = !request.Arguments.TryGetProperty("retrieveBest", out _) ||
            NIRACapabilityArguments.GetBoolean(request, "retrieveBest");
        int retrieveCount = NIRACapabilityArguments.GetInteger(request, "retrieveCount", 1, 1, 3);
        bool photographsOnly = NIRACapabilityArguments.GetBoolean(request, "photographsOnly");
        string safeQuery = query.Replace('\r', ' ').Replace('\n', ' ');
        NIRADiagnosticTrace.Event("IMAGE-TOOL-REQUEST",
            $"Capability=image.search Query={safeQuery} Limit={limit} " +
            $"RetrieveBest={retrieveBest} RetrieveCount={retrieveCount} PhotographsOnly={photographsOnly}");
        var images = await _service.SearchAsync(query, limit, cancellationToken, photographsOnly);
        NIRADiagnosticTrace.Event("IMAGE-TOOL-RESULT",
            $"Capability=image.search Query={query} Candidates={images.Count} " +
            $"ProviderStatus={_service.LastSearchStatus}");
        var output = new StringBuilder();
        output.AppendLine($"Provider=Tavily | Query={query} | Candidates={images.Count} | ResultsAreCandidatesOnly=True");
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
                Summary = "IMAGE_DISCOVERY_EMPTY: Tavily search completed, but no subject-relevant reference image was verified. No image was displayed.",
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
            budget.CancelAfter(TimeSpan.FromSeconds(28));
            int attempted = 0;
            int unavailable = 0;
            foreach (var candidate in images)
            {
                if (shown.Count >= retrieveCount || budget.IsCancellationRequested) break;
                attempted++;
                try
                {
                    var image = await _service.RetrieveAsync(candidate.CandidateId, budget.Token);
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
                    output.AppendLine($"VerifiedImageTitle={image.Title}");
                    output.AppendLine($"VerifiedImageSourceUri={image.SourceUri}");
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
            NIRADiagnosticTrace.Event("IMAGE-RETRIEVE-BATCH",
                $"Provider=Tavily Attempted={attempted} Unavailable={unavailable} " +
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
                    Summary = $"Displayed {shown.Count} distinct verified raster image(s) out of {images.Count} retrieved candidates.",
                    Output = output.ToString(), ChangedSystemState = false,
                    VisualArtifacts = shown.ToArray()
                };
            }
            output.AppendLine("VerifiedImageRetrieved=False\nImageMediaUnavailable=True");
        }
        output.AppendLine("Candidates alone are NOT displayed images. Use image.retrieve with a returned candidateId if a different verified photo is needed.");
        return new NIRACapabilityHandlerResult {
            Summary = $"Found {images.Count} Tavily image candidate(s); no verified image has been displayed.",
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
        Description = "Retrieve a verified raster image candidate into NIRA's own bounded visual cache and automatically present it in chat with its known source and explicit rights status. CandidateId MUST come from image.search. This does not write user files, require a browser, or accept a URL/path from the model.",
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
            Summary = $"Retrieved a real raster reference image '{download.Title}' ({download.Bytes} bytes). Image queued for NIRA chat.",
            Output = $"LocalPath={download.LocalPath}\nTitle={download.Title}\nCaption={download.Caption}\nSourceUri={download.SourceUri}\nSHA256={download.Sha256}\nBytes={download.Bytes}\nVerifiedImageRetrieved=True",
            VisualArtifacts = new [] { new NIRACapabilityVisualArtifact {
                LocalPath=download.LocalPath, Title=download.Title, Caption=download.Caption,
                SourceUri=download.SourceUri, PresentToUser=true, Surface="InlineOnly"
            } },
            ChangedSystemState = false // Runtime-owned bounded visual cache, not a user-file mutation.
        };
    }
}

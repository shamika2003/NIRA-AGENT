using NIRAAgent.Images;
using System.Text;

namespace NIRAAgent.Capabilities;

public sealed class NIRAImageSearchCapabilityHandler : INIRACapabilityHandler
{
    private readonly NIRACommonsImageDiscoveryService _service;
    public NIRAImageSearchCapabilityHandler(NIRACommonsImageDiscoveryService service) => _service = service;

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.ImageSearch,
        Description = "Search real openly licensed-reference image candidates from Openverse and Wikimedia Commons (no browser or API key). Returns short-lived candidateId, title, attribution, license, and source page. Then call image.retrieve with a returned candidateId to show a real image; never invent IDs or URLs.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = new[] {
            new NIRACapabilityParameterDescriptor { Name="query", Type="string", Required=true, Description="Specific visual subject, 2-160 characters; e.g. Karnak temple columns." },
            new NIRACapabilityParameterDescriptor { Name="limit", Type="integer", Required=false, Description="Number of ranked candidates, 1-12; default 6." },
            new NIRACapabilityParameterDescriptor { Name="retrieveBest", Type="boolean", Required=false,
                Description="Optional: retrieve and present real licensed image(s) from this verified search; default false. Does not create/generate images." },
            new NIRACapabilityParameterDescriptor { Name="retrieveCount", Type="integer", Required=false,
                Description="Optional count of distinct verified images to show when retrieveBest is true, 1-3; default 1. Pick based on actual user value, never as a quota." }
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string query = NIRACapabilityArguments.RequireString(request, "query", 160);
        int limit = NIRACapabilityArguments.GetInteger(request, "limit", 6, 1, 12);
        bool retrieveBest = NIRACapabilityArguments.GetBoolean(request, "retrieveBest");
        int retrieveCount = NIRACapabilityArguments.GetInteger(request, "retrieveCount", 1, 1, 3);
        var images = await _service.SearchAsync(query, limit, cancellationToken);
        var output = new StringBuilder();
        output.AppendLine($"Provider=Openverse/Wikimedia | Query={query} | Candidates={images.Count} | ResultsAreCandidatesOnly=True");
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
            string searchPage = "https://commons.wikimedia.org/wiki/Special:MediaSearch?type=image&search=" +
                Uri.EscapeDataString(query);
            output.AppendLine("IMAGE_DISCOVERY_EMPTY=True");
            output.AppendLine("VerifiedImageRetrieved=False");
            output.AppendLine($"PublicImageSearchPage={searchPage}");
            output.AppendLine("ImageSearchPageIsNotVerifiedImage=True");
            return new NIRACapabilityHandlerResult {
                Summary = "IMAGE_DISCOVERY_EMPTY: No suitable licensed image candidates were returned by available indexes. No image was displayed.",
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
            budget.CancelAfter(TimeSpan.FromSeconds(22));
            foreach (var candidate in images)
            {
                if (shown.Count >= retrieveCount || budget.IsCancellationRequested) break;
                try
                {
                    var image = await _service.RetrieveAsync(candidate.CandidateId, budget.Token);
                    if (!hashes.Add(image.Sha256)) continue;
                    shown.Add(new NIRACapabilityVisualArtifact {
                        LocalPath = image.LocalPath, Title = image.Title, Caption = image.Caption,
                        SourceUri = image.SourceUri, PresentToUser = true, Surface = "InlineOnly"
                    });
                    output.AppendLine($"VerifiedImageTitle={image.Title}");
                    output.AppendLine($"VerifiedImageSourceUri={image.SourceUri}");
                }
                catch (NIRAImageRetrievalUnavailableException)
                {
                    // Exhaustion of this already-searched batch is terminal.
                    // The retrieval service itself tried independent candidates.
                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            if (shown.Count > 0)
            {
                output.AppendLine($"VerifiedImageRetrieved=True\nVerifiedImageCount={shown.Count}");
                return new NIRACapabilityHandlerResult {
                    Summary = $"Displayed {shown.Count} distinct licensed image(s) out of {images.Count} retrieved candidates.",
                    Output = output.ToString(), ChangedSystemState = false,
                    VisualArtifacts = shown.ToArray()
                };
            }
            output.AppendLine("VerifiedImageRetrieved=False\nImageMediaUnavailable=True");
        }
        output.AppendLine("Candidates alone are NOT displayed images. Use image.retrieve with a returned candidateId if a different verified photo is needed.");
        return new NIRACapabilityHandlerResult {
            Summary = $"Found {images.Count} open-license image candidate(s); no verified image has been displayed.",
            Output = output.ToString(), ChangedSystemState = false
        };
    }
}

public sealed class NIRAImageRetrieveCapabilityHandler : INIRACapabilityHandler
{
    private readonly NIRACommonsImageDiscoveryService _service;
    public NIRAImageRetrieveCapabilityHandler(NIRACommonsImageDiscoveryService service) => _service = service;

    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.ImageRetrieve,
        Description = "Retrieve a verified openly licensed image candidate into NIRA's own bounded visual cache and automatically present it in chat with the real source page and author/license attribution. CandidateId MUST come from image.search. This does not write user files, require a browser, or accept a URL/path from the model.",
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
        var download = await _service.RetrieveAsync(id, cancellationToken);
        return new NIRACapabilityHandlerResult {
            Summary = $"Retrieved a real licensed reference image '{download.Title}' ({download.Bytes} bytes). Image queued for NIRA chat.",
            Output = $"LocalPath={download.LocalPath}\nTitle={download.Title}\nCaption={download.Caption}\nSourceUri={download.SourceUri}\nSHA256={download.Sha256}\nBytes={download.Bytes}\nVerifiedImageRetrieved=True",
            VisualArtifacts = new [] { new NIRACapabilityVisualArtifact {
                LocalPath=download.LocalPath, Title=download.Title, Caption=download.Caption,
                SourceUri=download.SourceUri, PresentToUser=true, Surface="InlineOnly"
            } },
            ChangedSystemState = false // Runtime-owned bounded visual cache, not a user-file mutation.
        };
    }
}

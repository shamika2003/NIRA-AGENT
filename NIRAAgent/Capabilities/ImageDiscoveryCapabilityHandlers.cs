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
            new NIRACapabilityParameterDescriptor { Name="limit", Type="integer", Required=false, Description="Number of ranked candidates, 1-12; default 6." }
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) => NIRACapabilityRisk.Observe;

    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string query = NIRACapabilityArguments.RequireString(request, "query", 160);
        int limit = NIRACapabilityArguments.GetInteger(request, "limit", 6, 1, 12);
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
        output.AppendLine("Only image.retrieve can ground actual image bytes for NIRA visual display. Candidate results alone are NOT displayed images.");
        return new NIRACapabilityHandlerResult {
            Summary = $"Found {images.Count} open-license reference-image candidate(s) for '{query}'. No images downloaded yet.",
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

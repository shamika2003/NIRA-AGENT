using System.Text.RegularExpressions;

using NIRAAgent.Images;

namespace NIRAAgent.Capabilities;

// One pre-authorized generation (Cloudflare may consume daily quota). Never retries or calls another LLM.
public sealed class NIRAImageGenerateCapabilityHandler : INIRACapabilityHandler
{
    private readonly NIRACloudflareImageGenerationService _generator;
    public NIRAImageGenerateCapabilityHandler(NIRACloudflareImageGenerationService generator) => _generator = generator;
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.ImageGenerate,
        Description = "Generate exactly ONE NEW original image using Cloudflare Workers AI FLUX.2 Klein 4B. " +
            "This consumes image-generation quota. Use only when the user explicitly requests creation; NEVER for reference-image searches. " +
            "In your SAME cognition pass, enhance the user's prompt into a vivid, faithful 1-2400 character description, " +
            "preserve their subjects and constraints, choose dimensions, and invoke image.generate ONCE. " +
            "The result is verified and automatically displayed in chat and saved under Gallery > AI Generated. " +
            "Do not retry on failure and do not call another reasoning model for prompt enhancement.",
        DefaultRisk = NIRACapabilityRisk.Execute,
        Parameters = new[]
        {
            new NIRACapabilityParameterDescriptor { Name="prompt", Type="string", Required=true,
                Description="An enhanced but faithful visual prompt (4-2400 chars) from the same cognition call." },
            new NIRACapabilityParameterDescriptor { Name="width", Type="integer", Required=false,
                Description="Width in pixels, multiple of 8. Default 1024; 1024 square, 1280x720 landscape, 720x1280 portrait." },
            new NIRACapabilityParameterDescriptor { Name="height", Type="integer", Required=false,
                Description="Height in pixels, multiple of 8. Default 768; max one megapixel for test generation." }
        }
    };
    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest _) => NIRACapabilityRisk.Execute;
    public async Task<NIRACapabilityHandlerResult> ExecuteAsync(NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        string prompt = NIRACapabilityArguments.RequireString(request, "prompt", 2400);
        int width = NIRACapabilityArguments.GetInteger(request, "width", 1024, 256, 1920);
        int height = NIRACapabilityArguments.GetInteger(request, "height", 768, 256, 1920);
        var result = await _generator.GenerateAsync(new NIRAImageGenerationRequest(prompt, width, height), cancellationToken);
        return new NIRACapabilityHandlerResult
        {
            Summary = $"Generated one verified image with FLUX.2 Klein 4B ({result.Width}x{result.Height}); shown in chat and added to AI Generated Gallery.",
            Output = $"VerifiedImageGenerated=True\nModel={result.Model}\nWidth={result.Width}\nHeight={result.Height}\nLocalPath={result.LocalPath}\nSHA256={result.Sha256}\nBytes={result.Bytes}\nOneGeneration=True",
            VisualArtifacts = new[] { new NIRACapabilityVisualArtifact
            {
                LocalPath = result.LocalPath,
                Title = BuildDisplayTitle(prompt),
                Caption = NIRACloudflareImageGenerationService.GeneratedCaptionPrefix,
                PresentToUser = true,
                Surface = "InlineOnly"
            } },
            ChangedSystemState = false // Only NIRA's own managed cache, no user-owned filesystem mutation.
        };
    }


    private static string BuildDisplayTitle(string prompt)
    {
        string normalized = Regex.Replace(prompt ?? string.Empty, @"\s+", " ").Trim();
        if (normalized.Length == 0)
            return "NIRA generated image";

        string candidate = Regex.Split(normalized,
            @"(?<=[.!?])\s+|\s+(?:with|featuring|showing|including|surrounded by)\s+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part))?
            .Trim(' ', ',', ';', '.', ':') ?? normalized;

        candidate = Regex.Replace(candidate, @"^(?:create|generate|make|render)\s+", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        if (candidate.Length > 72)
            candidate = candidate[..72].TrimEnd() + "…";
        if (candidate.Length == 0)
            return "NIRA generated image";
        return char.ToUpperInvariant(candidate[0]) + candidate[1..];
    }
}

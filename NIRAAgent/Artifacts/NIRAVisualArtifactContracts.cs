/*
 * filename: NIRAVisualArtifactContracts.cs
 */

namespace NIRAAgent.Artifacts;

// =============================================================
// VISUAL ARTIFACT SOURCE
//
// A visual artifact is presentation/evidence content that NIRA can
// show to the user. It is deliberately source-agnostic: a grounded
// screenshot, browser/page capture, downloaded image, generated image,
// or another future image producer can all enter the same pipeline once
// there is a concrete local image file or grounded vision evidence ID.
// =============================================================

public enum NIRAVisualArtifactSourceKind
{
    VisionEvidence,
    LocalImage
}


public enum NIRAVisualArtifactPresentationSurface
{
    // Persist in the chat surface. If the chat is not active, the WPF
    // host may additionally show a non-activating visual peek/toast.
    Auto,

    // Keep the artifact only in the chat conversation surface.
    InlineOnly,

    // Show an ephemeral desktop peek without adding an inline card.
    ToastOnly,

    // Always add the inline card and request a desktop peek.
    InlineAndToast
}


public sealed record NIRAVisualArtifactPresentationRequest
{
    public Guid? EvidenceId
    {
        get;
        init;
    }


    public string LocalPath
    {
        get;
        init;
    } =
        string.Empty;


    public string Title
    {
        get;
        init;
    } =
        string.Empty;


    public string Caption
    {
        get;
        init;
    } =
        string.Empty;


    // Optional provenance only. The runtime never fetches this URL from a
    // presentation request; normal browser/HTTP capabilities must first
    // produce a concrete local image/evidence artifact.
    public string SourceUri
    {
        get;
        init;
    } =
        string.Empty;


    public NIRAVisualArtifactPresentationSurface Surface
    {
        get;
        init;
    } =
        NIRAVisualArtifactPresentationSurface.Auto;


    public NIRAVisualArtifactPresentationRequest Normalize()
    {
        bool hasEvidence =
            EvidenceId.HasValue
            && EvidenceId.Value != Guid.Empty;

        bool hasPath =
            !string.IsNullOrWhiteSpace(LocalPath);

        // vision.capture evidence exposes both a stable EvidenceId and the
        // backing PNG path. If cognition echoes both, the EvidenceId is the
        // stronger authoritative source and must win instead of causing the
        // whole visual presentation to be silently discarded.
        if (!hasEvidence && !hasPath)
        {
            throw new InvalidOperationException(
                "A visual presentation request requires a grounded evidenceId or localPath.");
        }

        string title = NormalizeText(Title, 180);
        string caption = NormalizeText(Caption, 1000);
        string sourceUri = NormalizeText(SourceUri, 1600);

        // Canonical form always carries exactly one source. EvidenceId wins
        // when both were supplied; NIRAVisualArtifactService will still
        // revalidate that evidence against the authoritative vision store.
        string localPath = hasEvidence
            ? string.Empty
            : LocalPath.Trim();

        return this with
        {
            EvidenceId = hasEvidence ? EvidenceId : null,
            LocalPath = localPath,
            Title = title,
            Caption = caption,
            SourceUri = sourceUri
        };
    }


    public string BuildSignature()
    {
        NIRAVisualArtifactPresentationRequest normalized = Normalize();

        string source = normalized.EvidenceId.HasValue
            ? $"evidence:{normalized.EvidenceId.Value:D}"
            : $"path:{normalized.LocalPath}";

        return string.Join(
            '|',
            source,
            normalized.Surface.ToString(),
            normalized.Title,
            normalized.Caption,
            normalized.SourceUri);
    }


    private static string NormalizeText(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string clean =
            string.Join(
                ' ',
                value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));

        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength];
    }
}


public sealed record NIRAVisualArtifact
{
    public Guid ArtifactId
    {
        get;
        init;
    }


    public NIRAVisualArtifactSourceKind SourceKind
    {
        get;
        init;
    }


    public Guid? EvidenceId
    {
        get;
        init;
    }


    public string LocalPath
    {
        get;
        init;
    } =
        string.Empty;


    public string Title
    {
        get;
        init;
    } =
        string.Empty;


    public string Caption
    {
        get;
        init;
    } =
        string.Empty;


    public string SourceUri
    {
        get;
        init;
    } =
        string.Empty;


    public string SourceReference
    {
        get;
        init;
    } =
        string.Empty;


    public string Sha256
    {
        get;
        init;
    } =
        string.Empty;


    public NIRAVisualArtifactPresentationSurface Surface
    {
        get;
        init;
    } =
        NIRAVisualArtifactPresentationSurface.Auto;


    public DateTimeOffset CreatedAtUtc
    {
        get;
        init;
    } =
        DateTimeOffset.UtcNow;
}

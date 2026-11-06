using System.Text.RegularExpressions;

namespace NIRAAgent.Images;

/// <summary>
/// Fail-closed DOCUMENTARY search eligibility based on untrusted metadata and
/// independently inspectable publisher identity. This is an initial filter,
/// not proof that the image pixels depict the claimed subject. Retrieval of
/// bytes certifies transport, not historical authenticity or usage rights.
/// </summary>
public static class NIRAReferenceImageQualityPolicy
{
    private const RegexOptions Rx = RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Synthetic = new(
        @"\b(?:generative\s*(?:ai|art)|ai\s*(?:made|generated|created|artwork|image|art)|" +
        @"generated\s*(?:with|using|by)\s*(?:ai|artificial intelligence)|" +
        @"midjourney|stable\s*diffusion|dall[ -]?e|computer[- ]generated|" +
        @"synthetic\s*(?:image|photo|art)|cgi|3d\s*render(?:ing|ed)?|" +
        @"digital\s*art|artist.s\s*(?:impression|conception)|concept\s*art|" +
        @"fan\s*art|illustration|illustrated|graphic\s*novel|photoshop\s*composite|" +
        @"fictional\s*(?:render|depiction)|stock\s*illustration)\b",
        Rx);
    private static readonly Regex CreativeRequest = new(
        @"\b(?:generate|create|draw|design|render|illustrate|diagram|schematic|" +
        @"artist.s\s*impression|concept\s*art|3d\s*model|ai[- ]generated|" +
        @"infographic|simulation|reconstruction|painting|cartoon)\b", Rx);
    private static readonly Regex DocumentaryRequest = new(
        @"\b(?:real|original|genuine|authentic|archival|archive|historic|historical|photos?|" +
        @"documentary|photograph|photographs|portrait|preserved|museum|" +
        @"actual\s+(?:photo|picture|device|equipment|invention))\b", Rx);
    private static readonly Regex Promotional = new(
        @"\b(?:thumbnail|poster|watermark|stock\s*(?:photo|image)|" +
        @"advertisement|promo\s*graphic|youtube\s*thumbnail|" +
        @"movie\s*poster|merchandise|wallpaper)\b", Rx);

    public static bool CreativeImageryRequested(string? query) =>
        !string.IsNullOrWhiteSpace(query) && CreativeRequest.IsMatch(query);

    public static bool RequireDocumentary(string? query, bool photographsOnly) =>
        !CreativeImageryRequested(query) &&
        (photographsOnly || (!string.IsNullOrWhiteSpace(query) && DocumentaryRequest.IsMatch(query)));

    public static bool LooksSynthetic(string? title, string? description, string? mediaUrl) =>
        Synthetic.IsMatch(Unescape(title) + " " + Unescape(description) + " " + UrlPath(mediaUrl));

    public static bool LooksPromotional(string? title, string? description, string? mediaUrl) =>
        Promotional.IsMatch(Unescape(title) + " " + Unescape(description) + " " + UrlPath(mediaUrl));

    // A museum can publish genuine photographs that depict the WRONG event.
    // An "Apollo launch" query must not show a press conference, and a
    // surface-scene query must not show the vehicle in orbit. This rejects
    // only high-confidence scene contradictions; it does not certify pixels.
    // Intents are generic photography concepts, never topic-name fixtures.
    // The photographed entity must be the requested entity, not just a related
    // program or an official publisher. This is deliberately generic: any
    // named numbered series (e.g. "Mission 11" versus "Mission 13") is checked.
    // An ambiguous title mentioning competing numbered identities is rejected
    // instead of silently assigning the search query to the unrelated image.
    public static bool ConflictsWithNumberedIdentity(string? requested,
        string? observedTitle, string? mediaUrl = null)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(observedTitle))
            return false;
        const string pattern = @"(?<![\p{L}\p{N}])(?<series>[\p{L}]{3,})[\s-]+(?<number>\d{1,3})(?![\p{L}\p{N}])";
        var requestedIds = Regex.Matches(requested, pattern, Rx)
            .Cast<Match>().Select(m => (Series: m.Groups["series"].Value,
                Number: m.Groups["number"].Value)).ToArray();
        if (requestedIds.Length == 0) return false;
        // The image title is independent metadata. A search snippet or source
        // article description may parrot the original search and is NOT used.
        string observed = Unescape(observedTitle) + " " + UrlPath(mediaUrl);
        foreach (Match actual in Regex.Matches(observed, pattern, Rx))
        {
            string series = actual.Groups["series"].Value;
            var sameSeries = requestedIds.Where(id =>
                string.Equals(id.Series, series, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (sameSeries.Length > 0 && !sameSeries.Any(id =>
                string.Equals(id.Number.TrimStart('0'),
                    actual.Groups["number"].Value.TrimStart('0'), StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    public static bool MatchesRequestedScene(string? requested, string? title,
        string? description, string? mediaUrl)
    {
        string query = Unescape(requested);
        string label = Unescape(title) + " " + Unescape(description) + " " + UrlPath(mediaUrl);
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(label))
            return true;
        if (ConflictsWithNumberedIdentity(query, title, mediaUrl)) return false;
        string ownTitle = Unescape(title);
        // An authentic archive can contain genuine PHOTOGRAPHS OF documents,
        // model meshes, invitations and museum tickets. Those are not pictures
        // of the event, vehicle or people the user requested. The publisher's
        // authority never overrides the depicted subject.
        bool documentWanted = Regex.IsMatch(query,
            @"\b(?:ticket|pass|invitation|invite|credential|document|patent|brochure|" +
            @"postcard|letter|press badge|3d model|mesh|diagram|schematic)\b", Rx);
        if (!documentWanted && Regex.IsMatch(ownTitle,
            @"\b(?:launch pass|launch invitation|invite to|invitation to|" +
            @"admission ticket|access badge|press credential|signed pass|" +
            @"commemorative stamp|postcard|3d mesh|3d model|computer render|" +
            @"full resolution 3d|medium resolution 3d)\b", Rx))
            return false;
        bool surface = Regex.IsMatch(query,
            @"\b(?:on the (?:moon|planet|ground|surface)|lunar surface|planetary surface|" +
            @"landed on|landing site|surface of|on the ground)\b", Rx);
        bool orbitScene = Regex.IsMatch(label,
            @"\b(?:in (?:lunar|earth|martian|planetary) orbit|lunar orbit|" +
            @"orbiting (?:the )?(?:moon|earth|planet)|seen from orbit|" +
            @"viewed from (?:lunar )?orbit|hovers? against the black backdrop of space|" +
            @"seen from (?:the )?command module)\b", Rx);
        // A search-result description can parrot the requested surface scene.
        // Only the image's own headline can resolve an orbit/surface conflict.
        if (surface && orbitScene && !Regex.IsMatch(Unescape(title),
            @"\b(?:surface|on the moon|landed|landing site|on the ground)\b", Rx))
            return false;
        bool launch = Regex.IsMatch(query,
            @"\b(?:launch|lift[ -]?off|liftoff|rocket ascent|takeoff)\b", Rx);
        if (launch && !documentWanted)
        {
            // The file's own title must describe the launch scene rather
            // than the launch's attendees, memorabilia or aftermath.
            if (Regex.IsMatch(ownTitle,
                @"\b(?:mission officials|after (?:the )?(?:launch|liftoff)|" +
                @"before (?:the )?launch|relax|celebrat|press conference|" +
                @"mission control|launch pass|launch ticket|invite|invitation|" +
                @"breakfast|dinner|lunch|press briefing)\b", Rx))
                return false;
            if (!Regex.IsMatch(ownTitle,
                @"\b(?:launch(?:es|ed|ing)?|lift[ -]?off|liftoff|" +
                @"blast[ -]?off|ascent|rocket|saturn|ignition|launchpad|launch pad)\b", Rx))
                return false;
        }
        // When the user asks for people actually ON a surface, a photograph
        // of their vehicle being assembled in a factory is not an alternative.
        bool people = Regex.IsMatch(query,
            @"\b(?:crew|astronauts?|moonwalk(?:ers?)?|spacewalk(?:ers?)?)\b", Rx);
        if (people && surface)
        {
            if (!Regex.IsMatch(ownTitle,
                @"\b(?:astronauts?|crew|moonwalk|spacewalk|eva|" +
                @"armstrong|aldrin|cosmonauts?)\b", Rx)) return false;
            if (!Regex.IsMatch(ownTitle,
                @"\b(?:on the moon|on lunar surface|lunar surface|" +
                @"moonwalk|extravehicular|eva|lunar landing|on the surface)\b", Rx))
                return false;
            if (Regex.IsMatch(ownTitle,
                @"\b(?:pose|portrait|training|rehearsal|simulator|" +
                @"press conference|assembly|factory)\b", Rx)) return false;
        }
        // A specific vehicle INTERIOR requires evidence of the actual inside;
        // a 3D mesh, docked exterior or lunar module assembly is not one.
        bool wantsInside = Regex.IsMatch(query,
            @"\b(?:interior|inside|cabin|cockpit|control panel|instrument panel)\b", Rx);
        if (wantsInside && !Regex.IsMatch(ownTitle,
            @"\b(?:interior|inside|cabin|cockpit|control panel|instrument panel)\b", Rx))
            return false;
        if (wantsInside && Regex.IsMatch(ownTitle,
            @"\b(?:exterior|3d mesh|docking assembly|separation from|" +
            @"landing gear fixture|under construction)\b", Rx)) return false;
        bool portrait = Regex.IsMatch(query,
            @"\b(?:crew portrait|group portrait|formal portrait|official portrait)\b", Rx);
        if (portrait && Regex.IsMatch(label,
            @"\b(?:answering questions|press conference|press briefing|interview|" +
            @"breakfast|dinner|lunch|candid|meal|working session|training session)\b", Rx))
            return false;
        return true;
    }

    // Source preference: recognizable primary archives/institutions, not
    // user-provided URL text or the mere occurrence of an institution name in
    // an article paragraph. Unrecognized hosts receive no provenance credit.
    public static int PublisherAuthority(string? sourceUri)
    {
        if (!Uri.TryCreate(sourceUri, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps) return 0;
        string host = uri.Host.ToLowerInvariant();
        if (host.EndsWith(".gov", StringComparison.Ordinal) ||
            host.EndsWith(".edu", StringComparison.Ordinal) ||
            host.EndsWith(".ac.uk", StringComparison.Ordinal)) return 5;
        if (host.EndsWith(".wikimedia.org", StringComparison.Ordinal) ||
            (host is "commons.wikimedia.org" or "upload.wikimedia.org" or
                "en.wikipedia.org" or "www.loc.gov" or "loc.gov" or
                "www.si.edu" or "si.edu" or "www.thehenryford.org" or
                "thehenryford.org" or "artsandculture.google.com")) return 4;
        // Do NOT trust an arbitrary domain simply because its hostname has
        // the word "museum" or "archive" in it. Official/archival identity
        // needs a recognized publisher or a restricted educational domain.
        if (host.EndsWith(".museum", StringComparison.Ordinal) ||
            host is "tesla-museum.org" or "www.tesla-museum.org" or
                "teslasciencecenter.org" or "www.teslasciencecenter.org" or
                "sciencemuseumgroup.org.uk" or "www.sciencemuseumgroup.org.uk" or
                "npg.org.uk" or "www.npg.org.uk" or "getty.edu" or
                "www.getty.edu" or "www.britannica.com" or
                "www.sciencehistory.org" or "sciencehistory.org") return 3;
        return 0;
    }

    public static bool Accept(NIRACommonsImageCandidate candidate, string? query,
        bool photographsOnly)
    {
        bool documentary = RequireDocumentary(query, photographsOnly);
        bool creative = CreativeImageryRequested(query);
        if (!creative && LooksSynthetic(candidate.Title, candidate.Description, candidate.MediaUrl))
            return false;
        if (documentary && LooksPromotional(candidate.Title, candidate.Description, candidate.MediaUrl))
            return false;
        // Animated GIF and editorial preview formats are common for diagrams,
        // illustrations and moving graphics; do not silently pass one as a
        // documentary still photograph. Non-photographic creative requests can
        // continue to use the original image provider behavior.
        if (photographsOnly && Uri.TryCreate(candidate.MediaUrl, UriKind.Absolute,
                out Uri? media) && media.AbsolutePath.EndsWith(".gif",
                StringComparison.OrdinalIgnoreCase)) return false;
        // User explicitly asked for a genuine/documentary photograph: the
        // result needs a recognizable institutional provenance. A search
        // engine caption and a successfully downloaded JPG are not enough.
        // For ordinary non-documentary photo requests, rank archive results
        // higher without making the entire image capability unavailable.
        if (documentary && PublisherAuthority(candidate.SourceUri) == 0)
            return false;
        return true;
    }

    private static string UrlPath(string? url)
    {
        try { return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                ? Uri.UnescapeDataString(uri.AbsolutePath).Replace('_', ' ').Replace('-', ' ')
                : string.Empty; }
        catch (UriFormatException) { return string.Empty; }
    }
    private static string Unescape(string? text) =>
        (text ?? string.Empty).Replace('_', ' ').Replace('-', ' ');
}
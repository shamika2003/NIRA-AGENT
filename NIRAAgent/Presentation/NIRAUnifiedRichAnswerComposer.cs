using System.Text.Json;
using System.Text.RegularExpressions;
using NIRAAgent.Runtime.Contracts;
using NIRAAgent.Diagnostics;

namespace NIRAAgent.Presentation;

public sealed record NIRAUnifiedRichAnswer(string Screen, IReadOnlyList<NIRARichBlock> Blocks);

/// <summary>
/// Display-only completion boundary for NIRA's single agent loop. Preserves
/// explanatory Markdown, moves observed playable videos into one end-of-reply
/// group, and leaves photographs in their existing paragraph-anchored path.
/// Only user-provided or successfully observed URLs can become player blocks.
/// </summary>
public static class NIRAUnifiedRichAnswerComposer
{
    private static readonly Regex Appendix = new(
        @"(?:^|\r?\n)\s*```nira-blocks\s*\r?\n(?<json>\{[\s\S]*\})\s*\r?\n```\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex Url = new(
        @"https://[^\s<>\]\)\('""\\]{1,1500}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex MarkdownLink = new(
        @"\[(?<label>[^\]\r\n]{1,180})\]\((?<url>https://[^\s<>\)]{1,1900})\)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex InternalSourceLabel = new(
        @"^(?:S[1-9]\d*|Source\s+S[1-9]\d*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));
    // The research binder uses this label for a verified VIDEO citation.
    // It is not authored explanatory prose and must not be left inline when
    // the real playable item is displayed once in the video collection.
    private static readonly Regex BoundVideoCitationLabel = new(
        @"^Video\s*[·:\-]\s*.+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static NIRAUnifiedRichAnswer Compose(string answer, string userRequest,
        IReadOnlyList<string>? successfulObservationReceipts)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > 120_000)
            return new NIRAUnifiedRichAnswer(answer ?? "", Array.Empty<NIRARichBlock>());

        var trustedVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddUserVideoUrls(userRequest, trustedVideos);
        var observedTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string receipt in successfulObservationReceipts ?? Array.Empty<string>())
            if (receipt.Length <= 100_000)
                AddObservedVideoSources(receipt, trustedVideos, observedTitles);

        Match appendix;
        try { appendix = Appendix.Match(answer); }
        catch (RegexMatchTimeoutException)
        { return new NIRAUnifiedRichAnswer(answer, Array.Empty<NIRARichBlock>()); }
        if (!appendix.Success || appendix.Groups["json"].Length > 26_000)
            return GroupVideosAtEnd(answer, Array.Empty<NIRARichBlock>(), trustedVideos,
                observedTitles);

        string screen = answer[..appendix.Index].TrimEnd();
        try
        {
            using JsonDocument document = JsonDocument.Parse(appendix.Groups["json"].Value,
                new JsonDocumentOptions { MaxDepth = 12 });
            var proposed = NIRARichBlockJsonReader.Read(document.RootElement);
            var accepted = new List<NIRARichBlock>();
            foreach (NIRARichBlock block in proposed.Take(18))
            {
                if (block.Type is "file" or "source") continue;
                if (block.Type == "video" &&
                    (!NIRAVideoReferencePolicy.TryParse(block.Text, out var video) ||
                     video is null || !trustedVideos.Contains(video.Key))) continue;
                accepted.Add(block);
            }
            if (string.IsNullOrWhiteSpace(screen))
                return new NIRAUnifiedRichAnswer(answer, Array.Empty<NIRARichBlock>());
            return GroupVideosAtEnd(screen, accepted, trustedVideos, observedTitles);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or
            InvalidOperationException or RegexMatchTimeoutException)
        {
            NIRADiagnosticTrace.Event("AGENT-RICH-PRESENTATION",
                "Rejected=" + ex.GetType().Name);
            return GroupVideosAtEnd(screen.Length > 0 ? screen : answer,
                Array.Empty<NIRARichBlock>(), trustedVideos, observedTitles);
        }
    }

    private static NIRAUnifiedRichAnswer GroupVideosAtEnd(string screen,
        IReadOnlyList<NIRARichBlock> blocks, HashSet<string> trustedVideos,
        IReadOnlyDictionary<string, string> observedTitles)
    {
        // One collection per answer. Deduplicate by verified provider/video ID;
        // NEVER infer a video title from the subject of a neighboring web page.
        var videos = new List<NIRARichBlock>();
        var other = new List<NIRARichBlock>(blocks.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddVideo(NIRAVideoReference video, string? label)
        {
            if (!trustedVideos.Contains(video.Key) || !seen.Add(video.Key)) return;
            string title = CleanVideoLabel(label, video);
            if (NIRAVideoTitlePlaceholder(title, video) &&
                observedTitles.TryGetValue(video.Key, out string? observed))
                title = CleanVideoLabel(observed, video);
            videos.Add(new NIRARichBlock { Type = "video", Title = title,
                Text = video.OriginalUrl });
        }
        string Rewrite(string source)
        {
            // Transform cited/watch links into readable prose; the associated
            // playable item is now in the single video group at the end.
            string linked = MarkdownLink.Replace(source, match =>
            {
                if (!NIRAVideoReferencePolicy.TryParse(match.Groups["url"].Value,
                        out var video) || video is null ||
                    !trustedVideos.Contains(video.Key)) return match.Value;
                string label = match.Groups["label"].Value.Trim();
                AddVideo(video, label);
                // The authoritative source binder has already verified this
                // URL. The actual player with its descriptive title is rendered
                // once at the bottom; don't leak "Video · ..." citations into
                // engineering prose. Other authored link labels stay readable.
                return InternalSourceLabel.IsMatch(label) ||
                    BoundVideoCitationLabel.IsMatch(label)
                    ? "(see video collection below)" : label;
            });
            // Also accept an actual standalone or inline plaintext watch URL.
            // Do not replace non-video web links or unsupported destinations.
            return Url.Replace(linked, match =>
            {
                string raw = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*', '"');
                if (!NIRAVideoReferencePolicy.TryParse(raw, out var video) ||
                    video is null || !trustedVideos.Contains(video.Key)) return match.Value;
                AddVideo(video, null);
                return "(see video collection below)" + match.Value[raw.Length..];
            });
        }

        try
        {
            // Preserve code fences and their literal URLs untouched.
            string[] lines = screen.Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                { inCode = !inCode; continue; }
                if (!inCode) lines[i] = Rewrite(lines[i]);
            }
            screen = string.Join("\n", lines);
            foreach (NIRARichBlock block in blocks)
            {
                if (block.Type == "video")
                {
                    if (NIRAVideoReferencePolicy.TryParse(block.Text, out var v) && v is not null)
                        AddVideo(v, block.Title);
                }
                else if (block.Type is "card" or "text" or "insight" or "details")
                    other.Add(block with { Text = Rewrite(block.Text) });
                else other.Add(block);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological model paragraph cannot make us display invented
            // videos, or discard the underlying answer.
            return new NIRAUnifiedRichAnswer(screen, blocks);
        }
        other.AddRange(videos);
        NIRADiagnosticTrace.Event("AGENT-RICH-PRESENTATION",
            "Accepted=" + other.Count + " Videos=" + videos.Count + " Placement=End");
        return new NIRAUnifiedRichAnswer(screen.Trim(), other);
    }

    private static string CleanVideoLabel(string? proposed, NIRAVideoReference video)
    {
        string label = (proposed ?? "").Trim();
        if (BoundVideoCitationLabel.IsMatch(label))
            label = Regex.Replace(label, @"^Video\s*[·:\-]\s*", "",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (InternalSourceLabel.IsMatch(label) ||
            label.Equals("Source", StringComparison.OrdinalIgnoreCase) ||
            label.Equals("Video", StringComparison.OrdinalIgnoreCase) ||
            label.Length is < 4 or > 140)
            return video.Provider == "YouTube" ? "YouTube video" : video.Provider + " video";
        // The title is plain display text. Never pass through Markdown/HTML.
        return Regex.Replace(label, @"[\r\n\[\]<>]", " ").Trim();
    }

    private static bool NIRAVideoTitlePlaceholder(string title, NIRAVideoReference video) =>
        title.Equals(video.Provider + " video", StringComparison.OrdinalIgnoreCase);

    private static void AddUserVideoUrls(string source, HashSet<string> keys)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            foreach (Match match in Url.Matches(source.Length > 12000
                ? source[..12000] : source).Cast<Match>().Take(30))
            {
                string address = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*', '"');
                if (NIRAVideoReferencePolicy.TryParse(address, out var video) && video is not null)
                    keys.Add(video.Key);
            }
        }
        catch (RegexMatchTimeoutException) { }
    }

    private static void AddObservedVideoSources(string receipt, HashSet<string> keys,
        Dictionary<string, string> titles)
    {
        // Receipts supplied by the trusted host are authoritative only about
        // their structured source URLs. Do not promote arbitrary links found
        // inside untrusted page excerpts, snippets or JSON string values.
        try
        {
            using JsonDocument outer = JsonDocument.Parse(receipt,
                new JsonDocumentOptions { MaxDepth = 20 });
            JsonElement root = outer.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("output", out JsonElement output) &&
                output.ValueKind == JsonValueKind.String)
            {
                using JsonDocument inner = JsonDocument.Parse(output.GetString() ?? "{}",
                    new JsonDocumentOptions { MaxDepth = 20 });
                AddSourceArray(inner.RootElement, keys, titles);
            }
            else AddSourceArray(root, keys, titles);
        }
        catch (JsonException) { }
    }

    private static void AddSourceArray(JsonElement root, HashSet<string> keys,
        Dictionary<string, string> titles)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("sources", out JsonElement sources) ||
            sources.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement item in sources.EnumerateArray().Take(60))
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("url", out JsonElement url) ||
                url.ValueKind != JsonValueKind.String ||
                !NIRAVideoReferencePolicy.TryParse(url.GetString(), out var video) ||
                video is null) continue;
            keys.Add(video.Key);
            if (!item.TryGetProperty("title", out var title) ||
                title.ValueKind != JsonValueKind.String) continue;
            string name = (title.GetString() ?? "").Trim();
            if (name.Length is >= 4 and <= 140) titles.TryAdd(video.Key, name);
        }
    }

    public static int RunSelfCheck()
    {
        const string video = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        string valid = "Here is a full explanation.\n\n```nira-blocks\n" +
            "{\"displayBlocks\":[{\"type\":\"insight\",\"title\":\"Key idea\",\"text\":\"Observed.\"}," +
            "{\"type\":\"video\",\"title\":\"Verified footage\",\"text\":\"" + video + "\"}]}\n```";
        var trusted = Compose(valid, "Show me", new[] { "{\"sources\":[{\"url\":\"" + video + "\",\"title\":\"Verified footage\"}]}" });
        if (trusted.Blocks.Count != 2 || trusted.Screen.Contains("nira-blocks") ||
            trusted.Blocks[^1].Title != "Verified footage")
            throw new InvalidOperationException("Rich video appendix did not survive evidence filtering.");
        var untrusted = Compose(valid, "Show me", Array.Empty<string>());
        if (untrusted.Blocks.Count != 1 || untrusted.Blocks[0].Type != "insight")
            throw new InvalidOperationException("Unobserved video promoted into a trusted player.");
        const string videoAlias = "Here is the demonstration.\n\n```nira-blocks\n" +
            "{\"displayBlocks\":[{\"kind\":\"video_player\",\"title\":\"Launch and landing\",\"videoUrl\":\"" +
            video + "\"}]}\n```";
        var alias = Compose(videoAlias, "Watch the demonstration", new[] {
            "{\"sources\":[{\"url\":\"" + video + "\",\"title\":\"Launch and landing\"}]}" });
        if (alias.Blocks.Count != 1 || alias.Blocks[0].Type != "video" ||
            alias.Blocks[0].Title != "Launch and landing")
            throw new InvalidOperationException("Valid verified videoUrl block alias was discarded.");
        const string dualVideo = "Engineering discussion.\n\n```nira-blocks\n" +
            "{\"displayBlocks\":[{\"type\":\"video\",\"title\":\"Actual footage\"," +
            "\"text\":\"A meaningful description of the recovery sequence.\"," +
            "\"url\":\"" + video + "\"}]}\n```";
        var dualAccepted = Compose(dualVideo, "Explain recovery", new[] {
            "{\"sources\":[{\"url\":\"" + video + "\",\"title\":\"Actual footage\"}]}" });
        if (dualAccepted.Blocks.Count != 1 ||
            dualAccepted.Blocks[0].Type != "video" ||
            dualAccepted.Blocks[0].Text != video)
            throw new InvalidOperationException("Model descriptive video + URL shape was discarded.");
        var dualRejected = Compose(dualVideo, "Explain recovery", Array.Empty<string>());
        if (dualRejected.Blocks.Count != 0)
            throw new InvalidOperationException("Unobserved descriptive video was promoted.");
        var aliasDenied = Compose(videoAlias, "Explain launch sequence", Array.Empty<string>());
        if (aliasDenied.Blocks.Count != 0)
            throw new InvalidOperationException("Unobserved videoUrl alias promoted into a player.");
        var plain = Compose("Regular complete Markdown answer.", "hi", Array.Empty<string>());
        if (plain.Screen != "Regular complete Markdown answer." || plain.Blocks.Count != 0)
            throw new InvalidOperationException("Plain prose was modified by presentation.");
        var inline = Compose("Here is [Falcon booster landing](" + video + ") with details.",
            "Show video", new[] { "{\"sources\":[{\"url\":\"" + video + "\"}]}" });
        if (inline.Blocks.Count != 1 || inline.Blocks[0].Title != "Falcon booster landing" ||
            inline.Screen.Contains(video, StringComparison.Ordinal))
            throw new InvalidOperationException("Inline video was not moved to end-of-reply deck.");
        var boundCitation = Compose("Falcon 9 descent is explained [Video · Rocket descent](" +
            video + ").", "Show video", new[] {
            "{\"sources\":[{\"url\":\"" + video + "\"}]}" });
        if (boundCitation.Blocks.Count != 1 ||
            !boundCitation.Screen.Contains("video collection below", StringComparison.Ordinal) ||
            boundCitation.Screen.Contains("Video ·", StringComparison.Ordinal) ||
            boundCitation.Blocks[0].Title != "Rocket descent")
            throw new InvalidOperationException("Internal video citation leaked into explanation.");
        var duplicate = Compose("[Landing](" + video + ") and [Landing again](" + video + ")",
            "Show videos", new[] { "{\"sources\":[{\"url\":\"" + video + "\"}]}" });
        if (duplicate.Blocks.Count != 1)
            throw new InvalidOperationException("Duplicate watch URLs created extra player cards.");
        return 3; // Three production smoke families; extended video assertions above.
    }
}

/// <summary>
/// R81: A requested report is a file deliverable, not a cue to repeat its
/// chapters or downloaded reference pictures in chat. This presentation
/// projection requires paired, audited current-run Studio-create and DOCX
/// readback receipts. It never treats model prose as file evidence.
/// </summary>
public static class NIRADocxArtifactReplyPolicy
{
    public static bool TryCompose(AgentTaskContract? contract,
        IReadOnlyList<string> verifiedReceipts, out NIRAUnifiedRichAnswer reply)
    {
        reply = new NIRAUnifiedRichAnswer(string.Empty, Array.Empty<NIRARichBlock>());
        if (contract?.RequiresSavedArtifact != true || !contract.RequiresReadBack ||
            !contract.RequestedFileNames.Any(n => n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)))
            return false;
        var writes = new List<(string Path, string Hash)>();
        var inspections = new List<(string Path, string Hash, string Output)>();
        foreach (string raw in verifiedReceipts.Take(64))
        {
            if (raw.Length is < 2 or > 100_000) continue;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(raw);
                JsonElement r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object ||
                    Read(r, "status") != "Succeeded" ||
                    r.TryGetProperty("auditRecorded", out JsonElement audit) &&
                        audit.ValueKind != JsonValueKind.True) continue;
                // An absent audit field is not a trusted receipt.
                if (!r.TryGetProperty("auditRecorded", out audit) ||
                    audit.ValueKind != JsonValueKind.True) continue;
                string capability = Read(r, "capabilityId");
                string output = Read(r, "output");
                if (capability == "filesystem.docx.studio.create" &&
                    r.TryGetProperty("changedSystemState", out JsonElement changed) &&
                    changed.ValueKind == JsonValueKind.True &&
                    Field(output, "PostWriteVerified") == "True" &&
                    Field(output, "OverwroteExisting") == "False")
                {
                    string path = Field(output, "Destination");
                    string hash = Field(output, "DestinationSHA256");
                    if (IsRequestedPath(path, contract.RequestedFileNames) && ValidHash(hash))
                        writes.Add((path, hash));
                }
                else if (capability == "filesystem.docx.inspect")
                {
                    string path = Field(output, "Path");
                    string hash = Field(output, "SHA256");
                    if (IsRequestedPath(path, contract.RequestedFileNames) && ValidHash(hash))
                        inspections.Add((path, hash, output));
                }
            }
            catch (JsonException) { }
        }
        foreach (var saved in writes.AsEnumerable().Reverse())
        {
            var read = inspections.LastOrDefault(x =>
                x.Path.Equals(saved.Path, StringComparison.OrdinalIgnoreCase) &&
                x.Hash.Equals(saved.Hash, StringComparison.OrdinalIgnoreCase));
            if (read.Output is null) continue;
            string fileName = saved.Path.Split('\\', '/').Last();
            static string Count(string data, string field) =>
                int.TryParse(Field(data, field), out int value) ? value.ToString() : "not confirmed";
            string summary = "**Word report saved and inspected**\n\n" +
                "**File:** `" + saved.Path + "`\n\n" +
                "**Verified saved content:** " + Count(read.Output, "Paragraphs") +
                " paragraphs, " + Count(read.Output, "Tables") + " tables, " +
                Count(read.Output, "EmbeddedMediaParts") + " embedded images, and " +
                Count(read.Output, "ReferenceEntries") + " reference entries.\n\n" +
                "Structural inspection passed for **" + fileName +
                "**. Visual page layout and exact pagination have not been rendered or independently checked.";
            reply = new NIRAUnifiedRichAnswer(summary, Array.Empty<NIRARichBlock>());
            return true;
        }
        return false;
    }

    private static string Read(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() ?? string.Empty : string.Empty;

    private static string Field(string output, string key)
    {
        foreach (string raw in output.Split('\n'))
            if (raw.StartsWith(key + "=", StringComparison.Ordinal))
                return raw[(key.Length + 1)..].Trim().TrimEnd('\r');
        return string.Empty;
    }

    private static bool ValidHash(string hash) =>
        hash.Length == 64 && hash.All(Uri.IsHexDigit);

    private static bool IsRequestedPath(string path, IReadOnlyList<string> named) =>
        path.Length is >= 5 and <= 32760 &&
        ((path.Length >= 4 && char.IsLetter(path[0]) && path[1] == ':' &&
          path[2] is '\\' or '/') || path.StartsWith("\\\\", StringComparison.Ordinal)) &&
        named.Any(n => path.EndsWith("\\" + n, StringComparison.OrdinalIgnoreCase) ||
                       path.EndsWith("/" + n, StringComparison.OrdinalIgnoreCase));

    public static int RunSelfCheck()
    {
        const string destination = @"C:\Users\User\Documents\NIRA_ACCEPTANCE_SANDBOX\Report.docx";
        string hash = new string('a', 64);
        AgentTaskContract contract = AgentTaskContractPolicy.Create(
            "Create a 3 page illustrated Word report Report.docx with references and inspect the saved file.");
        var receipts = new[] {
            JsonSerializer.Serialize(new { capabilityId = "filesystem.docx.studio.create",
                status = "Succeeded", auditRecorded = true, changedSystemState = true,
                output = $"Destination={destination}\nDestinationSHA256={hash}\nPostWriteVerified=True\nOverwroteExisting=False" }),
            JsonSerializer.Serialize(new { capabilityId = "filesystem.docx.inspect",
                status = "Succeeded", auditRecorded = true, changedSystemState = false,
                output = $"Path={destination}\nSHA256={hash}\nParagraphs=20\nTables=1\nEmbeddedMediaParts=1\nReferenceEntries=5" })
        };
        if (!TryCompose(contract, receipts, out NIRAUnifiedRichAnswer response) ||
            response.Blocks.Count != 0 ||
            !response.Screen.Contains("Report.docx", StringComparison.Ordinal) ||
            response.Screen.Contains("Nikola Tesla", StringComparison.Ordinal))
            throw new InvalidOperationException("R81 verified report reply did not prioritize the file.");
        if (TryCompose(contract, receipts.Take(1).ToArray(), out _) ||
            TryCompose(contract, receipts.Select(x => x.Replace(hash, new string('b', 64),
                StringComparison.Ordinal)).ToArray().Take(1).ToArray(), out _))
            throw new InvalidOperationException("R81 report reply accepted absent readback.");
        return 3;
    }
}

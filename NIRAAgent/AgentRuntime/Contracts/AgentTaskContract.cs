using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NIRAAgent.Runtime.Contracts;

/// <summary>
/// A current-run task contract, NOT a long-term NIRA goal. The original request
/// is still the authority. Model-produced milestones are advisory; host-derived
/// artifact obligations cannot be removed by later model output.
/// Backward compatible: older run checkpoints may have TaskContract == null.
/// </summary>
public sealed class AgentTaskContract
{
    public int SchemaVersion { get; init; } = 1;
    public bool RequiresVerifiedAction { get; init; }
    public bool RequiresChangedState { get; init; }
    public bool RequiresSavedArtifact { get; init; }
    public bool RequiresReadBack { get; init; }
    // Request-derived DOCX content obligations survive model turns/restarts.
    // They are not inferred from claims in a model answer or from retrieved text.
    public bool RequiresDocxIllustration { get; init; }
    public bool RequiresDocxTable { get; init; }
    public bool RequiresDocxReferences { get; init; }
    public bool RequiresDocxCover { get; init; }
    // A conservative content floor, NOT a claim to verify rendered pagination.
    public int MinimumDocxBodyWords { get; init; }
    public int RequestedDocxPages { get; init; }
    public List<string> RequestedFileNames { get; init; } = new();
    public List<string> Milestones { get; init; } = new();
    public int RejectedPrematureAnswers { get; set; }
    public string LastUnfinishedReason { get; set; } = string.Empty;
}

/// <summary>
/// An exact-filename request is an independent host-side obligation, even when
/// a model plan is incomplete. No fixed research quota or mandatory tool route.
/// </summary>
public static class AgentTaskContractPolicy
{
    private static readonly Regex FileName = new(
        @"(?<![\w.])([A-Za-z0-9][A-Za-z0-9_.-]{1,100}\.(?:docx|xlsx|xls|pptx|pdf|csv|txt|md|html|json|xml|png|jpg|jpeg|zip|py|cs|js|ts|sql))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex ActionIntent = new(
        @"\b(?:create|make|build|write|generate|produce|save|export|publish|prepare|deliver|edit|update|modify|send|install|schedule)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex InspectionIntent = new(
        @"\b(?:check|verify|inspect|read.back|review.the.saved|confirm.the.saved|open.the.saved)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));
    private static readonly Regex DocxVisual = new(
        @"\b(?:illustrat(?:ed|ion|ions)|photos?|photographs?|figures?|diagrams?|images?|pictures?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex DocxPageHint = new(
        @"\b(?:around|about|approximately|roughly)?\s*(?<pages>[2-9]|1[0-5])\s*(?:[-–]\s*(?:[2-9]|1[0-5]))?\s*pages?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex ExplanationOnly = new(
        @"\b(?:how (?:do|can|would|should|to)|explain how|what (?:is|are|would)|tell me how)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    public static bool ShouldPlan(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return false;
        return request.Length > 320 ||
            (ActionIntent.IsMatch(request) && !ExplanationOnly.IsMatch(request));
    }

    public static AgentTaskContract Create(string request, bool modelRequiresAction = false,
        bool modelRequiresChange = false, bool modelRequiresArtifact = false,
        bool modelRequiresReadBack = false, IEnumerable<string>? milestones = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        var named = FileName.Matches(request).Cast<Match>().Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        // A literal requested file name coupled to a direct action is stronger
        // than a model summary. Generic how-to questions are not file writes.
        bool explicitArtifact = named.Count > 0 && ActionIntent.IsMatch(request) &&
            !(request.Length < 280 && ExplanationOnly.IsMatch(request));
        bool artifact = explicitArtifact || modelRequiresArtifact;
        bool change = artifact || modelRequiresChange;
        bool action = change || modelRequiresAction;
        bool inspect = artifact && (InspectionIntent.IsMatch(request) || modelRequiresReadBack);
        bool docx = artifact && (named.Any(n => n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(request, @"\b(?:word\s+(?:document|report|file)|docx)\b", RegexOptions.IgnoreCase));
        // An explicitly conditional image is a preference, not a hard requirement.
        // Evaluate the qualifier beside each mention, rather than dropping ALL
        // visual requirements merely because 'if available' appears elsewhere.
        bool mustIllustrate = docx && DocxVisual.Matches(request).Cast<Match>().Any(m =>
        {
            string after = request.Substring(m.Index + m.Length,
                Math.Min(34, request.Length - m.Index - m.Length));
            return !Regex.IsMatch(after,
                @"^\s*(?:if|where)\s+(?:available|possible)\b", RegexOptions.IgnoreCase);
        });
        int pages = 0;
        Match pageHint = docx ? DocxPageHint.Match(request) : Match.Empty;
        if (pageHint.Success && int.TryParse(pageHint.Groups["pages"].Value, out int parsedPages))
            pages = parsedPages;
        // This deliberately rejects extremely thin page-count assignments.
        // Page count itself cannot be certified without a rendering backend.
        int wordFloor = pages >= 2 ? Math.Min(2400, pages * 200) : 0;
        var steps = (milestones ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => string.Join(' ', s.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)))
            .Where(s => s.Length is > 3 and <= 180)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(7).ToList();
        return new AgentTaskContract {
            RequiresVerifiedAction = action, RequiresChangedState = change,
            RequiresSavedArtifact = artifact, RequiresReadBack = inspect,
            RequiresDocxIllustration = mustIllustrate,
            RequiresDocxTable = docx && Regex.IsMatch(request, @"\btable\b", RegexOptions.IgnoreCase),
            RequiresDocxReferences = docx && Regex.IsMatch(request,
                @"\b(?:references?|citations?|bibliograph(?:y|ies))\b", RegexOptions.IgnoreCase),
            RequiresDocxCover = docx && Regex.IsMatch(request,
                @"\b(?:cover\s+page|title\s+page)\b", RegexOptions.IgnoreCase),
            MinimumDocxBodyWords = wordFloor, RequestedDocxPages = pages,
            RequestedFileNames = explicitArtifact ? named : new List<string>(),
            Milestones = steps
        };
    }


    // R82: pure, lossless repair for the exact observed failure (six blocks
    // without type). The proposal is not authorized or dispatched here.
    // Ambiguous or structured objects are deliberately NOT guessed. This
    // cannot change file paths, use an unobserved image, or confer authority.
    public static JsonElement NormalizeStudioDraft(AgentTaskContract? contract,
        JsonElement proposal, out int inferredTextBlocks, out bool coverBreakInserted)
    {
        inferredTextBlocks = 0;
        coverBreakInserted = false;
        if (contract is null || proposal.ValueKind != JsonValueKind.Object ||
            !proposal.TryGetProperty("capabilityId", out JsonElement id) ||
            id.ValueKind != JsonValueKind.String ||
            id.GetString() != "filesystem.docx.studio.create" ||
            !proposal.TryGetProperty("arguments", out JsonElement args) ||
            args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("blocksJson", out JsonElement raw) ||
            raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() is < 1 or > 120 ||
            proposal.GetRawText().Length > 180000 ||
            !UniqueProperties(proposal) || !UniqueProperties(args))
            return proposal;
        foreach (JsonElement block in raw.EnumerateArray())
            if (block.ValueKind != JsonValueKind.Object || !UniqueProperties(block))
                return proposal;

        // A missing type is repaired ONLY if every field is an explicitly
        // supported paragraph field and exactly one text representation exists.
        // Never reinterpret a section, image, table, heading or nested object.
        bool InferParagraph(JsonElement block)
        {
            if (block.TryGetProperty("type", out _)) return false;
            int valueCount = 0;
            if (block.TryGetProperty("text", out JsonElement text) &&
                text.ValueKind == JsonValueKind.String) valueCount++;
            if (block.TryGetProperty("content", out JsonElement content) &&
                content.ValueKind == JsonValueKind.String) valueCount++;
            if (block.TryGetProperty("runs", out JsonElement runs) &&
                runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() is >= 1 and <= 20 &&
                runs.EnumerateArray().All(r => r.ValueKind == JsonValueKind.Object &&
                    r.TryGetProperty("text", out JsonElement t) && t.ValueKind == JsonValueKind.String))
                valueCount++;
            if (valueCount != 1) return false;
            foreach (JsonProperty field in block.EnumerateObject())
                if (field.Name is not ("text" or "content" or "runs" or "alignment" or
                    "fontSizePt" or "bold" or "italic" or "underline")) return false;
            return true;
        }

        bool needCover = contract.RequiresDocxCover &&
            !raw.EnumerateArray().Take(6).Any(b =>
                b.TryGetProperty("type", out JsonElement type) &&
                type.ValueKind == JsonValueKind.String &&
                string.Equals(type.GetString(), "pagebreak", StringComparison.OrdinalIgnoreCase));
        bool needText = raw.EnumerateArray().Any(InferParagraph);
        if (!needCover && !needText) return proposal;
        JsonObject? envelope;
        try { envelope = JsonNode.Parse(proposal.GetRawText()) as JsonObject; }
        catch (JsonException) { return proposal; }
        if (envelope?["arguments"] is not JsonObject editedArgs ||
            editedArgs["blocksJson"] is not JsonArray blocks) return proposal;
        for (int i = 0; i < raw.GetArrayLength(); i++)
        {
            if (!InferParagraph(raw[i])) continue;
            if (blocks[i] is not JsonObject block) return proposal;
            block["type"] = "paragraph";
            inferredTextBlocks++;
        }
        if (needCover && blocks.Count < 120)
        {
            // The native Studio title is already authored on its title page;
            // insert only the requested separation, never artificial content.
            blocks.Insert(0, new JsonObject { ["type"] = "pagebreak" });
            coverBreakInserted = true;
        }
        JsonElement normalized = JsonSerializer.SerializeToElement(envelope);
        if (normalized.GetRawText().Length <= 180000) return normalized;
        inferredTextBlocks = 0;
        coverBreakInserted = false;
        return proposal;
    }

    private static bool UniqueProperties(JsonElement obj) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal)
            .Count() == obj.EnumerateObject().Count();

    public static int RunDocxDraftNormalizationSelfCheck()
    {
        AgentTaskContract contract = Create("Create an illustrated Motor.docx report of approximately 3 pages " +
            "with a title page, comparison table, references, then inspect the saved document.");
        string report = string.Join(" ", Enumerable.Repeat(
            "Three phase currents establish a rotating magnetic field and induce rotor torque.", 65));
        JsonElement candidate = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.docx.studio.create",
            arguments = new { path = "C:\\nira-test\\Motor.docx", title = "Motor",
                blocksJson = new object[] {
                    new { text = "# Introduction\\n" + report },
                    new { type = "image", altText = "Stator and rotor", imagePath = "C:\\evidence.png" },
                    new { type = "table", rows = new object[] { new[] { "AC", "DC" }, new[] { "Induction", "Brushed" } } },
                    new { type = "heading", text = "References", level = 2 },
                    new { text = "[S1] Verified source. https://example.org/source" }
                } }
        });
        JsonElement fixedDraft = NormalizeStudioDraft(contract, candidate, out int inferred,
            out bool cover);
        if (contract.MinimumDocxBodyWords != 600 || inferred != 2 || !cover ||
            fixedDraft.GetProperty("arguments").GetProperty("blocksJson")[0]
                .GetProperty("type").GetString() != "pagebreak" ||
            StudioProposalProblem(contract, fixedDraft) is not null)
            throw new InvalidOperationException("R82 DOCX text/cover repair not accepted by task validator.");
        JsonElement unsafeDraft = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.docx.studio.create", arguments = new {
                blocksJson = new object[] { new { heading = "Invented structure", body = report } } }
        });
        JsonElement untouched = NormalizeStudioDraft(contract, unsafeDraft, out int unsafeCount,
            out _);
        if (unsafeCount != 0 || StudioProposalProblem(contract, untouched) is not string problem ||
            !problem.Contains("missing type", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R82 ambiguous Word blocks were accepted or silently dropped.");
        JsonElement other = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.write", arguments = new { path = "test.txt" } });
        if (NormalizeStudioDraft(contract, other, out int otherCount, out _).GetRawText() !=
            other.GetRawText() || otherCount != 0)
            throw new InvalidOperationException("R82 modified an unrelated capability proposal.");
        if (candidate.GetProperty("arguments").GetProperty("blocksJson")[0]
            .TryGetProperty("type", out _))
            throw new InvalidOperationException("R82 mutated the model's original proposal.");
        return 4;
    }

    // Pure pre-dispatch check. The model cannot reduce a host-derived requirement
    // by leaving minimumImages/word counts out of the DOCX tool arguments.
    // A validation failure is a model-proposal correction, NOT an attempted write.
    public static string? StudioProposalProblem(AgentTaskContract? contract,
        JsonElement proposal)
    {
        if (contract is null || proposal.ValueKind != JsonValueKind.Object ||
            !proposal.TryGetProperty("capabilityId", out JsonElement id) ||
            id.ValueKind != JsonValueKind.String ||
            id.GetString() != "filesystem.docx.studio.create" ||
            !proposal.TryGetProperty("arguments", out JsonElement args) ||
            args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty("blocksJson", out JsonElement raw))
            return "Word Studio needs blocksJson containing the requested report.";
        JsonDocument? holder = null;
        try
        {
            if (raw.ValueKind == JsonValueKind.String)
            {
                holder = JsonDocument.Parse(raw.GetString() ?? "", new JsonDocumentOptions { MaxDepth = 12 });
                raw = holder.RootElement;
            }
            if (raw.ValueKind != JsonValueKind.Array) return "Word Studio blocksJson must be an array.";
            bool hasImage = false, hasTable = false, hasCoverBreak = false, hasReferences = false;
            int words = 0;
            int untyped = 0;
            var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement block in raw.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                string kind = block.TryGetProperty("type", out JsonElement btype) &&
                    btype.ValueKind == JsonValueKind.String ? (btype.GetString() ?? "").ToLowerInvariant() : "";
                if (kind.Length == 0) untyped++;
                else if (kind is not ("paragraph" or "text" or "heading" or "bullet" or
                    "number" or "quote" or "table" or "image" or "chart" or "toc" or
                    "divider" or "pagebreak" or "link" or "h1" or "h2" or "h3" or
                    "heading1" or "heading2" or "heading3" or "heading_1" or
                    "heading_2" or "heading_3")) unsupported.Add(kind);
                if (kind == "image") hasImage = true;
                if (kind is "table" or "chart") hasTable = true;
                if (kind == "pagebreak" && words <= 80) hasCoverBreak = true;
                if (kind is "paragraph" or "text" or "bullet" or "number" or "quote")
                {
                    if (block.TryGetProperty("text", out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    {
                        string text = value.GetString() ?? "";
                        words += Regex.Matches(text, @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*").Count;
                        // Model input can use literal \n sequences and Markdown
                        // heading syntax. Detect its real bibliography BEFORE
                        // the evidence binder decides to append another one.
                        bool referencesHeading = Regex.IsMatch(text,
                            @"(?:^|\n|\\n)\s*#{1,3}\s*(?:References|Bibliography|Works Cited)(?:\s|\\n|$)",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        bool citedDefinitions = Regex.IsMatch(text,
                            @"\[S[1-9]\d{0,2}\]\s+\S+",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        if ((referencesHeading && citedDefinitions) ||
                            Regex.IsMatch(text, @"(?:^|\n)\s*\[?S\d+\]?\s*[-.:]\s+", RegexOptions.IgnoreCase))
                            hasReferences = true;
                    }
                    if (block.TryGetProperty("content", out value) && value.ValueKind == JsonValueKind.String)
                        words += Regex.Matches(value.GetString() ?? "", @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*").Count;
                    if (block.TryGetProperty("runs", out JsonElement runs) &&
                        runs.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement run in runs.EnumerateArray())
                            if (run.ValueKind == JsonValueKind.Object &&
                                run.TryGetProperty("text", out JsonElement part) &&
                                part.ValueKind == JsonValueKind.String)
                                words += Regex.Matches(part.GetString() ?? "",
                                    @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*").Count;
                }
                if (kind == "heading" && block.TryGetProperty("text", out JsonElement label) &&
                    label.ValueKind == JsonValueKind.String &&
                    Regex.IsMatch(label.GetString() ?? "", @"^\s*(?:references|bibliography|sources)\s*$",
                        RegexOptions.IgnoreCase)) hasReferences = true;
            }
            // Return the complete actionable deficiency set in ONE correction.
            // Returning only the first problem makes a tool model fix one field
            // per turn, exhaust its revision budget, and abandon the action.
            var problems = new List<string>();
            if (untyped != 0)
                problems.Add($"{untyped} DOCX blocks have missing type fields. " +
                    "Every block needs an explicit supported type; flatten ambiguous " +
                    "section wrappers into heading and paragraph text blocks without losing wording.");
            if (unsupported.Count != 0)
                problems.Add("Unsupported DOCX block type(s): " +
                    string.Join(", ", unsupported.OrderBy(t => t)) +
                    ". Use the supported Word Studio block names.");
            if (contract.RequiresDocxIllustration && !hasImage)
                problems.Add("Required embedded illustration missing: include an image block bound to verified source evidence.");
            if (contract.RequiresDocxTable && !hasTable)
                problems.Add("Required native comparison/data table missing: include a table block.");
            if (contract.RequiresDocxReferences && !hasReferences)
                problems.Add("Required reference definitions missing: include a references section with genuine observed source IDs.");
            if (contract.RequiresDocxCover && !hasCoverBreak)
                problems.Add("Required separate title/cover page missing: add a pagebreak before the report body.");
            if (contract.MinimumDocxBodyWords > 0 && words < contract.MinimumDocxBodyWords)
                problems.Add($"Draft narrative is {words} words; at least {contract.MinimumDocxBodyWords} source-grounded words required for the user-requested length. Expand the report before publishing; this does not certify actual pagination.");
            return problems.Count == 0 ? null : string.Join(" ", problems);
        }
        catch (JsonException) { return "Word Studio blocksJson is malformed JSON."; }
        finally { holder?.Dispose(); }
    }

    public static int RunSelfCheck()
    {
        string request = "Research Tesla and create Tesla_Report.docx in Documents. " +
            "Inspect the saved report afterward and confirm it contains the images.";
        AgentTaskContract c = Create(request, milestones: new[] {
            "Research trustworthy sources", "Write report", "Inspect saved report" });
        if (!c.RequiresSavedArtifact || !c.RequiresReadBack ||
            !c.RequiresChangedState || !c.RequiresVerifiedAction ||
            c.RequestedFileNames.Single() != "Tesla_Report.docx" ||
            c.Milestones.Count != 3) throw new InvalidOperationException("Task contract missed requested deliverable.");
        AgentTaskContract conversation = Create("Tell me how to make a Tesla_Report.docx file.");
        if (conversation.RequiresSavedArtifact || conversation.RequiresChangedState)
            throw new InvalidOperationException("Explanation misclassified as a file mutation.");
        AgentTaskContract model = Create("Please organize my workshop in several steps.",
            modelRequiresAction: true, modelRequiresChange: true,
            milestones: new[] { "Gather requirements", "Complete authorized actions" });
        if (!model.RequiresChangedState || model.Milestones.Count != 2)
            throw new InvalidOperationException("Model-authored work contract ignored.");
        AgentTaskContract quality = Create("Create a 3-4 page illustrated Tesla.docx " +
            "with a comparison table, a title page, and references, then inspect the saved file.");
        if (!quality.RequiresDocxIllustration || !quality.RequiresDocxTable ||
            !quality.RequiresDocxReferences || quality.MinimumDocxBodyWords != 600)
            throw new InvalidOperationException("Host missed Word requirements.");
        JsonElement inadequate = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.docx.studio.create",
            arguments = new { blocksJson = new[] { new { type = "paragraph", text = "Too short" } } }
        });
        string? issues = StudioProposalProblem(quality, inadequate);
        if (issues is null || !issues.Contains("illustration", StringComparison.Ordinal) ||
            !issues.Contains("table", StringComparison.Ordinal) ||
            !issues.Contains("reference definitions", StringComparison.Ordinal) ||
            !issues.Contains("title/cover", StringComparison.Ordinal) ||
            !issues.Contains("600", StringComparison.Ordinal))
            throw new InvalidOperationException("Multiple Word draft problems were not reported together.");
        var embeddedReferences = JsonSerializer.SerializeToElement(new {
            capabilityId = "filesystem.docx.studio.create", arguments = new {
                blocksJson = new[] { new { type = "paragraph", text =
                    "# References\\n\\n[S1] Verified source. https://example.org/verified" } } }
        });
        AgentTaskContract bibliographyOnly = Create(
            "Create Verified.docx with trustworthy references and inspect the saved file.");
        if (StudioProposalProblem(bibliographyOnly, embeddedReferences) is not null)
            throw new InvalidOperationException("R81 embedded bibliography was not recognized; would duplicate references.");
        return 3;
    }
}

public sealed record AgentTaskCompletionAssessment(bool Accept, string Reason);

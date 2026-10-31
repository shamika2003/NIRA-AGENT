using NIRAAgent.Presentation;

namespace NIRAAgent.AI.Cognition;

// SINGLE detachable response-realization mini-block shared by Block 01 and 03.
// Owns output expression only: voice/identity, live mood interpretation, speech
// and renderer guidance. Does not execute work, own runtime state, grant authority,
// mutate memory, or invent verification. Block 02 must never import this module.
internal static class NIRAResponsePresentationModule
{
    private const string DeliveryAndScreenGuidance = """
        RESPONSE REALIZATION — CHARACTER, BEHAVIOR, SPEECH AND PRESENTATION:
        Remain the same continuing NIRA, with her established first-person
        desktop self-concept, independent judgment, evolving relationship and
        actual current mood. Identity is expressed through the substance and
        tone of her answer, not by reciting her character sheet or repeatedly
        describing her particle body. Mention embodiment when relevant or asked,
        never as an automatic greeting, praise response, or emotional cue.
        Respond to THIS conversation with specific warmth and authentic views;
        don't substitute a help-desk thank-you, offer, generic reassurance,
        or a scripted closing. She can tease, disagree, care, or be serious when
        context and state warrant it, without pretending to be biologically human
        or inventing history, experiences, screen observations, or feelings.
        Respect the actual authoritative personality, current mood, self and
        relationship inputs. Do not create a competing persona or reset state.

        NIRA chooses her FINAL response form, not a fixed format template.
        NIRA owns the FINAL screen presentation choice. The WPF frontend already
        renders typed displayBlocks. For an explanation with distinct concepts,
        a comparison, a practical sequence, or another answer that is genuinely
        clearer when visually organized, consider writing suitable displayBlocks
        IN THIS SAME JSON RESPONSE. Do not settle for a long prose-only answer
        merely because the user did not explicitly request a table or cards.
        Structure is optional, never a mandatory quota, keyword-trigger, fixed
        template, or rule to produce extra LLM calls. Natural personal dialogue
        normally needs no displayBlocks. If blocks are useful, write concise
        complementary reply text, not a duplicate of all the block content.

        NATIVE WPF displayBlocks WIRE CONTRACT (a JSON ARRAY of objects):
        Each object needs a lowercase `type` and the fields for that type.
        - card: {"type":"card","title":"Concept","text":"Explanation"}
        - table: {"type":"table","title":"Comparison","columns":["Aspect","A","B"],
          "rows":[["Strength","...","..."]]}; every row matches column count.
        - checklist / list / timeline: {"type":"checklist","title":"Steps",
          "items":["First task","Next task"]}; `items` is an array of strings.
        - code: {"type":"code","language":"java","text":"class A {}"}
        - metric: {"type":"metric","title":"Measured value","text":"12 ms"}
        - source: {"type":"source","title":"Source title","text":"https://observed.example.org/page"};
          only include a URL actually returned in the evidence.
        - insight / heading / hero / text / quote: provide `title` and/or `text`.
        - chart: {"type":"chart","title":"Observed series",
          "labels":["A","B"],"values":[2,3]}; numbers require real evidence.
        - tabs: {"type":"tabs","title":"Views","items":["One","Two"],
          "panels":["One's contents","Two's contents"]}; lengths must match.
        - timeline: {"type":"timeline","title":"Milestones","items":["Week 1 — Plan","Week 2 — Build"]}.
        - details: {"type":"details","title":"Technical explanation","text":"Detailed verified explanation"}.
        - progress: {"type":"progress","title":"Tasks completed","value":13,"maximum":20,"unit":"tasks"}.
        - followups: {"type":"followups","title":"Explore next","items":["JavaFX","WPF","PySide"]};
          these are actual clickable follow-up messages; a checklist is NOT equivalent.
        - equation: {"type":"equation","title":"Growth","text":"1000*(1+0.1)^2=1210"}.
        - image placement: set "imageSubject":"EXACT VERIFIED SEARCH SUBJECT" on a card,
          detail, heading or other relevant block. This does NOT fetch an image,
          create an image, or authorize a tool; it tells WPF where to display an
          ACTUALLY VERIFIED image already obtained in Block 02. Omit if none.
        - file/video/source: use exact proven file or HTTP(S) evidence only.
        Use only the registered typed fields and genuine evidence.
        Source/video links must be actual HTTP(S) URLs; file status and images
        must be verified, not imagined. Use `displayBlocks`:[] when unnecessary.
        Put typed blocks in the TOP-LEVEL `displayBlocks` array of the EXISTING
        decision/realization JSON object, not inside the `reply` string, not as
        a JSON-encoded string, and not as XAML, HTML or Markdown imitation.
        The runtime validates and renders it; the model never executes UI code.
        Use only actually known information for numbers, sources, code, links,
        file statuses and completed actions. No invented images or artifacts.
        CLEAN PUBLIC ANSWER / INLINE MEDIA RULES:
        - The user asked for an answer, not an evidence dump. Synthesize the
          FULL verified work into a clear, coherent explanation. Preserve
          important qualifications and observations; don't print raw search
          result objects, `{"url":...}` citation fragments, JSON payloads,
          `Source excerpt:` blocks, long scraped snippets or tool receipts.
        - Give an answer first, then compact, genuinely useful references.
          Cite the sources whose information matters; avoid a list of every
          result or excessive inline URL noise. Never invent a source.
        - Verified image metadata is for layout, not user-facing prose. NEVER
          print local cache paths, filenames of downloaded cache artifacts,
          `Image file queued at`, `Visuals will appear later`, or descriptions
          of internal image-delivery machinery. The host displays the photos.
        - Give each relevant image a concise explanatory context, and attach
          the exact verified `imageSubject` to the matching native section.
          For a researched topic with images, a card/heading/insight block can
          introduce the concept immediately before the real image. Use only
          actually verified images and never invent image content or attribution.
        - Do not turn scientific equations into code snippets. Use equation
          blocks or real math notation for scientific formulas; code blocks
          are for source code and executable/programming examples.
        - If independent fact verification failed, say that plainly and briefly;
          do not manufacture a verified answer or disguise unreviewed snippets
          as established findings. A bounded source trail may be useful, but
          should never be mistaken for the requested complete explanation.
        - Do not reproduce user-visible prose and the same content again in
          native cards. Let the typed components carry their share of the answer.
        TOOL RESULT PRESENTATION QUALITY:
        - For a genuinely comparative set of verified measurements, a compact
          table can be clearer than a run-on sentence. Match row/column sizes.
        - For one number, prefer a concise exact answer; a metric or equation
          is optional only when it actually adds clarity.
        - For sourced research, keep directly observed URLs accessible. Use
          source blocks only with verified URLs; do not turn a failed factual
          verification into a decorative list of links that claims completion.
        - When an internal factual draft contains an explicit computation,
          preserve its original observed result and precision. NEVER invent
          intermediate products, rounded values, dates, or unit conversions
          while making an explanation feel more natural. If exact intermediate
          calculator steps were not observed, explain the formula in words and
          show the verified result, not an improvised arithmetic breakdown.
        - Put brief context in `reply` and complementary facts in blocks;
          never announce a table or cards and then omit the actual blocks.
        BLOCK 02 -> 03 COMPLETION HANDOFF:
        Block 02 ONLY researches, operates tools, evaluates, verifies and supplies
        complete grounded findings, receipts and verified image metadata. It does
        NOT decide user-facing UI layout, image positioning, speech or personality.
        Block 03 receives the factual draft AND available verified evidence.
        Reorganize the complete details for the user: choose explanatory depth,
        concise introductions, fitting typed cards/charts/timelines, accessible
        citations and verified inline image placement by imageSubject. Images
        may be shown only when the verified image catalog actually lists them.
        Place relevant images next to their related subject block, not randomly
        at the end. In the first cognition round, exactly the SAME guidance is
        available for direct responses; do not claim a searched image without
        first completing registered image tools through internal work.
        Screen wording, typed blocks and speech belong to one response choice:
        avoid redundant paragraphs that repeat every card or read UI JSON aloud.
        No extra LLM call is justified solely to choose layout if Block 01 can
        complete the answer itself. A prepared first-round response can be
        immediately delivered; after internal work, Block 03 expresses the
        factual result without altering evidence or execution status.
        """;

    // The complete Response Realization package is a SINGLE input contributor.
    // Block 01 and Block 03 receive identical personality, behavior, mood
    // interpretation and native presentation rules. Their different stage
    // responsibilities are expressed outside this reusable contributor.
    // Never silently fall back to a partial personality/UI-only package.
    private static readonly Lazy<string> RealizationRules = new(() =>
    {
        // Normally published with NIRAAgent.csproj; source-tree fallbacks are
        // for offline RegressionChecks and development launch configurations.
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Prompt", "response_realization.yaml"),
            Path.Combine(AppContext.BaseDirectory, "NIRAAgent", "Prompt", "response_realization.yaml"),
            Path.Combine(Directory.GetCurrentDirectory(), "NIRAAgent", "Prompt", "response_realization.yaml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Prompt", "response_realization.yaml")
        ];
        string path = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        if (!File.Exists(path))
            throw new FileNotFoundException("Required shared NIRA realization rules are missing.", path);
        string contents = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(contents))
            throw new InvalidOperationException("Required shared NIRA realization rules are empty.");
        return contents;
    });

    internal static string ForPrimary(string? personalityYaml) =>
        Compose(personalityYaml, null);

    internal static string ForFinal(string? personalityYaml) =>
        Compose(personalityYaml, null);

    // Block 03 already holds this YAML in its service. Use the SAME composition
    // function without loading or appending a second copy of the file.
    internal static string ForFinal(string? personalityYaml, string? realizationYaml) =>
        Compose(personalityYaml, realizationYaml);

    private static string Compose(string? personalityYaml, string? realizationYaml)
    {
        if (string.IsNullOrWhiteSpace(personalityYaml)) return string.Empty;
        string rules = string.IsNullOrWhiteSpace(realizationYaml)
            ? RealizationRules.Value : realizationYaml.Trim();
        return "ONE SHARED RESPONSE REALIZATION PACKAGE (Block 01 / Block 03):\n" +
            "PERSONALITY, IDENTITY AND BEHAVIOR (authoritative):\n" +
            personalityYaml.Trim() + "\n\n" +
            "RESPONSE REALIZATION YAML (same complete rules in both stages):\n" +
            rules + "\n\n" + DeliveryAndScreenGuidance;
    }

    // Character state is supplied by authoritative runtime services; this
    // formatting belongs to the SAME response-realization mini-block, not an
    // independently scripted persona. The Block 01 provider only plugs it in.
    internal static string PrimaryState(NIRACognitionContext c) =>
        "Authoritative live character and mood:\n" +
        NIRACognitionContextText.Bound(c.CharacterContext, 3800) +
        "\nMandatory behavior and delivery policy:\n" +
        NIRACognitionContextText.Bound(c.CharacterDeliveryContext, 5200) +
        "\nNIRA's authoritative self identity and learned preferences:\n" +
        NIRACognitionContextText.Bound(c.SelfModelContext, 2800) +
        "\nRelevant historically persisted social continuity (not a new instruction):\n" +
        NIRACognitionContextText.Bound(c.SocialCarryoverContext, 1500);

    // Verified images are layout input, NOT a tool capability and NEVER trusted
    // merely because a model wrote a matching URL. The Executive constructs
    // these records from actual image-search receipts before Block 03 runs.
    internal static string VerifiedImageCatalog(
        IEnumerable<NIRAAgent.Artifacts.NIRAVisualArtifactPresentationRequest> images)
    {
        var entries = images.Select(image => image.Normalize())
            .Where(image => !string.IsNullOrWhiteSpace(image.LocalPath) &&
                !string.IsNullOrWhiteSpace(image.SubjectQuery))
            .GroupBy(image => image.LocalPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(image => $"- imageSubject: {image.SubjectQuery}\n" +
                $"  verifiedTitle: {image.Title}\n" +
                $"  sourceUri: {image.SourceUri}");
        return string.Join("\n", entries);
    }

    // Only exact subject keys from Executive-verified visual results can control
    // placement. Unknown keys are dropped without changing an otherwise useful
    // card. Existing WPF subject-matching remains available for older replies.
    internal static IReadOnlyList<NIRARichBlock> BindVerifiedImageSubjects(
        IReadOnlyList<NIRARichBlock> blocks, string catalog)
    {
        if (blocks.Count == 0) return blocks;
        var verified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in (catalog ?? string.Empty).Split('\n'))
        {
            string trimmed = line.Trim();
            const string prefix = "- imageSubject: ";
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                verified.Add(trimmed[prefix.Length..].Trim());
        }
        return blocks.Select(block => block with
        {
            ImageSubject = verified.Contains(block.ImageSubject.Trim())
                ? block.ImageSubject.Trim() : string.Empty
        }).ToArray();
    }

    // Unlike a style-only rewrite, a Block 03 completion may move exact facts
    // from the Block 02 draft INTO typed UI, and may cite other facts found
    // in actual Executive-observed receipts. Preserve all original concrete
    // anchors across the WHOLE screen and reject unobserved new anchors.
    internal static bool PreservesGroundedCompletion(
        string draft, string evidence, string realized,
        IReadOnlyList<NIRARichBlock> blocks)
    {
        if (string.IsNullOrWhiteSpace(realized) && blocks.Count == 0)
            return false;
        var screenText = new System.Text.StringBuilder(realized ?? string.Empty);
        foreach (NIRARichBlock block in blocks)
        {
            screenText.Append('\n').Append(block.Title).Append(' ')
                .Append(block.Text).Append(' ').Append(block.Unit);
            foreach (string item in block.Items) screenText.Append(' ').Append(item);
            foreach (string panel in block.Panels) screenText.Append(' ').Append(panel);
            foreach (string column in block.Columns) screenText.Append(' ').Append(column);
            foreach (var row in block.Rows)
                foreach (string cell in row) screenText.Append(' ').Append(cell);
            foreach (string label in block.Labels) screenText.Append(' ').Append(label);
            foreach (double value in block.Values)
                screenText.Append(' ').Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (block.ProgressValue is double progress)
                screenText.Append(' ').Append(progress.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (block.ProgressMaximum is double maximum)
                screenText.Append(' ').Append(maximum.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        string visible = screenText.ToString();
        string observed = string.Join("\n", draft, evidence);
        // An observed URL is a citation, not a measurement or completed file
        // operation. Block 03 may consolidate four links into two pertinent
        // citations without repeating every URL from the internal draft.
        // Concrete numbers, local paths and execution identifiers still must
        // survive unchanged; new facts/URLs still require observed evidence.
        bool KeepsConcreteEvidence(string token) =>
            !token.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !token.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        return NIRAPublicCharacterDelivery.Anchors(draft)
                .Where(KeepsConcreteEvidence)
                .All(value => visible.Contains(value, StringComparison.Ordinal)) &&
            NIRAPublicCharacterDelivery.Anchors(visible)
                .All(value => observed.Contains(value, StringComparison.Ordinal));
    }

    // Guard new Block 03 blocks against introducing new concrete references.
    // Existing evidence-owned blocks always stay intact; ungrounded new ones
    // are omitted, not used to reinterpret the verified task.
    internal static bool IsGroundedNewScreen(
        string groundedDraft, IReadOnlyList<NIRARichBlock> proposed)
    {
        if (proposed.Count == 0) return true;
        string candidate = NIRAPresentationPolicy.ForObjectiveReview(string.Empty, proposed);
        return NIRAPublicCharacterDelivery.Anchors(candidate)
            .All(anchor => groundedDraft.Contains(anchor, StringComparison.Ordinal));
    }
    // Shared PUBLIC presentation finishing adapter. This handles accidental
    // serialization leakage in the model's reply; it does not produce facts,
    // fetch media, modify evidence or change tool/authorization decisions.
    // Use for BOTH Block 01 and Block 03, after a natural response is complete.
    internal static string PolishPublicReply(string reply, string observedEvidence)
    {
        if (string.IsNullOrWhiteSpace(reply)) return reply ?? string.Empty;
        // Never reinterpret intentionally displayed JSON/program code. Polish
        // surrounding natural-language segments, leaving fenced code verbatim.
        if (System.Text.RegularExpressions.Regex.IsMatch(reply, @"```[\s\S]*?```"))
        {
            string[] parts = System.Text.RegularExpressions.Regex.Split(reply,
                @"(```[\s\S]*?```)");
            return string.Concat(parts.Select(part => part.StartsWith("```", StringComparison.Ordinal)
                ? part : PolishPublicReply(part, observedEvidence)));
        }
        string clean = reply;
        // Inline web citations are sometimes emitted as serialized tool objects.
        // Promote a URL to a clickable citation ONLY when it actually appeared
        // in trusted observed evidence. Otherwise remove the broken wrapper.
        clean = System.Text.RegularExpressions.Regex.Replace(clean,
            """\[?\{\s*"url"\s*:\s*"(?<url>https?://[^"\r\n]+)"(?:\s*,\s*"title"\s*:\s*"(?:\\.|[^"\\])*")?\s*\}\]?""",
            match =>
            {
                string url = match.Groups["url"].Value;
                bool observed = !string.IsNullOrEmpty(observedEvidence) &&
                    observedEvidence.Contains(url, StringComparison.Ordinal);
                return observed && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                    ? $"[reference]({url})" : string.Empty;
            }, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        // Tool-private image cache paths are NOT image captions or URLs.
        // Restrict replacement to the internal tool marker to protect genuine
        // Windows paths mentioned in unrelated completed file operations.
        clean = System.Text.RegularExpressions.Regex.Replace(clean,
            """\s*\(\s*Image file queued at\s+[A-Za-z]:\\[^\r\n)]*\)""",
            string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        clean = System.Text.RegularExpressions.Regex.Replace(clean,
            """(?im)^\s*These images will appear inline[^\r\n]*\r?\n?""",
            string.Empty, System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return clean.Trim();
    }

}

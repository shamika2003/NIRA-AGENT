using Nira.V2.Evidence;

int passed = 0;
const string NASA = "https://science.nasa.gov/mission/webb/";
const string Wiki = "https://en.wikipedia.org/wiki/James_Webb_Space_Telescope";
const string Passage = "The telescope observes infrared light from distant galaxies.";

Test("new claims are unreviewed, never silently verified", () =>
{
    var ledger = New();
    Add(ledger, "S1", NASA, Passage, EvidenceMaterialKind.RetrievedDocument);
    ledger.AddClaim("C1", "JWST observes infrared light.", "writer", new[] { "S1" });
    Check(!ledger.EvaluatePublication("C1").Publishable, "unreviewed claim published");
});

Test("a search snippet is never sufficient for supporting a claim", () =>
{
    var ledger = New();
    Add(ledger, "S1", NASA, Passage, EvidenceMaterialKind.SearchExcerpt);
    ledger.AddClaim("C1", "JWST observes infrared light.", "writer", new[] { "S1" });
    Throws<InvalidOperationException>(() => ledger.RecordReview("C1", "independent-reviewer",
        ReviewOutcome.ReviewerSupported, new[] { Anchor() }, rationale: "Quoted passage seems relevant."));
});

Test("exact retrieved-document passage and independent assessment can be recorded", () =>
{
    var ledger = New();
    Add(ledger, "S1", NASA, Passage, EvidenceMaterialKind.RetrievedDocument);
    ledger.AddClaim("C1", "JWST observes infrared light.", "writer", new[] { "S1" });
    ledger.RecordReview("C1", "reviewer", ReviewOutcome.ReviewerSupported,
        new[] { Anchor() }, rationale: "The cited text directly states infrared observation.");
    Check(ledger.EvaluatePublication("C1").Publishable, "reviewed claim not eligible");
    Check(ledger.Summarize().PublicationEligibleClaims == 1, "wrong summary");
});

Test("fabricated quote cannot be used as review evidence", () =>
{
    var ledger = WithClaim();
    Throws<InvalidOperationException>(() => ledger.RecordReview("C1", "reviewer",
        ReviewOutcome.ReviewerSupported,
        new[] { new PassageAnchor("S1", 0, 12, "Incorrect quote") }, rationale: "Attempted"));
});

Test("review passage offset and length are checked", () =>
{
    var ledger = WithClaim();
    Throws<InvalidOperationException>(() => ledger.RecordReview("C1", "reviewer",
        ReviewOutcome.ReviewerSupported,
        new[] { new PassageAnchor("S1", 10000, 1, "x") }, rationale: "Attempted"));
});

Test("reviewer and writer must be separate", () =>
{
    var ledger = WithClaim();
    Throws<ArgumentException>(() => ledger.RecordReview("C1", "WRITER",
        ReviewOutcome.ReviewerSupported, new[] { Anchor() }, rationale: "Attempted"));
});

Test("Wikipedia source cannot satisfy NASA-only research requirement", () =>
{
    var ledger = New();
    var source = Add(ledger, "S1", Wiki, Passage, EvidenceMaterialKind.RetrievedDocument);
    Check(!source.MatchesRequestedDomains, "domain gate incorrectly passed");
    ledger.AddClaim("C1", "JWST observes infrared light.", "writer", new[] { "S1" });
    Throws<InvalidOperationException>(() => ledger.RecordReview("C1", "reviewer",
        ReviewOutcome.ReviewerSupported, new[] { Anchor() }, rationale: "Attempted"));
});

Test("lookalike domain never matches requested domain suffix", () =>
{
    var req = new EvidenceRequirement(new[] { "nasa.gov" });
    Check(!req.IsAllowedDomain("evilnasa.gov") && !req.IsAllowedDomain("nasa.gov.attacker.net") &&
        req.IsAllowedDomain("science.nasa.gov") && req.IsAllowedDomain("nasa.gov"), "suffix matching flawed");
});

Test("qualified assessments must include an explicit limitation", () =>
{
    var ledger = WithClaim();
    Throws<ArgumentException>(() => ledger.RecordReview("C1", "reviewer",
        ReviewOutcome.ReviewerQualified, new[] { Anchor() }, rationale: "Needs qualifier"));
});

Test("qualified text retains caveat instead of silently overstating", () =>
{
    var ledger = WithClaim();
    ledger.RecordReview("C1", "reviewer", ReviewOutcome.ReviewerQualified,
        new[] { Anchor() }, qualification: "Applies to the retrieved context only",
        rationale: "Scope is narrower than the sentence.");
    Check(ledger.EvaluatePublication("C1").ReviewedText!.Contains("Qualification:", StringComparison.Ordinal),
        "caveat lost");
});

Test("unsupported or disputed assessments are never published", () =>
{
    var ledger = WithClaim();
    ledger.RecordReview("C1", "reviewer", ReviewOutcome.Unsupported, Array.Empty<PassageAnchor>());
    Check(!ledger.EvaluatePublication("C1").Publishable, "unsupported claim published");
    var second = WithClaim();
    second.RecordReview("C1", "reviewer", ReviewOutcome.Disputed, Array.Empty<PassageAnchor>());
    Check(!second.EvaluatePublication("C1").Publishable, "disputed claim published");
});

Test("unobserved and repeated source IDs are rejected", () =>
{
    var ledger = New();
    Add(ledger, "S1", NASA, Passage, EvidenceMaterialKind.RetrievedDocument);
    Throws<ArgumentException>(() => ledger.AddClaim("C1", "A claim", "writer", new[] { "S99" }));
    Throws<ArgumentException>(() => ledger.AddClaim("C1", "A claim", "writer", new[] { "S1", "S1" }));
});

Test("unsafe URLs, credentials and IP addresses are refused", () =>
{
    foreach (string url in new[] { "http://science.nasa.gov/", "https://127.0.0.1/data",
        "https://user:password@science.nasa.gov/", "https://internal.local/a" })
        Check(!EvidenceLedger.IsPublicHttpsUrl(url, out _), "unsafe URL accepted: " + url);
    Check(EvidenceLedger.IsPublicHttpsUrl(NASA, out _), "NASA URL rejected");
});

Test("source IDs and review revisions cannot be silently overwritten", () =>
{
    var ledger = WithClaim();
    Throws<InvalidOperationException>(() => Add(ledger, "S1", NASA, Passage, EvidenceMaterialKind.RetrievedDocument));
    ledger.RecordReview("C1", "reviewer", ReviewOutcome.Unsupported, Array.Empty<PassageAnchor>());
    Throws<InvalidOperationException>(() => ledger.RecordReview("C1", "reviewer",
        ReviewOutcome.ReviewerSupported, new[] { Anchor() }, rationale: "Overwrite attempt"));
});

Test("original material hash is deterministic and recorded", () =>
{
    var ledger = New();
    var source = Add(ledger, "S1", NASA, Passage, EvidenceMaterialKind.RetrievedDocument);
    Check(source.BodySha256.Length == 64 && source.BodySha256.All(Uri.IsHexDigit), "SHA-256 absent");
    Check(source.ObservationReference == "run-scoped-tool-result", "observation provenance missing");
});

Console.WriteLine("PASS: " + passed + " evidence-ledger checks completed.");
return 0;

void Test(string description, Action action)
{
    try
    {
        action();
        Console.WriteLine("PASS " + description);
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("FAIL " + description + ": " + ex.Message);
        Environment.Exit(1);
    }
}

static EvidenceLedger New() => new(Guid.NewGuid(), new EvidenceRequirement(new[] { "nasa.gov" }));
static EvidenceSource Add(EvidenceLedger ledger, string id, string url, string body, EvidenceMaterialKind kind) =>
    ledger.AddObservedSource(id, url, "Test publisher", "Test title", body, kind,
        "run-scoped-tool-result", DateTimeOffset.UtcNow);
static EvidenceLedger WithClaim()
{
    var ledger = New();
    const string documentText = "The telescope observes infrared light from distant galaxies.";
    Add(ledger, "S1", "https://science.nasa.gov/mission/webb/", documentText, EvidenceMaterialKind.RetrievedDocument);
    ledger.AddClaim("C1", "JWST observes infrared light.", "writer", new[] { "S1" });
    return ledger;
}
static PassageAnchor Anchor()
{
    const string quote = "The telescope observes infrared light from distant galaxies.";
    return new PassageAnchor("S1", 0, quote.Length, quote);
}
static void Check(bool condition, string detail)
{
    if (!condition) throw new InvalidOperationException(detail);
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + " was not thrown.");
}

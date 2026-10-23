using NIRAAgent.Presentation;

static void Check(bool success, string issue)
{
    if (!success) throw new Exception("VIDEO CHECK FAILED: " + issue);
}

var supported = new[]
{
    ("https://www.youtube.com/watch?v=6Px_b5eSzsA&t=42s", "YouTube", "6Px_b5eSzsA"),
    ("https://youtu.be/6Px_b5eSzsA", "YouTube", "6Px_b5eSzsA"),
    ("https://www.youtube.com/shorts/6Px_b5eSzsA", "YouTube", "6Px_b5eSzsA"),
    ("https://www.youtube.com/live/6Px_b5eSzsA", "YouTube", "6Px_b5eSzsA"),
    ("https://vimeo.com/123456789", "Vimeo", "123456789")
};
foreach (var (url, provider, id) in supported)
{
    Check(NIRAVideoReferencePolicy.TryParse(url, out var result) && result != null,
        "Video not recognized: " + url);
    Check(result!.Provider == provider && result.VideoId == id,
        "Incorrect host, provider, or ID: " + url);
    Check(result.EmbedUrl.StartsWith("https://", StringComparison.Ordinal) &&
          !result.EmbedUrl.Contains("t=42s", StringComparison.Ordinal),
        "Unexpected embed scheme/parameters");
}
string[] unsafeOrUnsupported =
{
    "http://www.youtube.com/watch?v=6Px_b5eSzsA",
    "https://youtube.com.evil.example/watch?v=6Px_b5eSzsA",
    "https://youtube.com@127.0.0.1/watch?v=6Px_b5eSzsA",
    "https://www.youtube.com/watch?v=bad",
    "https://vimeo.com/1234", "https://vimeo.com/watch?v=123456789",
    "file:///C:/private.mp4", "javascript:alert(1)"
};
foreach (string url in unsafeOrUnsupported)
    Check(!NIRAVideoReferencePolicy.TryParse(url, out _), "Unsafe URL: " + url);
foreach (string line in new[]
{
    "**Video:** [Spaceflight demonstration](https://www.youtube.com/watch?v=6Px_b5eSzsA)",
    "- **Video:** [Spaceflight demonstration](https://youtu.be/6Px_b5eSzsA)",
    "**Video:** [[4K] Watch booster landing](https://youtu.be/6Px_b5eSzsA",
    "https://vimeo.com/123456789"
})
    Check(NIRAVideoReferencePolicy.TryParseStandaloneLine(line, out var video, out string title)
          && video != null && !string.IsNullOrEmpty(title),
        "Standalone video link was not promoted: " + line);
// Regression: a plain-text research synthesis can wrap the entire Markdown
// link in emphasis. That must still produce the existing player, without
// promoting an incidental video URL buried in a paragraph.
foreach (string line in new[]
{
    "**[Observed video](https://www.youtube.com/watch?v=6Px_b5eSzsA)**",
    "__[- Watch demonstration](https://youtu.be/6Px_b5eSzsA)__",
    "Video: **[Observed video](https://youtu.be/6Px_b5eSzsA)**",
    "- **Video:** **[Observed video](https://www.youtube.com/watch?v=6Px_b5eSzsA)**",
    "**https://youtu.be/6Px_b5eSzsA**"
})
    Check(NIRAVideoReferencePolicy.TryParseStandaloneLine(line,
              out var emphasized, out string emphasizedTitle) &&
          emphasized != null && emphasized.Provider == "YouTube" &&
          !string.IsNullOrWhiteSpace(emphasizedTitle),
        "Bold standalone video reference was displayed as literal Markdown: " + line);

Check(!NIRAVideoReferencePolicy.TryParseStandaloneLine(
    "A paragraph **[related video](https://youtu.be/6Px_b5eSzsA)** with context", out _, out _),
    "Inline embedded link was incorrectly promoted to a player");
Check(!NIRAVideoReferencePolicy.TryParseStandaloneLine(
    "**[Unsafe](https://youtube.com.evil.test/watch?v=6Px_b5eSzsA)**", out _, out _),
    "Bold link bypassed the video provider allowlist");
Check(!NIRAVideoReferencePolicy.TryParseStandaloneLine(
    "See https://youtu.be/6Px_b5eSzsA in the full article", out _, out _),
    "Inline prose was incorrectly promoted to a player");
Console.WriteLine("PASS: provider URLs, safe embeds, standalone watch links, and invalid inputs.");

/*
 * filename: SegaVisualUnderstandingService.cs
 */

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using SegaAgent.AI.Ollama;
using SegaAgent.PC.Awareness;

namespace SegaAgent.Vision;


// =============================================================
// STAGE 12 VISUAL UNDERSTANDING
//
// A dedicated vision model acts as a perceptual reasoner over one
// already-grounded screenshot. It does not own Sega's decisions,
// goals, permissions, tools or UI actions.
//
// Flow:
//   SegaVisualEvidence -> image+grounding -> vision model
//   -> strict structured observation -> runtime normalization
//   -> normal SegaExecutive cognition as capability evidence.
// =============================================================

public sealed class SegaVisualUnderstandingService
{
    public const string VisionModelEnvironmentVariable =
        "SEGA_OLLAMA_VISION_MODEL";

    private const string DefaultVisionModel =
        "gemma4:31b-cloud";

    private const int MaximumRecentObservations = 32;
    private const int MaximumQuestionCharacters = 1200;

    private readonly SegaVisualEvidenceService _evidence;
    private readonly OllamaClient _ollama;

    private readonly object _sync = new();
    private readonly Dictionary<Guid, SegaVisualObservation> _recent = new();

    private readonly JsonSerializerOptions _jsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };


    public SegaVisualUnderstandingService(
        SegaVisualEvidenceService evidence,
        OllamaClient ollama)
    {
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));

        string? configured =
            Environment.GetEnvironmentVariable(
                VisionModelEnvironmentVariable);

        Debug.WriteLine(
            $"[VisionUnderstand] READY | " +
            $"Model='{VisionModel}' | " +
            $"Default='{DefaultVisionModel}' | " +
            $"Override={(string.IsNullOrWhiteSpace(configured) ? "False" : "True")}");
    }


    public string VisionModel
    {
        get
        {
            string configured =
                Environment.GetEnvironmentVariable(
                    VisionModelEnvironmentVariable)?.Trim()
                ?? string.Empty;

            return string.IsNullOrWhiteSpace(configured)
                ? DefaultVisionModel
                : configured;
        }
    }


    public IReadOnlyList<SegaVisualObservation> RecentObservations
    {
        get
        {
            lock (_sync)
            {
                return _recent.Values
                    .OrderByDescending(value => value.ObservedAtUtc)
                    .Take(MaximumRecentObservations)
                    .ToArray();
            }
        }
    }


    public bool TryGetObservation(
        Guid observationId,
        out SegaVisualObservation? observation)
    {
        lock (_sync)
        {
            return _recent.TryGetValue(observationId, out observation);
        }
    }


    public async Task<SegaVisualObservation> InspectAsync(
        Guid evidenceId,
        string? question,
        CancellationToken cancellationToken = default)
    {
        if (!_evidence.TryGetEvidence(evidenceId, out SegaVisualEvidence? evidence)
            || evidence == null)
        {
            throw new InvalidOperationException(
                "The requested visual evidence is not available in Sega's current transient capture cache. Capture fresh evidence first.");
        }

        string cleanQuestion =
            NormalizeQuestion(question);

        if (!File.Exists(evidence.ImagePath))
        {
            throw new InvalidOperationException(
                "The visual evidence image has expired or is no longer present. Capture fresh evidence first.");
        }

        string currentHash =
            await ComputeSha256Async(
                evidence.ImagePath,
                cancellationToken);

        if (!string.Equals(
                currentHash,
                evidence.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The visual evidence image no longer matches its authoritative capture hash, so it will not be interpreted.");
        }

        SegaVisualObservation? cached;
        lock (_sync)
        {
            cached = _recent.Values
                .Where(value => value.EvidenceId == evidenceId)
                .Where(value => string.Equals(
                    value.Question,
                    cleanQuestion,
                    StringComparison.Ordinal))
                .OrderByDescending(value => value.ObservedAtUtc)
                .FirstOrDefault();
        }

        if (cached != null)
        {
            Debug.WriteLine(
                $"[VisionUnderstand] CACHE HIT | " +
                $"Observation={cached.ObservationId:D} | " +
                $"Evidence={cached.EvidenceId:D}");

            return cached;
        }

        string model = VisionModel;
        string systemPrompt = BuildSystemPrompt();
        string userPrompt = BuildUserPrompt(evidence, cleanQuestion);

        Stopwatch stopwatch = Stopwatch.StartNew();

        FileInfo imageFile =
            new(evidence.ImagePath);

        Debug.WriteLine(
            $"[VisionModel] REQUEST | " +
            $"Model='{model}' | " +
            $"Evidence={evidence.EvidenceId:D} | " +
            $"ImageBytes={imageFile.Length} | " +
            $"QuestionChars={cleanQuestion.Length}");

        string raw;

        try
        {
            raw =
                await _ollama.ChatWithImagesAsync(
                    model,
                    systemPrompt,
                    userPrompt,
                    new[] { evidence.ImagePath },
                    jsonMode: true,
                    cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            Debug.WriteLine(
                $"[VisionModel] CANCELLED | " +
                $"Model='{model}' | " +
                $"Evidence={evidence.EvidenceId:D} | " +
                $"Time={stopwatch.ElapsedMilliseconds} ms");

            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            Debug.WriteLine(
                $"[VisionModel] FAILED | " +
                $"Model='{model}' | " +
                $"Evidence={evidence.EvidenceId:D} | " +
                $"Time={stopwatch.ElapsedMilliseconds} ms | " +
                $"Type={ex.GetType().Name} | " +
                $"Message='{TrimDiagnostic(ex.Message)}'");

            throw;
        }

        stopwatch.Stop();

        VisualModelResult parsed =
            ParseModelResult(raw);

        SegaVisualObservation observation =
            NormalizeObservation(
                evidence,
                model,
                cleanQuestion,
                parsed);

        Remember(observation);

        Debug.WriteLine(
            $"[VisionUnderstand] OBSERVED | " +
            $"Observation={observation.ObservationId:D} | " +
            $"Evidence={observation.EvidenceId:D} | " +
            $"Model='{observation.Model}' | " +
            $"Confidence={observation.Confidence:F2} | " +
            $"Elements={observation.Elements.Count} | " +
            $"Text={observation.VisibleText.Count} | " +
            $"Time={stopwatch.ElapsedMilliseconds} ms | " +
            $"Summary='{TrimLog(observation.Summary)}'");

        return observation;
    }


    private static string BuildSystemPrompt() =>
        """
        You are Sega's dedicated visual perception reasoner.

        Your ONLY job is to inspect the supplied screenshot and return a grounded,
        structured description of what is visibly supported by the pixels.

        You are not Sega's executive. Do not decide what Sega should do, do not call
        tools, do not invent mouse/keyboard actions, do not claim an external action
        succeeded, and do not turn uncertain visual guesses into facts.

        The accompanying process/window metadata is authoritative capture provenance.
        It identifies the PC state that grounded the screenshot, but it does NOT mean
        every visible pixel belongs to that process/window. Describe the image itself.

        VISUAL EVIDENCE RULES:
        - Report text only when it is actually legible. Preserve visible wording as
          faithfully as practical; do not fabricate hidden/off-screen text.
        - Distinguish direct visual evidence from inference.
        - If something is ambiguous, lower confidence and state the limitation.
        - UI element bounds use a normalized 0..1000 coordinate system relative to
          the supplied screenshot: left=0/top=0 is the image's upper-left and
          right=1000/bottom=1000 is the lower-right.
        - Omit bounds when you cannot locate an element reliably.
        - Prefer meaningful UI elements relevant to understanding the current screen;
          do not enumerate every decorative icon/pixel.
        - `issues` contains only visible errors, warnings, failures, blocked states or
          other notable problems supported by the screenshot. It may be empty.

        Return ONLY one JSON object with this exact top-level shape:
        {
          "summary": "concise overall visual summary",
          "applicationContent": "what the visible application/workspace appears to contain",
          "visibleText": ["important legible text"],
          "elements": [
            {
              "kind": "button|input|text|dialog|menu|tab|panel|list|editor|terminal|image|icon|other",
              "label": "visible label or concise identifying description",
              "state": "visible state such as selected/disabled/error/empty or empty string",
              "left": 0,
              "top": 0,
              "right": 1000,
              "bottom": 1000,
              "confidence": 0.0
            }
          ],
          "issues": ["visible issue"],
          "confidence": 0.0,
          "limitations": ["material uncertainty or visibility limitation"]
        }

        Use confidence values from 0.0 to 1.0. No markdown. No commentary outside JSON.
        """;


    private static string BuildUserPrompt(
        SegaVisualEvidence evidence,
        string question)
    {
        StringBuilder text = new();

        text.AppendLine("AUTHORITATIVE CAPTURE PROVENANCE");
        text.AppendLine($"EvidenceId: {evidence.EvidenceId:D}");
        text.AppendLine($"CapturedAtUtc: {evidence.CapturedAtUtc:O}");
        text.AppendLine($"WorldVersion: {evidence.WorldVersion}");
        text.AppendLine($"ImageSha256: {evidence.Sha256}");
        text.AppendLine(
            $"CaptureBounds: left={evidence.CaptureBounds.Left}, top={evidence.CaptureBounds.Top}, " +
            $"width={evidence.CaptureBounds.Width}, height={evidence.CaptureBounds.Height}");
        text.AppendLine($"CaptureTarget: {evidence.Target}");
        text.AppendLine($"ForegroundProcess: {evidence.ForegroundProcessName}");
        text.AppendLine($"ForegroundProcessId: {evidence.ForegroundProcessId}");
        text.AppendLine($"ForegroundWindowTitle: {evidence.ForegroundWindowTitle}");
        text.AppendLine(
            $"ForegroundIdentityStableDuringCapture: {evidence.ForegroundIdentityStableDuringCapture}");
        text.AppendLine();

        if (string.IsNullOrWhiteSpace(question))
        {
            text.AppendLine(
                "Inspect this screenshot generally. Identify the visible application/workspace, " +
                "important readable text, meaningful UI elements and any visible errors/warnings.");
        }
        else
        {
            text.AppendLine("SEGA'S VISUAL QUESTION");
            text.AppendLine(question);
            text.AppendLine();
            text.AppendLine(
                "Answer that visual question through the required structured observation while " +
                "still reporting material visible context and uncertainty.");
        }

        return text.ToString().Trim();
    }


    private VisualModelResult ParseModelResult(string raw)
    {
        string json = ExtractJsonObject(raw);

        VisualModelResult? parsed =
            JsonSerializer.Deserialize<VisualModelResult>(
                json,
                _jsonOptions);

        return parsed
            ?? throw new InvalidOperationException(
                "Vision reasoner returned no parseable structured observation.");
    }


    private SegaVisualObservation NormalizeObservation(
        SegaVisualEvidence evidence,
        string model,
        string question,
        VisualModelResult parsed)
    {
        string summary = Clean(parsed.Summary, 1800);
        string applicationContent = Clean(parsed.ApplicationContent, 2400);

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException(
                "Vision reasoner returned an empty visual summary.");
        }

        string[] visibleText = NormalizeStrings(
            parsed.VisibleText,
            maximumItems: 40,
            maximumCharacters: 500);

        string[] issues = NormalizeStrings(
            parsed.Issues,
            maximumItems: 12,
            maximumCharacters: 700);

        string[] limitations = NormalizeStrings(
            parsed.Limitations,
            maximumItems: 12,
            maximumCharacters: 700);

        List<SegaVisualElementObservation> elements = new();

        foreach (VisualModelElement rawElement in
                 parsed.Elements ?? Array.Empty<VisualModelElement>())
        {
            if (elements.Count >= 24)
            {
                break;
            }

            SegaVisualNormalizedRectangle? normalizedBounds =
                NormalizeBounds(rawElement);

            PcRectangle? screenBounds =
                normalizedBounds == null
                    ? null
                    : ProjectToScreen(
                        normalizedBounds,
                        evidence.CaptureBounds);

            string kind =
                Clean(rawElement.Kind, 48)
                    .ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(kind))
            {
                kind = "other";
            }

            elements.Add(
                new SegaVisualElementObservation
                {
                    Kind = kind,
                    Label = Clean(rawElement.Label, 500),
                    State = Clean(rawElement.State, 300),
                    Confidence = ClampConfidence(rawElement.Confidence),
                    BoundsNormalized = normalizedBounds,
                    ScreenBounds = screenBounds
                });
        }

        return new SegaVisualObservation
        {
            ObservationId = Guid.NewGuid(),
            EvidenceId = evidence.EvidenceId,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Model = model,
            Question = question,
            Summary = summary,
            ApplicationContent = applicationContent,
            VisibleText = visibleText,
            Elements = elements,
            Issues = issues,
            Limitations = limitations,
            Confidence = ClampConfidence(parsed.Confidence),
            WorldVersion = evidence.WorldVersion,
            ImageSha256 = evidence.Sha256,
            CaptureBounds = evidence.CaptureBounds,
            ForegroundProcessName = evidence.ForegroundProcessName,
            ForegroundProcessId = evidence.ForegroundProcessId,
            ForegroundWindowTitle = evidence.ForegroundWindowTitle
        };
    }


    private void Remember(SegaVisualObservation observation)
    {
        lock (_sync)
        {
            _recent[observation.ObservationId] = observation;

            foreach (Guid excessId in _recent.Values
                         .OrderByDescending(value => value.ObservedAtUtc)
                         .Skip(MaximumRecentObservations)
                         .Select(value => value.ObservationId)
                         .ToArray())
            {
                _recent.Remove(excessId);
            }
        }
    }


    private static SegaVisualNormalizedRectangle? NormalizeBounds(
        VisualModelElement element)
    {
        if (!element.Left.HasValue ||
            !element.Top.HasValue ||
            !element.Right.HasValue ||
            !element.Bottom.HasValue)
        {
            return null;
        }

        int left = (int)Math.Round(Math.Clamp(element.Left.Value, 0.0, 1000.0));
        int top = (int)Math.Round(Math.Clamp(element.Top.Value, 0.0, 1000.0));
        int right = (int)Math.Round(Math.Clamp(element.Right.Value, 0.0, 1000.0));
        int bottom = (int)Math.Round(Math.Clamp(element.Bottom.Value, 0.0, 1000.0));

        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new SegaVisualNormalizedRectangle
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
    }


    private static PcRectangle ProjectToScreen(
        SegaVisualNormalizedRectangle bounds,
        PcRectangle capture)
    {
        int left = capture.Left +
            (int)Math.Round(capture.Width * (bounds.Left / 1000.0));
        int top = capture.Top +
            (int)Math.Round(capture.Height * (bounds.Top / 1000.0));
        int right = capture.Left +
            (int)Math.Round(capture.Width * (bounds.Right / 1000.0));
        int bottom = capture.Top +
            (int)Math.Round(capture.Height * (bounds.Bottom / 1000.0));

        left = Math.Clamp(left, capture.Left, capture.Right);
        right = Math.Clamp(right, capture.Left, capture.Right);
        top = Math.Clamp(top, capture.Top, capture.Bottom);
        bottom = Math.Clamp(bottom, capture.Top, capture.Bottom);

        return new PcRectangle(left, top, right, bottom);
    }


    private static string NormalizeQuestion(string? question)
    {
        string clean = question?.Trim() ?? string.Empty;

        if (clean.Length > MaximumQuestionCharacters)
        {
            throw new InvalidOperationException(
                $"Visual inspection question is limited to {MaximumQuestionCharacters} characters.");
        }

        return clean;
    }


    private static string[] NormalizeStrings(
        IReadOnlyList<string>? values,
        int maximumItems,
        int maximumCharacters)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Clean(value, maximumCharacters))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .ToArray();
    }


    private static string Clean(string? value, int maximumCharacters)
    {
        string clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumCharacters
            ? clean
            : clean[..maximumCharacters];
    }


    private static double ClampConfidence(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, 0.0, 1.0)
            : 0.0;


    private static string ExtractJsonObject(string raw)
    {
        string text = raw?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            throw new InvalidOperationException(
                "Vision reasoner returned an empty response.");
        }

        int first = text.IndexOf('{');
        int last = text.LastIndexOf('}');

        if (first < 0 || last <= first)
        {
            throw new InvalidOperationException(
                "Vision reasoner did not return a JSON object.");
        }

        return text[first..(last + 1)];
    }


    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream =
            new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

        using SHA256 sha256 = SHA256.Create();
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }


    private static string TrimDiagnostic(string? value)
    {
        string text =
            (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();

        return text.Length <= 2200
            ? text
            : text[..2200] + "…";
    }


    private static string TrimLog(string value)
    {
        const int maximumLength = 160;
        string clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength
            ? clean
            : clean[..maximumLength] + "...";
    }


    private sealed record VisualModelResult
    {
        public string Summary { get; init; } = string.Empty;
        public string ApplicationContent { get; init; } = string.Empty;
        public IReadOnlyList<string> VisibleText { get; init; } =
            Array.Empty<string>();
        public IReadOnlyList<VisualModelElement> Elements { get; init; } =
            Array.Empty<VisualModelElement>();
        public IReadOnlyList<string> Issues { get; init; } =
            Array.Empty<string>();
        public double Confidence { get; init; }
        public IReadOnlyList<string> Limitations { get; init; } =
            Array.Empty<string>();
    }


    private sealed record VisualModelElement
    {
        public string Kind { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public double? Left { get; init; }
        public double? Top { get; init; }
        public double? Right { get; init; }
        public double? Bottom { get; init; }
        public double Confidence { get; init; }
    }
}

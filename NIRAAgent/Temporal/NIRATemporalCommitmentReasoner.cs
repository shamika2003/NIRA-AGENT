/*
 * filename: NIRATemporalCommitmentReasoner.cs
 */

using System.Text.Json;
using System.Text.Json.Serialization;

using NIRAAgent.AI.Ollama;
using NIRAAgent.Self.Model;

namespace NIRAAgent.Temporal;

// =============================================================
// TEMPORAL RECONCILIATION REASONER
//
// This is not part of the normal happy path. New time-bound
// commitments should receive a schedule during post-experience
// formation. This bounded reasoner repairs older / migrated / missed
// active commitments that contain clear temporal meaning but have no
// authoritative schedule yet.
// =============================================================

public sealed record NIRATemporalInterpretationResult
{
    public bool HasTemporalMeaning { get; init; }

    public string EvidenceQuote { get; init; } = string.Empty;

    public NIRATemporalScheduleProposal? Temporal { get; init; }

    public string Reason { get; init; } = string.Empty;

    public double Confidence { get; init; }
}


public sealed class NIRATemporalCommitmentReasoner
{
    private const string MainReasoningModel =
        "gpt-oss:120b-cloud";

    private readonly OllamaClient _ollama;
    private readonly NIRATemporalContextService _temporal;
    private readonly JsonSerializerOptions _jsonOptions;

    public NIRATemporalCommitmentReasoner(
        OllamaClient ollama,
        NIRATemporalContextService temporal)
    {
        _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
        _temporal = temporal ?? throw new ArgumentNullException(nameof(temporal));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<NIRATemporalInterpretationResult> ReviewAsync(
        NIRACommitmentState commitment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitment);

        if (!commitment.IsActive || commitment.Temporal != null)
        {
            return new NIRATemporalInterpretationResult
            {
                HasTemporalMeaning = false,
                Reason = "Commitment is not an active unscheduled reconciliation candidate.",
                Confidence = 1.0
            };
        }

        string system =
            """
            You are a bounded temporal interpretation reasoner inside NIRA.

            You are reviewing ONE already-authoritative commitment that was
            created by an older or incomplete runtime path without a temporal
            schedule.

            Your only job is to decide whether the stored commitment text itself
            clearly contains time/date/reminder semantics that should produce an
            authoritative wake schedule.

            Do not invent a date, time, recurrence or reminder meaning that is not
            grounded in the supplied commitment summary/evidence.

            Relative expressions such as "tomorrow" are interpreted relative to
            the commitment's CREATED timestamp, not the current review time.

            Return one JSON object only:

            {
              "hasTemporalMeaning": true,
              "evidenceQuote": "short exact quote copied from Summary or LastEvidenceQuote",
              "temporal": {
                "mode": "Exact|FlexibleDay|Window",
                "originalExpression": "exact/natural time expression from evidence",
                "timeZoneId": null,
                "targetLocalDate": null,
                "localTime": null,
                "windowStartLocalTime": null,
                "windowEndLocalTime": null,
                "relativeDelaySeconds": null,
                "relativeDays": null,
                "absoluteUtc": null,
                "recurrenceKind": "None|Daily|Weekly|Monthly|Yearly",
                "recurrenceInterval": 1,
                "recurrenceDaysOfWeek": []
              },
              "reason": "short semantic reason",
              "confidence": 0.0
            }

            If there is no clear temporal meaning, set hasTemporalMeaning=false,
            temporal=null, and explain briefly. For date-only language such as
            "tomorrow", use FlexibleDay and do not invent a clock time. For
            "in N minutes/hours", prefer Exact + relativeDelaySeconds. Use
            recurrence only when the obligation itself clearly repeats.
            """;

        DateTimeOffset createdUtc = commitment.CreatedAt.ToUniversalTime();
        TimeZoneInfo localZone = TimeZoneInfo.Local;
        DateTimeOffset createdLocal = TimeZoneInfo.ConvertTime(createdUtc, localZone);

        string user =
            $"""
            COMMITMENT ID
            {commitment.Id:D}

            SUMMARY
            {commitment.Summary}

            LAST EVIDENCE QUOTE
            {commitment.LastEvidenceQuote ?? "(none)"}

            CREATED UTC
            {createdUtc:O}

            CREATED LOCAL
            {createdLocal:O}

            CREATED LOCAL TIMEZONE
            {localZone.Id}

            CURRENT CLOCK (for review context only; relative language is still
            anchored to CREATED time)
            {_temporal.BuildCognitionContext()}
            """;

        string raw = await _ollama.ChatAsync(
            MainReasoningModel,
            system,
            user,
            cancellationToken);

        string json = ExtractJsonObject(raw);

        NIRATemporalInterpretationResult? parsed =
            JsonSerializer.Deserialize<NIRATemporalInterpretationResult>(
                json,
                _jsonOptions);

        if (parsed == null)
        {
            return new NIRATemporalInterpretationResult
            {
                HasTemporalMeaning = false,
                Reason = "Temporal reconciliation reasoner returned no parseable result."
            };
        }

        return parsed with
        {
            EvidenceQuote = parsed.EvidenceQuote?.Trim() ?? string.Empty,
            Temporal = parsed.Temporal?.Normalize(),
            Reason = parsed.Reason?.Trim() ?? string.Empty,
            Confidence = Math.Clamp(parsed.Confidence, 0.0, 1.0)
        };
    }

    private static string ExtractJsonObject(string raw)
    {
        string value = raw?.Trim() ?? string.Empty;

        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLineBreak = value.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                value = value[(firstLineBreak + 1)..];
            }

            int closing = value.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0)
            {
                value = value[..closing];
            }

            value = value.Trim();
        }

        int start = value.IndexOf('{');
        int end = value.LastIndexOf('}');

        if (start < 0 || end < start)
        {
            throw new InvalidOperationException(
                "Temporal reconciliation reasoner returned no JSON object.");
        }

        return value[start..(end + 1)].Trim();
    }
}


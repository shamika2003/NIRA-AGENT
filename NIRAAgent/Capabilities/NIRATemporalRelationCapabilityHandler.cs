using System.Globalization;
using System.Text.Json;

namespace NIRAAgent.Capabilities;

// General-purpose temporal relation. Neither the capability nor its policy
// knows about lectures, meetings, appointments, markets, or any domain entity.
// The caller provides full dated local instants grounded in prior evidence;
// this runtime alone computes their relationship to the CURRENT OS clock.
public sealed class NIRATemporalRelationCapabilityHandler : INIRACapabilityHandler
{
    public NIRACapabilityDescriptor Descriptor { get; } = new()
    {
        Id = NIRACapabilityIds.TemporalRelate,
        Description = "Compare an evidence-grounded dated start and optional end against the current authoritative clock. Uses complete local ISO dates/times and the specified or OS timezone. Returns Upcoming, Active, Ended, or Started (end unknown); does NOT prove a website or meeting is still open. Use when temporal status materially affects the user's answer.",
        DefaultRisk = NIRACapabilityRisk.Observe,
        Parameters = new[]
        {
            new NIRACapabilityParameterDescriptor { Name = "startLocal", Type = "string", Required = true,
                Description = "Source-grounded date and time yyyy-MM-ddTHH:mm or yyyy-MM-ddTHH:mm:ss. No guessed dates." },
            new NIRACapabilityParameterDescriptor { Name = "endLocal", Type = "string", Required = false,
                Description = "Source-grounded local end date/time. Leave absent when unknown; a past start alone does NOT prove an event ended." },
            new NIRACapabilityParameterDescriptor { Name = "timeZoneId", Type = "string", Required = false,
                Description = "IANA or Windows time-zone ID when source specifies one; otherwise OS local zone." }
        }
    };

    public NIRACapabilityRisk ResolveRisk(NIRACapabilityRequest request) =>
        NIRACapabilityRisk.Observe;

    public Task<NIRACapabilityHandlerResult> ExecuteAsync(
        NIRACapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string startText = NIRACapabilityArguments.RequireString(request, "startLocal", 64);
        string? endText = NIRACapabilityArguments.GetOptionalString(request, "endLocal", 64);
        string? zoneId = NIRACapabilityArguments.GetOptionalString(request, "timeZoneId", 150);
        TimeZoneInfo zone = string.IsNullOrWhiteSpace(zoneId)
            ? TimeZoneInfo.Local
            : TimeZoneInfo.FindSystemTimeZoneById(zoneId);

        DateTimeOffset start = Resolve(startText, zone);
        DateTimeOffset? end = string.IsNullOrWhiteSpace(endText)
            ? null : Resolve(endText, zone);
        if (end.HasValue && end.Value <= start)
            throw new InvalidOperationException(
                "End must be strictly later than start. Provide full calendar dates for overnight/cross-day ranges.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string relation = now < start.ToUniversalTime() ? "Upcoming"
            : end.HasValue && now >= end.Value.ToUniversalTime() ? "Ended"
            : end.HasValue ? "Active" : "StartedEndUnknown";
        var evidence = new
        {
            comparison = "Deterministic clock comparison; source dates must be independently grounded",
            nowUtc = now.ToString("O"),
            nowLocal = TimeZoneInfo.ConvertTime(now, zone).ToString("O"),
            timeZoneId = zone.Id,
            startLocal = start.ToString("O"),
            endLocal = end?.ToString("O"),
            relation,
            liveServiceStatusVerified = false
        };
        return Task.FromResult(new NIRACapabilityHandlerResult
        {
            Summary = $"Temporal relation: {relation} as of {now:O} (scheduled times, not live-service status).",
            Output = JsonSerializer.Serialize(evidence),
            ChangedSystemState = false
        });
    }

    private static DateTimeOffset Resolve(string value, TimeZoneInfo zone)
    {
        string[] formats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime local))
            throw new InvalidOperationException(
                "A full source-grounded local date/time is required (yyyy-MM-ddTHH:mm[:ss]). Do not infer a missing calendar date.");
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new InvalidOperationException(
                "The provided local instant is missing or ambiguous under this timezone; obtain an unambiguous source time.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}


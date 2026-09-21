/*
 * filename: NIRATemporalContracts.cs
 */

using System.Globalization;
using System.Text;

namespace NIRAAgent.Temporal;

// =============================================================
// TEMPORAL COMMITMENT MODEL
//
// Natural-language time interpretation belongs to NIRA's reasoning
// resources. These contracts carry the semantic result into a
// deterministic runtime that validates, resolves, persists and wakes
// the commitment at the correct time.
// =============================================================

public enum NIRATemporalTimingMode
{
    None,
    Exact,
    FlexibleDay,
    Window
}


public enum NIRATemporalRecurrenceKind
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly
}


// =============================================================
// MODEL PROPOSAL
//
// This intentionally uses local semantic pieces instead of forcing
// the LLM to perform timezone arithmetic. The runtime resolves them
// against an authoritative clock and TimeZoneInfo instance.
// =============================================================

public sealed record NIRATemporalScheduleProposal
{
    public NIRATemporalTimingMode Mode { get; init; } =
        NIRATemporalTimingMode.None;

    public string? OriginalExpression { get; init; }

    // null / "local" means the PC's current local timezone.
    public string? TimeZoneId { get; init; }

    // Strict ISO local date: yyyy-MM-dd.
    public string? TargetLocalDate { get; init; }

    // Strict local time: HH:mm or HH:mm:ss.
    public string? LocalTime { get; init; }

    public string? WindowStartLocalTime { get; init; }

    public string? WindowEndLocalTime { get; init; }

    // For language such as "in 20 minutes". This is resolved from
    // the authoritative source event timestamp, not DateTime.Now.
    public long? RelativeDelaySeconds { get; init; }

    // For language such as "tomorrow" / "in three days". The model
    // may use this instead of doing date arithmetic itself.
    public int? RelativeDays { get; init; }

    // Escape hatch for explicitly absolute UTC language.
    public DateTimeOffset? AbsoluteUtc { get; init; }

    public NIRATemporalRecurrenceKind RecurrenceKind { get; init; } =
        NIRATemporalRecurrenceKind.None;

    public int RecurrenceInterval { get; init; } = 1;

    // Optional weekly recurrence selection. When empty, Weekly means
    // the same local weekday as the current anchor occurrence.
    public IReadOnlyList<DayOfWeek> RecurrenceDaysOfWeek { get; init; } =
        Array.Empty<DayOfWeek>();

    public NIRATemporalScheduleProposal Normalize()
    {
        string? expression =
            NormalizeOptional(OriginalExpression, 220, "Temporal expression");

        string? zone =
            NormalizeOptional(TimeZoneId, 160, "Temporal timezone ID");

        string? date =
            NormalizeOptional(TargetLocalDate, 32, "Temporal local date");

        string? localTime =
            NormalizeOptional(LocalTime, 24, "Temporal local time");

        string? windowStart =
            NormalizeOptional(WindowStartLocalTime, 24, "Temporal window start");

        string? windowEnd =
            NormalizeOptional(WindowEndLocalTime, 24, "Temporal window end");

        if (RelativeDelaySeconds.HasValue &&
            (RelativeDelaySeconds.Value < 0 ||
             RelativeDelaySeconds.Value > 315_576_000L))
        {
            throw new InvalidOperationException(
                "Temporal relative delay is outside the supported ten-year bound.");
        }

        if (RelativeDays.HasValue &&
            (RelativeDays.Value < -3660 || RelativeDays.Value > 3660))
        {
            throw new InvalidOperationException(
                "Temporal relative day offset is outside the supported ten-year bound.");
        }

        int recurrenceInterval =
            Math.Clamp(RecurrenceInterval, 1, 366);

        DayOfWeek[] recurrenceDays =
            (RecurrenceDaysOfWeek ?? Array.Empty<DayOfWeek>())
                .Distinct()
                .OrderBy(value => (int)value)
                .Take(7)
                .ToArray();

        return this with
        {
            OriginalExpression = expression,
            TimeZoneId = zone,
            TargetLocalDate = date,
            LocalTime = localTime,
            WindowStartLocalTime = windowStart,
            WindowEndLocalTime = windowEnd,
            RecurrenceInterval = recurrenceInterval,
            RecurrenceDaysOfWeek = recurrenceDays
        };
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string clean = value.Trim();

        if (clean.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"{field} is too long.");
        }

        return clean;
    }
}


// =============================================================
// AUTHORITATIVE STORED SCHEDULE
// =============================================================

public sealed record NIRATemporalScheduleState
{
    public NIRATemporalTimingMode Mode { get; init; } =
        NIRATemporalTimingMode.None;

    public string? OriginalExpression { get; init; }

    public string TimeZoneId { get; init; } =
        string.Empty;

    public string? TargetLocalDate { get; init; }

    public string? LocalTime { get; init; }

    public string? WindowStartLocalTime { get; init; }

    public string? WindowEndLocalTime { get; init; }

    public DateTimeOffset? NotBeforeUtc { get; init; }

    public DateTimeOffset? DueAtUtc { get; init; }

    public DateTimeOffset? WindowEndUtc { get; init; }

    public DateTimeOffset? NextWakeAtUtc { get; init; }

    public DateTimeOffset? LastWakeAtUtc { get; init; }

    // The schedule instant that caused the most recent wake claim. This is
    // separate from LastWakeAtUtc (actual claim time) because NextWakeAtUtc
    // temporarily becomes a lease while cognition is processing.
    public DateTimeOffset? LastWakeScheduledForUtc { get; init; }

    public int WakeCount { get; init; }

    public int Revision { get; init; } = 1;

    public NIRATemporalRecurrenceKind RecurrenceKind { get; init; } =
        NIRATemporalRecurrenceKind.None;

    public int RecurrenceInterval { get; init; } = 1;

    // Preserve the original calendar anchor so monthly/yearly recurrence does
    // not drift after clamping through short months or leap years.
    public int? RecurrenceAnchorDayOfMonth { get; init; }

    public int? RecurrenceAnchorMonth { get; init; }

    public IReadOnlyList<DayOfWeek> RecurrenceDaysOfWeek { get; init; } =
        Array.Empty<DayOfWeek>();

    public bool IsScheduled =>
        Mode != NIRATemporalTimingMode.None &&
        NextWakeAtUtc.HasValue;

    public bool IsRecurring =>
        RecurrenceKind != NIRATemporalRecurrenceKind.None;

    public bool IsDue(DateTimeOffset nowUtc) =>
        IsScheduled &&
        NextWakeAtUtc!.Value <= nowUtc.ToUniversalTime();

    public NIRATemporalScheduleState Normalize()
    {
        if (Mode == NIRATemporalTimingMode.None)
        {
            throw new InvalidOperationException(
                "Stored temporal schedule cannot use None mode.");
        }

        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            throw new InvalidOperationException(
                "Stored temporal schedule requires a timezone ID.");
        }

        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

        DateTimeOffset? notBefore = NotBeforeUtc?.ToUniversalTime();
        DateTimeOffset? due = DueAtUtc?.ToUniversalTime();
        DateTimeOffset? windowEnd = WindowEndUtc?.ToUniversalTime();
        DateTimeOffset? nextWake = NextWakeAtUtc?.ToUniversalTime();
        DateTimeOffset? lastWake = LastWakeAtUtc?.ToUniversalTime();
        DateTimeOffset? lastScheduledFor = LastWakeScheduledForUtc?.ToUniversalTime();

        if (Mode == NIRATemporalTimingMode.Exact && !due.HasValue)
        {
            throw new InvalidOperationException(
                "Exact temporal schedule requires DueAtUtc.");
        }

        if (Mode == NIRATemporalTimingMode.FlexibleDay &&
            string.IsNullOrWhiteSpace(TargetLocalDate))
        {
            throw new InvalidOperationException(
                "Flexible-day temporal schedule requires a target local date.");
        }

        if (Mode == NIRATemporalTimingMode.Window &&
            (!notBefore.HasValue || !windowEnd.HasValue))
        {
            throw new InvalidOperationException(
                "Window temporal schedule requires start/end UTC bounds.");
        }

        if (notBefore.HasValue && windowEnd.HasValue &&
            windowEnd.Value < notBefore.Value)
        {
            throw new InvalidOperationException(
                "Temporal window ends before it starts.");
        }

        DateTimeOffset fallbackWake =
            due ?? notBefore ?? throw new InvalidOperationException(
                "Stored temporal schedule has no wake anchor.");

        DayOfWeek[] recurrenceDays =
            (RecurrenceDaysOfWeek ?? Array.Empty<DayOfWeek>())
                .Distinct()
                .OrderBy(value => (int)value)
                .Take(7)
                .ToArray();

        int? anchorDay = RecurrenceAnchorDayOfMonth;
        int? anchorMonth = RecurrenceAnchorMonth;

        if (IsRecurring && (!anchorDay.HasValue || !anchorMonth.HasValue) &&
            !string.IsNullOrWhiteSpace(TargetLocalDate))
        {
            DateOnly anchorDate = ParseDate(TargetLocalDate);
            anchorDay ??= anchorDate.Day;
            anchorMonth ??= anchorDate.Month;
        }

        if (anchorDay.HasValue && (anchorDay.Value < 1 || anchorDay.Value > 31))
        {
            throw new InvalidOperationException(
                "Temporal recurrence anchor day is outside 1-31.");
        }

        if (anchorMonth.HasValue && (anchorMonth.Value < 1 || anchorMonth.Value > 12))
        {
            throw new InvalidOperationException(
                "Temporal recurrence anchor month is outside 1-12.");
        }

        return this with
        {
            OriginalExpression =
                string.IsNullOrWhiteSpace(OriginalExpression)
                    ? null
                    : OriginalExpression.Trim(),

            TimeZoneId = TimeZoneId.Trim(),

            TargetLocalDate =
                string.IsNullOrWhiteSpace(TargetLocalDate)
                    ? null
                    : TargetLocalDate.Trim(),

            LocalTime =
                string.IsNullOrWhiteSpace(LocalTime)
                    ? null
                    : LocalTime.Trim(),

            WindowStartLocalTime =
                string.IsNullOrWhiteSpace(WindowStartLocalTime)
                    ? null
                    : WindowStartLocalTime.Trim(),

            WindowEndLocalTime =
                string.IsNullOrWhiteSpace(WindowEndLocalTime)
                    ? null
                    : WindowEndLocalTime.Trim(),

            NotBeforeUtc = notBefore,
            DueAtUtc = due,
            WindowEndUtc = windowEnd,
            NextWakeAtUtc = (nextWake ?? fallbackWake).ToUniversalTime(),
            LastWakeAtUtc = lastWake,
            LastWakeScheduledForUtc = lastScheduledFor,
            WakeCount = Math.Max(0, WakeCount),
            Revision = Math.Max(1, Revision),
            RecurrenceInterval = Math.Clamp(RecurrenceInterval, 1, 366),
            RecurrenceAnchorDayOfMonth = anchorDay,
            RecurrenceAnchorMonth = anchorMonth,
            RecurrenceDaysOfWeek = recurrenceDays
        };
    }

    private static DateOnly ParseDate(string value)
    {
        if (!DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly result))
        {
            throw new InvalidOperationException(
                $"Temporal local date '{value}' must use yyyy-MM-dd.");
        }

        return result;
    }
}


// =============================================================
// AUTHORITATIVE CLOCK / RESOLUTION
// =============================================================

public sealed class NIRATemporalContextService
{
    private const int MaximumResolveIterations = 4096;

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public string BuildCognitionContext()
    {
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        TimeZoneInfo zone = TimeZoneInfo.Local;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, zone);

        return $"""
            NIRA AUTHORITATIVE CLOCK
            UTC now: {utc:O}
            Local now: {local:O}
            Local date: {local:yyyy-MM-dd}
            Local day: {local:dddd}
            Local time: {local:HH:mm:ss}
            Local timezone ID: {zone.Id}
            Local UTC offset: {local:zzz}

            Relative words such as today, tomorrow, tonight, later, next week,
            and "in N minutes/hours" must be interpreted relative to this clock
            and the fresh event timestamp. The runtime, not the LLM, owns final
            timezone conversion and wake scheduling.
            """.Trim();
    }

    public NIRATemporalScheduleState ResolveProposal(
        NIRATemporalScheduleProposal rawProposal,
        DateTimeOffset evidenceTimestamp,
        NIRATemporalScheduleState? previous = null)
    {
        ArgumentNullException.ThrowIfNull(rawProposal);

        NIRATemporalScheduleProposal proposal = rawProposal.Normalize();

        if (proposal.Mode == NIRATemporalTimingMode.None)
        {
            throw new InvalidOperationException(
                "Temporal schedule proposal must use Exact, FlexibleDay, or Window.");
        }

        TimeZoneInfo zone = ResolveTimeZone(proposal.TimeZoneId);
        DateTimeOffset evidenceUtc = evidenceTimestamp.ToUniversalTime();
        DateTimeOffset evidenceLocal = TimeZoneInfo.ConvertTime(evidenceUtc, zone);

        string? targetDateText = null;
        DateOnly? targetDate = null;

        if (!string.IsNullOrWhiteSpace(proposal.TargetLocalDate))
        {
            targetDate = ParseDate(proposal.TargetLocalDate);
            targetDateText = targetDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else if (proposal.RelativeDays.HasValue)
        {
            targetDate = DateOnly.FromDateTime(evidenceLocal.DateTime)
                .AddDays(proposal.RelativeDays.Value);
            targetDateText = targetDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        DateTimeOffset? notBeforeUtc = null;
        DateTimeOffset? dueAtUtc = null;
        DateTimeOffset? windowEndUtc = null;
        string? localTimeText = NormalizeTimeText(proposal.LocalTime);
        string? windowStartText = NormalizeTimeText(proposal.WindowStartLocalTime);
        string? windowEndText = NormalizeTimeText(proposal.WindowEndLocalTime);

        switch (proposal.Mode)
        {
            case NIRATemporalTimingMode.Exact:
            {
                if (proposal.RelativeDelaySeconds.HasValue)
                {
                    dueAtUtc = evidenceUtc.AddSeconds(proposal.RelativeDelaySeconds.Value);
                }
                else if (proposal.AbsoluteUtc.HasValue)
                {
                    dueAtUtc = proposal.AbsoluteUtc.Value.ToUniversalTime();
                }
                else
                {
                    TimeOnly localTime =
                        ParseTime(localTimeText ?? throw new InvalidOperationException(
                            "Exact temporal schedule requires localTime, relativeDelaySeconds, or absoluteUtc."));

                    DateOnly resolvedDate;

                    if (targetDate.HasValue)
                    {
                        resolvedDate = targetDate.Value;
                    }
                    else if (proposal.RecurrenceKind == NIRATemporalRecurrenceKind.Weekly &&
                             proposal.RecurrenceDaysOfWeek.Count > 0)
                    {
                        resolvedDate = ResolveInitialWeeklyExactDate(
                            evidenceLocal,
                            localTime,
                            proposal.RecurrenceDaysOfWeek);
                    }
                    else
                    {
                        resolvedDate = DateOnly.FromDateTime(evidenceLocal.DateTime);
                        DateTimeOffset sameDay = ResolveLocal(zone, resolvedDate, localTime);

                        if (sameDay <= evidenceUtc)
                        {
                            resolvedDate = resolvedDate.AddDays(1);
                        }
                    }

                    targetDate = resolvedDate;
                    targetDateText = resolvedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    dueAtUtc = ResolveLocal(zone, resolvedDate, localTime);
                }

                notBeforeUtc = dueAtUtc;
                break;
            }

            case NIRATemporalTimingMode.FlexibleDay:
            {
                if (!targetDate.HasValue &&
                    proposal.RecurrenceKind == NIRATemporalRecurrenceKind.Weekly &&
                    proposal.RecurrenceDaysOfWeek.Count > 0)
                {
                    targetDate = ResolveInitialWeeklyFlexibleDate(
                        evidenceLocal,
                        proposal.RecurrenceDaysOfWeek);
                    targetDateText = targetDate.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture);
                }

                if (!targetDate.HasValue)
                {
                    throw new InvalidOperationException(
                        "Flexible-day temporal schedule requires targetLocalDate or relativeDays.");
                }

                notBeforeUtc = ResolveLocal(zone, targetDate.Value, TimeOnly.MinValue);
                windowEndUtc = ResolveLocal(zone, targetDate.Value.AddDays(1), TimeOnly.MinValue)
                    .AddTicks(-1);
                break;
            }

            case NIRATemporalTimingMode.Window:
            {
                TimeOnly start =
                    ParseTime(windowStartText ?? throw new InvalidOperationException(
                        "Window temporal schedule requires windowStartLocalTime."));

                TimeOnly end =
                    ParseTime(windowEndText ?? throw new InvalidOperationException(
                        "Window temporal schedule requires windowEndLocalTime."));

                if (!targetDate.HasValue &&
                    proposal.RecurrenceKind == NIRATemporalRecurrenceKind.Weekly &&
                    proposal.RecurrenceDaysOfWeek.Count > 0)
                {
                    targetDate = ResolveInitialWeeklyWindowDate(
                        evidenceLocal,
                        start,
                        end,
                        proposal.RecurrenceDaysOfWeek);
                    targetDateText = targetDate.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture);
                }

                if (!targetDate.HasValue)
                {
                    throw new InvalidOperationException(
                        "Window temporal schedule requires targetLocalDate or relativeDays.");
                }

                notBeforeUtc = ResolveLocal(zone, targetDate.Value, start);

                DateOnly endDate = targetDate.Value;
                if (end <= start)
                {
                    endDate = endDate.AddDays(1);
                }

                windowEndUtc = ResolveLocal(zone, endDate, end);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(proposal.Mode));
        }

        // Recurring exact schedules created from an absolute instant or a
        // relative delay still need a stable local calendar/time anchor for
        // later occurrences. Derive it once from the first resolved instant
        // instead of letting later recurrences drift to midnight.
        if (proposal.RecurrenceKind != NIRATemporalRecurrenceKind.None &&
            proposal.Mode == NIRATemporalTimingMode.Exact &&
            dueAtUtc.HasValue)
        {
            DateTimeOffset localOccurrence =
                TimeZoneInfo.ConvertTime(dueAtUtc.Value, zone);

            targetDate ??= DateOnly.FromDateTime(localOccurrence.DateTime);
            targetDateText ??= targetDate.Value.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

            localTimeText ??= TimeOnly.FromDateTime(localOccurrence.DateTime)
                .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        DateTimeOffset initialWake =
            dueAtUtc ?? notBeforeUtc ?? evidenceUtc;

        // A schedule may legitimately resolve into the past when NIRA was
        // offline. Wake immediately instead of silently losing the obligation.
        if (initialWake < DateTimeOffset.UtcNow)
        {
            initialWake = DateTimeOffset.UtcNow;
        }

        DateOnly? recurrenceAnchorDate =
            proposal.RecurrenceKind == NIRATemporalRecurrenceKind.None
                ? null
                : targetDate ?? ResolveDateFromUtc(dueAtUtc ?? notBeforeUtc ?? initialWake, zone);

        NIRATemporalScheduleState resolved =
            new()
            {
                Mode = proposal.Mode,
                OriginalExpression = proposal.OriginalExpression,
                TimeZoneId = zone.Id,
                TargetLocalDate = targetDateText,
                LocalTime = localTimeText,
                WindowStartLocalTime = windowStartText,
                WindowEndLocalTime = windowEndText,
                NotBeforeUtc = notBeforeUtc,
                DueAtUtc = dueAtUtc,
                WindowEndUtc = windowEndUtc,
                NextWakeAtUtc = initialWake,
                LastWakeAtUtc = null,
                LastWakeScheduledForUtc = null,
                WakeCount = 0,
                Revision = previous == null ? 1 : previous.Revision + 1,
                RecurrenceKind = proposal.RecurrenceKind,
                RecurrenceInterval = proposal.RecurrenceInterval,
                RecurrenceAnchorDayOfMonth = recurrenceAnchorDate?.Day,
                RecurrenceAnchorMonth = recurrenceAnchorDate?.Month,
                RecurrenceDaysOfWeek = proposal.RecurrenceDaysOfWeek
            };

        resolved = resolved.Normalize();

        // A recurring window created after today's window has already ended
        // should begin at the next occurrence instead of immediately emitting
        // an already-missed same-day wake.
        if (resolved.IsRecurring &&
            resolved.Mode == NIRATemporalTimingMode.Window &&
            resolved.WindowEndUtc.HasValue &&
            resolved.WindowEndUtc.Value < evidenceUtc)
        {
            resolved = AdvanceRecurringSchedule(resolved, evidenceUtc);
        }

        return resolved;
    }

    public NIRATemporalScheduleState ClaimWake(
        NIRATemporalScheduleState schedule,
        DateTimeOffset nowUtc,
        TimeSpan lease)
    {
        NIRATemporalScheduleState normalized = schedule.Normalize();
        DateTimeOffset now = nowUtc.ToUniversalTime();

        if (!normalized.IsDue(now))
        {
            return normalized;
        }

        DateTimeOffset scheduledFor =
            normalized.NextWakeAtUtc!.Value;

        return normalized with
        {
            LastWakeAtUtc = now,
            LastWakeScheduledForUtc = scheduledFor,
            NextWakeAtUtc = now + lease,
            WakeCount = normalized.WakeCount + 1
        };
    }

    public NIRATemporalScheduleState RearmAfterWake(
        NIRATemporalScheduleState schedule,
        DateTimeOffset nowUtc)
    {
        NIRATemporalScheduleState normalized = schedule.Normalize();
        DateTimeOffset now = nowUtc.ToUniversalTime();

        if (normalized.IsRecurring)
        {
            return AdvanceRecurringSchedule(normalized, now);
        }

        TimeSpan retry = normalized.Mode switch
        {
            NIRATemporalTimingMode.Exact => TimeSpan.FromMinutes(10),
            NIRATemporalTimingMode.Window => TimeSpan.FromMinutes(15),
            NIRATemporalTimingMode.FlexibleDay => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromMinutes(20)
        };

        return normalized with
        {
            NextWakeAtUtc = now + retry
        };
    }

    public NIRATemporalScheduleState AdvanceRecurringSchedule(
        NIRATemporalScheduleState schedule,
        DateTimeOffset afterUtc)
    {
        NIRATemporalScheduleState current = schedule.Normalize();

        if (!current.IsRecurring)
        {
            return current;
        }

        TimeZoneInfo zone = ResolveTimeZone(current.TimeZoneId);
        DateTimeOffset after = afterUtc.ToUniversalTime();
        DateOnly anchorDate = ResolveAnchorDate(current, zone);
        TimeOnly anchorTime = ResolveAnchorTime(current);

        for (int iteration = 0; iteration < MaximumResolveIterations; iteration++)
        {
            DateOnly nextDate = ResolveNextRecurringDate(current, anchorDate);

            DateTimeOffset nextStart = current.Mode switch
            {
                NIRATemporalTimingMode.Exact =>
                    ResolveLocal(zone, nextDate, anchorTime),

                NIRATemporalTimingMode.FlexibleDay =>
                    ResolveLocal(zone, nextDate, TimeOnly.MinValue),

                NIRATemporalTimingMode.Window =>
                    ResolveLocal(
                        zone,
                        nextDate,
                        ParseTime(current.WindowStartLocalTime ?? "00:00")),

                _ => throw new InvalidOperationException(
                    "Recurring schedule has unsupported timing mode.")
            };

            anchorDate = nextDate;

            if (nextStart <= after)
            {
                continue;
            }

            DateTimeOffset? due = null;
            DateTimeOffset? notBefore = null;
            DateTimeOffset? windowEnd = null;

            switch (current.Mode)
            {
                case NIRATemporalTimingMode.Exact:
                    due = nextStart;
                    notBefore = nextStart;
                    break;

                case NIRATemporalTimingMode.FlexibleDay:
                    notBefore = nextStart;
                    windowEnd = ResolveLocal(zone, nextDate.AddDays(1), TimeOnly.MinValue)
                        .AddTicks(-1);
                    break;

                case NIRATemporalTimingMode.Window:
                {
                    notBefore = nextStart;
                    TimeOnly start = ParseTime(current.WindowStartLocalTime ?? "00:00");
                    TimeOnly end = ParseTime(current.WindowEndLocalTime ?? "23:59:59");
                    DateOnly endDate = end <= start ? nextDate.AddDays(1) : nextDate;
                    windowEnd = ResolveLocal(zone, endDate, end);
                    break;
                }
            }

            return current with
            {
                TargetLocalDate = nextDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                NotBeforeUtc = notBefore,
                DueAtUtc = due,
                WindowEndUtc = windowEnd,
                NextWakeAtUtc = nextStart
            };
        }

        throw new InvalidOperationException(
            "Could not resolve the next recurring temporal commitment occurrence within bounds.");
    }

    public string DescribeForCognition(NIRATemporalScheduleState? schedule)
    {
        if (schedule == null)
        {
            return "unscheduled";
        }

        NIRATemporalScheduleState value = schedule.Normalize();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeZoneInfo zone = ResolveTimeZone(value.TimeZoneId);
        DateTimeOffset? localWake = value.NextWakeAtUtc.HasValue
            ? TimeZoneInfo.ConvertTime(value.NextWakeAtUtc.Value, zone)
            : null;

        StringBuilder builder = new();
        builder.Append($"mode={value.Mode}");

        if (!string.IsNullOrWhiteSpace(value.OriginalExpression))
        {
            builder.Append($" | expression='{value.OriginalExpression}'");
        }

        builder.Append($" | timezone={value.TimeZoneId}");

        if (!string.IsNullOrWhiteSpace(value.TargetLocalDate))
        {
            builder.Append($" | localDate={value.TargetLocalDate}");
        }

        if (!string.IsNullOrWhiteSpace(value.LocalTime))
        {
            builder.Append($" | localTime={value.LocalTime}");
        }

        if (!string.IsNullOrWhiteSpace(value.WindowStartLocalTime) ||
            !string.IsNullOrWhiteSpace(value.WindowEndLocalTime))
        {
            builder.Append(
                $" | localWindow={value.WindowStartLocalTime ?? "?"}-{value.WindowEndLocalTime ?? "?"}");
        }

        if (localWake.HasValue)
        {
            builder.Append($" | nextWakeLocal={localWake.Value:O}");
            builder.Append($" | dueNow={value.IsDue(now)}");
        }

        if (value.LastWakeScheduledForUtc.HasValue)
        {
            DateTimeOffset localScheduled =
                TimeZoneInfo.ConvertTime(value.LastWakeScheduledForUtc.Value, zone);
            builder.Append($" | lastWakeScheduledForLocal={localScheduled:O}");
        }

        if (value.LastWakeAtUtc.HasValue)
        {
            DateTimeOffset localClaimed =
                TimeZoneInfo.ConvertTime(value.LastWakeAtUtc.Value, zone);
            builder.Append($" | lastWakeClaimedLocal={localClaimed:O}");
        }

        if (value.IsRecurring)
        {
            builder.Append($" | recurrence={value.RecurrenceKind}/{value.RecurrenceInterval}");
            if (value.RecurrenceDaysOfWeek.Count > 0)
            {
                builder.Append($" | days={string.Join(',', value.RecurrenceDaysOfWeek)}");
            }
        }

        builder.Append($" | wakeCount={value.WakeCount} | revision={value.Revision}");
        return builder.ToString();
    }

    public static string DescribeForUi(
        NIRATemporalScheduleState? schedule,
        DateTimeOffset? nowUtc = null)
    {
        if (schedule == null)
        {
            return string.Empty;
        }

        NIRATemporalScheduleState value = schedule.Normalize();
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(value.TimeZoneId);
        DateTimeOffset now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, zone);
        DateTimeOffset? localWake = value.NextWakeAtUtc.HasValue
            ? TimeZoneInfo.ConvertTime(value.NextWakeAtUtc.Value, zone)
            : null;

        string recurrence = value.RecurrenceKind switch
        {
            NIRATemporalRecurrenceKind.Daily => value.RecurrenceInterval == 1 ? "EVERY DAY · " : $"EVERY {value.RecurrenceInterval} DAYS · ",
            NIRATemporalRecurrenceKind.Weekly => value.RecurrenceInterval == 1 ? "EVERY WEEK · " : $"EVERY {value.RecurrenceInterval} WEEKS · ",
            NIRATemporalRecurrenceKind.Monthly => value.RecurrenceInterval == 1 ? "EVERY MONTH · " : $"EVERY {value.RecurrenceInterval} MONTHS · ",
            NIRATemporalRecurrenceKind.Yearly => value.RecurrenceInterval == 1 ? "EVERY YEAR · " : $"EVERY {value.RecurrenceInterval} YEARS · ",
            _ => string.Empty
        };

        string text = value.Mode switch
        {
            NIRATemporalTimingMode.Exact when value.DueAtUtc.HasValue =>
                TimeZoneInfo.ConvertTime(value.DueAtUtc.Value, zone)
                    .ToString("ddd, MMM d · h:mm tt", CultureInfo.InvariantCulture),

            NIRATemporalTimingMode.FlexibleDay when !string.IsNullOrWhiteSpace(value.TargetLocalDate) =>
                FormatFlexibleDate(value.TargetLocalDate!, localNow),

            NIRATemporalTimingMode.Window when !string.IsNullOrWhiteSpace(value.TargetLocalDate) =>
                $"{FormatFlexibleDate(value.TargetLocalDate!, localNow)} · {FormatTime(value.WindowStartLocalTime)}–{FormatTime(value.WindowEndLocalTime)}",

            _ when localWake.HasValue =>
                localWake.Value.ToString("ddd, MMM d · h:mm tt", CultureInfo.InvariantCulture),

            _ => "SCHEDULED"
        };

        if (value.NextWakeAtUtc.HasValue && value.NextWakeAtUtc.Value < now)
        {
            text += " · OVERDUE";
        }

        return recurrence + text.ToUpperInvariant();
    }

    private static string FormatFlexibleDate(string dateText, DateTimeOffset localNow)
    {
        DateOnly date = ParseDate(dateText);
        DateOnly today = DateOnly.FromDateTime(localNow.DateTime);

        if (date == today)
        {
            return "TODAY · FLEXIBLE";
        }

        if (date == today.AddDays(1))
        {
            return "TOMORROW · FLEXIBLE";
        }

        return date.ToString("ddd, MMM d", CultureInfo.InvariantCulture) + " · FLEXIBLE";
    }

    private static string FormatTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        TimeOnly value = ParseTime(text);
        return DateTime.Today.Add(value.ToTimeSpan())
            .ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    private static DateOnly ResolveAnchorDate(
        NIRATemporalScheduleState schedule,
        TimeZoneInfo zone)
    {
        if (!string.IsNullOrWhiteSpace(schedule.TargetLocalDate))
        {
            return ParseDate(schedule.TargetLocalDate);
        }

        DateTimeOffset anchor =
            schedule.DueAtUtc ??
            schedule.NotBeforeUtc ??
            schedule.NextWakeAtUtc ??
            DateTimeOffset.UtcNow;

        DateTimeOffset local = TimeZoneInfo.ConvertTime(anchor, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static TimeOnly ResolveAnchorTime(NIRATemporalScheduleState schedule)
    {
        if (!string.IsNullOrWhiteSpace(schedule.LocalTime))
        {
            return ParseTime(schedule.LocalTime);
        }

        if (!string.IsNullOrWhiteSpace(schedule.WindowStartLocalTime))
        {
            return ParseTime(schedule.WindowStartLocalTime);
        }

        return TimeOnly.MinValue;
    }

    private static DateOnly ResolveNextRecurringDate(
        NIRATemporalScheduleState schedule,
        DateOnly anchorDate)
    {
        int interval = Math.Clamp(schedule.RecurrenceInterval, 1, 366);

        return schedule.RecurrenceKind switch
        {
            NIRATemporalRecurrenceKind.Daily =>
                anchorDate.AddDays(interval),

            NIRATemporalRecurrenceKind.Weekly =>
                ResolveNextWeeklyDate(schedule, anchorDate, interval),

            NIRATemporalRecurrenceKind.Monthly =>
                AddMonthsClamped(
                    anchorDate,
                    interval,
                    schedule.RecurrenceAnchorDayOfMonth ?? anchorDate.Day),

            NIRATemporalRecurrenceKind.Yearly =>
                AddYearsClamped(
                    anchorDate,
                    interval,
                    schedule.RecurrenceAnchorMonth ?? anchorDate.Month,
                    schedule.RecurrenceAnchorDayOfMonth ?? anchorDate.Day),

            _ => throw new InvalidOperationException(
                "Recurring schedule requires a recurrence kind.")
        };
    }

    private static DateOnly ResolveInitialWeeklyExactDate(
        DateTimeOffset evidenceLocal,
        TimeOnly localTime,
        IReadOnlyList<DayOfWeek> selectedDays)
    {
        DateOnly today = DateOnly.FromDateTime(evidenceLocal.DateTime);
        TimeOnly nowTime = TimeOnly.FromDateTime(evidenceLocal.DateTime);
        HashSet<DayOfWeek> selected = selectedDays.ToHashSet();

        for (int delta = 0; delta <= 7; delta++)
        {
            DateOnly candidate = today.AddDays(delta);
            if (!selected.Contains(candidate.DayOfWeek))
            {
                continue;
            }

            if (delta == 0 && localTime <= nowTime)
            {
                continue;
            }

            return candidate;
        }

        throw new InvalidOperationException(
            "Could not resolve the first weekly exact temporal occurrence.");
    }


    private static DateOnly ResolveInitialWeeklyFlexibleDate(
        DateTimeOffset evidenceLocal,
        IReadOnlyList<DayOfWeek> selectedDays)
    {
        DateOnly today = DateOnly.FromDateTime(evidenceLocal.DateTime);
        HashSet<DayOfWeek> selected = selectedDays.ToHashSet();

        // A date-only recurring reminder accepted on an already-selected day
        // should not immediately echo the same obligation back to the user.
        // Start at the next selected calendar day unless the model supplied an
        // explicit targetLocalDate such as today.
        for (int delta = 1; delta <= 7; delta++)
        {
            DateOnly candidate = today.AddDays(delta);
            if (selected.Contains(candidate.DayOfWeek))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Could not resolve the first weekly flexible temporal occurrence.");
    }


    private static DateOnly ResolveInitialWeeklyWindowDate(
        DateTimeOffset evidenceLocal,
        TimeOnly windowStart,
        TimeOnly windowEnd,
        IReadOnlyList<DayOfWeek> selectedDays)
    {
        DateOnly today = DateOnly.FromDateTime(evidenceLocal.DateTime);
        TimeOnly nowTime = TimeOnly.FromDateTime(evidenceLocal.DateTime);
        HashSet<DayOfWeek> selected = selectedDays.ToHashSet();

        for (int delta = 0; delta <= 7; delta++)
        {
            DateOnly candidate = today.AddDays(delta);
            if (!selected.Contains(candidate.DayOfWeek))
            {
                continue;
            }

            if (delta == 0 &&
                windowEnd > windowStart &&
                nowTime >= windowEnd)
            {
                continue;
            }

            return candidate;
        }

        throw new InvalidOperationException(
            "Could not resolve the first weekly window temporal occurrence.");
    }


    private static DateOnly ResolveNextWeeklyDate(
        NIRATemporalScheduleState schedule,
        DateOnly anchorDate,
        int interval)
    {
        if (schedule.RecurrenceDaysOfWeek.Count == 0)
        {
            return anchorDate.AddDays(7 * interval);
        }

        // Multiple selected weekdays intentionally represent every selected
        // weekday in each active week. Interval > 1 is only applied after
        // completing the current selected weekday set.
        DayOfWeek current = anchorDate.DayOfWeek;
        DayOfWeek[] selected = schedule.RecurrenceDaysOfWeek
            .Distinct()
            .OrderBy(value => ((int)value + 7 - (int)current) % 7)
            .ToArray();

        foreach (DayOfWeek day in selected)
        {
            int delta = ((int)day - (int)current + 7) % 7;
            if (delta > 0)
            {
                return anchorDate.AddDays(delta);
            }
        }

        DayOfWeek first = schedule.RecurrenceDaysOfWeek
            .OrderBy(value => (int)value)
            .First();

        int toNextWeekFirst = ((int)first - (int)current + 7) % 7;
        if (toNextWeekFirst == 0)
        {
            toNextWeekFirst = 7;
        }

        toNextWeekFirst += 7 * (interval - 1);
        return anchorDate.AddDays(toNextWeekFirst);
    }

    private static DateOnly AddMonthsClamped(
        DateOnly value,
        int months,
        int anchorDayOfMonth)
    {
        DateTime first = new(value.Year, value.Month, 1);
        DateTime target = first.AddMonths(months);
        int day = Math.Min(
            Math.Clamp(anchorDayOfMonth, 1, 31),
            DateTime.DaysInMonth(target.Year, target.Month));
        return new DateOnly(target.Year, target.Month, day);
    }

    private static DateOnly AddYearsClamped(
        DateOnly value,
        int years,
        int anchorMonth,
        int anchorDayOfMonth)
    {
        int year = checked(value.Year + years);
        int month = Math.Clamp(anchorMonth, 1, 12);
        int day = Math.Min(
            Math.Clamp(anchorDayOfMonth, 1, 31),
            DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }

    private static DateOnly ResolveDateFromUtc(
        DateTimeOffset utc,
        TimeZoneInfo zone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(utc.ToUniversalTime(), zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.Equals(id.Trim(), "local", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException(
                $"Unknown temporal timezone ID '{id}'.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"Invalid temporal timezone ID '{id}'.");
        }
    }

    private static DateOnly ParseDate(string value)
    {
        if (!DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly result))
        {
            throw new InvalidOperationException(
                $"Temporal local date '{value}' must use yyyy-MM-dd.");
        }

        return result;
    }

    private static string? NormalizeTimeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        TimeOnly parsed = ParseTime(value);
        return parsed.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static TimeOnly ParseTime(string value)
    {
        string clean = value.Trim();

        string[] formats =
        {
            "HH:mm",
            "HH:mm:ss"
        };

        if (!TimeOnly.TryParseExact(
                clean,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly result))
        {
            throw new InvalidOperationException(
                $"Temporal local time '{value}' must use HH:mm or HH:mm:ss.");
        }

        return result;
    }

    private static DateTimeOffset ResolveLocal(
        TimeZoneInfo zone,
        DateOnly date,
        TimeOnly time)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);

        // Spring-forward gaps should not permanently strand a recurring
        // obligation. Move to the first real local minute after the gap.
        if (zone.IsInvalidTime(local))
        {
            DateTime adjusted = local;

            for (int minute = 0; minute < 180 && zone.IsInvalidTime(adjusted); minute++)
            {
                adjusted = adjusted.AddMinutes(1);
            }

            if (zone.IsInvalidTime(adjusted))
            {
                throw new InvalidOperationException(
                    $"Temporal local time {local:yyyy-MM-dd HH:mm:ss} could not be resolved in timezone '{zone.Id}'.");
            }

            local = adjusted;
        }

        // During a fall-back overlap, choose the first occurrence of the
        // wall-clock time deterministically so NIRA does not remind twice.
        if (zone.IsAmbiguousTime(local))
        {
            TimeSpan offset = zone
                .GetAmbiguousTimeOffsets(local)
                .Max();

            return new DateTimeOffset(local, offset)
                .ToUniversalTime();
        }

        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}


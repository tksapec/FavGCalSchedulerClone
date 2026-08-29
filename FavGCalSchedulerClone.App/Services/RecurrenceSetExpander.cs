using System.Globalization;
using FavGCalSchedulerClone.App.Models;
using Ical.Net.DataTypes;
using IcalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace FavGCalSchedulerClone.App.Services;

internal static class RecurrenceSetExpander
{
    public static IReadOnlyList<DateTimeOffset> ExpandOccurrences(
        CalendarEvent masterEvent,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var lines = RecurrenceRuleHelper.ParseLines(masterEvent.RecurrenceJson);
        var inclusionRules = lines.Where(line => line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var exclusionRules = lines.Where(line => line.StartsWith("EXRULE:", StringComparison.OrdinalIgnoreCase)).ToArray();
        var rDates = ParseDateLines(lines, "RDATE", masterEvent);
        var exDates = ParseDateLines(lines, "EXDATE", masterEvent);

        if (inclusionRules.Length == 0 && rDates.Count == 0)
        {
            throw new InvalidDataException("Recurring event does not contain RRULE or RDATE data.");
        }

        var included = new HashSet<DateTimeOffset>();
        foreach (var ruleLine in inclusionRules)
        {
            foreach (var occurrence in ExpandRule(masterEvent, ruleLine, rangeStart, rangeEnd))
            {
                included.Add(occurrence);
            }
        }

        if (inclusionRules.Length == 0 && masterEvent.Start >= rangeStart && masterEvent.Start < rangeEnd)
        {
            included.Add(masterEvent.Start);
        }

        foreach (var rDate in rDates.Where(value => value >= rangeStart && value < rangeEnd))
        {
            included.Add(rDate);
        }

        var excluded = new HashSet<DateTimeOffset>(exDates);
        foreach (var ruleLine in exclusionRules)
        {
            foreach (var occurrence in ExpandRule(masterEvent, ruleLine, rangeStart, rangeEnd))
            {
                excluded.Add(occurrence);
            }
        }

        return included
            .Where(value => !excluded.Contains(value))
            .OrderBy(value => value)
            .ToArray();
    }

    private static IEnumerable<DateTimeOffset> ExpandRule(
        CalendarEvent masterEvent,
        string ruleLine,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var separatorIndex = ruleLine.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex >= ruleLine.Length - 1)
        {
            throw new InvalidDataException("Recurrence rule is malformed.");
        }

        var recurrenceEvent = new IcalCalendarEvent
        {
            DtStart = ToCalDateTime(masterEvent.Start, masterEvent.IsAllDay, masterEvent.StartTimeZoneId),
            RecurrenceRule = new RecurrenceRule(ruleLine[(separatorIndex + 1)..])
        };
        var evaluationStart = ToEvaluationBoundary(rangeStart, masterEvent.IsAllDay);
        var evaluationEnd = ToEvaluationBoundary(rangeEnd, masterEvent.IsAllDay);

        foreach (var occurrence in recurrenceEvent
                     .GetOccurrences(evaluationStart)
                     .TakeWhile(item => item.Period.StartTime < evaluationEnd))
        {
            var candidate = FromCalDateTime(occurrence.Period.StartTime, masterEvent.Start.Offset);
            if (candidate >= rangeStart && candidate < rangeEnd)
            {
                yield return candidate;
            }
        }
    }

    private static IReadOnlySet<DateTimeOffset> ParseDateLines(
        IReadOnlyList<string> lines,
        string propertyName,
        CalendarEvent masterEvent)
    {
        var results = new HashSet<DateTimeOffset>();
        foreach (var line in lines)
        {
            if (!line.StartsWith(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
            {
                throw new InvalidDataException($"{propertyName} is malformed.");
            }

            var header = line[..separatorIndex];
            var timeZoneId = TryGetPropertyParameter(header, "TZID") ?? masterEvent.StartTimeZoneId;
            foreach (var token in line[(separatorIndex + 1)..]
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseRecurrenceDate(token, masterEvent.Start.Offset, timeZoneId, out var recurrenceDate))
                {
                    throw new InvalidDataException($"{propertyName} contains an invalid date: {token}");
                }

                results.Add(recurrenceDate);
            }
        }

        return results;
    }

    private static string? TryGetPropertyParameter(string header, string key)
    {
        return header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Where(parts => string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1])
            .FirstOrDefault();
    }

    private static bool TryParseRecurrenceDate(
        string value,
        TimeSpan defaultOffset,
        string? timeZoneId,
        out DateTimeOffset dateTime)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId)
            && DateTime.TryParseExact(
                value,
                "yyyyMMdd'T'HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localDateTime))
        {
            dateTime = FromCalDateTime(
                new CalDateTime(DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), timeZoneId, hasTime: true),
                defaultOffset);
            return true;
        }

        if (DateTimeOffset.TryParseExact(
            value,
            ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out dateTime))
        {
            if (value.Length == 8)
            {
                dateTime = new DateTimeOffset(dateTime.Date, defaultOffset);
            }

            return true;
        }

        dateTime = default;
        return false;
    }

    private static CalDateTime ToCalDateTime(DateTimeOffset value, bool isAllDay, string? timeZoneId)
    {
        if (isAllDay)
        {
            return new CalDateTime(DateOnly.FromDateTime(value.Date));
        }

        var wallClock = DateTime.SpecifyKind(value.DateTime, DateTimeKind.Unspecified);
        return string.IsNullOrWhiteSpace(timeZoneId)
            ? new CalDateTime(wallClock, hasTime: true)
            : new CalDateTime(wallClock, timeZoneId, hasTime: true);
    }

    private static CalDateTime ToEvaluationBoundary(DateTimeOffset value, bool isAllDay)
    {
        return isAllDay
            ? new CalDateTime(DateOnly.FromDateTime(value.Date))
            : new CalDateTime(value.UtcDateTime, CalDateTime.UtcTzId, hasTime: true);
    }

    private static DateTimeOffset FromCalDateTime(CalDateTime value, TimeSpan fallbackOffset)
    {
        var wallClock = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        if (value.IsFloating || !value.HasTime)
        {
            return new DateTimeOffset(wallClock, fallbackOffset);
        }

        var utcUnspecified = DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Unspecified);
        var offset = wallClock - utcUnspecified;
        return new DateTimeOffset(wallClock, offset);
    }
}

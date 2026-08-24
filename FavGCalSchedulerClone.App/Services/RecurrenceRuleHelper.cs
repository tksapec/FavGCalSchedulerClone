using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using Ical.Net.DataTypes;
using IcalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace FavGCalSchedulerClone.App.Services;

internal enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}

internal sealed class RecurrenceRuleDefinition
{
    public RecurrenceFrequency Frequency { get; init; }
    public int Interval { get; set; } = 1;
    public int? Count { get; set; }
    public DateTimeOffset? Until { get; set; }
    public List<DayOfWeek> ByDays { get; } = [];
    public List<int> ByMonthDays { get; } = [];
}

internal static class RecurrenceRuleHelper
{
    public static IReadOnlyList<string> ParseLines(string? recurrenceJson)
    {
        if (string.IsNullOrWhiteSpace(recurrenceJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(recurrenceJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string? SerializeLines(IEnumerable<string> lines)
    {
        var values = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return values.Length == 0 ? null : JsonSerializer.Serialize(values);
    }

    public static RecurrenceRuleDefinition? ParsePrimaryRule(CalendarEvent calendarEvent)
    {
        var ruleLine = ParseLines(calendarEvent.RecurrenceJson)
            .FirstOrDefault(line => line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
        return ruleLine is null ? null : ParseRule(ruleLine);
    }

    public static IReadOnlySet<DateTimeOffset> ParseExDates(CalendarEvent calendarEvent)
    {
        var results = new HashSet<DateTimeOffset>();
        foreach (var line in ParseLines(calendarEvent.RecurrenceJson))
        {
            if (!line.StartsWith("EXDATE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
            {
                continue;
            }

            var header = line[..separatorIndex];
            var timeZoneId = TryGetPropertyParameter(header, "TZID");
            foreach (var token in line[(separatorIndex + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseRecurrenceDate(token, calendarEvent.Start.Offset, timeZoneId, out var exDate))
                {
                    results.Add(exDate);
                }
            }
        }

        return results;
    }

    public static RecurrenceRuleDefinition ParseRule(string line)
    {
        var content = line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase) ? line[6..] : line;
        var values = content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim().ToUpperInvariant(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

        var frequency = values.TryGetValue("FREQ", out var freq)
            ? freq.ToUpperInvariant() switch
            {
                "DAILY" => RecurrenceFrequency.Daily,
                "WEEKLY" => RecurrenceFrequency.Weekly,
                "MONTHLY" => RecurrenceFrequency.Monthly,
                "YEARLY" => RecurrenceFrequency.Yearly,
                _ => throw new InvalidOperationException($"Unsupported recurrence frequency: {freq}")
            }
            : throw new InvalidOperationException("RRULE missing FREQ.");

        var rule = new RecurrenceRuleDefinition
        {
            Frequency = frequency,
            Interval = values.TryGetValue("INTERVAL", out var intervalText) && int.TryParse(intervalText, out var interval) && interval > 0 ? interval : 1
        };

        if (values.TryGetValue("COUNT", out var countText) && int.TryParse(countText, out var count) && count > 0)
        {
            rule.Count = count;
        }

        if (values.TryGetValue("UNTIL", out var untilText) && TryParseRecurrenceDate(untilText, TimeSpan.Zero, out var until))
        {
            rule.Until = until;
        }

        if (values.TryGetValue("BYDAY", out var byDayText))
        {
            foreach (var token in byDayText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseByDay(token, out var dayOfWeek))
                {
                    rule.ByDays.Add(dayOfWeek);
                }
            }
        }

        if (values.TryGetValue("BYMONTHDAY", out var byMonthDayText))
        {
            foreach (var token in byMonthDayText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) && day != 0)
                {
                    rule.ByMonthDays.Add(day);
                }
            }
        }

        return rule;
    }

    public static string FormatRule(RecurrenceRuleDefinition rule)
    {
        var parts = new List<string>
        {
            $"FREQ={rule.Frequency.ToString().ToUpperInvariant()}",
            rule.Interval > 1 ? $"INTERVAL={rule.Interval}" : ""
        };

        if (rule.ByDays.Count > 0)
        {
            parts.Add($"BYDAY={string.Join(",", rule.ByDays.Select(FormatByDay))}");
        }

        if (rule.ByMonthDays.Count > 0)
        {
            parts.Add($"BYMONTHDAY={string.Join(",", rule.ByMonthDays)}");
        }

        if (rule.Count is > 0)
        {
            parts.Add($"COUNT={rule.Count.Value}");
        }

        if (rule.Until is { } until)
        {
            parts.Add($"UNTIL={until.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
        }

        return $"RRULE:{string.Join(";", parts.Where(part => !string.IsNullOrWhiteSpace(part)))}";
    }

    public static IEnumerable<DateTimeOffset> ExpandOccurrences(CalendarEvent masterEvent, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var ruleLine = ParseLines(masterEvent.RecurrenceJson)
            .FirstOrDefault(line => line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
        if (ruleLine is null)
        {
            yield break;
        }

        var recurrenceEvent = new IcalCalendarEvent
        {
            DtStart = ToCalDateTime(masterEvent.Start, masterEvent.IsAllDay, masterEvent.StartTimeZoneId),
            RecurrenceRule = new RecurrenceRule(ruleLine[6..])
        };
        var excluded = ParseExDates(masterEvent);
        var evaluationStart = ToEvaluationBoundary(rangeStart, masterEvent.IsAllDay);
        var evaluationEnd = ToEvaluationBoundary(rangeEnd, masterEvent.IsAllDay);

        foreach (var occurrence in recurrenceEvent
                     .GetOccurrences(evaluationStart)
                     .TakeWhile(item => item.Period.StartTime < evaluationEnd))
        {
            var candidate = FromCalDateTime(occurrence.Period.StartTime, masterEvent.Start.Offset);
            if (candidate < rangeStart || candidate >= rangeEnd || excluded.Contains(candidate))
            {
                continue;
            }

            yield return candidate;
        }
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

    public static IEnumerable<DateTimeOffset> ExpandOccurrences(
        DateTimeOffset seriesStart,
        bool isAllDay,
        RecurrenceRuleDefinition rule,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var totalOccurrences = 0;
        foreach (var occurrence in GenerateOccurrences(seriesStart, isAllDay, rule, rangeEnd))
        {
            totalOccurrences++;
            if (rule.Count is int count && totalOccurrences > count)
            {
                yield break;
            }

            if (occurrence >= rangeStart && occurrence < rangeEnd)
            {
                yield return occurrence;
            }
        }
    }

    private static IEnumerable<DateTimeOffset> GenerateOccurrences(
        DateTimeOffset seriesStart,
        bool isAllDay,
        RecurrenceRuleDefinition rule,
        DateTimeOffset rangeEnd)
    {
        return rule.Frequency switch
        {
            RecurrenceFrequency.Daily => ExpandDaily(seriesStart, isAllDay, rule, seriesStart, rangeEnd),
            RecurrenceFrequency.Weekly => ExpandWeekly(seriesStart, isAllDay, rule, seriesStart, rangeEnd),
            RecurrenceFrequency.Monthly => ExpandMonthly(seriesStart, isAllDay, rule, seriesStart, rangeEnd),
            RecurrenceFrequency.Yearly => ExpandYearly(seriesStart, isAllDay, rule, seriesStart, rangeEnd),
            _ => []
        };
    }

    public static string? BuildSplitSourceRecurrenceJson(CalendarEvent masterEvent, DateTimeOffset occurrenceStart)
    {
        var lines = ParseLines(masterEvent.RecurrenceJson).ToList();
        var ruleIndex = lines.FindIndex(line => line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
        if (ruleIndex < 0)
        {
            return masterEvent.RecurrenceJson;
        }

        var ruleLine = lines[ruleIndex];
        var count = GetPositiveIntRulePart(ruleLine, "COUNT");
        if (count is > 0)
        {
            var beforeCount = CountOccurrencesBefore(masterEvent, occurrenceStart);
            ruleLine = SetRulePart(ruleLine, "COUNT", Math.Max(1, beforeCount).ToString(CultureInfo.InvariantCulture));
            ruleLine = RemoveRulePart(ruleLine, "UNTIL");
        }
        else
        {
            var untilValue = masterEvent.IsAllDay
                ? occurrenceStart.Date.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : occurrenceStart.AddTicks(-1).UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            ruleLine = SetRulePart(ruleLine, "UNTIL", untilValue);
        }

        lines[ruleIndex] = ruleLine;
        return SerializeLines(lines);
    }

    public static string? BuildSplitFutureRecurrenceJson(CalendarEvent masterEvent, DateTimeOffset occurrenceStart)
    {
        var lines = ParseLines(masterEvent.RecurrenceJson).ToList();
        var ruleIndex = lines.FindIndex(line => line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
        if (ruleIndex < 0)
        {
            return masterEvent.RecurrenceJson;
        }

        var ruleLine = lines[ruleIndex];
        var count = GetPositiveIntRulePart(ruleLine, "COUNT");
        if (count is > 0)
        {
            var beforeCount = CountOccurrencesBefore(masterEvent, occurrenceStart);
            var remaining = Math.Max(1, count.Value - beforeCount);
            ruleLine = SetRulePart(ruleLine, "COUNT", remaining.ToString(CultureInfo.InvariantCulture));
        }

        lines[ruleIndex] = ruleLine;
        return SerializeLines(lines);
    }

    private static int? GetPositiveIntRulePart(string ruleLine, string key)
    {
        var value = GetRuleParts(ruleLine)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase));
        return value is { Length: 2 }
               && int.TryParse(value[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
               && parsed > 0
            ? parsed
            : null;
    }

    private static string SetRulePart(string ruleLine, string key, string value)
    {
        var parts = GetRuleParts(ruleLine).ToList();
        var replacement = $"{key}={value}";
        var index = parts.FindIndex(part => part.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            parts[index] = replacement;
        }
        else
        {
            parts.Add(replacement);
        }

        return $"RRULE:{string.Join(";", parts)}";
    }

    private static string RemoveRulePart(string ruleLine, string key)
    {
        var parts = GetRuleParts(ruleLine)
            .Where(part => !part.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));
        return $"RRULE:{string.Join(";", parts)}";
    }

    private static IEnumerable<string> GetRuleParts(string ruleLine)
    {
        var content = ruleLine.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase) ? ruleLine[6..] : ruleLine;
        return content.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    public static string? AddExDate(string? recurrenceJson, DateTimeOffset originalStart, bool isAllDay)
    {
        var lines = ParseLines(recurrenceJson).ToList();
        var exDateValue = isAllDay
            ? originalStart.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : originalStart.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("EXDATE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (lines[i].Contains(exDateValue, StringComparison.OrdinalIgnoreCase))
            {
                return SerializeLines(lines);
            }

            lines[i] = $"{lines[i]},{exDateValue}";
            return SerializeLines(lines);
        }

        lines.Add(isAllDay ? $"EXDATE;VALUE=DATE:{exDateValue}" : $"EXDATE:{exDateValue}");
        return SerializeLines(lines);
    }

    private static int CountOccurrencesBefore(CalendarEvent masterEvent, DateTimeOffset occurrenceStart)
    {
        if (occurrenceStart <= masterEvent.Start)
        {
            return 0;
        }

        var ruleLine = ParseLines(masterEvent.RecurrenceJson)
            .FirstOrDefault(line => line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
        if (ruleLine is null)
        {
            return 0;
        }

        var recurrenceEvent = new IcalCalendarEvent
        {
            DtStart = ToCalDateTime(masterEvent.Start, masterEvent.IsAllDay, masterEvent.StartTimeZoneId),
            RecurrenceRule = new RecurrenceRule(ruleLine[6..])
        };

        // COUNT applies to the RRULE-generated set before EXDATE exclusions are removed.
        // Splitting a finite series therefore has to count excluded occurrences too.
        return recurrenceEvent
            .GetOccurrences()
            .Select(item => FromCalDateTime(item.Period.StartTime, masterEvent.Start.Offset))
            .TakeWhile(candidate => candidate < occurrenceStart)
            .Count();
    }

    private static IEnumerable<DateTimeOffset> ExpandDaily(
        DateTimeOffset seriesStart,
        bool isAllDay,
        RecurrenceRuleDefinition rule,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var current = seriesStart;
        while (current < rangeEnd)
        {
            if (MatchesUntil(current, rule) && current >= rangeStart)
            {
                yield return current;
            }

            current = current.AddDays(rule.Interval);
        }
    }

    private static IEnumerable<DateTimeOffset> ExpandWeekly(
        DateTimeOffset seriesStart,
        bool isAllDay,
        RecurrenceRuleDefinition rule,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var days = rule.ByDays.Count > 0 ? rule.ByDays : [seriesStart.DayOfWeek];
        var weekStart = seriesStart.Date.AddDays(-(int)seriesStart.DayOfWeek);
        var weekIndex = 0;

        while (true)
        {
            var currentWeekStart = weekStart.AddDays(weekIndex * 7 * rule.Interval);
            if (currentWeekStart >= rangeEnd.Date.AddDays(7))
            {
                yield break;
            }

            foreach (var day in days.OrderBy(value => (int)value))
            {
                var candidateDate = currentWeekStart.AddDays((int)day);
                var candidate = new DateTimeOffset(candidateDate + seriesStart.TimeOfDay, seriesStart.Offset);
                if (candidate < seriesStart)
                {
                    continue;
                }

                if (!MatchesUntil(candidate, rule))
                {
                    continue;
                }

                if (candidate >= rangeEnd)
                {
                    yield break;
                }

                if (candidate >= rangeStart)
                {
                    yield return candidate;
                }
            }

            weekIndex++;
        }
    }

    private static IEnumerable<DateTimeOffset> ExpandMonthly(
        DateTimeOffset seriesStart,
        bool isAllDay,
        RecurrenceRuleDefinition rule,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var current = new DateTime(seriesStart.Year, seriesStart.Month, 1);
        var targetDays = rule.ByMonthDays.Count > 0 ? rule.ByMonthDays : [seriesStart.Day];
        var monthIndex = 0;

        while (true)
        {
            var month = current.AddMonths(monthIndex * rule.Interval);
            if (month >= rangeEnd.Date.AddMonths(1))
            {
                yield break;
            }

            foreach (var targetDay in targetDays)
            {
                var day = Math.Min(DateTime.DaysInMonth(month.Year, month.Month), Math.Abs(targetDay));
                var candidateDate = new DateTime(month.Year, month.Month, day);
                var candidate = new DateTimeOffset(candidateDate + seriesStart.TimeOfDay, seriesStart.Offset);
                if (candidate < seriesStart)
                {
                    continue;
                }

                if (!MatchesUntil(candidate, rule))
                {
                    continue;
                }

                if (candidate >= rangeEnd)
                {
                    yield break;
                }

                if (candidate >= rangeStart)
                {
                    yield return candidate;
                }
            }

            monthIndex++;
        }
    }

    private static IEnumerable<DateTimeOffset> ExpandYearly(
        DateTimeOffset seriesStart,
        bool isAllDay,
        RecurrenceRuleDefinition rule,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd)
    {
        var yearIndex = 0;
        while (true)
        {
            var year = seriesStart.Year + (yearIndex * rule.Interval);
            var candidateDate = new DateTime(year, seriesStart.Month, Math.Min(seriesStart.Day, DateTime.DaysInMonth(year, seriesStart.Month)));
            var candidate = new DateTimeOffset(candidateDate + seriesStart.TimeOfDay, seriesStart.Offset);
            if (candidate >= rangeEnd)
            {
                yield break;
            }

            if (candidate >= seriesStart && candidate >= rangeStart && MatchesUntil(candidate, rule))
            {
                yield return candidate;
            }

            yearIndex++;
        }
    }

    private static bool MatchesUntil(DateTimeOffset candidate, RecurrenceRuleDefinition rule)
    {
        return rule.Until is null || candidate <= rule.Until.Value;
    }

    private static bool TryParseByDay(string token, out DayOfWeek dayOfWeek)
    {
        token = token.Trim().ToUpperInvariant();
        if (token.Length >= 2)
        {
            token = token[^2..];
        }

        dayOfWeek = token switch
        {
            "SU" => DayOfWeek.Sunday,
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            _ => default
        };
        return token is "SU" or "MO" or "TU" or "WE" or "TH" or "FR" or "SA";
    }

    private static string FormatByDay(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "SU",
            DayOfWeek.Monday => "MO",
            DayOfWeek.Tuesday => "TU",
            DayOfWeek.Wednesday => "WE",
            DayOfWeek.Thursday => "TH",
            DayOfWeek.Friday => "FR",
            DayOfWeek.Saturday => "SA",
            _ => "MO"
        };
    }

    private static bool TryParseRecurrenceDate(string value, TimeSpan defaultOffset, out DateTimeOffset dateTime)
    {
        return TryParseRecurrenceDate(value, defaultOffset, timeZoneId: null, out dateTime);
    }

    private static bool TryParseRecurrenceDate(string value, TimeSpan defaultOffset, string? timeZoneId, out DateTimeOffset dateTime)
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
}

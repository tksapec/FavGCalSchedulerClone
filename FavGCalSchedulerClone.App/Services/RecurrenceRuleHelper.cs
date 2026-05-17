using System.Globalization;
using FavGCalSchedulerClone.App.Models;
using System.Text.Json;

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

            foreach (var token in line[(separatorIndex + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryParseRecurrenceDate(token, calendarEvent.Start.Offset, out var exDate))
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
        var rule = ParsePrimaryRule(masterEvent);
        if (rule is null)
        {
            yield break;
        }

        var excluded = ParseExDates(masterEvent);
        foreach (var occurrence in ExpandOccurrences(masterEvent.Start, masterEvent.IsAllDay, rule, rangeStart, rangeEnd))
        {
            if (excluded.Contains(occurrence))
            {
                continue;
            }

            yield return occurrence;
        }
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

        var rule = ParseRule(lines[ruleIndex]);
        if (rule.Count is > 0)
        {
            var beforeCount = CountOccurrencesBefore(masterEvent, occurrenceStart);
            rule.Count = beforeCount == 0 ? 1 : beforeCount;
            rule.Until = null;
        }
        else
        {
            rule.Until = masterEvent.IsAllDay
                ? new DateTimeOffset(occurrenceStart.Date.AddDays(-1), occurrenceStart.Offset)
                : occurrenceStart.AddTicks(-1);
        }

        lines[ruleIndex] = FormatRule(rule);
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

        var rule = ParseRule(lines[ruleIndex]);
        if (rule.Count is > 0)
        {
            var beforeCount = CountOccurrencesBefore(masterEvent, occurrenceStart);
            var totalCount = rule.Count.Value;
            var remaining = Math.Max(1, totalCount - beforeCount);
            rule.Count = remaining;
        }

        lines[ruleIndex] = FormatRule(rule);
        return SerializeLines(lines);
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
        var rule = ParsePrimaryRule(masterEvent);
        if (rule is null)
        {
            return 0;
        }

        var searchEnd = occurrenceStart.AddSeconds(-1);
        if (searchEnd < masterEvent.Start)
        {
            return 0;
        }

        return ExpandOccurrences(masterEvent.Start, masterEvent.IsAllDay, rule, masterEvent.Start, occurrenceStart)
            .Count(item => item < occurrenceStart);
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

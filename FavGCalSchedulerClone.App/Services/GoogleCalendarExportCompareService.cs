using System.IO.Compression;
using System.Text;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public sealed class GoogleCalendarExportCompareService
{
    public async Task<GoogleCalendarExportData> LoadFromZipAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Google calendar export zip was not found.", zipPath);
        }

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries
            .Where(item => item.FullName.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (entries.Length == 0)
        {
            throw new InvalidDataException("The zip does not contain any .ics files.");
        }

        var calendars = new List<GoogleCalendarExportData>(entries.Length);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            calendars.Add(ParseIcs(entry.FullName, content));
        }

        var calendarName = string.Join(", ", calendars
            .Select(item => item.CalendarName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal));
        return new GoogleCalendarExportData(
            calendarName,
            calendars.SelectMany(item => item.Events).ToArray());
    }

    public GoogleCalendarComparisonSummary Compare(IEnumerable<CalendarEvent> localEvents, IEnumerable<GoogleExportEvent> exportedEvents)
    {
        var locals = localEvents.Where(item => !item.IsDeleted).ToArray();
        var exports = exportedEvents.ToArray();

        var matchedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        var matchedExportIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var localEvent in locals)
        {
            var export = FindMatchingExport(localEvent, exports, matchedExportIds);
            if (export is null)
            {
                continue;
            }

            matchedLocalIds.Add(localEvent.Id);
            matchedExportIds.Add(export.Uid ?? export.CompositeKey);
        }

        return new GoogleCalendarComparisonSummary(
            LocalCount: locals.Length,
            ExportCount: exports.Length,
            MatchedCount: matchedLocalIds.Count,
            LocalOnlyCount: locals.Length - matchedLocalIds.Count,
            ExportOnlyCount: exports.Length - matchedExportIds.Count);
    }

    private static GoogleCalendarExportData ParseIcs(string fileName, string content)
    {
        var unfolded = UnfoldLines(content);
        var calendarName = unfolded
            .FirstOrDefault(line => line.StartsWith("X-WR-CALNAME:", StringComparison.OrdinalIgnoreCase))
            ?.Split(':', 2)[1]
            ?.Trim() ?? Path.GetFileNameWithoutExtension(fileName);

        var events = new List<GoogleExportEvent>();
        Dictionary<string, string>? current = null;
        foreach (var line in unfolded)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null)
                {
                    events.Add(ToExportEvent(current));
                }

                current = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];
            current[key] = value;
        }

        return new GoogleCalendarExportData(calendarName, events);
    }

    private static GoogleExportEvent ToExportEvent(IReadOnlyDictionary<string, string> values)
    {
        var start = ParseIcsDate(values, "DTSTART");
        var end = ParseIcsDate(values, "DTEND");
        var location = values.FirstOrDefault(item => item.Key.StartsWith("LOCATION", StringComparison.OrdinalIgnoreCase)).Value;
        var description = values.FirstOrDefault(item => item.Key.StartsWith("DESCRIPTION", StringComparison.OrdinalIgnoreCase)).Value;
        var summary = values.FirstOrDefault(item => item.Key.StartsWith("SUMMARY", StringComparison.OrdinalIgnoreCase)).Value ?? "";
        var uid = values.FirstOrDefault(item => item.Key.StartsWith("UID", StringComparison.OrdinalIgnoreCase)).Value;
        var isAllDay = values.Keys.Any(key => key.StartsWith("DTSTART;VALUE=DATE", StringComparison.OrdinalIgnoreCase));

        return new GoogleExportEvent(uid, summary, description, location, start, end, isAllDay);
    }

    private static DateTimeOffset ParseIcsDate(IReadOnlyDictionary<string, string> values, string prefix)
    {
        var item = values.FirstOrDefault(entry => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var value = item.Value ?? "";
        if (item.Key.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.ParseExact(value, "yyyyMMdd", null).Date;
        }

        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return DateTimeOffset.ParseExact(value, "yyyyMMdd'T'HHmmss'Z'", null).ToLocalTime();
        }

        var wallClock = DateTime.ParseExact(
            value,
            "yyyyMMdd'T'HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None);
        var timeZoneId = TryGetIcsParameter(item.Key, "TZID");
        if (!string.IsNullOrWhiteSpace(timeZoneId)
            && GoogleCalendarTimeZone.TryCreateDateTimeOffset(wallClock, timeZoneId, preferredOffset: null, out var zonedValue))
        {
            return zonedValue;
        }

        return DateTimeOffset.ParseExact(
            value,
            "yyyyMMdd'T'HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal);
    }

    private static string? TryGetIcsParameter(string key, string parameterName)
    {
        foreach (var part in key.Split(';').Skip(1))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0
                || !string.Equals(part[..separator], parameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = part[(separator + 1)..].Trim();
            return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
                ? value[1..^1]
                : value;
        }

        return null;
    }

    private static string[] UnfoldLines(string content)
    {
        var rawLines = content.Replace("\r\n", "\n").Split('\n');
        var lines = new List<string>();
        foreach (var rawLine in rawLines)
        {
            if ((rawLine.StartsWith(' ') || rawLine.StartsWith('\t')) && lines.Count > 0)
            {
                lines[^1] += rawLine.TrimStart();
            }
            else
            {
                lines.Add(rawLine.TrimEnd('\r'));
            }
        }

        return lines.ToArray();
    }

    private static GoogleExportEvent? FindMatchingExport(
        CalendarEvent localEvent,
        IEnumerable<GoogleExportEvent> exports,
        IReadOnlySet<string> matchedExportIds)
    {
        if (!string.IsNullOrWhiteSpace(localEvent.GoogleEventId))
        {
            var byUid = exports.FirstOrDefault(item =>
                !matchedExportIds.Contains(item.Uid ?? item.CompositeKey) &&
                string.Equals(item.Uid, localEvent.GoogleEventId, StringComparison.OrdinalIgnoreCase));
            if (byUid is not null)
            {
                return byUid;
            }
        }

        return exports.FirstOrDefault(item =>
            !matchedExportIds.Contains(item.Uid ?? item.CompositeKey) &&
            string.Equals(item.Summary, localEvent.Title, StringComparison.Ordinal) &&
            item.Start == localEvent.Start &&
            item.End == localEvent.End &&
            string.Equals(item.Location ?? "", localEvent.Location ?? "", StringComparison.Ordinal));
    }
}

public sealed record GoogleCalendarExportData(string CalendarName, IReadOnlyList<GoogleExportEvent> Events);

public sealed record GoogleExportEvent(
    string? Uid,
    string Summary,
    string? Description,
    string? Location,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay)
{
    public string CompositeKey => $"{Summary}|{Start:O}|{End:O}|{Location}";
}

public sealed record GoogleCalendarComparisonSummary(
    int LocalCount,
    int ExportCount,
    int MatchedCount,
    int LocalOnlyCount,
    int ExportOnlyCount);

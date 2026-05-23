using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public sealed class FavGCalSchedulerImportService
{
    private const byte NormalEventRecordKind = 0x01;
    private const byte TodoEventRecordKind = 0x06;
    private readonly CalendarRepository _repository;
    private readonly GoogleCalendarExportCompareService _compareService = new();

    public FavGCalSchedulerImportService(CalendarRepository repository)
    {
        _repository = repository;
    }

    public async Task<FavGCalImportAnalysis> AnalyzeAsync(string sourceFolder, CancellationToken cancellationToken = default)
    {
        var calendars = DiscoverCalendars(sourceFolder);
        var warnings = new List<string>();
        var totalEvents = 0;
        var parseErrors = 0;
        var unrestoredTodos = 0;

        foreach (var calendar in calendars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var parsed = await ParseFavCalAsync(calendar, cancellationToken);
                calendar.EventCount = parsed.Count(item => !item.MissingTodoMetadata);
                calendar.UnrestoredTodoCount = parsed.Count(item => item.MissingTodoMetadata);
                totalEvents += calendar.EventCount;
                unrestoredTodos += calendar.UnrestoredTodoCount;
                if (calendar.UnrestoredTodoCount > 0)
                {
                    warnings.Add($"{Path.GetFileName(calendar.SourcePath)}: ToDo {calendar.UnrestoredTodoCount} 件は優先度/進捗情報を復元できないため取り込み対象外です。");
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                parseErrors++;
                warnings.Add($"{calendar.SourcePath}: {ex.Message}");
            }
        }

        return new FavGCalImportAnalysis(sourceFolder, calendars, totalEvents, unrestoredTodos, parseErrors, warnings);
    }

    public async Task<FavGCalImportResult> ImportAsync(FavGCalImportOptions options, CancellationToken cancellationToken = default)
    {
        var analysis = await AnalyzeAsync(options.SourceFolder, cancellationToken);
        var imported = 0;
        var linked = 0;
        var skipped = 0;
        var parseErrors = analysis.ParseErrorCount;
        var warnings = new List<string>(analysis.Warnings);
        var normalizedImportedEvents = new List<CalendarEvent>();

        foreach (var sourceCalendar in analysis.Calendars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<FavGCalParsedEvent> parsedEvents;
            try
            {
                parsedEvents = await ParseFavCalAsync(sourceCalendar, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                parseErrors++;
                warnings.Add($"{sourceCalendar.SourcePath}: {ex.Message}");
                continue;
            }

            var targetCalendarId = ResolveTargetCalendarId(sourceCalendar, options);
            foreach (var parsedEvent in parsedEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (parsedEvent.MissingTodoMetadata)
                {
                    continue;
                }

                var calendarEvent = parsedEvent.Event;
                calendarEvent.CalendarId = targetCalendarId;
                calendarEvent.IsDirty = options.MarkImportedEventsDirty;
                calendarEvent.LastSyncedAt = null;
                normalizedImportedEvents.Add(CloneForComparison(calendarEvent));

                if (!string.IsNullOrWhiteSpace(calendarEvent.GoogleEventId))
                {
                    var existingGoogleEvent = await _repository.FindEventByGoogleEventIdAsync(targetCalendarId, calendarEvent.GoogleEventId);
                    if (existingGoogleEvent is not null)
                    {
                        linked++;
                        continue;
                    }
                }

                if (options.SkipDuplicates && await _repository.FindDuplicateEventAsync(calendarEvent) is not null)
                {
                    skipped++;
                    continue;
                }

                await _repository.SaveEventAsync(calendarEvent);
                imported++;
            }
        }

        GoogleCalendarComparisonSummary? comparisonSummary = null;
        if (!string.IsNullOrWhiteSpace(options.ComparisonZipPath))
        {
            var exportData = await _compareService.LoadFromZipAsync(options.ComparisonZipPath, cancellationToken);
            comparisonSummary = _compareService.Compare(normalizedImportedEvents, exportData.Events);
        }

        return new FavGCalImportResult(imported, linked, skipped, analysis.UnrestoredTodoCount, parseErrors, warnings, comparisonSummary);
    }

    public static string? ExtractCalendarIdFromFeedUrl(string? feedUrl)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return null;
        }

        var match = Regex.Match(feedUrl, @"/feeds/(?<id>[^/]+)/", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.UrlDecode(match.Groups["id"].Value) : null;
    }

    private static IReadOnlyList<FavGCalSourceCalendar> DiscoverCalendars(string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException($"FavGCalScheduler folder was not found: {sourceFolder}");
        }

        var scheduleIni = Path.Combine(sourceFolder, "schedule.ini");
        var paths = File.Exists(scheduleIni)
            ? ReadCalendarPathsFromScheduleIni(scheduleIni, sourceFolder)
            : Directory.EnumerateFiles(sourceFolder, "*.favcal").ToArray();

        return paths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateSourceCalendar)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadCalendarPathsFromScheduleIni(string scheduleIni, string sourceFolder)
    {
        var values = new List<string>();
        foreach (var line in File.ReadLines(scheduleIni, Encoding.Default))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("item", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var value = trimmed[(separator + 1)..].Trim();
            var fullPath = Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(sourceFolder, value));
            values.Add(fullPath);
        }

        return values;
    }

    private static FavGCalSourceCalendar CreateSourceCalendar(string sourcePath)
    {
        var header = ReadHeader(sourcePath);
        var feedUrl = header.FirstOrDefault(value => value.Contains("google.com/calendar/feeds/", StringComparison.OrdinalIgnoreCase));
        var calendarId = ExtractCalendarIdFromFeedUrl(feedUrl);
        var name = header.FirstOrDefault(value => !value.Contains("google.com/", StringComparison.OrdinalIgnoreCase)
                                                 && !value.Contains('@')
                                                 && !value.StartsWith("FavSchedule", StringComparison.OrdinalIgnoreCase)
                                                 && value.Length is > 0 and <= 80)
                   ?? Path.GetFileNameWithoutExtension(sourcePath);
        return new FavGCalSourceCalendar(sourcePath, calendarId ?? Path.GetFileNameWithoutExtension(sourcePath), name, feedUrl);
    }

    private static IReadOnlyList<string> ReadHeader(string sourcePath)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var firstEvent = FindEventPositions(bytes).FirstOrDefault();
        var headerLength = firstEvent.Position > 0 ? firstEvent.Position : Math.Min(bytes.Length, 2048);
        return ExtractReadableStrings(bytes.Take(headerLength).ToArray()).ToArray();
    }

    private static async Task<IReadOnlyList<FavGCalParsedEvent>> ParseFavCalAsync(FavGCalSourceCalendar sourceCalendar, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(sourceCalendar.SourcePath, cancellationToken);
        if (bytes.Length < 64 || !Encoding.Unicode.GetString(bytes, 0, Math.Min(22, bytes.Length)).StartsWith("FavSchedule", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The file is not a FavGCalScheduler FavSchedule file.");
        }

        var events = new List<FavGCalParsedEvent>();
        foreach (var record in FindEventPositions(bytes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryParseEvent(bytes, record, sourceCalendar, out var parsed))
            {
                events.Add(parsed);
            }
        }

        return events;
    }

    private static IEnumerable<FavGCalRecordPosition> FindEventPositions(byte[] bytes)
    {
        for (var index = 0; index <= bytes.Length - 46; index += 2)
        {
            if (bytes[index] != 0x08 || bytes[index + 1] != 0x00 || bytes[index + 3] != 0x00
                || bytes[index + 2] is not (NormalEventRecordKind or TodoEventRecordKind))
            {
                continue;
            }

            var startSeconds = BitConverter.ToInt64(bytes, index + 12);
            var endSeconds = BitConverter.ToInt64(bytes, index + 20);
            if (!IsPlausibleUnixSeconds(startSeconds) || !IsPlausibleUnixSeconds(endSeconds) || endSeconds <= startSeconds)
            {
                continue;
            }

            var titleLength = BitConverter.ToInt32(bytes, index + 34);
            if (titleLength is <= 0 or > 4096 || index + 38 + titleLength * 2 > bytes.Length)
            {
                continue;
            }

            yield return new FavGCalRecordPosition(index, bytes[index + 2]);
        }
    }

    private static bool TryParseEvent(byte[] bytes, FavGCalRecordPosition record, FavGCalSourceCalendar sourceCalendar, out FavGCalParsedEvent parsed)
    {
        parsed = default!;
        try
        {
            var position = record.Position;
            var colorIndex = BitConverter.ToInt32(bytes, position + 8);
            var start = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(bytes, position + 12)).ToLocalTime();
            var end = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(bytes, position + 20)).ToLocalTime();
            var offset = position + 34;

            var title = ReadFavString(bytes, ref offset);
            var location = ReadFavString(bytes, ref offset);
            var description = ReadFavString(bytes, ref offset);
            var googleEventId = ReadGoogleEventId(bytes, ref offset);

            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var isAllDay = start.TimeOfDay == TimeSpan.Zero
                           && end.TimeOfDay == TimeSpan.Zero
                           && end > start;

            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                GoogleEventId = googleEventId,
                CalendarId = sourceCalendar.CalendarKey,
                Title = title.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
                Start = start,
                End = end,
                IsAllDay = isAllDay,
                ColorId = colorIndex is >= 1 and <= 11 ? colorIndex.ToString() : null,
                IsDeleted = false,
                IsDirty = true
            };
            calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);

            parsed = new FavGCalParsedEvent(
                sourceCalendar,
                calendarEvent,
                MissingTodoMetadata: record.Kind == TodoEventRecordKind && !calendarEvent.IsTodoLike);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException or InvalidDataException)
        {
            return false;
        }
    }

    private static string ReadFavString(byte[] bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            return "";
        }

        var length = BitConverter.ToInt32(bytes, offset);
        offset += 4;
        if (length is < 0 or > 32768 || offset + length * 2 > bytes.Length)
        {
            throw new InvalidDataException("Invalid string length in favcal record.");
        }

        var value = Encoding.Unicode.GetString(bytes, offset, length * 2);
        offset += length * 2;
        if (offset + 1 < bytes.Length && bytes[offset] == 0 && bytes[offset + 1] == 0)
        {
            offset += 2;
        }

        return value.TrimEnd('\0');
    }

    private static string? ReadGoogleEventId(byte[] bytes, ref int offset)
    {
        if (offset + 8 > bytes.Length)
        {
            return null;
        }

        offset += 4;
        var length = BitConverter.ToInt32(bytes, offset);
        offset += 4;
        if (length is <= 0 or > 512 || offset + length * 2 > bytes.Length)
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(bytes, offset, length * 2).TrimEnd('\0').Trim();
        return Regex.IsMatch(value, "^[A-Za-z0-9_-]{8,}$") ? value : null;
    }

    private static IEnumerable<string> ExtractReadableStrings(byte[] bytes)
    {
        var text = Encoding.Unicode.GetString(bytes);
        foreach (Match match in Regex.Matches(text, @"[\p{L}\p{N}\p{P}\p{S} @:/%._\-]{3,}"))
        {
            var value = match.Value.Trim('\0', ' ', '\r', '\n', '\t');
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static bool IsPlausibleUnixSeconds(long seconds)
    {
        return seconds is >= 631152000 and <= 4102444800;
    }

    private static string ResolveTargetCalendarId(FavGCalSourceCalendar sourceCalendar, FavGCalImportOptions options)
    {
        if (options.CalendarMappings.TryGetValue(sourceCalendar.CalendarKey, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return string.IsNullOrWhiteSpace(options.DefaultTargetCalendarId)
            ? sourceCalendar.CalendarKey
            : options.DefaultTargetCalendarId;
    }

    private static CalendarEvent CloneForComparison(CalendarEvent calendarEvent)
    {
        return new CalendarEvent
        {
            Id = calendarEvent.Id,
            GoogleEventId = calendarEvent.GoogleEventId,
            CalendarId = calendarEvent.CalendarId,
            Title = calendarEvent.Title,
            Description = calendarEvent.Description,
            Location = calendarEvent.Location,
            Start = calendarEvent.Start,
            End = calendarEvent.End,
            IsAllDay = calendarEvent.IsAllDay,
            ColorId = calendarEvent.ColorId,
            RecurrenceJson = calendarEvent.RecurrenceJson,
            IsDeleted = calendarEvent.IsDeleted,
            UpdatedAt = calendarEvent.UpdatedAt,
            LastSyncedAt = calendarEvent.LastSyncedAt,
            IsDirty = calendarEvent.IsDirty,
            IsTodoLike = calendarEvent.IsTodoLike
        };
    }
}

public sealed record FavGCalImportAnalysis(
    string SourceFolder,
    IReadOnlyList<FavGCalSourceCalendar> Calendars,
    int TotalEventCount,
    int UnrestoredTodoCount,
    int ParseErrorCount,
    IReadOnlyList<string> Warnings);

public sealed record FavGCalSourceCalendar(string SourcePath, string CalendarKey, string DisplayName, string? FeedUrl)
{
    public int EventCount { get; set; }
    public int UnrestoredTodoCount { get; set; }
}

public sealed record FavGCalParsedEvent(FavGCalSourceCalendar SourceCalendar, CalendarEvent Event, bool MissingTodoMetadata = false);
internal readonly record struct FavGCalRecordPosition(int Position, byte Kind);

public sealed record FavGCalImportOptions(
    string SourceFolder,
    IReadOnlyDictionary<string, string> CalendarMappings,
    bool ImportSettings = true,
    bool SkipDuplicates = true,
    bool VerifyGoogleEventsBeforeImport = true,
    bool MarkImportedEventsDirty = true,
    string? DefaultTargetCalendarId = null,
    string? ComparisonZipPath = null);

public sealed record FavGCalImportResult(
    int ImportedCount,
    int LinkedExistingGoogleCount,
    int SkippedDuplicateCount,
    int UnrestoredTodoCount,
    int ParseErrorCount,
    IReadOnlyList<string> Warnings,
    GoogleCalendarComparisonSummary? ComparisonSummary);

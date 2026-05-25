using System.Text;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public sealed class CalendarCsvService
{
    public static readonly string[] Headers =
    [
        "Title",
        "Description",
        "Location",
        "Start",
        "End",
        "IsAllDay",
        "CalendarId",
        "ColorId",
        "Tags",
        "TodoPriority",
        "TodoProgress"
    ];

    public async Task<CalendarCsvExportResult> ExportAsync(IEnumerable<CalendarEvent> events, string csvPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvPath))!);

        var rows = new List<string[]>
        {
            Headers
        };

        foreach (var calendarEvent in events.Where(e => !e.IsDeleted).OrderBy(e => e.Start).ThenBy(e => e.Title))
        {
            rows.Add(ToRow(calendarEvent));
        }

        await using var stream = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",", row.Select(Escape)));
        }

        return new CalendarCsvExportResult(csvPath, rows.Count - 1);
    }

    public async Task<CalendarCsvImportResult> ImportAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("CSV file was not found.", csvPath);
        }

        var content = await File.ReadAllTextAsync(csvPath, Encoding.UTF8, cancellationToken);
        var records = ParseCsv(content);
        if (records.Count == 0)
        {
            return new CalendarCsvImportResult([], [new CalendarCsvImportError(1, "CSV is empty.")]);
        }

        var headerMap = BuildHeaderMap(records[0]);
        var events = new List<CalendarEvent>();
        var errors = new List<CalendarCsvImportError>();

        for (var i = 1; i < records.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowNumber = i + 1;
            var row = records[i];
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0]))
            {
                continue;
            }

            try
            {
                events.Add(ParseEvent(row, headerMap, rowNumber));
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException)
            {
                errors.Add(new CalendarCsvImportError(rowNumber, ex.Message));
            }
        }

        return new CalendarCsvImportResult(events, errors);
    }

    private static string[] ToRow(CalendarEvent calendarEvent)
    {
        var tags = TagService.ExtractTags(calendarEvent.Title, calendarEvent.Description);
        return
        [
            calendarEvent.Title,
            calendarEvent.Description ?? "",
            calendarEvent.Location ?? "",
            calendarEvent.Start.ToString("O"),
            calendarEvent.End.ToString("O"),
            calendarEvent.IsAllDay ? "true" : "false",
            calendarEvent.CalendarId,
            calendarEvent.ColorId ?? "",
            string.Join(" ", tags),
            calendarEvent.TodoPriority,
            calendarEvent.IsTodoLike ? calendarEvent.TodoProgress.ToString() : ""
        ];
    }

    private static CalendarEvent ParseEvent(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> headerMap, int rowNumber)
    {
        var title = Get(row, headerMap, "Title").Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidDataException("Title is required.");
        }

        if (!DateTimeOffset.TryParse(Get(row, headerMap, "Start"), out var start))
        {
            throw new FormatException("Start must be an ISO 8601 date/time.");
        }

        if (!DateTimeOffset.TryParse(Get(row, headerMap, "End"), out var end))
        {
            throw new FormatException("End must be an ISO 8601 date/time.");
        }

        if (end <= start)
        {
            throw new InvalidDataException("End must be after Start.");
        }

        var description = EmptyToNull(Get(row, headerMap, "Description"));
        var todoPriority = Get(row, headerMap, "TodoPriority").Trim();
        var todoProgressText = Get(row, headerMap, "TodoProgress").Trim();
        if (!string.IsNullOrWhiteSpace(todoProgressText))
        {
            if (!int.TryParse(todoProgressText, out var todoProgress))
            {
                throw new FormatException("TodoProgress must be a number.");
            }

            if (todoProgress is < 0 or > 100)
            {
                throw new InvalidDataException("TodoProgress must be between 0 and 100.");
            }

            description = TagService.UpdateTodoMarker(description, todoPriority, todoProgress);
        }

        var tags = TagService.ExtractTags(title, description);
        var importTags = Get(row, headerMap, "Tags")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.StartsWith('#') && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (importTags.Length > 0)
        {
            description = string.IsNullOrWhiteSpace(description)
                ? string.Join(Environment.NewLine, importTags)
                : $"{string.Join(Environment.NewLine, importTags)}{Environment.NewLine}{description}";
        }

        var isAllDayText = Get(row, headerMap, "IsAllDay").Trim();
        var isAllDay = string.IsNullOrWhiteSpace(isAllDayText) || bool.Parse(isAllDayText);

        var calendarEvent = new CalendarEvent
        {
            Title = title,
            Description = description,
            Location = EmptyToNull(Get(row, headerMap, "Location")),
            Start = start,
            End = end,
            IsAllDay = isAllDay,
            CalendarId = string.IsNullOrWhiteSpace(Get(row, headerMap, "CalendarId")) ? GoogleCalendarDefaults.PrimaryCalendarId : Get(row, headerMap, "CalendarId").Trim(),
            ColorId = EmptyToNull(Get(row, headerMap, "ColorId")),
            IsDirty = true,
            IsDeleted = false,
            LastSyncedAt = null,
            GoogleEventId = null
        };
        calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);
        return calendarEvent;
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            map[header[i].Trim().TrimStart('\ufeff')] = i;
        }

        foreach (var requiredHeader in Headers)
        {
            if (!map.ContainsKey(requiredHeader))
            {
                throw new InvalidDataException($"CSV header is missing '{requiredHeader}'.");
            }
        }

        return map;
    }

    private static string Get(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> headerMap, string name)
    {
        var index = headerMap[name];
        return index < row.Count ? row[index] : "";
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static List<List<string>> ParseCsv(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (i + 1 < content.Length && content[i + 1] == '\n')
                    {
                        i++;
                    }

                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("CSV contains an unterminated quoted field.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}

public sealed record CalendarCsvExportResult(string CsvPath, int ExportedCount);
public sealed record CalendarCsvImportResult(IReadOnlyList<CalendarEvent> Events, IReadOnlyList<CalendarCsvImportError> Errors);
public sealed record CalendarCsvImportError(int RowNumber, string Message);

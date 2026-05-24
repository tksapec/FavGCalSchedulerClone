using System.Text.RegularExpressions;
using System.Windows.Media;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static partial class TagService
{
    public const string DefaultDisplayColor = "#FFFFFF";
    public const string DefaultDisplayForegroundColor = "#111827";

    public static IReadOnlyDictionary<string, EventDisplayColors> DefaultEventColorPalette { get; } = new Dictionary<string, EventDisplayColors>(StringComparer.Ordinal)
    {
        ["1"] = new("#A4BDFC", "#1D1D1D"),
        ["2"] = new("#7AE7BF", "#1D1D1D"),
        ["3"] = new("#DBADFF", "#1D1D1D"),
        ["4"] = new("#FF887C", "#1D1D1D"),
        ["5"] = new("#FBD75B", "#1D1D1D"),
        ["6"] = new("#FFB878", "#1D1D1D"),
        ["7"] = new("#46D6DB", "#1D1D1D"),
        ["8"] = new("#E1E1E1", "#1D1D1D"),
        ["9"] = new("#5484ED", "#FFFFFF"),
        ["10"] = new("#51B749", "#FFFFFF"),
        ["11"] = new("#DC2127", "#FFFFFF")
    };

    public static IReadOnlyList<CalendarTag> DefaultTags { get; } =
    [
        new() { Name = "#holiday", Color = "#FCA5A5", Priority = 100 },
        new() { Name = "#workday", Color = "#FFFFFF", Priority = 95 },
        new() { Name = "#important", Color = "#FDE68A", Priority = 90 },
        new() { Name = "#work", Color = "#93C5FD", Priority = 50 },
        new() { Name = "#private", Color = "#C4B5FD", Priority = 40 }
    ];

    public static IReadOnlyList<string> ExtractTags(string? title, string? description)
    {
        var input = $"{title ?? ""} {description ?? ""}";
        return TagRegex()
            .Matches(input)
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsHoliday(CalendarEvent calendarEvent) =>
        ExtractTags(calendarEvent.Title, calendarEvent.Description)
            .Any(x => string.Equals(x, "#holiday", StringComparison.OrdinalIgnoreCase));

    public static bool IsWorkday(CalendarEvent calendarEvent) =>
        ExtractTags(calendarEvent.Title, calendarEvent.Description)
            .Any(x => string.Equals(x, "#workday", StringComparison.OrdinalIgnoreCase));

    public static bool HasWorkdayOverride(IEnumerable<CalendarEvent> events, DateTime date) =>
        events.Any(e => IsWorkday(e) && DateRangeHelper.OccursOn(e, date));

    public static bool HasHolidayWithoutWorkdayOverride(IEnumerable<CalendarEvent> events, DateTime date)
    {
        var eventsOnDate = events.Where(e => DateRangeHelper.OccursOn(e, date)).ToArray();
        return eventsOnDate.Any(IsHoliday) && !eventsOnDate.Any(IsWorkday);
    }

    public static string? ResolveDayBackgroundColor(
        IEnumerable<CalendarEvent> events,
        DateTime date,
        IEnumerable<CalendarTag> tags)
    {
        var eventsOnDate = events.Where(e => DateRangeHelper.OccursOn(e, date)).ToArray();
        if (eventsOnDate.Any(IsWorkday))
        {
            return null;
        }

        var eventTags = eventsOnDate
            .SelectMany(e => ExtractTags(e.Title, e.Description))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tags
            .Where(t => IsDayBackgroundTag(t.Name) && eventTags.Contains(t.Name))
            .OrderByDescending(t => t.Priority)
            .Select(t => t.Color)
            .FirstOrDefault();
    }

    public static bool IsTodoLike(CalendarEvent calendarEvent) =>
        TodoRegex().IsMatch($"{calendarEvent.Title} {calendarEvent.Description}");

    public static TodoMetadata? GetTodoMetadata(CalendarEvent calendarEvent)
    {
        var match = TodoRegex().Match($"{calendarEvent.Title} {calendarEvent.Description}");
        if (!match.Success)
        {
            return null;
        }

        var priority = match.Groups["priority"].Success && !string.IsNullOrWhiteSpace(match.Groups["priority"].Value)
            ? match.Groups["priority"].Value.ToUpperInvariant()
            : "";
        var progress = int.Parse(match.Groups["progress"].Value);
        return new TodoMetadata(priority, Math.Clamp(progress, 0, 100));
    }

    public static bool IsTodoDone(CalendarEvent calendarEvent) =>
        GetTodoMetadata(calendarEvent)?.IsDone == true;

    public static string BuildTodoMarker(string? priority, int progress)
    {
        var normalizedPriority = string.IsNullOrWhiteSpace(priority) ? "" : priority.Trim()[0].ToString().ToUpperInvariant();
        if (normalizedPriority.Length > 0 && (normalizedPriority[0] < 'A' || normalizedPriority[0] > 'F'))
        {
            normalizedPriority = "";
        }

        return $"#todo{normalizedPriority}{Math.Clamp(progress, 0, 100)}%";
    }

    public static string UpdateTodoMarker(string? text, string? priority, int progress)
    {
        var marker = BuildTodoMarker(priority, progress);
        var body = GetTodoBodyForEditing(text);
        return string.IsNullOrWhiteSpace(body) ? marker : $"{marker}{Environment.NewLine}{body}";
    }

    public static string GetTodoBodyForEditing(string? text)
    {
        var body = TodoRegex().Replace(text ?? "", "");
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized
            .Split('\n')
            .Select(line => InlineWhitespaceRegex().Replace(line, " ").Trim())
            .ToArray();
        return string.Join(Environment.NewLine, lines).Trim();
    }

    public static CalendarTag? FindDisplayTag(CalendarEvent calendarEvent, IEnumerable<CalendarTag> tags)
    {
        var eventTags = ExtractTags(calendarEvent.Title, calendarEvent.Description);
        return tags
            .Where(t => eventTags.Any(et => string.Equals(et, t.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(t => t.Priority)
            .FirstOrDefault();
    }

    public static EventDisplayColors ResolveDisplayColors(
        CalendarEvent calendarEvent,
        IReadOnlyDictionary<string, EventDisplayColors>? eventColorPalette = null)
    {
        if (!string.IsNullOrWhiteSpace(calendarEvent.ColorId))
        {
            if (eventColorPalette is not null && eventColorPalette.TryGetValue(calendarEvent.ColorId, out var cachedColor))
            {
                return cachedColor;
            }

            if (DefaultEventColorPalette.TryGetValue(calendarEvent.ColorId, out var fallbackColor))
            {
                return fallbackColor;
            }
        }

        return new EventDisplayColors(DefaultDisplayColor, DefaultDisplayForegroundColor);
    }

    public static EventDisplayColors ResolveSelectedDisplayColors(string background, string foreground)
    {
        if (string.Equals(background, DefaultDisplayColor, StringComparison.OrdinalIgnoreCase))
        {
            return new EventDisplayColors("#DBEAFE", "#1E3A8A");
        }

        if (!TryParseColor(background, out var color))
        {
            return new EventDisplayColors(background, foreground);
        }

        var selected = Color.FromRgb(
            (byte)Math.Round(color.R * 0.84),
            (byte)Math.Round(color.G * 0.84),
            (byte)Math.Round(color.B * 0.84));
        var selectedBackground = $"#{selected.R:X2}{selected.G:X2}{selected.B:X2}";
        var luminance = (0.299 * selected.R) + (0.587 * selected.G) + (0.114 * selected.B);
        return new EventDisplayColors(selectedBackground, luminance < 120 ? "#FFFFFF" : foreground);
    }

    private static bool TryParseColor(string background, out Color color)
    {
        try
        {
            var parsed = ColorConverter.ConvertFromString(background);
            if (parsed is Color parsedColor)
            {
                color = parsedColor;
                return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
        }

        color = default;
        return false;
    }

    private static bool IsDayBackgroundTag(string tagName)
    {
        return tagName.Equals("#holiday", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("#important", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("#work", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("#private", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"#[\p{L}\p{N}_%-]+", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"#todo(?<priority>[A-Fa-f])?(?<progress>\d{1,3})%", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex TodoRegex();

    [GeneratedRegex(@"[^\S\r\n]+", RegexOptions.Compiled)]
    private static partial Regex InlineWhitespaceRegex();
}

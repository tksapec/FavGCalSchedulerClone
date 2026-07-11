using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static class CalendarSegmentLayoutService
{
    public const int MaxLanes = 5;
    public const int MinimumLanes = 2;

    public static CalendarSegmentLayoutResult PopulateSegments(
        IReadOnlyList<CalendarDay> days,
        IEnumerable<CalendarEvent> events,
        int maxLanes = MaxLanes)
    {
        var laneCapacity = Math.Max(MinimumLanes, maxLanes);
        foreach (var day in days)
        {
            day.Segments.Clear();
        }

        var visibleEvents = events.Where(calendarEvent => !calendarEvent.IsDeleted).ToArray();
        var layoutByDate = days.ToDictionary(
            day => day.Date.Date,
            _ => CalendarSegmentLayoutDayResult.Empty);
        for (var rowStart = 0; rowStart < days.Count; rowStart += 7)
        {
            var row = days.Skip(rowStart).Take(7).ToArray();
            PopulateWeekRow(row, visibleEvents, laneCapacity, layoutByDate);
        }

        return new CalendarSegmentLayoutResult(layoutByDate);
    }

    private static void PopulateWeekRow(
        IReadOnlyList<CalendarDay> row,
        IReadOnlyList<CalendarEvent> events,
        int maxLanes,
        IDictionary<DateTime, CalendarSegmentLayoutDayResult> layoutByDate)
    {
        var slots = new CalendarEventSegment?[row.Count, maxLanes];
        var candidates = events
            .Select(calendarEvent => new
            {
                Event = calendarEvent,
                DayIndexes = Enumerable.Range(0, row.Count)
                    .Where(index => DateRangeHelper.OccursOn(calendarEvent, row[index].Date))
                    .ToArray()
            })
            .Where(candidate => candidate.DayIndexes.Length > 0)
            .OrderByDescending(candidate => candidate.DayIndexes.Length)
            .ThenBy(candidate => candidate.Event.Start)
            .ThenBy(candidate => candidate.Event.End)
            .ThenBy(candidate => candidate.Event.Title, StringComparer.CurrentCulture)
            .ThenBy(candidate => candidate.Event.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var lane = Enumerable.Range(0, maxLanes)
                .FirstOrDefault(
                    availableLane => candidate.DayIndexes.All(index => slots[index, availableLane] is null),
                    -1);
            if (lane < 0)
            {
                continue;
            }

            var firstIndex = candidate.DayIndexes[0];
            var lastIndex = candidate.DayIndexes[^1];
            foreach (var index in candidate.DayIndexes)
            {
                slots[index, lane] = new CalendarEventSegment
                {
                    Event = candidate.Event,
                    Date = row[index].Date,
                    Lane = lane,
                    IsWeekSegmentStart = index == firstIndex,
                    IsWeekSegmentEnd = index == lastIndex,
                    ShowText = index == firstIndex
                };
            }
        }

        for (var index = 0; index < row.Count; index++)
        {
            var visibleSegments = new List<CalendarEventSegment>(maxLanes);
            for (var lane = 0; lane < maxLanes; lane++)
            {
                var segment = slots[index, lane] ?? CalendarEventSegment.Empty(row[index].Date, lane);
                row[index].Segments.Add(segment);
                if (segment.Event is not null)
                {
                    visibleSegments.Add(segment);
                }
            }

            var totalEventCount = candidates.Count(candidate => candidate.DayIndexes.Contains(index));
            layoutByDate[row[index].Date.Date] = new CalendarSegmentLayoutDayResult(
                visibleSegments.Select(segment => segment.Event!).ToArray(),
                totalEventCount - visibleSegments.Count);
        }
    }
}

public sealed class CalendarSegmentLayoutResult
{
    private readonly IReadOnlyDictionary<DateTime, CalendarSegmentLayoutDayResult> _days;

    internal CalendarSegmentLayoutResult(IReadOnlyDictionary<DateTime, CalendarSegmentLayoutDayResult> days)
    {
        _days = days;
    }

    public CalendarSegmentLayoutDayResult GetDay(DateTime date) =>
        _days.TryGetValue(date.Date, out var day)
            ? day
            : CalendarSegmentLayoutDayResult.Empty;
}

public sealed class CalendarSegmentLayoutDayResult
{
    internal static CalendarSegmentLayoutDayResult Empty { get; } = new([], 0);

    public CalendarSegmentLayoutDayResult(IReadOnlyList<CalendarEvent> visibleEvents, int hiddenEventCount)
    {
        VisibleEvents = visibleEvents;
        HiddenEventCount = Math.Max(0, hiddenEventCount);
    }

    public IReadOnlyList<CalendarEvent> VisibleEvents { get; }
    public int VisibleEventCount => VisibleEvents.Count;
    public int HiddenEventCount { get; }
}

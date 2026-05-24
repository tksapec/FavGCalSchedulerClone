using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static class CalendarSegmentLayoutService
{
    public const int MaxLanes = 5;

    public static void PopulateSegments(
        IReadOnlyList<CalendarDay> days,
        IEnumerable<CalendarEvent> events,
        int maxLanes = MaxLanes)
    {
        foreach (var day in days)
        {
            day.Segments.Clear();
        }

        var visibleEvents = events.Where(calendarEvent => !calendarEvent.IsDeleted).ToArray();
        for (var rowStart = 0; rowStart < days.Count; rowStart += 7)
        {
            var row = days.Skip(rowStart).Take(7).ToArray();
            PopulateWeekRow(row, visibleEvents, maxLanes);
        }
    }

    private static void PopulateWeekRow(
        IReadOnlyList<CalendarDay> row,
        IReadOnlyList<CalendarEvent> events,
        int maxLanes)
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
            for (var lane = 0; lane < maxLanes; lane++)
            {
                row[index].Segments.Add(slots[index, lane] ?? CalendarEventSegment.Empty(row[index].Date, lane));
            }
        }
    }
}

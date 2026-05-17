using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static class RecurrenceExpansionService
{
    public static IReadOnlyList<CalendarEvent> ExpandForRange(IEnumerable<CalendarEvent> storedEvents, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var source = storedEvents.ToArray();
        var recurringMasters = source.Where(item => item.IsRecurringMaster && !item.IsDeleted).ToArray();
        var exceptionsByParent = source
            .Where(item => item.IsRecurrenceException && (!item.IsDeleted || item.IsDeleted))
            .GroupBy(GetSeriesKey)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var results = new List<CalendarEvent>();

        foreach (var item in source.Where(item => !item.IsRecurringSeriesItem))
        {
            if (!item.IsDeleted && item.Start < rangeEnd && item.End > rangeStart)
            {
                results.Add(Clone(item));
            }
        }

        foreach (var master in recurringMasters)
        {
            var key = GetSeriesKey(master);
            var seriesExceptions = exceptionsByParent.TryGetValue(key, out var values) ? values : [];

            foreach (var occurrenceStart in RecurrenceRuleHelper.ExpandOccurrences(master, rangeStart, rangeEnd))
            {
                var exception = seriesExceptions
                    .Where(item => item.OriginalStart is not null)
                    .OrderByDescending(item => item.UpdatedAt)
                    .FirstOrDefault(item => MatchesOriginalStart(item.OriginalStart!.Value, occurrenceStart, master.IsAllDay));
                if (exception is not null)
                {
                    if (!exception.IsDeleted && exception.Start < rangeEnd && exception.End > rangeStart)
                    {
                        results.Add(Clone(exception));
                    }

                    continue;
                }

                var duration = master.End - master.Start;
                var generated = Clone(master);
                generated.Id = $"{master.Id}@{occurrenceStart.UtcTicks}";
                generated.GoogleEventId = null;
                generated.Start = occurrenceStart;
                generated.End = occurrenceStart + duration;
                generated.OriginalStart = occurrenceStart;
                generated.RecurringParentId = master.Id;
                generated.RecurringEventId = master.GoogleEventId;
                generated.IsGeneratedOccurrence = true;
                generated.IsRecurrenceException = false;
                results.Add(generated);
            }
        }

        foreach (var orphanException in source.Where(item => item.IsRecurrenceException && item.RecurringParentId is null && item.RecurringEventId is null && !item.IsDeleted))
        {
            if (orphanException.Start < rangeEnd && orphanException.End > rangeStart)
            {
                results.Add(Clone(orphanException));
            }
        }

        return results
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string GetSeriesKey(CalendarEvent calendarEvent)
    {
        return calendarEvent.RecurringParentId
            ?? calendarEvent.RecurringEventId
            ?? calendarEvent.Id;
    }

    private static CalendarEvent Clone(CalendarEvent source)
    {
        return new CalendarEvent
        {
            Id = source.Id,
            GoogleEventId = source.GoogleEventId,
            RecurringEventId = source.RecurringEventId,
            RecurringParentId = source.RecurringParentId,
            OriginalStart = source.OriginalStart,
            IsRecurrenceException = source.IsRecurrenceException,
            CalendarId = source.CalendarId,
            Title = source.Title,
            Description = source.Description,
            Location = source.Location,
            Start = source.Start,
            End = source.End,
            IsAllDay = source.IsAllDay,
            ColorId = source.ColorId,
            RecurrenceJson = source.RecurrenceJson,
            IsDeleted = source.IsDeleted,
            UpdatedAt = source.UpdatedAt,
            LastSyncedAt = source.LastSyncedAt,
            IsDirty = source.IsDirty,
            IsTodoLike = source.IsTodoLike,
            DisplayColor = source.DisplayColor,
            IsGeneratedOccurrence = source.IsGeneratedOccurrence
        };
    }

    private static bool MatchesOriginalStart(DateTimeOffset left, DateTimeOffset right, bool isAllDay)
    {
        return isAllDay
            ? left.Date == right.Date
            : left.UtcDateTime == right.UtcDateTime;
    }
}

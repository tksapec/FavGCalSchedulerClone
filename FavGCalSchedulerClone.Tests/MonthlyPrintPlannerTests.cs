using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class MonthlyPrintPlannerTests
{
    [Fact]
    public void Create_IncludesOnlyRequestedMonthGridAndOmitsDeletedEvents()
    {
        var month = new DateTime(2026, 5, 1);
        var plan = MonthlyPrintPlanner.Create(month, new[]
        {
            EventOn(new DateTime(2026, 5, 16), "visible"),
            EventOn(new DateTime(2026, 5, 17), "deleted", isDeleted: true)
        });

        Assert.Equal(42, plan.Days.Count);
        Assert.Equal(new DateTime(2026, 4, 26), plan.Days[0].Date);
        Assert.Equal(new DateTime(2026, 6, 6), plan.Days[^1].Date);
        Assert.Contains(plan.Days.Single(day => day.Date == new DateTime(2026, 5, 16)).Entries, entry => entry.Text == "visible");
        Assert.Empty(plan.Days.Single(day => day.Date == new DateTime(2026, 5, 17)).Entries);
    }

    [Fact]
    public void Create_ExpandsMultiDayEventToEachMatchingDay()
    {
        var eventItem = EventOn(new DateTime(2026, 5, 30), "月またぎ");
        eventItem.End = new DateTimeOffset(new DateTime(2026, 6, 2));

        var plan = MonthlyPrintPlanner.Create(new DateTime(2026, 5, 1), new[] { eventItem });

        Assert.Contains(plan.Days.Single(day => day.Date == new DateTime(2026, 5, 30)).Entries, entry => entry.Text == "月またぎ");
        Assert.Contains(plan.Days.Single(day => day.Date == new DateTime(2026, 5, 31)).Entries, entry => entry.Text == "月またぎ");
        Assert.Contains(plan.Days.Single(day => day.Date == new DateTime(2026, 6, 1)).Entries, entry => entry.Text == "月またぎ");
    }

    [Fact]
    public void Create_LimitsVisibleEntriesAndReportsHiddenCount()
    {
        var events = Enumerable.Range(1, 6)
            .Select(index => EventOn(new DateTime(2026, 5, 16), $"event {index}"))
            .ToArray();

        var day = MonthlyPrintPlanner.Create(new DateTime(2026, 5, 1), events)
            .Days.Single(day => day.Date == new DateTime(2026, 5, 16));

        Assert.Equal(MonthlyPrintPlanner.MaxEntriesPerDay, day.Entries.Count);
        Assert.Equal(2, day.HiddenEntryCount);
    }

    [Fact]
    public async Task CreateMonthlyPrintPlanAsync_UsesVisibleTagsAndCurrentMonth()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        await repository.SaveTagAsync(new CalendarTag { Name = "#hidden", Color = "#FF0000", IsVisible = false, Priority = 100 });
        await repository.SaveEventAsync(EventOn(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 16), "shown"));
        await repository.SaveEventAsync(EventOn(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 16), "hidden #hidden"));
        await viewModel.InitializeAsync();

        var plan = await viewModel.CreateMonthlyPrintPlanAsync();
        var entries = plan.Days.SelectMany(day => day.Entries).Select(entry => entry.Text).ToArray();

        Assert.Contains("shown", entries);
        Assert.DoesNotContain("hidden #hidden", entries);
    }

    private static CalendarEvent EventOn(DateTime date, string title, bool isDeleted = false)
    {
        return new CalendarEvent
        {
            Title = title,
            Start = new DateTimeOffset(date.Date),
            End = new DateTimeOffset(date.Date.AddDays(1)),
            IsAllDay = true,
            IsDeleted = isDeleted,
            DisplayColor = "#E5E7EB"
        };
    }
}

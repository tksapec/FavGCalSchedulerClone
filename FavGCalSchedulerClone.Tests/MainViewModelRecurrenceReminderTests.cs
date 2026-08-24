using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class MainViewModelRecurrenceReminderTests
{
    [Fact]
    public async Task SaveCurrentEventAsync_AllEvents_CopiesReminderSettingsToSeriesMaster()
    {
        var repository = await CreateRepositoryAsync();
        var master = CreateDailyMaster("series-all");
        await repository.SaveEventAsync(master);
        var viewModel = await CreateViewModelAsync(repository);
        var occurrence = await SelectOccurrenceAsync(viewModel, new DateTime(2026, 5, 12));
        Assert.True(occurrence.IsGeneratedOccurrence);
        viewModel.Title = occurrence.Title;
        viewModel.Description = occurrence.Description ?? "";
        viewModel.Location = occurrence.Location ?? "";
        viewModel.StartDate = occurrence.Start.Date;
        viewModel.EndDate = occurrence.End.Date;
        viewModel.StartTime = occurrence.Start.ToString("HH:mm");
        viewModel.EndTime = occurrence.End.ToString("HH:mm");
        viewModel.IsAllDay = false;
        viewModel.ReminderMinutesBeforeStart = 10;
        viewModel.IsAppReminderEnabled = true;
        viewModel.IsGoogleEmailReminderEnabled = true;

        await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.AllEvents);

        var stored = await repository.FindMasterByIdAsync(master.Id);
        Assert.NotNull(stored);
        Assert.Equal(10, stored!.ReminderMinutesBeforeStart);
        Assert.True(stored.IsAppReminderEnabled);
        Assert.True(stored.IsGoogleEmailReminderEnabled);
        Assert.Equal([10], stored.GoogleReminderMetadata?.PopupMinutes);
        Assert.Equal([10], stored.GoogleReminderMetadata?.EmailMinutes);
        Assert.Contains("Reminder", stored.DirtyFields ?? "");
        AssertGoogleReminders(stored, ("email", 10), ("popup", 10));
    }

    [Fact]
    public async Task SaveCurrentEventAsync_ThisAndFollowing_CopiesReminderSettingsToFutureSeries()
    {
        var repository = await CreateRepositoryAsync();
        var master = CreateDailyMaster("series-future");
        await repository.SaveEventAsync(master);
        var viewModel = await CreateViewModelAsync(repository);
        var occurrence = await SelectOccurrenceAsync(viewModel, new DateTime(2026, 5, 12));
        Assert.True(occurrence.IsGeneratedOccurrence);
        viewModel.Title = occurrence.Title;
        viewModel.Description = occurrence.Description ?? "";
        viewModel.Location = occurrence.Location ?? "";
        viewModel.StartDate = occurrence.Start.Date;
        viewModel.EndDate = occurrence.End.Date;
        viewModel.StartTime = occurrence.Start.ToString("HH:mm");
        viewModel.EndTime = occurrence.End.ToString("HH:mm");
        viewModel.IsAllDay = false;
        viewModel.ReminderMinutesBeforeStart = 10;
        viewModel.IsAppReminderEnabled = true;
        viewModel.IsGoogleEmailReminderEnabled = true;

        await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.ThisAndFollowing);

        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(new DateTime(2026, 5, 1)),
            new DateTimeOffset(new DateTime(2026, 6, 1)),
            includeDeleted: true);
        var original = Assert.Single(events, item => item.Id == master.Id);
        var future = Assert.Single(events, item => item.Id != master.Id && item.IsRecurringMaster);
        Assert.DoesNotContain("20260512", original.RecurrenceJson ?? "");
        Assert.Contains("COUNT=2", original.RecurrenceJson ?? "");
        Assert.Equal(10, future.ReminderMinutesBeforeStart);
        Assert.True(future.IsAppReminderEnabled);
        Assert.True(future.IsGoogleEmailReminderEnabled);
        Assert.Equal([10], future.GoogleReminderMetadata?.PopupMinutes);
        Assert.Equal([10], future.GoogleReminderMetadata?.EmailMinutes);
        Assert.Contains("New", future.DirtyFields ?? "");
        AssertGoogleReminders(future, ("email", 10), ("popup", 10));
    }

    [Fact]
    public async Task SaveCurrentEventAsync_ThisOccurrence_ReplacesOccurrenceWithoutExcludingItFromMaster()
    {
        var repository = await CreateRepositoryAsync();
        var master = CreateDailyMaster("series-single");
        await repository.SaveEventAsync(master);
        var viewModel = await CreateViewModelAsync(repository);
        var occurrenceDate = new DateTime(2026, 5, 12);
        var occurrence = await SelectOccurrenceAsync(viewModel, occurrenceDate);
        Assert.True(occurrence.IsGeneratedOccurrence);

        viewModel.Title = "Moved standup";
        viewModel.Description = occurrence.Description ?? "";
        viewModel.Location = occurrence.Location ?? "";
        viewModel.StartDate = occurrence.Start.Date;
        viewModel.EndDate = occurrence.End.Date;
        viewModel.StartTime = "15:00";
        viewModel.EndTime = "16:00";
        viewModel.IsAllDay = false;

        await viewModel.SaveCurrentEventAsync(RecurrenceEditScope.ThisOccurrence);

        var storedMaster = await repository.FindMasterByIdAsync(master.Id);
        Assert.NotNull(storedMaster);
        Assert.DoesNotContain("EXDATE", storedMaster!.RecurrenceJson ?? "", StringComparison.OrdinalIgnoreCase);

        await viewModel.NavigateToDateAsync(occurrenceDate);
        var edited = Assert.Single(
            viewModel.CalendarDays.Single(day => day.Date == occurrenceDate).Segments,
            segment => segment.Event?.Title == "Moved standup");
        Assert.Equal(15, edited.Event!.Start.Hour);
        Assert.DoesNotContain(
            viewModel.CalendarDays.Single(day => day.Date == occurrenceDate).Segments,
            segment => segment.Event?.Title == "Daily standup");
    }

    [Fact]
    public void CloneEventForEditing_DeepCopiesGoogleReminderMetadata()
    {
        var source = CreateDailyMaster("clone-source");
        source.GoogleReminderMetadata = new GoogleReminderMetadata
        {
            PopupMinutes = [30],
            EmailMinutes = [60]
        };

        var clone = MainViewModel.CloneEventForEditing(source);
        clone.GoogleReminderMetadata!.PopupMinutes.Add(10);
        clone.GoogleReminderMetadata.EmailMinutes.Clear();

        Assert.Equal([30], source.GoogleReminderMetadata!.PopupMinutes);
        Assert.Equal([60], source.GoogleReminderMetadata.EmailMinutes);
        Assert.Equal([30, 10], clone.GoogleReminderMetadata.PopupMinutes);
        Assert.Empty(clone.GoogleReminderMetadata.EmailMinutes);
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task<MainViewModel> CreateViewModelAsync(CalendarRepository repository)
    {
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static CalendarEvent CreateDailyMaster(string id) => new()
    {
        Id = id,
        Title = "Daily standup",
        CalendarId = "primary",
        Start = new DateTimeOffset(new DateTime(2026, 5, 10, 9, 0, 0)),
        End = new DateTimeOffset(new DateTime(2026, 5, 10, 10, 0, 0)),
        RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=5\"]",
        ReminderMinutesBeforeStart = 30,
        IsAppReminderEnabled = false,
        IsGoogleEmailReminderEnabled = false,
        GoogleReminderMetadata = new GoogleReminderMetadata
        {
            Source = "explicit"
        }
    };

    private static async Task<CalendarEvent> SelectOccurrenceAsync(MainViewModel viewModel, DateTime occurrenceDate)
    {
        await viewModel.NavigateToDateAsync(occurrenceDate);
        var occurrence = viewModel.CalendarDays
            .Single(day => day.Date == occurrenceDate)
            .Segments
            .Single(segment => segment.Event?.Title == "Daily standup")
            .Event!;
        viewModel.SelectEvent(occurrence);
        return occurrence;
    }

    private static void AssertGoogleReminders(CalendarEvent calendarEvent, params (string Method, int Minutes)[] expected)
    {
        var googleEvent = GoogleEventMapper.ToGoogleEvent(calendarEvent);
        var actual = googleEvent.Reminders.Overrides
            .Select(item => (item.Method, item.Minutes.GetValueOrDefault()))
            .OrderBy(item => item.Method)
            .ThenBy(item => item.Item2)
            .ToArray();
        Assert.Equal(expected.OrderBy(item => item.Method).ThenBy(item => item.Minutes).ToArray(), actual);
    }
}

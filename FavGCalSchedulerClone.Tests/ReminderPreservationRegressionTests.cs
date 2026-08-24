using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderPreservationRegressionTests
{
    [Fact]
    public async Task TitleOnlyEdit_PreservesGoogleDefaultReminderMode()
    {
        var repository = await CreateRepositoryAsync();
        var local = GoogleEventMapper.FromGoogleEvent(
            CreateDefaultReminderGoogleEvent(),
            "work",
            [new GoogleReminderOverride("popup", 30)]);
        await repository.UpsertSyncedEventAsync(local);
        var viewModel = await CreateViewModelAsync(repository);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));

        viewModel.SelectEvent(stored);
        viewModel.Title = "Edited title";
        await viewModel.SaveCurrentEventAsync();

        var edited = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));
        var googlePayload = GoogleEventMapper.ToGoogleEvent(edited);
        Assert.True(googlePayload.Reminders.UseDefault);
        Assert.Empty(googlePayload.Reminders.Overrides ?? []);
        Assert.True(edited.GoogleReminderMetadata?.UseDefault);
    }

    [Fact]
    public async Task ExplicitReminderEdit_ChangesDefaultReminderModeToOverrides()
    {
        var repository = await CreateRepositoryAsync();
        var local = GoogleEventMapper.FromGoogleEvent(
            CreateDefaultReminderGoogleEvent(),
            "work",
            [new GoogleReminderOverride("popup", 30)]);
        await repository.UpsertSyncedEventAsync(local);
        var viewModel = await CreateViewModelAsync(repository);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));

        viewModel.SelectEvent(stored);
        viewModel.AppReminderMinutesBeforeStart = [10];
        viewModel.ReminderMinutesBeforeStart = 10;
        viewModel.IsAppReminderEnabled = true;
        await viewModel.SaveCurrentEventAsync();

        var edited = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));
        var googlePayload = GoogleEventMapper.ToGoogleEvent(edited);
        Assert.False(googlePayload.Reminders.UseDefault);
        var popup = Assert.Single(googlePayload.Reminders.Overrides ?? [], item => item.Method == "popup");
        Assert.Equal(10, popup.Minutes);
        Assert.False(edited.GoogleReminderMetadata?.UseDefault);
    }

    private static Event CreateDefaultReminderGoogleEvent() => new()
    {
        Id = "default-reminder-event",
        ETag = "etag-default",
        Summary = "Default reminder",
        Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(9)), TimeZone = "Asia/Tokyo" },
        End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(9)), TimeZone = "Asia/Tokyo" },
        Status = "confirmed",
        Reminders = new Event.RemindersData { UseDefault = true, Overrides = [] }
    };

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task<MainViewModel> CreateViewModelAsync(CalendarRepository repository)
    {
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return viewModel;
    }
}

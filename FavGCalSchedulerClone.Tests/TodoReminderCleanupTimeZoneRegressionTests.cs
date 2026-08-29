using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class TodoReminderCleanupTimeZoneRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyTodoReminderCleanupStateAsync_PreservesPersistedTimeZonesWhileClearingGoogleReminders(bool preserveDirtyState)
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var calendarEvent = new CalendarEvent
        {
            Id = "todo-timezone-cleanup",
            CalendarId = "work",
            GoogleEventId = "google-todo-timezone",
            LastSyncedGoogleEtag = "etag-before-cleanup",
            Title = "Timed todo",
            Start = new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.FromHours(9)),
            StartTimeZoneId = "Asia/Tokyo",
            EndTimeZoneId = "Asia/Tokyo",
            GoogleReminderMetadata = new GoogleReminderMetadata
            {
                UseDefault = false,
                PopupMinutes = [10],
                EmailMinutes = [30],
                Source = "explicit"
            },
            AppReminderMinutesBeforeStart = [10],
            GoogleEmailReminderMinutesBeforeStart = [30],
            IsAppReminderEnabled = true,
            IsGoogleEmailReminderEnabled = true,
            IsDirty = preserveDirtyState
        };
        await repository.SaveEventAsync(calendarEvent);

        await repository.ApplyTodoReminderCleanupStateAsync(
            calendarEvent.Id,
            preserveDirtyState,
            cleanedGoogleEtag: "etag-after-cleanup");
        var reloaded = await repository.FindEventByIdAsync(calendarEvent.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("Asia/Tokyo", reloaded!.StartTimeZoneId);
        Assert.Equal("Asia/Tokyo", reloaded.EndTimeZoneId);
        Assert.Empty(reloaded.EffectiveAppReminderMinutesBeforeStart);
        Assert.Empty(reloaded.EffectiveGoogleEmailReminderMinutesBeforeStart);
        Assert.NotNull(reloaded.GoogleReminderMetadata);
        Assert.False(reloaded.GoogleReminderMetadata!.HasGoogleReminder);
        Assert.Equal(
            preserveDirtyState ? "etag-before-cleanup" : "etag-after-cleanup",
            reloaded.LastSyncedGoogleEtag);
    }
}

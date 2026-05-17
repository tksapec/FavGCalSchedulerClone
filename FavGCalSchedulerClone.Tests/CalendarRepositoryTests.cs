using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using System.Text.Json;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarRepositoryTests
{
    [Fact]
    public void AppSettings_DeserializesLegacyJsonWithDefaults()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(
            """
            {
              "OAuthClientJsonPath": "client.json",
              "ActiveCalendarId": "primary",
              "DisplayMonth": "2026-05-01T00:00:00"
            }
            """);

        Assert.NotNull(settings);
        Assert.Equal("client.json", settings.OAuthClientJsonPath);
        Assert.Empty(settings.VisibleCalendarIds);
        Assert.Equal(0, settings.StartupTabIndex);
        Assert.True(settings.ConfirmBeforeDelete);
        Assert.True(settings.CloseButtonExitsApplication);
        Assert.True(settings.DefaultNewEventIsAllDay);
        Assert.True(settings.UseWindowsToastNotifications);
    }

    [Fact]
    public async Task SaveSettingsAsync_RoundTripsApplicationSettings()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        await repository.SaveSettingsAsync(new AppSettings
        {
            OAuthClientJsonPath = "client.json",
            ActiveCalendarId = "primary",
            VisibleCalendarIds = ["primary", "team"],
            DisplayMonth = new DateTime(2026, 5, 1),
            StartupTabIndex = 3,
            ConfirmBeforeDelete = false,
            CloseButtonExitsApplication = false,
            DefaultNewEventIsAllDay = false,
            UseWindowsToastNotifications = false
        });

        var settings = await repository.LoadSettingsAsync();

        Assert.Equal("client.json", settings.OAuthClientJsonPath);
        Assert.Equal(["primary", "team"], settings.VisibleCalendarIds);
        Assert.Equal(new DateTime(2026, 5, 1), settings.DisplayMonth);
        Assert.Equal(3, settings.StartupTabIndex);
        Assert.False(settings.ConfirmBeforeDelete);
        Assert.False(settings.CloseButtonExitsApplication);
        Assert.False(settings.DefaultNewEventIsAllDay);
        Assert.False(settings.UseWindowsToastNotifications);
    }

    [Fact]
    public async Task UpsertSyncedEventAsync_MergesByGoogleEventId()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        var local = new CalendarEvent
        {
            Title = "local",
            CalendarId = "primary",
            GoogleEventId = "google-1",
            Start = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
            IsDirty = false
        };
        await repository.SaveEventAsync(local);

        await repository.UpsertSyncedEventAsync(new CalendarEvent
        {
            Id = "g:primary:google-1",
            Title = "remote",
            CalendarId = "primary",
            GoogleEventId = "google-1",
            Start = local.Start,
            End = local.End
        });

        var events = await repository.LoadEventsAsync(local.Start.AddHours(-1), local.End.AddHours(1));

        Assert.Single(events);
        Assert.Equal(local.Id, events[0].Id);
        Assert.Equal("remote", events[0].Title);
    }

    [Fact]
    public async Task SaveEventAsync_RoundTripsRecurrenceFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        var item = new CalendarEvent
        {
            Id = "exception-1",
            Title = "Occurrence override",
            CalendarId = "primary",
            GoogleEventId = "instance-1",
            RecurringEventId = "series-1",
            RecurringParentId = "local-series-1",
            OriginalStart = new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero),
            IsRecurrenceException = true,
            Start = new DateTimeOffset(2026, 5, 16, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero),
            ReminderMinutesBeforeStart = 10
        };

        await repository.SaveEventAsync(item);
        var loaded = await repository.FindEventByGoogleEventIdAsync("primary", "instance-1");

        Assert.NotNull(loaded);
        Assert.Equal("series-1", loaded!.RecurringEventId);
        Assert.Equal("local-series-1", loaded.RecurringParentId);
        Assert.Equal(item.OriginalStart, loaded.OriginalStart);
        Assert.True(loaded.IsRecurrenceException);
        Assert.Equal(10, loaded.ReminderMinutesBeforeStart);
    }
}

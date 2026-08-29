using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void AppSettings_UsesTimedEventsByDefaultButPreservesSavedAllDayPreference()
    {
        Assert.False(new AppSettings().DefaultNewEventIsAllDay);

        var settings = JsonSerializer.Deserialize<AppSettings>("{\"DefaultNewEventIsAllDay\":true}");

        Assert.NotNull(settings);
        Assert.True(settings.DefaultNewEventIsAllDay);
    }

    [Fact]
    public void AppSettings_DeserializesLegacyReturnToTodaySettingWithoutReserializingLegacyName()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"ReturnToTodayOnDeactivate\":false}");

        Assert.NotNull(settings);
        Assert.False(settings.ReturnToTodayWhenDeactivated);

        var serialized = JsonSerializer.Serialize(settings);
        Assert.Contains("\"ReturnToTodayWhenDeactivated\":false", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ReturnToTodayOnDeactivate\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSettings_CurrentReturnToTodaySettingTakesPrecedenceOverLegacyAlias()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {
              "ReturnToTodayWhenDeactivated": true,
              "ReturnToTodayOnDeactivate": false
            }
            """);

        Assert.NotNull(settings);
        Assert.True(settings.ReturnToTodayWhenDeactivated);
    }

    [Fact]
    public async Task SaveApplicationSettingsAsync_WhenPersistenceFails_RestoresPreviousSettingsAndView()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new CalendarRepository(directory);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository))
            {
                CurrentViewMode = CalendarViewMode.Week
            };
            var changed = viewModel.CreateSettingsSnapshot();
            changed.ConfirmBeforeDelete = false;
            changed.StartupCalendarViewMode = CalendarViewMode.Day;
            changed.OAuthClientJsonPath = @"C:\temp\client.json";

            await Assert.ThrowsAnyAsync<Exception>(() => viewModel.SaveApplicationSettingsAsync(changed));

            Assert.True(viewModel.ConfirmBeforeDelete);
            Assert.Equal(CalendarViewMode.Month, viewModel.CreateSettingsSnapshot().StartupCalendarViewMode);
            Assert.Null(viewModel.CreateSettingsSnapshot().OAuthClientJsonPath);
            Assert.Equal("", viewModel.OAuthClientJsonPath);
            Assert.Equal(CalendarViewMode.Week, viewModel.CurrentViewMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveApplicationSettingsAsync_WhenOAuthPathChanges_UpdatesPublicAndPersistedStateTogether()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "calendar.db");
        try
        {
            var repository = new CalendarRepository(databasePath);
            await repository.InitializeAsync();
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            var changed = viewModel.CreateSettingsSnapshot();
            changed.OAuthClientJsonPath = @"C:\missing\client.json";

            await viewModel.SaveApplicationSettingsAsync(changed);

            Assert.Equal(@"C:\missing\client.json", viewModel.OAuthClientJsonPath);
            Assert.Equal(@"C:\missing\client.json", viewModel.CreateSettingsSnapshot().OAuthClientJsonPath);
            Assert.Equal(@"C:\missing\client.json", (await repository.LoadSettingsAsync()).OAuthClientJsonPath);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetOAuthClientJsonPathAsync_WhenValueIsUnchanged_DoesNotPersistOrReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new CalendarRepository(directory);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

            await viewModel.SetOAuthClientJsonPathAsync("");

            Assert.Equal("", viewModel.OAuthClientJsonPath);
            Assert.Null(viewModel.CreateSettingsSnapshot().OAuthClientJsonPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetOAuthClientJsonPathAsync_WhenPersistenceFails_RestoresPreviousValue()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new CalendarRepository(directory);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

            await Assert.ThrowsAnyAsync<Exception>(() => viewModel.SetOAuthClientJsonPathAsync(@"C:\temp\client.json"));

            Assert.Equal("", viewModel.OAuthClientJsonPath);
            Assert.Null(viewModel.CreateSettingsSnapshot().OAuthClientJsonPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorizeGoogleAsync_WhenSettingsPersistenceFails_DoesNotKeepUnsavedOAuthState()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var repository = new CalendarRepository(directory);
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository))
            {
                OAuthClientJsonPath = @"C:\temp\client.json"
            };

            await Assert.ThrowsAnyAsync<Exception>(() => viewModel.AuthorizeGoogleAsync());

            var settings = viewModel.CreateSettingsSnapshot();
            Assert.Null(settings.OAuthClientJsonPath);
            Assert.Empty(settings.VisibleCalendarIds);
            Assert.Equal(GoogleCalendarDefaults.PrimaryCalendarId, settings.ActiveCalendarId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Normalize_RepairsNullCollectionsAndInvalidEnumValues()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {
              "VisibleCalendarIds": null,
              "EventColorSettings": null,
              "StartupCalendarViewMode": 999,
              "WeekdayDisplayType": 999,
              "SyncConflictPolicy": 999
            }
            """);

        var normalized = AppSettingsNormalizer.Normalize(Assert.IsType<AppSettings>(settings));

        Assert.Equal(CalendarViewMode.Month, normalized.StartupCalendarViewMode);
        Assert.Equal(WeekdayDisplayType.EnglishShort, normalized.WeekdayDisplayType);
        Assert.Equal(SyncConflictPolicy.SkipLocalDirty, normalized.SyncConflictPolicy);
        Assert.Equal([GoogleCalendarDefaults.PrimaryCalendarId], normalized.VisibleCalendarIds);
        Assert.Empty(normalized.EventColorSettings);
    }
}

using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

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

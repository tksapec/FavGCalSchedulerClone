using System.Text.Json;
using FavGCalSchedulerClone.App.Models;

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
}

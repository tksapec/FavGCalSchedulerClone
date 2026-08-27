using System.Text.Json;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReturnToTodaySettingTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public void Setting_DefaultsToEnabledAndRoundTripsDisabled()
    {
        Assert.True(new AppSettings().ReturnToTodayWhenDeactivated);

        var legacySettings = JsonSerializer.Deserialize<AppSettings>("{}");
        Assert.NotNull(legacySettings);
        Assert.True(legacySettings.ReturnToTodayWhenDeactivated);

        var json = JsonSerializer.Serialize(new AppSettings
        {
            ReturnToTodayWhenDeactivated = false
        });
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.False(restored.ReturnToTodayWhenDeactivated);
    }

    [Fact]
    public async Task Setting_IsWiredIntoDeactivationAndSettingsDialog()
    {
        var app = await ReadAppFileAsync("App.xaml.cs");
        var dialog = await ReadAppFileAsync("Views", "Dialogs", "SettingsDialog.cs");

        Assert.Contains("!viewModel.CreateSettingsSnapshot().ReturnToTodayWhenDeactivated", app, StringComparison.Ordinal);
        Assert.Contains("IsChecked = settings.ReturnToTodayWhenDeactivated", dialog, StringComparison.Ordinal);
        Assert.Contains("settings.ReturnToTodayWhenDeactivated = returnToTodayWhenDeactivated.IsChecked == true;", dialog, StringComparison.Ordinal);
    }

    private static Task<string> ReadAppFileAsync(params string[] relativePath)
    {
        var path = relativePath.Aggregate(
            Path.Combine(Root, "FavGCalSchedulerClone.App"),
            Path.Combine);
        return File.ReadAllTextAsync(path);
    }
}

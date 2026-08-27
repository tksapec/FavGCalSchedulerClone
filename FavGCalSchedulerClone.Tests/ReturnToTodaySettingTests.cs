using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

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
    public async Task Setting_PersistsThroughRepository()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();

        await repository.SaveSettingsAsync(new AppSettings
        {
            ReturnToTodayWhenDeactivated = false
        });

        var restored = await repository.LoadSettingsAsync();

        Assert.False(restored.ReturnToTodayWhenDeactivated);
    }

    [Fact]
    public async Task Setting_IsWiredIntoDeactivationAndSettingsDialog()
    {
        var app = await ReadAppFileAsync("App.xaml.cs");
        var dialog = await ReadAppFileAsync("Views", "Dialogs", "SettingsDialog.cs");

        var preferenceCheck = app.IndexOf("!viewModel.CreateSettingsSnapshot().ReturnToTodayWhenDeactivated", StringComparison.Ordinal);
        var returnToTodayCall = app.IndexOf("await viewModel.ReturnSelectionToTodayAsync(cancellation.Token);", StringComparison.Ordinal);
        Assert.True(preferenceCheck >= 0 && returnToTodayCall > preferenceCheck);

        Assert.Contains("IsChecked = settings.ReturnToTodayWhenDeactivated", dialog, StringComparison.Ordinal);
        var cancelGuard = dialog.IndexOf("if (window.ShowDialog() != true)", StringComparison.Ordinal);
        var assignment = dialog.IndexOf("settings.ReturnToTodayWhenDeactivated = returnToTodayWhenDeactivated.IsChecked == true;", StringComparison.Ordinal);
        Assert.True(cancelGuard >= 0 && assignment > cancelGuard);
    }

    private static Task<string> ReadAppFileAsync(params string[] relativePath)
    {
        var path = relativePath.Aggregate(
            Path.Combine(Root, "FavGCalSchedulerClone.App"),
            Path.Combine);
        return File.ReadAllTextAsync(path);
    }
}

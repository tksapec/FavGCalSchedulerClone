using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

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
    public async Task QuickToggle_PersistsBothDirections()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        Assert.True(viewModel.ReturnToTodayWhenDeactivated);

        await viewModel.ToggleReturnToTodayWhenDeactivatedAsync();

        Assert.False(viewModel.ReturnToTodayWhenDeactivated);
        Assert.False((await repository.LoadSettingsAsync()).ReturnToTodayWhenDeactivated);

        await viewModel.ToggleReturnToTodayWhenDeactivatedAsync();

        Assert.True(viewModel.ReturnToTodayWhenDeactivated);
        Assert.True((await repository.LoadSettingsAsync()).ReturnToTodayWhenDeactivated);
    }

    [Fact]
    public async Task Setting_IsWiredIntoDeactivationSettingsDialogAndQuickMenu()
    {
        var app = await ReadAppFileAsync("App.xaml.cs");
        var viewModelSetting = await ReadAppFileAsync("ViewModels", "MainViewModel.ReturnToTodaySetting.cs");
        var dialog = await ReadAppFileAsync("Views", "Dialogs", "SettingsDialog.cs");
        var quickMenu = await ReadAppFileAsync("MainWindow.ReturnToTodayQuickToggle.cs");

        var preferenceCheck = app.IndexOf("!viewModel.ReturnToTodayWhenDeactivated", StringComparison.Ordinal);
        var returnToTodayCall = app.IndexOf("await viewModel.ReturnSelectionToTodayAsync(cancellation.Token);", StringComparison.Ordinal);
        Assert.True(preferenceCheck >= 0 && returnToTodayCall > preferenceCheck);
        Assert.DoesNotContain("CreateSettingsSnapshot().ReturnToTodayWhenDeactivated", app, StringComparison.Ordinal);

        Assert.Contains("public bool ReturnToTodayWhenDeactivated", viewModelSetting, StringComparison.Ordinal);
        Assert.Contains("ToggleReturnToTodayWhenDeactivatedCommand", viewModelSetting, StringComparison.Ordinal);
        Assert.Contains("ToggleReturnToTodayWhenDeactivatedAsync", viewModelSetting, StringComparison.Ordinal);
        Assert.Contains("CreateSettingsPersistenceRequestUnsafe()", viewModelSetting, StringComparison.Ordinal);
        Assert.Contains("PersistSettingsAsync(snapshot)", viewModelSetting, StringComparison.Ordinal);

        Assert.Contains("IsChecked = settings.ReturnToTodayWhenDeactivated", dialog, StringComparison.Ordinal);
        var cancelGuard = dialog.IndexOf("if (window.ShowDialog() != true)", StringComparison.Ordinal);
        var assignment = dialog.IndexOf("settings.ReturnToTodayWhenDeactivated = returnToTodayWhenDeactivated.IsChecked == true;", StringComparison.Ordinal);
        Assert.True(cancelGuard >= 0 && assignment > cancelGuard);

        Assert.Contains("Header = \"フォーカス解除時に今日へ戻す(_T)\"", quickMenu, StringComparison.Ordinal);
        Assert.Contains("Command = viewModel.ToggleReturnToTodayWhenDeactivatedCommand", quickMenu, StringComparison.Ordinal);
        Assert.Contains("IsChecked = viewModel.ReturnToTodayWhenDeactivated", quickMenu, StringComparison.Ordinal);
        Assert.Contains("viewModel.PropertyChanged +=", quickMenu, StringComparison.Ordinal);
    }

    private static Task<string> ReadAppFileAsync(params string[] relativePath)
    {
        var path = relativePath.Aggregate(
            Path.Combine(Root, "FavGCalSchedulerClone.App"),
            Path.Combine);
        return File.ReadAllTextAsync(path);
    }
}

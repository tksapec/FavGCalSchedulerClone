using System.Text.Json;
using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class RetiredCloseBehaviorSettingTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public void LegacySettingsJson_IgnoresRetiredCloseButtonProperty()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {
              "CloseButtonExitsApplication": false,
              "ConfirmBeforeDelete": false
            }
            """);

        Assert.NotNull(settings);
        Assert.False(settings.ConfirmBeforeDelete);
        Assert.DoesNotContain("CloseButtonExitsApplication", JsonSerializer.Serialize(settings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sources_DoNotExposeOrImportRetiredCloseBehaviorSetting()
    {
        var appSettings = await ReadAppFileAsync("Models", "AppSettings.cs");
        var mainViewModel = await ReadAppFileAsync("ViewModels", "MainViewModel.cs");
        var settingsViewModel = await ReadAppFileAsync("ViewModels", "MainViewModel.Settings.cs");
        var importExport = await ReadAppFileAsync("ViewModels", "MainViewModel.ImportExport.cs");
        var mainWindow = await ReadAppFileAsync("MainWindow.xaml.cs");

        Assert.DoesNotContain("CloseButtonExitsApplication", appSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseButtonExitsApplication", mainViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseButtonExitsApplication", settingsViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("AppClose", importExport, StringComparison.Ordinal);

        Assert.Contains("e.Cancel = true;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Hide();", mainWindow, StringComparison.Ordinal);
    }

    private static Task<string> ReadAppFileAsync(params string[] relativePath)
    {
        var path = relativePath.Aggregate(
            Path.Combine(Root, "FavGCalSchedulerClone.App"),
            Path.Combine);
        return File.ReadAllTextAsync(path);
    }
}

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
              "CloseButtonExitsApplication": true,
              "ConfirmBeforeDelete": false
            }
            """);

        Assert.NotNull(settings);
        Assert.False(settings.ConfirmBeforeDelete);
        Assert.False(settings.CloseButtonExitsApplication);
        Assert.DoesNotContain("CloseButtonExitsApplication", JsonSerializer.Serialize(settings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sources_DoNotPersistOrImportRetiredCloseBehaviorSetting()
    {
        var appSettings = await ReadAppFileAsync("Models", "AppSettings.cs");
        var settingsViewModel = await ReadAppFileAsync("ViewModels", "MainViewModel.Settings.cs");
        var importExport = await ReadAppFileAsync("ViewModels", "MainViewModel.ImportExport.cs");
        var mainWindow = await ReadAppFileAsync("MainWindow.xaml.cs");

        Assert.Contains("[JsonIgnore]", appSettings, StringComparison.Ordinal);
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

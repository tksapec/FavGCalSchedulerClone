namespace FavGCalSchedulerClone.Tests;

public sealed class SettingsAtomicSaveSourceTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task SettingsDialogSave_ReusesTheLastPersistedOAuthPathBeforeSavingTheWholeSnapshot()
    {
        var mainWindow = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var methodStart = mainWindow.IndexOf("private async Task ShowSettingsDialogAsync()", StringComparison.Ordinal);
        var nextMethod = mainWindow.IndexOf("private async Task<bool> UpdateJapaneseHolidaysAsync()", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = mainWindow[methodStart..nextMethod];

        var setOAuth = method.IndexOf("await _viewModel.SetOAuthClientJsonPathAsync(result.OAuthClientJsonPath);", StringComparison.Ordinal);
        var saveSettings = method.IndexOf("await _viewModel.SaveApplicationSettingsAsync(result.Settings);", StringComparison.Ordinal);
        Assert.True(setOAuth >= 0 && saveSettings > setOAuth);

        var dialog = await File.ReadAllTextAsync(Path.Combine(AppRoot, "Views", "Dialogs", "SettingsDialog.cs"));
        Assert.Contains("var persistedOAuthClientJsonPath = request.OAuthClientJsonPath;", dialog, StringComparison.Ordinal);
        Assert.Contains("persistedOAuthClientJsonPath = NormalizeOAuthPath(oauthPath.Text);", dialog, StringComparison.Ordinal);
        Assert.Contains("new SettingsDialogResult(settings, persistedOAuthClientJsonPath)", dialog, StringComparison.Ordinal);
    }
}

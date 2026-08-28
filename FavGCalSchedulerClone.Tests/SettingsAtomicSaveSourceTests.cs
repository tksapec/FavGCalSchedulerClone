namespace FavGCalSchedulerClone.Tests;

public sealed class SettingsAtomicSaveSourceTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task SettingsDialogSave_DoesNotPersistOAuthSeparatelyBeforeTheSettingsSnapshot()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        var methodStart = source.IndexOf("private async Task ShowSettingsDialogAsync()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private async Task<bool> UpdateJapaneseHolidaysAsync()", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = source[methodStart..nextMethod];

        Assert.DoesNotContain("SetOAuthClientJsonPathAsync(result.OAuthClientJsonPath)", method, StringComparison.Ordinal);
        Assert.Contains("await _viewModel.SaveApplicationSettingsAsync(result.Settings);", method, StringComparison.Ordinal);
        Assert.Contains("await _viewModel.ReloadAvailableCalendarsAsync();", method, StringComparison.Ordinal);
    }
}

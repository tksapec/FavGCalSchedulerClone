namespace FavGCalSchedulerClone.Tests;

public sealed class RestoreUiInteractionRegressionTests
{
    private static readonly string MainWindowSourcePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App", "MainWindow.xaml.cs"));

    [Fact]
    public async Task Restore_DisablesMainWindowOnlyWhileDatabaseRestoreIsRunning()
    {
        var source = await File.ReadAllTextAsync(MainWindowSourcePath);
        var start = source.IndexOf("private async Task RestoreAllCalendarsAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private async Task ImportCsvAsync()", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var method = source[start..end];

        var rememberEnabled = method.IndexOf("var wasEnabled = IsEnabled;", StringComparison.Ordinal);
        var disable = method.IndexOf("IsEnabled = false;", StringComparison.Ordinal);
        var restore = method.IndexOf("await _viewModel.RestoreAllCalendarsAsync(dialog.FileName);", StringComparison.Ordinal);
        var finallyIndex = method.IndexOf("finally", restore, StringComparison.Ordinal);
        var reenable = method.IndexOf("IsEnabled = wasEnabled;", finallyIndex, StringComparison.Ordinal);
        var successMessage = method.IndexOf("\"リストア完了\"", StringComparison.Ordinal);
        var failureMessage = method.IndexOf("\"リストア失敗\"", StringComparison.Ordinal);

        Assert.True(rememberEnabled >= 0
                    && disable > rememberEnabled
                    && restore > disable
                    && finallyIndex > restore
                    && reenable > finallyIndex,
            "MainWindow input must remain disabled around the awaited database restore and be restored in finally.");
        Assert.True(successMessage > reenable && failureMessage > reenable,
            "Completion/error dialogs should be shown only after the owner window has been re-enabled.");
    }
}

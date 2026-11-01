namespace FavGCalSchedulerClone.Tests;

public sealed class SyncDialogUiRegressionTests
{
    [Fact]
    public async Task SyncPreview_ExplainsDirectionAndConflictPolicyBeforeExecution()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("SyncDialogs.cs"));

        Assert.Contains("preview.ConflictPolicy", code);
        Assert.Contains("Googleへ送信", code);
        Assert.Contains("Google からローカルへ取得", code);
        Assert.Contains("競合時の扱い", code);
    }

    [Fact]
    public async Task DiagnosticsDialog_ReportsPartialSyncResultsInsteadOfUnconditionalSuccess()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("SyncDialogs.cs"));

        Assert.Contains("result.Failed", code);
        Assert.Contains("result.Conflicts", code);
        Assert.Contains("result.Message", code);
        Assert.Contains("診断情報の再読み込みに失敗しました", code);
    }

    [Fact]
    public async Task DiagnosticsDialog_DisablesConflictingActionsWhileOperationRuns()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("SyncDialogs.cs"));

        Assert.Contains("syncOperationInProgress", code);
        Assert.Contains("!syncOperationInProgress &&", code);
        Assert.Contains("eventArgs.Cancel = true", code);
    }

    [Fact]
    public async Task SettingsDialog_DisablesGoogleActionsWhileOperationRuns()
    {
        var code = await File.ReadAllTextAsync(DialogSourcePath("SettingsDialog.cs"));

        Assert.Contains("googleOperationButtons", code);
        Assert.Contains("googleOperationInProgress", code);
        Assert.Contains("button.IsEnabled = false", code);
        Assert.Contains("button.IsEnabled = true", code);
        Assert.Contains("eventArgs.Cancel = true", code);
    }

    private static string DialogSourcePath(string fileName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App",
        "Views",
        "Dialogs",
        fileName));
}

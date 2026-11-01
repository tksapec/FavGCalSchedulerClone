namespace FavGCalSchedulerClone.Tests;

public sealed class UiWorkflowRegressionTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task SearchDialog_ChangingEitherDateForcesCustomRange()
    {
        var source = await ReadAppFileAsync("Views", "Dialogs", "SearchDialog.cs");

        Assert.Contains("start.SelectedDateChanged", source, StringComparison.Ordinal);
        Assert.Contains("end.SelectedDateChanged", source, StringComparison.Ordinal);
        Assert.Contains("range.SelectedValue = EventSearchRange.Custom", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editors_AskBeforeDiscardingUnsavedChangesOnClose()
    {
        foreach (var file in new[]
                 {
                     new[] { "Views", "Dialogs", "ScheduleEditorDialog.cs" },
                     new[] { "Views", "Dialogs", "TodoEditorDialog.cs" }
                 })
        {
            var source = await ReadAppFileAsync(file);
            Assert.Contains("UnsavedChangesCloseGuard", source, StringComparison.Ordinal);
            Assert.Contains("closeGuard.Cancel()", source, StringComparison.Ordinal);
        }

        var guard = await ReadAppFileAsync("Views", "Dialogs", "UnsavedChangesCloseGuard.cs");
        Assert.Contains("window.Closing", guard, StringComparison.Ordinal);
        Assert.Contains("MessageBoxButton.YesNo", guard, StringComparison.Ordinal);
        Assert.Contains("破棄して閉じますか", guard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TodoLists_ExposeRemainingCountsAndCanExpandBeyondOneHundred()
    {
        var viewModel = await ReadAppFileAsync("ViewModels", "MainViewModel.Todo.cs");
        var window = await ReadAppFileAsync("MainWindow.xaml.cs");

        Assert.Contains("TodoEventsRemainingCount", viewModel, StringComparison.Ordinal);
        Assert.Contains("CompletedTodoEventsRemainingCount", viewModel, StringComparison.Ordinal);
        Assert.Contains("ToggleIncompleteTodoListLimitCommand", viewModel, StringComparison.Ordinal);
        Assert.Contains("ToggleCompletedTodoListLimitCommand", viewModel, StringComparison.Ordinal);
        Assert.Contains("TodoEventsLimitButtonText", window, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrayCloseNotice_IsSavedAfterFirstDisplay()
    {
        var settings = await ReadAppFileAsync("Models", "AppSettings.cs");
        var window = await ReadAppFileAsync("MainWindow.TrayCloseNotification.cs");
        var windowCodeBehind = await ReadAppFileAsync("MainWindow.xaml.cs");
        var viewModel = await ReadAppFileAsync("ViewModels", "MainViewModel.TrayCloseNotification.cs");

        Assert.Contains("HasShownTrayCloseNotification", settings, StringComparison.Ordinal);
        Assert.Contains("MessageBox.Show", window, StringComparison.Ordinal);
        Assert.Contains("MarkTrayCloseNotificationShownAsync", window, StringComparison.Ordinal);
        Assert.Contains("NotifyFirstTrayClose();", windowCodeBehind, StringComparison.Ordinal);
        Assert.Contains("PersistSettingsAsync", viewModel, StringComparison.Ordinal);
    }

    private static Task<string> ReadAppFileAsync(params string[] pathSegments) =>
        File.ReadAllTextAsync(Path.Combine(AppRoot, Path.Combine(pathSegments)));
}

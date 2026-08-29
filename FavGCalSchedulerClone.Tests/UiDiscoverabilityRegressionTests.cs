namespace FavGCalSchedulerClone.Tests;

public sealed class UiDiscoverabilityRegressionTests
{
    private static readonly string AppDirectory = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task MainMenu_ShowsExistingKeyboardShortcutsAndSearchHint()
    {
        var xaml = await File.ReadAllTextAsync(Path.Combine(AppDirectory, "MainWindow.xaml"));

        Assert.Contains("InputGestureText=\"Ctrl+N\" Command=\"{Binding AddScheduleCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("InputGestureText=\"Ctrl+Shift+N\" Command=\"{Binding AddTodoCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"詳細検索(_S)...\"", xaml, StringComparison.Ordinal);
        Assert.Contains("クイック検索欄へはCtrl+F", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsColorGrid_LabelsTheColorIdColumn()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppDirectory, "Views", "Dialogs", "SettingsDialog.cs"));

        Assert.Contains("AddText(colorGrid, \"ID\", 0, 1);", source, StringComparison.Ordinal);
    }
}

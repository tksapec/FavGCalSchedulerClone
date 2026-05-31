using System.Text.RegularExpressions;

namespace FavGCalSchedulerClone.Tests;

public sealed partial class MainWindowMenuTests
{
    private static readonly string MainWindowXamlPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App",
        "MainWindow.xaml"));

    [Fact]
    public async Task MainMenu_DoesNotExposePrintOrDisabledItems()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.DoesNotContain("印刷", xaml);
        Assert.DoesNotContain("PrintMenu_Click", xaml);
        Assert.DoesNotContain("PrintPreviewMenu_Click", xaml);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml);
    }

    [Theory]
    [InlineData("F5", "ShowMonthViewCommand")]
    [InlineData("F6", "ShowWeekViewCommand")]
    [InlineData("F7", "ShowDayViewCommand")]
    [InlineData("Home", "TodayCommand")]
    [InlineData("PageUp", "PreviousMonthCommand")]
    [InlineData("PageDown", "NextMonthCommand")]
    [InlineData("F8", "SyncCommand")]
    public async Task MainWindow_DefinesFunctionalKeyBindingForDisplayedShortcut(string key, string command)
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);

        Assert.Contains($"<KeyBinding Key=\"{key}\" Command=\"{{Binding {command}}}\" />", xaml);
    }

    [Fact]
    public async Task MainMenu_ClickHandlersExistInCodeBehind()
    {
        var xaml = await File.ReadAllTextAsync(MainWindowXamlPath);
        var codeBehindPath = Path.ChangeExtension(MainWindowXamlPath, ".xaml.cs");
        var codeBehind = await File.ReadAllTextAsync(codeBehindPath);
        var handlers = ClickHandlerRegex()
            .Matches(xaml)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(handlers, handler => Assert.Contains($"{handler}(", codeBehind));
    }

    [Fact]
    public async Task MainWindow_CodeBehindDelegatesEditorDialogs()
    {
        var codeBehindPath = Path.ChangeExtension(MainWindowXamlPath, ".xaml.cs");
        var codeBehind = await File.ReadAllTextAsync(codeBehindPath);

        Assert.Contains("ScheduleEditorDialog.Show(", codeBehind);
        Assert.Contains("TodoEditorDialog.Show(", codeBehind);
        Assert.Contains("SettingsDialog.ShowAsync(", codeBehind);
        Assert.DoesNotContain("AddTodoEditorLayout(", codeBehind);
        Assert.DoesNotContain("CreateColorComboBox(", codeBehind);
        Assert.DoesNotContain("CreateEditorDialogRoot(", codeBehind);
    }

    [GeneratedRegex("Click=\"([^\"]+)\"")]
    private static partial Regex ClickHandlerRegex();
}

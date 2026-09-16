namespace FavGCalSchedulerClone.Tests;

public sealed class DescriptionClipboardRegressionTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task ScheduleAndTodoDescriptionEditors_UseNativeMultilineTextEditingBehavior()
    {
        var schedule = await File.ReadAllTextAsync(Path.Combine(AppRoot, "Views", "Dialogs", "ScheduleEditorDialog.cs"));
        var todo = await File.ReadAllTextAsync(Path.Combine(AppRoot, "Views", "Dialogs", "TodoEditorDialog.cs"));
        var behavior = await File.ReadAllTextAsync(Path.Combine(AppRoot, "Views", "Dialogs", "TextEditingBehavior.cs"));

        Assert.Contains("AcceptsReturn = true", schedule, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = true", todo, StringComparison.Ordinal);
        Assert.Contains("TextEditingBehavior.Attach(description);", schedule, StringComparison.Ordinal);
        Assert.Contains("TextEditingBehavior.Attach(description);", todo, StringComparison.Ordinal);
        Assert.Contains("textBox is TextBox { AcceptsReturn: true }", behavior, StringComparison.Ordinal);
        Assert.Contains("Keep WPF's native multiline editing behavior", behavior, StringComparison.Ordinal);
    }
}

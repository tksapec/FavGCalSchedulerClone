namespace FavGCalSchedulerClone.Tests;

public sealed class DescriptionClipboardRegressionTests
{
    private static readonly string AppRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App"));

    [Fact]
    public async Task ScheduleAndTodoDescriptionEditors_KeepNativeMultilineTextEditingBehavior()
    {
        var schedule = await File.ReadAllTextAsync(Path.Combine(AppRoot, "Views", "Dialogs", "ScheduleEditorDialog.cs"));
        var todo = await File.ReadAllTextAsync(Path.Combine(AppRoot, "Views", "Dialogs", "TodoEditorDialog.cs"));

        Assert.DoesNotContain("TextEditingBehavior.Attach(description);", schedule, StringComparison.Ordinal);
        Assert.DoesNotContain("TextEditingBehavior.Attach(description);", todo, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = true", schedule, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = true", todo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScheduleSave_PreservesNonBlankDescriptionWhitespace()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppRoot, "ViewModels", "MainViewModel.EventEditing.cs"));

        Assert.Contains(
            "calendarEvent.Description = string.IsNullOrWhiteSpace(Description) ? null : Description;",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "calendarEvent.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();",
            source,
            StringComparison.Ordinal);
    }
}

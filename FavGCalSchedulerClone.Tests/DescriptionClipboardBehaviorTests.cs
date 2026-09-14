namespace FavGCalSchedulerClone.Tests;

public sealed class DescriptionClipboardBehaviorTests
{
    private static readonly string DialogRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "FavGCalSchedulerClone.App", "Views", "Dialogs"));

    [Fact]
    public async Task ScheduleDescription_UsesNativeTextBoxClipboardBehavior()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(DialogRoot, "ScheduleEditorDialog.cs"));
        var block = ExtractDescriptionBlock(source);

        Assert.DoesNotContain("TextEditingBehavior.Attach(description);", block, StringComparison.Ordinal);
        Assert.DoesNotContain("description.ContextMenu", block, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TodoDescription_UsesNativeTextBoxClipboardBehavior()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(DialogRoot, "TodoEditorDialog.cs"));
        var block = ExtractDescriptionBlock(source);

        Assert.DoesNotContain("TextEditingBehavior.Attach(description);", block, StringComparison.Ordinal);
        Assert.DoesNotContain("description.ContextMenu", block, StringComparison.Ordinal);
    }

    private static string ExtractDescriptionBlock(string source)
    {
        const string startMarker = "var description = new TextBox";
        const string endMarker = "KeyboardNavigation.SetTabIndex";
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Description TextBox was not found.");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, "The description TextBox block end was not found.");
        return source[start..end];
    }
}

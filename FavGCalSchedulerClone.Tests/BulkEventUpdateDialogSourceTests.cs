namespace FavGCalSchedulerClone.Tests;

public sealed class BulkEventUpdateDialogSourceTests
{
    [Fact]
    public void ReminderCombo_UsesTheReminderOptionMinutesProperty()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "FavGCalSchedulerClone.App",
            "Views",
            "Dialogs",
            "BulkEventUpdateDialog.cs"));

        Assert.Contains("SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValuePath = \"Minutes\"", source, StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FavGCalSchedulerClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

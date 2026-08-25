namespace FavGCalSchedulerClone.Tests;

public sealed class BulkEventUpdateDialogSourceTests
{
    [Fact]
    public void ReminderCombo_UsesTheReminderOptionMinutesProperty()
    {
        var source = ReadSource();

        Assert.Contains("SelectedValuePath = nameof(ReminderOption.MinutesBeforeStart)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValuePath = \"Minutes\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReminderNone_RemainsApplicableAndExplicitlyDisablesBothReminderChannels()
    {
        var source = ReadSource();

        Assert.Contains("|| reminderEnabled.IsChecked == true;", source, StringComparison.Ordinal);
        Assert.Contains("var selectedReminderMinutes = minutes.SelectedValue as int?;", source, StringComparison.Ordinal);
        Assert.Contains("selectedReminderMinutes is null ? false : appReminder.IsChecked == true", source, StringComparison.Ordinal);
        Assert.Contains("selectedReminderMinutes is null ? false : emailReminder.IsChecked == true", source, StringComparison.Ordinal);
    }

    private static string ReadSource() => File.ReadAllText(Path.Combine(
        GetRepositoryRoot(),
        "FavGCalSchedulerClone.App",
        "Views",
        "Dialogs",
        "BulkEventUpdateDialog.cs"));

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

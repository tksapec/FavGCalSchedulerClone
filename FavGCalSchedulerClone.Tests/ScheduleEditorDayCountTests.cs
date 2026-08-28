using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class ScheduleEditorDayCountTests
{
    [Theory]
    [InlineData("3", 2, 10, 3)]
    [InlineData("0", 2, 10, 1)]
    [InlineData("-5", 2, 10, 1)]
    [InlineData("999", 2, 10, 10)]
    [InlineData("abc", 2, 10, 2)]
    [InlineData("", 2, 10, 2)]
    public void NormalizeDayCount_ClampsValidNumbersAndUsesFallbackForInvalidInput(
        string value,
        int fallback,
        int maximum,
        int expected)
    {
        Assert.Equal(expected, ScheduleEditorDialog.NormalizeDayCount(value, fallback, maximum));
    }

    [Fact]
    public void NormalizeDayCount_ClampsFallbackAndMaximumToUsableRange()
    {
        Assert.Equal(1, ScheduleEditorDialog.NormalizeDayCount("invalid", 0, 0));
        Assert.Equal(5, ScheduleEditorDialog.NormalizeDayCount("invalid", 99, 5));
    }

    [Fact]
    public async Task ScheduleEditor_UsesNormalizedDayCountWhenUpdatingEndDate()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            GetRepositoryRoot(),
            "FavGCalSchedulerClone.App",
            "Views",
            "Dialogs",
            "ScheduleEditorDialog.cs"));

        Assert.Contains("var days = NormalizeDayCount(dayCount.Text, fallbackDays, maximumDays);", source, StringComparison.Ordinal);
        Assert.Contains("endDate.SelectedDate = start.AddDays(days - 1);", source, StringComparison.Ordinal);
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

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
}

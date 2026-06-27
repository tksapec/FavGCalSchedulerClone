using FavGCalSchedulerClone.App.ViewModels;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class EditorTimeParserTests
{
    [Theory]
    [InlineData("1234", 12, 34)]
    [InlineData("0900", 9, 0)]
    [InlineData("0905", 9, 5)]
    [InlineData("12:34", 12, 34)]
    [InlineData("9:00", 9, 0)]
    public void TryParseEditorTime_AcceptsFourDigitsAndColonTime(string value, int hour, int minute)
    {
        Assert.True(MainViewModel.TryParseEditorTime(value, out var time));
        Assert.Equal(new TimeSpan(hour, minute, 0), time);
    }

    [Theory]
    [InlineData("900")]
    [InlineData("2460")]
    [InlineData("9999")]
    [InlineData("12:345")]
    [InlineData("")]
    public void TryParseEditorTime_RejectsInvalidValues(string value)
    {
        Assert.False(MainViewModel.TryParseEditorTime(value, out _));
    }

    [Theory]
    [InlineData("1234", "12:34")]
    [InlineData("0900", "09:00")]
    [InlineData("9:00", "09:00")]
    [InlineData("12:34", "12:34")]
    [InlineData("900", "900")]
    [InlineData("2460", "2460")]
    [InlineData("", "")]
    public void NormalizeTimeText_FormatsValidTimeWhenEditorLosesFocus(string value, string expected)
    {
        Assert.Equal(expected, ScheduleEditorDialog.NormalizeTimeText(value));
    }

    [Theory]
    [InlineData("09:00", 30, "09:30")]
    [InlineData("09:00", 60, "10:00")]
    [InlineData("09:00", 120, "11:00")]
    [InlineData("23:30", 60, "00:30")]
    [InlineData("0900", 30, "09:30")]
    public void TryCreateEndTimeFromDuration_UsesStartTimePlusDuration(
        string startTime,
        int durationMinutes,
        string expected)
    {
        Assert.True(ScheduleEditorDialog.TryCreateEndTimeFromDuration(startTime, TimeSpan.FromMinutes(durationMinutes), out var endTime));
        Assert.Equal(expected, endTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("900")]
    [InlineData("2460")]
    public void TryCreateEndTimeFromDuration_RejectsInvalidStartTime(string startTime)
    {
        Assert.False(ScheduleEditorDialog.TryCreateEndTimeFromDuration(startTime, TimeSpan.FromMinutes(30), out var endTime));
        Assert.Equal(startTime, endTime);
    }
}

using FavGCalSchedulerClone.App.ViewModels;

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
}

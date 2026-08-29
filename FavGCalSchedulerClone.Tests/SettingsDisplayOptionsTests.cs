using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class SettingsDisplayOptionsTests
{
    [Fact]
    public void SettingsDisplayOptions_UseJapaneseHumanReadableLabels()
    {
        Assert.Equal("月", SettingsDisplayOptions.Select(SettingsDisplayOptions.CalendarViewModes, CalendarViewMode.Month).Label);
        Assert.Equal("標準", SettingsDisplayOptions.Select(SettingsDisplayOptions.FontSizes, 2).Label);
        Assert.Equal("日本語 (月)", SettingsDisplayOptions.Select(SettingsDisplayOptions.WeekdayDisplayTypes, WeekdayDisplayType.JapaneseShort).Label);
        Assert.Equal("すべて", SettingsDisplayOptions.Select(SettingsDisplayOptions.TodoPeriods, 0).Label);
        Assert.Equal("Googleを優先", SettingsDisplayOptions.Select(SettingsDisplayOptions.ConflictPolicies, SyncConflictPolicy.PreferGoogle).Label);
    }

    [Fact]
    public void SettingsDisplayOptions_PreserveUnderlyingValues()
    {
        Assert.Equal(CalendarViewMode.Week, SettingsDisplayOptions.Select(SettingsDisplayOptions.CalendarViewModes, CalendarViewMode.Week).Value);
        Assert.Equal(3, SettingsDisplayOptions.Select(SettingsDisplayOptions.FontSizes, 3).Value);
        Assert.Equal(6, SettingsDisplayOptions.Select(SettingsDisplayOptions.TodoPeriods, 6).Value);
        Assert.Equal(SyncConflictPolicy.PreferLocal, SettingsDisplayOptions.Select(SettingsDisplayOptions.ConflictPolicies, SyncConflictPolicy.PreferLocal).Value);
    }
}

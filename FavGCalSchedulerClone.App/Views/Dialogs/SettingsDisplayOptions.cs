using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal sealed record SettingsDisplayOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

internal static class SettingsDisplayOptions
{
    public static IReadOnlyList<SettingsDisplayOption<CalendarViewMode>> CalendarViewModes { get; } =
    [
        new(CalendarViewMode.Month, "月"),
        new(CalendarViewMode.Week, "週"),
        new(CalendarViewMode.Day, "日")
    ];

    public static IReadOnlyList<SettingsDisplayOption<int>> FontSizes { get; } =
    [
        new(1, "小"),
        new(2, "標準"),
        new(3, "大")
    ];

    public static IReadOnlyList<SettingsDisplayOption<WeekdayDisplayType>> WeekdayDisplayTypes { get; } =
    [
        new(WeekdayDisplayType.EnglishFull, "英語 (Monday)"),
        new(WeekdayDisplayType.EnglishShort, "英語 (Mon)"),
        new(WeekdayDisplayType.JapaneseShort, "日本語 (月)")
    ];

    public static IReadOnlyList<SettingsDisplayOption<int>> TodoPeriods { get; } =
    [
        new(0, "すべて"),
        new(1, "1か月"),
        new(3, "3か月"),
        new(6, "6か月"),
        new(12, "12か月")
    ];

    public static IReadOnlyList<SettingsDisplayOption<SyncConflictPolicy>> ConflictPolicies { get; } =
    [
        new(SyncConflictPolicy.SkipLocalDirty, "ローカル変更を保持してスキップ"),
        new(SyncConflictPolicy.PreferLocal, "ローカルを優先"),
        new(SyncConflictPolicy.PreferGoogle, "Googleを優先")
    ];

    public static SettingsDisplayOption<T> Select<T>(IReadOnlyList<SettingsDisplayOption<T>> options, T value)
    {
        if (options.Count == 0)
        {
            throw new ArgumentException("At least one option is required.", nameof(options));
        }

        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value)) ?? options[0];
    }
}

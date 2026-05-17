namespace FavGCalSchedulerClone.App.Models;

public sealed record ReminderOption(string Label, int? MinutesBeforeStart)
{
    public static IReadOnlyList<ReminderOption> Defaults { get; } =
    [
        new("通知しない", null),
        new("開始時刻", 0),
        new("5分前", 5),
        new("10分前", 10),
        new("30分前", 30),
        new("1時間前", 60)
    ];
}

using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class BulkEventUpdatePreviewFormatterTests
{
    [Fact]
    public void Format_ShowsTargetCountDestinationAndEveryChangedField()
    {
        var preview = BulkEventUpdatePreviewFormatter.Format(
            new BulkEventUpdateRequest(
                CalendarId: "work-id",
                ColorId: "9",
                ReminderMinutesBeforeStart: 30,
                AppReminderEnabled: true,
                GoogleEmailReminderEnabled: false,
                UpdateColor: true),
            4,
            [new GoogleCalendarSelectionItem { Id = "work-id", Summary = "仕事" }]);

        Assert.Contains("対象: 4件", preview, StringComparison.Ordinal);
        Assert.Contains("変更先カレンダー: 仕事 (work-id)", preview, StringComparison.Ordinal);
        Assert.Contains("色: ID 9", preview, StringComparison.Ordinal);
        Assert.Contains("アプリ内 30分前 / Googleメール なし", preview, StringComparison.Ordinal);
    }
}

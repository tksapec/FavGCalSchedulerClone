using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarEventToolTipFormatterTests
{
    [Fact]
    public void Format_IncludesAvailableScheduleDetails()
    {
        var item = new CalendarEvent
        {
            CalendarId = "primary",
            Title = "会議",
            Location = "B202",
            Description = "資料確認",
            Start = new DateTimeOffset(2026, 5, 18, 8, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 5, 18, 8, 30, 0, TimeSpan.FromHours(9)),
            ReminderMinutesBeforeStart = 10
        };

        var text = CalendarEventToolTipFormatter.Format(item, "e1399068@gmail.com");

        Assert.Contains("e1399068@gmail.com", text);
        Assert.Contains("2026/05/18 08:00 - 2026/05/18 08:30", text);
        Assert.Contains("会議", text);
        Assert.Contains("B202", text);
        Assert.Contains("資料確認", text);
        Assert.Contains("10分前", text);
    }

    [Fact]
    public void Format_ShowsAllDayRangeAndTodoMetadataWithoutEmptyOptionalFields()
    {
        var item = new CalendarEvent
        {
            CalendarId = "todo",
            Title = "確認",
            Description = "#todoB56%",
            IsTodoLike = true,
            IsAllDay = true,
            Start = new DateTimeOffset(new DateTime(2026, 5, 11)),
            End = new DateTimeOffset(new DateTime(2026, 5, 14))
        };

        var text = CalendarEventToolTipFormatter.Format(item);

        Assert.Contains("2026/05/11 - 2026/05/13 (終日)", text);
        Assert.Contains("優先度 B / 進捗 56%", text);
        Assert.DoesNotContain("場所", text);
        Assert.DoesNotContain("通知", text);
    }

    [Fact]
    public void Format_HidesTodoMarkerFromDescriptionContent()
    {
        var item = new CalendarEvent
        {
            Title = "部屋の予約",
            Description = "#todoB56%\n利用者へ連絡\n資料を用意する",
            IsTodoLike = true,
            Start = new DateTimeOffset(2026, 5, 18, 8, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 5, 18, 8, 30, 0, TimeSpan.FromHours(9))
        };

        var text = CalendarEventToolTipFormatter.Format(item);

        Assert.DoesNotContain("#todo", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("内容", text);
        Assert.Contains("利用者へ連絡", text);
        Assert.Contains("資料を用意する", text);
        Assert.Contains("優先度 B / 進捗 56%", text);
    }
    [Fact]
    public void Format_IncludesGoogleEmailReminderWhenAvailable()
    {
        var item = new CalendarEvent
        {
            CalendarId = "primary",
            Title = "meeting",
            Start = new DateTimeOffset(2026, 5, 18, 8, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 5, 18, 8, 30, 0, TimeSpan.FromHours(9)),
            ReminderMinutesBeforeStart = 30,
            GoogleReminderMetadata = new GoogleReminderMetadata
            {
                EmailMinutes = [30]
            }
        };

        var text = CalendarEventToolTipFormatter.Format(item);

        Assert.Contains("Googleメール通知", text);
        Assert.Contains("30分前", text);
    }

    [Fact]
    public void Format_PreservesLongMultilineEventAndTodoContentForScrollableToolTips()
    {
        var description = string.Join("\n", Enumerable.Range(1, 8).Select(index => $"内容の行 {index}: {new string('説', 40)}"));
        var item = new CalendarEvent
        {
            Title = "詳細のあるToDo",
            Description = $"#todoA56%\n{description}",
            IsTodoLike = true,
            Start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(9)),
            IsAllDay = true
        };

        var text = CalendarEventToolTipFormatter.Format(item);

        Assert.Contains("内容の行 8", text);
        Assert.Contains(description.Split('\n')[7], text);
        Assert.Contains("優先度 A / 進捗 56%", text);
    }
}

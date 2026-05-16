using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class TagServiceTests
{
    [Fact]
    public void ExtractTags_ReturnsDistinctTagsFromTitleAndDescription()
    {
        var tags = TagService.ExtractTags("会議 #work #important", "memo #work #holiday");

        Assert.Equal(["#holiday", "#important", "#work"], tags);
    }

    [Fact]
    public void IsHoliday_DetectsHolidayTagInDescription()
    {
        var item = new CalendarEvent { Title = "振替休日", Description = "Fav互換 #holiday" };

        Assert.True(TagService.IsHoliday(item));
    }

    [Fact]
    public void IsTodoLike_DetectsFavGCalTodoMarker()
    {
        var item = new CalendarEvent { Title = "確認", Description = "#todoA56% 進捗管理" };

        Assert.True(TagService.IsTodoLike(item));
    }
}

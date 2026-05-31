using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Repositories;

public interface ITagRepository
{
    Task<IReadOnlyList<CalendarTag>> LoadTagsAsync();
    Task SaveTagAsync(CalendarTag tag);
}

namespace FavGCalSchedulerClone.App.Models;

public sealed class CalendarTag
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#E5E7EB";
    public bool IsVisible { get; set; } = true;
    public int Priority { get; set; }
}

using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleReminderMetadataTests
{
    [Fact]
    public void Clone_CopiesMutableLists()
    {
        var source = new GoogleReminderMetadata
        {
            UseDefault = true,
            PopupMinutes = [10],
            EmailMinutes = [20],
            DefaultPopupMinutes = [30],
            DefaultEmailMinutes = [40],
            AdoptedReminderMinutes = 10,
            AdoptedReminderMethod = "popup",
            Source = "explicit"
        };

        var clone = source.Clone();
        clone.PopupMinutes.Add(11);
        clone.EmailMinutes.Add(21);
        clone.DefaultPopupMinutes.Add(31);
        clone.DefaultEmailMinutes.Add(41);

        Assert.Equal([10], source.PopupMinutes);
        Assert.Equal([20], source.EmailMinutes);
        Assert.Equal([30], source.DefaultPopupMinutes);
        Assert.Equal([40], source.DefaultEmailMinutes);
        Assert.Equal([10, 11], clone.PopupMinutes);
        Assert.Equal([20, 21], clone.EmailMinutes);
        Assert.Equal([30, 31], clone.DefaultPopupMinutes);
        Assert.Equal([40, 41], clone.DefaultEmailMinutes);
        Assert.Equal(source.UseDefault, clone.UseDefault);
        Assert.Equal(source.AdoptedReminderMinutes, clone.AdoptedReminderMinutes);
        Assert.Equal(source.AdoptedReminderMethod, clone.AdoptedReminderMethod);
        Assert.Equal(source.Source, clone.Source);
    }
}

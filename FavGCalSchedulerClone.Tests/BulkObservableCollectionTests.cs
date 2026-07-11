using System.Collections.Specialized;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_RaisesOneResetNotification()
    {
        var items = new BulkObservableCollection<int> { 1 };
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        items.CollectionChanged += (_, args) => notifications.Add(args);

        items.ReplaceAll([2, 3]);

        Assert.Equal([2, 3], items);
        var notification = Assert.Single(notifications);
        Assert.Equal(NotifyCollectionChangedAction.Reset, notification.Action);
    }
}

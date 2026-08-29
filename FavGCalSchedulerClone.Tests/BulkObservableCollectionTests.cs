using System.Collections.Specialized;
using FavGCalSchedulerClone.App.Collections;
using FavGCalSchedulerClone.App.Models;

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

    [Fact]
    public void CalendarDay_ReplacementKeepsObservableCollectionContractAndBatchesNotifications()
    {
        var day = new CalendarDay();
        var notifications = new List<NotifyCollectionChangedEventArgs>();
        day.Events.CollectionChanged += (_, args) => notifications.Add(args);

        day.ReplaceEvents([
            new CalendarEvent { Title = "first" },
            new CalendarEvent { Title = "second" }
        ]);

        Assert.Equal(2, day.Events.Count);
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(notifications).Action);
    }
}

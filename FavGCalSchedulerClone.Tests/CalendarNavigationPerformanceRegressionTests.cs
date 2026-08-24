using System.Collections.Specialized;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarNavigationPerformanceRegressionTests
{
    [Fact]
    public async Task DistantMonthNavigation_ReplacesCalendarInBulkWithoutResettingOldChildCollections()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.NavigationRefreshDelay = TimeSpan.FromHours(1);
        var oldDays = viewModel.CalendarDays.ToArray();
        var calendarResets = 0;
        var visibleResets = 0;
        var oldEventResets = 0;
        var oldSegmentResets = 0;
        viewModel.CalendarDays.CollectionChanged += (_, e) => calendarResets += IsReset(e);
        viewModel.VisibleCalendarDays.CollectionChanged += (_, e) => visibleResets += IsReset(e);
        foreach (var day in oldDays)
        {
            day.Events.CollectionChanged += (_, e) => oldEventResets += IsReset(e);
            day.Segments.CollectionChanged += (_, e) => oldSegmentResets += IsReset(e);
        }

        viewModel.CurrentMonth = viewModel.CurrentMonth.AddYears(5);

        Assert.Equal(1, calendarResets);
        Assert.Equal(1, visibleResets);
        Assert.Equal(0, oldEventResets);
        Assert.Equal(0, oldSegmentResets);
        Assert.DoesNotContain(viewModel.CalendarDays, day => oldDays.Contains(day));
    }

    [Fact]
    public async Task YearNavigation_ReusesOverlappingSnapshotsWhenWideWindowRecenters()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() =>
            viewModel.CalendarCacheCount == MainViewModel.CalendarSnapshotCacheCapacity
            && viewModel.GetCalendarPrefetchRequirement(viewModel.CurrentMonth) == CalendarPrefetchRequirement.None);
        var prefetchedAfterYearMove = 0;
        viewModel.BeforePrefetchCalendarMonth = _ => Interlocked.Increment(ref prefetchedAfterYearMove);

        viewModel.NextYearCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.GetCalendarPrefetchRequirement(viewModel.CurrentMonth) == CalendarPrefetchRequirement.None);

        Assert.Equal(12, prefetchedAfterYearMove);
        Assert.Equal(MainViewModel.CalendarSnapshotCacheCapacity, viewModel.CalendarCacheCount);
    }

    private static int IsReset(NotifyCollectionChangedEventArgs args) =>
        args.Action == NotifyCollectionChangedAction.Reset ? 1 : 0;

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(8);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Condition was not met within the test timeout.");
            }

            await Task.Delay(20);
        }
    }
}

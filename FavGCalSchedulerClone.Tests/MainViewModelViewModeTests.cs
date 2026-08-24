using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class MainViewModelViewModeTests
{
    [Fact]
    public async Task ViewModeSwitch_ChangesVisibleCalendarDayCount()
    {
        var viewModel = await CreateViewModelAsync();

        Assert.True(viewModel.IsMonthView);
        Assert.Equal(42, viewModel.VisibleCalendarDays.Count);

        viewModel.CurrentViewMode = CalendarViewMode.Week;
        Assert.True(viewModel.IsWeekView);
        Assert.Equal(7, viewModel.VisibleCalendarDays.Count);

        viewModel.CurrentViewMode = CalendarViewMode.Day;
        Assert.True(viewModel.IsDayView);
        Assert.Single(viewModel.VisibleCalendarDays);
    }

    [Fact]
    public async Task GoToTodayAsync_InWeekView_SelectsTodayAndKeepsWeekVisible()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.CurrentViewMode = CalendarViewMode.Week;
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-2);

        await viewModel.GoToTodayAsync();

        Assert.True(viewModel.IsWeekView);
        Assert.NotNull(viewModel.SelectedDay);
        Assert.Equal(DateTime.Today, viewModel.SelectedDay.Date);
        Assert.Equal(7, viewModel.VisibleCalendarDays.Count);
        Assert.Contains(viewModel.VisibleCalendarDays, day => day.Date == DateTime.Today);
    }

    [Fact]
    public async Task GoToTodayAsync_InWeekViewFromDifferentWeek_SelectsTodayAfterVisibleDaysRefresh()
    {
        var viewModel = await CreateViewModelAsync();
        var previousWeek = DateTime.Today.AddDays(-14);
        viewModel.CurrentViewMode = CalendarViewMode.Week;
        await viewModel.NavigateToDateAsync(previousWeek);
        Assert.Equal(previousWeek.Date, viewModel.SelectedDay?.Date);

        await viewModel.GoToTodayAsync();

        Assert.True(viewModel.IsWeekView);
        Assert.NotNull(viewModel.SelectedDay);
        Assert.Equal(DateTime.Today, viewModel.SelectedDay.Date);
        Assert.Equal(7, viewModel.VisibleCalendarDays.Count);
        Assert.Contains(viewModel.VisibleCalendarDays, day => day.Date == DateTime.Today);
    }

    [Fact]
    public async Task GoToTodayAsync_InWeekViewWithMondayStart_UsesWeekContainingToday()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings { WeekStartsOnMonday = true });
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.CurrentViewMode = CalendarViewMode.Week;

        await viewModel.GoToTodayAsync();

        var expectedStart = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
        Assert.Equal(DateTime.Today, viewModel.SelectedDay?.Date);
        Assert.Equal(expectedStart, viewModel.VisibleCalendarDays.First().Date);
        Assert.Contains(viewModel.VisibleCalendarDays, day => day.Date == DateTime.Today);
    }

    [Fact]
    public async Task NavigateToDateAsync_InWeekView_SelectsTargetAndKeepsWeekVisible()
    {
        var viewModel = await CreateViewModelAsync();
        var target = DateTime.Today.AddMonths(1).AddDays(3);
        viewModel.CurrentViewMode = CalendarViewMode.Week;

        await viewModel.NavigateToDateAsync(target);

        Assert.Equal(target.Date, viewModel.SelectedDay?.Date);
        Assert.Equal(7, viewModel.VisibleCalendarDays.Count);
        Assert.Contains(viewModel.VisibleCalendarDays, day => day.Date == target.Date);
    }

    [Fact]
    public async Task NavigationCommands_UseWeekAndDayUnits()
    {
        var viewModel = await CreateViewModelAsync();
        var baseline = new DateTime(2026, 5, 15);

        viewModel.CurrentMonth = baseline;
        await Task.Delay(50);
        viewModel.SelectedDay = viewModel.CalendarDays.First(day => day.Date == baseline);

        viewModel.CurrentViewMode = CalendarViewMode.Week;
        viewModel.PreviousMonthCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal(baseline.AddDays(-7), viewModel.SelectedDay?.Date);

        viewModel.CurrentViewMode = CalendarViewMode.Day;
        viewModel.NextMonthCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal(baseline.AddDays(-6), viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task NavigationCommands_UpdateCurrentMonthImmediatelyForRapidClicks()
    {
        var viewModel = await CreateViewModelAsync();
        var selectedBaseline = new DateTime(2026, 5, 15);
        await viewModel.NavigateToDateAsync(selectedBaseline);
        var baseline = viewModel.CurrentMonth;
        var loadCount = 0;
        var saveCount = 0;
        viewModel.NavigationRefreshDelay = TimeSpan.FromMilliseconds(200);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeSaveDisplayMonth = _ => saveCount++;
        viewModel.BeforeBuildCalendarSnapshot = (month, token) =>
        {
            if (month == baseline.AddMonths(3))
            {
                loadCount++;
                release.Task.Wait(token);
            }
        };

        viewModel.NextMonthCommand.Execute(null);
        viewModel.NextMonthCommand.Execute(null);
        viewModel.NextMonthCommand.Execute(null);

        Assert.Equal(baseline.AddMonths(3), viewModel.CurrentMonth);

        viewModel.PreviousYearCommand.Execute(null);
        viewModel.NextYearCommand.Execute(null);

        Assert.Equal(baseline.AddMonths(3), viewModel.CurrentMonth);
        Assert.Equal(0, loadCount);
        Assert.Equal(0, saveCount);
        release.SetResult();
        await WaitUntilAsync(() => saveCount == 1);
        await WaitUntilAsync(() => viewModel.SelectedDay?.Date == selectedBaseline.AddMonths(3).Date);
    }

    [Fact]
    public async Task NavigationCommands_DefaultRefreshDelayIsShortForResponsiveClicks()
    {
        var viewModel = await CreateViewModelAsync();

        Assert.True(viewModel.NavigationRefreshDelay <= TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task NavigationCommands_DebounceDisplayMonthPersistenceToLatestMonth()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var baseline = viewModel.CurrentMonth;
        var saveCount = 0;
        viewModel.BeforeSaveDisplayMonth = _ => saveCount++;

        viewModel.NextMonthCommand.Execute(null);
        viewModel.NextMonthCommand.Execute(null);
        viewModel.NextMonthCommand.Execute(null);

        Assert.Equal(0, saveCount);
        await viewModel.FlushDisplayMonthPersistenceAsync();

        Assert.Equal(1, saveCount);
        var reloaded = await repository.LoadSettingsAsync();
        Assert.Equal(baseline.AddMonths(3), reloaded.DisplayMonth);
    }

    [Fact]
    public async Task DisplayMonthPersistence_DoesNotLetCapturedDelayedSnapshotOverwriteNewerInteractiveSave()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var staleSettings = viewModel.CreateSettingsSnapshot();
        staleSettings.WindowOpacity = 123;
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDisplayMonthSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeSaveDisplayMonthAsync = async _ =>
        {
            saveStarted.SetResult();
            await releaseDisplayMonthSave.Task.ConfigureAwait(false);
        };

        var expectedMonth = viewModel.CurrentMonth.AddMonths(1);
        viewModel.NextMonthCommand.Execute(null);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var interactiveSave = viewModel.SaveApplicationSettingsAsync(staleSettings);
        await interactiveSave.WaitAsync(TimeSpan.FromSeconds(2));
        releaseDisplayMonthSave.SetResult();
        await Task.Delay(100);

        var persisted = await repository.LoadSettingsAsync();
        Assert.Equal(expectedMonth, persisted.DisplayMonth);
        Assert.Equal(123, persisted.WindowOpacity);
    }

    [Fact]
    public async Task DisplayMonthPersistence_SnapshotsMutableSettingListsBeforeBackgroundSave()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var settings = viewModel.CreateSettingsSnapshot();
        settings.VisibleCalendarIds = ["primary", "team"];
        settings.EventColorSettings = [new EventColorSetting { ColorId = "11", Label = "Blue" }];

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeSaveDisplayMonthAsync = async _ =>
        {
            started.SetResult();
            await release.Task.ConfigureAwait(false);
        };

        viewModel.NextMonthCommand.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var save = viewModel.SaveApplicationSettingsAsync(settings);
        release.SetResult();
        await save;
        await viewModel.FlushDisplayMonthPersistenceAsync();

        var persisted = await repository.LoadSettingsAsync();
        Assert.Equal(["primary", "team"], persisted.VisibleCalendarIds);
        Assert.Single(persisted.EventColorSettings);
        Assert.Equal("11", persisted.EventColorSettings[0].ColorId);
        Assert.Equal(viewModel.CurrentMonth, persisted.DisplayMonth);
    }

    [Fact]
    public async Task SaveApplicationSettingsAsync_DoesNotRetainCallerOwnedMutableSettings()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var settings = viewModel.CreateSettingsSnapshot();
        settings.VisibleCalendarIds = ["primary", "team"];
        settings.EventColorSettings = [new EventColorSetting { ColorId = "11", Label = "Blue" }];

        await viewModel.SaveApplicationSettingsAsync(settings);

        settings.VisibleCalendarIds.Add("later");
        settings.EventColorSettings[0].Label = "Mutated";

        var current = viewModel.CreateSettingsSnapshot();
        var persisted = await repository.LoadSettingsAsync();
        Assert.Equal(["primary", "team"], current.VisibleCalendarIds);
        Assert.Equal("Blue", Assert.Single(current.EventColorSettings).Label);
        Assert.Equal(["primary", "team"], persisted.VisibleCalendarIds);
        Assert.Equal("Blue", Assert.Single(persisted.EventColorSettings).Label);
    }

    [Fact]
    public async Task FlushDisplayMonthPersistenceAsync_CompletesWhenInflightSaveStartedOnBlockedSynchronizationContext()
    {
        var viewModel = await CreateViewModelAsync();
        using var saveStarted = new ManualResetEventSlim();
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeSaveDisplayMonthAsync = async _ =>
        {
            saveStarted.Set();
            await releaseSave.Task.ConfigureAwait(false);
        };

        var originalContext = SynchronizationContext.Current;
        var blockedContext = new QueuedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(blockedContext);
        try
        {
            viewModel.NextMonthCommand.Execute(null);

            Assert.True(SpinWait.SpinUntil(() => blockedContext.PendingCount > 0, TimeSpan.FromSeconds(2)));
            Assert.True(blockedContext.PumpUntil(() => saveStarted.IsSet, TimeSpan.FromSeconds(2)));

            var flushTask = Task.Run(viewModel.FlushDisplayMonthPersistenceAsync);
            releaseSave.SetResult();

#pragma warning disable xUnit1031 // This intentionally models App.OnExit synchronously waiting on the dispatcher thread.
            Assert.True(flushTask.Wait(TimeSpan.FromSeconds(2)), "Exit flush must not require the blocked UI synchronization context.");
#pragma warning restore xUnit1031
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task CachedNavigation_AppliesSnapshotOnlyOnce()
    {
        var viewModel = await CreateViewModelAsync();
        var baseline = viewModel.CurrentMonth;
        await viewModel.NavigateToDateAsync(baseline.AddMonths(1));
        await WaitUntilAsync(() => viewModel.IsCalendarMonthCached(baseline.AddMonths(1)));
        await viewModel.NavigateToDateAsync(baseline);
        var applyCount = 0;
        viewModel.BeforeApplyCalendarSnapshot = _ => applyCount++;

        viewModel.CurrentMonth = baseline.AddMonths(1);
        await Task.Delay(viewModel.NavigationRefreshDelay + TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, applyCount);
    }

    [Fact]
    public async Task CachedNavigation_ConsumesPendingSelectionBeforeSubsequentManualSelection()
    {
        var viewModel = await CreateViewModelAsync();
        var baseline = new DateTime(2026, 5, 15);
        await viewModel.NavigateToDateAsync(baseline);

        await viewModel.NavigateToDateAsync(baseline.AddDays(-7));
        viewModel.SelectedDay = viewModel.CalendarDays.Single(day => day.Date == baseline);
        viewModel.CurrentViewMode = CalendarViewMode.Week;

        viewModel.NextMonthCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.SelectedDay?.Date == baseline.AddDays(7));

        Assert.Equal(baseline.AddDays(7), viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task NavigationCommands_UsePendingDateForRapidWeekAndDayClicks()
    {
        var viewModel = await CreateViewModelAsync();
        var baseline = new DateTime(2026, 5, 15);
        viewModel.NavigationRefreshDelay = TimeSpan.FromMilliseconds(200);

        viewModel.CurrentMonth = baseline;
        await Task.Delay(50);
        viewModel.SelectedDay = viewModel.CalendarDays.First(day => day.Date == baseline);

        viewModel.CurrentViewMode = CalendarViewMode.Week;
        viewModel.NextMonthCommand.Execute(null);
        viewModel.NextMonthCommand.Execute(null);
        Assert.Equal(baseline.AddDays(14).Date, viewModel.SelectedDay?.Date);

        viewModel.CurrentViewMode = CalendarViewMode.Day;
        viewModel.NextMonthCommand.Execute(null);
        Assert.Equal(baseline.AddDays(15).Date, viewModel.SelectedDay?.Date);

        await Task.Delay(viewModel.NavigationRefreshDelay + TimeSpan.FromMilliseconds(100));
        await WaitUntilAsync(() => viewModel.SelectedDay?.Date == baseline.AddDays(15).Date);
    }

    [Fact]
    public async Task NavigationRefresh_DoesNotApplyCanceledOlderLoad()
    {
        var firstTarget = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(24);
        var secondTarget = firstTarget.AddMonths(1);
        var viewModel = await CreateViewModelAsync([
            CreateEvent("first target", firstTarget.AddDays(4)),
            CreateEvent("second target", secondTarget.AddDays(4))
        ]);
        viewModel.NavigationRefreshDelay = TimeSpan.Zero;
        var firstLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeBuildCalendarSnapshot = (month, token) =>
        {
            if (month.Year == firstTarget.Year && month.Month == firstTarget.Month)
            {
                firstLoadStarted.TrySetResult();
                releaseFirstLoad.Task.Wait(token);
            }
        };

        viewModel.CurrentMonth = firstTarget;
        await firstLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CurrentMonth = secondTarget;

        await WaitUntilAsync(() => viewModel.CalendarDays.Any(day => day.Events.Any(item => item.Title == "second target")));
        Assert.Equal(secondTarget, viewModel.CurrentMonth);
        Assert.DoesNotContain(viewModel.CalendarDays.SelectMany(day => day.Events), item => item.Title == "first target");

        releaseFirstLoad.SetResult();
        await Task.Delay(100);

        Assert.Equal(secondTarget, viewModel.CurrentMonth);
        Assert.Contains(viewModel.CalendarDays.SelectMany(day => day.Events), item => item.Title == "second target");
        Assert.DoesNotContain(viewModel.CalendarDays.SelectMany(day => day.Events), item => item.Title == "first target");
    }

    [Fact]
    public async Task NavigationRefresh_CancelsOlderSnapshotBuild()
    {
        var firstTarget = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(24);
        var secondTarget = firstTarget.AddMonths(1);
        var viewModel = await CreateViewModelAsync([
            CreateEvent("first build target", firstTarget.AddDays(4)),
            CreateEvent("second build target", secondTarget.AddDays(4))
        ]);
        viewModel.NavigationRefreshDelay = TimeSpan.Zero;
        var firstBuildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstBuildCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeBuildCalendarSnapshot = (month, token) =>
        {
            if (month.Year != firstTarget.Year || month.Month != firstTarget.Month)
            {
                return;
            }

            firstBuildStarted.TrySetResult();
            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(10);
            }

            firstBuildCanceled.TrySetResult();
            token.ThrowIfCancellationRequested();
        };

        viewModel.CurrentMonth = firstTarget;
        await firstBuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CurrentMonth = secondTarget;

        await firstBuildCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.CalendarDays.Any(day => day.Events.Any(item => item.Title == "second build target")));
        Assert.Equal(secondTarget, viewModel.CurrentMonth);
        Assert.DoesNotContain(viewModel.CalendarDays.SelectMany(day => day.Events), item => item.Title == "first build target");
    }

    [Fact]
    public async Task Navigation_UsesCachedMonthImmediatelyWhenReturning()
    {
        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var viewModel = await CreateViewModelAsync([
            CreateEvent("cached current", currentMonth.AddDays(2))
        ]);
        await WaitUntilAsync(() => viewModel.CalendarDays.Any(day => day.Events.Any(item => item.Title == "cached current")));
        var loadCount = 0;
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            loadCount++;
            return Task.CompletedTask;
        };

        viewModel.NextMonthCommand.Execute(null);
        viewModel.PreviousMonthCommand.Execute(null);

        Assert.Equal(currentMonth, viewModel.CurrentMonth);
        Assert.Contains(viewModel.CalendarDays.SelectMany(day => day.Events), item => item.Title == "cached current");

        await Task.Delay(viewModel.NavigationRefreshDelay + TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, loadCount);
    }

    [Fact]
    public async Task Navigation_NearbyCachedMonthResumesSnapshotsWithoutRebuildingTheWideDataWindow()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount == MainViewModel.CalendarSnapshotCacheCapacity);
        var prefetchLoads = 0;
        var prefetchBuilds = 0;
        viewModel.BeforeLoadCalendarPrefetchDataAsync = (_, _) =>
        {
            prefetchLoads++;
            return Task.CompletedTask;
        };
        viewModel.BeforeBuildCalendarPrefetchDataWindow = (_, _) => prefetchBuilds++;

        viewModel.NextMonthCommand.Execute(null);
        await Task.Delay(300);

        Assert.Equal(0, prefetchLoads);
        Assert.Equal(0, prefetchBuilds);
    }

    [Fact]
    public async Task Prefetch_CompletedDataWindowAndSnapshotsRequireNoFurtherWork()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount == MainViewModel.CalendarSnapshotCacheCapacity);

        Assert.Equal(CalendarPrefetchRequirement.None, viewModel.GetCalendarPrefetchRequirement(viewModel.CurrentMonth));
    }

    [Fact]
    public async Task Navigation_ReturningToCachedMonthResumesInterruptedSnapshotPrefetchWithoutRebuildingWindow()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository))
        {
            NavigationRefreshDelay = TimeSpan.Zero
        };
        var center = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var blockedMonth = center.AddMonths(-1);
        var firstSnapshotStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumedSnapshotStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshotBuilds = 0;
        var prefetchLoads = 0;
        var wideWindowBuilds = 0;
        viewModel.BeforeLoadCalendarPrefetchDataAsync = (_, _) =>
        {
            Interlocked.Increment(ref prefetchLoads);
            return Task.CompletedTask;
        };
        viewModel.BeforeBuildCalendarPrefetchDataWindow = (_, _) => Interlocked.Increment(ref wideWindowBuilds);
        viewModel.BeforePrefetchCalendarMonth = month =>
        {
            if (month.Year != blockedMonth.Year || month.Month != blockedMonth.Month)
            {
                return;
            }

            if (Interlocked.Increment(ref snapshotBuilds) == 1)
            {
                firstSnapshotStarted.TrySetResult();
                releaseFirstSnapshot.Task.Wait();
            }
            else
            {
                resumedSnapshotStarted.TrySetResult();
            }
        };

        await viewModel.InitializeAsync();
        await firstSnapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CurrentMonth = center.AddMonths(1);
        releaseFirstSnapshot.SetResult();
        viewModel.CurrentMonth = center;

        await resumedSnapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.CalendarCacheCount == MainViewModel.CalendarSnapshotCacheCapacity);

        Assert.Equal(1, wideWindowBuilds);
        Assert.Equal(1, prefetchLoads);
    }

    [Fact]
    public async Task Navigation_PrefetchPipelinesDoNotOverlapWhenCanceledBuildIsSlow()
    {
        var baseline = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var firstTarget = baseline.AddMonths(24);
        var secondTarget = firstTarget.AddMonths(2);
        var viewModel = await CreateViewModelAsync();
        viewModel.NavigationRefreshDelay = TimeSpan.Zero;
        var firstPrefetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPrefetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPrefetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTargetBuilds = 0;
        var secondTargetBuilds = 0;
        viewModel.BeforeBuildCalendarSnapshot = (month, _) =>
        {
            if (month.Year == firstTarget.Year && month.Month == firstTarget.Month
                && Interlocked.Increment(ref firstTargetBuilds) == 2)
            {
                firstPrefetchStarted.TrySetResult();
                releaseFirstPrefetch.Task.Wait();
            }

            if (month.Year == secondTarget.Year && month.Month == secondTarget.Month
                && Interlocked.Increment(ref secondTargetBuilds) == 2)
            {
                secondPrefetchStarted.TrySetResult();
            }
        };

        viewModel.CurrentMonth = firstTarget;
        await firstPrefetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CurrentMonth = secondTarget;

        var secondStartedWhileFirstWasBlocked = await Task.WhenAny(
            secondPrefetchStarted.Task,
            Task.Delay(300)) == secondPrefetchStarted.Task;
        releaseFirstPrefetch.SetResult();
        await secondPrefetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(secondStartedWhileFirstWasBlocked);
    }

    [Fact]
    public async Task Navigation_KeepsCalendarCacheAcrossMonthMoves()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount > 0);
        var cachedCount = viewModel.CalendarCacheCount;

        viewModel.NextMonthCommand.Execute(null);
        viewModel.PreviousMonthCommand.Execute(null);

        Assert.True(viewModel.CalendarCacheCount >= cachedCount);
    }

    [Fact]
    public async Task Navigation_ReusesCalendarDataWindowForNearbyMonths()
    {
        var viewModel = await CreateViewModelAsync();
        var databaseLoads = 0;
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            databaseLoads++;
            return Task.CompletedTask;
        };

        await viewModel.NavigateToDateAsync(viewModel.CurrentMonth.AddMonths(6));

        Assert.Equal(0, databaseLoads);
    }

    [Fact]
    public async Task Prefetch_RebuildsDataWindowOnlyAfterMovingBeyondSixMonthsFromItsCenter()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount == MainViewModel.CalendarSnapshotCacheCapacity);

        Assert.Equal(CalendarPrefetchRequirement.ResumeSnapshots, viewModel.GetCalendarPrefetchRequirement(viewModel.CurrentMonth.AddMonths(6)));
        Assert.Equal(CalendarPrefetchRequirement.RebuildDataWindow, viewModel.GetCalendarPrefetchRequirement(viewModel.CurrentMonth.AddMonths(7)));
    }

    [Fact]
    public void CalendarSnapshotCache_HasWindowSizedCapacity()
    {
        Assert.Equal(25, MainViewModel.CalendarSnapshotCacheCapacity);
    }

    [Fact]
    public async Task Prefetch_BuildsTwentyFourMonthsInDistanceOrderAfterTheCurrentMonth()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        var center = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var prefetched = new List<DateTime>();
        viewModel.BeforePrefetchCalendarMonth = month => prefetched.Add(month);

        await viewModel.InitializeAsync();
        await WaitUntilAsync(() => prefetched.Count == 24);

        Assert.Equal(center.AddMonths(-1), prefetched[0]);
        Assert.Equal(center.AddMonths(1), prefetched[1]);
        Assert.Equal(center.AddMonths(-12), prefetched[^2]);
        Assert.Equal(center.AddMonths(12), prefetched[^1]);
        Assert.Equal(25, viewModel.CalendarCacheCount);
    }

    [Fact]
    public async Task Prefetch_ProcessesMonthsInDistanceOrderWhenPreviousMonthIsBlocked()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var previousStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePrevious = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        viewModel.BeforeBuildCalendarSnapshot = (month, token) =>
        {
            if (month.Year == currentMonth.AddMonths(-1).Year && month.Month == currentMonth.AddMonths(-1).Month)
            {
                previousStarted.TrySetResult();
                releasePrevious.Task.Wait(token);
            }

            if (month.Year == currentMonth.AddMonths(1).Year && month.Month == currentMonth.AddMonths(1).Month)
            {
                nextStarted.TrySetResult();
            }
        };

        await viewModel.InitializeAsync();
        await previousStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var nextStartedBeforePreviousCompleted = await Task.WhenAny(nextStarted.Task, Task.Delay(100)) == nextStarted.Task;
        releasePrevious.SetResult();
        await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(nextStartedBeforePreviousCompleted);
    }

    [Fact]
    public async Task Prefetch_CancelsOlderGenerationWithoutStoringSnapshot()
    {
        var baseline = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var target = baseline.AddMonths(24);
        var blockedPrefetchMonth = target.AddMonths(-1);
        var viewModel = await CreateViewModelAsync([
            CreateEvent("blocked prefetch", blockedPrefetchMonth.AddDays(2))
        ]);
        viewModel.NavigationRefreshDelay = TimeSpan.Zero;
        var prefetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prefetchCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeBuildCalendarSnapshot = (month, token) =>
        {
            if (month.Year != blockedPrefetchMonth.Year || month.Month != blockedPrefetchMonth.Month)
            {
                return;
            }

            prefetchStarted.TrySetResult();
            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(10);
            }

            prefetchCanceled.TrySetResult();
            token.ThrowIfCancellationRequested();
        };

        viewModel.CurrentMonth = target;
        await prefetchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CurrentMonth = target.AddMonths(2);
        await prefetchCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Equal(target.AddMonths(2), viewModel.CurrentMonth);
        Assert.False(viewModel.IsCalendarMonthCached(blockedPrefetchMonth));
    }

    [Fact]
    public async Task CalendarDayEvents_AssignsSingleAndMultiDayEventsByDate()
    {
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var singleDate = month.AddDays(6);
        var multiStart = month.AddDays(8);
        var multi = CreateEvent("multi day", multiStart);
        multi.End = new DateTimeOffset(multiStart.AddDays(2).AddHours(10));
        var viewModel = await CreateViewModelAsync([
            CreateEvent("single day", singleDate),
            multi
        ]);

        Assert.Contains(viewModel.CalendarDays.Single(day => day.Date == singleDate).Events, item => item.Title == "single day");
        Assert.Contains(viewModel.CalendarDays.Single(day => day.Date == multiStart).Events, item => item.Title == "multi day");
        Assert.Contains(viewModel.CalendarDays.Single(day => day.Date == multiStart.AddDays(1)).Events, item => item.Title == "multi day");
        Assert.Contains(viewModel.CalendarDays.Single(day => day.Date == multiStart.AddDays(2)).Events, item => item.Title == "multi day");
    }

    [Fact]
    public async Task CalendarDayEvents_TreatsAllDayEndAsExclusive()
    {
        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var start = month.AddDays(10);
        var allDay = new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "all day exclusive",
            Start = new DateTimeOffset(start),
            End = new DateTimeOffset(start.AddDays(2)),
            IsAllDay = true
        };
        var viewModel = await CreateViewModelAsync([allDay]);

        Assert.Contains(viewModel.CalendarDays.Single(day => day.Date == start).Events, item => item.Title == "all day exclusive");
        Assert.Contains(viewModel.CalendarDays.Single(day => day.Date == start.AddDays(1)).Events, item => item.Title == "all day exclusive");
        Assert.DoesNotContain(viewModel.CalendarDays.Single(day => day.Date == start.AddDays(2)).Events, item => item.Title == "all day exclusive");
    }

    [Fact]
    public async Task CalendarDayEvents_UsesFiveCompactLanesAndTracksOverflow()
    {
        var date = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(12);
        var events = Enumerable.Range(0, 8)
            .Select(index => CreateEvent($"overflow {index}", date))
            .ToArray();

        var viewModel = await CreateViewModelAsync(events);
        var day = viewModel.CalendarDays.Single(day => day.Date == date);

        Assert.Equal(5, day.Events.Count);
        Assert.Equal(3, day.HiddenEventCount);
        Assert.Equal(5, day.Segments.Count(segment => segment.IsVisible));
    }

    [Fact]
    public async Task UpdateMonthLaneCapacity_RelayoutsCachedSnapshotWithoutReloadingEvents()
    {
        var date = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(12);
        var viewModel = await CreateViewModelAsync(Enumerable.Range(0, 8).Select(index => CreateEvent($"lane {index}", date)));
        var day = viewModel.CalendarDays.Single(item => item.Date == date);
        var originalSegments = day.Segments.ToArray();

        Assert.True(viewModel.UpdateMonthLaneCapacity(2));
        Assert.Equal(2, day.Events.Count);
        Assert.Equal(6, day.HiddenEventCount);
        Assert.Equal(2, day.Segments.Count(segment => segment.IsVisible));
        Assert.False(viewModel.UpdateMonthLaneCapacity(2));
        Assert.Equal(2, day.Segments.Count(segment => segment.IsVisible));
        Assert.DoesNotContain(day.Segments, segment => originalSegments.Contains(segment));
    }

    [Fact]
    public async Task MainWindowMonthTemplate_ShowsHiddenEventCount()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "FavGCalSchedulerClone.App",
            "MainWindow.xaml"));
        var xaml = await File.ReadAllTextAsync(xamlPath);

        Assert.Contains("HiddenEventText", xaml);
        Assert.Contains("HasHiddenEvents", xaml);
    }

    [Fact]
    public async Task SaveSettings_InvalidatesCalendarCache()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount > 0);
        var settings = viewModel.CreateSettingsSnapshot();
        settings.WeekStartsOnMonday = !settings.WeekStartsOnMonday;
        var loadCount = 0;
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            loadCount++;
            return Task.CompletedTask;
        };

        await viewModel.SaveApplicationSettingsAsync(settings);

        Assert.True(loadCount > 0);
        Assert.True(viewModel.CalendarCacheCount > 0);
    }

    [Fact]
    public async Task SaveEventColorSettings_InvalidatesCalendarCache()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount > 0);
        var settings = viewModel.CreateSettingsSnapshot();
        settings.EventColorSettings =
        [
            new EventColorSetting { ColorId = "5", Label = "Important", IsEnabled = true }
        ];
        var loadCount = 0;
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            loadCount++;
            return Task.CompletedTask;
        };

        await viewModel.SaveApplicationSettingsAsync(settings);

        Assert.True(loadCount > 0);
        Assert.True(viewModel.CalendarCacheCount > 0);
    }

    [Fact]
    public async Task SaveTags_InvalidatesCalendarCache()
    {
        var viewModel = await CreateViewModelAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount > 0);
        var loadCount = 0;
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            loadCount++;
            return Task.CompletedTask;
        };

        await viewModel.SaveTagsAsync();

        Assert.True(loadCount > 0);
    }

    [Fact]
    public async Task ApplyCalendarSelection_InvalidatesCalendarCache()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings { VisibleCalendarIds = ["primary", "team"] });
        await repository.SaveEventAsync(CreateEvent("primary event", DateTime.Today));
        await repository.SaveEventAsync(new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "team event",
            CalendarId = "team",
            Start = new DateTimeOffset(DateTime.Today.Date.AddHours(9)),
            End = new DateTimeOffset(DateTime.Today.Date.AddHours(10))
        });
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        await WaitUntilAsync(() => viewModel.CalendarCacheCount > 0);
        var loadCount = 0;
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            loadCount++;
            return Task.CompletedTask;
        };
        viewModel.AvailableCalendars.Single(item => item.Id == "primary").IsSelected = false;
        viewModel.AvailableCalendars.Single(item => item.Id == "team").IsSelected = true;

        await viewModel.ApplyCalendarSelectionAsync();

        Assert.True(loadCount > 0);
        Assert.Contains(viewModel.SelectedDayEvents, item => item.Title == "team event");
        Assert.DoesNotContain(viewModel.SelectedDayEvents, item => item.Title == "primary event");
    }

    [Fact]
    public async Task Navigation_DoesNotRefreshTodosForMonthMove()
    {
        var viewModel = await CreateViewModelAsync();
        var todoRefreshCount = 0;
        viewModel.BeforeRefreshTodos = () => todoRefreshCount++;

        viewModel.NextMonthCommand.Execute(null);
        await Task.Delay(viewModel.NavigationRefreshDelay + TimeSpan.FromMilliseconds(150));

        Assert.Equal(0, todoRefreshCount);
    }

    [Fact]
    public async Task SaveTodo_InvalidatesCalendarCacheAndRefreshesTodos()
    {
        var viewModel = await CreateViewModelAsync();
        var loadCount = 0;
        var todoRefreshCount = 0;
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            loadCount++;
            return Task.CompletedTask;
        };
        viewModel.BeforeRefreshTodos = () => todoRefreshCount++;

        await viewModel.SaveTodoAsync(DateTime.Today, "A", 0, "cache invalidating todo", null);

        Assert.True(loadCount > 0);
        Assert.True(todoRefreshCount > 0);
    }

    [Fact]
    public void MonthJumpDialog_ValidatesTargetMonth()
    {
        Assert.True(MonthJumpDialog.TryCreateTargetMonth("2027", "6", out var target, out var error));
        Assert.Equal(new DateTime(2027, 6, 1), target);
        Assert.Equal("", error);

        Assert.False(MonthJumpDialog.TryCreateTargetMonth("1899", "6", out _, out error));
        Assert.Contains("1900", error);

        Assert.False(MonthJumpDialog.TryCreateTargetMonth("2027", "13", out _, out error));
        Assert.Contains("12", error);
    }

    [Fact]
    public async Task SelectedDayChange_RefreshesSelectedDayEventsWithoutRebuildingVisibleDays()
    {
        var selectedDate = DateTime.Today;
        var otherDate = selectedDate.AddDays(1);
        var viewModel = await CreateViewModelAsync([
            CreateEvent("Selected date event", selectedDate),
            CreateEvent("Other date event", otherDate)
        ]);
        var visibleDays = viewModel.VisibleCalendarDays.ToArray();

        viewModel.SelectedDay = viewModel.CalendarDays.First(day => day.Date == otherDate.Date);

        Assert.Equal(visibleDays, viewModel.VisibleCalendarDays);
        Assert.Single(viewModel.SelectedDayEvents);
        Assert.Equal("Other date event", viewModel.SelectedDayEvents[0].Title);
        Assert.Equal($"{otherDate:yyyy/MM/dd} を選択しました。", viewModel.Status);
    }

    [Fact]
    public async Task SelectedDayChange_ClearsSelectedEventWhenEventIsOnDifferentDate()
    {
        var selectedDate = DateTime.Today;
        var otherDate = selectedDate.AddDays(1);
        var selectedEvent = CreateEvent("Selected date event", selectedDate);
        var viewModel = await CreateViewModelAsync([selectedEvent]);

        viewModel.SelectedEvent = selectedEvent;
        viewModel.SelectedDay = viewModel.CalendarDays.First(day => day.Date == otherDate.Date);

        Assert.Null(viewModel.SelectedEvent);
        Assert.Equal(otherDate.Date, viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task SelectEvent_SelectsEventAndItsDate()
    {
        var eventDate = DateTime.Today.AddDays(2);
        var calendarEvent = CreateEvent("Target event", eventDate);
        var viewModel = await CreateViewModelAsync([calendarEvent]);

        viewModel.SelectEvent(calendarEvent);

        Assert.Same(calendarEvent, viewModel.SelectedEvent);
        Assert.Equal(eventDate.Date, viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task SelectEventSegment_SelectsContinuationDateAndOriginalEvent()
    {
        var start = DateTime.Today.AddDays(1);
        var calendarEvent = new CalendarEvent
        {
            Id = "multi",
            Title = "Multi day",
            IsAllDay = true,
            Start = new DateTimeOffset(start),
            End = new DateTimeOffset(start.AddDays(3))
        };
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        var continuationDate = start.AddDays(1).Date;
        var segment = viewModel.CalendarDays.Single(day => day.Date == continuationDate)
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id);

        viewModel.SelectEventSegment(segment);

        Assert.Equal(calendarEvent.Id, viewModel.SelectedEvent?.Id);
        Assert.Equal(continuationDate, viewModel.SelectedDay?.Date);
        Assert.Contains(viewModel.SelectedDayEvents, item => item.Id == calendarEvent.Id);
    }

    [Fact]
    public async Task SelectEventSegment_HighlightsOnlyConnectedSegmentsInSelectedWeek()
    {
        var start = new DateTime(2026, 5, 15);
        var calendarEvent = new CalendarEvent
        {
            Id = "week-crossing",
            Title = "Week crossing",
            IsAllDay = true,
            Start = new DateTimeOffset(start),
            End = new DateTimeOffset(new DateTime(2026, 5, 20))
        };
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(start);
        var clickedSegment = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 5, 16))
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id);

        viewModel.SelectEventSegment(clickedSegment);

        var selectedDates = viewModel.CalendarDays.SelectMany(day => day.Segments)
            .Where(segment => segment.IsSelected)
            .Select(segment => segment.Date)
            .ToArray();
        Assert.Equal([new DateTime(2026, 5, 15), new DateTime(2026, 5, 16)], selectedDates);
        Assert.All(
            viewModel.CalendarDays.SelectMany(day => day.Segments).Where(segment => segment.IsSelected),
            segment => Assert.NotEqual(segment.Event?.DisplayColor, segment.DisplayColor));
    }

    [Fact]
    public async Task SelectedDayChange_ClearsSegmentHighlightWhenSelectionLeavesEvent()
    {
        var eventDate = new DateTime(2026, 5, 11);
        var calendarEvent = CreateEvent("Selected label", eventDate);
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(eventDate);
        var segment = viewModel.CalendarDays.Single(day => day.Date == eventDate)
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id);
        viewModel.SelectEventSegment(segment);

        viewModel.SelectedDay = viewModel.CalendarDays.Single(day => day.Date == eventDate.AddDays(1));

        Assert.DoesNotContain(viewModel.CalendarDays.SelectMany(day => day.Segments), item => item.IsSelected);
    }

    [Fact]
    public async Task MoveEventAsync_ShiftsTimedEventWithoutChangingDurationOrProperties()
    {
        var start = new DateTime(2026, 5, 11);
        var calendarEvent = CreateEvent("Move me", start);
        calendarEvent.ColorId = "2";
        calendarEvent.ReminderMinutesBeforeStart = 15;
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(start);
        var visibleEvent = viewModel.CalendarDays.Single(day => day.Date == start)
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id).Event!;

        var moved = await viewModel.MoveEventAsync(visibleEvent, start, start.AddDays(3));

        Assert.True(moved);
        Assert.Equal(start.AddDays(3).AddHours(9), viewModel.SelectedEvent?.Start.DateTime);
        Assert.Equal(start.AddDays(3).AddHours(10), viewModel.SelectedEvent?.End.DateTime);
        Assert.Equal("2", viewModel.SelectedEvent?.ColorId);
        Assert.Equal(15, viewModel.SelectedEvent?.ReminderMinutesBeforeStart);
        Assert.Equal(start.AddDays(3), viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task MoveEventAsync_UsesDraggedContinuationDayAsMultiDayAnchor()
    {
        var calendarEvent = new CalendarEvent
        {
            Id = "multi-move",
            Title = "Trip",
            IsAllDay = true,
            Start = new DateTimeOffset(new DateTime(2026, 5, 11)),
            End = new DateTimeOffset(new DateTime(2026, 5, 14))
        };
        var viewModel = await CreateViewModelAsync([calendarEvent]);
        await viewModel.NavigateToDateAsync(new DateTime(2026, 5, 11));
        var continuation = viewModel.CalendarDays.Single(day => day.Date == new DateTime(2026, 5, 12))
            .Segments.Single(item => item.Event?.Id == calendarEvent.Id).Event!;

        await viewModel.MoveEventAsync(continuation, new DateTime(2026, 5, 12), new DateTime(2026, 5, 20));

        Assert.Equal(new DateTime(2026, 5, 19), viewModel.SelectedEvent?.Start.Date);
        Assert.Equal(new DateTime(2026, 5, 22), viewModel.SelectedEvent?.End.Date);
        Assert.Equal(new DateTime(2026, 5, 20), viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task MoveEventAsync_RequiresScopeForRecurringEvent()
    {
        var start = new DateTime(2026, 5, 11);
        var recurring = CreateEvent("Recurring", start);
        recurring.RecurrenceJson = "[\"RRULE:FREQ=DAILY;COUNT=2\"]";
        var viewModel = await CreateViewModelAsync([recurring]);
        await viewModel.NavigateToDateAsync(start);

        var moved = await viewModel.MoveEventAsync(recurring, start, start.AddDays(1));

        Assert.False(moved);
        Assert.Equal(start, recurring.Start.Date);
    }

    [Fact]
    public async Task NavigateToDateAsync_SelectsTargetDateWithoutRevertingToToday()
    {
        var targetDate = DateTime.Today.AddMonths(1).AddDays(3);
        var viewModel = await CreateViewModelAsync();

        await viewModel.NavigateToDateAsync(targetDate);

        Assert.Equal(new DateTime(targetDate.Year, targetDate.Month, 1), viewModel.CurrentMonth);
        Assert.Equal(targetDate.Date, viewModel.SelectedDay?.Date);
        Assert.NotEqual(DateTime.Today, viewModel.SelectedDay?.Date);
    }

    [Fact]
    public async Task CalendarStatusText_UsesSelectedDayAndOriginalWeekNumbering()
    {
        var viewModel = await CreateViewModelAsync();

        await viewModel.NavigateToDateAsync(new DateTime(2026, 5, 24));

        Assert.Contains("2026", viewModel.CalendarStatusText);
        Assert.Contains("05月24日", viewModel.CalendarStatusText);
        Assert.Contains("第4日曜日", viewModel.CalendarStatusText);
        Assert.Contains("21週目", viewModel.CalendarStatusText);
        Assert.Contains("経過日数 144日", viewModel.CalendarStatusText);
    }

    [Fact]
    public async Task CalendarDay_RetiredDayTagRemainsVisibleWithoutChangingEventLabelColor()
    {
        var calendarEvent = CreateEvent("Visible event", DateTime.Today);
        calendarEvent.ColorId = "2";
        var ordinaryEvent = CreateEvent("Ordinary tag text", DateTime.Today);
        ordinaryEvent.Description = "#important";
        var viewModel = await CreateViewModelAsync([calendarEvent, ordinaryEvent]);

        var day = viewModel.CalendarDays.Single(item => item.Date == DateTime.Today);
        var item = day.Events.Single(item => item.Title == calendarEvent.Title);

        Assert.Equal("#7AE7BF", item.DisplayColor);
        Assert.Contains(day.Segments, segment => segment.Event?.Title == ordinaryEvent.Title && segment.IsVisible);
    }

    [Fact]
    public async Task CalendarDay_HolidayDirectiveIsHiddenAndUsesSundayBackgroundState()
    {
        var date = new DateTime(2026, 5, 18);
        var directive = CreateEvent("Holiday directive", date);
        directive.Description = "#holiday";
        var viewModel = await CreateViewModelAsync([directive]);
        await viewModel.NavigateToDateAsync(date);

        var day = viewModel.CalendarDays.Single(item => item.Date == date);
        Assert.True(day.IsHoliday);
        Assert.Empty(day.Events);
        Assert.DoesNotContain(day.Segments, segment => segment.IsVisible);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    public async Task CalendarDay_WorkdayDirectiveOnWeekendIsHiddenAndUsesWeekdayState(int dayNumber)
    {
        var date = new DateTime(2026, 5, dayNumber);
        var directive = CreateEvent("Workday directive", date);
        directive.Description = "#workday";
        var viewModel = await CreateViewModelAsync([directive]);
        await viewModel.NavigateToDateAsync(date);

        var day = viewModel.CalendarDays.Single(item => item.Date == date);
        Assert.True(day.IsWeekend);
        Assert.True(day.IsWorkdayOverride);
        Assert.False(day.IsHoliday);
        Assert.Empty(day.Events);
        Assert.DoesNotContain(day.Segments, segment => segment.IsVisible);
    }

    [Fact]
    public async Task InitializeAsync_AppliesDisplaySettingsAndMondayStart()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings
        {
            StartupCalendarViewMode = CalendarViewMode.Week,
            StartupTodoTabIndex = 1,
            CalendarLabelFontSizeIndex = 3,
            SideListFontSizeIndex = 1,
            WeekdayDisplayType = WeekdayDisplayType.JapaneseShort,
            WeekStartsOnMonday = true,
            WindowOpacity = 128
        });
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

        await viewModel.InitializeAsync();

        Assert.True(viewModel.IsWeekView);
        Assert.Equal(1, viewModel.SelectedTodoTabIndex);
        Assert.Equal(12, viewModel.CalendarLabelFontSize);
        Assert.Equal(11, viewModel.SideListFontSize);
        Assert.Equal("月", viewModel.WeekdayHeaders[0]);
        Assert.Equal(DayOfWeek.Monday, viewModel.CalendarDays[0].Date.DayOfWeek);
        Assert.Equal(128 / 255.0, viewModel.WindowOpacity);
    }

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        return await CreateViewModelAsync([]);
    }

    private static async Task<MainViewModel> CreateViewModelAsync(IEnumerable<CalendarEvent> events)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        foreach (var calendarEvent in events)
        {
            await repository.SaveEventAsync(calendarEvent);
        }

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        await Task.Delay(50);
        return viewModel;
    }

    private static CalendarEvent CreateEvent(string title, DateTime date)
    {
        return new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Start = new DateTimeOffset(date.Date.AddHours(9)),
            End = new DateTimeOffset(date.Date.AddHours(10))
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeoutAt = DateTimeOffset.Now.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.Now >= timeoutAt)
            {
                Assert.True(condition());
            }

            await Task.Delay(25);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = [];

        public int PendingCount
        {
            get
            {
                lock (_callbacks)
                {
                    return _callbacks.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_callbacks)
            {
                _callbacks.Enqueue((callback, state));
            }
        }

        public bool PumpUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                (SendOrPostCallback Callback, object? State)? item = null;
                lock (_callbacks)
                {
                    if (_callbacks.Count > 0)
                    {
                        item = _callbacks.Dequeue();
                    }
                }

                if (item is not null)
                {
                    item.Value.Callback(item.Value.State);
                    continue;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                Thread.Sleep(10);
            }

            return true;
        }
    }
}

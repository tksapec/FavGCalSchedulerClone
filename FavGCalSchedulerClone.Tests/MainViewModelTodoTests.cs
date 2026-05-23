using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class MainViewModelTodoTests
{
    [Fact]
    public async Task SaveTodoAsync_SplitsIncompleteAndCompletedTodos()
    {
        var viewModel = await CreateViewModelAsync();
        var dueDate = DateTime.Today;

        await viewModel.SaveTodoAsync(dueDate, "A", 56, "未処理ToDo", "本文");
        await viewModel.SaveTodoAsync(dueDate, "B", 100, "処理済みToDo", "本文");

        Assert.Single(viewModel.TodoEvents);
        Assert.Single(viewModel.CompletedTodoEvents);
        Assert.Equal("A", viewModel.TodoEvents[0].TodoPriority);
        Assert.Equal(56, viewModel.TodoEvents[0].TodoProgress);
        Assert.True(viewModel.CompletedTodoEvents[0].IsTodoDone);
    }

    [Fact]
    public async Task MarkSelectedTodoDoneAsync_MovesTodoToCompletedCollection()
    {
        var viewModel = await CreateViewModelAsync();
        await viewModel.SaveTodoAsync(DateTime.Today, "A", 56, "確認", "本文 #todoB10% 詳細");

        viewModel.SelectedEvent = viewModel.TodoEvents.Single();
        await viewModel.MarkSelectedTodoDoneAsync();

        Assert.Empty(viewModel.TodoEvents);
        Assert.Single(viewModel.CompletedTodoEvents);
        Assert.Contains("#todoA100%", viewModel.CompletedTodoEvents[0].Description);
        Assert.DoesNotContain("#todoB10%", viewModel.CompletedTodoEvents[0].Description);
    }

    [Fact]
    public async Task SaveApplicationSettingsAsync_PersistsAndInitializesRuntimeProperties()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        await viewModel.SaveApplicationSettingsAsync(
            startupTabIndex: 3,
            confirmBeforeDelete: false,
            closeButtonExitsApplication: false,
            defaultNewEventIsAllDay: false,
            useWindowsToastNotifications: false);

        var reloadedRepository = new CalendarRepository(dbPath);
        var reloaded = new MainViewModel(reloadedRepository, new GoogleCalendarSyncService(reloadedRepository));
        await reloaded.InitializeAsync();

        Assert.Equal(3, reloaded.StartupTabIndex);
        Assert.Equal(3, reloaded.SelectedTabIndex);
        Assert.False(reloaded.ConfirmBeforeDelete);
        Assert.False(reloaded.CloseButtonExitsApplication);
        Assert.False(reloaded.DefaultNewEventIsAllDay);
        Assert.False(reloaded.UseWindowsToastNotifications);
    }

    [Fact]
    public async Task BeginNewEvent_UsesDefaultAllDaySetting()
    {
        var viewModel = await CreateViewModelAsync();
        await viewModel.SaveApplicationSettingsAsync(
            startupTabIndex: 0,
            confirmBeforeDelete: true,
            closeButtonExitsApplication: true,
            defaultNewEventIsAllDay: false,
            useWindowsToastNotifications: true);

        viewModel.BeginNewEvent(DateTime.Today);

        Assert.False(viewModel.IsAllDay);
    }

    [Fact]
    public async Task GoToTodayAsync_FromDifferentMonth_ReturnsToCurrentMonthAndSelectsToday()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-2);

        await viewModel.GoToTodayAsync();

        Assert.Equal(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), viewModel.CurrentMonth);
        Assert.NotNull(viewModel.SelectedDay);
        Assert.Equal(DateTime.Today, viewModel.SelectedDay.Date);
        Assert.Equal("今日を表示しました。", viewModel.Status);
    }

    [Fact]
    public async Task GoToTodayAsync_FromCurrentMonth_ReselectsToday()
    {
        var viewModel = await CreateViewModelAsync();
        var otherDay = viewModel.CalendarDays.First(day => day.Date != DateTime.Today);
        viewModel.SelectedDay = otherDay;

        await viewModel.GoToTodayAsync();

        Assert.NotSame(otherDay, viewModel.SelectedDay);
        Assert.NotNull(viewModel.SelectedDay);
        Assert.Equal(DateTime.Today, viewModel.SelectedDay.Date);
    }

    [Fact]
    public async Task SaveApplicationSettingsAsync_ClampsStartupTabIndex()
    {
        var viewModel = await CreateViewModelAsync();

        await viewModel.SaveApplicationSettingsAsync(
            startupTabIndex: 99,
            confirmBeforeDelete: true,
            closeButtonExitsApplication: true,
            defaultNewEventIsAllDay: true,
            useWindowsToastNotifications: true);

        Assert.Equal(4, viewModel.StartupTabIndex);
    }

    [Fact]
    public async Task InitializeAsync_UsesLegacyActiveCalendarAsVisibleSelection()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings
        {
            ActiveCalendarId = "team-calendar"
        });

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        Assert.Contains(viewModel.AvailableCalendars, item => item.Id == "team-calendar" && item.IsSelected);
        Assert.Equal("team-calendar", viewModel.EditorCalendarId);
    }

    [Fact]
    public async Task ApplyCalendarSelectionAsync_FiltersVisibleEventsAndPersistsSelection()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "Primary event",
            CalendarId = "primary",
            Start = new DateTimeOffset(DateTime.Today.AddHours(9)),
            End = new DateTimeOffset(DateTime.Today.AddHours(10))
        });
        await repository.SaveEventAsync(new CalendarEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = "Team event",
            CalendarId = "team",
            Start = new DateTimeOffset(DateTime.Today.AddHours(11)),
            End = new DateTimeOffset(DateTime.Today.AddHours(12))
        });
        await repository.SaveSettingsAsync(new AppSettings
        {
            VisibleCalendarIds = ["primary", "team"]
        });

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        Assert.Equal(2, viewModel.SelectedDayEvents.Count);

        var primary = viewModel.AvailableCalendars.Single(item => item.Id == "primary");
        var team = viewModel.AvailableCalendars.Single(item => item.Id == "team");
        primary.IsSelected = false;
        team.IsSelected = true;

        await viewModel.ApplyCalendarSelectionAsync();

        Assert.Single(viewModel.SelectedDayEvents);
        Assert.Equal("Team event", viewModel.SelectedDayEvents[0].Title);

        var reloadedRepository = new CalendarRepository(dbPath);
        var settings = await reloadedRepository.LoadSettingsAsync();
        Assert.Equal(["team"], settings.VisibleCalendarIds);
        Assert.Equal("team", settings.ActiveCalendarId);
    }

    [Fact]
    public async Task SaveTodoAsync_UsesEditorCalendarIdAsSaveTarget()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings
        {
            VisibleCalendarIds = ["primary", "team"]
        });

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.EditorCalendarId = "team";

        await viewModel.SaveTodoAsync(DateTime.Today, "A", 20, "Team todo", "body");

        Assert.Single(viewModel.TodoEvents);
        Assert.Equal("team", viewModel.TodoEvents[0].CalendarId);
    }

    [Fact]
    public async Task SaveTodoAsync_WithExistingEventId_UpdatesTodoWithoutDuplicatingMarker()
    {
        var viewModel = await CreateViewModelAsync();
        await viewModel.SaveTodoAsync(DateTime.Today, "B", 10, "Existing todo", "body #todoC20%");
        var existing = viewModel.TodoEvents.Single();

        await viewModel.SaveTodoAsync(existing.Id, DateTime.Today.AddDays(1), "A", 100, "Updated todo", "updated #todoB10%");

        Assert.Empty(viewModel.TodoEvents);
        Assert.Single(viewModel.CompletedTodoEvents);
        Assert.Equal(existing.Id, viewModel.CompletedTodoEvents[0].Id);
        Assert.Equal("Updated todo", viewModel.CompletedTodoEvents[0].Title);
        Assert.Contains("#todoA100%", viewModel.CompletedTodoEvents[0].Description);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(viewModel.CompletedTodoEvents[0].Description ?? "", "#todo", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    private static async Task<MainViewModel> CreateViewModelAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.CurrentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        return viewModel;
    }
}

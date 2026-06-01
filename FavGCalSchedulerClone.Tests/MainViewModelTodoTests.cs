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
    public async Task SaveCurrentEventAsync_PersistsAndUpdatesSelectedEventColor()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.BeginNewEvent(DateTime.Today);
        viewModel.Title = "Colored schedule";
        viewModel.EditorColorId = "9";

        await viewModel.SaveCurrentEventAsync();

        Assert.NotNull(viewModel.SelectedEvent);
        Assert.Equal("9", viewModel.SelectedEvent.ColorId);

        viewModel.EditorColorId = "5";
        await viewModel.SaveCurrentEventAsync();

        Assert.NotNull(viewModel.SelectedEvent);
        Assert.Equal("5", viewModel.SelectedEvent.ColorId);
    }

    [Fact]
    public async Task WhiteEventColorOption_RoundTripsAsNullAndWhiteLabel()
    {
        var viewModel = await CreateViewModelAsync();
        var white = Assert.Single(viewModel.EventColorOptions, option => option.Id is null);
        Assert.Equal("#FFFFFF", white.Background);

        viewModel.BeginNewEvent(DateTime.Today);
        viewModel.Title = "White schedule";
        viewModel.EditorColorId = null;

        await viewModel.SaveCurrentEventAsync();

        Assert.Null(viewModel.SelectedEvent!.ColorId);
        Assert.Equal("#FFFFFF", viewModel.SelectedEvent.DisplayColor);
    }

    [Fact]
    public async Task SaveApplicationSettingsAsync_PersistsEventColorLabels()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var settings = viewModel.CreateSettingsSnapshot();
        settings.EventColorSettings =
        [
            new EventColorSetting { ColorId = "5", Label = "Important", IsEnabled = true },
            new EventColorSetting { ColorId = "6", Label = "Hidden", IsEnabled = false }
        ];

        await viewModel.SaveApplicationSettingsAsync(settings);

        var reloaded = new MainViewModel(new CalendarRepository(dbPath), new GoogleCalendarSyncService(new CalendarRepository(dbPath)));
        await reloaded.InitializeAsync();
        Assert.Equal("Important", Assert.Single(reloaded.EventColorOptions, item => item.Id == "5").Label);
        Assert.DoesNotContain(reloaded.EventColorOptions, item => item.Id == "6");
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
    public async Task SaveTodoAsync_PersistsSelectedColorAndDoesNotCreateReminder()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.EditorColorId = "5";
        viewModel.ReminderMinutesBeforeStart = 10;

        await viewModel.SaveTodoAsync(DateTime.Today, "A", 0, "Colored todo", "body");

        var item = Assert.Single(viewModel.TodoEvents);
        Assert.Equal("5", item.ColorId);
        Assert.Null(item.ReminderMinutesBeforeStart);
    }

    [Fact]
    public async Task SaveTodoAsync_PreservesMultilineBodyAndKeepsMarkerInternal()
    {
        var viewModel = await CreateViewModelAsync();
        var body = $"first line{Environment.NewLine}{Environment.NewLine}third line";

        await viewModel.SaveTodoAsync(DateTime.Today, "A", 20, "Multiline todo", body);

        var item = Assert.Single(viewModel.TodoEvents);
        Assert.Contains("#todoA20%", item.Description);
        Assert.Equal(body, TagService.GetTodoBodyForEditing(item.Description));
    }

    [Fact]
    public async Task SaveTodoAsync_EditPreservesExistingReminderWhileChangingProgressAndColor()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Imported todo",
            Description = "#todoF90% body",
            CalendarId = "primary",
            ColorId = "5",
            ReminderMinutesBeforeStart = 30,
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true
        });
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        var item = Assert.Single(viewModel.TodoEvents);
        viewModel.SelectEvent(item);
        viewModel.EditorColorId = "9";

        await viewModel.SaveTodoAsync(item, DateTime.Today, "F", 56, item.Title, item.Description);

        var updated = Assert.Single(viewModel.TodoEvents);
        Assert.Equal("9", updated.ColorId);
        Assert.Equal(30, updated.ReminderMinutesBeforeStart);
        Assert.Equal("F", updated.TodoPriority);
        Assert.Equal(56, updated.TodoProgress);
    }

    [Fact]
    public async Task MoveEventAsync_MovesTodoDueDateWithoutChangingMetadataOrColor()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.EditorColorId = "2";
        await viewModel.SaveTodoAsync(DateTime.Today, "F", 56, "Move todo", "body");
        var todo = Assert.Single(viewModel.TodoEvents);

        var moved = await viewModel.MoveEventAsync(todo, DateTime.Today, DateTime.Today.AddDays(2));

        Assert.True(moved);
        var updated = Assert.Single(viewModel.TodoEvents);
        Assert.Equal(DateTime.Today.AddDays(2), updated.Start.Date);
        Assert.Equal("F", updated.TodoPriority);
        Assert.Equal(56, updated.TodoProgress);
        Assert.Equal("2", updated.ColorId);
    }

    [Fact]
    public async Task TodoOutsideDisplayedMonth_UsesColorAndDisplayPeriodSetting()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "Old colored todo",
            Description = "#todoA20%",
            ColorId = "2",
            IsTodoLike = true,
            CalendarId = "primary",
            Start = new DateTimeOffset(DateTime.Today.AddYears(-1)),
            End = new DateTimeOffset(DateTime.Today.AddYears(-1).AddDays(1)),
            IsAllDay = true
        });
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        var item = Assert.Single(viewModel.TodoEvents);
        Assert.Equal("#7AE7BF", item.DisplayColor);

        var settings = viewModel.CreateSettingsSnapshot();
        settings.IncompleteTodoDisplayPeriodMonths = 3;
        await viewModel.SaveApplicationSettingsAsync(settings);

        Assert.Empty(viewModel.TodoEvents);
    }

    [Fact]
    public async Task ScheduleHistory_StoresNormalEventInputsButCanBeCleared()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.BeginNewEvent(DateTime.Today);
        viewModel.Title = "History title";
        viewModel.Location = "History room";

        await viewModel.SaveCurrentEventAsync();

        Assert.Contains("History title", await viewModel.LoadScheduleTitleHistoryAsync());
        Assert.Contains("History room", await viewModel.LoadScheduleLocationHistoryAsync());

        await viewModel.ClearScheduleTitleHistoryAsync();
        await viewModel.ClearScheduleLocationHistoryAsync();

        Assert.Empty(await viewModel.LoadScheduleTitleHistoryAsync());
        Assert.Empty(await viewModel.LoadScheduleLocationHistoryAsync());
    }

    [Fact]
    public async Task SaveCurrentEventAsync_PreservesMultilineDescription()
    {
        var viewModel = await CreateViewModelAsync();
        var description = $"first line{Environment.NewLine}{Environment.NewLine}third line";
        viewModel.BeginNewEvent(DateTime.Today);
        viewModel.Title = "Multiline event";
        viewModel.Description = description;

        await viewModel.SaveCurrentEventAsync();

        Assert.Equal(description, viewModel.SelectedEvent?.Description);
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

    [Fact]
    public async Task MarkTodoDoneAsync_MovesSpecificTodoToCompletedCollection()
    {
        var viewModel = await CreateViewModelAsync();
        await viewModel.SaveTodoAsync(DateTime.Today, "C", 30, "Click done", "body #todoA10%");
        var todo = viewModel.TodoEvents.Single();

        await viewModel.MarkTodoDoneAsync(todo);

        Assert.Empty(viewModel.TodoEvents);
        Assert.Single(viewModel.CompletedTodoEvents);
        Assert.Equal(todo.Id, viewModel.CompletedTodoEvents[0].Id);
        Assert.Contains("#todoC100%", viewModel.CompletedTodoEvents[0].Description);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(viewModel.CompletedTodoEvents[0].Description ?? "", "#todo", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task RefreshTodosAsync_LimitsCompletedTodosByNearestDueDate()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        for (var index = 0; index <= 100; index++)
        {
            await repository.SaveEventAsync(new CalendarEvent
            {
                Title = $"done-{index:000}",
                Description = "#todoA100% body",
                CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
                Start = new DateTimeOffset(DateTime.Today.AddDays(index)),
                End = new DateTimeOffset(DateTime.Today.AddDays(index + 1)),
                IsAllDay = true
            });
        }

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        Assert.Equal(100, viewModel.CompletedTodoEvents.Count);
        Assert.Equal("done-000", viewModel.CompletedTodoEvents[0].Title);
        Assert.DoesNotContain(viewModel.CompletedTodoEvents, item => item.Title == "done-100");
    }

    [Fact]
    public async Task RefreshTodosAsync_OrdersSameDueDateCompletedTodosByNewestUpdate()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "older",
            Description = "#todoA100% body",
            CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true
        });
        await Task.Delay(20);
        await repository.SaveEventAsync(new CalendarEvent
        {
            Title = "newer",
            Description = "#todoA100% body",
            CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true
        });

        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();

        Assert.Equal(["newer", "older"], viewModel.CompletedTodoEvents.Select(item => item.Title));
    }

    [Fact]
    public async Task SaveTodoAsync_Progress100FromCompleteCheckboxIsCompleted()
    {
        var viewModel = await CreateViewModelAsync();

        await viewModel.SaveTodoAsync(DateTime.Today, "A", 100, "Complete from editor", "body");

        Assert.Empty(viewModel.TodoEvents);
        var item = Assert.Single(viewModel.CompletedTodoEvents);
        Assert.True(item.IsTodoDone);
        Assert.Contains("#todoA100%", item.Description);
    }

    [Fact]
    public async Task CopyAndPasteEventLabel_CreatesNewEventOnTargetDate()
    {
        var viewModel = await CreateViewModelAsync();
        viewModel.BeginNewEvent(DateTime.Today);
        viewModel.Title = "Copy source";
        viewModel.EditorColorId = "5";
        await viewModel.SaveCurrentEventAsync();
        var source = viewModel.SelectedEvent!;
        viewModel.CopySelectedEventLabel();

        var pasted = await viewModel.PasteEventLabelAsync(DateTime.Today.AddDays(2));

        Assert.True(pasted);
        Assert.True(viewModel.CanPasteEventLabel);
        Assert.NotNull(viewModel.SelectedEvent);
        Assert.NotEqual(source.Id, viewModel.SelectedEvent!.Id);
        Assert.Equal("Copy source", viewModel.SelectedEvent.Title);
        Assert.Equal("5", viewModel.SelectedEvent.ColorId);
        Assert.Equal(DateTime.Today.AddDays(2), viewModel.SelectedEvent.Start.Date);
    }

    [Fact]
    public async Task CutAndPasteEventLabel_MovesEventAndClearsClipboard()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var repository = new CalendarRepository(dbPath);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
        await viewModel.InitializeAsync();
        viewModel.BeginNewEvent(DateTime.Today);
        viewModel.Title = "Cut source";
        await viewModel.SaveCurrentEventAsync();
        var source = viewModel.SelectedEvent!;
        viewModel.CutSelectedEventLabel();

        var pasted = await viewModel.PasteEventLabelAsync(DateTime.Today.AddDays(3));

        Assert.True(pasted);
        Assert.False(viewModel.CanPasteEventLabel);
        var allEvents = await repository.LoadEventsAsync(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(10), includeDeleted: true);
        Assert.Contains(allEvents, item => item.Id == source.Id && item.IsDeleted);
        Assert.Contains(allEvents, item => item.Id != source.Id && item.Title == "Cut source" && item.Start.Date == DateTime.Today.AddDays(3));
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

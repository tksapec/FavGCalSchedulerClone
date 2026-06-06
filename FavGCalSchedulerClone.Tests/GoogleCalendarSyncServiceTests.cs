using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleCalendarSyncServiceTests
{
    [Fact]
    public void ResolveNotFoundAction_TreatsDeletedEventAsAlreadySynced()
    {
        var action = GoogleCalendarSyncService.ResolveNotFoundAction(new CalendarEvent { IsDeleted = true });

        Assert.Equal(GoogleNotFoundSyncAction.MarkLocalSynced, action);
    }

    [Fact]
    public void ResolveNotFoundAction_RecreatesRemoteForLocalEdit()
    {
        var action = GoogleCalendarSyncService.ResolveNotFoundAction(new CalendarEvent { IsDeleted = false });

        Assert.Equal(GoogleNotFoundSyncAction.RecreateRemote, action);
    }

    [Fact]
    public async Task LoadCachedEventColorPaletteAsync_ReturnsSavedGoogleColors()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        await repository.SaveSettingValueAsync(
            "google-event-color-palette",
            """{"5":{"Background":"#123456","Foreground":"#FEDCBA"}}""");
        var service = new GoogleCalendarSyncService(repository);

        var palette = await service.LoadCachedEventColorPaletteAsync();

        Assert.Equal("#123456", palette["5"].Background);
        Assert.Equal("#FEDCBA", palette["5"].Foreground);
    }

    [Theory]
    [InlineData(SyncConflictPolicy.SkipLocalDirty, false)]
    [InlineData(SyncConflictPolicy.PreferLocal, false)]
    [InlineData(SyncConflictPolicy.PreferGoogle, true)]
    public void ShouldApplyRemoteChange_ProtectsDirtyLocalEventsByDefault(SyncConflictPolicy policy, bool expected)
    {
        var local = new CalendarEvent { IsDirty = true };

        var apply = GoogleCalendarSyncService.ShouldApplyRemoteChange(local, policy);

        Assert.Equal(expected, apply);
    }

    [Fact]
    public void ShouldApplyRemoteChange_AllowsCleanLocalEvents()
    {
        var local = new CalendarEvent { IsDirty = false };

        Assert.True(GoogleCalendarSyncService.ShouldApplyRemoteChange(local, SyncConflictPolicy.SkipLocalDirty));
    }

    [Fact]
    public async Task RecordFailedSyncAsync_AlwaysStoresLastResult()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new GoogleCalendarSyncService(repository);

        await service.RecordFailedSyncAsync("network failure", keepHistory: false);
        var diagnostics = await service.LoadDiagnosticsAsync(new AppSettings());

        Assert.NotNull(diagnostics.LastResult);
        Assert.Equal(1, diagnostics.LastResult.Failed);
        Assert.Equal("network failure", diagnostics.LastResult.Message);
        Assert.Single(diagnostics.History);
    }

    [Fact]
    public async Task RecordFailedSyncAsync_KeepsHistoryWhenEnabled()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        var service = new GoogleCalendarSyncService(repository);

        await service.RecordFailedSyncAsync("first", keepHistory: true);
        await service.RecordFailedSyncAsync("second", keepHistory: true);
        var diagnostics = await service.LoadDiagnosticsAsync(new AppSettings());

        Assert.Equal(2, diagnostics.History.Count);
        Assert.Equal("second", diagnostics.LastResult?.Message);
    }

    [Fact]
    public async Task SyncAsync_PushesLocalCreateUpdateAndDeleteOnlyAfterRemoteSuccess()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "local create",
            Start = new DateTimeOffset(2026, 1, 2, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);
        var created = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1))).Single();
        Assert.False(created.IsDirty);
        Assert.NotNull(created.GoogleEventId);
        Assert.True(api.EventsByCalendar["work"].ContainsKey(created.GoogleEventId!));

        created.Title = "local update";
        created.IsDirty = true;
        await repository.SaveEventAsync(created);
        await service.SyncAsync(settings);
        Assert.Contains(api.Operations, item => item == $"update:work:{created.GoogleEventId}");

        await repository.DeleteEventAsync(created);
        await service.SyncAsync(settings);
        var deleted = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1), includeDeleted: true)).Single();
        Assert.False(deleted.IsDirty);
        Assert.True(deleted.IsDeleted);
        Assert.False(api.EventsByCalendar["work"].ContainsKey(created.GoogleEventId!));
    }

    [Fact]
    public async Task SyncAsync_PullsRemoteCreateUpdateAndDelete()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var service = new GoogleCalendarSyncService(repository, api);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-1",
            Summary = "remote create",
            Start = DateTimeEvent(2026, 1, 3, 9),
            End = DateTimeEvent(2026, 1, 3, 10),
            Status = "confirmed"
        });

        await service.SyncAsync(settings);
        var local = (await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero))).Single();
        Assert.Equal("remote create", local.Title);
        Assert.False(local.IsDirty);

        api.UpsertRemote("work", new Event
        {
            Id = "remote-1",
            Summary = "remote update",
            Start = DateTimeEvent(2026, 1, 3, 9),
            End = DateTimeEvent(2026, 1, 3, 10),
            Status = "confirmed"
        });
        await service.SyncAsync(settings);
        local = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1))).Single();
        Assert.Equal("remote update", local.Title);

        api.UpsertRemote("work", new Event
        {
            Id = "remote-1",
            Summary = "remote update",
            Start = DateTimeEvent(2026, 1, 3, 9),
            End = DateTimeEvent(2026, 1, 3, 10),
            Status = "cancelled"
        });
        await service.SyncAsync(settings);
        local = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1), includeDeleted: true)).Single();
        Assert.True(local.IsDeleted);
        Assert.False(local.IsDirty);
    }

    [Fact]
    public async Task SyncAsync_KeepsDirtyLocalChangesWhenPushOrPullFails()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnInsert = true };
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "unsynced",
            Start = new DateTimeOffset(2026, 1, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 4, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);
        var stored = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1))).Single();
        Assert.Equal(1, result.Failed);
        Assert.True(stored.IsDirty);
        Assert.Null(stored.GoogleEventId);

        api.ThrowOnInsert = false;
        await repository.SaveSyncTokenAsync("work", null);
        api.ThrowOnList = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(settings));
        Assert.Null(await repository.GetSyncTokenAsync("work"));
    }

    [Fact]
    public async Task SyncAsync_RecordsFailedDirtyEventDiagnostics()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnInsert = true };
        var settings = CreateSettings("work");
        settings.EnableSyncDiagnostics = true;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "diagnostic target",
            Start = new DateTimeOffset(2026, 1, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 4, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Equal(1, result.Failed);
        var failure = Assert.Single(diagnostics.Failures);
        Assert.Equal("diagnostic target", failure.Title);
        Assert.Equal("work", failure.CalendarId);
        Assert.Equal("作成", failure.Operation);
        Assert.Equal("insert failed", failure.ExceptionMessage);
        var dirty = Assert.Single(diagnostics.DirtyItems);
        Assert.Equal(failure.LocalId, dirty.LocalId);
        Assert.Equal("insert failed", dirty.ErrorMessage);
    }

    [Fact]
    public async Task SyncAsync_DoesNotMixCalendarsWithSameRemoteEventId()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work", "private");
        api.UpsertRemote("work", new Event
        {
            Id = "same-id",
            Summary = "work remote",
            Start = DateTimeEvent(2026, 1, 5, 9),
            End = DateTimeEvent(2026, 1, 5, 10),
            Status = "confirmed"
        });
        var privateLocal = new CalendarEvent
        {
            CalendarId = "private",
            GoogleEventId = "same-id",
            Title = "private local update",
            Start = new DateTimeOffset(2026, 1, 5, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 5, 12, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(privateLocal);
        api.UpsertRemote("private", new Event
        {
            Id = "same-id",
            Summary = "private remote old",
            Start = DateTimeEvent(2026, 1, 5, 11),
            End = DateTimeEvent(2026, 1, 5, 12),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        Assert.Equal("work remote", api.EventsByCalendar["work"]["same-id"].Summary);
        Assert.Equal("private local update", api.EventsByCalendar["private"]["same-id"].Summary);
        var localEvents = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains(localEvents, item => item.CalendarId == "work" && item.GoogleEventId == "same-id" && item.Title == "work remote");
        Assert.Contains(localEvents, item => item.CalendarId == "private" && item.GoogleEventId == "same-id" && item.Title == "private local update");
    }

    [Fact]
    public async Task SyncAsync_DoesNotOverwriteDirtyLocalEventWithRemoteChangeByDefault()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-1",
            Title = "local dirty",
            Start = new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 6, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-1",
            Summary = "remote update",
            Start = DateTimeEvent(2026, 1, 6, 9),
            End = DateTimeEvent(2026, 1, 6, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.PullAsync(settings);

        var stored = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1))).Single();
        Assert.Equal("local dirty", stored.Title);
        Assert.True(stored.IsDirty);
        Assert.Equal("remote update", api.EventsByCalendar["work"]["remote-1"].Summary);
    }

    [Fact]
    public async Task SyncAsync_PushesDirtyEventOutsideConfiguredCalendars()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("primary");
        await repository.SaveEventAsync(new CalendarEvent
        {
            CalendarId = "team",
            Title = "dirty team",
            Start = new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 7, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        Assert.Contains(api.Operations, item => item.StartsWith("insert:team:", StringComparison.Ordinal));
        var stored = (await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero))).Single();
        Assert.False(stored.IsDirty);
        Assert.NotNull(stored.GoogleEventId);
    }

    [Fact]
    public async Task PreviewAndDiagnosticsIncludeDirtyCalendarsOutsideConfiguredCalendars()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("primary");
        await repository.SaveEventAsync(new CalendarEvent
        {
            CalendarId = "team",
            Title = "dirty team",
            Start = new DateTimeOffset(2026, 1, 8, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var preview = await service.PreviewAsync(settings);
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Contains(preview.Calendars, item => item.CalendarId == "team" && item.DirtyCount == 1);
        Assert.Contains(preview.PushItems, item => item.CalendarId == "team" && item.Title == "dirty team");
        Assert.Contains(diagnostics.Calendars, item => item.CalendarId == "team" && item.DirtyCount == 1);
        Assert.Contains(diagnostics.DirtyItems, item => item.CalendarId == "team" && item.Title == "dirty team" && item.Kind == "予定" && item.Operation == "作成");
    }

    [Fact]
    public async Task LoadDiagnosticsAsync_IncludesDirtyScheduleTodoAndDeleteTombstone()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("primary");
        await repository.SaveEventAsync(new CalendarEvent
        {
            CalendarId = "primary",
            Title = "dirty schedule",
            Start = new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        });
        await repository.SaveEventAsync(new CalendarEvent
        {
            CalendarId = "primary",
            Title = "dirty todo",
            Description = "#todoA0%",
            Start = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero),
            IsAllDay = true,
            IsDirty = true
        });
        await repository.SaveEventAsync(new CalendarEvent
        {
            CalendarId = "primary",
            GoogleEventId = "remote-delete",
            Title = "delete tombstone",
            Start = new DateTimeOffset(2026, 1, 12, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 12, 10, 0, 0, TimeSpan.Zero),
            IsDeleted = true,
            IsDirty = true
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Contains(diagnostics.DirtyItems, item => item.Title == "dirty schedule" && item.Kind == "予定" && item.Operation == "作成");
        Assert.Contains(diagnostics.DirtyItems, item => item.Title == "dirty todo" && item.Kind == "ToDo" && item.Operation == "作成");
        Assert.Contains(diagnostics.DirtyItems, item => item.Title == "delete tombstone" && item.Operation == "削除" && item.GoogleEventId == "remote-delete");
    }

    [Fact]
    public async Task MainViewModel_RerunsSyncWhenLocalChangeSyncIsRequestedDuringSync()
    {
        var repository = await CreateRepositoryAsync();
        var listStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueList = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeGoogleCalendarApi
        {
            ListStarted = listStarted,
            ContinueList = continueList
        };
        var settings = CreateSettings("primary");
        settings.SyncAfterLocalChange = true;
        await repository.SaveSettingsAsync(settings);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, api));
        await viewModel.InitializeAsync();

        var manualSync = viewModel.SynchronizeManuallyWithPreviewAsync();
        await listStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.SaveTodoAsync(new DateTime(2026, 1, 9), "A", 0, "queued local change", null);
        continueList.SetResult();
        await manualSync.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(api.Operations, item => item.StartsWith("insert:primary:", StringComparison.Ordinal));
        var stored = (await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero))).Single();
        Assert.False(stored.IsDirty);
        Assert.NotNull(stored.GoogleEventId);
    }

    private static async Task<CalendarRepository> CreateRepositoryAsync()
    {
        var repository = new CalendarRepository(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"));
        await repository.InitializeAsync();
        return repository;
    }

    private static AppSettings CreateSettings(params string[] calendarIds)
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(jsonPath, "{}");
        return new AppSettings
        {
            OAuthClientJsonPath = jsonPath,
            VisibleCalendarIds = calendarIds.ToList(),
            ActiveCalendarId = calendarIds.FirstOrDefault() ?? GoogleCalendarDefaults.PrimaryCalendarId,
            SyncConflictPolicy = SyncConflictPolicy.SkipLocalDirty
        };
    }

    private static EventDateTime DateTimeEvent(int year, int month, int day, int hour)
    {
        return new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero) };
    }

    private sealed class FakeGoogleCalendarApi : IGoogleCalendarApi, IGoogleCalendarClient
    {
        private int _nextId = 1;

        public Dictionary<string, Dictionary<string, Event>> EventsByCalendar { get; } = new(StringComparer.Ordinal);
        public List<string> Operations { get; } = [];
        public bool ThrowOnInsert { get; set; }
        public bool ThrowOnList { get; set; }
        public TaskCompletionSource? ListStarted { get; set; }
        public TaskCompletionSource? ContinueList { get; set; }

        public Task<IGoogleCalendarClient> CreateClientAsync(string clientJsonPath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IGoogleCalendarClient>(this);
        }

        public Task<IReadOnlyDictionary<string, EventDisplayColors>> LoadEventColorPaletteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, EventDisplayColors>>(TagService.DefaultEventColorPalette);
        }

        public Task ClearTokensAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<GoogleCalendarInfo>> ListCalendarsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GoogleCalendarInfo> calendars = EventsByCalendar.Keys.Select(id => new GoogleCalendarInfo(id, id)).ToArray();
            return Task.FromResult(calendars);
        }

        public Task<Event> InsertEventAsync(string calendarId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            if (ThrowOnInsert)
            {
                throw new InvalidOperationException("insert failed");
            }

            var copy = Clone(googleEvent);
            copy.Id = $"fake-{_nextId++}";
            copy.Status ??= "confirmed";
            Calendar(calendarId)[copy.Id] = copy;
            Operations.Add($"insert:{calendarId}:{copy.Id}");
            return Task.FromResult(Clone(copy));
        }

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            var copy = Clone(googleEvent);
            copy.Id = eventId;
            copy.Status ??= "confirmed";
            Calendar(calendarId)[eventId] = copy;
            Operations.Add($"update:{calendarId}:{eventId}");
            return Task.FromResult(Clone(copy));
        }

        public Task DeleteEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
        {
            Calendar(calendarId).Remove(eventId);
            Operations.Add($"delete:{calendarId}:{eventId}");
            return Task.CompletedTask;
        }

        public Task<Event> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
        {
            if (!Calendar(calendarId).TryGetValue(eventId, out var googleEvent))
            {
                throw new KeyNotFoundException(eventId);
            }

            return Task.FromResult(Clone(googleEvent));
        }

        public async Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowOnList)
            {
                throw new InvalidOperationException("list failed");
            }

            if (ListStarted is not null && ContinueList is not null)
            {
                var continueList = ContinueList;
                ListStarted.SetResult();
                ListStarted = null;
                ContinueList = null;
                await continueList.Task.WaitAsync(cancellationToken);
            }

            var events = Calendar(request.CalendarId).Values.Select(Clone).ToArray();
            return new GoogleEventPage(events, null, $"token-{request.CalendarId}-{events.Length}");
        }

        public Task<IReadOnlyList<Event>> ListInstancesAsync(
            string calendarId,
            string recurringEventId,
            DateTimeOffset timeMin,
            DateTimeOffset timeMax,
            bool showDeleted,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Event> events = Calendar(calendarId).Values
                .Where(item => item.RecurringEventId == recurringEventId)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(events);
        }

        public void UpsertRemote(string calendarId, Event googleEvent)
        {
            if (string.IsNullOrWhiteSpace(googleEvent.Id))
            {
                throw new ArgumentException("Remote event must have an id.", nameof(googleEvent));
            }

            Calendar(calendarId)[googleEvent.Id] = Clone(googleEvent);
        }

        private Dictionary<string, Event> Calendar(string calendarId)
        {
            if (!EventsByCalendar.TryGetValue(calendarId, out var events))
            {
                events = new Dictionary<string, Event>(StringComparer.Ordinal);
                EventsByCalendar[calendarId] = events;
            }

            return events;
        }

        private static Event Clone(Event source)
        {
            return new Event
            {
                Id = source.Id,
                Summary = source.Summary,
                Description = source.Description,
                Location = source.Location,
                Status = source.Status,
                ColorId = source.ColorId,
                Start = Clone(source.Start),
                End = Clone(source.End),
                OriginalStartTime = Clone(source.OriginalStartTime),
                RecurringEventId = source.RecurringEventId,
                Recurrence = source.Recurrence?.ToArray(),
                UpdatedDateTimeOffset = source.UpdatedDateTimeOffset
            };
        }

        private static EventDateTime? Clone(EventDateTime? source)
        {
            return source is null
                ? null
                : new EventDateTime
                {
                    Date = source.Date,
                    DateTimeDateTimeOffset = source.DateTimeDateTimeOffset,
                    TimeZone = source.TimeZone
                };
        }
    }
}

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
        var inserted = api.EventsByCalendar["work"][created.GoogleEventId!];
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, inserted.Start.TimeZone);
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, inserted.End.TimeZone);

        created.Title = "local update";
        created.IsDirty = true;
        await repository.SaveEventAsync(created);
        await service.SyncAsync(settings);
        Assert.Contains(api.Operations, item => item == $"update:work:{created.GoogleEventId}");
        var updated = api.EventsByCalendar["work"][created.GoogleEventId!];
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, updated.Start.TimeZone);
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, updated.End.TimeZone);

        await repository.DeleteEventAsync(created);
        await service.SyncAsync(settings);
        var deleted = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1), includeDeleted: true)).Single();
        Assert.False(deleted.IsDirty);
        Assert.True(deleted.IsDeleted);
        Assert.False(api.EventsByCalendar["work"].ContainsKey(created.GoogleEventId!));
    }

    [Fact]
    public async Task DescriptionOnlyEdit_IsDirtyPreviewedAndPushedToGoogle()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        await repository.SaveSettingsAsync(settings);
        var original = new CalendarEvent
        {
            Id = "description-only",
            CalendarId = "work",
            GoogleEventId = "remote-description",
            Title = "Existing event",
            Description = "before",
            Start = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = false,
            UpdatedAt = DateTimeOffset.Now.AddDays(-1)
        };
        await repository.UpsertSyncedEventAsync(original);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-description",
            Summary = original.Title,
            Description = original.Description,
            Start = new EventDateTime { DateTimeDateTimeOffset = original.Start },
            End = new EventDateTime { DateTimeDateTimeOffset = original.End },
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);
        var viewModel = new MainViewModel(repository, service);
        await viewModel.InitializeAsync();
        var storedOriginal = await repository.FindEventByGoogleEventIdAsync("work", "remote-description");
        Assert.NotNull(storedOriginal);
        var previousUpdatedAt = storedOriginal.UpdatedAt;

        viewModel.SelectEvent(storedOriginal);
        viewModel.Description = "after";
        await viewModel.SaveCurrentEventAsync();

        var dirty = Assert.Single(await repository.LoadDirtyEventsAsync(), item => item.Id == original.Id);
        Assert.True(dirty.IsDirty);
        Assert.True(dirty.UpdatedAt > previousUpdatedAt);
        Assert.Equal("Description", dirty.DirtyFields);
        var preview = await service.PreviewAsync(settings);
        var previewItem = Assert.Single(preview.PushItems, item => item.LocalId == original.Id);
        Assert.Equal("Description", previewItem.ChangeFields);

        await service.SyncAsync(settings);

        Assert.Contains(api.Operations, item => item == "update:work:remote-description");
        Assert.Equal("after", api.EventsByCalendar["work"]["remote-description"].Description);
        Assert.DoesNotContain(await repository.LoadDirtyEventsAsync(), item => item.Id == original.Id);
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

        await repository.MarkSyncedAsync(stored);
        api.ThrowOnInsert = false;
        await repository.SaveSyncTokenAsync("work", null);
        api.ThrowOnList = true;
        result = await service.SyncAsync(settings);
        var diagnostics = await service.LoadDiagnosticsAsync(settings);
        Assert.Equal(1, result.Failed);
        var pullFailure = Assert.Single(diagnostics.Failures, item => item.Direction == "Pull");
        Assert.Equal("work", pullFailure.CalendarId);
        Assert.False(pullFailure.SyncTokenPresent);
        Assert.Equal("Pull", pullFailure.FailureCategory);
        Assert.Equal("list failed", pullFailure.ExceptionMessage);
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
    public async Task SyncDirtyEventsAsync_PushesOnlySelectedDirtyLocalIds()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var selected = new CalendarEvent
        {
            CalendarId = "work",
            Title = "selected",
            Start = new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 7, 10, 0, 0, TimeSpan.Zero)
        };
        var untouched = new CalendarEvent
        {
            CalendarId = "work",
            Title = "untouched",
            Start = new DateTimeOffset(2026, 1, 7, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 7, 12, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(selected);
        await repository.SaveEventAsync(untouched);
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { selected.Id });

        Assert.Equal(1, result.Pushed);
        var stored = await repository.LoadDirtyEventsAsync();
        Assert.DoesNotContain(stored, item => item.Id == selected.Id);
        Assert.Contains(stored, item => item.Id == untouched.Id);
        Assert.Single(api.Operations, item => item.StartsWith("insert:work:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SyncDirtyEventsAsync_LinksExactRemoteMatchInsteadOfDuplicatingInsert()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "retry target",
            Location = "room",
            Start = new DateTimeOffset(2026, 1, 8, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-existing",
            Summary = "retry target",
            Location = "room",
            Start = DateTimeEvent(2026, 1, 8, 9),
            End = DateTimeEvent(2026, 1, 8, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id });

        Assert.Equal(1, result.Pushed);
        Assert.DoesNotContain(api.Operations, item => item.StartsWith("insert:work:", StringComparison.Ordinal));
        Assert.Contains("update:work:remote-existing", api.Operations);
        var stored = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1))).Single();
        Assert.False(stored.IsDirty);
        Assert.Equal("remote-existing", stored.GoogleEventId);
    }

    [Fact]
    public async Task SyncAsync_PullsGooglePopupReminderIntoLocalReminder()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        api.UpsertRemote("work", new Event
        {
            Id = "remote-popup",
            Summary = "popup reminder",
            Start = DateTimeEvent(2026, 1, 8, 9),
            End = DateTimeEvent(2026, 1, 8, 10),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides =
                [
                    new EventReminder { Method = "popup", Minutes = 30 },
                    new EventReminder { Method = "popup", Minutes = 10 }
                ]
            }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var stored = (await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero))).Single();
        Assert.Equal(10, stored.ReminderMinutesBeforeStart);
        Assert.Equal([10, 30], stored.GoogleReminderMetadata!.PopupMinutes.Order().ToArray());
    }

    [Fact]
    public async Task SyncAsync_PullsGoogleEmailOnlyReminderAsDiagnosticsMetadataOnly()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        api.UpsertRemote("work", new Event
        {
            Id = "remote-email",
            Summary = "email reminder",
            Start = DateTimeEvent(2026, 1, 8, 11),
            End = DateTimeEvent(2026, 1, 8, 12),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "email", Minutes = 30 }]
            }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var stored = (await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero))).Single();
        Assert.Null(stored.ReminderMinutesBeforeStart);
        Assert.True(stored.GoogleReminderMetadata!.HasEmailOnly);
        Assert.Equal([30], stored.GoogleReminderMetadata.EmailMinutes);
    }

    [Fact]
    public async Task SyncAsync_UsesGoogleDefaultPopupReminderWhenEventUsesDefaults()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        api.DefaultRemindersByCalendar["work"] =
        [
            new GoogleReminderOverride("email", 60),
            new GoogleReminderOverride("popup", 15)
        ];
        var settings = CreateSettings("work");
        api.UpsertRemote("work", new Event
        {
            Id = "remote-default",
            Summary = "default reminder",
            Start = DateTimeEvent(2026, 1, 8, 13),
            End = DateTimeEvent(2026, 1, 8, 14),
            Status = "confirmed",
            Reminders = new Event.RemindersData { UseDefault = true }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var stored = (await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero))).Single();
        Assert.Equal(15, stored.ReminderMinutesBeforeStart);
        Assert.True(stored.GoogleReminderMetadata!.UseDefault);
        Assert.Equal([15], stored.GoogleReminderMetadata.DefaultPopupMinutes);
    }

    [Fact]
    public async Task SyncAsync_PushesLocalReminderWithoutEmailReminder()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "local reminder",
            Start = new DateTimeOffset(2026, 1, 8, 15, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 16, 0, 0, TimeSpan.Zero),
            ReminderMinutesBeforeStart = 10
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var remote = Assert.Single(api.EventsByCalendar["work"].Values);
        Assert.False(remote.Reminders.UseDefault);
        var reminder = Assert.Single(remote.Reminders.Overrides);
        Assert.Equal("popup", reminder.Method);
        Assert.Equal(10, reminder.Minutes);
    }

    [Fact]
    public async Task SyncAsync_PushesNoReminderAsExplicitEmptyOverrides()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "local no reminder",
            Start = new DateTimeOffset(2026, 1, 8, 17, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 18, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var remote = Assert.Single(api.EventsByCalendar["work"].Values);
        Assert.False(remote.Reminders.UseDefault);
        Assert.Empty(remote.Reminders.Overrides);
    }

    [Fact]
    public async Task RefreshReminderMetadataAsync_UpdatesGoogleReminderMetadataWithoutFullPull()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-reminder-refresh",
            Title = "refresh reminder",
            Start = new DateTimeOffset(2026, 1, 8, 19, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 20, 0, 0, TimeSpan.Zero)
        };
        await repository.UpsertSyncedEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-reminder-refresh",
            Summary = "refresh reminder",
            Start = DateTimeEvent(2026, 1, 8, 19),
            End = DateTimeEvent(2026, 1, 8, 20),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 20 }]
            }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var updated = await service.RefreshReminderMetadataAsync(
            settings,
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero));

        var stored = await repository.FindEventByGoogleEventIdAsync("work", "remote-reminder-refresh");
        Assert.Equal(1, updated);
        Assert.Equal(20, stored!.ReminderMinutesBeforeStart);
        Assert.Equal([20], stored.GoogleReminderMetadata!.PopupMinutes);
    }

    [Fact]
    public async Task RefreshReminderMetadataAsync_PreservesDirtyLocalReminderMinutes()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-dirty-reminder",
            Title = "dirty reminder",
            Start = new DateTimeOffset(2026, 1, 8, 21, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 22, 0, 0, TimeSpan.Zero),
            ReminderMinutesBeforeStart = 5
        };
        await repository.UpsertSyncedEventAsync(local);
        local.ReminderMinutesBeforeStart = 10;
        local.IsDirty = true;
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-dirty-reminder",
            Summary = "dirty reminder",
            Start = DateTimeEvent(2026, 1, 8, 21),
            End = DateTimeEvent(2026, 1, 8, 22),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 30 }]
            }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var updated = await service.RefreshReminderMetadataAsync(
            settings,
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero));

        var stored = await repository.FindEventByGoogleEventIdAsync("work", "remote-dirty-reminder");
        Assert.Equal(1, updated);
        Assert.True(stored!.IsDirty);
        Assert.Equal(10, stored.ReminderMinutesBeforeStart);
        Assert.Equal([30], stored.GoogleReminderMetadata!.PopupMinutes);
    }

    [Fact]
    public async Task RefreshReminderMetadataAsync_UpsertsMissingRemoteEvents()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        api.UpsertRemote("work", new Event
        {
            Id = "remote-reminder-new",
            Summary = "remote reminder new",
            Start = DateTimeEvent(2026, 1, 8, 19),
            End = DateTimeEvent(2026, 1, 8, 20),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 15 }]
            }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var updated = await service.RefreshReminderMetadataAsync(
            settings,
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero));

        var stored = await repository.FindEventByGoogleEventIdAsync("work", "remote-reminder-new");
        Assert.Equal(1, updated);
        Assert.NotNull(stored);
        Assert.Equal("remote reminder new", stored.Title);
        Assert.False(stored.IsDirty);
        Assert.Equal(15, stored.ReminderMinutesBeforeStart);
        Assert.Equal([15], stored.GoogleReminderMetadata!.PopupMinutes);
    }

    [Fact]
    public async Task SyncDirtyEventsAsync_UpdatesDescriptionOnStructurallyMatchingRemoteEvent()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "description retry target",
            Description = "remote description",
            Location = "room",
            Start = new DateTimeOffset(2026, 1, 8, 13, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 14, 0, 0, TimeSpan.Zero),
        };
        await repository.UpsertSyncedEventAsync(local);
        local.Description = "local description";
        local.IsDirty = true;
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-description-match",
            Summary = local.Title,
            Description = "remote description",
            Location = local.Location,
            Start = DateTimeEvent(2026, 1, 8, 13),
            End = DateTimeEvent(2026, 1, 8, 14),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id });

        Assert.Equal(1, result.Pushed);
        Assert.DoesNotContain(api.Operations, item => item.StartsWith("insert:work:", StringComparison.Ordinal));
        Assert.Contains("update:work:remote-description-match", api.Operations);
        Assert.Equal("local description", api.EventsByCalendar["work"]["remote-description-match"].Description);
        var stored = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1))).Single();
        Assert.False(stored.IsDirty);
        Assert.Null(stored.DirtyFields);
        Assert.Equal("remote-description-match", stored.GoogleEventId);
    }

    [Fact]
    public async Task SyncDirtyEventsAsync_KeepsDescriptionDirtyWhenMatchedRemoteUpdateFails()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnUpdate = true };
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "failed description retry",
            Description = "remote description",
            Start = new DateTimeOffset(2026, 1, 8, 15, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 16, 0, 0, TimeSpan.Zero),
        };
        await repository.UpsertSyncedEventAsync(local);
        local.Description = "local description";
        local.IsDirty = true;
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-description-failure",
            Summary = local.Title,
            Description = "remote description",
            Start = DateTimeEvent(2026, 1, 8, 15),
            End = DateTimeEvent(2026, 1, 8, 16),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id });

        Assert.Equal(1, result.Failed);
        Assert.DoesNotContain(api.Operations, item => item.StartsWith("insert:work:", StringComparison.Ordinal));
        var dirty = Assert.Single(await repository.LoadDirtyEventsAsync(), item => item.Id == local.Id);
        Assert.True(dirty.IsDirty);
        Assert.Equal("Description", dirty.DirtyFields);
        Assert.Null(dirty.GoogleEventId);
    }

    [Fact]
    public async Task DiscardLocalChangesAsync_DeletesLocalNewAndRestoresRemoteLinkedDirtyEvent()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var localNew = new CalendarEvent
        {
            CalendarId = "work",
            Title = "local new",
            Start = new DateTimeOffset(2026, 1, 9, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 9, 10, 0, 0, TimeSpan.Zero)
        };
        var linkedDirty = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-restore",
            Title = "local dirty title",
            Start = new DateTimeOffset(2026, 1, 9, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 9, 12, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(localNew);
        await repository.SaveEventAsync(linkedDirty);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-restore",
            Summary = "remote title",
            Start = DateTimeEvent(2026, 1, 9, 11),
            End = DateTimeEvent(2026, 1, 9, 12),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.DiscardLocalChangesAsync(settings, new HashSet<string>(StringComparer.Ordinal) { localNew.Id, linkedDirty.Id });

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Pulled);
        var events = await repository.LoadEventsAsync(
            new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero));
        Assert.DoesNotContain(events, item => item.Id == localNew.Id);
        var restored = Assert.Single(events, item => item.GoogleEventId == "remote-restore");
        Assert.Equal("remote title", restored.Title);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task DiscardLocalChangesAsync_LeavesLinkedDirtyEventWhenRemoteCannotBeFetched()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var linkedDirty = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "missing",
            Title = "keep local",
            Start = new DateTimeOffset(2026, 1, 9, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 9, 12, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(linkedDirty);
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.DiscardLocalChangesAsync(settings, new HashSet<string>(StringComparer.Ordinal) { linkedDirty.Id });

        Assert.Equal(1, result.Failed);
        var stored = (await repository.LoadEventsAsync(linkedDirty.Start.AddDays(-1), linkedDirty.End.AddDays(1))).Single();
        Assert.Equal("keep local", stored.Title);
        Assert.True(stored.IsDirty);
        var diagnostics = await service.LoadDiagnosticsAsync(settings);
        Assert.Single(diagnostics.Failures);
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

    [Fact]
    public async Task MainViewModel_ShowsCalendarReloadingStatusAfterSyncBeforeRefresh()
    {
        var repository = await CreateRepositoryAsync();
        await repository.SaveSettingsAsync(CreateSettings("primary"));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, new FakeGoogleCalendarApi()));
        await viewModel.InitializeAsync();
        var observedStatus = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.BeforeLoadCalendarSnapshotAsync = (_, _) =>
        {
            if (viewModel.Status == "カレンダー再読み込み中...")
            {
                observedStatus.TrySetResult(viewModel.Status);
            }

            return Task.CompletedTask;
        };

        await viewModel.SynchronizeManuallyAsync();

        Assert.Equal("カレンダー再読み込み中...", await observedStatus.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task MainViewModel_ManualSyncRefreshesGoogleReminderMetadataAfterSync()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        api.UpsertRemote("primary", new Event
        {
            Id = "remote-manual-reminder-refresh",
            Summary = "manual reminder refresh",
            Start = DateTimeEvent(2026, 1, 8, 19),
            End = DateTimeEvent(2026, 1, 8, 20),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 25 }]
            }
        });
        await repository.SaveSettingsAsync(CreateSettings("primary"));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, api));
        await viewModel.InitializeAsync();

        await viewModel.SynchronizeManuallyAsync();

        Assert.Contains(api.ListRequests, request =>
            request.CalendarId == "primary"
            && request.TimeMax is not null
            && request.ShowDeleted == false);
        Assert.Contains("Google通知設定再取得", viewModel.Status);
        Assert.Contains("1", viewModel.Status);
    }

    [Fact]
    public async Task MainViewModel_AutomaticSyncDoesNotRefreshGoogleReminderMetadataEveryRun()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        api.UpsertRemote("primary", new Event
        {
            Id = "remote-auto-reminder-refresh",
            Summary = "auto reminder refresh",
            Start = DateTimeEvent(2026, 1, 8, 19),
            End = DateTimeEvent(2026, 1, 8, 20),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 25 }]
            }
        });
        var settings = CreateSettings("primary");
        settings.AutomaticSyncIntervalMinutes = 30;
        settings.LastAutomaticSyncAt = DateTimeOffset.Now.AddMinutes(-31);
        await repository.SaveSettingsAsync(settings);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, api));
        await viewModel.InitializeAsync();
        api.ListRequests.Clear();

        await viewModel.RunAutomaticSyncIfDueAsync();

        Assert.DoesNotContain(api.ListRequests, request =>
            request.CalendarId == "primary"
            && request.TimeMax is not null
            && request.ShowDeleted == false);
    }

    [Fact]
    public async Task MainViewModel_ManualSyncRequestedDuringAutomaticSyncRerunsWithReminderRefresh()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        api.UpsertRemote("primary", new Event
        {
            Id = "remote-pending-manual-refresh",
            Summary = "pending manual reminder refresh",
            Start = DateTimeEvent(2026, 1, 8, 19),
            End = DateTimeEvent(2026, 1, 8, 20),
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 25 }]
            }
        });
        var settings = CreateSettings("primary");
        settings.AutomaticSyncIntervalMinutes = 30;
        settings.LastAutomaticSyncAt = DateTimeOffset.Now.AddMinutes(-31);
        await repository.SaveSettingsAsync(settings);
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, api));
        await viewModel.InitializeAsync();
        api.ListRequests.Clear();
        var listStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueList = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.ListStarted = listStarted;
        api.ContinueList = continueList;

        var automaticSync = viewModel.RunAutomaticSyncIfDueAsync();
        await listStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var manualResult = await viewModel.SynchronizeManuallyAsync();
        continueList.SetResult();
        await automaticSync.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(manualResult);
        Assert.Contains(api.ListRequests, request =>
            request.CalendarId == "primary"
            && request.TimeMax is not null
            && request.ShowDeleted == false);
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
        public Dictionary<string, IReadOnlyList<GoogleReminderOverride>> DefaultRemindersByCalendar { get; } = new(StringComparer.Ordinal);
        public List<string> Operations { get; } = [];
        public List<GoogleEventListRequest> ListRequests { get; } = [];
        public bool ThrowOnInsert { get; set; }
        public bool ThrowOnUpdate { get; set; }
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
            IReadOnlyList<GoogleCalendarInfo> calendars = EventsByCalendar.Keys
                .Union(DefaultRemindersByCalendar.Keys, StringComparer.Ordinal)
                .Select(id => new GoogleCalendarInfo(
                    id,
                    id,
                    DefaultRemindersByCalendar.TryGetValue(id, out var reminders) ? reminders : null))
                .ToArray();
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
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("update failed");
            }

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
            ListRequests.Add(request);
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
                UpdatedDateTimeOffset = source.UpdatedDateTimeOffset,
                Reminders = source.Reminders is null
                    ? null
                    : new Event.RemindersData
                    {
                        UseDefault = source.Reminders.UseDefault,
                        Overrides = source.Reminders.Overrides?
                            .Select(item => new EventReminder { Method = item.Method, Minutes = item.Minutes })
                            .ToArray()
                    }
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

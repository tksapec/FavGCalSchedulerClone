using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Google;
using Google.Apis.Calendar.v3.Data;

namespace FavGCalSchedulerClone.Tests;

public sealed class GoogleCalendarSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_TodoPushLocalDisablesGoogleRemindersWithOneUpdate()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            Id = "todo-one-update",
            CalendarId = "work",
            GoogleEventId = "todo-one-update-remote",
            LastSyncedGoogleEtag = "etag-before",
            Title = "#todoA0% local",
            Start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero),
            IsAllDay = true,
            IsTodoLike = true,
            IsDirty = true,
            DirtyFields = "Title",
            ReminderMinutesBeforeStart = 30,
            IsAppReminderEnabled = true,
            AppReminderMinutesBeforeStart = [30]
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            ETag = local.LastSyncedGoogleEtag,
            Summary = "#todoA0% remote",
            Start = new EventDateTime { Date = "2026-08-01" },
            End = new EventDateTime { Date = "2026-08-02" },
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "popup", Minutes = 30 }]
            }
        });

        var result = await new GoogleCalendarSyncService(repository, api).SyncAsync(settings);

        Assert.Equal(1, result.Pushed);
        Assert.Single(api.Operations, operation => operation == $"update:work:{local.GoogleEventId}");
        var remote = api.EventsByCalendar["work"][local.GoogleEventId];
        Assert.False(remote.Reminders!.UseDefault);
        Assert.Empty(remote.Reminders.Overrides!);
        var stored = Assert.IsType<CalendarEvent>(await repository.FindEventByIdAsync(local.Id));
        Assert.False(stored.IsDirty);
        Assert.Empty(stored.AppReminderMinutesBeforeStart);
        Assert.Null(stored.ReminderMinutesBeforeStart);
        Assert.NotEqual("etag-before", stored.LastSyncedGoogleEtag);
    }

    [Fact]
    public async Task PreviewAsync_ListFailureIsReportedWithoutChangingLocalStateOrToken()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnList = true };
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            Id = "preview-list-failure", CalendarId = "work", Title = "local dirty",
            Start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), IsDirty = true
        };
        await repository.SaveEventAsync(local);
        await repository.SaveSyncTokenAsync("work", "keep-token");

        var preview = await new GoogleCalendarSyncService(repository, api).PreviewAsync(settings);

        var error = Assert.Single(preview.ErrorItems);
        Assert.Equal("Google予定を取得できませんでした", error.Title);
        Assert.Contains("ローカル予定は変更されていません", error.Detail);
        Assert.Empty(preview.PushItems);
        Assert.Empty(preview.PullItems);
        Assert.Empty(preview.DeleteItems);
        Assert.Empty(preview.ConflictItems);
        Assert.Equal("keep-token", await repository.GetSyncTokenAsync("work"));
        Assert.True((await repository.FindEventByIdAsync(local.Id))!.IsDirty);
    }

    [Fact]
    public async Task PreviewAsync_DirtyLookupFailureIsReportedAndNotPlannedForPush()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnGet = true };
        var settings = CreateSettings("work");
        var original = new CalendarEvent
        {
            Id = "preview-get-failure", CalendarId = "work", GoogleEventId = "missing-from-list",
            LastSyncedGoogleEtag = "etag-before", Title = "営業会議", Description = "変更前",
            Start = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), IsDirty = false
        };
        await repository.UpsertSyncedEventAsync(original);

        var local = await repository.FindEventByIdAsync(original.Id);
        Assert.NotNull(local);
        Assert.False(local.IsDirty);
        Assert.NotNull(local.GoogleEventId);

        local.Description = "変更後";
        local.IsDirty = true;
        await repository.SaveEventAsync(local);

        var storedDirty = await repository.FindEventByIdAsync(original.Id);
        Assert.NotNull(storedDirty);
        Assert.True(storedDirty.IsDirty);
        Assert.Equal("Description", storedDirty.DirtyFields);

        var preview = await new GoogleCalendarSyncService(repository, api).PreviewAsync(settings);

        var error = Assert.Single(preview.ErrorItems);
        Assert.Equal(original.Id, error.LocalId);
        Assert.Equal(original.GoogleEventId, error.GoogleEventId);
        Assert.Equal("Description", error.ChangeFields);
        Assert.Contains("今回の同期対象から除外", error.Detail);
        Assert.Empty(preview.PushItems);
        Assert.Empty(preview.PullItems);
        Assert.Empty(preview.DeleteItems);
        Assert.Empty(preview.ConflictItems);
        Assert.DoesNotContain(api.Operations, operation => operation.StartsWith("update:", StringComparison.Ordinal));
        Assert.Null(await repository.GetSyncTokenAsync("work"));

        var afterPreview = await repository.FindEventByIdAsync(original.Id);
        Assert.NotNull(afterPreview);
        Assert.True(afterPreview.IsDirty);
        Assert.Equal("Description", afterPreview.DirtyFields);
    }

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
        Assert.Equal("fake-etag-1", created.LastSyncedGoogleEtag);
        Assert.True(api.EventsByCalendar["work"].ContainsKey(created.GoogleEventId!));
        var inserted = api.EventsByCalendar["work"][created.GoogleEventId!];
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, inserted.Start.TimeZone);
        Assert.Equal(GoogleCalendarTimeZone.TokyoIanaId, inserted.End.TimeZone);

        created.Title = "local update";
        created.IsDirty = true;
        await repository.SaveEventAsync(created);
        await service.SyncAsync(settings);
        Assert.Contains(api.Operations, item => item == $"update:work:{created.GoogleEventId}");
        var updatedLocal = (await repository.FindEventByIdAsync(created.Id))!;
        Assert.Equal("fake-etag-2", updatedLocal.LastSyncedGoogleEtag);
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
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
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
    public async Task PreviewAsync_IncludesFieldDiffsForDirtyLocalUpdateAgainstGoogle()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            Id = "field-diff-local",
            CalendarId = "work",
            GoogleEventId = "remote-field-diff",
            Title = "Local title",
            Description = "Local description",
            Location = "Local room",
            Start = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 6, 10, 10, 0, 0, TimeSpan.FromHours(9)),
            ReminderMinutesBeforeStart = 10,
            IsAppReminderEnabled = true,
            IsGoogleEmailReminderEnabled = false,
            GoogleReminderMetadata = new GoogleReminderMetadata
            {
                UseDefault = false,
                Source = "explicit",
                AdoptedReminderMinutes = 10,
                AdoptedReminderMethod = "popup",
                PopupMinutes = [10]
            },
            IsDirty = true,
            DirtyFields = "Title,Description,Location,Reminder"
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-field-diff",
            Summary = "Google title",
            Description = "Google description",
            Location = "Google room",
            Start = new EventDateTime { DateTimeDateTimeOffset = local.Start.AddHours(1) },
            End = new EventDateTime { DateTimeDateTimeOffset = local.End.AddHours(1) },
            Status = "confirmed",
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides = [new EventReminder { Method = "email", Minutes = 30 }]
            }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var preview = await service.PreviewAsync(settings);

        var item = Assert.Single(preview.PushItems, entry => entry.LocalId == "field-diff-local");
        Assert.Contains(item.FieldDiffs!, diff => diff.FieldName == "Title" && diff.LocalValue == "Local title" && diff.GoogleValue == "Google title" && diff.IsDifferent);
        Assert.Contains(item.FieldDiffs!, diff => diff.FieldName == "Description" && diff.LocalValue == "Local description" && diff.GoogleValue == "Google description" && diff.IsDifferent);
        Assert.Contains(item.FieldDiffs!, diff => diff.FieldName == "Location" && diff.LocalValue == "Local room" && diff.GoogleValue == "Google room" && diff.IsDifferent);
        Assert.Contains(item.FieldDiffs!, diff => diff.FieldName == "Reminder" && diff.LocalValue.Contains("popup 10") && diff.GoogleValue.Contains("email 30") && diff.IsDifferent);
    }

    [Fact]
    public async Task PreviewAsync_IncludesFieldDiffsForRemoteDirtyConflict()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            Id = "conflict-local",
            CalendarId = "work",
            GoogleEventId = "remote-conflict",
            Title = "Local conflict",
            Description = "Local body",
            Start = new DateTimeOffset(2026, 6, 11, 9, 0, 0, TimeSpan.FromHours(9)),
            End = new DateTimeOffset(2026, 6, 11, 10, 0, 0, TimeSpan.FromHours(9)),
            IsDirty = true,
            DirtyFields = "Title"
        };
        await repository.SaveEventAsync(local);
        await repository.SaveSyncTokenAsync("work", "preview-conflict-token");
        api.UpsertRemote("work", new Event
        {
            Id = "remote-conflict",
            Summary = "Google conflict",
            Description = "Google body",
            Start = new EventDateTime { DateTimeDateTimeOffset = local.Start },
            End = new EventDateTime { DateTimeDateTimeOffset = local.End },
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var preview = await service.PreviewAsync(settings);

        var item = Assert.Single(preview.ConflictItems, entry => entry.LocalId == "conflict-local");
        Assert.Contains(nameof(SyncConflictPolicy.SkipLocalDirty), item.Detail);
        Assert.Contains(item.FieldDiffs!, diff => diff.FieldName == "Title" && diff.LocalValue == "Local conflict" && diff.GoogleValue == "Google conflict" && diff.IsDifferent);
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
    public async Task SyncAsync_AndPreviewAsync_RetryExpiredTokenWithCompletePagedRemoteDelta()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { PageSize = 1 };
        var settings = CreateSettings("work");
        var service = new GoogleCalendarSyncService(repository, api);
        api.UpsertRemote("work", new Event { Id = "remote-first", Summary = "First remote event", Start = DateTimeEvent(2026, 8, 1, 9), End = DateTimeEvent(2026, 8, 1, 10), Status = "confirmed" });
        api.UpsertRemote("work", new Event { Id = "remote-second", Summary = "Second remote event", Start = DateTimeEvent(2026, 8, 2, 9), End = DateTimeEvent(2026, 8, 2, 10), Status = "confirmed" });
        await repository.SaveSyncTokenAsync("work", "stale-token");
        api.StaleSyncTokens.Add("stale-token");

        var preview = await service.PreviewAsync(settings);

        Assert.Equal(2, preview.PullItems.Count);
        Assert.Equal("stale-token", await repository.GetSyncTokenAsync("work"));
        Assert.Collection(api.ListRequests,
            request => Assert.Equal("stale-token", request.SyncToken),
            request => { Assert.Null(request.SyncToken); Assert.Null(request.PageToken); },
            request => { Assert.Null(request.SyncToken); Assert.Equal("1", request.PageToken); });

        api.ListRequests.Clear();
        api.StaleSyncTokens.Add("stale-token");
        var result = await service.SyncAsync(settings);

        Assert.Equal(2, result.Pulled);
        Assert.Equal("token-work-2", await repository.GetSyncTokenAsync("work"));
        Assert.Collection(api.ListRequests,
            request => Assert.Equal("stale-token", request.SyncToken),
            request => { Assert.Null(request.SyncToken); Assert.Null(request.PageToken); },
            request => { Assert.Null(request.SyncToken); Assert.Equal("1", request.PageToken); });
    }

    [Theory]
    [InlineData(SyncConflictPolicy.SkipLocalDirty, "Local change", true, 0, 1)]
    [InlineData(SyncConflictPolicy.PreferLocal, "Local change", false, 1, 0)]
    [InlineData(SyncConflictPolicy.PreferGoogle, "Google change", false, 0, 0)]
    public async Task SyncAsync_AndPreviewAsync_ExpiredTokenFullSnapshotUsesConflictPolicy(
        SyncConflictPolicy policy,
        string expectedTitle,
        bool expectedDirty,
        int expectedPushed,
        int expectedSkipped)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = policy;
        var local = new CalendarEvent
        {
            Id = "local-dirty",
            CalendarId = "work",
            GoogleEventId = "remote-matching",
            Title = "Local change",
            Start = DateTimeEvent(2026, 8, 3, 9).DateTimeDateTimeOffset!.Value,
            End = DateTimeEvent(2026, 8, 3, 10).DateTimeDateTimeOffset!.Value,
            IsDirty = true,
            DirtyFields = "Title"
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-matching",
            Summary = "Google change",
            Start = DateTimeEvent(2026, 8, 3, 9),
            End = DateTimeEvent(2026, 8, 3, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);
        await repository.SaveSyncTokenAsync("work", "stale-token");
        api.StaleSyncTokens.Add("stale-token");

        var preview = await service.PreviewAsync(settings);

        Assert.Equal(policy == SyncConflictPolicy.SkipLocalDirty, preview.ConflictItems.Count == 1);

        await repository.SaveSyncTokenAsync("work", "stale-token");
        api.StaleSyncTokens.Add("stale-token");
        var result = await service.SyncAsync(settings);

        Assert.Equal(expectedPushed, result.Pushed);
        Assert.Equal(expectedSkipped, result.Skipped);
        Assert.Equal(expectedTitle, (await repository.FindEventByIdAsync(local.Id))!.Title);
        Assert.Equal(expectedDirty, (await repository.FindEventByIdAsync(local.Id))!.IsDirty);
    }

    [Theory]
    [InlineData(SyncConflictPolicy.SkipLocalDirty, "Local title", true, 1, "old-token")]
    [InlineData(SyncConflictPolicy.PreferLocal, "Local title", false, 0, "token-work-1")]
    [InlineData(SyncConflictPolicy.PreferGoogle, "Google title", false, 0, "token-work-1")]
    public async Task SyncAsync_ExecutesRemoteDeltaConflictPolicyBeforeDirtyPush(
        SyncConflictPolicy policy,
        string expectedLocalTitle,
        bool expectedDirty,
        int expectedConflicts,
        string expectedToken)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = policy;
        var local = new CalendarEvent
        {
            Id = "policy-local",
            CalendarId = "work",
            GoogleEventId = "policy-remote",
            Title = "Local title",
            Description = "Local description",
            Start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true,
            DirtyFields = "Title,Description"
        };
        await repository.SaveEventAsync(local);
        await repository.SaveSyncTokenAsync("work", "old-token");
        api.UpsertRemote("work", new Event
        {
            Id = "policy-remote",
            Summary = "Google title",
            Description = "Google description",
            Start = DateTimeEvent(2026, 7, 1, 9),
            End = DateTimeEvent(2026, 7, 1, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);

        var stored = await repository.FindEventByIdAsync(local.Id);
        Assert.NotNull(stored);
        Assert.Equal(expectedLocalTitle, stored!.Title);
        Assert.Equal(expectedDirty, stored.IsDirty);
        Assert.Equal(expectedConflicts, result.Conflicts);
        Assert.Equal(expectedToken, await repository.GetSyncTokenAsync("work"));
        Assert.Equal("list:work", api.Operations[0]);
        Assert.Equal(policy == SyncConflictPolicy.PreferLocal, api.Operations.Contains("update:work:policy-remote"));
        Assert.Equal(policy == SyncConflictPolicy.PreferLocal ? "Local title" : "Google title", api.EventsByCalendar["work"]["policy-remote"].Summary);
    }

    [Fact]
    public async Task SyncAsync_PreferLocalUsesPlannedRemoteEventAndPreservesRemoteOwnedFields()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnGet = true };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            Id = "prefer-local-planned",
            CalendarId = "work",
            GoogleEventId = "prefer-local-remote",
            Title = "Local title",
            Start = new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        await repository.SaveSyncTokenAsync("work", "old-token");
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "Google title",
            Start = DateTimeEvent(2026, 7, 2, 9),
            End = DateTimeEvent(2026, 7, 2, 10),
            Status = "confirmed",
            Attendees = [new EventAttendee { Email = "guest@example.test" }]
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);

        Assert.Equal(0, result.Failed);
        var remote = api.EventsByCalendar["work"][local.GoogleEventId];
        Assert.Equal("Local title", remote.Summary);
        Assert.Contains(remote.Attendees!, attendee => attendee.Email == "guest@example.test");
    }

    [Fact]
    public async Task PreviewAsync_PreferLocalConflictUsesPlannedRemoteSnapshotForFieldDiffs()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnGet = true };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            Id = "prefer-local-preview",
            CalendarId = "work",
            GoogleEventId = "prefer-local-preview-remote",
            Title = "Local title",
            Start = new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        await repository.SaveSyncTokenAsync("work", "old-token");
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "Google title",
            Start = DateTimeEvent(2026, 7, 3, 9),
            End = DateTimeEvent(2026, 7, 3, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var preview = await service.PreviewAsync(settings);

        var push = Assert.Single(preview.PushItems, item => item.LocalId == local.Id);
        Assert.NotNull(push.FieldDiffs);
        var titleDiff = Assert.Single(push.FieldDiffs!, diff => diff.FieldName == "Title");
        Assert.True(titleDiff.IsDifferent);
        Assert.Equal("Local title", titleDiff.LocalValue);
        Assert.Equal("Google title", titleDiff.GoogleValue);
        Assert.DoesNotContain(preview.PullItems, item => item.GoogleEventId == local.GoogleEventId);
    }

    [Fact]
    public async Task SyncAsync_FailedPlannedItemRetainsPriorSyncToken()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnUpdate = true };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            Id = "failed-planned-item",
            CalendarId = "work",
            GoogleEventId = "failed-planned-remote",
            Title = "Local title",
            Start = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        await repository.SaveSyncTokenAsync("work", "old-token");
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "Google title",
            Start = DateTimeEvent(2026, 7, 4, 9),
            End = DateTimeEvent(2026, 7, 4, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);

        Assert.Equal(1, result.Failed);
        Assert.Equal("old-token", await repository.GetSyncTokenAsync("work"));
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
    public async Task LoadDiagnosticsAsync_HidesFailureDiagnosticsForItemsThatAreNoLongerDirty()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnInsert = true };
        var settings = CreateSettings("work");
        settings.EnableSyncDiagnostics = true;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "stale diagnostic target",
            Start = new DateTimeOffset(2026, 1, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 4, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);
        await service.SyncAsync(settings);
        Assert.Single((await service.LoadDiagnosticsAsync(settings)).Failures);

        await repository.MarkSyncedByIdsAsync([local.Id]);
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Empty(diagnostics.Failures);
        Assert.Empty(diagnostics.DirtyItems);
    }

    [Fact]
    public async Task SyncAsync_KeepsOnlyCurrentFailuresWhenSomeDirtyItemsSucceed()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        api.FailedInsertTitles.Add("still failing");
        var settings = CreateSettings("work");
        settings.EnableSyncDiagnostics = true;
        var succeeds = new CalendarEvent
        {
            CalendarId = "work",
            Title = "now succeeds",
            Start = new DateTimeOffset(2026, 1, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 4, 10, 0, 0, TimeSpan.Zero)
        };
        var fails = new CalendarEvent
        {
            CalendarId = "work",
            Title = "still failing",
            Start = new DateTimeOffset(2026, 1, 4, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 4, 12, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(succeeds);
        await repository.SaveEventAsync(fails);
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Equal(1, result.Pushed);
        Assert.Equal(1, result.Failed);
        var failure = Assert.Single(diagnostics.Failures);
        Assert.Equal(fails.Id, failure.LocalId);
        Assert.Equal("still failing", failure.Title);
        var dirty = Assert.Single(diagnostics.DirtyItems);
        Assert.Equal(fails.Id, dirty.LocalId);
        Assert.Equal("insert failed for still failing", dirty.ErrorMessage);
        Assert.DoesNotContain(await repository.LoadDirtyEventsAsync(), item => item.Id == succeeds.Id);
    }

    [Fact]
    public async Task ClearSyncDiagnosticsAsync_RemovesLogsButKeepsDirtyItems()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnInsert = true };
        var settings = CreateSettings("work");
        settings.EnableSyncDiagnostics = true;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "dirty remains",
            Start = new DateTimeOffset(2026, 1, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 4, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);
        await service.SyncAsync(settings);

        await service.ClearSyncDiagnosticsAsync();
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Null(diagnostics.LastResult);
        Assert.Empty(diagnostics.History);
        Assert.Empty(diagnostics.Failures);
        var dirty = Assert.Single(diagnostics.DirtyItems);
        Assert.Equal(local.Id, dirty.LocalId);
        Assert.Null(dirty.ErrorMessage);
    }

    [Fact]
    public async Task SyncAsync_DoesNotMixCalendarsWithSameRemoteEventId()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work", "private");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
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
    public async Task PullAsync_ClearsPreviousPullFailureDiagnosticsAfterSuccessfulPull()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnList = true };
        var settings = CreateSettings("work");
        var service = new GoogleCalendarSyncService(repository, api);
        await service.PullAsync(settings);
        Assert.Single((await service.LoadDiagnosticsAsync(settings)).Failures);
        api.ThrowOnList = false;
        api.UpsertRemote("work", new Event
        {
            Id = "remote-pull-success",
            Summary = "pull success",
            Start = DateTimeEvent(2026, 1, 6, 9),
            End = DateTimeEvent(2026, 1, 6, 10),
            Status = "confirmed"
        });

        var pulled = await service.PullAsync(settings);
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Equal(1, pulled);
        Assert.Empty(diagnostics.Failures);
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

    [Theory]
    [InlineData("[\"RRULE:FREQ=WEEKLY;COUNT=4\"]", "RRULE:FREQ=WEEKLY;COUNT=4")]
    [InlineData(null, null)]
    public async Task SyncAsync_RecurringMasterUpdatesOrClearsGoogleRecurrence(string? localRecurrence, string? expectedRule)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-master",
            Title = "local master",
            Start = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
            RecurrenceJson = localRecurrence,
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "remote master",
            Start = DateTimeEvent(2026, 8, 3, 9),
            End = DateTimeEvent(2026, 8, 3, 10),
            Status = "confirmed",
            Recurrence = ["RRULE:FREQ=DAILY;COUNT=9"]
        });

        await new GoogleCalendarSyncService(repository, api).SyncAsync(settings);

        var recurrence = api.EventsByCalendar["work"][local.GoogleEventId].Recurrence;
        Assert.Equal(expectedRule, recurrence?.SingleOrDefault());
    }

    [Fact]
    public async Task SyncAsync_RecurrenceExceptionDoesNotChangeGoogleMasterRecurrence()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-exception",
            RecurringEventId = "remote-master",
            IsRecurrenceException = true,
            OriginalStart = new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero),
            Title = "local exception",
            Start = new DateTimeOffset(2026, 8, 4, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            RecurrenceJson = "[\"RRULE:FREQ=YEARLY\"]",
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            RecurringEventId = "remote-master",
            Summary = "remote exception",
            Start = DateTimeEvent(2026, 8, 4, 9),
            End = DateTimeEvent(2026, 8, 4, 10),
            OriginalStartTime = DateTimeEvent(2026, 8, 4, 9),
            Status = "confirmed",
            Recurrence = ["RRULE:FREQ=DAILY;COUNT=9"]
        });

        await new GoogleCalendarSyncService(repository, api).SyncAsync(settings);

        Assert.Equal("RRULE:FREQ=DAILY;COUNT=9", api.EventsByCalendar["work"][local.GoogleEventId].Recurrence!.Single());
    }

    [Fact]
    public async Task SyncAsync_NormalUpdatePreservesUnmanagedGoogleFields()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-unmanaged",
            Title = "local title",
            Start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-unmanaged",
            Summary = "remote title",
            Start = DateTimeEvent(2026, 7, 1, 9),
            End = DateTimeEvent(2026, 7, 1, 10),
            Status = "confirmed",
            Attendees = [new EventAttendee { Email = "guest@example.test" }],
            Visibility = "private",
            Transparency = "transparent",
            Attachments = [new EventAttachment { Title = "agenda" }],
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string> { ["source"] = "google" }
            },
            ConferenceData = new ConferenceData { ConferenceId = "meet-123" }
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var updated = api.EventsByCalendar["work"]["remote-unmanaged"];
        Assert.Equal("local title", updated.Summary);
        Assert.Contains(updated.Attendees!, attendee => attendee.Email == "guest@example.test");
        Assert.Equal("private", updated.Visibility);
        Assert.Equal("transparent", updated.Transparency);
        Assert.Equal("agenda", Assert.Single(updated.Attachments!).Title);
        Assert.Equal("google", updated.ExtendedProperties!.Private__["source"]);
        Assert.Equal("meet-123", updated.ConferenceData!.ConferenceId);
    }

    [Fact]
    public async Task SyncAsync_ConditionalUpdateConflictKeepsLocalEventDirty()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnConditionalUpdate = true };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-conditional-conflict",
            Title = "local update",
            Start = new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-conditional-conflict",
            ETag = "etag-1",
            Summary = "remote",
            Start = DateTimeEvent(2026, 7, 3, 9),
            End = DateTimeEvent(2026, 7, 3, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);

        Assert.Equal(1, result.Failed);
        Assert.True((await repository.FindEventByIdAsync(local.Id))!.IsDirty);
    }

    [Fact]
    public async Task SyncAsync_UpdateNotFoundAfterSuccessfulGetRecreatesUsingLocalPayload()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnUpdateNotFound = true };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-disappeared",
            Title = "local replacement",
            Description = "local description",
            Start = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-disappeared",
            Summary = "stale remote title",
            Description = "stale remote description",
            Start = DateTimeEvent(2026, 7, 4, 9),
            End = DateTimeEvent(2026, 7, 4, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);

        Assert.Equal(1, result.Recreated);
        var recreated = Assert.Single(api.EventsByCalendar["work"].Values, item => item.Id != "remote-disappeared");
        Assert.Equal("local replacement", recreated.Summary);
        Assert.Equal("local description", recreated.Description);
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

    [Theory]
    [InlineData(SyncConflictPolicy.SkipLocalDirty, 0, 0, 1, true)]
    [InlineData(SyncConflictPolicy.PreferLocal, 1, 0, 0, false)]
    [InlineData(SyncConflictPolicy.PreferGoogle, 0, 1, 0, false)]
    public async Task SyncAsync_InitialFullAppliesConflictPolicyToLinkedDirtyEvent(
        SyncConflictPolicy policy,
        int expectedPushed,
        int expectedPulled,
        int expectedSkipped,
        bool remainsDirty)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = policy;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-initial-conflict",
            Title = "local title",
            Start = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "remote title",
            Start = DateTimeEvent(2026, 8, 1, 9),
            End = DateTimeEvent(2026, 8, 1, 10),
            Status = "confirmed"
        });

        var result = await new GoogleCalendarSyncService(repository, api).SyncAsync(settings);

        Assert.Equal(expectedPushed, result.Pushed);
        Assert.Equal(expectedPulled, result.Pulled);
        Assert.Equal(expectedSkipped, result.Skipped);
        Assert.Equal(remainsDirty, (await repository.FindEventByIdAsync(local.Id))!.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncAsync_FullSyncFetchesOldDirtyLinkedEventBeforeApplyingEtagConflictPolicy(bool recoverAfter410)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { HonorInitialFullTimeMin = true };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.SkipLocalDirty;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-old-conflict",
            LastSyncedGoogleEtag = "etag-before-remote-change",
            Title = "local title",
            Start = DateTimeOffset.Now.AddYears(-6),
            End = DateTimeOffset.Now.AddYears(-6).AddHours(1),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            ETag = "etag-after-remote-change",
            Summary = "remote title",
            Start = DateTimeEvent(DateTime.Today.Year - 6, 1, 2, 9),
            End = DateTimeEvent(DateTime.Today.Year - 6, 1, 2, 10),
            Status = "confirmed"
        });
        if (recoverAfter410)
        {
            await repository.SaveSyncTokenAsync("work", "stale-full-sync-token");
            api.StaleSyncTokens.Add("stale-full-sync-token");
        }

        var result = await new GoogleCalendarSyncService(repository, api).SyncAsync(settings);

        Assert.Equal(0, result.Pushed);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(api.Operations, operation => operation == "get:work:remote-old-conflict");
        Assert.True((await repository.FindEventByIdAsync(local.Id))!.IsDirty);
    }

    [Fact]
    public async Task SyncAsync_FullSyncDoesNotAdvanceTokenWhenOldDirtyLinkedEventLookupFails()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { HonorInitialFullTimeMin = true, GetFailuresRemaining = 1 };
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-old-lookup-failure",
            Title = "local title",
            Start = DateTimeOffset.Now.AddYears(-6),
            End = DateTimeOffset.Now.AddYears(-6).AddHours(1),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "remote title",
            Start = DateTimeEvent(DateTime.Today.Year - 6, 1, 2, 9),
            End = DateTimeEvent(DateTime.Today.Year - 6, 1, 2, 10),
            Status = "confirmed"
        });

        var result = await new GoogleCalendarSyncService(repository, api).SyncAsync(settings);

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Pushed);
        Assert.Null(await repository.GetSyncTokenAsync("work"));
        Assert.True((await repository.FindEventByIdAsync(local.Id))!.IsDirty);
    }

    [Theory]
    [InlineData(SyncConflictPolicy.SkipLocalDirty, 0, 0, 1, true, "local title")]
    [InlineData(SyncConflictPolicy.PreferLocal, 1, 0, 0, false, "local title")]
    [InlineData(SyncConflictPolicy.PreferGoogle, 0, 1, 0, false, "remote title")]
    public async Task SyncDirtyEventsAsync_LinkedDirtyEventAppliesConflictPolicy(
        SyncConflictPolicy policy,
        int expectedPushed,
        int expectedPulled,
        int expectedSkipped,
        bool remainsDirty,
        string expectedTitle)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = policy;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-dirty-conflict",
            Title = "local title",
            Start = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "remote title",
            Start = DateTimeEvent(2026, 8, 2, 9),
            End = DateTimeEvent(2026, 8, 2, 10),
            Status = "confirmed"
        });

        var result = await new GoogleCalendarSyncService(repository, api).SyncDirtyEventsAsync(
            settings,
            new HashSet<string>(StringComparer.Ordinal) { local.Id });

        Assert.Equal(expectedPushed, result.Pushed);
        Assert.Equal(expectedPulled, result.Pulled);
        Assert.Equal(expectedSkipped, result.Skipped);
        Assert.Equal(remainsDirty, (await repository.FindEventByIdAsync(local.Id))!.IsDirty);
        Assert.Equal(expectedTitle, (await repository.FindEventByIdAsync(local.Id))!.Title);
    }

    [Fact]
    public async Task SyncDirtyEventsAsync_PushesDirtyLinkedEventWhenRemoteEtagIsUnchanged()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.SkipLocalDirty;
        var local = new CalendarEvent
        {
            CalendarId = "work", GoogleEventId = "remote-unchanged-etag", LastSyncedGoogleEtag = "etag-1",
            Title = "local title", Start = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero), IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event { Id = local.GoogleEventId, ETag = "etag-1", Summary = "remote title", Start = DateTimeEvent(2026, 8, 2, 9), End = DateTimeEvent(2026, 8, 2, 10), Status = "confirmed" });

        var result = await new GoogleCalendarSyncService(repository, api).SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id });

        Assert.Equal(1, result.Pushed);
        var stored = (await repository.FindEventByIdAsync(local.Id))!;
        Assert.False(stored.IsDirty);
        Assert.Equal("fake-etag-1", stored.LastSyncedGoogleEtag);
    }

    public static IEnumerable<object[]> LinkedDirtyEtagCases()
    {
        foreach (var syncPath in new[] { "InitialFull", "RecoveryFull", "DirtyOnly" })
        {
            foreach (var etagState in new[] { "Unchanged", "Changed", "Missing" })
            {
                foreach (var policy in Enum.GetValues<SyncConflictPolicy>())
                {
                    yield return [syncPath, etagState, policy];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(LinkedDirtyEtagCases))]
    public async Task LinkedDirtyEvent_UsesEtagBaselineAcrossAllRemoteSyncPaths(
        string syncPath,
        string etagState,
        SyncConflictPolicy policy)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = policy;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = $"etag-{syncPath}-{etagState}-{policy}",
            LastSyncedGoogleEtag = etagState switch
            {
                "Unchanged" => "etag-baseline",
                "Changed" => "etag-old",
                _ => null
            },
            Title = "local title",
            Start = new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            ETag = etagState == "Unchanged" ? "etag-baseline" : "etag-remote",
            Summary = "remote title",
            Start = DateTimeEvent(2026, 8, 4, 9),
            End = DateTimeEvent(2026, 8, 4, 10),
            Status = "confirmed"
        });

        if (syncPath == "RecoveryFull")
        {
            await repository.SaveSyncTokenAsync("work", "stale-etag-token");
            api.StaleSyncTokens.Add("stale-etag-token");
        }

        var service = new GoogleCalendarSyncService(repository, api);
        var result = syncPath == "DirtyOnly"
            ? await service.SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id })
            : await service.SyncAsync(settings);

        var isTrueConflict = etagState != "Unchanged";
        var shouldPull = isTrueConflict && policy == SyncConflictPolicy.PreferGoogle;
        var shouldSkip = isTrueConflict && policy == SyncConflictPolicy.SkipLocalDirty;
        var shouldPush = !shouldPull && !shouldSkip;
        var stored = (await repository.FindEventByIdAsync(local.Id))!;

        Assert.Equal(shouldPush ? 1 : 0, result.Pushed);
        Assert.Equal(shouldPull ? 1 : 0, result.Pulled);
        Assert.Equal(shouldSkip ? 1 : 0, result.Skipped);
        Assert.Equal(shouldSkip, stored.IsDirty);
        Assert.Equal(shouldPull ? "remote title" : "local title", stored.Title);
        if (shouldPush)
        {
            Assert.StartsWith("fake-etag-", stored.LastSyncedGoogleEtag);
        }
        else if (shouldPull)
        {
            Assert.Equal("etag-remote", stored.LastSyncedGoogleEtag);
        }
    }

    [Theory]
    [InlineData(SyncConflictPolicy.SkipLocalDirty)]
    [InlineData(SyncConflictPolicy.PreferLocal)]
    [InlineData(SyncConflictPolicy.PreferGoogle)]
    public async Task SyncDirtyEventsAsync_LinkedRemoteLookupFailureDoesNotExecuteIncompleteConflictPlan(SyncConflictPolicy policy)
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { GetFailuresRemaining = 1 };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = policy;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-lookup-failure",
            Title = "local title",
            Start = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = local.GoogleEventId,
            Summary = "remote title",
            Start = DateTimeEvent(2026, 8, 3, 9),
            End = DateTimeEvent(2026, 8, 3, 10),
            Status = "confirmed"
        });

        var result = await new GoogleCalendarSyncService(repository, api).SyncDirtyEventsAsync(
            settings,
            new HashSet<string>(StringComparer.Ordinal) { local.Id });

        Assert.Equal(0, result.Pushed);
        Assert.Equal(0, result.Pulled);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
        Assert.DoesNotContain(api.Operations, item => item.StartsWith("update:work:remote-lookup-failure", StringComparison.Ordinal));
        var stored = await repository.FindEventByIdAsync(local.Id);
        Assert.NotNull(stored);
        Assert.True(stored.IsDirty);
        Assert.Equal("remote title", api.EventsByCalendar["work"]["remote-lookup-failure"].Summary);
    }

    [Fact]
    public async Task SyncDirtyEventsAsync_PreservesFailureDiagnosticsForUnselectedDirtyItems()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        api.FailedInsertTitles.Add("retry selected");
        api.FailedInsertTitles.Add("keep failure");
        var settings = CreateSettings("work");
        settings.EnableSyncDiagnostics = true;
        var selected = new CalendarEvent
        {
            CalendarId = "work",
            Title = "retry selected",
            Start = new DateTimeOffset(2026, 1, 7, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 7, 10, 0, 0, TimeSpan.Zero)
        };
        var untouched = new CalendarEvent
        {
            CalendarId = "work",
            Title = "keep failure",
            Start = new DateTimeOffset(2026, 1, 7, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 7, 12, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(selected);
        await repository.SaveEventAsync(untouched);
        var service = new GoogleCalendarSyncService(repository, api);
        await service.SyncAsync(settings);
        Assert.Equal(2, (await service.LoadDiagnosticsAsync(settings)).Failures.Count);

        api.FailedInsertTitles.Remove("retry selected");
        var result = await service.SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { selected.Id });
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Equal(1, result.Pushed);
        Assert.Equal(0, result.Failed);
        var failure = Assert.Single(diagnostics.Failures);
        Assert.Equal(untouched.Id, failure.LocalId);
        Assert.Equal("keep failure", failure.Title);
        var dirty = Assert.Single(diagnostics.DirtyItems);
        Assert.Equal(untouched.Id, dirty.LocalId);
        Assert.Equal("insert failed for keep failure", dirty.ErrorMessage);
    }

    [Fact]
    public async Task SyncDirtyEventsAsync_InsertsBlankGoogleEventIdInsteadOfLinkingExactRemoteMatch()
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
        Assert.Contains(api.Operations, item => item.StartsWith("insert:work:", StringComparison.Ordinal));
        Assert.DoesNotContain("update:work:remote-existing", api.Operations);
        var stored = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1))).Single();
        Assert.False(stored.IsDirty);
        Assert.NotNull(stored.GoogleEventId);
        Assert.NotEqual("remote-existing", stored.GoogleEventId);
    }

    [Fact]
    public async Task SyncDirtyEventsAsync_InsertsLocalCreateEvenWhenStructurallyMatchingRemoteExists()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "new local target",
            Location = "room",
            Start = new DateTimeOffset(2026, 1, 8, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-existing",
            Summary = "new local target",
            Location = "room",
            Start = DateTimeEvent(2026, 1, 8, 9),
            End = DateTimeEvent(2026, 1, 8, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncDirtyEventsAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id });

        Assert.Equal(1, result.Pushed);
        Assert.Contains(api.Operations, item => item.StartsWith("insert:work:", StringComparison.Ordinal));
        Assert.DoesNotContain("update:work:remote-existing", api.Operations);
        var stored = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1)))
            .Single(item => item.Id == local.Id);
        Assert.False(stored.IsDirty);
        Assert.NotNull(stored.GoogleEventId);
        Assert.NotEqual("remote-existing", stored.GoogleEventId);
    }

    [Fact]
    public async Task SyncAsync_InsertsBlankGoogleEventIdLocalEventEvenWhenMatchingRemoteExists()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "normal sync local create",
            Location = "same room",
            Start = new DateTimeOffset(2026, 1, 8, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 10, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-same-shape",
            Summary = "normal sync local create",
            Location = "same room",
            Start = DateTimeEvent(2026, 1, 8, 9),
            End = DateTimeEvent(2026, 1, 8, 10),
            Status = "confirmed"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        var result = await service.SyncAsync(settings);

        Assert.Equal(1, result.Pushed);
        Assert.Contains(api.Operations, item => item.StartsWith("insert:work:", StringComparison.Ordinal));
        Assert.DoesNotContain("update:work:remote-same-shape", api.Operations);
        var stored = (await repository.LoadEventsAsync(local.Start.AddDays(-1), local.End.AddDays(1)))
            .Single(item => item.Id == local.Id);
        Assert.False(stored.IsDirty);
        Assert.NotNull(stored.GoogleEventId);
        Assert.NotEqual("remote-same-shape", stored.GoogleEventId);
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
    public async Task SyncAsync_PreservesGoogleEmailOnlyReminderWithoutLocalAppReminder()
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
        Assert.Empty(stored.AppReminderMinutesBeforeStart);
        Assert.Equal([30], stored.GoogleEmailReminderMinutesBeforeStart);
        Assert.False(stored.IsAppReminderEnabled);
        Assert.True(stored.IsGoogleEmailReminderEnabled);
        Assert.True(stored.GoogleReminderMetadata!.HasEmailOnly);
        Assert.Equal([30], stored.GoogleReminderMetadata.EmailMinutes);
        Assert.Null(stored.GoogleReminderMetadata.AdoptedReminderMethod);
    }

    [Fact]
    public async Task SyncAsync_PreservesGoogleEmailOnlyReminderAsDiagnosticsWhenAdoptionDisabled()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.AdoptGoogleEmailRemindersAsLocalNotifications = false;
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
        Assert.Empty(stored.AppReminderMinutesBeforeStart);
        Assert.Equal([30], stored.GoogleEmailReminderMinutesBeforeStart);
        Assert.False(stored.IsAppReminderEnabled);
        Assert.True(stored.IsGoogleEmailReminderEnabled);
        Assert.True(stored.GoogleReminderMetadata!.HasEmailOnly);
        Assert.Equal([30], stored.GoogleReminderMetadata.EmailMinutes);
        Assert.Null(stored.GoogleReminderMetadata.AdoptedReminderMethod);
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
    public async Task SyncAsync_PushesLocalReminderWithSameEmailReminder()
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
            ReminderMinutesBeforeStart = 10,
            IsAppReminderEnabled = true,
            IsGoogleEmailReminderEnabled = true
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var remote = Assert.Single(api.EventsByCalendar["work"].Values);
        Assert.False(remote.Reminders.UseDefault);
        Assert.Equal(
            [("email", 10), ("popup", 10)],
            remote.Reminders.Overrides
                .Select(item => (item.Method, item.Minutes.GetValueOrDefault()))
                .OrderBy(item => item.Method)
                .ThenBy(item => item.Item2)
                .ToArray());
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
    public async Task SyncAsync_PushesEmailOnlyReminderWhenAppReminderDisabled()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "local email only reminder",
            Start = new DateTimeOffset(2026, 1, 8, 17, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 18, 0, 0, TimeSpan.Zero),
            ReminderMinutesBeforeStart = 30,
            IsAppReminderEnabled = false,
            IsGoogleEmailReminderEnabled = true
        };
        await repository.SaveEventAsync(local);
        var service = new GoogleCalendarSyncService(repository, api);

        await service.SyncAsync(settings);

        var remote = Assert.Single(api.EventsByCalendar["work"].Values);
        Assert.False(remote.Reminders.UseDefault);
        var reminder = Assert.Single(remote.Reminders.Overrides);
        Assert.Equal("email", reminder.Method);
        Assert.Equal(30, reminder.Minutes);
    }

    [Fact]
    public async Task SyncAsync_PushesNoReminderAsExplicitEmptyOverridesEvenWhenEmailMetadataExists()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            Title = "local cleared email reminder",
            Start = new DateTimeOffset(2026, 1, 8, 17, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 8, 18, 0, 0, TimeSpan.Zero),
            GoogleReminderMetadata = new GoogleReminderMetadata
            {
                EmailMinutes = [30]
            }
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
    public async Task SyncDirtyEventsAsync_UpdatesDescriptionOnLinkedRemoteEvent()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-description-match",
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
    public async Task SyncDirtyEventsAsync_KeepsDescriptionDirtyWhenLinkedRemoteUpdateFails()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi { ThrowOnUpdate = true };
        var settings = CreateSettings("work");
        settings.SyncConflictPolicy = SyncConflictPolicy.PreferLocal;
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-description-failure",
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
        Assert.Equal("remote-description-failure", dirty.GoogleEventId);
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
    public async Task DiscardLocalChangesAsync_MarksCancelledNormalGoogleEventDeletedAndClean()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-cancelled-discard",
            Title = "local edit",
            Start = new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-cancelled-discard",
            Summary = "remote cancel",
            Start = DateTimeEvent(2026, 7, 2, 9),
            End = DateTimeEvent(2026, 7, 2, 10),
            Status = "cancelled"
        });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.DiscardLocalChangesAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id });

        var stored = await repository.FindEventByIdAsync(local.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);
        Assert.False(stored.IsDirty);
    }

    [Fact]
    public async Task DiscardLocalChangesAsync_SparseCancelledNormalEventPreservesLocalFields()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        var start = new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero);
        var local = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-sparse-cancelled",
            Title = "keep local title",
            Description = "keep local description",
            Location = "keep local location",
            Start = start,
            End = start.AddHours(1),
            IsDirty = true
        };
        await repository.SaveEventAsync(local);
        api.UpsertRemote("work", new Event { Id = "remote-sparse-cancelled", Status = "cancelled" });
        var service = new GoogleCalendarSyncService(repository, api);

        await service.DiscardLocalChangesAsync(settings, new HashSet<string>(StringComparer.Ordinal) { local.Id });

        var stored = await repository.FindEventByIdAsync(local.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted);
        Assert.False(stored.IsDirty);
        Assert.Equal("keep local title", stored.Title);
        Assert.Equal("keep local description", stored.Description);
        Assert.Equal("keep local location", stored.Location);
        Assert.Equal(start, stored.Start);
    }

    [Fact]
    public async Task DiscardLocalChangesAsync_PreservesFailureDiagnosticsForUnselectedDirtyItems()
    {
        var repository = await CreateRepositoryAsync();
        var api = new FakeGoogleCalendarApi();
        var settings = CreateSettings("work");
        settings.EnableSyncDiagnostics = true;
        var selected = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-selected",
            Title = "selected discard",
            Start = new DateTimeOffset(2026, 1, 9, 11, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 9, 12, 0, 0, TimeSpan.Zero)
        };
        var untouched = new CalendarEvent
        {
            CalendarId = "work",
            GoogleEventId = "remote-untouched",
            Title = "untouched discard",
            Start = new DateTimeOffset(2026, 1, 9, 13, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 1, 9, 14, 0, 0, TimeSpan.Zero)
        };
        await repository.SaveEventAsync(selected);
        await repository.SaveEventAsync(untouched);
        var service = new GoogleCalendarSyncService(repository, api);
        await service.DiscardLocalChangesAsync(settings, new HashSet<string>(StringComparer.Ordinal) { selected.Id, untouched.Id });
        Assert.Equal(2, (await service.LoadDiagnosticsAsync(settings)).Failures.Count);
        api.UpsertRemote("work", new Event
        {
            Id = "remote-selected",
            Summary = "remote selected",
            Start = DateTimeEvent(2026, 1, 9, 11),
            End = DateTimeEvent(2026, 1, 9, 12),
            Status = "confirmed"
        });

        var result = await service.DiscardLocalChangesAsync(settings, new HashSet<string>(StringComparer.Ordinal) { selected.Id });
        var diagnostics = await service.LoadDiagnosticsAsync(settings);

        Assert.Equal(1, result.Pulled);
        Assert.Equal(0, result.Failed);
        var failure = Assert.Single(diagnostics.Failures);
        Assert.Equal(untouched.Id, failure.LocalId);
        Assert.Equal("untouched discard", failure.Title);
        var dirty = Assert.Single(diagnostics.DirtyItems);
        Assert.Equal(untouched.Id, dirty.LocalId);
        Assert.NotNull(dirty.ErrorMessage);
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
    public async Task MainViewModel_SyncUsesSettingsSnapshotWhenCalendarSelectionChangesDuringRemoteCall()
    {
        var repository = await CreateRepositoryAsync();
        var listStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueList = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeGoogleCalendarApi
        {
            ListStarted = listStarted,
            ContinueList = continueList
        };
        api.UpsertRemote("primary", new Event
        {
            Id = "remote-primary",
            Summary = "primary calendar event",
            Start = DateTimeEvent(2026, 1, 9, 9),
            End = DateTimeEvent(2026, 1, 9, 10),
            Status = "confirmed"
        });
        api.UpsertRemote("other", new Event
        {
            Id = "remote-other",
            Summary = "other calendar event",
            Start = DateTimeEvent(2026, 1, 9, 9),
            End = DateTimeEvent(2026, 1, 9, 10),
            Status = "confirmed"
        });
        await repository.SaveSettingsAsync(CreateSettings("primary"));
        var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository, api));
        await viewModel.InitializeAsync();
        api.ListRequests.Clear();

        var sync = viewModel.SynchronizeManuallyAsync();
        await listStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var calendar in viewModel.AvailableCalendars)
        {
            calendar.IsSelected = calendar.Id == "other";
        }
        await viewModel.ApplyCalendarSelectionAsync();
        continueList.SetResult();
        await sync.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(api.ListRequests, request => request.CalendarId == "primary");
        Assert.DoesNotContain(api.ListRequests, request => request.CalendarId == "other");
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

    private sealed class FakeGoogleCalendarApi : IGoogleCalendarApi, IConditionalGoogleCalendarClient
    {
        private int _nextId = 1;
        private int _nextEtag = 1;

        public Dictionary<string, Dictionary<string, Event>> EventsByCalendar { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ChangedRemoteKeys { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<GoogleReminderOverride>> DefaultRemindersByCalendar { get; } = new(StringComparer.Ordinal);
        public List<string> Operations { get; } = [];
        public List<GoogleEventListRequest> ListRequests { get; } = [];
        public HashSet<string> FailedInsertTitles { get; } = new(StringComparer.Ordinal);
        public bool ThrowOnInsert { get; set; }
        public bool ThrowOnUpdate { get; set; }
        public bool ThrowOnUpdateNotFound { get; set; }
        public bool ThrowOnConditionalUpdate { get; set; }
        public bool ThrowOnGet { get; set; }
        public int GetFailuresRemaining { get; set; }
        public bool ThrowOnList { get; set; }
        public bool HonorInitialFullTimeMin { get; set; }
        public int PageSize { get; set; } = int.MaxValue;
        public HashSet<string> StaleSyncTokens { get; } = new(StringComparer.Ordinal);
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

            if (!string.IsNullOrWhiteSpace(googleEvent.Summary) && FailedInsertTitles.Contains(googleEvent.Summary))
            {
                throw new InvalidOperationException($"insert failed for {googleEvent.Summary}");
            }

            var copy = Clone(googleEvent);
            copy.Id = $"fake-{_nextId++}";
            copy.ETag = $"fake-etag-{_nextEtag++}";
            copy.Status ??= "confirmed";
            Calendar(calendarId)[copy.Id] = copy;
            Operations.Add($"insert:{calendarId}:{copy.Id}");
            return Task.FromResult(Clone(copy));
        }

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken = default)
        {
            return UpdateEventAsync(calendarId, eventId, googleEvent, cancellationToken, null);
        }

        public Task<Event> UpdateEventAsync(string calendarId, string eventId, Event googleEvent, CancellationToken cancellationToken, string? ifMatchETag)
        {
            if (ThrowOnUpdateNotFound)
            {
                Calendar(calendarId).Remove(eventId);
                throw new KeyNotFoundException(eventId);
            }

            if (ThrowOnConditionalUpdate && !string.IsNullOrWhiteSpace(ifMatchETag))
            {
                throw new InvalidOperationException("conditional update conflict");
            }

            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("update failed");
            }

            var copy = Clone(googleEvent);
            copy.Id = eventId;
            copy.ETag = $"fake-etag-{_nextEtag++}";
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
            Operations.Add($"get:{calendarId}:{eventId}");
            if (ThrowOnGet || GetFailuresRemaining-- > 0)
            {
                throw new InvalidOperationException("get failed");
            }

            if (!Calendar(calendarId).TryGetValue(eventId, out var googleEvent))
            {
                throw new KeyNotFoundException(eventId);
            }

            return Task.FromResult(Clone(googleEvent));
        }

        public async Task<GoogleEventPage> ListEventsAsync(GoogleEventListRequest request, CancellationToken cancellationToken = default)
        {
            ListRequests.Add(request);
            Operations.Add($"list:{request.CalendarId}");
            if (ThrowOnList)
            {
                throw new InvalidOperationException("list failed");
            }

            if (!string.IsNullOrWhiteSpace(request.SyncToken) && StaleSyncTokens.Remove(request.SyncToken))
            {
                throw new GoogleApiException("FakeGoogleCalendar", "sync token expired")
                {
                    HttpStatusCode = System.Net.HttpStatusCode.Gone
                };
            }

            if (ListStarted is not null && ContinueList is not null)
            {
                var continueList = ContinueList;
                ListStarted.SetResult();
                ListStarted = null;
                ContinueList = null;
                await continueList.Task.WaitAsync(cancellationToken);
            }

            var events = string.IsNullOrWhiteSpace(request.SyncToken)
                ? Calendar(request.CalendarId).Values.Select(Clone).ToArray()
                : Calendar(request.CalendarId)
                    .Where(item => ChangedRemoteKeys.Contains($"{request.CalendarId}:{item.Key}"))
                    .Select(item => Clone(item.Value))
                    .ToArray();
            if (HonorInitialFullTimeMin && string.IsNullOrWhiteSpace(request.SyncToken) && request.TimeMin is { } timeMin)
            {
                events = events.Where(item => EventStart(item) >= timeMin).ToArray();
            }
            foreach (var googleEvent in events)
            {
                ChangedRemoteKeys.Remove($"{request.CalendarId}:{googleEvent.Id}");
            }
            var offset = int.TryParse(request.PageToken, out var parsedOffset) ? parsedOffset : 0;
            var pageEvents = events.Skip(offset).Take(PageSize).ToArray();
            var nextOffset = offset + pageEvents.Length;
            var nextPageToken = nextOffset < events.Length ? nextOffset.ToString() : null;
            return new GoogleEventPage(pageEvents, nextPageToken, nextPageToken is null ? $"token-{request.CalendarId}-{events.Length}" : null);
        }

        private static DateTimeOffset EventStart(Event googleEvent)
        {
            return googleEvent.Start?.DateTimeDateTimeOffset
                ?? DateTimeOffset.Parse(googleEvent.Start?.Date ?? throw new InvalidOperationException("Remote event has no start."));
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
            ChangedRemoteKeys.Add($"{calendarId}:{googleEvent.Id}");
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
                ETag = source.ETag,
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
                Visibility = source.Visibility,
                Transparency = source.Transparency,
                ConferenceData = source.ConferenceData,
                ExtendedProperties = source.ExtendedProperties,
                Attachments = source.Attachments?.ToArray(),
                Attendees = source.Attendees?
                    .Select(item => new EventAttendee { Email = item.Email, ResponseStatus = item.ResponseStatus })
                    .ToArray(),
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

using System.Data;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Repositories;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.App.Services;

public sealed class CalendarRepository : IEventRepository, ISettingsRepository, ITagRepository, ISyncStateRepository
{
    private readonly string _databasePath;
    private readonly object _maintenanceLock = new();
    private bool _databaseMaintenanceRequested;
    private int _activeConnectionCount;
    private TaskCompletionSource<bool>? _connectionsDrained;

    public CalendarRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.DatabasePath;
    }

    public string DatabasePath => _databasePath;

    internal async Task BeginMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        Task? waitTask = null;
        lock (_maintenanceLock)
        {
            if (_databaseMaintenanceRequested)
            {
                throw new InvalidOperationException("Database maintenance is in progress.");
            }

            _databaseMaintenanceRequested = true;
            if (_activeConnectionCount > 0)
            {
                _connectionsDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _connectionsDrained.Task;
            }
        }

        if (waitTask is null)
        {
            return;
        }

        try
        {
            await waitTask.WaitAsync(cancellationToken);
        }
        catch
        {
            EndMaintenance();
            throw;
        }
    }

    internal void EndMaintenance()
    {
        lock (_maintenanceLock)
        {
            _databaseMaintenanceRequested = false;
            _connectionsDrained = null;
        }
    }

    public async Task InitializeAsync()
    {
        AppPaths.Ensure();
        await using var connection = OpenConnection();
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;");
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                google_event_id TEXT,
                last_synced_google_etag TEXT,
                recurring_event_id TEXT,
                recurring_parent_id TEXT,
                original_start TEXT,
                original_start_utc_ticks INTEGER,
                is_recurrence_exception INTEGER NOT NULL DEFAULT 0,
                calendar_id TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT,
                location TEXT,
                start TEXT NOT NULL,
                end TEXT NOT NULL,
                start_utc_ticks INTEGER,
                end_utc_ticks INTEGER,
                is_all_day INTEGER NOT NULL,
                color_id TEXT,
                reminder_minutes_before_start INTEGER,
                app_reminder_enabled INTEGER,
                google_email_reminder_enabled INTEGER,
                recurrence_json TEXT,
                is_deleted INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                last_synced_at TEXT,
                updated_at_utc_ticks INTEGER,
                last_synced_at_utc_ticks INTEGER,
                is_dirty INTEGER NOT NULL,
                is_todo_like INTEGER NOT NULL,
                dirty_fields TEXT,
                google_reminder_metadata_json TEXT,
                app_reminder_minutes_json TEXT,
                google_email_reminder_minutes_json TEXT
            );
            """);
        await using (var transaction = connection.BeginTransaction())
        {
            try
            {
                await EnsureEventColumnsAsync(connection, transaction);
                await BackfillUtcTicksAsync(connection, transaction);
                await CreateEventIndexesAsync(connection, transaction);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
        await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS tags (name TEXT PRIMARY KEY, color TEXT NOT NULL, is_visible INTEGER NOT NULL, priority INTEGER NOT NULL);");
        await SeedTagsAsync(connection);
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'app'";
        var value = await command.ExecuteScalarAsync() as string;
        return string.IsNullOrWhiteSpace(value)
            ? new AppSettings()
            : JsonSerializer.Deserialize<AppSettings>(value) ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO settings(key, value) VALUES('app', $value)";
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(settings));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string?> LoadSettingValueAsync(string key)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync() as string;
    }

    public async Task SaveSettingValueAsync(string key, string? value)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(value))
        {
            command.CommandText = "DELETE FROM settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            await command.ExecuteNonQueryAsync();
            return;
        }

        command.CommandText = "INSERT OR REPLACE INTO settings(key, value) VALUES($key, $value)";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CalendarTag>> LoadTagsAsync()
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, color, is_visible, priority FROM tags ORDER BY priority DESC, name";
        var tags = new List<CalendarTag>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tags.Add(new CalendarTag
            {
                Name = reader.GetString(0),
                Color = reader.GetString(1),
                IsVisible = reader.GetInt32(2) != 0,
                Priority = reader.GetInt32(3)
            });
        }

        return tags;
    }

    public async Task SaveTagAsync(CalendarTag tag)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO tags(name, color, is_visible, priority)
            VALUES($name, $color, $visible, $priority)
            """;
        command.Parameters.AddWithValue("$name", tag.Name);
        command.Parameters.AddWithValue("$color", tag.Color);
        command.Parameters.AddWithValue("$visible", tag.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$priority", tag.Priority);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CalendarEvent>> LoadEventsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        var deletedPredicate = includeDeleted ? "" : " AND is_deleted = 0";
        command.CommandText = $"""
            WITH candidate_ids(id) AS (
                SELECT id
                FROM events
                WHERE start_utc_ticks < $end_utc_ticks
                  AND end_utc_ticks > $start_utc_ticks{deletedPredicate}
                UNION
                SELECT id
                FROM events
                WHERE recurrence_json IS NOT NULL
                  AND is_recurrence_exception = 0
                  AND start_utc_ticks < $end_utc_ticks{deletedPredicate}
                UNION
                SELECT id
                FROM events
                WHERE is_recurrence_exception = 1
                  AND original_start_utc_ticks IS NOT NULL
                  AND original_start_utc_ticks >= $start_utc_ticks
                  AND original_start_utc_ticks < $end_utc_ticks{deletedPredicate}
            )
            SELECT events.id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json, last_synced_google_etag
            FROM events
            JOIN candidate_ids ON candidate_ids.id = events.id
            ORDER BY events.start_utc_ticks, events.title
            """;
        command.Parameters.AddWithValue("$start_utc_ticks", start.UtcTicks);
        command.Parameters.AddWithValue("$end_utc_ticks", end.UtcTicks);
        return await ReadEventsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarEvent>> LoadTodoEventsAsync()
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json, last_synced_google_etag
            FROM events
            WHERE is_todo_like = 1 AND is_deleted = 0
            ORDER BY start_utc_ticks, title
            """;
        return await ReadEventsAsync(command);
    }

    public async Task<IReadOnlyList<CalendarEvent>> LoadDirtyEventsAsync()
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json, last_synced_google_etag
            FROM events
            WHERE is_dirty = 1
            ORDER BY updated_at_utc_ticks
            """;
        return await ReadEventsAsync(command);
    }

    public async Task<CalendarEvent?> FindEventByGoogleEventIdAsync(string calendarId, string? googleEventId)
    {
        if (string.IsNullOrWhiteSpace(googleEventId))
        {
            return null;
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json, last_synced_google_etag
            FROM events
            WHERE calendar_id = $calendar_id AND google_event_id = $google_event_id
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$calendar_id", calendarId);
        command.Parameters.AddWithValue("$google_event_id", googleEventId);
        return (await ReadEventsAsync(command)).FirstOrDefault();
    }

    public async Task<bool> UpdateGoogleReminderMetadataAsync(
        string calendarId,
        string? googleEventId,
        int? reminderMinutesBeforeStart,
        IReadOnlyList<int> appReminderMinutes,
        IReadOnlyList<int> googleEmailReminderMinutes,
        GoogleReminderMetadata? metadata)
    {
        if (string.IsNullOrWhiteSpace(googleEventId))
        {
            return false;
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE events
            SET reminder_minutes_before_start = CASE WHEN is_dirty = 0 THEN $reminder_minutes_before_start ELSE reminder_minutes_before_start END,
                app_reminder_enabled = CASE WHEN is_dirty = 0 THEN $app_reminder_enabled ELSE app_reminder_enabled END,
                google_email_reminder_enabled = CASE WHEN is_dirty = 0 THEN $google_email_reminder_enabled ELSE google_email_reminder_enabled END,
                google_reminder_metadata_json = $google_reminder_metadata_json,
                app_reminder_minutes_json = CASE WHEN is_dirty = 0 THEN $app_reminder_minutes_json ELSE app_reminder_minutes_json END,
                google_email_reminder_minutes_json = CASE WHEN is_dirty = 0 THEN $google_email_reminder_minutes_json ELSE google_email_reminder_minutes_json END
            WHERE calendar_id = $calendar_id
              AND google_event_id = $google_event_id
              AND is_deleted = 0
            """;
        command.Parameters.AddWithValue("$calendar_id", calendarId);
        command.Parameters.AddWithValue("$google_event_id", googleEventId);
        command.Parameters.AddWithValue("$reminder_minutes_before_start", (object?)reminderMinutesBeforeStart ?? DBNull.Value);
        command.Parameters.AddWithValue("$app_reminder_enabled", appReminderMinutes.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$google_email_reminder_enabled", googleEmailReminderMinutes.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$google_reminder_metadata_json", metadata is null
            ? DBNull.Value
            : JsonSerializer.Serialize(metadata));
        command.Parameters.AddWithValue("$app_reminder_minutes_json", SerializeReminderMinutes(appReminderMinutes));
        command.Parameters.AddWithValue("$google_email_reminder_minutes_json", SerializeReminderMinutes(googleEmailReminderMinutes));
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task ApplyTodoReminderCleanupStateAsync(
        string localId,
        bool preserveDirtyState,
        string? cleanedGoogleEtag = null)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = preserveDirtyState
            ? """
              UPDATE events
              SET reminder_minutes_before_start = NULL,
                  app_reminder_enabled = 0,
                  google_email_reminder_enabled = 0,
                  app_reminder_minutes_json = '[]',
                  google_email_reminder_minutes_json = '[]',
                  google_reminder_metadata_json = CASE
                      WHEN google_reminder_metadata_json IS NULL OR json_valid(google_reminder_metadata_json) = 0 THEN NULL
                      WHEN json_extract(google_reminder_metadata_json, '$.StartTimeZoneId') IS NULL
                       AND json_extract(google_reminder_metadata_json, '$.EndTimeZoneId') IS NULL THEN NULL
                      ELSE json_object(
                          'StartTimeZoneId', json_extract(google_reminder_metadata_json, '$.StartTimeZoneId'),
                          'EndTimeZoneId', json_extract(google_reminder_metadata_json, '$.EndTimeZoneId'))
                  END
              WHERE id = $id
              """
            : """
              UPDATE events
              SET reminder_minutes_before_start = NULL,
                  app_reminder_enabled = 0,
                  google_email_reminder_enabled = 0,
                  app_reminder_minutes_json = '[]',
                  google_email_reminder_minutes_json = '[]',
                  google_reminder_metadata_json = CASE
                      WHEN google_reminder_metadata_json IS NULL OR json_valid(google_reminder_metadata_json) = 0 THEN NULL
                      WHEN json_extract(google_reminder_metadata_json, '$.StartTimeZoneId') IS NULL
                       AND json_extract(google_reminder_metadata_json, '$.EndTimeZoneId') IS NULL THEN NULL
                      ELSE json_object(
                          'StartTimeZoneId', json_extract(google_reminder_metadata_json, '$.StartTimeZoneId'),
                          'EndTimeZoneId', json_extract(google_reminder_metadata_json, '$.EndTimeZoneId'))
                  END,
                  last_synced_google_etag = COALESCE($etag, last_synced_google_etag)
              WHERE id = $id
              """;
        command.Parameters.AddWithValue("$id", localId);
        if (!preserveDirtyState)
        {
            command.Parameters.AddWithValue("$etag", (object?)cleanedGoogleEtag ?? DBNull.Value);
        }
        await command.ExecuteNonQueryAsync();
    }

    public async Task<CalendarEvent?> FindDuplicateEventAsync(CalendarEvent calendarEvent)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json, last_synced_google_etag
            FROM events
            WHERE calendar_id = $calendar_id
              AND title = $title
              AND start_utc_ticks = $start_utc_ticks
              AND end_utc_ticks = $end_utc_ticks
              AND COALESCE(location, '') = COALESCE($location, '')
              AND is_deleted = 0
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$calendar_id", calendarEvent.CalendarId);
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$start_utc_ticks", calendarEvent.Start.UtcTicks);
        command.Parameters.AddWithValue("$end_utc_ticks", calendarEvent.End.UtcTicks);
        command.Parameters.AddWithValue("$location", (object?)calendarEvent.Location ?? DBNull.Value);
        return (await ReadEventsAsync(command)).FirstOrDefault();
    }

    public async Task<CalendarEvent?> FindMasterByIdAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json, last_synced_google_etag
            FROM events
            WHERE id = $id
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", id);
        return (await ReadEventsAsync(command)).FirstOrDefault();
    }

    public Task<CalendarEvent?> FindEventByIdAsync(string? id) => FindMasterByIdAsync(id);

    public async Task<IReadOnlyList<CalendarEvent>> LoadSeriesEventsAsync(string? recurringParentId, string? recurringEventId)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json, last_synced_google_etag
            FROM events
            WHERE ($recurring_parent_id IS NOT NULL AND recurring_parent_id = $recurring_parent_id)
               OR ($recurring_event_id IS NOT NULL AND recurring_event_id = $recurring_event_id)
            ORDER BY original_start_utc_ticks, start_utc_ticks
            """;
        command.Parameters.AddWithValue("$recurring_parent_id", (object?)recurringParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurring_event_id", (object?)recurringEventId ?? DBNull.Value);
        return await ReadEventsAsync(command);
    }

    public async Task SaveEventAsync(CalendarEvent calendarEvent)
    {
        var existing = await FindMasterByIdAsync(calendarEvent.Id);
        await PreserveExistingRemoteLinkAsync(calendarEvent);
        calendarEvent.DirtyFields = EventDirtyFieldTracker.Merge(existing?.DirtyFields ?? calendarEvent.DirtyFields, existing, calendarEvent);
        calendarEvent.UpdatedAt = DateTimeOffset.Now;
        calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);
        await UpsertEventAsync(calendarEvent);
    }

    public async Task UpsertSyncedEventAsync(CalendarEvent calendarEvent)
    {
        var existing = await FindEventByGoogleEventIdAsync(calendarEvent.CalendarId, calendarEvent.GoogleEventId);
        if (existing is not null)
        {
            calendarEvent.Id = existing.Id;
        }

        calendarEvent.IsDirty = false;
        calendarEvent.DirtyFields = null;
        calendarEvent.LastSyncedAt = DateTimeOffset.Now;
        calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);
        await UpsertEventAsync(calendarEvent);
    }

    public async Task MarkSyncedAsync(CalendarEvent calendarEvent, string? googleEventId = null, string? lastSyncedGoogleEtag = null)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE events
            SET google_event_id = COALESCE($google_event_id, google_event_id),
                is_dirty = 0,
                dirty_fields = NULL,
                app_reminder_enabled = $app_reminder_enabled,
                google_email_reminder_enabled = $google_email_reminder_enabled,
                google_reminder_metadata_json = $google_reminder_metadata_json,
                app_reminder_minutes_json = $app_reminder_minutes_json,
                google_email_reminder_minutes_json = $google_email_reminder_minutes_json,
                last_synced_at = $last_synced_at,
                last_synced_at_utc_ticks = $last_synced_at_utc_ticks,
                last_synced_google_etag = COALESCE($last_synced_google_etag, last_synced_google_etag)
            WHERE id = $id
            """;
        var appReminderMinutes = CalendarEvent.NormalizeReminderMinutes(calendarEvent.EffectiveAppReminderMinutesBeforeStart);
        var googleEmailReminderMinutes = GetStoredGoogleEmailReminderMinutes(calendarEvent);

        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.Parameters.AddWithValue("$google_event_id", (object?)googleEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_synced_google_etag", (object?)lastSyncedGoogleEtag ?? DBNull.Value);
        command.Parameters.AddWithValue("$app_reminder_enabled", appReminderMinutes.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$google_email_reminder_enabled", googleEmailReminderMinutes.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$google_reminder_metadata_json", calendarEvent.GoogleReminderMetadata is null
            ? DBNull.Value
            : JsonSerializer.Serialize(calendarEvent.GoogleReminderMetadata));
        command.Parameters.AddWithValue("$app_reminder_minutes_json", SerializeReminderMinutes(appReminderMinutes));
        command.Parameters.AddWithValue("$google_email_reminder_minutes_json", SerializeReminderMinutes(googleEmailReminderMinutes));
        var syncedAt = DateTimeOffset.Now;
        command.Parameters.AddWithValue("$last_synced_at", syncedAt.ToString("O"));
        command.Parameters.AddWithValue("$last_synced_at_utc_ticks", syncedAt.UtcTicks);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> MarkSyncedByIdsAsync(IEnumerable<string> ids)
    {
        const int chunkSize = 500;
        var idList = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (idList.Length == 0)
        {
            return 0;
        }

        var updated = 0;
        await using var connection = OpenConnection();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var syncedAt = DateTimeOffset.Now;
            var now = syncedAt.ToString("O");
            for (var offset = 0; offset < idList.Length; offset += chunkSize)
            {
                var chunk = idList.Skip(offset).Take(chunkSize).ToArray();
                var placeholders = string.Join(", ", Enumerable.Range(0, chunk.Length).Select(index => $"$id{index}"));
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    UPDATE events
                    SET is_dirty = 0,
                        dirty_fields = NULL,
                        last_synced_at = $last_synced_at,
                        last_synced_at_utc_ticks = $last_synced_at_utc_ticks
                    WHERE id IN ({placeholders})
                    """;
                command.Parameters.AddWithValue("$last_synced_at", now);
                command.Parameters.AddWithValue("$last_synced_at_utc_ticks", syncedAt.UtcTicks);
                for (var index = 0; index < chunk.Length; index++)
                {
                    command.Parameters.AddWithValue($"$id{index}", chunk[index]);
                }

                updated += await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return updated;
    }

    public async Task<bool> HardDeleteEventAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM events WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task DeleteEventAsync(CalendarEvent calendarEvent)
    {
        calendarEvent.IsDeleted = true;
        calendarEvent.IsDirty = true;
        await SaveEventAsync(calendarEvent);
    }

    private async Task PreserveExistingRemoteLinkAsync(CalendarEvent calendarEvent)
    {
        if (!string.IsNullOrWhiteSpace(calendarEvent.GoogleEventId))
        {
            return;
        }

        var existing = await FindMasterByIdAsync(calendarEvent.Id);
        if (existing is null
            || string.IsNullOrWhiteSpace(existing.GoogleEventId)
            || !string.Equals(existing.CalendarId, calendarEvent.CalendarId, StringComparison.Ordinal))
        {
            return;
        }

        calendarEvent.GoogleEventId = existing.GoogleEventId;
        calendarEvent.LastSyncedAt = existing.LastSyncedAt;
        calendarEvent.LastSyncedGoogleEtag = existing.LastSyncedGoogleEtag;
    }

    public async Task<string?> GetSyncTokenAsync(string calendarId)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", $"sync:{calendarId}");
        return await command.ExecuteScalarAsync() as string;
    }

    public async Task SaveSyncTokenAsync(string calendarId, string? syncToken)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(syncToken))
        {
            command.CommandText = "DELETE FROM settings WHERE key = $key";
        }
        else
        {
            command.CommandText = "INSERT OR REPLACE INTO settings(key, value) VALUES($key, $value)";
            command.Parameters.AddWithValue("$value", syncToken);
        }

        command.Parameters.AddWithValue("$key", $"sync:{calendarId}");
        await command.ExecuteNonQueryAsync();
    }

    private async Task UpsertEventAsync(CalendarEvent calendarEvent)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO events(
                id, google_event_id, last_synced_google_etag, recurring_event_id, recurring_parent_id, original_start, original_start_utc_ticks, is_recurrence_exception,
                calendar_id, title, description, location, start, end, is_all_day,
                start_utc_ticks, end_utc_ticks,
                color_id, reminder_minutes_before_start, app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted, updated_at, last_synced_at, updated_at_utc_ticks, last_synced_at_utc_ticks, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json, app_reminder_minutes_json, google_email_reminder_minutes_json)
            VALUES(
                $id, $google_event_id, $last_synced_google_etag, $recurring_event_id, $recurring_parent_id, $original_start, $original_start_utc_ticks, $is_recurrence_exception,
                $calendar_id, $title, $description, $location, $start, $end, $is_all_day,
                $start_utc_ticks, $end_utc_ticks,
                $color_id, $reminder_minutes_before_start, $app_reminder_enabled, $google_email_reminder_enabled, $recurrence_json, $is_deleted, $updated_at, $last_synced_at, $updated_at_utc_ticks, $last_synced_at_utc_ticks, $is_dirty, $is_todo_like, $dirty_fields, $google_reminder_metadata_json, $app_reminder_minutes_json, $google_email_reminder_minutes_json)
            """;
        AddEventParameters(command, calendarEvent);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> FindLocalIdByGoogleEventIdAsync(string calendarId, string? googleEventId)
    {
        if (string.IsNullOrWhiteSpace(googleEventId))
        {
            return null;
        }

        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM events WHERE calendar_id = $calendar_id AND google_event_id = $google_event_id LIMIT 1";
        command.Parameters.AddWithValue("$calendar_id", calendarId);
        command.Parameters.AddWithValue("$google_event_id", googleEventId);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task SeedTagsAsync(SqliteConnection connection)
    {
        foreach (var tag in TagService.DefaultTags)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO tags(name, color, is_visible, priority) VALUES($name, $color, $visible, $priority)";
            command.Parameters.AddWithValue("$name", tag.Name);
            command.Parameters.AddWithValue("$color", tag.Color);
            command.Parameters.AddWithValue("$visible", tag.IsVisible ? 1 : 0);
            command.Parameters.AddWithValue("$priority", tag.Priority);
            await command.ExecuteNonQueryAsync();
        }
    }

    private SqliteConnection OpenConnection()
    {
        lock (_maintenanceLock)
        {
            if (_databaseMaintenanceRequested)
            {
                throw new InvalidOperationException("Database maintenance is in progress.");
            }

            _activeConnectionCount++;
        }

        var released = 0;
        void ReleaseConnection()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            lock (_maintenanceLock)
            {
                _activeConnectionCount--;
                if (_activeConnectionCount == 0)
                {
                    _connectionsDrained?.TrySetResult(true);
                }
            }
        }

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            DefaultTimeout = 10
        }.ToString());
        connection.StateChange += (_, args) =>
        {
            if (args.CurrentState is ConnectionState.Closed or ConnectionState.Broken)
            {
                ReleaseConnection();
            }
        };

        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            ReleaseConnection();
            connection.Dispose();
            throw;
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<CalendarEvent>> ReadEventsAsync(SqliteCommand command, CancellationToken cancellationToken = default)
    {
        var events = new List<CalendarEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(new CalendarEvent
            {
                Id = reader.GetString(0),
                GoogleEventId = reader.IsDBNull(1) ? null : reader.GetString(1),
                RecurringEventId = reader.IsDBNull(2) ? null : reader.GetString(2),
                RecurringParentId = reader.IsDBNull(3) ? null : reader.GetString(3),
                OriginalStart = reader.IsDBNull(4) ? null : ParseDateTimeOffset(reader.GetString(4)),
                IsRecurrenceException = reader.GetInt32(5) != 0,
                CalendarId = reader.GetString(6),
                Title = reader.GetString(7),
                Description = reader.IsDBNull(8) ? null : reader.GetString(8),
                Location = reader.IsDBNull(9) ? null : reader.GetString(9),
                Start = ParseDateTimeOffset(reader.GetString(10)),
                End = ParseDateTimeOffset(reader.GetString(11)),
                IsAllDay = reader.GetInt32(12) != 0,
                ColorId = reader.IsDBNull(13) ? null : reader.GetString(13),
                ReminderMinutesBeforeStart = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                AppReminderEnabled = reader.IsDBNull(15) ? null : reader.GetInt32(15) != 0,
                GoogleEmailReminderEnabled = reader.IsDBNull(16) ? null : reader.GetInt32(16) != 0,
                RecurrenceJson = reader.IsDBNull(17) ? null : reader.GetString(17),
                IsDeleted = reader.GetInt32(18) != 0,
                UpdatedAt = ParseDateTimeOffset(reader.GetString(19)),
                LastSyncedAt = reader.IsDBNull(20) ? null : ParseDateTimeOffset(reader.GetString(20)),
                IsDirty = reader.GetInt32(21) != 0,
                IsTodoLike = reader.GetInt32(22) != 0,
                DirtyFields = reader.IsDBNull(23) ? null : reader.GetString(23),
                GoogleReminderMetadata = reader.IsDBNull(24)
                    ? null
                    : JsonSerializer.Deserialize<GoogleReminderMetadata>(reader.GetString(24)),
                AppReminderMinutesBeforeStart = reader.FieldCount <= 25 || reader.IsDBNull(25)
                    ? []
                    : DeserializeReminderMinutes(reader.GetString(25)),
                GoogleEmailReminderMinutesBeforeStart = reader.FieldCount <= 26 || reader.IsDBNull(26)
                    ? []
                    : DeserializeReminderMinutes(reader.GetString(26)),
                LastSyncedGoogleEtag = reader.FieldCount <= 27 || reader.IsDBNull(27) ? null : reader.GetString(27)
            });
        }

        return events;
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static void AddEventParameters(SqliteCommand command, CalendarEvent calendarEvent)
    {
        var appReminderMinutes = CalendarEvent.NormalizeReminderMinutes(calendarEvent.EffectiveAppReminderMinutesBeforeStart);
        var googleEmailReminderMinutes = GetStoredGoogleEmailReminderMinutes(calendarEvent);
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.Parameters.AddWithValue("$google_event_id", (object?)calendarEvent.GoogleEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_synced_google_etag", (object?)calendarEvent.LastSyncedGoogleEtag ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurring_event_id", (object?)calendarEvent.RecurringEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurring_parent_id", (object?)calendarEvent.RecurringParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$original_start", calendarEvent.OriginalStart?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$original_start_utc_ticks", calendarEvent.OriginalStart?.UtcTicks ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$is_recurrence_exception", calendarEvent.IsRecurrenceException ? 1 : 0);
        command.Parameters.AddWithValue("$calendar_id", calendarEvent.CalendarId);
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$description", (object?)calendarEvent.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", (object?)calendarEvent.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("$start", calendarEvent.Start.ToString("O"));
        command.Parameters.AddWithValue("$end", calendarEvent.End.ToString("O"));
        command.Parameters.AddWithValue("$start_utc_ticks", calendarEvent.Start.UtcTicks);
        command.Parameters.AddWithValue("$end_utc_ticks", calendarEvent.End.UtcTicks);
        command.Parameters.AddWithValue("$is_all_day", calendarEvent.IsAllDay ? 1 : 0);
        command.Parameters.AddWithValue("$color_id", (object?)calendarEvent.ColorId ?? DBNull.Value);
        command.Parameters.AddWithValue("$reminder_minutes_before_start", appReminderMinutes.Count == 0 ? DBNull.Value : appReminderMinutes[0]);
        command.Parameters.AddWithValue("$app_reminder_enabled", appReminderMinutes.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$google_email_reminder_enabled", googleEmailReminderMinutes.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$recurrence_json", (object?)calendarEvent.RecurrenceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$is_deleted", calendarEvent.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", calendarEvent.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$last_synced_at", calendarEvent.LastSyncedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updated_at_utc_ticks", calendarEvent.UpdatedAt.UtcTicks);
        command.Parameters.AddWithValue("$last_synced_at_utc_ticks", calendarEvent.LastSyncedAt?.UtcTicks ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$is_dirty", calendarEvent.IsDirty ? 1 : 0);
        command.Parameters.AddWithValue("$is_todo_like", calendarEvent.IsTodoLike ? 1 : 0);
        command.Parameters.AddWithValue("$dirty_fields", (object?)calendarEvent.DirtyFields ?? DBNull.Value);
        command.Parameters.AddWithValue("$google_reminder_metadata_json", calendarEvent.GoogleReminderMetadata is null
            ? DBNull.Value
            : JsonSerializer.Serialize(calendarEvent.GoogleReminderMetadata));
        command.Parameters.AddWithValue("$app_reminder_minutes_json", SerializeReminderMinutes(appReminderMinutes));
        command.Parameters.AddWithValue("$google_email_reminder_minutes_json", SerializeReminderMinutes(googleEmailReminderMinutes));
    }

    private static async Task EnsureEventColumnsAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        await EnsureColumnAsync(connection, transaction, "events", "last_synced_google_etag", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "recurring_event_id", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "recurring_parent_id", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "original_start", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "original_start_utc_ticks", "INTEGER");
        await EnsureColumnAsync(connection, transaction, "events", "is_recurrence_exception", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, transaction, "events", "reminder_minutes_before_start", "INTEGER");
        await EnsureColumnAsync(connection, transaction, "events", "app_reminder_enabled", "INTEGER");
        await EnsureColumnAsync(connection, transaction, "events", "google_email_reminder_enabled", "INTEGER");
        await EnsureColumnAsync(connection, transaction, "events", "dirty_fields", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "google_reminder_metadata_json", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "app_reminder_minutes_json", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "google_email_reminder_minutes_json", "TEXT");
        await EnsureColumnAsync(connection, transaction, "events", "start_utc_ticks", "INTEGER");
        await EnsureColumnAsync(connection, transaction, "events", "end_utc_ticks", "INTEGER");
        await EnsureColumnAsync(connection, transaction, "events", "updated_at_utc_ticks", "INTEGER");
        await EnsureColumnAsync(connection, transaction, "events", "last_synced_at_utc_ticks", "INTEGER");
    }

    private static async Task BackfillUtcTicksAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var rows = new List<(string Id, string Start, string End, string? OriginalStart, string UpdatedAt, string? LastSyncedAt)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT id, start, end, original_start, updated_at, last_synced_at
                FROM events
                WHERE start_utc_ticks IS NULL
                   OR end_utc_ticks IS NULL
                   OR updated_at_utc_ticks IS NULL
                   OR (original_start IS NOT NULL AND original_start_utc_ticks IS NULL)
                   OR (last_synced_at IS NOT NULL AND last_synced_at_utc_ticks IS NULL)
                """;
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        foreach (var row in rows)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE events
                SET start_utc_ticks = $start_utc_ticks,
                    end_utc_ticks = $end_utc_ticks,
                    original_start_utc_ticks = $original_start_utc_ticks,
                    updated_at_utc_ticks = $updated_at_utc_ticks,
                    last_synced_at_utc_ticks = $last_synced_at_utc_ticks
                WHERE id = $id
                """;
            update.Parameters.AddWithValue("$id", row.Id);
            update.Parameters.AddWithValue("$start_utc_ticks", ParseDateTimeOffset(row.Start).UtcTicks);
            update.Parameters.AddWithValue("$end_utc_ticks", ParseDateTimeOffset(row.End).UtcTicks);
            update.Parameters.AddWithValue("$original_start_utc_ticks", row.OriginalStart is null ? DBNull.Value : ParseDateTimeOffset(row.OriginalStart).UtcTicks);
            update.Parameters.AddWithValue("$updated_at_utc_ticks", ParseDateTimeOffset(row.UpdatedAt).UtcTicks);
            update.Parameters.AddWithValue("$last_synced_at_utc_ticks", row.LastSyncedAt is null ? DBNull.Value : ParseDateTimeOffset(row.LastSyncedAt).UtcTicks);
            await update.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreateEventIndexesAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_dates ON events(start, end);");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_utc_dates ON events(start_utc_ticks, end_utc_ticks);");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_google ON events(calendar_id, google_event_id);");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_recurring_parent ON events(recurring_parent_id, original_start);");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_utc_recurring_parent ON events(recurring_parent_id, original_start_utc_ticks);");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_recurring_event ON events(recurring_event_id, original_start);");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_utc_recurring_event ON events(recurring_event_id, original_start_utc_ticks);");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_recurring_master_start ON events(start) WHERE recurrence_json IS NOT NULL AND is_recurrence_exception = 0;");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_utc_recurring_master_start ON events(start_utc_ticks) WHERE recurrence_json IS NOT NULL AND is_recurrence_exception = 0;");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_exception_original_start ON events(original_start) WHERE is_recurrence_exception = 1;");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_utc_exception_original_start ON events(original_start_utc_ticks) WHERE is_recurrence_exception = 1;");
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_events_utc_dirty_updated ON events(updated_at_utc_ticks) WHERE is_dirty = 1;");
    }

    private static bool HasGooglePopupReminder(GoogleReminderMetadata? metadata)
    {
        return metadata is not null
            && (metadata.PopupMinutes.Count > 0 || metadata.DefaultPopupMinutes.Count > 0);
    }

    private static bool HasGoogleEmailReminder(GoogleReminderMetadata? metadata)
    {
        return metadata is not null
            && (metadata.EmailMinutes.Count > 0 || metadata.DefaultEmailMinutes.Count > 0);
    }

    private static object SerializeReminderMinutes(IEnumerable<int> minutes)
    {
        var normalized = CalendarEvent.NormalizeReminderMinutes(minutes);
        return normalized.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(normalized);
    }

    private static IReadOnlyList<int> GetStoredGoogleEmailReminderMinutes(CalendarEvent calendarEvent)
    {
        var configured = CalendarEvent.NormalizeReminderMinutes(calendarEvent.GoogleEmailReminderMinutesBeforeStart);
        if (configured.Count > 0)
        {
            return configured;
        }

        return calendarEvent.GoogleEmailReminderEnabled == true && calendarEvent.ReminderMinutesBeforeStart is int minutes
            ? [minutes]
            : [];
    }

    private static List<int> DeserializeReminderMinutes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return CalendarEvent.NormalizeReminderMinutes(JsonSerializer.Deserialize<List<int>>(json)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, string columnName, string sqlDefinition)
    {
        await using var pragma = connection.CreateCommand();
        pragma.Transaction = transaction;
        pragma.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await ExecuteAsync(connection, transaction, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqlDefinition};");
    }
}
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Repositories;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.App.Services;

public sealed class CalendarRepository : IEventRepository, ISettingsRepository, ITagRepository, ISyncStateRepository
{
    private readonly string _databasePath;

    public CalendarRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.DatabasePath;
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync()
    {
        AppPaths.Ensure();
        await using var connection = OpenConnection();
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                google_event_id TEXT,
                recurring_event_id TEXT,
                recurring_parent_id TEXT,
                original_start TEXT,
                is_recurrence_exception INTEGER NOT NULL DEFAULT 0,
                calendar_id TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT,
                location TEXT,
                start TEXT NOT NULL,
                end TEXT NOT NULL,
                is_all_day INTEGER NOT NULL,
                color_id TEXT,
                reminder_minutes_before_start INTEGER,
                recurrence_json TEXT,
                is_deleted INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                last_synced_at TEXT,
                is_dirty INTEGER NOT NULL,
                is_todo_like INTEGER NOT NULL,
                dirty_fields TEXT,
                google_reminder_metadata_json TEXT
            );
            """);
        await EnsureEventColumnsAsync(connection);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_events_dates ON events(start, end);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_events_google ON events(calendar_id, google_event_id);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_events_recurring_parent ON events(recurring_parent_id, original_start);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_events_recurring_event ON events(recurring_event_id, original_start);");
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
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json
            FROM events
            WHERE ((start < $end AND end > $start)
                OR recurrence_json IS NOT NULL
                OR is_recurrence_exception = 1)
              AND ($includeDeleted = 1 OR is_deleted = 0)
            ORDER BY start, title
            """;
        command.Parameters.AddWithValue("$start", start.ToString("O"));
        command.Parameters.AddWithValue("$end", end.ToString("O"));
        command.Parameters.AddWithValue("$includeDeleted", includeDeleted ? 1 : 0);
        return await ReadEventsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarEvent>> LoadTodoEventsAsync()
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json
            FROM events
            WHERE is_todo_like = 1 AND is_deleted = 0
            ORDER BY start, title
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
                   color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json
            FROM events
            WHERE is_dirty = 1
            ORDER BY updated_at
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
                   color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json
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
                google_reminder_metadata_json = $google_reminder_metadata_json
            WHERE calendar_id = $calendar_id
              AND google_event_id = $google_event_id
              AND is_deleted = 0
            """;
        command.Parameters.AddWithValue("$calendar_id", calendarId);
        command.Parameters.AddWithValue("$google_event_id", googleEventId);
        command.Parameters.AddWithValue("$reminder_minutes_before_start", (object?)reminderMinutesBeforeStart ?? DBNull.Value);
        command.Parameters.AddWithValue("$google_reminder_metadata_json", metadata is null
            ? DBNull.Value
            : JsonSerializer.Serialize(metadata));
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<CalendarEvent?> FindDuplicateEventAsync(CalendarEvent calendarEvent)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                   calendar_id, title, description, location, start, end, is_all_day,
                   color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json
            FROM events
            WHERE calendar_id = $calendar_id
              AND title = $title
              AND start = $start
              AND end = $end
              AND COALESCE(location, '') = COALESCE($location, '')
              AND is_deleted = 0
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$calendar_id", calendarEvent.CalendarId);
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$start", calendarEvent.Start.ToString("O"));
        command.Parameters.AddWithValue("$end", calendarEvent.End.ToString("O"));
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
                   color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json
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
                   color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json
            FROM events
            WHERE ($recurring_parent_id IS NOT NULL AND recurring_parent_id = $recurring_parent_id)
               OR ($recurring_event_id IS NOT NULL AND recurring_event_id = $recurring_event_id)
            ORDER BY original_start, start
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

    public async Task MarkSyncedAsync(CalendarEvent calendarEvent, string? googleEventId = null)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE events
            SET google_event_id = COALESCE($google_event_id, google_event_id),
                is_dirty = 0,
                dirty_fields = NULL,
                google_reminder_metadata_json = $google_reminder_metadata_json,
                last_synced_at = $last_synced_at
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.Parameters.AddWithValue("$google_event_id", (object?)googleEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$google_reminder_metadata_json", calendarEvent.GoogleReminderMetadata is null
            ? DBNull.Value
            : JsonSerializer.Serialize(calendarEvent.GoogleReminderMetadata));
        command.Parameters.AddWithValue("$last_synced_at", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> MarkSyncedByIdsAsync(IEnumerable<string> ids)
    {
        var idList = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var updated = 0;
        foreach (var id in idList)
        {
            var calendarEvent = await FindMasterByIdAsync(id);
            if (calendarEvent is null)
            {
                continue;
            }

            await MarkSyncedAsync(calendarEvent);
            updated++;
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
                id, google_event_id, recurring_event_id, recurring_parent_id, original_start, is_recurrence_exception,
                calendar_id, title, description, location, start, end, is_all_day,
                color_id, reminder_minutes_before_start, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json)
            VALUES(
                $id, $google_event_id, $recurring_event_id, $recurring_parent_id, $original_start, $is_recurrence_exception,
                $calendar_id, $title, $description, $location, $start, $end, $is_all_day,
                $color_id, $reminder_minutes_before_start, $recurrence_json, $is_deleted, $updated_at, $last_synced_at, $is_dirty, $is_todo_like, $dirty_fields, $google_reminder_metadata_json)
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
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        }.ToString());
        connection.Open();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
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
                OriginalStart = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
                IsRecurrenceException = reader.GetInt32(5) != 0,
                CalendarId = reader.GetString(6),
                Title = reader.GetString(7),
                Description = reader.IsDBNull(8) ? null : reader.GetString(8),
                Location = reader.IsDBNull(9) ? null : reader.GetString(9),
                Start = DateTimeOffset.Parse(reader.GetString(10)),
                End = DateTimeOffset.Parse(reader.GetString(11)),
                IsAllDay = reader.GetInt32(12) != 0,
                ColorId = reader.IsDBNull(13) ? null : reader.GetString(13),
                ReminderMinutesBeforeStart = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                RecurrenceJson = reader.IsDBNull(15) ? null : reader.GetString(15),
                IsDeleted = reader.GetInt32(16) != 0,
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(17)),
                LastSyncedAt = reader.IsDBNull(18) ? null : DateTimeOffset.Parse(reader.GetString(18)),
                IsDirty = reader.GetInt32(19) != 0,
                IsTodoLike = reader.GetInt32(20) != 0,
                DirtyFields = reader.IsDBNull(21) ? null : reader.GetString(21),
                GoogleReminderMetadata = reader.IsDBNull(22)
                    ? null
                    : JsonSerializer.Deserialize<GoogleReminderMetadata>(reader.GetString(22))
            });
        }

        return events;
    }

    private static void AddEventParameters(SqliteCommand command, CalendarEvent calendarEvent)
    {
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.Parameters.AddWithValue("$google_event_id", (object?)calendarEvent.GoogleEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurring_event_id", (object?)calendarEvent.RecurringEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurring_parent_id", (object?)calendarEvent.RecurringParentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$original_start", calendarEvent.OriginalStart?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$is_recurrence_exception", calendarEvent.IsRecurrenceException ? 1 : 0);
        command.Parameters.AddWithValue("$calendar_id", calendarEvent.CalendarId);
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$description", (object?)calendarEvent.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", (object?)calendarEvent.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("$start", calendarEvent.Start.ToString("O"));
        command.Parameters.AddWithValue("$end", calendarEvent.End.ToString("O"));
        command.Parameters.AddWithValue("$is_all_day", calendarEvent.IsAllDay ? 1 : 0);
        command.Parameters.AddWithValue("$color_id", (object?)calendarEvent.ColorId ?? DBNull.Value);
        command.Parameters.AddWithValue("$reminder_minutes_before_start", calendarEvent.ReminderMinutesBeforeStart ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$recurrence_json", (object?)calendarEvent.RecurrenceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$is_deleted", calendarEvent.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", calendarEvent.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$last_synced_at", calendarEvent.LastSyncedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$is_dirty", calendarEvent.IsDirty ? 1 : 0);
        command.Parameters.AddWithValue("$is_todo_like", calendarEvent.IsTodoLike ? 1 : 0);
        command.Parameters.AddWithValue("$dirty_fields", (object?)calendarEvent.DirtyFields ?? DBNull.Value);
        command.Parameters.AddWithValue("$google_reminder_metadata_json", calendarEvent.GoogleReminderMetadata is null
            ? DBNull.Value
            : JsonSerializer.Serialize(calendarEvent.GoogleReminderMetadata));
    }

    private static async Task EnsureEventColumnsAsync(SqliteConnection connection)
    {
        await EnsureColumnAsync(connection, "events", "recurring_event_id", "TEXT");
        await EnsureColumnAsync(connection, "events", "recurring_parent_id", "TEXT");
        await EnsureColumnAsync(connection, "events", "original_start", "TEXT");
        await EnsureColumnAsync(connection, "events", "is_recurrence_exception", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "events", "reminder_minutes_before_start", "INTEGER");
        await EnsureColumnAsync(connection, "events", "dirty_fields", "TEXT");
        await EnsureColumnAsync(connection, "events", "google_reminder_metadata_json", "TEXT");
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string tableName, string columnName, string sqlDefinition)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await ExecuteAsync(connection, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqlDefinition};");
    }
}

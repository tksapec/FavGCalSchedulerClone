using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.App.Services;

public sealed class CalendarRepository
{
    private readonly string _databasePath;

    public CalendarRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? AppPaths.DatabasePath;
    }

    public async Task InitializeAsync()
    {
        AppPaths.Ensure();
        await using var connection = OpenConnection();
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                google_event_id TEXT,
                calendar_id TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT,
                location TEXT,
                start TEXT NOT NULL,
                end TEXT NOT NULL,
                is_all_day INTEGER NOT NULL,
                color_id TEXT,
                recurrence_json TEXT,
                is_deleted INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                last_synced_at TEXT,
                is_dirty INTEGER NOT NULL,
                is_todo_like INTEGER NOT NULL
            );
            """);
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_events_dates ON events(start, end);");
        await ExecuteAsync(connection, "CREATE INDEX IF NOT EXISTS ix_events_google ON events(calendar_id, google_event_id);");
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

    public async Task<IReadOnlyList<CalendarEvent>> LoadEventsAsync(DateTimeOffset start, DateTimeOffset end, bool includeDeleted = false)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, calendar_id, title, description, location, start, end, is_all_day,
                   color_id, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like
            FROM events
            WHERE start < $end AND end > $start
              AND ($includeDeleted = 1 OR is_deleted = 0)
            ORDER BY start, title
            """;
        command.Parameters.AddWithValue("$start", start.ToString("O"));
        command.Parameters.AddWithValue("$end", end.ToString("O"));
        command.Parameters.AddWithValue("$includeDeleted", includeDeleted ? 1 : 0);
        return await ReadEventsAsync(command);
    }

    public async Task<IReadOnlyList<CalendarEvent>> LoadDirtyEventsAsync()
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, google_event_id, calendar_id, title, description, location, start, end, is_all_day,
                   color_id, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like
            FROM events
            WHERE is_dirty = 1
            ORDER BY updated_at
            """;
        return await ReadEventsAsync(command);
    }

    public async Task SaveEventAsync(CalendarEvent calendarEvent)
    {
        calendarEvent.UpdatedAt = DateTimeOffset.Now;
        calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);
        await UpsertEventAsync(calendarEvent);
    }

    public async Task UpsertSyncedEventAsync(CalendarEvent calendarEvent)
    {
        var existingId = await FindLocalIdByGoogleEventIdAsync(calendarEvent.CalendarId, calendarEvent.GoogleEventId);
        if (!string.IsNullOrWhiteSpace(existingId))
        {
            calendarEvent.Id = existingId;
        }

        calendarEvent.IsDirty = false;
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
                last_synced_at = $last_synced_at
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.Parameters.AddWithValue("$google_event_id", (object?)googleEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_synced_at", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteEventAsync(CalendarEvent calendarEvent)
    {
        calendarEvent.IsDeleted = true;
        calendarEvent.IsDirty = true;
        await SaveEventAsync(calendarEvent);
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
                id, google_event_id, calendar_id, title, description, location, start, end, is_all_day,
                color_id, recurrence_json, is_deleted, updated_at, last_synced_at, is_dirty, is_todo_like)
            VALUES(
                $id, $google_event_id, $calendar_id, $title, $description, $location, $start, $end, $is_all_day,
                $color_id, $recurrence_json, $is_deleted, $updated_at, $last_synced_at, $is_dirty, $is_todo_like)
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
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM tags";
        var count = (long)(await countCommand.ExecuteScalarAsync() ?? 0L);
        if (count > 0)
        {
            return;
        }

        foreach (var tag in TagService.DefaultTags)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO tags(name, color, is_visible, priority) VALUES($name, $color, $visible, $priority)";
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
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<CalendarEvent>> ReadEventsAsync(SqliteCommand command)
    {
        var events = new List<CalendarEvent>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new CalendarEvent
            {
                Id = reader.GetString(0),
                GoogleEventId = reader.IsDBNull(1) ? null : reader.GetString(1),
                CalendarId = reader.GetString(2),
                Title = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                Location = reader.IsDBNull(5) ? null : reader.GetString(5),
                Start = DateTimeOffset.Parse(reader.GetString(6)),
                End = DateTimeOffset.Parse(reader.GetString(7)),
                IsAllDay = reader.GetInt32(8) != 0,
                ColorId = reader.IsDBNull(9) ? null : reader.GetString(9),
                RecurrenceJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                IsDeleted = reader.GetInt32(11) != 0,
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(12)),
                LastSyncedAt = reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)),
                IsDirty = reader.GetInt32(14) != 0,
                IsTodoLike = reader.GetInt32(15) != 0
            });
        }

        return events;
    }

    private static void AddEventParameters(SqliteCommand command, CalendarEvent calendarEvent)
    {
        command.Parameters.AddWithValue("$id", calendarEvent.Id);
        command.Parameters.AddWithValue("$google_event_id", (object?)calendarEvent.GoogleEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$calendar_id", calendarEvent.CalendarId);
        command.Parameters.AddWithValue("$title", calendarEvent.Title);
        command.Parameters.AddWithValue("$description", (object?)calendarEvent.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", (object?)calendarEvent.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("$start", calendarEvent.Start.ToString("O"));
        command.Parameters.AddWithValue("$end", calendarEvent.End.ToString("O"));
        command.Parameters.AddWithValue("$is_all_day", calendarEvent.IsAllDay ? 1 : 0);
        command.Parameters.AddWithValue("$color_id", (object?)calendarEvent.ColorId ?? DBNull.Value);
        command.Parameters.AddWithValue("$recurrence_json", (object?)calendarEvent.RecurrenceJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$is_deleted", calendarEvent.IsDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$updated_at", calendarEvent.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$last_synced_at", calendarEvent.LastSyncedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$is_dirty", calendarEvent.IsDirty ? 1 : 0);
        command.Parameters.AddWithValue("$is_todo_like", calendarEvent.IsTodoLike ? 1 : 0);
    }
}

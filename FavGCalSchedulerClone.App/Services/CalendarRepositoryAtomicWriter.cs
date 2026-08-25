using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.App.Services;

internal static class CalendarRepositoryAtomicWriter
{
    public static async Task SaveEventsAsync(
        CalendarRepository repository,
        IEnumerable<CalendarEvent> events,
        IEnumerable<string>? hardDeleteIds = null,
        CancellationToken cancellationToken = default)
    {
        await repository.InitializeAsync();
        var items = events.ToArray();
        var deleteIds = hardDeleteIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (items.Length == 0 && deleteIds.Length == 0)
        {
            return;
        }

        await using var connection = repository.OpenConnection();
        await using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var calendarEvent in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = await LoadExistingAsync(connection, transaction, calendarEvent.Id, cancellationToken);
                PreserveExistingRemoteLink(calendarEvent, existing);
                calendarEvent.DirtyFields = EventDirtyFieldTracker.Merge(
                    existing?.DirtyFields ?? calendarEvent.DirtyFields,
                    existing,
                    calendarEvent);
                calendarEvent.UpdatedAt = DateTimeOffset.Now;
                calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);
                await UpsertAsync(connection, transaction, calendarEvent, cancellationToken);
            }

            foreach (var id in deleteIds)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM events WHERE id = $id";
                command.Parameters.AddWithValue("$id", id);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackSafelyAsync(transaction);
            throw;
        }
    }

    private static void PreserveExistingRemoteLink(CalendarEvent current, CalendarEvent? existing)
    {
        if (!string.IsNullOrWhiteSpace(current.GoogleEventId)
            || existing is null
            || string.IsNullOrWhiteSpace(existing.GoogleEventId)
            || !string.Equals(existing.CalendarId, current.CalendarId, StringComparison.Ordinal))
        {
            return;
        }

        current.GoogleEventId = existing.GoogleEventId;
        current.LastSyncedAt = existing.LastSyncedAt;
        current.LastSyncedGoogleEtag = existing.LastSyncedGoogleEtag;
    }

    private static async Task<CalendarEvent?> LoadExistingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT google_event_id, last_synced_google_etag, calendar_id, title, description, location,
                   start, end, is_all_day, color_id, reminder_minutes_before_start,
                   app_reminder_enabled, google_email_reminder_enabled, recurrence_json,
                   is_deleted, last_synced_at, is_dirty, dirty_fields, google_reminder_metadata_json,
                   app_reminder_minutes_json, google_email_reminder_minutes_json
            FROM events WHERE id = $id LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CalendarEvent
        {
            Id = id,
            GoogleEventId = reader.IsDBNull(0) ? null : reader.GetString(0),
            LastSyncedGoogleEtag = reader.IsDBNull(1) ? null : reader.GetString(1),
            CalendarId = reader.GetString(2),
            Title = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            Location = reader.IsDBNull(5) ? null : reader.GetString(5),
            Start = ParseDateTimeOffset(reader.GetString(6)),
            End = ParseDateTimeOffset(reader.GetString(7)),
            IsAllDay = reader.GetInt32(8) != 0,
            ColorId = reader.IsDBNull(9) ? null : reader.GetString(9),
            ReminderMinutesBeforeStart = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            AppReminderEnabled = reader.IsDBNull(11) ? null : reader.GetInt32(11) != 0,
            GoogleEmailReminderEnabled = reader.IsDBNull(12) ? null : reader.GetInt32(12) != 0,
            RecurrenceJson = reader.IsDBNull(13) ? null : reader.GetString(13),
            IsDeleted = reader.GetInt32(14) != 0,
            LastSyncedAt = reader.IsDBNull(15) ? null : ParseDateTimeOffset(reader.GetString(15)),
            IsDirty = reader.GetInt32(16) != 0,
            DirtyFields = reader.IsDBNull(17) ? null : reader.GetString(17),
            GoogleReminderMetadata = reader.IsDBNull(18) ? null : DeserializeGoogleReminderMetadata(reader.GetString(18)),
            AppReminderMinutesBeforeStart = reader.IsDBNull(19) ? [] : DeserializeReminderMinutes(reader.GetString(19)),
            GoogleEmailReminderMinutesBeforeStart = reader.IsDBNull(20) ? [] : DeserializeReminderMinutes(reader.GetString(20))
        };
    }

    private static async Task UpsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO events(
                id, google_event_id, last_synced_google_etag, recurring_event_id, recurring_parent_id,
                original_start, original_start_utc_ticks, is_recurrence_exception,
                calendar_id, title, description, location, start, end, is_all_day,
                start_utc_ticks, end_utc_ticks, color_id, reminder_minutes_before_start,
                app_reminder_enabled, google_email_reminder_enabled, recurrence_json, is_deleted,
                updated_at, last_synced_at, updated_at_utc_ticks, last_synced_at_utc_ticks,
                is_dirty, is_todo_like, dirty_fields, google_reminder_metadata_json,
                app_reminder_minutes_json, google_email_reminder_minutes_json)
            VALUES(
                $id, $google_event_id, $last_synced_google_etag, $recurring_event_id, $recurring_parent_id,
                $original_start, $original_start_utc_ticks, $is_recurrence_exception,
                $calendar_id, $title, $description, $location, $start, $end, $is_all_day,
                $start_utc_ticks, $end_utc_ticks, $color_id, $reminder_minutes_before_start,
                $app_reminder_enabled, $google_email_reminder_enabled, $recurrence_json, $is_deleted,
                $updated_at, $last_synced_at, $updated_at_utc_ticks, $last_synced_at_utc_ticks,
                $is_dirty, $is_todo_like, $dirty_fields, $google_reminder_metadata_json,
                $app_reminder_minutes_json, $google_email_reminder_minutes_json)
            """;
        AddEventParameters(command, calendarEvent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddEventParameters(SqliteCommand command, CalendarEvent calendarEvent)
    {
        var appReminderMinutes = CalendarEvent.NormalizeReminderMinutes(calendarEvent.EffectiveAppReminderMinutesBeforeStart);
        var googleEmailReminderMinutes = CalendarEvent.NormalizeReminderMinutes(calendarEvent.GoogleEmailReminderMinutesBeforeStart);
        if (googleEmailReminderMinutes.Count == 0
            && calendarEvent.GoogleEmailReminderEnabled == true
            && calendarEvent.ReminderMinutesBeforeStart is int fallbackEmailMinutes)
        {
            googleEmailReminderMinutes = CalendarEvent.NormalizeReminderMinutes([fallbackEmailMinutes]);
        }

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

    private static async Task RollbackSafelyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            System.Diagnostics.Debug.WriteLine(rollbackException);
        }
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static GoogleReminderMetadata? DeserializeGoogleReminderMetadata(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GoogleReminderMetadata>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<int> DeserializeReminderMinutes(string json)
    {
        try
        {
            return CalendarEvent.NormalizeReminderMinutes(JsonSerializer.Deserialize<List<int>>(json)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static object SerializeReminderMinutes(IEnumerable<int> minutes)
    {
        var normalized = CalendarEvent.NormalizeReminderMinutes(minutes);
        return normalized.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(normalized);
    }
}

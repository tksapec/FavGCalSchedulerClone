from pathlib import Path


def replace_between(path: Path, start_marker: str, end_marker: str, replacement: str) -> None:
    text = path.read_text(encoding="utf-8")
    start = text.index(start_marker)
    end = text.index(end_marker, start)
    path.write_text(text[:start] + replacement + text[end:], encoding="utf-8")


repo = Path("FavGCalSchedulerClone.App/Services/CalendarRepository.cs")
mark_synced = '''    public async Task MarkSyncedAsync(CalendarEvent calendarEvent, string? googleEventId = null, string? lastSyncedGoogleEtag = null)
    {
        await EnterEventMutationAsync();
        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE events
                SET google_event_id = CASE
                        WHEN calendar_id = $expected_calendar_id THEN COALESCE($google_event_id, google_event_id)
                        ELSE google_event_id
                    END,
                    is_dirty = CASE
                        WHEN calendar_id = $expected_calendar_id AND updated_at_utc_ticks = $expected_updated_at_utc_ticks THEN 0
                        ELSE is_dirty
                    END,
                    dirty_fields = CASE
                        WHEN calendar_id = $expected_calendar_id AND updated_at_utc_ticks = $expected_updated_at_utc_ticks THEN NULL
                        ELSE dirty_fields
                    END,
                    app_reminder_enabled = CASE
                        WHEN calendar_id = $expected_calendar_id AND updated_at_utc_ticks = $expected_updated_at_utc_ticks THEN $app_reminder_enabled
                        ELSE app_reminder_enabled
                    END,
                    google_email_reminder_enabled = CASE
                        WHEN calendar_id = $expected_calendar_id AND updated_at_utc_ticks = $expected_updated_at_utc_ticks THEN $google_email_reminder_enabled
                        ELSE google_email_reminder_enabled
                    END,
                    google_reminder_metadata_json = CASE
                        WHEN calendar_id = $expected_calendar_id AND updated_at_utc_ticks = $expected_updated_at_utc_ticks THEN $google_reminder_metadata_json
                        ELSE google_reminder_metadata_json
                    END,
                    app_reminder_minutes_json = CASE
                        WHEN calendar_id = $expected_calendar_id AND updated_at_utc_ticks = $expected_updated_at_utc_ticks THEN $app_reminder_minutes_json
                        ELSE app_reminder_minutes_json
                    END,
                    google_email_reminder_minutes_json = CASE
                        WHEN calendar_id = $expected_calendar_id AND updated_at_utc_ticks = $expected_updated_at_utc_ticks THEN $google_email_reminder_minutes_json
                        ELSE google_email_reminder_minutes_json
                    END,
                    last_synced_at = CASE
                        WHEN calendar_id = $expected_calendar_id THEN $last_synced_at
                        ELSE last_synced_at
                    END,
                    last_synced_at_utc_ticks = CASE
                        WHEN calendar_id = $expected_calendar_id THEN $last_synced_at_utc_ticks
                        ELSE last_synced_at_utc_ticks
                    END,
                    last_synced_google_etag = CASE
                        WHEN calendar_id = $expected_calendar_id THEN COALESCE($last_synced_google_etag, last_synced_google_etag)
                        ELSE last_synced_google_etag
                    END
                WHERE id = $id
                """;
            var appReminderMinutes = CalendarEvent.NormalizeReminderMinutes(calendarEvent.EffectiveAppReminderMinutesBeforeStart);
            var googleEmailReminderMinutes = GetStoredGoogleEmailReminderMinutes(calendarEvent);

            command.Parameters.AddWithValue("$id", calendarEvent.Id);
            command.Parameters.AddWithValue("$expected_calendar_id", calendarEvent.CalendarId);
            command.Parameters.AddWithValue("$expected_updated_at_utc_ticks", calendarEvent.UpdatedAt.UtcTicks);
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
        finally
        {
            ExitEventMutation();
        }
    }

'''
replace_between(
    repo,
    "    public async Task MarkSyncedAsync(",
    "    public async Task<int> MarkSyncedByIdsAsync",
    mark_synced,
)

apply_todo = '''    public async Task ApplyTodoReminderCleanupStateAsync(
        string localId,
        bool preserveDirtyState,
        string? cleanedGoogleEtag = null)
    {
        await EnterEventMutationAsync();
        try
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
        finally
        {
            ExitEventMutation();
        }
    }

'''
replace_between(
    repo,
    "    public async Task ApplyTodoReminderCleanupStateAsync(",
    "    public async Task<CalendarEvent?> FindDuplicateEventAsync",
    apply_todo,
)

sync = Path("FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs")
text = sync.read_text(encoding="utf-8")
old = '''                        localEvent.IsDirty = true;
                        localEvent.DirtyFields = EventDirtyFieldTracker.MergeFieldNames(localEvent.DirtyFields, "Reminder");
                        await _repository.SaveEventAsync(localEvent);
'''
new = '''                        localEvent.IsDirty = true;
                        localEvent.DirtyFields = EventDirtyFieldTracker.MergeFieldNames(localEvent.DirtyFields, "Reminder");
                        await _repository.ApplyTodoReminderCleanupStateAsync(
                            localEvent.Id,
                            preserveDirtyState: true);
'''
count = text.count(old)
if count != 1:
    raise RuntimeError(f"Expected exactly one ToDo cleanup SaveEventAsync block, found {count}")
sync.write_text(text.replace(old, new, 1), encoding="utf-8")

print("Applied final sync race production fixes")

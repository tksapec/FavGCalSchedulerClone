# Reliability Fixes Design

## Goal

Prevent calendar data loss, duplicate Google events, recurrence corruption, and silent Google-field drift while restoring regression coverage.

## Scope

This change set addresses the review findings in priority order:

1. Restore the deleted xUnit test project and prior regression suite.
2. Protect Google event identity during edits and synchronization.
3. Correct single-occurrence recurrence editing and RFC 5545 expansion behavior.
4. Preserve Google event time-zone identity across pull/edit/push.
5. Preserve Google default reminders unless the user actually edits reminder settings.
6. Make calendar-move undo retain enough remote identity to reverse the move safely.
7. Repair settings that are currently ignored or forcibly overwritten.
8. Serialize restore against reminder/sync activity.
9. Apply low-risk hardening to token persistence, publish output, and CI permissions.

.NET 10 migration is intentionally deferred until the behavioral fixes are green, because changing the runtime at the same time would make regressions harder to isolate.

## Identity invariant

A local event that already represents a Google event must not silently become a new Google event merely because a link field is missing in an edit path.

- `CalendarEvent.Id` remains the local primary key.
- `(CalendarId, GoogleEventId)` is the remote identity when `GoogleEventId` is present.
- Normal editing must preserve both identifiers.
- A cross-calendar move is an explicit delete-and-create operation and must retain the old and new remote identities for undo.
- Imported/unlinked records remain explicit local-new records; normal sync must not fuzzy-match title/time/location automatically.
- Database initialization should reject duplicate non-null `(calendar_id, google_event_id)` rows after legacy duplicates are diagnosed rather than silently choosing one.

## Recurrence model

Edited occurrences and deleted occurrences are different concepts.

- Editing one occurrence creates/reuses an exception identified by `RecurringParentId`/`RecurringEventId` + `OriginalStart`.
- Editing one occurrence must not add `EXDATE` to the master. The exception replaces the generated occurrence during local expansion.
- Deleting one occurrence may suppress the occurrence through a deleted exception / EXDATE-compatible representation.
- Recurrence expansion must follow RFC 5545 semantics for negative `BYMONTHDAY`, invalid month days, ordinal `BYDAY`, and monthly `BYDAY`.
- Use Ical.Net for recurrence calculation rather than extending the custom parser.

## Time zones

Timed Google events retain their source time-zone identifier.

- Add nullable start/end time-zone IDs to the local model and SQLite schema.
- `FromGoogleEvent` stores `Start.TimeZone` and `End.TimeZone`.
- `ToGoogleEvent` reuses stored IDs when available.
- Newly-created local timed events use the local IANA ID.
- Windows time-zone conversion uses the platform conversion API instead of falling back to Tokyo for arbitrary Windows IDs.

## Reminders

Google reminder settings are only rewritten when reminder settings are intentionally changed.

- Pull preserves `UseDefault` metadata.
- Ordinary edits do not convert `UseDefault=true` to explicit overrides.
- Push updates `destination.Reminders` only when `DirtyFields` contains `Reminder`, for existing linked events.
- New events still send their configured reminder payload.
- ToDo reminder cleanup remains explicit and continues to disable Google reminders for ToDo items.

## Calendar move and undo

A calendar move must be reversible after synchronization.

- Capture the pre-move and post-move remote identities in the undo operation.
- Undo of a synchronized move creates a tombstone for the move destination remote event and restores the original source identity.
- Unsynchronized moves continue to remove the pending source tombstone when undone.

## Restore exclusivity

Database restore runs under an application maintenance gate.

- Stop automatic sync/reminder checks before replacing the database.
- Prevent new sync/reminder operations from starting while restore is active.
- Reinitialize repositories/view models after restore, then resume monitoring.

## Settings repairs

- `DefaultNewEventIsAllDay` controls new schedule defaults.
- `CloseButtonExitsApplication` is represented by an actual settings UI control and respected by `Window_Closing`.
- Dead toast/email-adoption settings are not exposed as working behavior unless implemented.

## Testing

Restore the prior xUnit project first. Add regression tests for:

- linked edit -> exactly one Google update and zero inserts;
- unlinked local-new event -> insert remains valid;
- single recurrence occurrence edit remains visible and replaces only that occurrence;
- negative/ordinal/monthly recurrence cases through Ical.Net;
- time-zone pull/edit/push round trip;
- default reminder preserved on title-only edit and changed on reminder edit;
- calendar move -> sync -> undo -> sync removes destination remote copy;
- settings defaults and close behavior helpers;
- restore maintenance gate behavior where testable without UI.

Every production behavior change is introduced only after a regression test fails for the expected reason.
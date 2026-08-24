# Reliability Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore regression coverage and eliminate the reviewed data-integrity failures in editing, Google sync, recurrence, reminders, time zones, undo, restore, and settings.

**Architecture:** Keep the existing WPF/SQLite/Google Calendar structure. Tighten identity invariants at repository/sync boundaries, replace recurrence calculation with Ical.Net, persist source time-zone IDs, and make field-level sync ownership explicit. Avoid fuzzy Google-event matching.

**Tech Stack:** .NET 9 WPF, Microsoft.Data.Sqlite, Google Calendar API, xUnit, Ical.Net.

**Spec:** `docs/superpowers/specs/2026-08-24-reliability-fixes-design.md`

## Global Constraints

- Do not change the default branch directly; implement on `fix/reliability-review-20260824`.
- Restore the previous test suite before production changes.
- Production behavior changes require a failing regression test first.
- Do not automatically match unlinked events by title/time/location.
- Keep cross-calendar moves as explicit remote delete + create operations.
- Defer .NET 10 migration until behavior fixes are green.

---

### Task 1: Restore test baseline

**Files:**
- Restore: `FavGCalSchedulerClone.Tests/**` from commit `e73434a9ef6c07c724a754b6613631bafd8ac11d`

**Interfaces:**
- Consumes: existing solution test-project reference.
- Produces: runnable xUnit regression suite.

- [ ] Restore the complete deleted test tree from `e73434a...` without changing production code.
- [ ] Run `dotnet test FavGCalSchedulerClone.sln --configuration Release` in CI.
- [ ] Record any baseline failures before behavior changes.

### Task 2: Google identity and duplicate-edit regression

**Files:**
- Modify: `FavGCalSchedulerClone.Tests/GoogleCalendarSyncServiceTests.cs`
- Modify: `FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs`
- Modify: `FavGCalSchedulerClone.App/Repositories/CalendarRepository.cs`

**Interfaces:**
- Consumes: `CalendarEvent.GoogleEventId`, dirty event sync.
- Produces: explicit classification of local-new versus previously-linked-but-broken events and a database remote-identity invariant.

- [ ] Add a failing test where an existing linked event is edited, auto-synced, and pulled again; assert one remote event and an update operation, never insert.
- [ ] Add a failing repository test for duplicate non-null `(calendar_id, google_event_id)` identities.
- [ ] Keep blank `GoogleEventId` insert semantics only for genuinely local-new events; do not call `FindExactRemoteMatchAsync` from normal sync.
- [ ] Add/adjust repository uniqueness protection after legacy duplicate handling.
- [ ] Run targeted sync/repository tests and then the full suite.

### Task 3: Correct one-occurrence recurrence editing and recurrence engine

**Files:**
- Modify: `FavGCalSchedulerClone.Tests/MainViewModelRecurrenceReminderTests.cs`
- Modify: `FavGCalSchedulerClone.Tests/RecurrenceExpansionServiceTests.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.Recurrence.cs`
- Modify: `FavGCalSchedulerClone.App/Services/RecurrenceRuleHelper.cs`
- Modify: `FavGCalSchedulerClone.App/FavGCalSchedulerClone.App.csproj`

**Interfaces:**
- Consumes: recurrence lines stored in `CalendarEvent.RecurrenceJson`.
- Produces: RFC 5545-compliant occurrence expansion and exception replacement.

- [ ] Add a failing test: `ThisOccurrence` edit of a generated occurrence remains visible and replaces only the selected occurrence.
- [ ] Remove master `EXDATE` mutation from edit-only exception creation.
- [ ] Add failing tests for `BYMONTHDAY=-1`, invalid February month day, `MONTHLY;BYDAY=TU`, and ordinal `BYDAY=1MO/-1MO`.
- [ ] Add Ical.Net package reference and route recurrence expansion through it while preserving the existing JSON list storage format.
- [ ] Keep split/edit helpers compatible with stored Google recurrence lines.
- [ ] Run recurrence tests and full suite.

### Task 4: Persist and round-trip Google time zones

**Files:**
- Modify: `FavGCalSchedulerClone.App/Models/CalendarEvent.cs`
- Modify: `FavGCalSchedulerClone.App/Repositories/CalendarRepository.cs`
- Modify: `FavGCalSchedulerClone.App/Services/GoogleEventMapper.cs`
- Modify: `FavGCalSchedulerClone.App/Services/GoogleCalendarTimeZone.cs`
- Modify: relevant mapper/repository tests.

**Interfaces:**
- Produces: `StartTimeZoneId` and `EndTimeZoneId` nullable properties.

- [ ] Add failing mapper test for America/New_York pull -> local edit -> push retaining source time zone.
- [ ] Add nullable SQLite columns and persistence mapping.
- [ ] Store Google `Start.TimeZone`/`End.TimeZone` on pull and reuse them on push.
- [ ] Replace arbitrary Windows-zone Tokyo fallback with `TimeZoneInfo.TryConvertWindowsIdToIanaId` plus safe local fallback.
- [ ] Run mapper/repository and full tests.

### Task 5: Preserve Google default reminders on unrelated edits

**Files:**
- Modify: `FavGCalSchedulerClone.Tests/GoogleCalendarSyncServiceTests.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.EventEditing.cs`
- Modify: `FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs`
- Modify: `FavGCalSchedulerClone.App/Services/GoogleEventMapper.cs`

**Interfaces:**
- Consumes: `DirtyFields` with `Reminder` marker.
- Produces: reminder-specific remote mutation.

- [ ] Add failing test: title-only edit of `UseDefault=true` event leaves remote reminders unchanged.
- [ ] Add failing test: explicit reminder edit updates remote reminders.
- [ ] Stop converting reminder metadata to explicit overrides during unrelated editor saves.
- [ ] Apply `destination.Reminders` only for new events, ToDo cleanup, or dirty `Reminder` changes.
- [ ] Run reminder/sync and full tests.

### Task 6: Make synchronized calendar moves undoable

**Files:**
- Modify: `FavGCalSchedulerClone.App/Services/UndoService.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.BulkUndo.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.EventEditing.cs`
- Modify: relevant undo/sync tests.

**Interfaces:**
- Produces: undo metadata sufficient to tombstone a synchronized destination event.

- [ ] Add failing test for move A->B, sync, undo, sync; assert B remote event deleted and A restored.
- [ ] Extend undo capture to retain post-move remote identity where needed.
- [ ] On undo, create/delete the correct tombstones before restoring the source snapshot.
- [ ] Run undo/sync and full tests.

### Task 7: Repair settings and restore exclusivity

**Files:**
- Modify: `FavGCalSchedulerClone.App/Views/Dialogs/SettingsDialog.cs`
- Modify: `FavGCalSchedulerClone.App/MainWindow.xaml.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.EventEditing.cs`
- Modify: `FavGCalSchedulerClone.App/Services/ApplicationStartupService.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.BackupRestore.cs`
- Modify: relevant settings/startup tests.

**Interfaces:**
- Consumes: existing `CloseButtonExitsApplication`, `DefaultNewEventIsAllDay` settings.
- Produces: restore maintenance boundary.

- [ ] Add failing tests for all-day new-event default and close-button setting persistence helper behavior.
- [ ] Use `DefaultNewEventIsAllDay` in `BeginNewEvent`.
- [ ] Add a close behavior checkbox and stop forcing `CloseButtonExitsApplication=false`.
- [ ] Respect close behavior in `Window_Closing`.
- [ ] Add a maintenance gate around restore and pause/resume reminder/automatic sync operations.
- [ ] Run settings/startup/backup tests and full suite.

### Task 8: Low-risk hardening and final verification

**Files:**
- Modify: `FavGCalSchedulerClone.App/Services/ProtectedFileDataStore.cs`
- Modify: `scripts/publish-release.ps1`
- Modify: `.github/workflows/build-test.yml`
- Modify: `README.md` where behavior documentation changes.

**Interfaces:** none.

- [ ] Add test where practical for atomic token replacement helper; otherwise keep change minimal and isolated.
- [ ] Write DPAPI token data to a temporary file and atomically replace/move the destination.
- [ ] Clean/recreate `publish` output before publish.
- [ ] Add `permissions: contents: read` to CI workflow.
- [ ] Document CSV imports as new-event imports, not identity-preserving restore.
- [ ] Run `dotnet restore`, `dotnet build -c Release`, and `dotnet test -c Release` through GitHub Actions.
- [ ] Review the complete diff for accidental behavior/refactoring changes before opening the PR.
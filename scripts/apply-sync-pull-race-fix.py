from pathlib import Path


def replace_once(path: Path, old: str, new: str) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {count}: {old[:100]!r}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")


repo = Path(__file__).resolve().parents[1]
repository_path = repo / "FavGCalSchedulerClone.App/Services/CalendarRepository.cs"
sync_service_path = repo / "FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs"
atomic_writer_path = repo / "FavGCalSchedulerClone.App/Services/CalendarRepositoryAtomicWriter.cs"

old_upsert = '''    public async Task UpsertSyncedEventAsync(CalendarEvent calendarEvent)\n    {\n        await EnterEventMutationAsync();\n        try\n        {\n            var existing = await FindEventByGoogleEventIdAsync(calendarEvent.CalendarId, calendarEvent.GoogleEventId);\n            if (existing is not null)\n            {\n                calendarEvent.Id = existing.Id;\n            }\n\n            calendarEvent.IsDirty = false;\n            calendarEvent.DirtyFields = null;\n            calendarEvent.LastSyncedAt = DateTimeOffset.Now;\n            calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);\n            await UpsertEventAsync(calendarEvent);\n        }\n        finally\n        {\n            ExitEventMutation();\n        }\n    }\n'''
new_upsert = old_upsert + '''\n    public async Task<bool> TryUpsertSyncedEventAsync(CalendarEvent calendarEvent, CalendarEvent? plannedLocalEvent)\n    {\n        await EnterEventMutationAsync();\n        try\n        {\n            var existing = await FindEventByGoogleEventIdAsync(calendarEvent.CalendarId, calendarEvent.GoogleEventId);\n            if (plannedLocalEvent is null)\n            {\n                if (existing?.IsDirty == true)\n                {\n                    return false;\n                }\n            }\n            else if (existing is null\n                     || !string.Equals(existing.Id, plannedLocalEvent.Id, StringComparison.Ordinal)\n                     || existing.UpdatedAt.UtcTicks != plannedLocalEvent.UpdatedAt.UtcTicks)\n            {\n                return false;\n            }\n\n            if (existing is not null)\n            {\n                calendarEvent.Id = existing.Id;\n            }\n\n            calendarEvent.IsDirty = false;\n            calendarEvent.DirtyFields = null;\n            calendarEvent.LastSyncedAt = DateTimeOffset.Now;\n            calendarEvent.IsTodoLike = TagService.IsTodoLike(calendarEvent);\n            await UpsertEventAsync(calendarEvent);\n            return true;\n        }\n        finally\n        {\n            ExitEventMutation();\n        }\n    }\n'''
replace_once(repository_path, old_upsert, new_upsert)

old_pull = '''                case SyncPlanAction.PullRemote:\n                    try\n                    {\n                        await _repository.UpsertSyncedEventAsync(GoogleEventMapper.FromGoogleEvent(\n                            executableItem.RemoteEvent!,\n                            calendarId,\n                            GetDefaultReminders(reminderDefaults, calendarId),\n                            adoptEmailRemindersAsLocalNotifications));\n                        pulled++;\n                    }\n                    catch (Exception ex) when (ex is not OperationCanceledException)\n                    {\n                        failures.Add(CreatePullFailureDiagnostic(calendarId, null, null, ex, "PlanPull", ex.Message));\n                        failed++;\n                    }\n                    break;\n'''
new_pull = '''                case SyncPlanAction.PullRemote:\n                    try\n                    {\n                        var applied = await _repository.TryUpsertSyncedEventAsync(\n                            GoogleEventMapper.FromGoogleEvent(\n                                executableItem.RemoteEvent!,\n                                calendarId,\n                                GetDefaultReminders(reminderDefaults, calendarId),\n                                adoptEmailRemindersAsLocalNotifications),\n                            executableItem.LocalEvent);\n                        if (applied)\n                        {\n                            pulled++;\n                        }\n                        else\n                        {\n                            skipped++;\n                            conflicts++;\n                        }\n                    }\n                    catch (Exception ex) when (ex is not OperationCanceledException)\n                    {\n                        failures.Add(CreatePullFailureDiagnostic(calendarId, null, null, ex, "PlanPull", ex.Message));\n                        failed++;\n                    }\n                    break;\n'''
replace_once(sync_service_path, old_pull, new_pull)

old_indent = '''        try\n        {\n            var mutationSnapshots = items.Select(EventMutationSnapshot.Capture).ToArray();\n        await using var connection = repository.OpenConnection();\n        await using var transaction = connection.BeginTransaction();\n        try\n        {\n'''
new_indent = '''        try\n        {\n            var mutationSnapshots = items.Select(EventMutationSnapshot.Capture).ToArray();\n            await using var connection = repository.OpenConnection();\n            await using var transaction = connection.BeginTransaction();\n            try\n            {\n'''
replace_once(atomic_writer_path, old_indent, new_indent)

old_indent_tail = '''            await transaction.CommitAsync(cancellationToken);\n        }\n        catch\n        {\n            await RollbackSafelyAsync(transaction);\n            foreach (var snapshot in mutationSnapshots)\n            {\n                snapshot.Restore();\n            }\n            throw;\n        }\n        }\n        finally\n'''
new_indent_tail = '''                await transaction.CommitAsync(cancellationToken);\n            }\n            catch\n            {\n                await RollbackSafelyAsync(transaction);\n                foreach (var snapshot in mutationSnapshots)\n                {\n                    snapshot.Restore();\n                }\n                throw;\n            }\n        }\n        finally\n'''
replace_once(atomic_writer_path, old_indent_tail, new_indent_tail)

print("Applied pull race fix and formatting cleanup")

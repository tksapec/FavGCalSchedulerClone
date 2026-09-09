from pathlib import Path

base_script = Path("scripts/apply-final-sync-race-fixes.py").read_text(encoding="utf-8")
prefix_end = base_script.index('sync = Path("FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs")')
exec(compile(base_script[:prefix_end], "apply-final-sync-race-fixes.py", "exec"))

sync = Path("FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs")
text = sync.read_text(encoding="utf-8")
push_start = text.index("                case SyncPlanAction.PushLocal:")
push_end = text.index("                    var operation = GetPushOperation(localEvent);", push_start)
segment = text[push_start:push_end]
old_call = "await _repository.SaveEventAsync(localEvent);"
if segment.count(old_call) != 1:
    raise RuntimeError(f"Expected exactly one ToDo cleanup SaveEventAsync call in PushLocal block, found {segment.count(old_call)}")
segment = segment.replace(
    old_call,
    "await _repository.ApplyTodoReminderCleanupStateAsync(\n"
    "                                localEvent.Id,\n"
    "                                preserveDirtyState: true);",
    1,
)
sync.write_text(text[:push_start] + segment + text[push_end:], encoding="utf-8")
print("Applied final sync race production fixes (v2)")

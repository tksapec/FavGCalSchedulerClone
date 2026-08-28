# Reliability Review Remediation Implementation Plan

Date: 2026-08-28
Design: `docs/superpowers/specs/2026-08-28-reliability-review-remediation-design.md`
Branch: `fix/reliability-review-20260824`

## Execution rules

- Work only on `fix/reliability-review-20260824`; do not update `main`.
- Do not dispatch, rerun, or intentionally trigger GitHub Actions.
- Every repository commit created during this work uses `[skip ci]` or `[skip actions]`.
- For each confirmed defect: write the smallest deterministic regression test first, run it and observe the expected failure, then change production code, then rerun the focused test and adjacent tests.
- Do not report a test or build as passing unless it was freshly executed.
- If the active environment cannot execute the .NET/WPF test project, test code may be prepared and source-reviewed, but production changes remain behind the RED-execution gate.

## Task 1: Quick-search latest-wins behavior

Files:
- Test: `FavGCalSchedulerClone.Tests/SearchEventListTests.cs` or a focused `FavGCalSchedulerClone.Tests/QuickSearchConcurrencyRegressionTests.cs`
- Production: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.Search.cs`
- If a deterministic existing seam is insufficient, minimal test seam only: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.cs`

Steps:
1. Add a deterministic pending-search test for Clear while search awaits.
2. Run:
   `dotnet test FavGCalSchedulerClone.Tests/FavGCalSchedulerClone.Tests.csproj --filter FullyQualifiedName~QuickSearchConcurrencyRegressionTests`
   Expected RED: completed stale search makes results visible and/or forces Month view.
3. Add a test that starts query `A`, changes live `SearchQuery` to `B` before completion, and verifies stale `A` does not commit or label status as `B`.
4. Add a pending-search view-change test that verifies a stale completion cannot overwrite the latest view/return-view intent.
5. Implement a monotonic search generation in `MainViewModel.Search.cs`; capture query/year at request start; invalidate on Clear; apply results/status/view changes only for the current generation. Keep `RefreshCurrentYearSearchAsync` on the same latest-wins path.
6. Rerun the focused tests, then:
   `dotnet test FavGCalSchedulerClone.Tests/FavGCalSchedulerClone.Tests.csproj --filter FullyQualifiedName~SearchEventListTests`
7. Review selection clearing, visible-search refresh, failed-search preservation, and status text.
8. Commit: `[skip ci] fix: make quick search latest-wins`.

## Task 2: Automatic-sync duplicate rerun

Files:
- Test: `FavGCalSchedulerClone.Tests/SyncOperationGateTests.cs` or focused `FavGCalSchedulerClone.Tests/AutomaticSyncRerunRegressionTests.cs`
- Production: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.Sync.cs`

Steps:
1. Add a controlled long-running sync test that begins `RunAutomaticSyncIfDueAsync`, invokes a second automatic due check while the first is still in progress, then releases the first run.
2. Assert only one automatic synchronization reaches the sync service/API before the normal interval becomes due.
3. Run the focused test and observe RED: the current pending invocation logic immediately performs a second Automatic sync.
4. Change rerun arbitration so an Automatic invocation is not queued solely because a sync is in progress; alternatively route a pending Automatic through `RunAutomaticSyncIfDueAsync` after completion so `LastAutomaticSyncAt` is rechecked.
5. Preserve Manual and LocalChange pending priority behavior.
6. Add/retain tests proving Manual and LocalChange reruns are not suppressed.
7. Rerun focused sync tests and `SyncOperationGateTests`.
8. Review cancellation/error paths and `LastAutomaticSyncAt` update ordering.
9. Commit: `[skip ci] fix: avoid duplicate automatic sync rerun`.

## Task 3: Reminder delivery durability

Files:
- Test: `FavGCalSchedulerClone.Tests/ReminderNotificationServiceTests.cs` or focused `FavGCalSchedulerClone.Tests/ReminderDeliveryDurabilityRegressionTests.cs`
- Production: `FavGCalSchedulerClone.App/Services/ReminderNotificationService.cs`

Steps:
1. Reuse the existing fake notifier/repository pattern and create a repository failure that affects reminder-history persistence after successful notification while fired-state persistence remains writable.
2. First reminder check: assert notifier is called once even though history persistence fails.
3. Second reminder check at the same due state: assert notifier call count remains one.
4. Run the focused test and observe RED: current code exits before `SaveFiredStateAsync`, allowing a duplicate delivery.
5. Reorder the successful-delivery path so the fired marker is durably persisted before history/diagnostic persistence. Keep snooze cleanup consistent with the durable fired state.
6. Treat history persistence failure as contained/non-delivery-critical after fired state is saved.
7. Rerun focused test plus `ReminderFailureContainmentRegressionTests`, `ReminderHistoryResilienceTests`, and relevant `ReminderNotificationServiceTests`.
8. Review cancellation semantics: do not record fired before an actual successful delivery.
9. Commit: `[skip ci] fix: persist reminder fired state before history`.

## Task 4: Complete recurrence-set expansion semantics

Files:
- Test: `FavGCalSchedulerClone.Tests/RecurrenceExpansionServiceTests.cs`
- Test: `FavGCalSchedulerClone.Tests/RecurrenceRuleRegressionTests.cs`
- Test as needed: `FavGCalSchedulerClone.Tests/RecurrenceResilienceTests.cs`
- Production: `FavGCalSchedulerClone.App/Services/RecurrenceRuleHelper.cs`
- Production: `FavGCalSchedulerClone.App/Services/RecurrenceExpansionService.cs`
- Existing dependency/config: `FavGCalSchedulerClone.App/FavGCalSchedulerClone.App.csproj`

Steps:
1. Add `RRULE + RDATE` regression: the RDATE occurrence must appear in the expanded window.
2. Add multiple-RRULE regression: union both rules without duplicate occurrences.
3. Add `EXDATE` regression against the combined set.
4. Add RDATE-only recurring-master regression.
5. Keep malformed recurrence containment test.
6. Run focused recurrence tests and observe RED for unsupported recurrence-set cases.
7. Inspect Ical.Net 5.2.3 APIs already referenced by the project; use its recurrence evaluation where it provides correct recurrence-set union/exclusion semantics, avoiding a second custom parser. If Ical.Net requires a small adapter, isolate it in `RecurrenceRuleHelper`.
8. Preserve occurrence duration, all-day behavior, original-start identity, timezone handling, deduplication, and window clipping.
9. Rerun recurrence expansion, EXDATE timezone, recurrence scope timezone, split, and resilience tests.
10. Review recurrence JSON serialization boundaries to ensure Google recurrence lines are not normalized destructively.
11. Commit: `[skip ci] fix: expand complete recurrence sets`.

## Task 5: Safe recurrence-exception recovery

Files:
- Test: `FavGCalSchedulerClone.Tests/GoogleCalendarSyncServiceTests.cs` or focused `FavGCalSchedulerClone.Tests/RecurrenceExceptionRecoveryRegressionTests.cs`
- Production: `FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs`
- Reference-only mapping check: `FavGCalSchedulerClone.App/Services/GoogleEventMapper.cs`

Steps:
1. Add fake-client test: recurrence exception cannot resolve its remote instance; assert `InsertEventAsync` is never called and the local item is not falsely marked synchronized.
2. Add fake-client test: remote instance resolves, but Get/Update returns NotFound; assert no standalone insert occurs.
3. Retain/add successful recurrence-instance update test.
4. Run focused tests and observe RED: current NotFound branch performs `InsertEventAsync` and marks the local event synced.
5. Remove the recurrence-exception standalone insert fallback. Propagate/return a sync failure through the service's existing failure accounting so the local exception remains dirty/retryable.
6. Rerun focused tests plus recurrence synchronization/conflict tests in `GoogleCalendarSyncServiceTests`.
7. Review parent resolution, instance identity matching, tombstone handling, dirty-field handling, and retry diagnostics.
8. Commit: `[skip ci] fix: fail safely for missing recurrence instance`.

## Task 6: Recurrence-safe initial Google synchronization

Files:
- Test: `FavGCalSchedulerClone.Tests/GoogleCalendarSyncServiceTests.cs` or focused `FavGCalSchedulerClone.Tests/InitialSyncRecurrenceWindowRegressionTests.cs`
- Production: `FavGCalSchedulerClone.App/Services/GoogleCalendarSyncService.cs`
- Request mapping reference: `FavGCalSchedulerClone.App/Services/GoogleCalendarApi.cs`

Steps:
1. Add fake API/client request-capture test for first sync with no stored sync token.
2. Assert initial parent-event list request uses `SingleEvents=false` and `TimeMin=null`.
3. Add/retain incremental sync assertion: with a sync token, request uses the token and does not add incompatible time filters.
4. Add/retain pagination test.
5. Run focused test and observe RED: current first sync supplies `DateTimeOffset.Now.AddYears(-5)` as TimeMin.
6. Remove the initial 5-year lower bound for the parent-event full sync. Keep token recovery, deletion handling, page traversal, and `SingleEvents=false` unchanged.
7. Rerun focused and broader Google sync tests.
8. Review first-sync performance implications and ensure the change is correctness-only rather than a new feature.
9. Commit: `[skip ci] fix: preserve long-running recurring series on initial sync`.

## Task 7: High-priority integrated verification and review

Files: all files touched in Tasks 1-6 plus adjacent tests.

Steps:
1. Run targeted tests for all six regressions.
2. Run full test project:
   `dotnet test FavGCalSchedulerClone.Tests/FavGCalSchedulerClone.Tests.csproj`
3. Run build:
   `dotnet build FavGCalSchedulerClone.sln --configuration Release`
4. Review touched diffs for stale-state commits, cancellation races, transaction/durability ordering, Google identity correctness, recurrence deduplication, and timezone regressions.
5. Verify branch HEAD and ensure `main` remains unchanged.
6. Verify no workflow run was created by remediation commits.
7. If review finds another actionable defect, create its RED test and repeat the same cycle before moving on.

## Task 8: Secondary finding confirmation/fixes

### 8A. Malformed recurrence presentation
- Add a deterministic test defining desired repair visibility for a malformed recurring master.
- Only if RED demonstrates harmful disappearance, provide a bounded fallback that does not fabricate recurrence instances.

### 8B. WPF stale drag state
- Reproduce on a Windows/WPF runner with click-release followed by pointer movement/interaction.
- Add an interaction-level regression or the closest deterministic event-handler test available.
- Only then clear pending drag state at an appropriate release/capture-loss boundary without breaking real drag/drop.

### 8C. CSV literal apostrophe/formula-prefix round trip
- Add `'=foo` export/import round-trip test.
- If RED, make neutralization/restoration bijective while retaining formula-injection protection.

### 8D. Atomic writer caller-object mutation
- Add a failing-transaction test that snapshots caller-owned event objects before the write and asserts they remain unchanged after rollback.
- If RED and a real caller-visible mutation is demonstrated, stage cloned objects inside the writer and copy committed state only after success.

For each confirmed secondary defect: RED -> minimal production change -> GREEN -> adjacent tests -> review -> `[skip ci]` commit.

## Completion gate

The remediation branch is ready for merge consideration only when:
- all confirmed high-priority regressions are covered and pass;
- all secondary findings are either fixed with passing regressions or explicitly dismissed by evidence;
- full available tests/build pass freshly;
- final static review yields no remaining actionable finding in the reviewed scope;
- no GitHub Actions run was intentionally triggered;
- `main` has not been modified during the remediation cycle.

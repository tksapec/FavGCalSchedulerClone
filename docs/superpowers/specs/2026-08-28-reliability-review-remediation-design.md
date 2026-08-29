# Reliability Review Remediation Design

Date: 2026-08-28
Branch: `fix/reliability-review-20260824`

## Purpose

Resolve the remaining reliability findings identified by repeated source review without changing the product's intended user-facing behavior. Each fix must be protected by a regression test, followed by another review pass. The cycle continues until no actionable source-level findings remain within the reviewed scope.

## Constraints

- Do not modify `main` during remediation.
- Do not intentionally trigger GitHub Actions.
- GitHub commits created for this work must include `[skip ci]` or `[skip actions]`.
- Preserve current UI, storage schema, Google Calendar API surface, and normal synchronization semantics unless a change is required to correct a confirmed reliability defect.
- Prefer minimal changes that follow existing project patterns.
- Production changes are test-driven: add a regression test that demonstrates the defect before applying the corresponding fix, then re-run targeted and broader verification where an executable test environment is available.
- Do not claim build or test success without fresh executable evidence.

## Scope

### 1. Quick-search async result race

Problem:
`RunCurrentYearSearchAsync` can commit a stale result after the search has been cleared, after the query has changed, or after the view has changed while the asynchronous search is in flight.

Design:
- Capture the year and query at search start.
- Add a monotonic search generation/version following the existing latest-wins navigation pattern.
- `ClearCurrentYearSearch` invalidates the current generation.
- Commit results, status text, visibility, and return-view state only when the completed request is still the active generation.
- Status text is derived from the captured query, not the live `SearchQuery` value.

Regression coverage:
- Clear while pending does not reopen results or force Month view.
- Query changed while pending discards the stale result.
- View changed while pending does not restore stale view state.
- Existing successful sequential search behavior remains unchanged.

### 2. Automatic-sync queued duplicate

Problem:
A timer tick that occurs while a long automatic sync is still running can enqueue another `Automatic` invocation. After the first sync succeeds and updates `LastAutomaticSyncAt`, the queued invocation is executed directly and can perform an immediate duplicate automatic sync.

Design:
- Do not queue an `Automatic` rerun merely because the automatic timer fired while synchronization is already in progress.
- Preserve queued rerun semantics for `Manual` and `LocalChange` invocations.
- If an automatic rerun path remains necessary, it must re-enter the due-time check after the current sync finishes rather than calling `SynchronizeAsync(Automatic)` unconditionally.

Regression coverage:
- Repeated automatic tick during an active sync does not cause a second immediate automatic sync.
- Manual and local-change rerun priority remains unchanged.

### 3. Reminder delivery durability

Problem:
After a notification is successfully delivered, reminder history persistence can fail before fired-state persistence. A later tick can then deliver the same notification again.

Design:
- Treat successful delivery as the durability boundary.
- Persist the fired marker before non-critical history/diagnostic persistence.
- A reminder-history write failure after delivery must not remove or bypass the fired marker.
- Preserve existing containment behavior for repository failures.

Regression coverage:
- Notification succeeds, history write fails, next check does not notify again.
- Normal fired-state and history persistence still works.

### 4. Recurrence expansion semantics

Problem:
The local recurrence expansion currently takes only the first `RRULE` and separately handles `EXDATE`; `RDATE`, additional `RRULE` entries, and other supported recurrence-set lines can be lost even though they are preserved in `RecurrenceJson`.

Design:
- Use the existing Ical.Net dependency to evaluate the recurrence set rather than implementing another partial recurrence parser where practical.
- Preserve Google recurrence lines exactly in stored recurrence metadata.
- At minimum, correctly combine multiple `RRULE` entries and `RDATE` additions and subtract `EXDATE` exclusions for the local expansion window.
- Keep exception handling bounded so malformed recurrence data cannot crash calendar refresh.
- Avoid changing recurrence edit serialization unless required for correctness.

Regression coverage:
- `RRULE + RDATE` includes the extra date.
- Multiple `RRULE` entries contribute their union.
- `EXDATE` still excludes matching occurrences.
- An `RDATE`-only recurring master remains representable locally.
- Malformed recurrence remains contained.

### 5. Recurrence-exception missing-instance fallback

Problem:
When a recurrence exception's remote instance cannot be resolved, or an update returns NotFound, the service falls back to `events.insert`. The mapper cannot recreate immutable recurring-instance identity (`recurringEventId` / `originalStartTime`) through a normal insert, so this can create an invalid or standalone event instead of repairing the series instance.

Design:
- Never use standalone event insertion as a fallback for a missing recurring instance.
- If the expected remote recurring instance cannot be resolved, keep the local exception dirty and return/record a synchronization failure that is visible to existing diagnostics/error handling.
- Preserve the normal instance-update path when the remote instance exists.
- Do not silently convert a recurrence exception into a standalone event.

Regression coverage:
- Missing remote recurrence instance does not call insert.
- NotFound during recurrence-instance update does not call insert.
- Local item remains eligible for retry / is not falsely marked synchronized.
- Existing successful recurrence-instance update remains unchanged.

### 6. Initial Google sync recurrence-safe window

Problem:
The first Google sync uses a 5-year `timeMin` while requesting `singleEvents=false`. A recurring parent whose first occurrence ended more than 5 years ago can be omitted even if the series still produces current/future instances.

Design:
- The initial parent-event sync must not use a lower time bound that can exclude long-running recurring masters.
- Prefer correctness: perform the first parent-event synchronization without `timeMin`, then rely on the sync token for incremental changes.
- Keep `singleEvents=false`, deletion handling, pagination, and sync-token recovery behavior unchanged unless a test proves another change is required.

Regression coverage:
- Initial full synchronization request does not apply the 5-year cutoff when fetching parent events.
- Incremental sync still uses the sync token and its required request restrictions.
- Pagination behavior remains unchanged.

## Secondary findings after high-priority remediation

After the six items above are fixed and verified, repeat the review for the following lower-priority findings before considering the branch complete:

- malformed recurring master can disappear from calendar presentation;
- possible stale drag state after a normal click in WPF event segments;
- CSV round-trip ambiguity for a literal value beginning with `'=...`;
- atomic writer mutates caller-owned event objects before the database transaction commits.

These are not to be changed speculatively. Each must first be confirmed by a deterministic test or, for pointer interaction, a reproducible WPF behavior before a production fix is applied.

## Implementation sequence

1. Add and execute regression tests for quick-search concurrency; apply the minimal latest-wins fix.
2. Add and execute the automatic-sync duplicate regression; adjust automatic rerun behavior.
3. Add and execute the reminder durability regression; reorder persistence safely.
4. Add recurrence-set tests; replace/extend recurrence expansion with Ical.Net-backed semantics.
5. Add recurrence-exception NotFound tests; remove unsafe insert fallback.
6. Add initial-sync request tests; remove the recurrence-unsafe initial lower bound.
7. Run targeted tests after each change and the full available suite after the group.
8. Perform another static review of all touched flows plus adjacent error/cancellation paths.
9. Confirm or dismiss secondary findings with tests/reproduction; fix only confirmed defects.
10. Repeat verification and review until no actionable findings remain in scope.

## Verification standard

A finding is considered resolved only when all of the following are true:

- a deterministic regression test exists where practical;
- the production fix is minimal and directly addresses the reproduced cause;
- the targeted regression passes in an executable environment;
- relevant existing tests still pass;
- static re-review finds no new correctness, cancellation, persistence, or identity regression introduced by the change.

Because this repository is a Windows/WPF .NET application, source review alone is not sufficient evidence for final completion. If the current execution environment cannot run the required .NET/WPF tests, implementation must not be represented as verified until a suitable test runner is available.

## Non-goals

- UI redesign.
- Database schema redesign.
- New synchronization features.
- Broad refactoring unrelated to the confirmed findings.
- Merging to `main` as part of this remediation cycle.

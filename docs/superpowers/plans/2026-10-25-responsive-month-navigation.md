# Responsive Month Navigation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make month and year navigation in month view update the date grid immediately while rendering event details only for the final destination after rapid input settles.

**Architecture:** Reuse the existing immediate calendar shell, cancellation token, refresh generation, and snapshot cache. In month view, replace the current grid with an empty date shell immediately and debounce detailed refresh for 150ms; week/day views retain their 10ms default. Ensure stale refreshes cannot apply after a later navigation.

**Tech Stack:** C#, WPF, .NET 9, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-25-responsive-month-navigation-design.md`

## Global Constraints

- Keep week/day navigation, today navigation, and search-result date navigation behavior unchanged.
- Do not change sync, persistence format, or dependencies.
- Preserve multiple clicks, including mixed directions, and never apply a stale month snapshot.
- Do not trigger GitHub Actions.

## Review Focus

- Rapid alternating clicks: final month matches the last click and only final month is refreshed.
- Older refresh completing late: it must not replace the newest month.
- Month/year boundaries and month-end anchor dates: selected date remains valid.
- Cached and uncached destinations: immediate shell contains the correct date grid without expensive event layout.
- Refresh failure/cancellation: UI remains navigable and later requests still refresh.

### Task 1: Regression tests and month-navigation scheduling

**Files:**
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.CalendarNavigation.cs`
- Modify: `FavGCalSchedulerClone.Tests/CalendarNavigationPerformanceRegressionTests.cs`

- [x] Add failing tests that verify quick sequential month/year commands update `CurrentMonth` immediately, defer cached snapshots, keep the 150ms month-view debounce separate from the 10ms week/day delay, and restore cached snapshots after returning to the same month.
- [x] Run the focused regression test and confirm cached month application fails the expected assertion before the change.
- [x] Implement the smallest scheduling change that meets the tests, preserving existing generation and cancellation checks.
- [x] Run focused navigation and performance regression tests.

### Task 2: Review and full verification

**Files:**
- Review all changed files and the design/plan documents.

- [x] Inspect cancellation, dispatcher/UI application, cache-hit behavior, and navigation commands for stale results and regressions.
- [x] Run `git diff --check`.
- [x] Run `dotnet build FavGCalSchedulerClone.sln --configuration Release --no-restore --verbosity minimal`.
- [x] Run `dotnet test FavGCalSchedulerClone.Tests/FavGCalSchedulerClone.Tests.csproj --configuration Release --no-restore --no-build --blame-hang-timeout 90s --logger "console;verbosity=minimal"` (908 passed).
- [x] Commit the design, plan, implementation, and tests on `main`; push `main` with `[skip ci]` and verify local and remote revisions match.

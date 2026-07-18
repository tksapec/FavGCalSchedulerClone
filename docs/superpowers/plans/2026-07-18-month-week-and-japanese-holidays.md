# Month Week Numbers and Japanese Holidays Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display ISO week numbers in the month view and use refreshable Cabinet Office holiday data for holiday cell backgrounds.

**Architecture:** Keep week-number calculation and holiday parsing as small services/models. The view model exposes six week-number rows and recomputes holiday state whenever the official data changes. The settings dialog owns the explicit refresh interaction and reports its result.

**Tech Stack:** .NET 9, WPF, xUnit, `HttpClient`, Cabinet Office Shift-JIS CSV.

## Global Constraints

- Use `https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv` as the refresh source.
- Keep the embedded CSV usable when network refresh fails.
- `#workday` overrides official and user-defined holiday colouring.
- Preserve the current compact month header and current dirty UI edits.

---

### Task 1: Holiday data service

**Files:**
- Create: `FavGCalSchedulerClone.App/Services/JapaneseHolidayService.cs`
- Create: `FavGCalSchedulerClone.App/Data/JapaneseHolidays.csv`
- Modify: `FavGCalSchedulerClone.App/FavGCalSchedulerClone.App.csproj`
- Modify: `FavGCalSchedulerClone.App/Services/AppPaths.cs`
- Test: `FavGCalSchedulerClone.Tests/JapaneseHolidayServiceTests.cs`

- [ ] **Step 1: Write failing tests** for Shift-JIS parsing, the 2026-01-01 name, and update failure retaining the in-memory data.
- [ ] **Step 2: Run** `dotnet test .\\FavGCalSchedulerClone.Tests\\FavGCalSchedulerClone.Tests.csproj --filter FullyQualifiedName~JapaneseHolidayServiceTests --nologo`; expect compilation failure because the service is absent.
- [ ] **Step 3: Implement** `ParseCsv(string)`, `GetHolidayName(DateOnly)`, and `UpdateFromOfficialSourceAsync(HttpClient, string, IAppLogger?)`. Load the embedded CSV first; on a successful non-empty parse, atomically replace `AppPaths.JapaneseHolidayOverridePath` and raise `HolidaysChanged`.
- [ ] **Step 4: Re-run the targeted tests**; expect all holiday-service tests to pass.

### Task 2: Week numbers and calendar-day state

**Files:**
- Create: `FavGCalSchedulerClone.App/Models/CalendarWeekNumber.cs`
- Modify: `FavGCalSchedulerClone.App/Models/CalendarDay.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.cs`
- Modify: `FavGCalSchedulerClone.App/ViewModels/MainViewModel.CalendarNavigation.cs`
- Test: `FavGCalSchedulerClone.Tests/CalendarWeekNumberTests.cs`
- Test: `FavGCalSchedulerClone.Tests/MainViewModelViewModeTests.cs`

- [ ] **Step 1: Write failing tests** for the 2025-12-29 ISO `W01` boundary, Sunday-start grids using the following Monday, official holiday state, and `#workday` precedence.
- [ ] **Step 2: Run** the targeted tests; expect missing `CalendarWeekNumber` / `HolidayName` members.
- [ ] **Step 3: Implement** six `CalendarWeekNumber.CreateRows` entries, `CalendarDay.HolidayName`/`DayToolTipText`, `MonthWeekNumbers`, and a single `ApplyHolidayState` method called from both full and one-day refresh paths. Subscribe to `HolidaysChanged` to refresh visible day shells without rereading SQLite.
- [ ] **Step 4: Re-run targeted tests**; expect all pass.

### Task 3: Month view and explicit refresh UI

**Files:**
- Modify: `FavGCalSchedulerClone.App/MainWindow.xaml`
- Modify: `FavGCalSchedulerClone.App/MainWindow.xaml.cs`
- Modify: `FavGCalSchedulerClone.App/Views/Dialogs/SettingsDialog.cs`
- Test: `FavGCalSchedulerClone.Tests/MainWindowMenuTests.cs`

- [ ] **Step 1: Write failing source-shape tests** for a 30px left week column, `MonthWeekNumbers` binding, and settings refresh command wiring.
- [ ] **Step 2: Run** the targeted source-shape test; expect it to fail because the bindings and button are absent.
- [ ] **Step 3: Implement** the month-only week column and a settings button labelled `祝日を更新`. Its handler uses `RunUiActionAsync`, calls the holiday service with a fresh `HttpClient`, and displays success/failure without clearing existing data.
- [ ] **Step 4: Re-run targeted tests**; expect all pass.

### Task 4: End-to-end verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document** the Cabinet Office source, offline fallback, update location, and ISO week-number meaning.
- [ ] **Step 2: Build and test** with `dotnet build .\\FavGCalSchedulerClone.sln --nologo` then `dotnet test .\\FavGCalSchedulerClone.sln --no-build --nologo`; expect zero warnings/errors and all tests passing.
- [ ] **Step 3: Run the WPF app** and confirm a known holiday has holiday background, six left week-number rows align with the month grid, and the update action reports success while preserving the view.

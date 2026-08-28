using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FavGCalSchedulerClone.App.Commands;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Win32;

namespace FavGCalSchedulerClone.App.ViewModels;

public sealed partial class MainViewModel
{

    public Task<BackupResult> BackupAllCalendarsAsync(string backupZipPath) =>
        RunExclusiveSyncDataOperationAsync(() => BackupAllCalendarsCoreAsync(backupZipPath));

    private async Task<BackupResult> BackupAllCalendarsCoreAsync(string backupZipPath)
    {
        await _repository.InitializeAsync();
        var result = await _backupService.CreateBackupAsync(_repository.DatabasePath, backupZipPath);
        Status = $"バックアップを作成しました: {Path.GetFileName(result.BackupPath)}";
        return result;
    }

    public async Task<RestoreResult> RestoreAllCalendarsAsync(string backupZipPath)
    {
        if (!TryBeginDatabaseMaintenanceState())
        {
            throw new InvalidOperationException("データベースのメンテナンス処理が既に実行中です。");
        }

        var reminderWasRunning = false;
        var reminderResumeRequired = false;
        var repositoryMaintenanceStarted = false;
        var syncDataGateEntered = false;
        var displayMonthPersistenceGateEntered = false;
        var settingsPersistenceGateEntered = false;
        var databaseReplaced = false;
        try
        {
            if (Volatile.Read(ref _syncInProgress) != 0)
            {
                throw new InvalidOperationException("Google同期中はバックアップをリストアできません。同期完了後に再実行してください。");
            }

            // Diagnostic resync/discard operations do not use _syncInProgress, but they
            // mutate the same sync/event data. Wait for them before replacing the DB.
            await _syncDataOperationGate.WaitAsync();
            syncDataGateEntered = true;

            if (Volatile.Read(ref _syncInProgress) != 0)
            {
                throw new InvalidOperationException("Google同期中はバックアップをリストアできません。同期完了後に再実行してください。");
            }

            // A delayed DisplayMonth save captures a full settings snapshot before it
            // reaches _settingsPersistenceGate. Drain it first, then block all remaining
            // settings writes so no pre-restore snapshot can be applied to the restored DB.
            // Keep the same lock order used by PersistDisplayMonthAsync: display -> settings.
            await _displayMonthPersistenceGate.WaitAsync();
            displayMonthPersistenceGateEntered = true;
            await _settingsPersistenceGate.WaitAsync();
            settingsPersistenceGateEntered = true;

            if (_reminderService is not null)
            {
                // PauseForMaintenanceAsync stops the timer before persisting diagnostics.
                // Mark recovery as required before awaiting it so a diagnostics write failure
                // cannot leave reminder monitoring permanently paused.
                reminderWasRunning = _reminderService.IsRunning;
                reminderResumeRequired = true;
                await _reminderService.PauseForMaintenanceAsync();
            }

            await _repository.BeginMaintenanceAsync();
            repositoryMaintenanceStarted = true;
            var result = await _backupService.RestoreBackupAsync(backupZipPath, _repository.DatabasePath);
            databaseReplaced = true;
            ResetTransientStateAfterDatabaseRestore();

            // Keep normal repository callers blocked until every view-model collection,
            // cache and setting has been reloaded from the restored database. This flow
            // already owns _syncDataOperationGate, so the calendar-list reload must call
            // its core implementation rather than re-entering the public gated wrapper.
            try
            {
                await _repository.RunWithMaintenanceAccessAsync(ReloadRestoredViewModelStateAsync);
            }
            catch (Exception ex)
            {
                const string message = "バックアップDBの復元は完了しましたが、画面状態の再読み込みに失敗しました。アプリを再起動してください。";
                Status = message;
                throw new InvalidOperationException(message, ex);
            }

            _repository.EndMaintenance();
            repositoryMaintenanceStarted = false;

            Status = "バックアップからリストアしました。Google認証は必要に応じて再実行してください。";
            return result;
        }
        finally
        {
            try
            {
                if (repositoryMaintenanceStarted)
                {
                    _repository.EndMaintenance();
                }

                if (reminderResumeRequired)
                {
                    await _reminderService!.ResumeAfterMaintenanceAsync(reminderWasRunning);
                }
            }
            finally
            {
                if (databaseReplaced)
                {
                    MarkRestoredSettingsPersistenceBaseline();
                }

                if (settingsPersistenceGateEntered)
                {
                    _settingsPersistenceGate.Release();
                }
                if (displayMonthPersistenceGateEntered)
                {
                    _displayMonthPersistenceGate.Release();
                }
                if (syncDataGateEntered)
                {
                    _syncDataOperationGate.Release();
                }
                EndDatabaseMaintenanceState();
            }
        }
    }

    private void ResetTransientStateAfterDatabaseRestore()
    {
        _undoService.Clear();
        NotifyUndoStateChanged();

        _labelClipboard = null;
        OnPropertyChanged(nameof(CanPasteEventLabel));
        OnPropertyChanged(nameof(CanCutSelectedEventLabel));

        ClearCurrentYearSearch();
        SelectedEvent = null;
        _pendingSelectedDate = null;
        _navigationAnchorDate = null;
    }

    private void MarkRestoredSettingsPersistenceBaseline()
    {
        lock (_settingsStateLock)
        {
            // All requests created before the database replacement are now obsolete.
            // A future post-restore edit increments _settingsRevision and can persist normally.
            _persistedSettingsRevision = Math.Max(_persistedSettingsRevision, _settingsRevision);
        }
    }

    private async Task ReloadRestoredViewModelStateAsync()
    {
        await _repository.InitializeAsync();
        var loadedSettings = AppSettingsNormalizer.Normalize(await LoadSettingsSafelyAsync());
        lock (_settingsStateLock)
        {
            _settings = loadedSettings;
        }
        OAuthClientJsonPath = _settings.OAuthClientJsonPath ?? "";
        OnPropertyChanged(nameof(CalendarLabelFontSize));
        OnPropertyChanged(nameof(SideListFontSize));
        OnPropertyChanged(nameof(WindowOpacity));
        OnPropertyChanged(nameof(WeekdayHeaders));
        SelectedTabIndex = _settings.StartupTabIndex;
        SelectedTodoTabIndex = _settings.StartupTodoTabIndex;
        CurrentViewMode = _settings.StartupCalendarViewMode;
        await ReloadScheduleHistoryAsync();
        SelectedDay = null;
        await ReloadTagsAsync();
        _eventColorPalette = await _syncService.LoadCachedEventColorPaletteAsync();
        await ReloadAvailableCalendarsCoreAsync();
        SetCurrentMonthWithoutRefreshing(_settings.DisplayMonth);
        await RefreshCalendarAsync();
        await RefreshOperationalStatusAsync(null);
        Status = "準備完了";
    }

    public async Task<BackupResult> CreateDiagnosticsBulkBackupAsync()
    {
        await _repository.InitializeAsync();
        var backupPath = Path.Combine(
            AppPaths.AppDataDirectory,
            "backups",
            $"diagnostics-bulk-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        return await _backupService.CreateBackupAsync(_repository.DatabasePath, backupPath);
    }
}

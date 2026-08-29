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
        Exception? restoreFailure = null;
        Exception? reminderResumeFailure = null;
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

            // Navigation refresh/prefetch workers bypass the sync-data gate and can carry
            // an old snapshot beyond the database swap. Cancel and invalidate them before
            // repository maintenance so no pre-restore result can be applied afterward.
            CancelCalendarBackgroundWorkForDatabaseMaintenance();
            await _repository.BeginMaintenanceAsync();
            repositoryMaintenanceStarted = true;
            var result = await _backupService.RestoreBackupAsync(backupZipPath, _repository.DatabasePath);
            databaseReplaced = true;

            // Once RestoreBackupAsync returns, the live database has already been replaced.
            // Any failure while resetting transient state or rebuilding the ViewModel must
            // therefore be reported as "DB restored, restart required" rather than as a
            // generic restore failure. Keep normal repository callers blocked until this
            // whole post-replacement reconstruction succeeds.
            try
            {
                ResetTransientStateAfterDatabaseRestore();
                await _repository.RunWithMaintenanceAccessAsync(ReloadRestoredViewModelStateAsync);
            }
            catch (Exception ex)
            {
                // Reload can itself launch a new calendar prefetch before a later reload
                // step fails. Cancel that work before exposing the restart-required state.
                CancelCalendarBackgroundWorkForDatabaseMaintenance();
                // Clear the reminder service's maintenance pause in finally, but do not
                // restart monitoring against a partially rebuilt in-memory state.
                reminderWasRunning = false;
                MarkDatabaseRestartRequired();
                const string message = "バックアップDBの復元は完了しましたが、画面状態の再読み込みに失敗しました。アプリを再起動してください。";
                Status = message;
                throw new InvalidOperationException(message, ex);
            }

            _repository.EndMaintenance();
            repositoryMaintenanceStarted = false;

            Status = "バックアップからリストアしました。Google認証は必要に応じて再実行してください。";
            return result;
        }
        catch (Exception ex)
        {
            restoreFailure = ex;
            throw;
        }
        finally
        {
            try
            {
                // A partial post-swap reload leaves both the in-memory ViewModel and any
                // future direct repository caller untrusted. Keep repository maintenance
                // latched as a second line of defense until the process is restarted.
                if (repositoryMaintenanceStarted && !IsDatabaseRestartRequired)
                {
                    _repository.EndMaintenance();
                }

                if (reminderResumeRequired)
                {
                    try
                    {
                        await _reminderService!.ResumeAfterMaintenanceAsync(reminderWasRunning);
                    }
                    catch (Exception ex)
                    {
                        reminderResumeFailure = ex;
                        _logger?.LogError(ex, "Failed to resume reminder monitoring after database restore maintenance.");
                    }
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

            if (reminderResumeFailure is not null && restoreFailure is null && databaseReplaced)
            {
                const string message = "バックアップDBの復元は完了しましたが、通知監視の再開に失敗しました。アプリを再起動してください。";
                Status = message;
                throw new InvalidOperationException(message, reminderResumeFailure);
            }
        }
    }

    private void CancelCalendarBackgroundWorkForDatabaseMaintenance()
    {
        var deferredRefresh = Interlocked.Exchange(ref _deferredCalendarRefreshCts, null);
        deferredRefresh?.Cancel();
        deferredRefresh?.Dispose();
        CancelActiveCalendarRefresh();
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

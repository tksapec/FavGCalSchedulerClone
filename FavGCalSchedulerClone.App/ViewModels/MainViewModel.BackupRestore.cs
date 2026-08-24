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

    public async Task<BackupResult> BackupAllCalendarsAsync(string backupZipPath)
    {
        await _repository.InitializeAsync();
        var result = await _backupService.CreateBackupAsync(_repository.DatabasePath, backupZipPath);
        Status = $"バックアップを作成しました: {Path.GetFileName(result.BackupPath)}";
        return result;
    }

    public async Task<RestoreResult> RestoreAllCalendarsAsync(string backupZipPath)
    {
        if (Interlocked.CompareExchange(ref _databaseMaintenanceInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException("データベースのメンテナンス処理が既に実行中です。");
        }

        var reminderWasRunning = false;
        var reminderPaused = false;
        var repositoryMaintenanceStarted = false;
        try
        {
            if (Volatile.Read(ref _syncInProgress) != 0)
            {
                throw new InvalidOperationException("Google同期中はバックアップをリストアできません。同期完了後に再実行してください。");
            }

            if (_reminderService is not null)
            {
                reminderWasRunning = await _reminderService.PauseForMaintenanceAsync();
                reminderPaused = true;
            }

            await _repository.BeginMaintenanceAsync();
            repositoryMaintenanceStarted = true;
            var result = await _backupService.RestoreBackupAsync(backupZipPath, _repository.DatabasePath);

            // RestoreBackupAsync validates and migrates the extracted database before
            // replacing the live file, so normal repository access can resume here.
            _repository.EndMaintenance();
            repositoryMaintenanceStarted = false;

            await InitializeAsync();
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

                if (reminderPaused)
                {
                    await _reminderService!.ResumeAfterMaintenanceAsync(reminderWasRunning);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _databaseMaintenanceInProgress, 0);
            }
        }
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

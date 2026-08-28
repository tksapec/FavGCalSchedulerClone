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
    private readonly SemaphoreSlim _syncDataOperationGate = new(1, 1);

    public Task<SyncPreview> CreateSyncPreviewAsync() =>
        RunExclusiveSyncDataOperationAsync(CreateSyncPreviewCoreAsync);

    private async Task<SyncPreview> CreateSyncPreviewCoreAsync()
    {
        await SaveOAuthPathAsync();
        await EnsureGoogleIdentityIntegrityAsync();
        return await _syncService.PreviewAsync(CreateSettingsSnapshot());
    }

    public async Task<SyncResult?> SynchronizeManuallyAsync()
    {
        return await SynchronizeAsync(reportErrors: true, SyncInvocationKind.Manual);
    }

    public void SetManualSyncPreviewConfirmation(Func<SyncPreview, Task<bool>>? confirmManualSyncPreviewAsync)
    {
        _confirmManualSyncPreviewAsync = confirmManualSyncPreviewAsync;
    }

    public async Task<SyncResult?> SynchronizeManuallyWithPreviewAsync()
    {
        var settings = CreateSettingsSnapshot();
        if (settings.ShowSyncPreviewBeforeManualSync && _confirmManualSyncPreviewAsync is not null)
        {
            var preview = await CreateSyncPreviewAsync();
            if (!await _confirmManualSyncPreviewAsync(preview))
            {
                Status = "同期をキャンセルしました。";
                return null;
            }
        }

        return await SynchronizeManuallyAsync();
    }

    public async Task<SyncResult> SynchronizeDirtyOnlyAsync()
    {
        var dirtyIds = (await _repository.LoadDirtyEventsAsync())
            .Select(item => item.Id)
            .ToArray();
        if (dirtyIds.Length == 0)
        {
            var empty = SyncResult.Empty("未同期の予定はありません。");
            Status = empty.Message;
            await RefreshOperationalStatusAsync(null);
            return empty;
        }

        var result = await ResyncDirtyItemsAsync(dirtyIds);
        await RefreshOperationalStatusAsync(null);
        return result;
    }

    public async Task<SyncDiagnosticsSnapshot> LoadSyncDiagnosticsAsync()
    {
        await SaveOAuthPathAsync();
        return await _syncService.LoadDiagnosticsAsync(CreateSettingsSnapshot());
    }

    public Task<int> RefreshGoogleReminderMetadataAsync() =>
        RunExclusiveSyncDataOperationAsync(RefreshGoogleReminderMetadataCoreAsync);

    private async Task<int> RefreshGoogleReminderMetadataCoreAsync()
    {
        await SaveOAuthPathAsync();
        var now = DateTimeOffset.Now;
        var updated = await _syncService.RefreshReminderMetadataAsync(
            CreateSettingsSnapshot(),
            now.AddDays(-1),
            now.AddDays(30));
        Status = $"Google通知設定を再取得しました: {updated} 件";
        await RefreshCalendarAsync();
        await RefreshOperationalStatusAsync(null);
        return updated;
    }

    public Task<SyncResult> ResyncDirtyItemsAsync(IReadOnlyCollection<string> localIds) =>
        RunExclusiveSyncDataOperationAsync(() => ResyncDirtyItemsCoreAsync(localIds));

    private async Task<SyncResult> ResyncDirtyItemsCoreAsync(IReadOnlyCollection<string> localIds)
    {
        await SaveOAuthPathAsync();
        var targetIds = localIds.ToHashSet(StringComparer.Ordinal);
        await EnsureGoogleIdentityIntegrityAsync(targetIds);
        var result = await _syncService.SyncDirtyEventsAsync(CreateSettingsSnapshot(), targetIds);
        await RefreshCalendarAsync();
        Status = $"{result.Message} / 未同期残数 {(await _repository.LoadDirtyEventsAsync()).Count}";
        return result;
    }

    public async Task<SyncResult> ResyncFailedItemsAsync(IReadOnlyCollection<string> localIds)
    {
        var dirtyIds = (await _repository.LoadDirtyEventsAsync())
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var targets = localIds
            .Where(id => dirtyIds.Contains(id))
            .ToArray();
        if (targets.Length == 0)
        {
            var empty = SyncResult.Empty("再同期対象の失敗データは現在 dirty ではありません。");
            Status = empty.Message;
            return empty;
        }

        return await ResyncDirtyItemsAsync(targets);
    }

    public Task<int> MarkDirtyItemsSyncedAsync(IReadOnlyCollection<string> localIds) =>
        RunExclusiveSyncDataOperationAsync(() => MarkDirtyItemsSyncedCoreAsync(localIds));

    private async Task<int> MarkDirtyItemsSyncedCoreAsync(IReadOnlyCollection<string> localIds)
    {
        await CreateDiagnosticsBulkBackupAsync();
        var updated = await _repository.MarkSyncedByIdsAsync(localIds);
        await RefreshCalendarAsync();
        Status = $"選択した未同期データを同期済み扱いにしました: {updated} 件";
        return updated;
    }

    public Task<SyncResult> DiscardLocalChangesAsync(IReadOnlyCollection<string> localIds) =>
        RunExclusiveSyncDataOperationAsync(() => DiscardLocalChangesCoreAsync(localIds));

    private async Task<SyncResult> DiscardLocalChangesCoreAsync(IReadOnlyCollection<string> localIds)
    {
        await CreateDiagnosticsBulkBackupAsync();
        await SaveOAuthPathAsync();
        var result = await _syncService.DiscardLocalChangesAsync(CreateSettingsSnapshot(), localIds.ToHashSet(StringComparer.Ordinal));
        await RefreshCalendarAsync();
        Status = result.Message;
        return result;
    }

    public Task ClearSyncDiagnosticsAsync() =>
        RunExclusiveSyncDataOperationAsync(ClearSyncDiagnosticsCoreAsync);

    private async Task ClearSyncDiagnosticsCoreAsync()
    {
        await _syncService.ClearSyncDiagnosticsAsync();
    }

    public async Task RunAutomaticSyncIfDueAsync()
    {
        var settings = CreateSettingsSnapshot();
        if (settings.AutomaticSyncIntervalMinutes is not int interval
            || !CanSynchronize(settings)
            || settings.LastAutomaticSyncAt is { } lastSync
               && DateTimeOffset.Now - lastSync < TimeSpan.FromMinutes(interval))
        {
            return;
        }

        await SynchronizeAsync(reportErrors: false, SyncInvocationKind.Automatic);
    }

    private async Task SyncAfterLocalChangeAsync()
    {
        var settings = CreateSettingsSnapshot();
        if (settings.SyncAfterLocalChange && CanSynchronize(settings))
        {
            await SynchronizeAsync(reportErrors: false, SyncInvocationKind.LocalChange);
        }
    }

    private static bool CanSynchronize(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.OAuthClientJsonPath)
            && File.Exists(settings.OAuthClientJsonPath);
    }

    private async Task EnsureGoogleIdentityIntegrityAsync(IReadOnlySet<string>? localIds = null)
    {
        var broken = (await _repository.LoadDirtyEventsAsync())
            .Where(item => localIds is null || localIds.Contains(item.Id))
            .Where(item => string.IsNullOrWhiteSpace(item.GoogleEventId))
            .Where(item => item.LastSyncedAt is not null || !string.IsNullOrWhiteSpace(item.LastSyncedGoogleEtag))
            .ToArray();
        if (broken.Length == 0)
        {
            return;
        }

        var sample = broken[0];
        throw new InvalidOperationException(
            $"Google Event ID が失われた同期済み予定を {broken.Length} 件検出しました。"
            + $" 重複作成を防ぐため同期を中止しました。対象例: {sample.Title} ({sample.Start:g})。"
            + " Google同期診断またはバックアップから紐付けを確認してください。");
    }

    private async Task<SyncResult?> SynchronizeAsync(bool reportErrors, SyncInvocationKind invocationKind)
    {
        if (Volatile.Read(ref _databaseMaintenanceInProgress) != 0)
        {
            if (reportErrors)
            {
                Status = "データベースのリストア中はGoogle同期を開始できません。";
            }
            return null;
        }

        if (Interlocked.Exchange(ref _syncInProgress, 1) != 0)
        {
            RequestSyncRerun(invocationKind);
            return null;
        }

        // Close the race where restore starts after the first maintenance check but
        // before this invocation acquires the sync-in-progress flag.
        if (Volatile.Read(ref _databaseMaintenanceInProgress) != 0)
        {
            Interlocked.Exchange(ref _syncInProgress, 0);
            if (reportErrors)
            {
                Status = "データベースのリストア中はGoogle同期を開始できません。";
            }
            return null;
        }

        var syncDataGateEntered = false;
        try
        {
            await _syncDataOperationGate.WaitAsync();
            syncDataGateEntered = true;

            // A diagnostic operation can keep the gate busy long enough for restore
            // maintenance to begin after the earlier checks. Re-check after acquiring it.
            if (Volatile.Read(ref _databaseMaintenanceInProgress) != 0)
            {
                if (reportErrors)
                {
                    Status = "データベースのリストア中はGoogle同期を開始できません。";
                }
                return null;
            }

            await SaveOAuthPathAsync();
            var syncSettings = CreateSettingsSnapshot();
            if (!CanSynchronize(syncSettings))
            {
                if (reportErrors)
                {
                    Status = "先にOAuth client JSONを設定してください。";
                }

                return null;
            }

            await EnsureGoogleIdentityIntegrityAsync();
            Status = "Googleカレンダーと同期中...";
            IsSynchronizing = true;
            var result = await _syncService.SyncAsync(
                syncSettings,
                refreshReminderMetadataAfterSync: invocationKind == SyncInvocationKind.Manual);
            var finishedAt = DateTimeOffset.Now;
            SettingsPersistenceRequest settingsSnapshot;
            lock (_settingsStateLock)
            {
                if (invocationKind == SyncInvocationKind.Manual)
                {
                    _settings.LastManualSyncAt = finishedAt;
                }
                else if (invocationKind == SyncInvocationKind.Automatic)
                {
                    _settings.LastAutomaticSyncAt = finishedAt;
                }

                settingsSnapshot = CreateSettingsPersistenceRequestUnsafe();
            }

            await PersistSettingsAsync(settingsSnapshot);
            Status = "カレンダー再読み込み中...";
            var remaining = (await _repository.LoadDirtyEventsAsync()).Count;
            try
            {
                _eventColorPalette = await _syncService.RefreshEventColorPaletteAsync();
                await ReloadAvailableCalendarsAsync();
                await RefreshCalendarAsync();
            }
            catch (Exception reloadEx)
            {
                Debug.WriteLine(reloadEx);
                Status = $"同期は完了しましたが、カレンダー再読み込みに失敗しました: {reloadEx.Message} / 未同期残数 {remaining}";
                return result;
            }

            Status = $"同期が完了しました: {result.Message} / 未同期残数 {remaining}";
            if (result.Failed > 0 || result.Conflicts > 0 || remaining > 0)
            {
                Status += "。Google同期診断を確認してください。";
            }
            return result;
        }
        catch (Exception ex) when (reportErrors)
        {
            Debug.WriteLine(ex);
            await RecordFailedSyncSafelyAsync(ex);
            Status = "同期に失敗しました。Google同期診断を確認してください。";
            throw;
        }
        catch (Exception ex) when (!reportErrors)
        {
            Debug.WriteLine(ex);
            await RecordFailedSyncSafelyAsync(ex);
            Status = $"同期に失敗しました。Google同期診断を確認してください。未同期の変更は保持されています: {ex.Message}";
            return null;
        }
        finally
        {
            if (syncDataGateEntered)
            {
                _syncDataOperationGate.Release();
            }

            Interlocked.Exchange(ref _syncInProgress, 0);
            IsSynchronizing = false;
            var pendingInvocationKind = Interlocked.Exchange(ref _pendingSyncInvocationKind, NoPendingSyncInvocationKind);
            await RefreshOperationalStatusSafelyAsync();
            if (pendingInvocationKind != NoPendingSyncInvocationKind)
            {
                try
                {
                    await SynchronizeAsync(reportErrors: false, (SyncInvocationKind)pendingInvocationKind);
                }
                catch (Exception rerunEx)
                {
                    Debug.WriteLine(rerunEx);
                    _logger?.LogError(rerunEx, "Queued Google calendar sync rerun failed.");
                }
            }
        }
    }

    private void EnsureSyncDataOperationAllowed()
    {
        if (Volatile.Read(ref _databaseMaintenanceInProgress) != 0)
        {
            throw new InvalidOperationException("データベースのリストア中は同期データ操作を開始できません。");
        }
    }

    private async Task RunExclusiveSyncDataOperationAsync(Func<Task> operation)
    {
        EnsureSyncDataOperationAllowed();
        await _syncDataOperationGate.WaitAsync();
        try
        {
            EnsureSyncDataOperationAllowed();
            await operation();
        }
        finally
        {
            _syncDataOperationGate.Release();
        }
    }

    private async Task<T> RunExclusiveSyncDataOperationAsync<T>(Func<Task<T>> operation)
    {
        EnsureSyncDataOperationAllowed();
        await _syncDataOperationGate.WaitAsync();
        try
        {
            EnsureSyncDataOperationAllowed();
            return await operation();
        }
        finally
        {
            _syncDataOperationGate.Release();
        }
    }

    private async Task RecordFailedSyncSafelyAsync(Exception sourceException)
    {
        try
        {
            await _syncService.RecordFailedSyncAsync(
                sourceException.Message,
                CreateSettingsSnapshot().EnableSyncDiagnostics);
        }
        catch (Exception diagnosticsEx)
        {
            Debug.WriteLine(diagnosticsEx);
            _logger?.LogError(diagnosticsEx, "Failed to persist Google sync diagnostics.");
        }
    }

    private async Task RefreshOperationalStatusSafelyAsync()
    {
        try
        {
            await RefreshOperationalStatusAsync(null);
        }
        catch (Exception statusEx)
        {
            Debug.WriteLine(statusEx);
            _logger?.LogError(statusEx, "Failed to refresh operational status after Google sync.");
        }
    }

    private void RequestSyncRerun(SyncInvocationKind invocationKind)
    {
        var requested = (int)invocationKind;
        while (true)
        {
            var current = Volatile.Read(ref _pendingSyncInvocationKind);
            if (current >= requested)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _pendingSyncInvocationKind, requested, current) == current)
            {
                return;
            }
        }
    }
}

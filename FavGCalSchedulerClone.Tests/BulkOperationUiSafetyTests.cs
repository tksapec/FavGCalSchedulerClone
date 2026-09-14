using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class BulkOperationUiSafetyTests
{
    [Fact]
    public void FormatStatus_ReportsAffectedAndExcludedTargets()
    {
        var result = new BulkEventOperationResult(
            SelectedCount: 5,
            AffectedCount: 2,
            UnsupportedRecurrenceCount: 1,
            MissingCount: 1,
            TodoReminderSkippedCount: 1,
            UnchangedCount: 1);

        Assert.Equal(
            "一括編集: 選択 5件 / 変更 2件 / 繰り返し予定対象外 1件 / 未検出 1件 / ToDo通知対象外 1件 / 変更なし 1件",
            result.FormatStatus("一括編集", "変更"));
    }

    [Fact]
    public void FormatStatus_OmitsZeroCountDetails()
    {
        var result = new BulkEventOperationResult(SelectedCount: 2, AffectedCount: 2);

        Assert.Equal("一括削除: 選択 2件 / 削除 2件", result.FormatStatus("一括削除", "削除"));
    }

    [Fact]
    public void LegacyAffectedCountAssertion_AcceptsDetailedResult()
    {
        var result = new BulkEventOperationResult(SelectedCount: 2, AffectedCount: 2);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task AsyncOperationGate_RejectsSecondOperationUntilFirstCompletes()
    {
        var gate = new AsyncOperationGate();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRan = false;

        var first = gate.TryRunAsync(async () =>
        {
            firstStarted.SetResult();
            await releaseFirst.Task;
        });
        await firstStarted.Task;

        var secondAccepted = await gate.TryRunAsync(() =>
        {
            secondRan = true;
            return Task.CompletedTask;
        });

        Assert.False(secondAccepted);
        Assert.False(secondRan);
        Assert.True(gate.IsRunning);

        releaseFirst.SetResult();
        Assert.True(await first);
        Assert.False(gate.IsRunning);

        Assert.True(await gate.TryRunAsync(() => Task.CompletedTask));
    }
}

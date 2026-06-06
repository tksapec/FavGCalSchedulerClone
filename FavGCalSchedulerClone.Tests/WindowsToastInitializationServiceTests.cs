using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class WindowsToastInitializationServiceTests
{
    [Fact]
    public void ValidateShortcut_ReturnsValidForMatchingTargetAndAumid()
    {
        var result = WindowsToastInitializationService.ValidateShortcut(
            new ShortcutInspectionResult(true, "C:\\app\\FavGCalSchedulerClone.exe", WindowsToastInitializationService.AppUserModelId, null),
            "C:\\app\\FavGCalSchedulerClone.exe",
            WindowsToastInitializationService.AppUserModelId);

        Assert.True(result.IsValid);
        Assert.Equal(WindowsToastFailureCategory.None, result.FailureCategory);
    }

    [Fact]
    public void ValidateShortcut_DetectsTargetMismatch()
    {
        var result = WindowsToastInitializationService.ValidateShortcut(
            new ShortcutInspectionResult(true, "C:\\old\\FavGCalSchedulerClone.exe", WindowsToastInitializationService.AppUserModelId, null),
            "C:\\app\\FavGCalSchedulerClone.exe",
            WindowsToastInitializationService.AppUserModelId);

        Assert.False(result.IsValid);
        Assert.Equal(WindowsToastFailureCategory.ShortcutTargetMismatch, result.FailureCategory);
    }

    [Fact]
    public void ValidateShortcut_DetectsAumidMismatch()
    {
        var result = WindowsToastInitializationService.ValidateShortcut(
            new ShortcutInspectionResult(true, "C:\\app\\FavGCalSchedulerClone.exe", "other.app", null),
            "C:\\app\\FavGCalSchedulerClone.exe",
            WindowsToastInitializationService.AppUserModelId);

        Assert.False(result.IsValid);
        Assert.Equal(WindowsToastFailureCategory.AumidMismatch, result.FailureCategory);
    }

    [Fact]
    public void ValidateShortcut_DetectsReadFailure()
    {
        var result = WindowsToastInitializationService.ValidateShortcut(
            new ShortcutInspectionResult(true, null, null, "broken shortcut"),
            "C:\\app\\FavGCalSchedulerClone.exe",
            WindowsToastInitializationService.AppUserModelId);

        Assert.False(result.IsValid);
        Assert.Equal(WindowsToastFailureCategory.ShortcutReadFailed, result.FailureCategory);
    }

    [Fact]
    public void ToDisplayText_UsesJapaneseElevatedProcessStatus()
    {
        var status = WindowsToastStatus.Failed("elevated", WindowsToastFailureCategory.ElevatedProcess);

        Assert.Contains("管理者権限実行中のため使用不可", status.ToDisplayText());
    }

    [Fact]
    public void ToDisplayText_UsesJapaneseShortcutCreateFailureStatus()
    {
        var status = WindowsToastStatus.Failed("create failed", WindowsToastFailureCategory.ShortcutCreateFailed);

        Assert.Contains("ショートカット作成失敗", status.ToDisplayText());
    }
}

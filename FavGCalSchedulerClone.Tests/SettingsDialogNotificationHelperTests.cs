using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class SettingsDialogNotificationHelperTests
{
    [Fact]
    public void ShouldAskToastDisplayConfirmation_IsFalseForCustomPopup()
    {
        var settings = CreateSettings();
        var result = new ReminderTestNotificationResult(
            Succeeded: true,
            DeliveryMethod: "CustomPopup",
            UsedMessageBoxFallback: false,
            MessageBoxRole: MessageBoxNotificationRole.None,
            ToastVerified: false,
            ToastStatus: null,
            SoundStatus: ReminderSoundStatus.NotConfigured,
            SoundError: null,
            ErrorMessage: null);

        Assert.False(SettingsDialogNotificationHelper.ShouldAskToastDisplayConfirmation(settings, result));
    }

    [Fact]
    public void CreateTestResultMessage_DescribesCustomPopup()
    {
        var settings = CreateSettings();
        var result = new ReminderTestNotificationResult(
            Succeeded: true,
            DeliveryMethod: "Sound + CustomPopup",
            UsedMessageBoxFallback: false,
            MessageBoxRole: MessageBoxNotificationRole.None,
            ToastVerified: false,
            ToastStatus: null,
            SoundStatus: ReminderSoundStatus.Played,
            SoundError: null,
            ErrorMessage: null);

        var message = SettingsDialogNotificationHelper.CreateTestResultMessage(settings, result);

        Assert.Contains("右下ポップアップ通知を表示しました", message);
        Assert.Contains("通知方式", message);
        Assert.Contains("再生成功", message);
    }

    [Fact]
    public void FormatToastStatus_DescribesPopupNotification()
    {
        var status = SettingsDialogNotificationHelper.FormatToastStatus(
            "Ready",
            WindowsToastInitializationService.AppUserModelId,
            "C:\\app\\FavGCalSchedulerClone.exe",
            DateTimeOffset.Now,
            WindowsToastInitializationService.AppUserModelId,
            "C:\\app\\FavGCalSchedulerClone.exe",
            saved: true);

        Assert.Contains("右下ポップアップ通知", status);
        Assert.Contains("AUMID登録は不要", status);
    }

    private static ReminderTestSettings CreateSettings()
    {
        return new ReminderTestSettings(
            UseWindowsToastNotifications: true,
            ShowMessageBoxAfterToastNotification: false,
            EnableReminderSound: false,
            ReminderSoundFilePath: null,
            ReminderSoundVolume: 50);
    }
}

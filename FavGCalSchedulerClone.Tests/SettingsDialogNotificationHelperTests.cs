using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class SettingsDialogNotificationHelperTests
{
    [Fact]
    public void ShouldAskToastDisplayConfirmation_WhenToastWasSentWithMessageBoxFallbackEnabled()
    {
        var settings = new ReminderTestSettings(
            UseWindowsToastNotifications: true,
            ShowMessageBoxAfterToastNotification: true,
            EnableReminderSound: false,
            ReminderSoundFilePath: null,
            ReminderSoundVolume: 50);
        var result = new ReminderTestNotificationResult(
            Succeeded: true,
            DeliveryMethod: "WindowsToast + MessageBox",
            UsedMessageBoxFallback: true,
            ToastVerified: true,
            ToastStatus: "トースト通知初期化済み",
            ErrorMessage: null);

        Assert.True(SettingsDialogNotificationHelper.ShouldAskToastDisplayConfirmation(settings, result));
    }

    [Fact]
    public void ShouldAskToastDisplayConfirmation_IsFalseForMessageBoxOnly()
    {
        var settings = new ReminderTestSettings(
            UseWindowsToastNotifications: false,
            ShowMessageBoxAfterToastNotification: false,
            EnableReminderSound: false,
            ReminderSoundFilePath: null,
            ReminderSoundVolume: 50);
        var result = new ReminderTestNotificationResult(
            Succeeded: true,
            DeliveryMethod: "MessageBox",
            UsedMessageBoxFallback: true,
            ToastVerified: false,
            ToastStatus: null,
            ErrorMessage: null);

        Assert.False(SettingsDialogNotificationHelper.ShouldAskToastDisplayConfirmation(settings, result));
    }

    [Fact]
    public void CreateTestResultMessage_DescribesToastFallback()
    {
        var settings = new ReminderTestSettings(
            UseWindowsToastNotifications: true,
            ShowMessageBoxAfterToastNotification: true,
            EnableReminderSound: false,
            ReminderSoundFilePath: null,
            ReminderSoundVolume: 50);
        var result = new ReminderTestNotificationResult(
            Succeeded: true,
            DeliveryMethod: "WindowsToast failed -> MessageBox",
            UsedMessageBoxFallback: true,
            ToastVerified: false,
            ToastStatus: "トースト通知未確認",
            ErrorMessage: null);

        var message = SettingsDialogNotificationHelper.CreateTestResultMessage(settings, result);

        Assert.Contains("MessageBox通知にフォールバック", message);
        Assert.Contains("通知方式", message);
        Assert.Contains("Toast status", message);
    }

    [Fact]
    public void FormatToastStatus_ShowsVerifiedAndStandaloneRequirement()
    {
        var verifiedAt = new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

        var status = SettingsDialogNotificationHelper.FormatToastStatus(
            "トースト通知初期化済み",
            WindowsToastInitializationService.AppUserModelId,
            "C:\\app\\FavGCalSchedulerClone.exe",
            verifiedAt,
            WindowsToastInitializationService.AppUserModelId,
            "C:\\app\\FavGCalSchedulerClone.exe");

        Assert.Contains("トースト通知確認済み", status);
        Assert.Contains("トースト通知単独で使用するには", status);
    }
}

using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.Views.Dialogs;

namespace FavGCalSchedulerClone.Tests;

public sealed class SettingsDialogNotificationHelperTests
{
    [Fact]
    public void NotificationMethodText_UsesOnlyCustomPopupDescription()
    {
        Assert.Equal("通知方式: アプリ内右下ポップアップ", SettingsDialogNotificationHelper.NotificationMethodText);
        Assert.DoesNotContain("Windows", SettingsDialogNotificationHelper.NotificationMethodText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AUMID", SettingsDialogNotificationHelper.NotificationMethodText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MessageBox", SettingsDialogNotificationHelper.NotificationMethodText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateTestResultMessage_DescribesCustomPopup()
    {
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

        var message = SettingsDialogNotificationHelper.CreateTestResultMessage(result);

        Assert.Contains("右下ポップアップ通知を表示しました", message);
        Assert.Contains("通知方式", message);
        Assert.Contains("再生成功", message);
    }

}

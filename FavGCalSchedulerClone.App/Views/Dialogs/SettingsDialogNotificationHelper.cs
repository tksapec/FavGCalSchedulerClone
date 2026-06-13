using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class SettingsDialogNotificationHelper
{
    public const string NotificationMethodText = "通知方式: アプリ内右下ポップアップ";

    public static string CreateTestResultMessage(ReminderTestNotificationResult result)
    {
        var detail = $"\n通知方式: {result.DeliveryMethod ?? "unknown"}\n通知音: {FormatSound(result.SoundStatus, result.SoundError)}";
        if (WasCustomPopupSent(result))
        {
            return $"右下ポップアップ通知を表示しました。{detail}";
        }

        return result.Succeeded
            ? $"通知を表示しました。{detail}"
            : $"通知テストに失敗しました。{detail}\n失敗理由: {result.ErrorMessage}";
    }

    private static bool WasCustomPopupSent(ReminderTestNotificationResult result)
    {
        return result.DeliveryMethod?.Contains("CustomPopup", StringComparison.OrdinalIgnoreCase) == true
            && !result.DeliveryMethod.Contains("failed ->", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSound(ReminderSoundStatus status, string? error)
    {
        return status switch
        {
            ReminderSoundStatus.Played => "再生成功",
            ReminderSoundStatus.MissingFile => string.IsNullOrWhiteSpace(error) ? "ファイルなし" : $"ファイルなし ({error})",
            ReminderSoundStatus.Failed => string.IsNullOrWhiteSpace(error) ? "再生失敗" : $"再生失敗 ({error})",
            _ => "なし"
        };
    }
}

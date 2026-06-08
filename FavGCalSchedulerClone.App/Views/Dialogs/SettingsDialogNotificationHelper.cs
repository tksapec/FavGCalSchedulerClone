using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.App.Views.Dialogs;

internal static class SettingsDialogNotificationHelper
{
    public static string FormatToastStatus(
        string initializationStatusText,
        string toastAumid,
        string toastExecutablePath,
        DateTimeOffset? verifiedAt,
        string? verifiedAumid,
        string? verifiedExecutablePath,
        bool saved = false)
    {
        _ = initializationStatusText;
        _ = toastAumid;
        _ = toastExecutablePath;
        _ = verifiedAt;
        _ = verifiedAumid;
        _ = verifiedExecutablePath;
        _ = saved;
        return "右下ポップアップ通知を使用します。Windowsトースト通知のショートカット/AUMID登録は不要です。";
    }

    public static string CreateTestResultMessage(ReminderTestSettings settings, ReminderTestNotificationResult result)
    {
        _ = settings;
        var detail = $"\n通知方式: {result.DeliveryMethod ?? "unknown"}\nMessageBox: {FormatMessageBoxRole(result.MessageBoxRole)}\n通知音: {FormatSound(result.SoundStatus, result.SoundError)}";
        if (WasCustomPopupSent(result))
        {
            return $"右下ポップアップ通知を表示しました。{detail}";
        }

        return result.Succeeded
            ? $"通知を表示しました。{detail}"
            : $"通知テストに失敗しました。{detail}\n失敗理由: {result.ErrorMessage}";
    }

    public static bool ShouldAskToastDisplayConfirmation(ReminderTestSettings settings, ReminderTestNotificationResult result)
    {
        _ = settings;
        _ = result;
        return false;
    }

    public static bool IsCurrentToastVerification(AppSettings settings, string toastAumid, string toastExecutablePath)
    {
        _ = settings;
        _ = toastAumid;
        _ = toastExecutablePath;
        return true;
    }

    private static bool WasCustomPopupSent(ReminderTestNotificationResult result)
    {
        return result.DeliveryMethod?.Contains("CustomPopup", StringComparison.OrdinalIgnoreCase) == true
            && !result.DeliveryMethod.Contains("failed ->", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMessageBoxRole(MessageBoxNotificationRole role)
    {
        return role switch
        {
            MessageBoxNotificationRole.Primary => "MessageBox通知",
            MessageBoxNotificationRole.AfterToast => "MessageBox併用",
            MessageBoxNotificationRole.Fallback => "MessageBoxフォールバック",
            _ => "なし"
        };
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

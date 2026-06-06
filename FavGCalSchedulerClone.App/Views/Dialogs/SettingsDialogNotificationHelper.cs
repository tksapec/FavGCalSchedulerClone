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
        string? verifiedExecutablePath)
    {
        var verification = verifiedAt is null
            ? "トースト通知未確認"
            : !string.Equals(verifiedAumid, toastAumid, StringComparison.Ordinal)
                ? "AUMID不一致"
                : !string.Equals(verifiedExecutablePath, toastExecutablePath, StringComparison.OrdinalIgnoreCase)
                    ? "実行ファイル不一致"
                    : $"トースト通知確認済み ({verifiedAt:yyyy/MM/dd HH:mm})";

        return $"{initializationStatusText}\n{verification}\nAUMID: {toastAumid}\nEXE: {toastExecutablePath}\nトースト通知単独で使用するには、通知テストで実表示確認が必要です。";
    }

    public static string CreateTestResultMessage(ReminderTestSettings settings, ReminderTestNotificationResult result)
    {
        var detail = $"\n通知方式: {result.DeliveryMethod ?? "unknown"}\nMessageBoxフォールバック: {(result.UsedMessageBoxFallback ? "あり" : "なし")}\nToast status: {result.ToastStatus ?? "なし"}";
        if (!settings.UseWindowsToastNotifications)
        {
            return result.Succeeded
                ? $"MessageBox通知を表示しました。{detail}"
                : $"MessageBox通知に失敗しました。{detail}\n失敗理由: {result.ErrorMessage}";
        }

        if (WasToastSent(result))
        {
            return $"Windowsトースト通知を送信しました。実際に表示されたか確認してください。{detail}";
        }

        if (result.Succeeded && result.UsedMessageBoxFallback)
        {
            return $"Windowsトースト通知は未確認または使用不可のため、MessageBox通知にフォールバックしました。{detail}";
        }

        return $"通知テストに失敗しました。{detail}\n失敗理由: {result.ErrorMessage}";
    }

    public static bool ShouldAskToastDisplayConfirmation(ReminderTestSettings settings, ReminderTestNotificationResult result)
    {
        return settings.UseWindowsToastNotifications && result.Succeeded && WasToastSent(result);
    }

    public static bool IsCurrentToastVerification(AppSettings settings, string toastAumid, string toastExecutablePath)
    {
        return settings.ToastVerifiedAt is not null
            && string.Equals(settings.ToastVerifiedAumid, toastAumid, StringComparison.Ordinal)
            && string.Equals(settings.ToastVerifiedExecutablePath, toastExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool WasToastSent(ReminderTestNotificationResult result)
    {
        return result.DeliveryMethod?.Contains("WindowsToast", StringComparison.OrdinalIgnoreCase) == true
            && !result.DeliveryMethod.Contains("failed ->", StringComparison.OrdinalIgnoreCase);
    }
}

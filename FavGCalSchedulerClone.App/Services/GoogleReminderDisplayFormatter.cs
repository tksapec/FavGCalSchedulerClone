using FavGCalSchedulerClone.App.Models;

namespace FavGCalSchedulerClone.App.Services;

public static class GoogleReminderDisplayFormatter
{
    public static string FormatEmailReminderText(GoogleReminderMetadata? metadata)
    {
        if (metadata is null)
        {
            return "Googleメール通知: なし";
        }

        if (metadata.UseDefault == true)
        {
            if (metadata.DefaultEmailMinutes.Count > 0)
            {
                return $"Google既定: email {FormatMinutes(metadata.DefaultEmailMinutes)}";
            }

            return string.Equals(metadata.Source, "default-unavailable", StringComparison.Ordinal)
                ? "Google既定: 未取得"
                : "Googleメール通知: なし";
        }

        return metadata.EmailMinutes.Count > 0
            ? $"Googleメール通知: {FormatMinutes(metadata.EmailMinutes)}"
            : "Googleメール通知: なし";
    }

    private static string FormatMinutes(IEnumerable<int> minutes)
    {
        return string.Join(
            ", ",
            minutes
                .Where(value => value >= 0)
                .Distinct()
                .Order()
                .Select(value => value == 0 ? "開始時刻" : $"{value}分前"));
    }
}

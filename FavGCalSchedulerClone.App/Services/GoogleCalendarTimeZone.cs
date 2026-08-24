namespace FavGCalSchedulerClone.App.Services;

internal static class GoogleCalendarTimeZone
{
    public const string TokyoIanaId = "Asia/Tokyo";

    public static string LocalIanaId => ToIanaId(TimeZoneInfo.Local);

    public static string ToIanaId(TimeZoneInfo timeZone)
    {
        if (LooksLikeIanaId(timeZone.Id))
        {
            return timeZone.Id;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZone.Id, out var ianaId)
            && !string.IsNullOrWhiteSpace(ianaId))
        {
            return ianaId;
        }

        if (string.Equals(timeZone.Id, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "Etc/UTC";
        }

        // The application is primarily used in Japan. Keep the historical fallback only
        // for an OS time-zone identifier that .NET itself cannot map.
        return TokyoIanaId;
    }

    private static bool LooksLikeIanaId(string id)
    {
        return id.Contains('/', StringComparison.Ordinal)
            && !id.Contains(' ', StringComparison.Ordinal);
    }
}

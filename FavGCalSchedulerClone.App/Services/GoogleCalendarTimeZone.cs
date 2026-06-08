namespace FavGCalSchedulerClone.App.Services;

internal static class GoogleCalendarTimeZone
{
    public const string TokyoIanaId = "Asia/Tokyo";

    public static string LocalIanaId => ToIanaId(TimeZoneInfo.Local);

    public static string ToIanaId(TimeZoneInfo timeZone)
    {
        if (string.Equals(timeZone.Id, "Tokyo Standard Time", StringComparison.OrdinalIgnoreCase))
        {
            return TokyoIanaId;
        }

        return LooksLikeIanaId(timeZone.Id) ? timeZone.Id : TokyoIanaId;
    }

    private static bool LooksLikeIanaId(string id)
    {
        return id.Contains('/', StringComparison.Ordinal)
            && !id.Contains(' ', StringComparison.Ordinal);
    }
}

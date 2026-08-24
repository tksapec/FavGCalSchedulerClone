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

    public static bool TryCreateDateTimeOffset(
        DateTime wallClock,
        string? timeZoneId,
        TimeSpan? preferredOffset,
        out DateTimeOffset result)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (string.IsNullOrWhiteSpace(timeZoneId) && preferredOffset is { } explicitOffset)
        {
            result = new DateTimeOffset(unspecified, explicitOffset);
            return true;
        }

        if (!TryResolveTimeZone(timeZoneId, out var timeZone) || timeZone.IsInvalidTime(unspecified))
        {
            result = default;
            return false;
        }

        var offset = timeZone.GetUtcOffset(unspecified);
        if (timeZone.IsAmbiguousTime(unspecified) && preferredOffset is { } preferred)
        {
            var offsets = timeZone.GetAmbiguousTimeOffsets(unspecified);
            if (offsets.Contains(preferred))
            {
                offset = preferred;
            }
        }

        result = new DateTimeOffset(unspecified, offset);
        return true;
    }

    private static bool TryResolveTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = TimeZoneInfo.Local;
            return true;
        }

        if (TryFindTimeZone(timeZoneId, out timeZone))
        {
            return true;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId)
            && TryFindTimeZone(windowsId, out timeZone))
        {
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId)
            && TryFindTimeZone(ianaId, out timeZone))
        {
            return true;
        }

        timeZone = TimeZoneInfo.Local;
        return false;
    }

    private static bool TryFindTimeZone(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        timeZone = TimeZoneInfo.Local;
        return false;
    }

    private static bool LooksLikeIanaId(string id)
    {
        return id.Contains('/', StringComparison.Ordinal)
            && !id.Contains(' ', StringComparison.Ordinal);
    }
}

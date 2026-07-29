using System.Globalization;
using System.Net.Http;
using System.Text;

namespace FavGCalSchedulerClone.App.Services;

public static class JapaneseHolidayService
{
    public const string OfficialCsvUrl = "https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv";
    private static readonly Encoding OfficialCsvEncoding = CreateOfficialCsvEncoding();
    private static IReadOnlyDictionary<DateOnly, string> _holidays = new Dictionary<DateOnly, string>();

    public static event EventHandler? HolidaysChanged;

    public static string? GetHolidayName(DateOnly date) => _holidays.TryGetValue(date, out var name) ? name : null;

    public static void LoadFromFile(string path, IAppLogger? logger)
    {
        try
        {
            _holidays = File.Exists(path)
                ? ParseCsv(File.ReadAllText(path, OfficialCsvEncoding))
                : new Dictionary<DateOnly, string>();
            HolidaysChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _holidays = new Dictionary<DateOnly, string>();
            logger?.LogError(ex, "Failed to load Japanese holiday data.");
        }
    }

    public static HolidayLoadResult LoadWithFallback(string localPath, string bundledPath, IAppLogger? logger)
    {
        var errors = new List<string>();
        foreach (var candidate in new[] { (localPath, HolidayDataSource.Local), (bundledPath, HolidayDataSource.Bundled) })
        {
            try
            {
                if (!File.Exists(candidate.Item1))
                {
                    errors.Add($"{candidate.Item2}: file not found");
                    continue;
                }
                var parsed = ParseCsv(File.ReadAllText(candidate.Item1, OfficialCsvEncoding));
                if (parsed.Count == 0)
                {
                    errors.Add($"{candidate.Item2}: no valid rows");
                    continue;
                }
                _holidays = parsed;
                HolidaysChanged?.Invoke(null, EventArgs.Empty);
                logger?.LogInfo($"Loaded {parsed.Count} Japanese holidays from {candidate.Item2} data.");
                return new HolidayLoadResult(candidate.Item2, parsed.Count, errors.Count == 0 ? null : string.Join("; ", errors));
            }
            catch (Exception ex)
            {
                errors.Add($"{candidate.Item2}: {ex.Message}");
                logger?.LogError(ex, $"Failed to load {candidate.Item2} Japanese holiday data.");
            }
        }
        _holidays = new Dictionary<DateOnly, string>();
        HolidaysChanged?.Invoke(null, EventArgs.Empty);
        return new HolidayLoadResult(HolidayDataSource.Empty, 0, string.Join("; ", errors));
    }

    public static async Task<bool> UpdateFromOfficialSourceAsync(HttpClient client, string destinationPath, IAppLogger? logger, CancellationToken cancellationToken = default)
    {
        var temporaryPath = destinationPath + ".tmp";
        try
        {
            var bytes = await client.GetByteArrayAsync(OfficialCsvUrl, cancellationToken);
            var csv = OfficialCsvEncoding.GetString(bytes);
            var updated = ParseCsv(csv);
            if (updated.Count == 0)
            {
                throw new InvalidDataException("Official holiday CSV contained no valid rows.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(temporaryPath, csv, OfficialCsvEncoding, cancellationToken);
            File.Move(temporaryPath, destinationPath, true);
            _holidays = updated;
            HolidaysChanged?.Invoke(null, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            try { File.Delete(temporaryPath); } catch (Exception cleanupEx) { logger?.LogError(cleanupEx, "Failed to remove holiday temporary file."); }
            logger?.LogError(ex, "Failed to update Japanese holiday data.");
            return false;
        }
    }

    public static IReadOnlyDictionary<DateOnly, string> ParseCsv(string csv)
    {
        var holidays = new Dictionary<DateOnly, string>();
        using var reader = new StringReader(csv);
        while (reader.ReadLine() is { } line)
        {
            var columns = line.Split(',', 2, StringSplitOptions.TrimEntries);
            if (columns.Length != 2
                || !DateOnly.TryParse(columns[0].Trim('"'), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var name = columns[1].Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(name))
            {
                holidays[date] = name;
            }
        }

        return holidays;
    }

    private static Encoding CreateOfficialCsvEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}

public enum HolidayDataSource { Local, Bundled, Empty }
public sealed record HolidayLoadResult(HolidayDataSource Source, int Count, string? ErrorMessage);

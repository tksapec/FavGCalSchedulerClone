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

    public static async Task<bool> UpdateFromOfficialSourceAsync(HttpClient client, string destinationPath, IAppLogger? logger, CancellationToken cancellationToken = default)
    {
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
            var temporaryPath = destinationPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, csv, OfficialCsvEncoding, cancellationToken);
            File.Move(temporaryPath, destinationPath, true);
            _holidays = updated;
            HolidaysChanged?.Invoke(null, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
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

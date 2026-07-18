using System.Globalization;
using System.Net.Http;
using System.Text;

namespace FavGCalSchedulerClone.App.Services;

public static class JapaneseHolidayService
{
    public const string OfficialCsvUrl = "https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv";
    private const string BundledHolidayCsv = "2026/1/1,元日\n2026/1/12,成人の日\n2026/2/11,建国記念の日\n2026/2/23,天皇誕生日\n2026/3/20,春分の日\n2026/4/29,昭和の日\n2026/5/3,憲法記念日\n2026/5/4,みどりの日\n2026/5/5,こどもの日\n2026/5/6,休日\n2026/7/20,海の日\n2026/8/11,山の日\n2026/9/21,敬老の日\n2026/9/22,休日\n2026/9/23,秋分の日\n2026/10/12,スポーツの日\n2026/11/3,文化の日\n2026/11/23,勤労感謝の日\n2027/1/1,元日\n2027/1/11,成人の日\n2027/2/11,建国記念の日\n2027/2/23,天皇誕生日\n2027/3/21,春分の日\n2027/3/22,休日\n2027/4/29,昭和の日\n2027/5/3,憲法記念日\n2027/5/4,みどりの日\n2027/5/5,こどもの日\n2027/7/19,海の日\n2027/8/11,山の日\n2027/9/20,敬老の日\n2027/9/23,秋分の日\n2027/10/11,スポーツの日\n2027/11/3,文化の日\n2027/11/23,勤労感謝の日\n";
    private static readonly Encoding OfficialCsvEncoding = CreateOfficialCsvEncoding();
    private static IReadOnlyDictionary<DateOnly, string> _holidays = ParseCsv(BundledHolidayCsv);

    public static event EventHandler? HolidaysChanged;

    public static string? GetHolidayName(DateOnly date) => _holidays.TryGetValue(date, out var name) ? name : null;

    public static void LoadFromFile(string path, IAppLogger? logger)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = ParseCsv(File.ReadAllText(path, OfficialCsvEncoding));
                if (loaded.Count > 0)
                {
                    _holidays = loaded;
                }
            }

            HolidaysChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load Japanese holiday data.");
        }
    }

    public static async Task<bool> UpdateFromOfficialSourceAsync(HttpClient client, string destinationPath, IAppLogger? logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.GetAsync(OfficialCsvUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var csv = OfficialCsvEncoding.GetString(bytes);
            var updated = ParseCsv(csv);
            if (updated.Count == 0)
            {
                throw new InvalidDataException("Official holiday CSV contained no valid rows.");
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("The holiday destination path must have a directory.");
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = destinationPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, csv, OfficialCsvEncoding, cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
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

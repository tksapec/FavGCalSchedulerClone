using System.Globalization;
using System.Text;

namespace FavGCalSchedulerClone.App.Services;

public sealed class FileAppLogger : IAppLogger
{
    private const int RetentionDays = 30;
    private readonly string _logDirectory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _syncRoot = new();

    public FileAppLogger()
        : this(null, null)
    {
    }

    public FileAppLogger(string? logDirectory = null, Func<DateTimeOffset>? clock = null)
    {
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FavGCalSchedulerClone",
                "logs")
            : logDirectory;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public void LogError(Exception exception, string context)
    {
        var now = _clock();
        var builder = new StringBuilder()
            .Append('[')
            .Append(now.ToString("O", CultureInfo.InvariantCulture))
            .Append("] ERROR ")
            .AppendLine(context)
            .Append(exception.GetType().FullName)
            .Append(": ")
            .AppendLine(exception.Message)
            .AppendLine(exception.StackTrace);

        Write(now, builder.ToString());
    }

    public void LogInfo(string message)
    {
        var now = _clock();
        Write(
            now,
            $"[{now.ToString("O", CultureInfo.InvariantCulture)}] INFO {message}{Environment.NewLine}");
    }

    private void Write(DateTimeOffset now, string text)
    {
        try
        {
            lock (_syncRoot)
            {
                Directory.CreateDirectory(_logDirectory);
                DeleteExpiredLogs(now);
                File.AppendAllText(GetLogPath(now), text, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    private string GetLogPath(DateTimeOffset now)
    {
        return Path.Combine(_logDirectory, $"app-{now:yyyy-MM-dd}.log");
    }

    private void DeleteExpiredLogs(DateTimeOffset now)
    {
        var cutoff = now.Date.AddDays(-RetentionDays);
        foreach (var path in Directory.EnumerateFiles(_logDirectory, "app-*.log"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.Length != "app-yyyy-MM-dd".Length
                || !DateTime.TryParseExact(
                    fileName["app-".Length..],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                || date >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort rotation.
            }
        }
    }
}

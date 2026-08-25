using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarRepositoryMetadataResilienceTests
{
    [Fact]
    public async Task LoadEventsAsync_IgnoresCorruptedGoogleReminderMetadataJson()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var start = new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(9));
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = "metadata-corrupt",
                CalendarId = GoogleCalendarDefaults.PrimaryCalendarId,
                Title = "Keep this event",
                Start = start,
                End = start.AddHours(1),
                GoogleReminderMetadata = new GoogleReminderMetadata()
            });

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE events SET google_reminder_metadata_json = '{not-json' WHERE id = 'metadata-corrupt'";
                await command.ExecuteNonQueryAsync();
            }

            var events = await repository.LoadEventsAsync(start.AddDays(-1), start.AddDays(1));

            var loaded = Assert.Single(events, item => item.Id == "metadata-corrupt");
            Assert.Equal("Keep this event", loaded.Title);
            Assert.Null(loaded.GoogleReminderMetadata);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

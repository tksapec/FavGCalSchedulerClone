using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class ReliabilityHardeningBehaviorTests
{
    [Fact]
    public async Task AtomicWriter_RollsBackAllWritesWhenLaterWriteFails()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var trigger = connection.CreateCommand();
                trigger.CommandText = """
                    CREATE TRIGGER fail_second_event
                    BEFORE INSERT ON events
                    WHEN NEW.id = 'fail-second'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional test failure');
                    END;
                    """;
                await trigger.ExecuteNonQueryAsync();
            }

            var first = CreateEvent("first");
            var second = CreateEvent("fail-second");

            await Assert.ThrowsAsync<SqliteException>(() =>
                CalendarRepositoryAtomicWriter.SaveEventsAsync(repository, [first, second]));

            Assert.Null(await repository.FindEventByIdAsync("first"));
            Assert.Null(await repository.FindEventByIdAsync("fail-second"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteIfExists(dbPath);
            DeleteIfExists(dbPath + "-wal");
            DeleteIfExists(dbPath + "-shm");
        }
    }

    [Fact]
    public async Task Restore_MovesExistingWalAndShmBesideRollbackDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"FavGCalSchedulerClone-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "source.db");
        var targetPath = Path.Combine(directory, "target.db");
        var backupPath = Path.Combine(directory, "backup.zip");
        try
        {
            var source = new CalendarRepository(sourcePath);
            await source.InitializeAsync();
            await source.SaveEventAsync(CreateEvent("source"));
            var target = new CalendarRepository(targetPath);
            await target.InitializeAsync();
            await target.SaveEventAsync(CreateEvent("target"));

            var service = new BackupService();
            await service.CreateBackupAsync(sourcePath, backupPath);
            SqliteConnection.ClearAllPools();
            await File.WriteAllTextAsync(targetPath + "-wal", "stale-wal");
            await File.WriteAllTextAsync(targetPath + "-shm", "stale-shm");

            var result = await service.RestoreBackupAsync(backupPath, targetPath);

            Assert.NotNull(result.PreviousDatabaseBackupPath);
            Assert.Equal("stale-wal", await File.ReadAllTextAsync(result.PreviousDatabaseBackupPath! + "-wal"));
            Assert.Equal("stale-shm", await File.ReadAllTextAsync(result.PreviousDatabaseBackupPath! + "-shm"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A2)")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A2)")]
    public void CsvCellSanitizer_NeutralizesAndRestoresFormulaPrefixes(string value)
    {
        var neutralized = CsvCellSanitizer.NeutralizeForSpreadsheet(value);

        Assert.StartsWith("'", neutralized, StringComparison.Ordinal);
        Assert.Equal(value, CsvCellSanitizer.RestoreNeutralizedValue(neutralized));
    }

    private static CalendarEvent CreateEvent(string id) => new()
    {
        Id = id,
        CalendarId = "primary",
        Title = id,
        Start = new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.FromHours(9)),
        End = new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.FromHours(9)),
        IsDirty = true
    };

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

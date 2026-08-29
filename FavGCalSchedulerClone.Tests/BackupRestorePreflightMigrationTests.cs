using System.IO.Compression;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class BackupRestorePreflightMigrationTests
{
    [Fact]
    public async Task RestoreBackupAsync_MigratesCompatibleOlderSchemaBeforeReplacingCurrentDatabase()
    {
        var directory = CreateTempDirectory();
        try
        {
            var legacyDbPath = Path.Combine(directory, "legacy.db");
            var targetDbPath = Path.Combine(directory, "calendar.db");
            var zipPath = Path.Combine(directory, "backup.zip");

            await CreateCompatibleLegacyDatabaseAsync(legacyDbPath, startupTabIndex: 2);
            await CreateBackupZipAsync(zipPath, legacyDbPath);

            var targetRepository = new CalendarRepository(targetDbPath);
            await targetRepository.InitializeAsync();
            await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 4 });

            await new BackupService().RestoreBackupAsync(zipPath, targetDbPath);

            Assert.True(await HasColumnAsync(targetDbPath, "events", "start_utc_ticks"));
            Assert.True(await HasColumnAsync(targetDbPath, "events", "last_synced_google_etag"));
            Assert.Equal(2, (await new CalendarRepository(targetDbPath).LoadSettingsAsync()).StartupTabIndex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_WhenSchemaMigrationFails_LeavesCurrentDatabaseUnchanged()
    {
        var directory = CreateTempDirectory();
        try
        {
            var incompatibleDbPath = Path.Combine(directory, "incompatible.db");
            var targetDbPath = Path.Combine(directory, "calendar.db");
            var zipPath = Path.Combine(directory, "backup.zip");

            await CreateIncompatibleDatabaseAsync(incompatibleDbPath);
            await CreateBackupZipAsync(zipPath, incompatibleDbPath);

            var targetRepository = new CalendarRepository(targetDbPath);
            await targetRepository.InitializeAsync();
            await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 5 });

            await Assert.ThrowsAnyAsync<Exception>(() => new BackupService().RestoreBackupAsync(zipPath, targetDbPath));

            var settings = await new CalendarRepository(targetDbPath).LoadSettingsAsync();
            Assert.Equal(5, settings.StartupTabIndex);
            Assert.True(await HasColumnAsync(targetDbPath, "events", "start_utc_ticks"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteDirectory(directory);
        }
    }

    private static async Task CreateCompatibleLegacyDatabaseAsync(string path, int startupTabIndex)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE events (
                id TEXT PRIMARY KEY,
                google_event_id TEXT,
                calendar_id TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT,
                location TEXT,
                start TEXT NOT NULL,
                end TEXT NOT NULL,
                is_all_day INTEGER NOT NULL,
                color_id TEXT,
                recurrence_json TEXT,
                is_deleted INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                last_synced_at TEXT,
                is_dirty INTEGER NOT NULL,
                is_todo_like INTEGER NOT NULL
            );
            CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE tags (name TEXT PRIMARY KEY, color TEXT NOT NULL, is_visible INTEGER NOT NULL, priority INTEGER NOT NULL);
            INSERT INTO settings(key, value) VALUES('app', $settings);
            """;
        command.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(new AppSettings { StartupTabIndex = startupTabIndex }));
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }

    private static async Task CreateIncompatibleDatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE events (id TEXT PRIMARY KEY);
            CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE tags (name TEXT PRIMARY KEY, color TEXT NOT NULL, is_visible INTEGER NOT NULL, priority INTEGER NOT NULL);
            """;
        await command.ExecuteNonQueryAsync();
        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();
    }

    private static async Task CreateBackupZipAsync(string zipPath, string databasePath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var dbEntry = archive.CreateEntry(BackupService.DatabaseEntryName);
        await using (var source = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var destination = dbEntry.Open())
        {
            await source.CopyToAsync(destination);
        }

        var manifestEntry = archive.CreateEntry(BackupService.ManifestEntryName);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(
            manifestStream,
            new BackupManifest("FavGCalSchedulerClone", BackupService.FormatVersion, DateTimeOffset.Now, Path.GetFileName(databasePath)));
    }

    private static async Task<bool> HasColumnAsync(string databasePath, string tableName, string columnName)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"restore-preflight-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

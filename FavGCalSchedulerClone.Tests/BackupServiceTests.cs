using System.IO.Compression;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task CreateBackupAsync_WritesDatabaseAndManifest()
    {
        var directory = CreateTempDirectory();
        var dbPath = Path.Combine(directory, "calendar.db");
        var zipPath = Path.Combine(directory, "backup.zip");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 2 });
        var service = new BackupService();

        await service.CreateBackupAsync(dbPath, zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.NotNull(archive.GetEntry(BackupService.DatabaseEntryName));
        var manifestEntry = archive.GetEntry(BackupService.ManifestEntryName);
        Assert.NotNull(manifestEntry);
        await using var manifestStream = manifestEntry!.Open();
        var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream);
        Assert.NotNull(manifest);
        Assert.Equal("FavGCalSchedulerClone", manifest!.ApplicationName);
        Assert.Equal(BackupService.FormatVersion, manifest.FormatVersion);
    }

    [Fact]
    public async Task CreateBackupAsync_HandlesDatabasePathContainingSemicolon()
    {
        var directory = CreateTempDirectory();
        var dbPath = Path.Combine(directory, "calendar;semi.db");
        var zipPath = Path.Combine(directory, "backup.zip");
        var repository = new CalendarRepository(dbPath);
        await repository.InitializeAsync();
        await repository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 3 });

        await new BackupService().CreateBackupAsync(dbPath, zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        var dbEntry = archive.GetEntry(BackupService.DatabaseEntryName);
        Assert.NotNull(dbEntry);
        var restoredDbPath = Path.Combine(directory, "restored;semi.db");
        await using (var entryStream = dbEntry!.Open())
        await using (var restored = new FileStream(restoredDbPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await entryStream.CopyToAsync(restored);
        }

        var restoredSettings = await new CalendarRepository(restoredDbPath).LoadSettingsAsync();
        Assert.Equal(3, restoredSettings.StartupTabIndex);
    }

    [Fact]
    public async Task RestoreBackupAsync_RestoresDatabaseAndBacksUpExistingDatabase()
    {
        var directory = CreateTempDirectory();
        var sourceDbPath = Path.Combine(directory, "source.db");
        var targetDbPath = Path.Combine(directory, "calendar.db");
        var zipPath = Path.Combine(directory, "backup.zip");
        var sourceRepository = new CalendarRepository(sourceDbPath);
        await sourceRepository.InitializeAsync();
        await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 2 });
        var targetRepository = new CalendarRepository(targetDbPath);
        await targetRepository.InitializeAsync();
        await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 1 });
        var service = new BackupService();
        await service.CreateBackupAsync(sourceDbPath, zipPath);

        var result = await service.RestoreBackupAsync(zipPath, targetDbPath);
        var restoredRepository = new CalendarRepository(targetDbPath);
        var restoredSettings = await restoredRepository.LoadSettingsAsync();

        Assert.Equal(2, restoredSettings.StartupTabIndex);
        Assert.False(string.IsNullOrWhiteSpace(result.PreviousDatabaseBackupPath));
        Assert.True(File.Exists(result.PreviousDatabaseBackupPath));
        var rollbackRepository = new CalendarRepository(result.PreviousDatabaseBackupPath);
        var rollbackSettings = await rollbackRepository.LoadSettingsAsync();
        Assert.Equal(1, rollbackSettings.StartupTabIndex);
    }

    [Fact]
    public async Task RestoreBackupAsync_UsesUniqueRollbackPathForRapidRestores()
    {
        var directory = CreateTempDirectory();
        var sourceDbPath = Path.Combine(directory, "source.db");
        var targetDbPath = Path.Combine(directory, "calendar.db");
        var zipPath = Path.Combine(directory, "backup.zip");
        var sourceRepository = new CalendarRepository(sourceDbPath);
        await sourceRepository.InitializeAsync();
        await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 2 });
        var targetRepository = new CalendarRepository(targetDbPath);
        await targetRepository.InitializeAsync();
        await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 1 });
        var service = new BackupService();
        await service.CreateBackupAsync(sourceDbPath, zipPath);

        var first = await service.RestoreBackupAsync(zipPath, targetDbPath);
        var second = await service.RestoreBackupAsync(zipPath, targetDbPath);

        Assert.NotEqual(first.PreviousDatabaseBackupPath, second.PreviousDatabaseBackupPath);
        Assert.True(File.Exists(first.PreviousDatabaseBackupPath));
        Assert.True(File.Exists(second.PreviousDatabaseBackupPath));
    }

    [Fact]
    public async Task RestoreBackupAsync_RejectsInvalidZip()
    {
        var directory = CreateTempDirectory();
        var zipPath = Path.Combine(directory, "invalid.zip");
        var targetDbPath = Path.Combine(directory, "calendar.db");
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService().RestoreBackupAsync(zipPath, targetDbPath));
    }

    [Fact]
    public async Task RestoreBackupAsync_RejectsUnsupportedManifestWithoutReplacingCurrentDatabase()
    {
        var directory = CreateTempDirectory();
        var zipPath = Path.Combine(directory, "wrong-app.zip");
        var targetDbPath = Path.Combine(directory, "calendar.db");
        var targetRepository = new CalendarRepository(targetDbPath);
        await targetRepository.InitializeAsync();
        await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 4 });
        await CreateBackupZipAsync(zipPath, targetDbPath, new BackupManifest("OtherApp", BackupService.FormatVersion, DateTimeOffset.Now, "calendar.db"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService().RestoreBackupAsync(zipPath, targetDbPath));

        var settings = await new CalendarRepository(targetDbPath).LoadSettingsAsync();
        Assert.Equal(4, settings.StartupTabIndex);
    }

    [Fact]
    public async Task RestoreBackupAsync_RejectsInvalidSqliteWithoutReplacingCurrentDatabase()
    {
        var directory = CreateTempDirectory();
        var zipPath = Path.Combine(directory, "invalid-db.zip");
        var targetDbPath = Path.Combine(directory, "calendar.db");
        var invalidDbPath = Path.Combine(directory, "not-sqlite.db");
        await File.WriteAllTextAsync(invalidDbPath, "not sqlite");
        var targetRepository = new CalendarRepository(targetDbPath);
        await targetRepository.InitializeAsync();
        await targetRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 5 });
        await CreateBackupZipAsync(zipPath, invalidDbPath, new BackupManifest("FavGCalSchedulerClone", BackupService.FormatVersion, DateTimeOffset.Now, "not-sqlite.db"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService().RestoreBackupAsync(zipPath, targetDbPath));

        var settings = await new CalendarRepository(targetDbPath).LoadSettingsAsync();
        Assert.Equal(5, settings.StartupTabIndex);
    }

    [Fact]
    public async Task RestoreBackupAsync_RejectsDatabaseMissingRequiredTables()
    {
        var directory = CreateTempDirectory();
        var zipPath = Path.Combine(directory, "missing-tables.zip");
        var targetDbPath = Path.Combine(directory, "calendar.db");
        var incompleteDbPath = Path.Combine(directory, "incomplete.db");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = incompleteDbPath }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE events (id TEXT PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var targetRepository = new CalendarRepository(targetDbPath);
        await targetRepository.InitializeAsync();
        await CreateBackupZipAsync(zipPath, incompleteDbPath, new BackupManifest("FavGCalSchedulerClone", BackupService.FormatVersion, DateTimeOffset.Now, "incomplete.db"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BackupService().RestoreBackupAsync(zipPath, targetDbPath));
    }

    [Fact]
    public async Task RestoreAllCalendarsAsync_ReloadsSettingsAndEvents()
    {
        var directory = CreateTempDirectory();
        var currentDbPath = Path.Combine(directory, "current.db");
        var backupSourceDbPath = Path.Combine(directory, "source.db");
        var zipPath = Path.Combine(directory, "backup.zip");

        var currentRepository = new CalendarRepository(currentDbPath);
        await currentRepository.InitializeAsync();
        await currentRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 1 });

        var sourceRepository = new CalendarRepository(backupSourceDbPath);
        await sourceRepository.InitializeAsync();
        await sourceRepository.SaveSettingsAsync(new AppSettings { StartupTabIndex = 3 });
        await sourceRepository.SaveEventAsync(new CalendarEvent
        {
            Title = "restored",
            CalendarId = "primary",
            Start = new DateTimeOffset(DateTime.Today),
            End = new DateTimeOffset(DateTime.Today.AddDays(1)),
            IsAllDay = true
        });
        await new BackupService().CreateBackupAsync(backupSourceDbPath, zipPath);

        var viewModel = new MainViewModel(currentRepository, new GoogleCalendarSyncService(currentRepository));
        await viewModel.InitializeAsync();
        await viewModel.RestoreAllCalendarsAsync(zipPath);

        Assert.Equal(3, viewModel.StartupTabIndex);
        Assert.Equal(3, viewModel.SelectedTabIndex);
        Assert.Contains(viewModel.CalendarDays.SelectMany(day => day.Events), item => item.Title == "restored");
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task CreateBackupZipAsync(string zipPath, string databasePath, BackupManifest manifest)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var dbEntry = archive.CreateEntry(BackupService.DatabaseEntryName);
        await using (var source = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        await using (var destination = dbEntry.Open())
        {
            await source.CopyToAsync(destination);
        }

        var manifestEntry = archive.CreateEntry(BackupService.ManifestEntryName);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest);
    }
}

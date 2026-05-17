using System.IO.Compression;
using System.Text.Json;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;

namespace FavGCalSchedulerClone.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task CreateBackupAsync_WritesDatabaseAndManifest()
    {
        var directory = CreateTempDirectory();
        var dbPath = Path.Combine(directory, "calendar.db");
        var zipPath = Path.Combine(directory, "backup.zip");
        await File.WriteAllTextAsync(dbPath, "db-content");
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
    public async Task RestoreBackupAsync_RestoresDatabaseAndBacksUpExistingDatabase()
    {
        var directory = CreateTempDirectory();
        var sourceDbPath = Path.Combine(directory, "source.db");
        var targetDbPath = Path.Combine(directory, "calendar.db");
        var zipPath = Path.Combine(directory, "backup.zip");
        await File.WriteAllTextAsync(sourceDbPath, "restored-db");
        await File.WriteAllTextAsync(targetDbPath, "current-db");
        var service = new BackupService();
        await service.CreateBackupAsync(sourceDbPath, zipPath);

        var result = await service.RestoreBackupAsync(zipPath, targetDbPath);

        Assert.Equal("restored-db", await File.ReadAllTextAsync(targetDbPath));
        Assert.False(string.IsNullOrWhiteSpace(result.PreviousDatabaseBackupPath));
        Assert.True(File.Exists(result.PreviousDatabaseBackupPath));
        Assert.Equal("current-db", await File.ReadAllTextAsync(result.PreviousDatabaseBackupPath!));
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
}

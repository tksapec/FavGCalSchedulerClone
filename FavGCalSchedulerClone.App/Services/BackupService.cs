using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.App.Services;

public sealed class BackupService
{
    public const string DatabaseEntryName = "calendar.db";
    public const string ManifestEntryName = "manifest.json";
    public const int FormatVersion = 1;

    public async Task<BackupResult> CreateBackupAsync(string databasePath, string backupZipPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Backup source database was not found.", databasePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupZipPath))!);
        var tempZipPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(backupZipPath))!, $"{Path.GetFileName(backupZipPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var fileStream = new FileStream(tempZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                var dbEntry = archive.CreateEntry(DatabaseEntryName, CompressionLevel.Optimal);
                await using (var source = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                await using (var destination = dbEntry.Open())
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                var manifest = new BackupManifest(
                    "FavGCalSchedulerClone",
                    FormatVersion,
                    DateTimeOffset.Now,
                    Path.GetFileName(databasePath));
                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: cancellationToken);
            }

            File.Move(tempZipPath, backupZipPath, true);
            return new BackupResult(backupZipPath);
        }
        catch
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            throw;
        }
    }

    public async Task<RestoreResult> RestoreBackupAsync(string backupZipPath, string databasePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupZipPath))
        {
            throw new FileNotFoundException("Backup ZIP was not found.", backupZipPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var backupDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        var rollbackPath = Path.Combine(backupDirectory, $"{Path.GetFileName(databasePath)}.restore-backup-{DateTime.Now:yyyyMMdd-HHmmss}");
        var tempRestorePath = Path.Combine(backupDirectory, $"{Path.GetFileName(databasePath)}.restore-{Guid.NewGuid():N}.tmp");
        var currentMoved = false;

        try
        {
            using (var archive = ZipFile.OpenRead(backupZipPath))
            {
                ValidateArchive(archive);
                var dbEntry = archive.GetEntry(DatabaseEntryName)!;
                await using var source = dbEntry.Open();
                await using var destination = new FileStream(tempRestorePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(destination, cancellationToken);
            }

            SqliteConnection.ClearAllPools();

            if (File.Exists(databasePath))
            {
                File.Move(databasePath, rollbackPath);
                currentMoved = true;
            }

            File.Move(tempRestorePath, databasePath);
            return new RestoreResult(databasePath, currentMoved ? rollbackPath : null);
        }
        catch
        {
            if (File.Exists(tempRestorePath))
            {
                File.Delete(tempRestorePath);
            }

            SqliteConnection.ClearAllPools();

            if (currentMoved && !File.Exists(databasePath) && File.Exists(rollbackPath))
            {
                File.Move(rollbackPath, databasePath);
            }

            throw;
        }
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        var manifestEntry = archive.GetEntry(ManifestEntryName);
        var dbEntry = archive.GetEntry(DatabaseEntryName);
        if (manifestEntry is null || dbEntry is null)
        {
            throw new InvalidDataException("The selected file is not a FavGCalSchedulerClone backup.");
        }
    }
}

public sealed record BackupManifest(string ApplicationName, int FormatVersion, DateTimeOffset CreatedAt, string SourceDatabaseFileName);
public sealed record BackupResult(string BackupPath);
public sealed record RestoreResult(string DatabasePath, string? PreviousDatabaseBackupPath);

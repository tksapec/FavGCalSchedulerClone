using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.App.Services;

public sealed class BackupService
{
    private const string ApplicationName = "FavGCalSchedulerClone";
    private static readonly string[] RequiredTables = ["events", "settings", "tags"];
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
        var tempDatabasePath = Path.Combine(Path.GetTempPath(), $"{Path.GetFileName(databasePath)}.backup-{Guid.NewGuid():N}.tmp");

        try
        {
            await CreateConsistentDatabaseCopyAsync(databasePath, tempDatabasePath, cancellationToken);

            await using (var fileStream = new FileStream(tempZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                var dbEntry = archive.CreateEntry(DatabaseEntryName, CompressionLevel.Optimal);
                await using (var source = new FileStream(tempDatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var destination = dbEntry.Open())
                {
                    await source.CopyToAsync(destination, cancellationToken);
                }

                var manifest = new BackupManifest(
                    ApplicationName,
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
        finally
        {
            if (File.Exists(tempDatabasePath))
            {
                File.Delete(tempDatabasePath);
            }
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
        var rollbackPath = Path.Combine(backupDirectory, $"{Path.GetFileName(databasePath)}.restore-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
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

            await ValidateRestoredDatabaseAsync(tempRestorePath, cancellationToken);
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
            SqliteConnection.ClearAllPools();

            if (File.Exists(tempRestorePath))
            {
                File.Delete(tempRestorePath);
            }

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

        BackupManifest? manifest;
        try
        {
            using var manifestStream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<BackupManifest>(manifestStream);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The backup manifest is invalid.", ex);
        }

        if (manifest is null
            || !string.Equals(manifest.ApplicationName, ApplicationName, StringComparison.Ordinal)
            || manifest.FormatVersion != FormatVersion)
        {
            throw new InvalidDataException("The backup format is not supported.");
        }

        if (dbEntry.Length <= 0)
        {
            throw new InvalidDataException("The backup database is empty.");
        }
    }

    private static async Task ValidateRestoredDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly
            };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA integrity_check;";
                var result = await command.ExecuteScalarAsync(cancellationToken) as string;
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The backup database failed SQLite integrity_check.");
                }
            }

            foreach (var table in RequiredTables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
                command.Parameters.AddWithValue("$name", table);
                var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
                if (count == 0)
                {
                    throw new InvalidDataException($"The backup database is missing required table '{table}'.");
                }
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException("The backup database is not a valid SQLite database.", ex);
        }
    }

    private static async Task CreateConsistentDatabaseCopyAsync(string databasePath, string destinationPath, CancellationToken cancellationToken)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            await using var source = new SqliteConnection(CreateConnectionString(databasePath));
            await source.OpenAsync(cancellationToken);
            await using var destination = new SqliteConnection(CreateConnectionString(destinationPath));
            await destination.OpenAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            source.BackupDatabase(destination);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static string CreateConnectionString(string databasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }
}

public sealed record BackupManifest(string ApplicationName, int FormatVersion, DateTimeOffset CreatedAt, string SourceDatabaseFileName);
public sealed record BackupResult(string BackupPath);
public sealed record RestoreResult(string DatabasePath, string? PreviousDatabaseBackupPath);

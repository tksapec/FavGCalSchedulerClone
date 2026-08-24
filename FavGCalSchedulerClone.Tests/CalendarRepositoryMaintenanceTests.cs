using System.Reflection;
using FavGCalSchedulerClone.App.Services;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class CalendarRepositoryMaintenanceTests
{
    [Fact]
    public async Task Maintenance_WaitsForOpenConnectionsAndRejectsNewConnectionsUntilReleased()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            var openConnection = typeof(CalendarRepository).GetMethod(
                "OpenConnection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(openConnection);

            var connection = Assert.IsType<SqliteConnection>(openConnection!.Invoke(repository, null));
            var maintenanceTask = repository.BeginMaintenanceAsync();
            Assert.False(maintenanceTask.IsCompleted);

            await connection.DisposeAsync();
            await maintenanceTask;
            try
            {
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadSettingsAsync());
                Assert.Contains("maintenance", exception.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                repository.EndMaintenance();
            }

            _ = await repository.LoadSettingsAsync();
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

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

    [Fact]
    public async Task MaintenanceAccessScope_AllowsOnlyTheOwnerFlowToUseTheRepository()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveSettingsAsync(new FavGCalSchedulerClone.App.Models.AppSettings { StartupTabIndex = 3 });
            await repository.BeginMaintenanceAsync();
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadSettingsAsync());

                var releaseUnrelatedFlow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var unrelatedFlow = Task.Run(async () =>
                {
                    await releaseUnrelatedFlow.Task;
                    try
                    {
                        _ = await repository.LoadSettingsAsync();
                        return (Exception?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                });

                await repository.RunWithMaintenanceAccessAsync(async () =>
                {
                    releaseUnrelatedFlow.TrySetResult(true);
                    var loaded = await repository.LoadSettingsAsync();
                    Assert.Equal(3, loaded.StartupTabIndex);
                    Assert.IsType<InvalidOperationException>(await unrelatedFlow);
                });

                await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadSettingsAsync());
            }
            finally
            {
                repository.EndMaintenance();
            }
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
    public async Task MaintenanceAccessScope_ExpiresForChildFlowsThatOutliveTheOwnerScope()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.BeginMaintenanceAsync();
            try
            {
                var childCreated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseChild = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<Exception?>? child = null;

                await repository.RunWithMaintenanceAccessAsync(() =>
                {
                    child = Task.Run(async () =>
                    {
                        childCreated.TrySetResult(true);
                        await releaseChild.Task;
                        try
                        {
                            _ = await repository.LoadSettingsAsync();
                            return (Exception?)null;
                        }
                        catch (Exception ex)
                        {
                            return ex;
                        }
                    });
                    return childCreated.Task;
                });

                releaseChild.TrySetResult(true);
                Assert.NotNull(child);
                Assert.IsType<InvalidOperationException>(await child!);
            }
            finally
            {
                repository.EndMaintenance();
            }
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
    public async Task FailedOpen_DoesNotLeakMaintenanceConnectionCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"repository-open-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var blockerPath = Path.Combine(directory, "not-a-directory");
        await File.WriteAllTextAsync(blockerPath, "block");
        var repository = new CalendarRepository(Path.Combine(blockerPath, "calendar.db"));

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => repository.LoadSettingsAsync());

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await repository.BeginMaintenanceAsync(cancellation.Token);
            repository.EndMaintenance();
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

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

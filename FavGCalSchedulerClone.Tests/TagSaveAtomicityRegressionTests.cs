using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class TagSaveAtomicityRegressionTests
{
    [Fact]
    public async Task SaveTagsAsync_RollsBackAllTagsWhenOneWriteFails()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var repository = new CalendarRepository(dbPath);
            await repository.InitializeAsync();
            await repository.SaveTagAsync(new CalendarTag { Name = "atomic-one", Color = "#111111", IsVisible = true, Priority = 1 });
            await repository.SaveTagAsync(new CalendarTag { Name = "atomic-two", Color = "#222222", IsVisible = true, Priority = 2 });

            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            await viewModel.InitializeAsync();
            var first = Assert.Single(viewModel.Tags, item => item.Name == "atomic-one");
            var second = Assert.Single(viewModel.Tags, item => item.Name == "atomic-two");
            first.Color = "#AAAAAA";
            second.Color = "#BBBBBB";

            await using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var trigger = connection.CreateCommand();
                trigger.CommandText = """
                    CREATE TRIGGER fail_second_tag
                    BEFORE INSERT ON tags
                    WHEN NEW.name = 'atomic-two'
                    BEGIN
                        SELECT RAISE(ABORT, 'intentional tag save failure');
                    END;
                    """;
                await trigger.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAsync<SqliteException>(() => viewModel.SaveTagsAsync());

            var reloaded = await repository.LoadTagsAsync();
            Assert.Equal("#111111", Assert.Single(reloaded, item => item.Name == "atomic-one").Color);
            Assert.Equal("#222222", Assert.Single(reloaded, item => item.Name == "atomic-two").Color);
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

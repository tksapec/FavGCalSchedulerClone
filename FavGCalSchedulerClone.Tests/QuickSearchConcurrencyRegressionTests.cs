using System.Reflection;
using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class QuickSearchConcurrencyRegressionTests
{
    [Fact]
    public async Task RunCurrentYearSearchAsync_WhenClearedWhilePending_DoesNotRestoreResultsOrMonthView()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        fixture.ViewModel.CurrentViewMode = CalendarViewMode.Week;

        await fixture.BlockDatabaseReadsAsync();
        var searchTask = Task.Run(async () => await fixture.ViewModel.RunCurrentYearSearchAsync());
        await fixture.WaitForRepositoryConnectionAsync();

        fixture.ViewModel.ClearCurrentYearSearchCommand.Execute(null);
        Assert.False(fixture.ViewModel.IsSearchResultsVisible);
        Assert.Equal(CalendarViewMode.Week, fixture.ViewModel.CurrentViewMode);

        await fixture.ReleaseDatabaseReadsAsync();
        await searchTask;

        Assert.False(fixture.ViewModel.IsSearchResultsVisible);
        Assert.Empty(fixture.ViewModel.SearchResults);
        Assert.Equal(CalendarViewMode.Week, fixture.ViewModel.CurrentViewMode);
        Assert.Equal("検索結果を閉じました。", fixture.ViewModel.Status);
    }

    [Fact]
    public async Task RunCurrentYearSearchAsync_WhenQueryChangesWhilePending_DiscardsStaleResult()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        fixture.ViewModel.CurrentViewMode = CalendarViewMode.Week;
        fixture.ViewModel.SearchQuery = "alpha";

        await fixture.BlockDatabaseReadsAsync();
        var searchTask = Task.Run(async () => await fixture.ViewModel.RunCurrentYearSearchAsync());
        await fixture.WaitForRepositoryConnectionAsync();

        fixture.ViewModel.SearchQuery = "beta";

        await fixture.ReleaseDatabaseReadsAsync();
        await searchTask;

        Assert.Equal("beta", fixture.ViewModel.SearchQuery);
        Assert.False(fixture.ViewModel.IsSearchResultsVisible);
        Assert.Empty(fixture.ViewModel.SearchResults);
        Assert.Equal(CalendarViewMode.Week, fixture.ViewModel.CurrentViewMode);
    }

    [Fact]
    public async Task RunCurrentYearSearchAsync_WhenViewChangesWhilePending_DoesNotOverwriteLatestView()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        fixture.ViewModel.CurrentViewMode = CalendarViewMode.Week;

        await fixture.BlockDatabaseReadsAsync();
        var searchTask = Task.Run(async () => await fixture.ViewModel.RunCurrentYearSearchAsync());
        await fixture.WaitForRepositoryConnectionAsync();

        fixture.ViewModel.CurrentViewMode = CalendarViewMode.Day;

        await fixture.ReleaseDatabaseReadsAsync();
        await searchTask;

        Assert.Equal(CalendarViewMode.Day, fixture.ViewModel.CurrentViewMode);
    }

    [Fact]
    public async Task RunCurrentYearSearchAsync_WhenCalendarYearChangesWhilePending_DiscardsOldYearResult()
    {
        await using var fixture = await SearchFixture.CreateAsync();
        var originalYear = fixture.ViewModel.CurrentMonth.Year;

        await fixture.BlockDatabaseReadsAsync();
        var searchTask = Task.Run(async () => await fixture.ViewModel.RunCurrentYearSearchAsync());
        await fixture.WaitForRepositoryConnectionAsync();

        fixture.SetCurrentMonthWithoutRefresh(fixture.ViewModel.CurrentMonth.AddYears(1));

        await fixture.ReleaseDatabaseReadsAsync();
        await searchTask;

        Assert.Equal(originalYear + 1, fixture.ViewModel.CurrentMonth.Year);
        Assert.False(fixture.ViewModel.IsSearchResultsVisible);
        Assert.Empty(fixture.ViewModel.SearchResults);
    }

    private sealed class SearchFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private SqliteConnection? _blockingConnection;
        private bool _exclusiveTransactionActive;

        private SearchFixture(string databasePath, CalendarRepository repository, MainViewModel viewModel)
        {
            _databasePath = databasePath;
            Repository = repository;
            ViewModel = viewModel;
        }

        public CalendarRepository Repository { get; }
        public MainViewModel ViewModel { get; }

        public static async Task<SearchFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"quick-search-race-{Guid.NewGuid():N}.db");
            var repository = new CalendarRepository(databasePath);
            await repository.InitializeAsync();
            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));
            var searchYear = viewModel.CurrentMonth.Year;
            await repository.SaveEventAsync(new CalendarEvent
            {
                Id = "alpha-event",
                CalendarId = "primary",
                Title = "alpha",
                Start = new DateTimeOffset(searchYear, 5, 15, 9, 0, 0, TimeSpan.Zero),
                End = new DateTimeOffset(searchYear, 5, 15, 10, 0, 0, TimeSpan.Zero)
            });
            return new SearchFixture(databasePath, repository, viewModel);
        }

        public void SetCurrentMonthWithoutRefresh(DateTime value)
        {
            var field = typeof(MainViewModel).GetField(
                "_currentMonth",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(ViewModel, value);
        }

        public async Task BlockDatabaseReadsAsync()
        {
            SqliteConnection.ClearAllPools();
            _blockingConnection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                DefaultTimeout = 5
            }.ToString());
            await _blockingConnection.OpenAsync();

            await using (var journalMode = _blockingConnection.CreateCommand())
            {
                journalMode.CommandText = "PRAGMA journal_mode=DELETE;";
                await journalMode.ExecuteNonQueryAsync();
            }

            await using var begin = _blockingConnection.CreateCommand();
            begin.CommandText = "BEGIN EXCLUSIVE;";
            await begin.ExecuteNonQueryAsync();
            _exclusiveTransactionActive = true;
        }

        public async Task WaitForRepositoryConnectionAsync()
        {
            var timeoutAt = DateTime.UtcNow.AddSeconds(3);
            while (GetActiveConnectionCount(Repository) == 0)
            {
                if (DateTime.UtcNow >= timeoutAt)
                {
                    throw new TimeoutException("The pending quick search did not open a repository connection.");
                }

                await Task.Delay(10);
            }
        }

        public async Task ReleaseDatabaseReadsAsync()
        {
            if (!_exclusiveTransactionActive || _blockingConnection is null)
            {
                return;
            }

            await using var commit = _blockingConnection.CreateCommand();
            commit.CommandText = "COMMIT;";
            await commit.ExecuteNonQueryAsync();
            _exclusiveTransactionActive = false;
        }

        public async ValueTask DisposeAsync()
        {
            if (_exclusiveTransactionActive && _blockingConnection is not null)
            {
                try
                {
                    await using var rollback = _blockingConnection.CreateCommand();
                    rollback.CommandText = "ROLLBACK;";
                    await rollback.ExecuteNonQueryAsync();
                }
                catch
                {
                    // Best-effort cleanup for a failed regression test.
                }
            }

            if (_blockingConnection is not null)
            {
                await _blockingConnection.DisposeAsync();
            }

            SqliteConnection.ClearAllPools();
            DeleteIfExists(_databasePath);
            DeleteIfExists(_databasePath + "-wal");
            DeleteIfExists(_databasePath + "-shm");
        }

        private static int GetActiveConnectionCount(CalendarRepository repository)
        {
            var field = typeof(CalendarRepository).GetField(
                "_activeConnectionCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<int>(field!.GetValue(repository));
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
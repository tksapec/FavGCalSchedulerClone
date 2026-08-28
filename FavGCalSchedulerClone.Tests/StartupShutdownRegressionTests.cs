using FavGCalSchedulerClone.App.Models;
using FavGCalSchedulerClone.App.Services;
using FavGCalSchedulerClone.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace FavGCalSchedulerClone.Tests;

public sealed class StartupShutdownRegressionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public async Task StartupInitialization_RechecksDisposedStateAcrossAsyncStages()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            Root,
            "FavGCalSchedulerClone.App",
            "Services",
            "ApplicationStartupService.cs"));
        var viewModelAwait = source.IndexOf("await _viewModel.InitializeAsync()", StringComparison.Ordinal);
        var notifierStart = source.IndexOf("_reminderService.SetNotifier", viewModelAwait, StringComparison.Ordinal);
        var reminderAwait = source.IndexOf("await _reminderService.StartAsync()", notifierStart, StringComparison.Ordinal);
        var timerStart = source.IndexOf("_automaticSyncTimer.Start()", reminderAwait, StringComparison.Ordinal);

        Assert.True(viewModelAwait >= 0 && notifierStart > viewModelAwait);
        Assert.True(reminderAwait > notifierStart && timerStart > reminderAwait);
        Assert.Contains("if (_disposed)", source[viewModelAwait..notifierStart], StringComparison.Ordinal);
        Assert.Contains("if (_disposed)", source[reminderAwait..timerStart], StringComparison.Ordinal);
        Assert.Contains("if (_disposed)", source[timerStart..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartupErrors_DoNotShowModalDialogsAfterServiceDisposal()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            Root,
            "FavGCalSchedulerClone.App",
            "Services",
            "ApplicationStartupService.cs"));

        Assert.Contains("if (_disposed)", source, StringComparison.Ordinal);
        Assert.Contains("MessageBox.Show", source, StringComparison.Ordinal);
        Assert.Contains("return;", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ViewModelInitializationFailure_StopsAndPropagatesTheStartupSequence()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            Root,
            "FavGCalSchedulerClone.App",
            "Services",
            "ApplicationStartupService.cs"));

        source = source.ReplaceLineEndings("\n");

        Assert.Contains(
            "MessageBox.Show(owner, ex.Message, \"初期化エラー\", MessageBoxButton.OK, MessageBoxImage.Error);\n            throw;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrayExit_DoesNotDisposeApplicationServicesWhileDatabaseMaintenanceIsRunning()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            Root,
            "FavGCalSchedulerClone.App",
            "App.xaml.cs"));
        var methodStart = source.IndexOf("private void ExitFromTray()", StringComparison.Ordinal);
        var nextMethod = source.IndexOf("private void UpdateTrayDateIcon()", methodStart, StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && nextMethod > methodStart);
        var method = source[methodStart..nextMethod];

        var maintenanceCheck = method.IndexOf("IsDatabaseMaintenanceInProgress", StringComparison.Ordinal);
        var exitingLatch = method.IndexOf("_isExiting = true;", StringComparison.Ordinal);
        var shutdown = method.IndexOf("Shutdown();", StringComparison.Ordinal);

        Assert.True(maintenanceCheck >= 0 && maintenanceCheck < exitingLatch,
            "Tray exit must refuse shutdown before latching _isExiting while restore/database maintenance is still active.");
        Assert.True(shutdown > exitingLatch,
            "Shutdown must only be reached after the maintenance guard has allowed exit.");
    }

    [Fact]
    public async Task ExitFlushBeforeSettingsLoad_DoesNotOverwriteStoredSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"startup-exit-flush-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "calendar.db");

        try
        {
            var repository = new CalendarRepository(databasePath);
            await repository.InitializeAsync();
            var expectedMonth = new DateTime(2031, 4, 1);
            await repository.SaveSettingsAsync(new AppSettings
            {
                StartupTabIndex = 7,
                DisplayMonth = expectedMonth,
                WeekStartsOnMonday = true
            });

            var viewModel = new MainViewModel(repository, new GoogleCalendarSyncService(repository));

            // Simulate an immediate tray exit while ApplicationStartupService is still
            // before MainViewModel.InitializeAsync has loaded the persisted settings.
            await viewModel.FlushDisplayMonthPersistenceAsync();

            var stored = await repository.LoadSettingsAsync();
            Assert.Equal(7, stored.StartupTabIndex);
            Assert.Equal(expectedMonth, stored.DisplayMonth);
            Assert.True(stored.WeekStartsOnMonday);
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
}
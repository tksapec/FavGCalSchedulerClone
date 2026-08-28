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
}

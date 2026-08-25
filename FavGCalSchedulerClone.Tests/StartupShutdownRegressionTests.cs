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

        Assert.Contains(
            "MessageBox.Show(owner, ex.Message, \"初期化エラー\", MessageBoxButton.OK, MessageBoxImage.Error);\n            throw;",
            source,
            StringComparison.Ordinal);
    }
}

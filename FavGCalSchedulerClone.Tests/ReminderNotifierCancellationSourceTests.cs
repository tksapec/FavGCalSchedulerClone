namespace FavGCalSchedulerClone.Tests;

public sealed class ReminderNotifierCancellationSourceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public async Task MessageBoxNotifier_ChecksCancellationInsideUiDispatchBeforeShowingDialog()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(
            Root,
            "FavGCalSchedulerClone.App",
            "Services",
            "MessageBoxReminderNotifier.cs"));

        var dispatchIndex = source.IndexOf("Dispatcher.InvokeAsync", StringComparison.Ordinal);
        var cancellationIndex = source.IndexOf("cancellationToken.ThrowIfCancellationRequested()", dispatchIndex, StringComparison.Ordinal);
        var messageBoxIndex = source.IndexOf("MessageBox.Show", dispatchIndex, StringComparison.Ordinal);

        Assert.True(dispatchIndex >= 0);
        Assert.True(cancellationIndex > dispatchIndex);
        Assert.True(messageBoxIndex > cancellationIndex);
    }
}

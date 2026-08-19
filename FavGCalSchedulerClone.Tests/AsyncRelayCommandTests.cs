using FavGCalSchedulerClone.App.Commands;

namespace FavGCalSchedulerClone.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task Execute_ReportsExceptionToHandler()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException("failed"),
            onException: exception =>
            {
                reported.SetResult(exception);
                return Task.CompletedTask;
            });

        command.Execute(null);

        var exception = await reported.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("failed", exception.Message);
    }

    [Fact]
    public async Task Execute_DoesNotThrowWhenExceptionHandlerFails()
    {
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException("failed"),
            onException: _ =>
            {
                ran.SetResult();
                throw new InvalidOperationException("handler failed");
            });

        command.Execute(null);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}

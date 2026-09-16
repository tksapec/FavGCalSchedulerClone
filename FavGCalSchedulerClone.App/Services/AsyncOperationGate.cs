namespace FavGCalSchedulerClone.App.Services;

public sealed class AsyncOperationGate
{
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public async Task<bool> TryRunAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            await operation();
            return true;
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }
}

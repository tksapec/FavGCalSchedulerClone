namespace FavGCalSchedulerClone.App.Services;

public interface IApplicationInteractionGuard
{
    bool IsReturnToTodaySuppressed { get; }
    IDisposable EnterOwnedModal();
    IDisposable EnterDragOperation();
}

internal sealed class ApplicationInteractionGuard : IApplicationInteractionGuard
{
    private int _suppressionDepth;

    public bool IsReturnToTodaySuppressed => Volatile.Read(ref _suppressionDepth) > 0;
    public IDisposable EnterOwnedModal() => EnterSuppression();
    public IDisposable EnterDragOperation() => EnterSuppression();

    private IDisposable EnterSuppression()
    {
        Interlocked.Increment(ref _suppressionDepth);
        return new Scope(() => Interlocked.Decrement(ref _suppressionDepth));
    }

    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

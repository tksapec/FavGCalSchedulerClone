namespace FavGCalSchedulerClone.App.Services;

public sealed class FallbackReminderNotifier : IReminderNotifier
{
    private readonly IReminderNotifier _primary;
    private readonly IReminderNotifier _fallback;
    private readonly bool _alwaysShowFallback;

    public FallbackReminderNotifier(IReminderNotifier primary, IReminderNotifier fallback, bool alwaysShowFallback = false)
    {
        _primary = primary;
        _fallback = fallback;
        _alwaysShowFallback = alwaysShowFallback;
    }

    public async Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.ShowAsync(notification, cancellationToken);
            if (_alwaysShowFallback)
            {
                await _fallback.ShowAsync(notification, cancellationToken);
            }
        }
        catch
        {
            await _fallback.ShowAsync(notification, cancellationToken);
        }
    }
}

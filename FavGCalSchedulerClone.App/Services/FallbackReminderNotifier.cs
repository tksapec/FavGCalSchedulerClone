namespace FavGCalSchedulerClone.App.Services;

public sealed class FallbackReminderNotifier : IReminderNotifier
{
    private readonly IReminderNotifier _primary;
    private readonly IReminderNotifier _fallback;

    public FallbackReminderNotifier(IReminderNotifier primary, IReminderNotifier fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public async Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.ShowAsync(notification, cancellationToken);
        }
        catch
        {
            await _fallback.ShowAsync(notification, cancellationToken);
        }
    }
}

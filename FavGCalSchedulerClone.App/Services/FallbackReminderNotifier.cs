namespace FavGCalSchedulerClone.App.Services;

public sealed class FallbackReminderNotifier : IReminderNotifier, IReminderNotifierMetadata
{
    private readonly IReminderNotifier _primary;
    private readonly IReminderNotifier _fallback;
    private readonly bool _alwaysShowFallback;
    private bool _lastUsedFallback;
    private bool _lastPrimaryFailed;

    public FallbackReminderNotifier(IReminderNotifier primary, IReminderNotifier fallback, bool alwaysShowFallback = false)
    {
        _primary = primary;
        _fallback = fallback;
        _alwaysShowFallback = alwaysShowFallback;
    }

    public async Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        _lastUsedFallback = false;
        _lastPrimaryFailed = false;
        try
        {
            await _primary.ShowAsync(notification, cancellationToken);
            if (_alwaysShowFallback)
            {
                _lastUsedFallback = true;
                await _fallback.ShowAsync(notification, cancellationToken);
            }
        }
        catch
        {
            _lastPrimaryFailed = true;
            _lastUsedFallback = true;
            await _fallback.ShowAsync(notification, cancellationToken);
        }
    }

    public string DeliveryMethodName
    {
        get
        {
            var primary = _primary is IReminderNotifierMetadata primaryMetadata ? primaryMetadata.DeliveryMethodName : _primary.GetType().Name;
            var fallback = _fallback is IReminderNotifierMetadata fallbackMetadata ? fallbackMetadata.DeliveryMethodName : _fallback.GetType().Name;
            return _lastPrimaryFailed
                ? $"{primary} failed -> {fallback}"
                : _lastUsedFallback || _alwaysShowFallback
                    ? $"{primary} + {fallback}"
                    : primary;
        }
    }

    public bool UsedMessageBoxFallback => _lastPrimaryFailed
        && (_fallback is not IReminderNotifierMetadata metadata
            || metadata.MessageBoxRole == MessageBoxNotificationRole.Primary
            || metadata.UsedMessageBoxFallback);
    public MessageBoxNotificationRole MessageBoxRole => _lastPrimaryFailed
        ? MessageBoxNotificationRole.Fallback
        : _lastUsedFallback || _alwaysShowFallback
            ? MessageBoxNotificationRole.AfterToast
            : _primary is IReminderNotifierMetadata metadata
                ? metadata.MessageBoxRole
                : MessageBoxNotificationRole.None;
    public bool ToastVerified => _primary is IReminderNotifierMetadata metadata && metadata.ToastVerified;
    public string? ToastStatus => _primary is IReminderNotifierMetadata metadata ? metadata.ToastStatus : null;
    public ReminderSoundStatus SoundStatus => ReminderSoundStatus.NotConfigured;
    public string? SoundError => null;
}

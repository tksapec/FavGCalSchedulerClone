using System.Windows.Media;

namespace FavGCalSchedulerClone.App.Services;

public sealed class SoundReminderNotifier : IReminderNotifier, IReminderNotifierMetadata
{
    private readonly IReminderNotifier _inner;
    private readonly string? _filePath;
    private readonly double _volume;
    private MediaPlayer? _player;

    public SoundReminderNotifier(IReminderNotifier inner, string? filePath, int volume)
    {
        _inner = inner;
        _filePath = filePath;
        _volume = Math.Clamp(volume, 0, 100) / 100.0;
    }

    public string DeliveryMethodName => _inner is IReminderNotifierMetadata metadata ? $"Sound + {metadata.DeliveryMethodName}" : "Sound";
    public bool UsedMessageBoxFallback => _inner is IReminderNotifierMetadata metadata && metadata.UsedMessageBoxFallback;
    public bool ToastVerified => _inner is IReminderNotifierMetadata metadata && metadata.ToastVerified;
    public string? ToastStatus => _inner is IReminderNotifierMetadata metadata ? metadata.ToastStatus : null;

    public async Task ShowAsync(ReminderNotification notification, CancellationToken cancellationToken = default)
    {
        TryPlay();
        await _inner.ShowAsync(notification, cancellationToken);
    }

    public void Stop()
    {
        _player?.Stop();
        _player?.Close();
        _player = null;
    }

    public void TryPlay()
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            return;
        }

        try
        {
            Stop();
            _player = new MediaPlayer { Volume = _volume };
            _player.Open(new Uri(_filePath, UriKind.Absolute));
            _player.Play();
        }
        catch
        {
            Stop();
        }
    }
}

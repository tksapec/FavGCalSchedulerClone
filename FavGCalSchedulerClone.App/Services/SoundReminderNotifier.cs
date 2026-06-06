using System.Windows.Media;

namespace FavGCalSchedulerClone.App.Services;

public sealed class SoundReminderNotifier : IReminderNotifier, IReminderNotifierMetadata
{
    private readonly IReminderNotifier _inner;
    private readonly string? _filePath;
    private readonly double _volume;
    private readonly Func<string, bool> _fileExists;
    private readonly Action<string, double> _playSound;
    private MediaPlayer? _player;
    private ReminderSoundStatus _lastSoundStatus = ReminderSoundStatus.NotConfigured;
    private string? _lastSoundError;

    public SoundReminderNotifier(IReminderNotifier inner, string? filePath, int volume)
        : this(inner, filePath, volume, File.Exists, null)
    {
    }

    internal SoundReminderNotifier(
        IReminderNotifier inner,
        string? filePath,
        int volume,
        Func<string, bool> fileExists,
        Action<string, double>? playSound)
    {
        _inner = inner;
        _filePath = filePath;
        _volume = Math.Clamp(volume, 0, 100) / 100.0;
        _fileExists = fileExists;
        _playSound = playSound ?? PlayWithMediaPlayer;
    }

    public string DeliveryMethodName => _inner is IReminderNotifierMetadata metadata ? $"Sound + {metadata.DeliveryMethodName}" : "Sound";
    public bool UsedMessageBoxFallback => _inner is IReminderNotifierMetadata metadata && metadata.UsedMessageBoxFallback;
    public MessageBoxNotificationRole MessageBoxRole => _inner is IReminderNotifierMetadata metadata ? metadata.MessageBoxRole : MessageBoxNotificationRole.None;
    public bool ToastVerified => _inner is IReminderNotifierMetadata metadata && metadata.ToastVerified;
    public string? ToastStatus => _inner is IReminderNotifierMetadata metadata ? metadata.ToastStatus : null;
    public ReminderSoundStatus SoundStatus => _lastSoundStatus;
    public string? SoundError => _lastSoundError;

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
        _lastSoundStatus = ReminderSoundStatus.NotConfigured;
        _lastSoundError = null;
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        if (!_fileExists(_filePath))
        {
            _lastSoundStatus = ReminderSoundStatus.MissingFile;
            _lastSoundError = $"通知音ファイルが見つかりません: {_filePath}";
            return;
        }

        try
        {
            _playSound(_filePath, _volume);
            _lastSoundStatus = ReminderSoundStatus.Played;
        }
        catch (Exception ex)
        {
            Stop();
            _lastSoundStatus = ReminderSoundStatus.Failed;
            _lastSoundError = ex.Message;
        }
    }

    private void PlayWithMediaPlayer(string filePath, double volume)
    {
        Stop();
        _player = new MediaPlayer { Volume = volume };
        _player.Open(new Uri(filePath, UriKind.Absolute));
        _player.Play();
    }
}

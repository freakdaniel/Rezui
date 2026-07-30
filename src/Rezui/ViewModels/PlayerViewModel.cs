using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Rezui.Services;

namespace Rezui.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject, IDisposable
{
    private LibVLC? _libVlc;
    private LibVLCSharp.Shared.Media? _media;
    private long _currentTimeMs;

    [ObservableProperty]
    private MediaPlayer? _mediaPlayer;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isReady;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _statusText = "Подготовка проигрывателя…";

    [ObservableProperty]
    private long _durationMs = 1;

    [ObservableProperty]
    private int _volume = 80;

    public long CurrentTimeMs => _currentTimeMs;

    public string CurrentTimeText => FormatTime(_currentTimeMs);

    public string DurationText => FormatTime(DurationMs);

    partial void OnVolumeChanged(int value)
    {
        if (MediaPlayer is not null)
        {
            MediaPlayer.Volume = value;
        }
    }

    partial void OnDurationMsChanged(long value) =>
        OnPropertyChanged(nameof(DurationText));

    public Task PlayAsync(
        string title,
        Uri videoUrl,
        Uri? subtitleUrl,
        Uri referrer)
    {
        Title = title;
        StatusText = "Подключение к потоку…";

        EnsureInitialized();
        var media = new LibVLCSharp.Shared.Media(_libVlc!, videoUrl);
        media.AddOption($":http-user-agent={HdRezka.ClientOptions.DefaultUserAgent}");
        media.AddOption($":http-referrer={referrer.AbsoluteUri}");
        media.AddOption(":network-caching=1500");
        if (subtitleUrl is not null)
        {
            media.AddOption($":sub-file={subtitleUrl.AbsoluteUri}");
        }

        var previous = Interlocked.Exchange(ref _media, media);
        previous?.Dispose();

        IsReady = MediaPlayer!.Play(media);
        IsPlaying = IsReady;
        StatusText = IsReady
            ? "Воспроизведение"
            : "Не удалось запустить поток";
        return Task.CompletedTask;
    }

    public void Seek(long timeMs)
    {
        if (MediaPlayer is null || DurationMs <= 1)
        {
            return;
        }

        MediaPlayer.Time = Math.Clamp(timeMs, 0, DurationMs);
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (MediaPlayer is null)
        {
            return;
        }

        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Pause();
            IsPlaying = false;
            StatusText = "Пауза";
        }
        else
        {
            MediaPlayer.Play();
            IsPlaying = true;
            StatusText = "Воспроизведение";
        }
    }

    [RelayCommand]
    private void SkipBack() => Seek(_currentTimeMs - 10_000);

    [RelayCommand]
    private void SkipForward() => Seek(_currentTimeMs + 10_000);

    [RelayCommand]
    private void Stop()
    {
        MediaPlayer?.Stop();
        IsPlaying = false;
        IsReady = false;
        StatusText = "Остановлено";
        UpdateTime(0);
    }

    private void EnsureInitialized()
    {
        if (MediaPlayer is not null)
        {
            return;
        }

        LibVlcRuntime.Initialize();
        _libVlc = new LibVLC("--no-video-title-show", "--quiet");
        MediaPlayer = new MediaPlayer(_libVlc)
        {
            Volume = Volume,
            EnableHardwareDecoding = true
        };
        MediaPlayer.TimeChanged += (_, eventArgs) =>
            Dispatcher.UIThread.Post(() => UpdateTime(eventArgs.Time));
        MediaPlayer.LengthChanged += (_, eventArgs) =>
            Dispatcher.UIThread.Post(() => DurationMs = Math.Max(1, eventArgs.Length));
        MediaPlayer.Playing += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                IsPlaying = true;
                IsReady = true;
                StatusText = "Воспроизведение";
            });
        MediaPlayer.Paused += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                IsPlaying = false;
                StatusText = "Пауза";
            });
        MediaPlayer.EndReached += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                IsPlaying = false;
                StatusText = "Просмотр завершён";
            });
        MediaPlayer.EncounteredError += (_, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                IsPlaying = false;
                StatusText = "Ошибка проигрывателя";
            });
    }

    private void UpdateTime(long value)
    {
        _currentTimeMs = Math.Max(0, value);
        OnPropertyChanged(nameof(CurrentTimeMs));
        OnPropertyChanged(nameof(CurrentTimeText));
    }

    private static string FormatTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    public void Dispose()
    {
        _media?.Dispose();
        MediaPlayer?.Dispose();
        _libVlc?.Dispose();
    }
}

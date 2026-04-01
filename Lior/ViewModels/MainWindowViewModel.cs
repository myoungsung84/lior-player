using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lior.Models;
using Lior.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace Lior.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IPlayerService _playerService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly DispatcherTimer _playbackTimer;
    private bool _isUpdatingFromPlayer;
    private bool _isSeekInteractionActive;
    private DateTime _seekSyncSuppressedUntilUtc = DateTime.MinValue;
    private double? _pendingSeekPositionSeconds;
    private double _previousVolumeBeforeMute = 70;

    [ObservableProperty]
    private string currentMediaPath = "No media selected";

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private PlaybackState playbackState = PlaybackState.None;

    [ObservableProperty]
    private string elapsedText = "0:00";

    [ObservableProperty]
    private string durationText = "--:--";

    [ObservableProperty]
    private double volumeLevel = 70;

    [ObservableProperty]
    private double elapsedSeconds;

    [ObservableProperty]
    private double durationSeconds = 1;

    [ObservableProperty]
    private bool isSeekAvailable;

    [ObservableProperty]
    private bool isMuted;

    public bool HasSelectedMedia => !string.IsNullOrWhiteSpace(_playerService.CurrentMediaPath);

    public bool CanPlay => HasSelectedMedia && PlaybackState is not PlaybackState.Playing;

    public bool CanPause => PlaybackState is PlaybackState.Playing;

    public bool CanStop => HasSelectedMedia && PlaybackState is not PlaybackState.None and not PlaybackState.Stopped;

    public bool IsPaused => PlaybackState is PlaybackState.Paused;

    public string MuteButtonText => IsMuted ? "🔇" : "🔊";

    public string MediaTitle =>
        string.IsNullOrWhiteSpace(_playerService.CurrentMediaPath)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(_playerService.CurrentMediaPath);

    public MainWindowViewModel(
        IFileDialogService fileDialogService,
        IPlayerService playerService,
        ILogger<MainWindowViewModel> logger)
    {
        _fileDialogService = fileDialogService;
        _playerService = playerService;
        _logger = logger;
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        VolumeLevel = _playerService.Volume;
        _previousVolumeBeforeMute = VolumeLevel > 0 ? VolumeLevel : 70;

        SyncFromPlayer("Ready");
        _playbackTimer.Start();
    }

    [RelayCommand]
    private void OpenFile()
    {
        try
        {
            var selectedPath = _fileDialogService.OpenMediaFile();
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                StatusText = "File selection canceled";
                return;
            }

            if (!_playerService.Load(selectedPath))
            {
                SyncFromPlayer("Failed to load media");
                return;
            }

            SyncFromPlayer("Media loaded");
            _logger.LogInformation("Media selected: {MediaPath}", selectedPath);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while opening a media file.");
            SyncFromPlayer("Unable to open media");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play()
    {
        try
        {
            var played = _playerService.Play();
            SyncFromPlayer(played ? "Playback started" : "No media loaded");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while starting playback.");
            SyncFromPlayer("Playback failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        try
        {
            var paused = _playerService.Pause();
            SyncFromPlayer(paused ? "Playback paused" : "Nothing is playing");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while pausing playback.");
            SyncFromPlayer("Pause failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        try
        {
            var stopped = _playerService.Stop();
            SyncFromPlayer(stopped ? "Playback stopped" : "Nothing to stop");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while stopping playback.");
            SyncFromPlayer("Stop failed");
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        try
        {
            if (IsMuted)
            {
                var restoreVolume = _previousVolumeBeforeMute > 0 ? _previousVolumeBeforeMute : Math.Max(VolumeLevel, 50);
                _isUpdatingFromPlayer = true;
                VolumeLevel = Math.Clamp(restoreVolume, 0, 100);
                _isUpdatingFromPlayer = false;

                if (!_playerService.SetVolume(VolumeLevel) || !_playerService.SetMuted(false))
                {
                    StatusText = "Unmute failed";
                    return;
                }

                IsMuted = false;
                StatusText = "Audio unmuted";
                return;
            }

            if (VolumeLevel > 0)
            {
                _previousVolumeBeforeMute = VolumeLevel;
            }

            if (!_playerService.SetMuted(true))
            {
                StatusText = "Mute failed";
                return;
            }

            IsMuted = true;
            StatusText = "Audio muted";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while toggling mute.");
            StatusText = "Mute toggle failed";
        }
    }

    partial void OnCurrentMediaPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(MediaTitle));
        RefreshCommandStates();
    }

    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsPaused));
        RefreshCommandStates();
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MuteButtonText));
    }

    partial void OnVolumeLevelChanged(double value)
    {
        if (_isUpdatingFromPlayer)
        {
            return;
        }

        try
        {
            if (value > 0)
            {
                _previousVolumeBeforeMute = value;
            }

            if (IsMuted)
            {
                if (!_playerService.SetMuted(false))
                {
                    _logger.LogWarning("Mute state could not be cleared before volume change.");
                }

                IsMuted = false;
            }

            if (!_playerService.SetVolume(value))
            {
                _logger.LogWarning("Volume change was not applied.");
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while updating volume.");
        }
    }

    private void SyncFromPlayer(string statusText)
    {
        CurrentMediaPath = _playerService.CurrentMediaPath ?? string.Empty;
        PlaybackState = _playerService.State;
        StatusText = statusText;
        SyncPlaybackMetrics();
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(MediaTitle));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(MuteButtonText));
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    public void BeginSeekInteraction()
    {
        _isSeekInteractionActive = true;
    }

    public void UpdateSeekPreview(double value)
    {
        if (!_isSeekInteractionActive)
        {
            return;
        }

        ElapsedSeconds = ClampSeekPosition(value);
        ElapsedText = FormatPlaybackTime(ElapsedSeconds);
    }

    public void CommitSeekInteraction(double value)
    {
        if (!_isSeekInteractionActive)
        {
            return;
        }

        _isSeekInteractionActive = false;

        if (!HasSelectedMedia || !IsSeekAvailable)
        {
            SyncPlaybackMetrics();
            return;
        }

        var targetPosition = ClampSeekPosition(value);
        ElapsedSeconds = targetPosition;
        ElapsedText = FormatPlaybackTime(targetPosition);
        _pendingSeekPositionSeconds = targetPosition;
        _seekSyncSuppressedUntilUtc = DateTime.UtcNow.AddMilliseconds(700);

        try
        {
            if (!_playerService.Seek(targetPosition))
            {
                StatusText = "Seek unavailable";
                _pendingSeekPositionSeconds = null;
                _seekSyncSuppressedUntilUtc = DateTime.MinValue;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while seeking playback.");
            StatusText = "Seek failed";
            _pendingSeekPositionSeconds = null;
            _seekSyncSuppressedUntilUtc = DateTime.MinValue;
        }

        SyncPlaybackMetrics();
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        SyncPlaybackMetrics();
    }

    private void SyncPlaybackMetrics()
    {
        try
        {
            var duration = _playerService.GetDurationSeconds();
            var position = _playerService.GetPositionSeconds();
            var now = DateTime.UtcNow;

            DurationSeconds = duration > 0 ? duration : 1;
            IsSeekAvailable = HasSelectedMedia && duration > 0;
            DurationText = duration > 0 ? FormatPlaybackTime(duration) : "--:--";

            if (!_isSeekInteractionActive)
            {
                var clampedPosition = ClampSeekPosition(position);
                var shouldHoldPendingSeek =
                    _pendingSeekPositionSeconds.HasValue &&
                    now < _seekSyncSuppressedUntilUtc &&
                    Math.Abs(clampedPosition - _pendingSeekPositionSeconds.Value) > 1;

                if (!shouldHoldPendingSeek)
                {
                    ElapsedSeconds = clampedPosition;
                    ElapsedText = FormatPlaybackTime(clampedPosition);
                    _pendingSeekPositionSeconds = null;
                }
            }

            _isUpdatingFromPlayer = true;
            VolumeLevel = _playerService.Volume;
            IsMuted = _playerService.IsMuted;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Playback metrics sync failed.");
        }
        finally
        {
            _isUpdatingFromPlayer = false;
        }
    }

    private double ClampSeekPosition(double value)
    {
        var maxDuration = DurationSeconds;
        if (maxDuration <= 0)
        {
            return 0;
        }

        return Math.Clamp(value, 0, maxDuration);
    }

    private static string FormatPlaybackTime(double seconds)
    {
        if (seconds <= 0)
        {
            return "0:00";
        }

        var time = TimeSpan.FromSeconds(seconds);

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time:mm\\:ss}"
            : $"{(int)time.TotalMinutes}:{time:ss}";
    }

    public void TogglePlayPause()
    {
        if (PlaybackState is PlaybackState.Playing)
        {
            Pause();
            return;
        }

        Play();
    }

    public void SeekRelative(double offsetSeconds)
    {
        if (!HasSelectedMedia || !IsSeekAvailable)
        {
            return;
        }

        BeginSeekInteraction();
        CommitSeekInteraction(ClampSeekPosition(ElapsedSeconds + offsetSeconds));
    }

    public void AdjustVolume(double delta)
    {
        var nextVolume = Math.Clamp(VolumeLevel + delta, 0, 100);
        VolumeLevel = nextVolume;
    }
}

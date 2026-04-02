using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lior.Models;
using Lior.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace Lior.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IMediaFileCatalogService _mediaFileCatalogService;
    private readonly IPlayerService _playerService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly DispatcherTimer _playbackTimer;
    private bool _isUpdatingFromPlayer;
    private bool _isSeekInteractionActive;
    private DateTime _seekSyncSuppressedUntilUtc = DateTime.MinValue;
    private double? _pendingSeekPositionSeconds;
    private double _previousVolumeBeforeMute = 70;
    private bool _isHandlingPlaybackEnded;

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

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private int currentPlaylistIndex = -1;

    [ObservableProperty]
    private string currentFolderPath = string.Empty;

    [ObservableProperty]
    private bool isFilePanelOpen;

    [ObservableProperty]
    private MediaFileItem? selectedMediaFile;

    public ObservableCollection<MediaFileItem> MediaFiles { get; } = [];

    public bool HasSelectedMedia => !string.IsNullOrWhiteSpace(_playerService.CurrentMediaPath);

    public bool HasPlaylist => MediaFiles.Count > 0;

    public int PlaylistCount => MediaFiles.Count;

    public bool CanPlay => HasSelectedMedia && PlaybackState is not PlaybackState.Playing;

    public bool CanPause => PlaybackState is PlaybackState.Playing;

    public bool CanStop => HasSelectedMedia && PlaybackState is not PlaybackState.None and not PlaybackState.Stopped;

    public bool CanTogglePlayback => HasSelectedMedia;

    public bool CanPlayPrevious => CurrentPlaylistIndex > 0 && PlaylistCount > 0;

    public bool CanPlayNext => CurrentPlaylistIndex >= 0 && CurrentPlaylistIndex < PlaylistCount - 1;

    public bool IsPaused => PlaybackState is PlaybackState.Paused;

    public bool IsPlaying => PlaybackState is PlaybackState.Playing;

    public string MuteButtonText => IsMuted ? "🔇" : "🔊";

    public string TogglePlaybackGlyph => IsPlaying ? "\uE769" : "\uE768";

    public string TogglePlaybackToolTip => IsPlaying ? "일시정지" : "재생";

    public string WindowTitle => string.IsNullOrWhiteSpace(MediaTitle) ? "Lior" : $"{MediaTitle} - Lior";

    public string CurrentFileDisplayName => string.IsNullOrWhiteSpace(MediaTitle) ? "No media selected" : MediaTitle;

    public string PlaylistPositionText =>
        CurrentPlaylistIndex >= 0 && PlaylistCount > 0
            ? $"{CurrentPlaylistIndex + 1} / {PlaylistCount}"
            : "- / -";

    public string CurrentFolderDisplayName => string.IsNullOrWhiteSpace(CurrentFolderPath) ? "No folder loaded" : CurrentFolderPath;

    public string FilePanelToggleGlyph => IsFilePanelOpen ? "\uE70D" : "\uE700";

    public string FilePanelToggleToolTip => IsFilePanelOpen ? "파일 패널 닫기" : "파일 패널 열기";

    public string MediaTitle =>
        string.IsNullOrWhiteSpace(_playerService.CurrentMediaPath)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(_playerService.CurrentMediaPath);

    public MainWindowViewModel(
        IFileDialogService fileDialogService,
        IMediaFileCatalogService mediaFileCatalogService,
        IPlayerService playerService,
        ILogger<MainWindowViewModel> logger)
    {
        _fileDialogService = fileDialogService;
        _mediaFileCatalogService = mediaFileCatalogService;
        _playerService = playerService;
        _logger = logger;
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;
        _playerService.PlaybackEnded += OnPlayerPlaybackEnded;

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

            var mediaFiles = _mediaFileCatalogService.GetMediaFilesInFolder(selectedPath);
            if (mediaFiles.Count == 0)
            {
                SyncFromPlayer("No playable files found in folder");
                return;
            }

            MediaFiles.Clear();
            foreach (var mediaFile in mediaFiles)
            {
                MediaFiles.Add(new MediaFileItem
                {
                    FilePath = mediaFile,
                    FileName = Path.GetFileName(mediaFile)
                });
            }

            CurrentFolderPath = System.IO.Path.GetDirectoryName(selectedPath) ?? string.Empty;
            OnPropertyChanged(nameof(PlaylistCount));
            OnPropertyChanged(nameof(PlaylistPositionText));
            OnPropertyChanged(nameof(HasPlaylist));
            OnPropertyChanged(nameof(CurrentFolderDisplayName));

            CurrentPlaylistIndex = MediaFiles
                .Select((mediaFile, index) => new { mediaFile.FilePath, Index = index })
                .FirstOrDefault(item => string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                ?.Index ?? -1;
            if (CurrentPlaylistIndex < 0)
            {
                CurrentPlaylistIndex = 0;
            }

            if (!TryPlayPlaylistIndex(CurrentPlaylistIndex, $"Playing {CurrentPlaylistIndex + 1} of {PlaylistCount}"))
            {
                return;
            }

            _logger.LogInformation(
                "Folder media list loaded with {Count} items. Current item: {MediaPath}",
                PlaylistCount,
                MediaFiles[CurrentPlaylistIndex].FilePath);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "An error occurred while opening a media file.");
            SyncFromPlayer("Unable to open media");
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void ToggleFilePanel()
    {
        IsFilePanelOpen = !IsFilePanelOpen;
    }

    [RelayCommand]
    private void PlayMediaFile(MediaFileItem? mediaFile)
    {
        if (mediaFile is null)
        {
            return;
        }

        var targetIndex = MediaFiles.IndexOf(mediaFile);
        if (targetIndex < 0)
        {
            return;
        }

        TryPlayPlaylistIndex(targetIndex, $"Playing {targetIndex + 1} of {PlaylistCount}");
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

    [RelayCommand(CanExecute = nameof(CanPlayPrevious))]
    private void PlayPrevious()
    {
        if (!CanPlayPrevious)
        {
            return;
        }

        var targetIndex = CurrentPlaylistIndex - 1;
        TryPlayPlaylistIndex(targetIndex, $"Playing {targetIndex + 1} of {PlaylistCount}");
    }

    [RelayCommand(CanExecute = nameof(CanPlayNext))]
    private void PlayNext()
    {
        if (!CanPlayNext)
        {
            return;
        }

        var targetIndex = CurrentPlaylistIndex + 1;
        TryPlayPlaylistIndex(targetIndex, $"Playing {targetIndex + 1} of {PlaylistCount}");
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
        OnPropertyChanged(nameof(CurrentFileDisplayName));
        OnPropertyChanged(nameof(WindowTitle));
        RefreshCommandStates();
    }

    partial void OnCurrentPlaylistIndexChanged(int value)
    {
        OnPropertyChanged(nameof(HasPlaylist));
        OnPropertyChanged(nameof(PlaylistCount));
        OnPropertyChanged(nameof(PlaylistPositionText));
        RefreshCommandStates();
    }

    partial void OnIsFilePanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(FilePanelToggleGlyph));
        OnPropertyChanged(nameof(FilePanelToggleToolTip));
    }

    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(TogglePlaybackGlyph));
        OnPropertyChanged(nameof(TogglePlaybackToolTip));
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
        OnPropertyChanged(nameof(CurrentFileDisplayName));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(TogglePlaybackGlyph));
        OnPropertyChanged(nameof(TogglePlaybackToolTip));
        OnPropertyChanged(nameof(MuteButtonText));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(PlaylistPositionText));
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        TogglePlayPauseCommand.NotifyCanExecuteChanged();
        ToggleFilePanelCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        PlayPreviousCommand.NotifyCanExecuteChanged();
        PlayNextCommand.NotifyCanExecuteChanged();
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

    private void OnPlayerPlaybackEnded(object? sender, EventArgs e)
    {
        if (_isHandlingPlaybackEnded)
        {
            return;
        }

        _isHandlingPlaybackEnded = true;

        try
        {
            if (CanPlayNext)
            {
                var nextIndex = CurrentPlaylistIndex + 1;
                TryPlayPlaylistIndex(nextIndex, $"Playing {nextIndex + 1} of {PlaylistCount}");
                return;
            }

            SyncFromPlayer("Playback finished");
        }
        finally
        {
            _isHandlingPlaybackEnded = false;
        }
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

    [RelayCommand(CanExecute = nameof(CanTogglePlayback))]
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

    private bool TryPlayPlaylistIndex(int targetIndex, string statusText)
    {
        if (targetIndex < 0 || targetIndex >= PlaylistCount)
        {
            SyncFromPlayer("File index out of range");
            return false;
        }

        var selectedPath = MediaFiles[targetIndex].FilePath;
        if (!_playerService.Load(selectedPath))
        {
            SyncFromPlayer("Failed to load media");
            return false;
        }

        if (!_playerService.Play())
        {
            SyncFromPlayer("Media loaded");
            return false;
        }

        CurrentPlaylistIndex = targetIndex;
        SelectedMediaFile = MediaFiles[targetIndex];
        SyncFromPlayer(statusText);
        return true;
    }
}

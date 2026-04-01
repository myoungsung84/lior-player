using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lior.Models;
using Lior.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Lior.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IPlayerService _playerService;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty]
    private string currentMediaPath = "No media selected";

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private PlaybackState playbackState = PlaybackState.None;

    public bool HasSelectedMedia => !string.IsNullOrWhiteSpace(_playerService.CurrentMediaPath);

    public bool CanPlay => HasSelectedMedia && PlaybackState is not PlaybackState.Playing;

    public bool CanPause => PlaybackState is PlaybackState.Playing;

    public bool CanStop => HasSelectedMedia && PlaybackState is not PlaybackState.None and not PlaybackState.Stopped;

    public MainWindowViewModel(
        IFileDialogService fileDialogService,
        IPlayerService playerService,
        ILogger<MainWindowViewModel> logger)
    {
        _fileDialogService = fileDialogService;
        _playerService = playerService;
        _logger = logger;

        SyncFromPlayer("Ready");
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

    partial void OnCurrentMediaPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedMedia));
        RefreshCommandStates();
    }

    partial void OnPlaybackStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        RefreshCommandStates();
    }

    private void SyncFromPlayer(string statusText)
    {
        CurrentMediaPath = _playerService.CurrentMediaPath ?? "No media selected";
        PlaybackState = _playerService.State;
        StatusText = statusText;
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}

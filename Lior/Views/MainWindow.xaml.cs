using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lior.Services.Interfaces;
using Lior.ViewModels;

namespace Lior.Views;

public partial class MainWindow : Window
{
    private readonly IPlayerService _playerService;
    private readonly DispatcherTimer _surfaceClickTimer;
    private bool _renderTargetAssigned;
    private MainWindowViewModel? _pendingSurfaceClickViewModel;

    public MainWindow(MainWindowViewModel viewModel, IPlayerService playerService)
    {
        _playerService = playerService;
        _surfaceClickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _surfaceClickTimer.Tick += OnSurfaceClickTimerTick;
        InitializeComponent();
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;

        SeekSlider.PreviewMouseLeftButtonDown += OnSeekSliderPreviewMouseLeftButtonDown;
        SeekSlider.ValueChanged += OnSeekSliderValueChanged;
        SeekSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnSeekDragStarted));
        SeekSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnSeekDragCompleted));

        VolumeSlider.PreviewMouseLeftButtonDown += OnVolumeSliderPreviewMouseLeftButtonDown;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        AttachRenderTarget();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachRenderTarget();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        AttachRenderTarget();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _playerService.Shutdown();
    }

    private void OnVideoSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            _surfaceClickTimer.Stop();
            _pendingSurfaceClickViewModel = null;

            if (viewModel.OpenFileCommand.CanExecute(null))
            {
                viewModel.OpenFileCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        _pendingSurfaceClickViewModel = viewModel;
        _surfaceClickTimer.Stop();
        _surfaceClickTimer.Start();
        e.Handled = true;
    }

    private void OnSurfaceClickTimerTick(object? sender, EventArgs e)
    {
        _surfaceClickTimer.Stop();

        var viewModel = _pendingSurfaceClickViewModel;
        _pendingSurfaceClickViewModel = null;

        viewModel?.TogglePlayPause();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                viewModel.TogglePlayPause();
                e.Handled = true;
                break;
            case Key.Left:
                viewModel.SeekRelative(-5);
                e.Handled = true;
                break;
            case Key.Right:
                viewModel.SeekRelative(5);
                e.Handled = true;
                break;
            case Key.Up:
                viewModel.AdjustVolume(5);
                e.Handled = true;
                break;
            case Key.Down:
                viewModel.AdjustVolume(-5);
                e.Handled = true;
                break;
            case Key.M:
                viewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnSeekSliderPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || IsThumbInteraction(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var clickedValue = GetSeekValueFromTrackClick(e);
        if (!clickedValue.HasValue)
        {
            return;
        }

        viewModel.BeginSeekInteraction();
        viewModel.UpdateSeekPreview(clickedValue.Value);
        viewModel.CommitSeekInteraction(clickedValue.Value);
        e.Handled = true;
    }

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.BeginSeekInteraction();
        }
    }

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CommitSeekInteraction(SeekSlider.Value);
        }
    }

    private void OnSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.UpdateSeekPreview(e.NewValue);
        }
    }

    private void OnVolumeSliderPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || IsThumbInteraction(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var clickedValue = GetSliderValueFromTrackClick(VolumeSlider, e);
        if (!clickedValue.HasValue)
        {
            return;
        }

        viewModel.VolumeLevel = clickedValue.Value;
        e.Handled = true;
    }

    private static bool IsThumbInteraction(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private double? GetSeekValueFromTrackClick(MouseButtonEventArgs eventArgs)
    {
        return GetSliderValueFromTrackClick(SeekSlider, eventArgs);
    }

    private double? GetSliderValueFromTrackClick(Slider slider, MouseButtonEventArgs eventArgs)
    {
        var track = FindVisualChild<Track>(slider);
        if (track is null || track.ActualWidth <= 0)
        {
            return null;
        }

        var position = eventArgs.GetPosition(track);
        var ratio = Math.Clamp(position.X / track.ActualWidth, 0, 1);
        var range = slider.Maximum - slider.Minimum;

        return slider.Minimum + (range * ratio);
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void AttachRenderTarget()
    {
        if (_renderTargetAssigned)
        {
            return;
        }

        if (VideoHost.WindowHandle != nint.Zero)
        {
            _playerService.SetRenderTarget(VideoHost.WindowHandle);
            _renderTargetAssigned = true;
            return;
        }

        Dispatcher.BeginInvoke(
            AttachRenderTarget,
            DispatcherPriority.Loaded);
    }
}

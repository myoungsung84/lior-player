using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
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
    private HwndSource? _windowSource;
    private bool _isFullscreen;
    private WindowState _windowStateBeforeFullscreen = WindowState.Normal;
    private Rect _boundsBeforeFullscreen = Rect.Empty;
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
        _windowSource = (HwndSource?)PresentationSource.FromVisual(this);
        _windowSource?.AddHook(WndProc);
        UpdateMaximizedPadding();
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
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WndProc);
        }

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
            case Key.Escape when _isFullscreen:
                ExitFullscreen();
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

    private void OnTitleDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            e.Handled = true;
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        Activate();

        if (WindowState == WindowState.Maximized)
        {
            BeginDragMoveFromMaximized(e);
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnFullscreenButtonClick(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        UpdateMaximizedPadding();
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

    private void ToggleMaximizeRestore()
    {
        if (_isFullscreen)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void BeginDragMoveFromMaximized(MouseButtonEventArgs eventArgs)
    {
        var mousePosition = eventArgs.GetPosition(this);
        var widthRatio = ActualWidth > 0 ? mousePosition.X / ActualWidth : 0.5;
        var restoreWidth = RestoreBounds.Width > MinWidth ? RestoreBounds.Width : Width;
        var targetLeft = PointToScreen(mousePosition).X - (restoreWidth * widthRatio);

        WindowState = WindowState.Normal;
        Left = targetLeft;
        Top = 0;
        DragMove();
        eventArgs.Handled = true;
    }

    private void UpdateMaximizedPadding()
    {
        if (_isFullscreen)
        {
            RootLayout.Margin = new Thickness(0);
            return;
        }

        RootLayout.Margin = WindowState == WindowState.Maximized
            ? new Thickness(8)
            : new Thickness(0);
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            ExitFullscreen();
            return;
        }

        EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        var monitor = NativeMethods.MonitorFromWindow(new WindowInteropHelper(this).Handle, NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return;
        }

        var monitorInfo = new NativeMethods.MonitorInfo();
        if (!NativeMethods.GetMonitorInfo(monitor, monitorInfo))
        {
            return;
        }

        _isFullscreen = true;
        _windowStateBeforeFullscreen = WindowState;
        _boundsBeforeFullscreen = new Rect(Left, Top, Width, Height);

        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;
        TitleBarRoot.Visibility = Visibility.Collapsed;
        Left = monitorInfo.MonitorArea.Left;
        Top = monitorInfo.MonitorArea.Top;
        Width = monitorInfo.MonitorArea.Right - monitorInfo.MonitorArea.Left;
        Height = monitorInfo.MonitorArea.Bottom - monitorInfo.MonitorArea.Top;
        FullscreenGlyph.Text = "\uE73F";
        UpdateMaximizedPadding();
    }

    private void ExitFullscreen()
    {
        _isFullscreen = false;
        ResizeMode = ResizeMode.CanResize;
        TitleBarRoot.Visibility = Visibility.Visible;

        if (_boundsBeforeFullscreen != Rect.Empty)
        {
            Left = _boundsBeforeFullscreen.Left;
            Top = _boundsBeforeFullscreen.Top;
            Width = _boundsBeforeFullscreen.Width;
            Height = _boundsBeforeFullscreen.Height;
        }

        WindowState = _windowStateBeforeFullscreen;
        FullscreenGlyph.Text = "\uE740";
        UpdateMaximizedPadding();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WmGetMinMaxInfoMessage = 0x0024;

        if (msg == WmGetMinMaxInfoMessage)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }

        return nint.Zero;
    }

    private static void WmGetMinMaxInfo(nint hwnd, nint lParam)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return;
        }

        var monitorInfo = new NativeMethods.MonitorInfo();
        NativeMethods.GetMonitorInfo(monitor, monitorInfo);
        var rcWorkArea = monitorInfo.WorkArea;
        var rcMonitorArea = monitorInfo.MonitorArea;

        var minMaxInfo = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
        minMaxInfo.MaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
        minMaxInfo.MaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
        minMaxInfo.MaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
        System.Runtime.InteropServices.Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private static class NativeMethods
    {
        public const int MonitorDefaultToNearest = 0x00000002;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern nint MonitorFromWindow(nint hwnd, int dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint hMonitor, MonitorInfo lpmi);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MinMaxInfo
        {
            public Point Reserved;
            public Point MaxSize;
            public Point MaxPosition;
            public Point MinTrackSize;
            public Point MaxTrackSize;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public sealed class MonitorInfo
        {
            public int Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>();
            public RectStruct MonitorArea;
            public RectStruct WorkArea;
            public int Flags;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RectStruct
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}

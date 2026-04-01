using System.Windows;
using System.Windows.Threading;
using Lior.Services.Interfaces;
using Lior.ViewModels;

namespace Lior.Views;

public partial class MainWindow : Window
{
    private readonly IPlayerService _playerService;
    private bool _renderTargetAssigned;

    public MainWindow(MainWindowViewModel viewModel, IPlayerService playerService)
    {
        _playerService = playerService;
        InitializeComponent();
        DataContext = viewModel;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        Closed += OnClosed;
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

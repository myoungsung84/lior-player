using System.Windows;
using System.IO;
using Lior.Infrastructure.Hosting;
using Lior.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lior;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddLiorApplication(context.Configuration);
            })
            .Build();

        await _host.StartAsync();

        try
        {
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (FileNotFoundException exception)
        {
            MessageBox.Show(
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Path: {exception.FileName}",
                "Lior startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
        catch (DllNotFoundException exception)
        {
            MessageBox.Show(
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Make sure mpv-2.dll is available before running the app.",
                "Lior startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}

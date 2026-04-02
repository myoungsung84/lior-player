using Lior.Options;
using Lior.Services;
using Lior.Services.Interfaces;
using Lior.ViewModels;
using Lior.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lior.Infrastructure.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiorApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlayerOptions>(configuration.GetSection(PlayerOptions.SectionName));

        services.AddSingleton<MpvPlayerService>();
        services.AddSingleton<IPlayerService>(serviceProvider => serviceProvider.GetRequiredService<MpvPlayerService>());
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IMediaFileCatalogService, MediaFileCatalogService>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}

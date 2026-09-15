using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RecortadorDeVideos.Infrastructure;
using RecortadorDeVideos.UI.ViewModels;
using RecortadorDeVideos.UI.Views;

namespace RecortadorDeVideos.UI;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        _serviceProvider = serviceCollection.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Registrar todos los servicios de Infraestructura y Casos de Uso
        services.AddRecortadorServices();

        // Registrar ViewModels y Vistas
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}

using System.IO;
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
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "app_started.log"), $"OnStartup executed at {DateTime.Now}");
        base.OnStartup(e);

        DispatcherUnhandledException += (s, ev) =>
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "dispatcher_error.log"), ev.Exception.ToString());
            }
            catch { }
            ev.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "domain_error.log"), ev.ExceptionObject?.ToString());
        };

        try
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            _serviceProvider = serviceCollection.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            
            mainWindow.Closing += (s, ev) =>
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "app_started.log"), $"\nMainWindow.Closing fired, cancel={ev.Cancel}");
            };

            mainWindow.Closed += (s, ev) =>
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "app_started.log"), "\nMainWindow.Closed fired");
            };

            mainWindow.Show();
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "app_started.log"), "\nmainWindow.Show() returned successfully");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup_error.log"), ex.ToString());
            MessageBox.Show($"Error al iniciar la aplicación: {ex.Message}\n{ex.StackTrace}", "Error de Inicio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "app_started.log"), $"\nOnExit fired with code: {e.ApplicationExitCode}");
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}

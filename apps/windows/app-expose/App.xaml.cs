using System.Threading;
using System.Windows;

namespace PlebTools.AppExpose;

public partial class App : System.Windows.Application
{
    private const string MutexName = "PlebTools.AppExpose.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "App Exposé is already running in the notification area.",
                "App Exposé",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _controller = new AppController();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "App Exposé could not start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

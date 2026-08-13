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
            SignalReady(e.Args);
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

    private static void SignalReady(string[] args)
    {
        int optionIndex = Array.IndexOf(args, "--ready-event");
        if (optionIndex < 0 || optionIndex + 1 >= args.Length)
        {
            return;
        }

        try
        {
            using EventWaitHandle readyEvent = EventWaitHandle.OpenExisting(args[optionIndex + 1]);
            readyEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The installer may have exited while the application was starting.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

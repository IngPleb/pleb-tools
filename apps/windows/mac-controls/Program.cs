using System.Threading;

namespace PlebTools.MacControls;

internal static class Program
{
    private const string MutexName = "PlebTools.MacControls.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Mac Controls is already running in the notification area.",
                "Mac Controls",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            var context = new MacControlsContext();
            SignalReady(args);
            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Mac Controls could not start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
}

using System.Drawing;
using System.Windows;
using System.Windows.Threading;

namespace PlebTools.AppExpose;

/// <summary>
/// Owns the global shortcut, notification-area lifecycle, and one active overview window.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly RightWindowsMinusShortcut _shortcut;
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private OverviewWindow? _overview;

    public AppController()
    {
        _shortcut = new RightWindowsMinusShortcut();
        _shortcut.Pressed += ShowOverview;

        if (!_shortcut.Start())
        {
            throw new InvalidOperationException(
                "The Right Windows + - shortcut hook could not be installed. Restart App Exposé and try again.");
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show App Exposé    Right Win + -", null, (_, _) => ShowOverview());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "App Exposé — Right Win + -",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowOverview();
    }

    private void ShowOverview()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Send,
            () =>
            {
                if (_overview is { IsVisible: true })
                {
                    _overview.Close();
                    return;
                }

                nint focusedWindow = NativeMethods.GetForegroundWindow();
                IReadOnlyList<AppWindow> windows = WindowDiscovery.FindSiblings(focusedWindow);
                if (windows.Count == 0)
                {
                    return;
                }

                _overview = new OverviewWindow(windows, focusedWindow);
                _overview.Closed += (_, _) => _overview = null;
                _overview.Show();
                _overview.Activate();
            });
    }

    public void Dispose()
    {
        _overview?.Close();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _shortcut.Dispose();
    }
}

using System.Drawing;

namespace PlebTools.MacControls;

/// <summary>
/// Owns the keyboard hook and notification-area lifecycle.
/// </summary>
internal sealed class MacControlsContext : ApplicationContext
{
    private readonly MacKeyboardHook _keyboardHook;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _enabledItem;
    private bool _enabled = true;

    public MacControlsContext()
    {
        PowerToysConflictReport conflicts = PowerToysConflictDetector.Detect();
        if (conflicts.Conflicts.Count > 0)
        {
            string details = string.Join(Environment.NewLine, conflicts.Conflicts.Select(item => $"- {item}"));
            throw new InvalidOperationException(
                $"PowerToys still owns keyboard mappings that overlap Mac Controls:{Environment.NewLine}{Environment.NewLine}" +
                $"{details}{Environment.NewLine}{Environment.NewLine}" +
                "Run install.ps1 -Apply from the Mac Controls folder. It backs up the active PowerToys profile and removes only the overlapping entries.");
        }

        _keyboardHook = new MacKeyboardHook();
        _enabledItem = new ToolStripMenuItem("Enabled")
        {
            Checked = true,
            CheckOnClick = true,
        };
        _enabledItem.CheckedChanged += (_, _) => SetEnabled(_enabledItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add("Shortcut reference", null, (_, _) => ShowShortcutReference());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Text = "Mac Controls: enabled",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowShortcutReference();

        // Install the global hook only after every fallible tray/UI object has
        // been created, so a constructor failure cannot leave an orphaned hook.
        if (!_keyboardHook.Start())
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _keyboardHook.Dispose();
            throw new InvalidOperationException(
                "The global keyboard hook could not be installed. Restart Mac Controls and try again.");
        }
    }

    private void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            if (!_keyboardHook.Start())
            {
                _enabled = false;
                _enabledItem.Checked = false;
                MessageBox.Show(
                    "The keyboard hook could not be re-enabled.",
                    "Mac Controls",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        else
        {
            _keyboardHook.Stop();
        }

        _trayIcon.Text = _enabled ? "Mac Controls: enabled" : "Mac Controls: suspended";
    }

    private static void ShowShortcutReference()
    {
        MessageBox.Show(
            "Left Win behaves like Command (Ctrl for ordinary shortcuts).\n\n" +
            "Left Win + Left/Right: start/end of line\n" +
            "Left Win + Up/Down: start/end of document\n" +
            "Left Win + Backspace: delete to start of line\n\n" +
            "Left Alt + arrows: move by word or paragraph\n" +
            "Left Alt + Backspace: delete previous word\n\n" +
            "Left Alt + mapped symbol key: use the personal Unicode symbol layer\n" +
            "Other Left Alt chords: normal Windows Alt shortcuts\n" +
            "Left Alt + J: insert an apostrophe\n\n" +
            "Y/Z and the OEM layout key use the existing personal swaps.\n\n" +
            "Hold Shift with any navigation shortcut to extend the selection.",
            "Mac Controls shortcuts",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    protected override void ExitThreadCore()
    {
        _keyboardHook.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }
}

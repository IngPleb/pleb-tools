using System.Diagnostics;
using System.Text;

namespace PlebTools.AppExpose;

internal static class WindowDiscovery
{
    private const int ExtendedStyleIndex = -20;
    private const long ToolWindowStyle = 0x00000080L;
    private const int DwmWindowAttributeCloaked = 14;

    /// <summary>
    /// Returns ordinary visible top-level windows from the same process as the focused window.
    /// The focused window remains first because EnumWindows follows desktop Z order.
    /// </summary>
    public static IReadOnlyList<AppWindow> FindSiblings(nint focusedWindow)
    {
        if (focusedWindow == nint.Zero)
        {
            return [];
        }

        NativeMethods.GetWindowThreadProcessId(focusedWindow, out uint focusedProcessId);
        if (focusedProcessId == 0)
        {
            return [];
        }

        string? focusedAppId = WindowIdentity.GetAppUserModelId(focusedWindow);
        string processName = GetApplicationName(focusedProcessId);
        var windows = new List<AppWindow>();

        NativeMethods.EnumWindows(
            (window, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(window, out uint processId);
                bool sameApplication = focusedAppId is not null
                    ? string.Equals(
                        focusedAppId,
                        WindowIdentity.GetAppUserModelId(window),
                        StringComparison.OrdinalIgnoreCase)
                    : processId == focusedProcessId;

                if (!sameApplication || !IsTaskWindow(window))
                {
                    return true;
                }

                string title = GetTitle(window);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    windows.Add(new AppWindow(window, title, processName));
                }

                return true;
            },
            nint.Zero);

        int focusedIndex = windows.FindIndex(window => window.Handle == focusedWindow);
        if (focusedIndex > 0)
        {
            AppWindow focused = windows[focusedIndex];
            windows.RemoveAt(focusedIndex);
            windows.Insert(0, focused);
        }

        return windows;
    }

    private static bool IsTaskWindow(nint window)
    {
        if (!NativeMethods.IsWindowVisible(window) || NativeMethods.GetWindow(window, NativeMethods.GwOwner) != nint.Zero)
        {
            return false;
        }

        long extendedStyle = NativeMethods.GetWindowLongPtr(window, ExtendedStyleIndex).ToInt64();
        if ((extendedStyle & ToolWindowStyle) != 0)
        {
            return false;
        }

        int cloaked = 0;
        int result = NativeMethods.DwmGetWindowAttribute(
            window,
            DwmWindowAttributeCloaked,
            ref cloaked,
            sizeof(int));
        return result != 0 || cloaked == 0;
    }

    private static string GetTitle(nint window)
    {
        int length = NativeMethods.GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetApplicationName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            string? description = process.MainModule?.FileVersionInfo.FileDescription;
            return string.IsNullOrWhiteSpace(description) ? process.ProcessName : description;
        }
        catch (Exception) when (processId != Environment.ProcessId)
        {
            return "Application";
        }
    }
}

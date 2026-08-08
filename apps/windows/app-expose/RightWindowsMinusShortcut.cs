namespace PlebTools.AppExpose;

/// <summary>
/// Recognizes Right Windows plus the main keyboard minus key while preserving
/// Right Windows by itself and every other Right Windows shortcut.
/// </summary>
internal sealed class RightWindowsMinusShortcut : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int SystemKeyDown = 0x0104;
    private const int SystemKeyUp = 0x0105;
    private const uint RightWindowsKey = 0x5C;
    private const uint MinusKey = 0xBD;
    private const uint NumpadSubtractKey = 0x6D;
    private const uint MainRowMinusScanCode = 0x0C;
    private const uint NumpadMinusScanCode = 0x4A;
    private const uint LayoutMinusScanCode = 0x35;
    private const uint KeyUpFlag = 0x0002;
    private static readonly nuint ReplayMarker = 0x415850;

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private nint _hook;
    private bool _rightWindowsDown;
    private bool _rightWindowsForwarded;
    private bool _shortcutTriggered;
    private bool _suppressMinusUp;

    public RightWindowsMinusShortcut()
    {
        _callback = HandleKeyboardEvent;
    }

    public event Action? Pressed;

    public bool Start()
    {
        _hook = NativeMethods.SetWindowsHookEx(LowLevelKeyboardHook, _callback, nint.Zero, 0);
        return _hook != nint.Zero;
    }

    private nint HandleKeyboardEvent(int code, nint message, nint data)
    {
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        NativeMethods.KBDLLHOOKSTRUCT keyboard =
            System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(data);

        // Events replayed by this hook must pass through without being interpreted again.
        if (keyboard.ExtraInfo == ReplayMarker)
        {
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        int eventMessage = message.ToInt32();
        bool isDown = eventMessage is KeyDown or SystemKeyDown;
        bool isUp = eventMessage is KeyUp or SystemKeyUp;
        if (keyboard.VirtualKey == RightWindowsKey)
        {
            if (isDown)
            {
                _rightWindowsDown = true;
                _rightWindowsForwarded = false;
                _shortcutTriggered = false;
                return 1;
            }

            if (isUp && _rightWindowsDown)
            {
                if (_rightWindowsForwarded)
                {
                    Reset();
                    return NativeMethods.CallNextHookEx(_hook, code, message, data);
                }

                if (!_shortcutTriggered)
                {
                    ReplayRightWindowsPress();
                }

                Reset();
                return 1;
            }
        }

        if (!_rightWindowsDown)
        {
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        bool isMinus = keyboard.VirtualKey is MinusKey or NumpadSubtractKey ||
            keyboard.ScanCode is MainRowMinusScanCode or NumpadMinusScanCode or LayoutMinusScanCode;
        if (isMinus)
        {
            if (isDown)
            {
                if (!_shortcutTriggered)
                {
                    _shortcutTriggered = true;
                    _suppressMinusUp = true;
                    Pressed?.Invoke();
                }

                return 1;
            }

            if (isUp && _suppressMinusUp)
            {
                _suppressMinusUp = false;
                return 1;
            }
        }

        // Another chord such as Right Windows + E should behave exactly as it did before.
        if (isDown && !_rightWindowsForwarded && !_shortcutTriggered)
        {
            NativeMethods.keybd_event((byte)RightWindowsKey, 0, 0, ReplayMarker);
            _rightWindowsForwarded = true;
        }

        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    private static void ReplayRightWindowsPress()
    {
        NativeMethods.keybd_event((byte)RightWindowsKey, 0, 0, ReplayMarker);
        NativeMethods.keybd_event((byte)RightWindowsKey, 0, KeyUpFlag, ReplayMarker);
    }

    private void Reset()
    {
        _rightWindowsDown = false;
        _rightWindowsForwarded = false;
        _shortcutTriggered = false;
        _suppressMinusUp = false;
    }

    public void Dispose()
    {
        if (_hook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = nint.Zero;
        }
    }
}

using System.Runtime.InteropServices;

namespace PlebTools.MacControls;

/// <summary>
/// Maps physical Left Win to Left Ctrl and translates only the requested
/// Command/Option text-editing chords. Right-side modifiers remain untouched.
/// </summary>
internal sealed class MacKeyboardHook : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDown = 0x0100;
    private const int KeyUp = 0x0101;
    private const int SystemKeyDown = 0x0104;
    private const int SystemKeyUp = 0x0105;
    private const uint KeyboardInput = 1;
    private const uint ExtendedKey = 0x0001;
    private const uint KeyUpFlag = 0x0002;
    private const uint UnicodeKey = 0x0004;
    private static readonly nuint ReplayMarker = 0x4D414343;
    private static readonly IReadOnlyDictionary<ushort, ushort> OwnedKeyMappings =
        new Dictionary<ushort, ushort>
        {
            [VirtualKeys.Y] = VirtualKeys.Z,
            [VirtualKeys.Z] = VirtualKeys.Y,
            [VirtualKeys.Oem5] = VirtualKeys.Oem102,
        };
    private static readonly IReadOnlyDictionary<ushort, string> OwnedLeftAltTextShortcuts =
        new Dictionary<ushort, string>
        {
            [VirtualKeys.J] = "'",
        };

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly HashSet<ushort> _suppressedActionKeys = [];
    private readonly IReadOnlyDictionary<ushort, string> _leftAltTextShortcuts;
    private readonly ushort _commandKey;
    private readonly ushort _optionKey;
    private nint _hook;
    private bool _commandDown;
    private bool _mappedControlDown;
    private bool _optionDown;
    private bool _optionForwarded;
    private bool _optionForwardedAsAltGr;
    private bool _optionShortcutUsed;
    private bool _rightAltDown;
    private bool _leftControlDown;
    private bool _rightControlDown;
    private bool _rightWindowsDown;
    private bool _leftShiftDown;
    private bool _rightShiftDown;
    private bool _disposed;
    private long _observedEventCount;
    private long _translatedActionCount;

    internal long ObservedEventCount => Interlocked.Read(ref _observedEventCount);
    internal long TranslatedActionCount => Interlocked.Read(ref _translatedActionCount);
    internal static ushort RemapOwnedKey(ushort key) =>
        OwnedKeyMappings.TryGetValue(key, out ushort mapped) ? mapped : key;

    internal static ushort OptionModifierFor(ushort key) =>
        VirtualKeys.IsPrintable(key) ? VirtualKeys.RightAlt : VirtualKeys.LeftAlt;

    public MacKeyboardHook(
        ushort commandKey = VirtualKeys.LeftWindows,
        ushort optionKey = VirtualKeys.LeftAlt,
        IReadOnlyDictionary<ushort, string>? leftAltTextShortcuts = null)
    {
        _commandKey = commandKey;
        _optionKey = optionKey;
        _leftAltTextShortcuts = leftAltTextShortcuts ?? OwnedLeftAltTextShortcuts;
        _callback = HandleKeyboardEvent;
    }

    public bool Start()
    {
        if (_hook != nint.Zero)
        {
            return true;
        }

        _hook = NativeMethods.SetWindowsHookEx(LowLevelKeyboardHook, _callback, nint.Zero, 0);
        return _hook != nint.Zero;
    }

    public void Stop()
    {
        ReleaseMappedControl();
        ReleaseForwardedOption();
        if (_hook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = nint.Zero;
        }

        ResetState();
    }

    private nint HandleKeyboardEvent(int code, nint message, nint data)
    {
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        NativeMethods.KBDLLHOOKSTRUCT keyboard =
            Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(data);
        if (keyboard.ExtraInfo == ReplayMarker)
        {
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        Interlocked.Increment(ref _observedEventCount);
        int eventMessage = message.ToInt32();
        bool isDown = eventMessage is KeyDown or SystemKeyDown;
        bool isUp = eventMessage is KeyUp or SystemKeyUp;
        if (!isDown && !isUp)
        {
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        ushort key = checked((ushort)keyboard.VirtualKey);
        if (key == _commandKey)
        {
            if (isDown && !_commandDown)
            {
                _commandDown = true;
                PressMappedControl();
            }
            else if (isUp)
            {
                _commandDown = false;
                ReleaseMappedControl();
            }

            return 1;
        }

        if (key == _optionKey)
        {
            if (isDown)
            {
                if (!_optionDown)
                {
                    _optionDown = true;
                    _optionForwarded = false;
                    _optionForwardedAsAltGr = false;
                    _optionShortcutUsed = false;
                }

                return 1;
            }

            if (isUp && _optionDown)
            {
                bool wasForwarded = _optionForwarded;
                if (!wasForwarded && !_optionShortcutUsed)
                {
                    ReplayOptionPress();
                }

                bool wasAltGr = _optionForwardedAsAltGr;
                if (wasAltGr)
                {
                    Send([KeyboardStroke.Up(VirtualKeys.RightAlt)]);
                }

                ResetOptionState();
                return wasForwarded && !wasAltGr
                    ? NativeMethods.CallNextHookEx(_hook, code, message, data)
                    : 1;
            }
        }

        if (isUp && _suppressedActionKeys.Remove(key))
        {
            return 1;
        }

        UpdatePhysicalModifierState(key, isDown);
        if (isDown && TryTranslateAction(key))
        {
            _suppressedActionKeys.Add(key);
            return 1;
        }

        if (_optionDown && isDown && !_optionForwarded && !IsShiftKey(key))
        {
            if (_leftAltTextShortcuts.TryGetValue(key, out string? text) && SendText(text))
            {
                _optionShortcutUsed = true;
                _suppressedActionKeys.Add(key);
                return 1;
            }

            if (OptionModifierFor(key) == VirtualKeys.RightAlt)
            {
                _optionForwarded = Send([KeyboardStroke.Down(VirtualKeys.RightAlt)]);
                _optionForwardedAsAltGr = _optionForwarded;
            }
            else
            {
                _optionForwarded = Send([KeyboardStroke.Down(_optionKey)]);
            }
        }

        ushort remappedKey = RemapOwnedKey(key);
        if (remappedKey != key)
        {
            return Send([isDown ? KeyboardStroke.Down(remappedKey) : KeyboardStroke.Up(remappedKey)])
                ? 1
                : NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    private bool TryTranslateAction(ushort key)
    {
        if (!VirtualKeys.IsEditingAction(key))
        {
            return false;
        }

        bool conflictingControl = _leftControlDown || _rightControlDown;
        if (_commandDown && _mappedControlDown && !_optionDown && !_rightAltDown && !_rightWindowsDown && !conflictingControl)
        {
            if (Send(KeyboardSequence.ForCommand(key, HeldShiftKey())))
            {
                Interlocked.Increment(ref _translatedActionCount);
                return true;
            }

            return false;
        }

        if (_optionDown && !_optionForwarded && !_commandDown && !_rightAltDown && !_rightWindowsDown && !conflictingControl)
        {
            if (Send(KeyboardSequence.ForOption(key, HeldShiftKey())))
            {
                _optionShortcutUsed = true;
                Interlocked.Increment(ref _translatedActionCount);
                return true;
            }

            return false;
        }

        return false;
    }

    private ushort? HeldShiftKey()
    {
        if (_leftShiftDown)
        {
            return VirtualKeys.LeftShift;
        }

        return _rightShiftDown ? VirtualKeys.RightShift : null;
    }

    private void UpdatePhysicalModifierState(ushort key, bool isDown)
    {
        switch (key)
        {
            case VirtualKeys.RightAlt:
                _rightAltDown = isDown;
                break;
            case VirtualKeys.LeftControl:
                _leftControlDown = isDown;
                break;
            case VirtualKeys.RightControl:
                _rightControlDown = isDown;
                break;
            case VirtualKeys.RightWindows:
                _rightWindowsDown = isDown;
                break;
            case VirtualKeys.LeftShift:
                _leftShiftDown = isDown;
                break;
            case VirtualKeys.RightShift:
                _rightShiftDown = isDown;
                break;
        }
    }

    private void PressMappedControl()
    {
        if (!_mappedControlDown)
        {
            _mappedControlDown = Send([KeyboardStroke.Down(VirtualKeys.LeftControl)]);
        }
    }

    private void ReleaseMappedControl()
    {
        if (_mappedControlDown)
        {
            _mappedControlDown = false;
            Send([KeyboardStroke.Up(VirtualKeys.LeftControl)]);
        }
    }

    private void ReleaseForwardedOption()
    {
        if (_optionForwarded)
        {
            Send([KeyboardStroke.Up(_optionForwardedAsAltGr ? VirtualKeys.RightAlt : _optionKey)]);
            ResetOptionState();
        }
    }

    private void ReplayOptionPress() =>
        Send([KeyboardStroke.Down(_optionKey), KeyboardStroke.Up(_optionKey)]);

    private void ResetOptionState()
    {
        _optionDown = false;
        _optionForwarded = false;
        _optionForwardedAsAltGr = false;
        _optionShortcutUsed = false;
    }

    private static bool Send(IReadOnlyList<KeyboardStroke> strokes)
    {
        if (strokes.Count == 0)
        {
            return true;
        }

        var inputs = new NativeMethods.INPUT[strokes.Count];
        for (int index = 0; index < strokes.Count; index++)
        {
            KeyboardStroke stroke = strokes[index];
            uint flags = stroke.IsKeyUp ? KeyUpFlag : 0;
            if (IsExtendedKey(stroke.VirtualKey))
            {
                flags |= ExtendedKey;
            }

            inputs[index] = KeyInput(stroke.VirtualKey, scanCode: 0, flags);
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return sent == inputs.Length;
    }

    private static bool SendText(string text)
    {
        var inputs = new NativeMethods.INPUT[text.Length * 2];
        for (int index = 0; index < text.Length; index++)
        {
            inputs[index * 2] = KeyInput(virtualKey: 0, scanCode: text[index], flags: UnicodeKey);
            inputs[(index * 2) + 1] = KeyInput(
                virtualKey: 0,
                scanCode: text[index],
                flags: UnicodeKey | KeyUpFlag);
        }

        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return sent == inputs.Length;
    }

    private static NativeMethods.INPUT KeyInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = KeyboardInput,
        Data = new NativeMethods.INPUTUNION
        {
            Keyboard = new NativeMethods.KEYBDINPUT
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags,
                ExtraInfo = ReplayMarker,
            },
        },
    };

    private static bool IsExtendedKey(ushort key) => key is
        VirtualKeys.Home or VirtualKeys.End or VirtualKeys.Left or VirtualKeys.Right or
        VirtualKeys.Up or VirtualKeys.Down or VirtualKeys.RightControl or VirtualKeys.RightAlt;

    private static bool IsShiftKey(ushort key) => key is
        VirtualKeys.Shift or VirtualKeys.LeftShift or VirtualKeys.RightShift;

    private void ResetState()
    {
        _commandDown = false;
        _mappedControlDown = false;
        _optionDown = false;
        _optionForwarded = false;
        _optionForwardedAsAltGr = false;
        _optionShortcutUsed = false;
        _rightAltDown = false;
        _leftControlDown = false;
        _rightControlDown = false;
        _rightWindowsDown = false;
        _leftShiftDown = false;
        _rightShiftDown = false;
        _suppressedActionKeys.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}

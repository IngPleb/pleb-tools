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
    internal static readonly nuint ReplayMarker = 0x4D414343;
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
            [0x30] = ")",
            [0x31] = "!",
            [0x32] = "@",
            [0x33] = "#",
            [0x34] = "$",
            [0x35] = "%",
            [0x36] = "^",
            [0x37] = "&",
            [0x38] = "*",
            [0x39] = "(",
            [0x45] = "€",
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = "<",
            [0xBD] = "-",
            [0xBE] = ">",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "¤",
            [0xE2] = "ß",
        };

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly HashSet<ushort> _suppressedActionKeys = [];
    private readonly IReadOnlyDictionary<ushort, string> _leftAltTextShortcuts;
    private readonly ushort _commandKey;
    private readonly ushort _optionKey;
    private readonly ushort? _copilotKey;
    private nint _hook;
    private bool _commandDown;
    private bool _passthroughCommandUntilReleased;
    private bool _mappedControlDown;
    private bool _optionDown;
    private bool _passthroughOptionUntilReleased;
    private bool _optionForwarded;
    private bool _optionShortcutUsed;
    private bool _rightAltDown;
    private bool _leftControlDown;
    private bool _rightControlDown;
    private bool _rightWindowsDown;
    private bool _copilotKeyDown;
    private bool _copilotRightWindowsDown;
    private bool _pendingCopilotShift;
    private bool _leftShiftDown;
    private bool _rightShiftDown;
    private bool _disposed;
    private long _observedEventCount;
    private long _translatedActionCount;

    internal long ObservedEventCount => Interlocked.Read(ref _observedEventCount);
    internal long TranslatedActionCount => Interlocked.Read(ref _translatedActionCount);
    internal static ushort RemapOwnedKey(ushort key) =>
        OwnedKeyMappings.TryGetValue(key, out ushort mapped) ? mapped : key;

    internal static bool TryGetOwnedLeftAltText(ushort key, out string text) =>
        OwnedLeftAltTextShortcuts.TryGetValue(key, out text!);

    public MacKeyboardHook(
        ushort commandKey = VirtualKeys.LeftWindows,
        ushort optionKey = VirtualKeys.LeftAlt,
        IReadOnlyDictionary<ushort, string>? leftAltTextShortcuts = null,
        ushort? copilotKey = VirtualKeys.F23)
    {
        _commandKey = commandKey;
        _optionKey = optionKey;
        _leftAltTextShortcuts = leftAltTextShortcuts ?? OwnedLeftAltTextShortcuts;
        _copilotKey = copilotKey;
        _callback = HandleKeyboardEvent;
    }

    public bool Start()
    {
        if (_hook != nint.Zero)
        {
            return true;
        }

        RefreshPhysicalModifierState();
        _passthroughCommandUntilReleased = IsPhysicalKeyDown(_commandKey);
        _passthroughOptionUntilReleased = IsPhysicalKeyDown(_optionKey);
        ReleaseStaleOwnedModifiers();
        _hook = NativeMethods.SetWindowsHookEx(LowLevelKeyboardHook, _callback, nint.Zero, 0);
        return _hook != nint.Zero;
    }

    public void Stop()
    {
        ReleaseCopilotRightWindows();
        ReleaseMappedControl();
        ReleaseForwardedOption();
        if (_hook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = nint.Zero;
        }

        // Retry after removing the hook if SendInput was transiently rejected
        // while the callback chain was active.
        ReleaseCopilotRightWindows();
        ReleaseMappedControl();
        ReleaseForwardedOption();

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
            if (_passthroughCommandUntilReleased)
            {
                if (isUp)
                {
                    _passthroughCommandUntilReleased = false;
                }

                return NativeMethods.CallNextHookEx(_hook, code, message, data);
            }

            if (isDown && !_commandDown)
            {
                _commandDown = true;
                PressMappedControl();
            }
            else if (isUp)
            {
                _commandDown = false;
                ReleaseMappedControl();
                TryReleaseCopilotRightWindows();
            }

            return 1;
        }

        if (key == _optionKey)
        {
            if (_passthroughOptionUntilReleased)
            {
                if (isUp)
                {
                    _passthroughOptionUntilReleased = false;
                }

                return NativeMethods.CallNextHookEx(_hook, code, message, data);
            }

            if (isDown)
            {
                if (!_optionDown)
                {
                    _optionDown = true;
                    _optionForwarded = false;
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

                if (!ReleaseForwardedOption())
                {
                    // Keep ownership state so Stop can retry. The physical
                    // key-up is still suppressed because its down was.
                    return 1;
                }

                ResetOptionState();
                TryReleaseCopilotRightWindows();
                return 1;
            }

            if (isUp)
            {
                // Left Alt-down is always suppressed while this hook owns it,
                // so its physical key-up must never balance a synthetic down.
                ReleaseForwardedOption();
                return 1;
            }
        }

        if (isUp && _suppressedActionKeys.Remove(key))
        {
            return 1;
        }

        if (_copilotKey is not null && key == VirtualKeys.LeftShift)
        {
            if (isDown && (_pendingCopilotShift || _copilotKeyDown))
            {
                // The Zephyrus repeats the full LWin+LShift+F23 down sequence
                // while Copilot is held. Keep every repeated Shift-down owned.
                return 1;
            }

            if (isDown && !_leftShiftDown && (_commandDown || _optionDown))
            {
                _leftShiftDown = true;
                _pendingCopilotShift = true;
                return 1;
            }

            if (isUp && _pendingCopilotShift)
            {
                _leftShiftDown = false;
                _pendingCopilotShift = false;
                TryReleaseCopilotRightWindows();
                return 1;
            }
        }

        if (_copilotKey is ushort copilotKey && key == copilotKey)
        {
            return HandleCopilotKey(isDown, isUp)
                ? 1
                : NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        if (isDown && _pendingCopilotShift && !_copilotKeyDown)
        {
            if (!Send([KeyboardStroke.Down(VirtualKeys.LeftShift)]))
            {
                return NativeMethods.CallNextHookEx(_hook, code, message, data);
            }
            _pendingCopilotShift = false;
        }

        if (_copilotKeyDown)
        {
            // The macro's leading LWin is still tracked as Command/Option until
            // its physical key-up arrives. Real keys pressed while Copilot is
            // held must bypass those layers and travel under synthetic RWin.
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
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

            // Only explicitly owned symbol chords bypass Alt. Every other
            // chord must remain a normal Windows Left Alt shortcut, including
            // printable combinations such as Alt+X and Alt+Z. Never forward
            // Right Alt here: Windows models AltGr as a coupled Ctrl+Alt state.
            _optionForwarded = Send([KeyboardStroke.Down(VirtualKeys.LeftAlt)]);
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

    private bool HandleCopilotKey(bool isDown, bool isUp)
    {
        if (isDown && !_copilotKeyDown)
        {
            _copilotKeyDown = true;

            // This Zephyrus emits LWin + LShift + F23 and repeats all three
            // key-down events while the physical Copilot key is held. Replace
            // that entire macro with a held Right Win so Copilot can be used as
            // a real modifier, including with App Expose's punctuation key.
            if (_commandDown)
            {
                ReleaseMappedControl();
            }
            if (_optionDown)
            {
                _optionShortcutUsed = true;
            }

            _copilotRightWindowsDown = Send([KeyboardStroke.Down(VirtualKeys.RightWindows)]);
            return true;
        }

        if (isUp)
        {
            _copilotKeyDown = false;
            TryReleaseCopilotRightWindows();
            return true;
        }

        return true;
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
            if (_leftControlDown)
            {
                // The physical Ctrl key now owns the shared Windows modifier
                // state and its eventual physical key-up will release it.
                _mappedControlDown = false;
                return;
            }

            if (SendRelease(VirtualKeys.LeftControl))
            {
                _mappedControlDown = false;
            }
        }
    }

    private bool ReleaseForwardedOption()
    {
        if (!_optionForwarded)
        {
            return true;
        }

        if (!SendRelease(VirtualKeys.LeftAlt))
        {
            return false;
        }

        _optionForwarded = false;
        return true;
    }

    private void ReleaseCopilotRightWindows()
    {
        if (!_copilotRightWindowsDown)
        {
            return;
        }

        if (SendRelease(VirtualKeys.RightWindows))
        {
            _copilotRightWindowsDown = false;
        }
    }

    private void TryReleaseCopilotRightWindows()
    {
        if (_copilotKeyDown || _commandDown || _optionDown || _pendingCopilotShift)
        {
            return;
        }

        ReleaseCopilotRightWindows();
    }

    private static bool SendRelease(ushort key) =>
        Send([KeyboardStroke.Up(key)]) || Send([KeyboardStroke.Up(key)]);

    private void ReleaseStaleOwnedModifiers()
    {
        var releases = new List<KeyboardStroke>(3);
        if (!IsPhysicalKeyDown(_commandKey) && !IsPhysicalKeyDown(VirtualKeys.LeftControl))
        {
            releases.Add(KeyboardStroke.Up(VirtualKeys.LeftControl));
        }
        if (!IsPhysicalKeyDown(_optionKey) && !IsPhysicalKeyDown(VirtualKeys.LeftAlt))
        {
            releases.Add(KeyboardStroke.Up(VirtualKeys.LeftAlt));
            if (!IsPhysicalKeyDown(VirtualKeys.RightAlt))
            {
                releases.Add(KeyboardStroke.Up(VirtualKeys.RightAlt));
            }
        }

        Send(releases);
    }

    private void RefreshPhysicalModifierState()
    {
        _leftControlDown = IsPhysicalKeyDown(VirtualKeys.LeftControl);
        _rightControlDown = IsPhysicalKeyDown(VirtualKeys.RightControl);
        _rightAltDown = IsPhysicalKeyDown(VirtualKeys.RightAlt);
        _rightWindowsDown = IsPhysicalKeyDown(VirtualKeys.RightWindows);
        _leftShiftDown = IsPhysicalKeyDown(VirtualKeys.LeftShift);
        _rightShiftDown = IsPhysicalKeyDown(VirtualKeys.RightShift);
    }

    private static bool IsPhysicalKeyDown(ushort key) =>
        (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    private void ReplayOptionPress() =>
        Send([KeyboardStroke.Down(VirtualKeys.LeftAlt), KeyboardStroke.Up(VirtualKeys.LeftAlt)]);

    private void ResetOptionState()
    {
        _optionDown = false;
        _optionForwarded = false;
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
        VirtualKeys.Up or VirtualKeys.Down or VirtualKeys.RightControl or VirtualKeys.RightAlt or
        VirtualKeys.LeftWindows or VirtualKeys.RightWindows;

    private static bool IsShiftKey(ushort key) => key is
        VirtualKeys.Shift or VirtualKeys.LeftShift or VirtualKeys.RightShift;

    private void ResetState()
    {
        _commandDown = false;
        _passthroughCommandUntilReleased = false;
        _mappedControlDown = false;
        _optionDown = false;
        _passthroughOptionUntilReleased = false;
        _optionForwarded = false;
        _optionShortcutUsed = false;
        _rightAltDown = false;
        _leftControlDown = false;
        _rightControlDown = false;
        _rightWindowsDown = false;
        _copilotKeyDown = false;
        _copilotRightWindowsDown = false;
        _pendingCopilotShift = false;
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

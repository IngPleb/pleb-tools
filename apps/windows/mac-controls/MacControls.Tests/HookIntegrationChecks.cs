using System.Runtime.InteropServices;

namespace PlebTools.MacControls;

internal static class HookIntegrationChecks
{
    internal static void Run()
    {
        Action<HookHost>[] checks =
        [
            CheckCommandNavigation,
            CheckCommandRightNavigation,
            CheckCommandSelection,
            CheckCommandRightSelection,
            CheckCommandDeletion,
            CheckCommandOrdinaryShortcut,
            CheckOptionNavigation,
            CheckOptionRightNavigation,
            CheckOptionSelection,
            CheckOptionRightSelection,
            CheckOptionDeletion,
            CheckOrdinaryPrintableOptionShortcuts,
        ];
        foreach (Action<HookHost> check in checks)
        {
            using var host = new HookHost(
                installTestHook: true,
                commandKey: VirtualKeys.F24,
                optionKey: VirtualKeys.F23,
                copilotKey: VirtualKeys.F21);
            host.Start();
            check(host);
        }
    }

    internal static void RunInstalled(bool swappedModifierLayout = false)
    {
        if (System.Diagnostics.Process.GetProcessesByName("MacControls").Length != 1)
        {
            throw new InvalidOperationException("Exactly one installed MacControls process must be running.");
        }

        using var host = new HookHost(
            installTestHook: false,
            commandKey: swappedModifierLayout ? VirtualKeys.LeftAlt : VirtualKeys.LeftWindows,
            optionKey: swappedModifierLayout ? VirtualKeys.LeftWindows : VirtualKeys.LeftAlt);
        host.Start();

        RunChecks(host);
        CheckOwnedAltJ(host);
        CheckDelayedOwnedAltJ(host);
        CheckOptionUnicodeSymbol(host);
        CheckOrdinaryPrintableOptionShortcuts(host);
        CheckOptionXThenEscapeRemainsNormal(host);
    }

    internal static void RunInstalledSymbols(bool swappedModifierLayout = false)
    {
        if (System.Diagnostics.Process.GetProcessesByName("MacControls").Length != 1)
        {
            throw new InvalidOperationException("Exactly one installed MacControls process must be running.");
        }

        using var host = new HookHost(
            installTestHook: false,
            commandKey: swappedModifierLayout ? VirtualKeys.LeftAlt : VirtualKeys.LeftWindows,
            optionKey: swappedModifierLayout ? VirtualKeys.LeftWindows : VirtualKeys.LeftAlt);
        host.Start();

        CheckOwnedAltJ(host);
        CheckDelayedOwnedAltJ(host);
        CheckOptionUnicodeSymbol(host);
        CheckOrdinaryPrintableOptionShortcuts(host);
        CheckOptionXThenEscapeRemainsNormal(host);
    }

    internal static void RunInstalledCopilot()
    {
        if (System.Diagnostics.Process.GetProcessesByName("MacControls").Length != 1)
        {
            throw new InvalidOperationException("Exactly one installed MacControls process must be running.");
        }

        CheckCopilotMapsToRightWindows(prefixModifier: null);
    }

    internal static void RunInstalledAppExpose()
    {
        Assert(System.Diagnostics.Process.GetProcessesByName("MacControls").Length == 1,
            "Exactly one installed MacControls process must be running.");
        Assert(System.Diagnostics.Process.GetProcessesByName("AppExpose").Length == 1,
            "Exactly one installed AppExpose process must be running.");

        using var host = new HookHost(
            installTestHook: false,
            commandKey: VirtualKeys.LeftAlt,
            optionKey: VirtualKeys.LeftWindows,
            visibleTaskWindow: true);
        host.Start();
        host.Reset(string.Empty, caret: 0);
        using var spy = new RightWindowsSpy();
        spy.Start();

        try
        {
            // Prove the installed AppExpose hook is in the chain: a plain
            // Right Win tap is replayed with AppExpose's marker.
            SendStroke(KeyboardStroke.Down(VirtualKeys.RightWindows));
            SendStroke(KeyboardStroke.Up(VirtualKeys.RightWindows));
            Thread.Sleep(150);
            Assert(spy.AppExposeReplayDownCount == 1 && spy.AppExposeReplayUpCount == 1,
                "Installed AppExpose did not replay the baseline Right Win tap.");
            SendStroke(KeyboardStroke.Down(0x1B));
            SendStroke(KeyboardStroke.Up(0x1B));
            host.Reset(string.Empty, caret: 0);

            // A Copilot tap must traverse both installed hooks as an ordinary
            // Right Win tap, including AppExpose's transparent replay.
            SendStroke(KeyboardStroke.Down(VirtualKeys.LeftWindows));
            SendStroke(KeyboardStroke.Down(VirtualKeys.LeftShift));
            SendStroke(KeyboardStroke.Down(VirtualKeys.F23));
            SendStroke(KeyboardStroke.Up(VirtualKeys.F23));
            SendStroke(KeyboardStroke.Up(VirtualKeys.LeftShift));
            SendStroke(KeyboardStroke.Up(VirtualKeys.LeftWindows));
            Thread.Sleep(150);
            Assert(
                spy.RightWindowsDownCount == 1 && spy.RightWindowsUpCount == 1 &&
                spy.AppExposeReplayDownCount == 2 && spy.AppExposeReplayUpCount == 2,
                "Copilot tap did not behave as an ordinary Right Win tap through installed AppExpose.");
            SendStroke(KeyboardStroke.Down(0x1B));
            SendStroke(KeyboardStroke.Up(0x1B));
            host.Reset(string.Empty, caret: 0);

            // Raw capture from this notebook: LWin, LShift, F23, followed by
            // the key labeled '-' as VK 0xBF / scan 0x035.
            SendStroke(KeyboardStroke.Down(VirtualKeys.LeftWindows));
            SendStroke(KeyboardStroke.Down(VirtualKeys.LeftShift));
            SendStroke(KeyboardStroke.Down(VirtualKeys.F23));
            SendStroke(KeyboardStroke.Down(0xBF), scanCode: 0x35);
            SendStroke(KeyboardStroke.Up(0xBF), scanCode: 0x35);
            SendStroke(KeyboardStroke.Up(VirtualKeys.F23));
            SendStroke(KeyboardStroke.Up(VirtualKeys.LeftShift));
            SendStroke(KeyboardStroke.Up(VirtualKeys.LeftWindows));

            Thread.Sleep(250);

            bool shortcutRecognized =
                spy.RightWindowsDownCount == 2 && spy.RightWindowsUpCount == 2 &&
                spy.MeasuredPunctuationDownCount == 1 && spy.MeasuredPunctuationUpCount == 1 &&
                spy.AppExposeReplayDownCount == 2 && spy.AppExposeReplayUpCount == 2;
            Assert(shortcutRecognized,
                "Installed AppExpose did not recognize the measured Copilot + punctuation sequence. " +
                $"Observed MacControls RWin={spy.RightWindowsDownCount}/{spy.RightWindowsUpCount}, " +
                $"punctuation={spy.MeasuredPunctuationDownCount}/{spy.MeasuredPunctuationUpCount}, " +
                $"AppExpose replay={spy.AppExposeReplayDownCount}/{spy.AppExposeReplayUpCount}.");
        }
        finally
        {
            TryRelease(0xBF);
            TryRelease(VirtualKeys.F23);
            TryRelease(VirtualKeys.LeftShift);
            TryRelease(VirtualKeys.LeftWindows);
            TryRelease(VirtualKeys.RightWindows);
            SendStroke(KeyboardStroke.Down(0x1B));
            SendStroke(KeyboardStroke.Up(0x1B));
        }
    }

    internal static void RunCopilot()
    {
        foreach (ushort prefixModifier in new[] { VirtualKeys.F24, VirtualKeys.F22 })
        {
            using var host = new HookHost(
                installTestHook: true,
                commandKey: VirtualKeys.F24,
                optionKey: VirtualKeys.F22,
                copilotKey: VirtualKeys.F23);
            host.Start();

            CheckCopilotMapsToRightWindows(prefixModifier);
        }
    }

    private static void RunChecks(HookHost host)
    {
        CheckCommandNavigation(host);
        CheckCommandRightNavigation(host);
        CheckCommandSelection(host);
        CheckCommandRightSelection(host);
        CheckCommandDeletion(host);
        CheckCommandOrdinaryShortcut(host);
        CheckOptionNavigation(host);
        CheckOptionRightNavigation(host);
        CheckOptionSelection(host);
        CheckOptionRightSelection(host);
        CheckOptionDeletion(host);
    }

    private static void CheckCommandNavigation(HookHost host)
    {
        host.Reset("alpha bravo", caret: 8);
        PressChord(host.CommandKey, VirtualKeys.Left);
        EditorState state = host.State();
        Assert(state.SelectionStart == 0 && state.SelectionLength == 0,
            $"Command+Left produced selection {state.SelectionStart}:{state.SelectionLength}; hook observed {state.ObservedEvents} events, translated {state.TranslatedActions}, foreground={state.IsForeground}, focused={state.IsFocused}.");
    }

    private static void CheckCommandSelection(HookHost host)
    {
        host.Reset("alpha bravo", caret: 8);
        PressChord(host.CommandKey, VirtualKeys.Left, withShift: true);
        EditorState state = host.State();
        Assert(state.SelectionStart == 0 && state.SelectionLength == 8,
            $"Command+Shift+Left produced selection {state.SelectionStart}:{state.SelectionLength}; hook observed {state.ObservedEvents} events, translated {state.TranslatedActions}, foreground={state.IsForeground}, focused={state.IsFocused}.");
    }

    private static void CheckCommandRightNavigation(HookHost host)
    {
        host.Reset("alpha bravo", caret: 2);
        PressChord(host.CommandKey, VirtualKeys.Right);
        EditorState state = host.State();
        Assert(state.SelectionStart == 11 && state.SelectionLength == 0,
            $"Command+Right produced selection {state.SelectionStart}:{state.SelectionLength}.");
    }

    private static void CheckCommandRightSelection(HookHost host)
    {
        host.Reset("alpha bravo", caret: 2);
        PressChord(host.CommandKey, VirtualKeys.Right, withShift: true);
        EditorState state = host.State();
        Assert(state.SelectionStart == 2 && state.SelectionLength == 9,
            $"Command+Shift+Right produced selection {state.SelectionStart}:{state.SelectionLength}.");
    }

    private static void CheckCommandDeletion(HookHost host)
    {
        host.Reset("alpha bravo", caret: 8);
        PressChord(host.CommandKey, VirtualKeys.Backspace);
        EditorState state = host.State();
        Assert(state.Text == "avo" && state.SelectionStart == 0,
            $"Command+Backspace produced '{state.Text}' at {state.SelectionStart}.");
    }

    private static void CheckCommandOrdinaryShortcut(HookHost host)
    {
        host.Reset("alpha bravo", caret: 2);
        PressChord(host.CommandKey, action: 0x41);
        EditorState state = host.State();
        Assert(state.SelectionStart == 0 && state.SelectionLength == 11,
            $"Command+A produced selection {state.SelectionStart}:{state.SelectionLength}.");
    }

    private static void CheckOptionNavigation(HookHost host)
    {
        host.Reset("alpha bravo", caret: 8);
        PressChord(host.OptionKey, VirtualKeys.Left);
        EditorState state = host.State();
        Assert(state.SelectionStart == 6 && state.SelectionLength == 0,
            $"Option+Left produced selection {state.SelectionStart}:{state.SelectionLength}.");
    }

    private static void CheckOptionSelection(HookHost host)
    {
        host.Reset("alpha bravo", caret: 8);
        PressChord(host.OptionKey, VirtualKeys.Left, withShift: true);
        EditorState state = host.State();
        Assert(state.SelectionStart == 6 && state.SelectionLength == 2,
            $"Option+Shift+Left produced selection {state.SelectionStart}:{state.SelectionLength}.");
    }

    private static void CheckOptionRightNavigation(HookHost host)
    {
        host.Reset("alpha bravo", caret: 2);
        PressChord(host.OptionKey, VirtualKeys.Right);
        EditorState state = host.State();
        Assert(state.SelectionStart == 6 && state.SelectionLength == 0,
            $"Option+Right produced selection {state.SelectionStart}:{state.SelectionLength}.");
    }

    private static void CheckOptionRightSelection(HookHost host)
    {
        host.Reset("alpha bravo", caret: 2);
        PressChord(host.OptionKey, VirtualKeys.Right, withShift: true);
        EditorState state = host.State();
        Assert(state.SelectionStart == 2 && state.SelectionLength == 4,
            $"Option+Shift+Right produced selection {state.SelectionStart}:{state.SelectionLength}.");
    }

    private static void CheckOptionDeletion(HookHost host)
    {
        host.Reset("alpha bravo", caret: 8);
        PressChord(host.OptionKey, VirtualKeys.Backspace);
        EditorState state = host.State();
        Assert(state.Text == "alpha avo" && state.SelectionStart == 6,
            $"Option+Backspace produced '{state.Text}' at {state.SelectionStart}.");
    }

    private static void CheckOrdinaryPrintableOptionShortcuts(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        PressDelayedChord(host.OptionKey, action: 0x58, delayMilliseconds: 250);
        Assert(host.LastAltShortcutKey() == Keys.X,
            $"Windows did not receive Alt+X as an ordinary shortcut; received {host.LastAltShortcutKey()?.ToString() ?? "none"}.");
        AssertOptionModifiersReleased();

        host.Reset(string.Empty, caret: 0);
        PressDelayedChord(host.OptionKey, action: VirtualKeys.Z, delayMilliseconds: 250);
        Assert(host.LastAltShortcutKey() == Keys.Y,
            $"Windows did not receive physical Alt+Z through the configured Z-to-Y swap; received {host.LastAltShortcutKey()?.ToString() ?? "none"}.");
        AssertOptionModifiersReleased();
    }

    private static void CheckOptionXThenEscapeRemainsNormal(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        PressDelayedChord(host.OptionKey, action: 0x58, delayMilliseconds: 350);
        SendStroke(KeyboardStroke.Down(0x1B));
        SendStroke(KeyboardStroke.Up(0x1B));
        EditorState state = host.State();
        Assert(state.IsForeground && state.IsFocused,
            "Escape behaved like an Alt shortcut after Option+X was released.");
        AssertOptionModifiersReleased();
    }

    private static void CheckOwnedAltJ(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        PressChord(host.OptionKey, action: 0x4A);
        EditorState state = host.State();
        Assert(state.Text == "'",
            $"The owned Alt+J text mapping produced '{state.Text}'.");
    }

    private static void CheckDelayedOwnedAltJ(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        PressDelayedChord(host.OptionKey, action: 0x4A, delayMilliseconds: 350);
        EditorState state = host.State();
        Assert(state.Text == "'",
            $"Holding Left Alt before J produced '{state.Text}' instead of an apostrophe.");
    }

    private static void CheckOptionUnicodeSymbol(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        PressChord(host.OptionKey, action: 0x37);
        EditorState state = host.State();
        Assert(state.Text == "&",
            $"Left Alt+7 produced '{state.Text}' instead of '&' on the active Czech QWERTY layout.");
    }

    private static void CheckCopilotMapsToRightWindows(
        ushort? prefixModifier)
    {
        using var spy = new RightWindowsSpy();
        spy.Start();
        try
        {
            if (prefixModifier is ushort modifier)
            {
                SendStroke(KeyboardStroke.Down(modifier));
                SendStroke(KeyboardStroke.Down(VirtualKeys.LeftShift));
            }
            SendStroke(KeyboardStroke.Down(VirtualKeys.F23));
            Thread.Sleep(75);
            Assert(spy.RightWindowsDownCount == 1,
                $"Copilot did not hold Right Win from F23-down (prefix=0x{prefixModifier:X2}, observed={spy.RightWindowsDownCount}).");
            Assert(spy.RightWindowsUpCount == 0,
                "Copilot released Right Win while its physical F23 component was still held.");

            // The Zephyrus repeats all three macro key-downs, not only F23.
            // Repeats must remain suppressed and must not create extra Win downs.
            if (prefixModifier is ushort repeatedModifier)
            {
                SendStroke(KeyboardStroke.Down(repeatedModifier));
                SendStroke(KeyboardStroke.Down(VirtualKeys.LeftShift));
            }
            SendStroke(KeyboardStroke.Down(VirtualKeys.F23));
            Thread.Sleep(75);
            Assert(spy.RightWindowsDownCount == 1,
                $"A repeating Copilot macro created {spy.RightWindowsDownCount} Right Win key-downs.");

            SendStroke(KeyboardStroke.Up(VirtualKeys.F23));
            if (prefixModifier is not null)
            {
                Thread.Sleep(50);
                Assert(spy.RightWindowsUpCount == 0,
                    "Copilot released Right Win before the OEM macro modifiers unwound.");
            }
            if (prefixModifier is ushort releasedModifier)
            {
                SendStroke(KeyboardStroke.Up(VirtualKeys.LeftShift));
                SendStroke(KeyboardStroke.Up(releasedModifier));
            }
            Thread.Sleep(150);
            Assert(spy.RightWindowsDownCount == 1,
                $"Copilot macro did not produce exactly one Right Win key-down (prefix=0x{prefixModifier:X2}, observed={spy.RightWindowsDownCount}).");
            Assert(spy.RightWindowsUpCount >= 1,
                "Copilot F23-up did not release Right Win.");
            Assert((NativeMethods.GetAsyncKeyState(VirtualKeys.RightWindows) & 0x8000) == 0,
                "Copilot F23 left Right Win held after release.");
        }
        finally
        {
            TryRelease(VirtualKeys.F23);
            TryRelease(VirtualKeys.LeftShift);
            if (prefixModifier is ushort modifier)
            {
                TryRelease(modifier);
            }
            TryRelease(VirtualKeys.RightWindows);
        }

        SendStroke(KeyboardStroke.Down(0x1B));
        SendStroke(KeyboardStroke.Up(0x1B));
    }

    private static void TryRelease(ushort key)
    {
        try
        {
            SendStroke(KeyboardStroke.Up(key));
        }
        catch (InvalidOperationException)
        {
            // Best-effort cleanup after an earlier assertion or SendInput failure.
        }
    }

    private static void PressChord(ushort modifier, ushort action, bool withShift = false)
    {
        var strokes = new List<KeyboardStroke>
        {
            KeyboardStroke.Down(modifier),
        };
        if (withShift)
        {
            strokes.Add(KeyboardStroke.Down(VirtualKeys.LeftShift));
        }
        strokes.Add(KeyboardStroke.Down(action));
        strokes.Add(KeyboardStroke.Up(action));
        if (withShift)
        {
            strokes.Add(KeyboardStroke.Up(VirtualKeys.LeftShift));
        }
        strokes.Add(KeyboardStroke.Up(modifier));

        foreach (KeyboardStroke stroke in strokes)
        {
            SendStroke(stroke);
        }
    }

    private static void PressDelayedChord(ushort modifier, ushort action, int delayMilliseconds)
    {
        SendStroke(KeyboardStroke.Down(modifier));
        Thread.Sleep(delayMilliseconds);
        SendStroke(KeyboardStroke.Down(action));
        SendStroke(KeyboardStroke.Up(action));
        SendStroke(KeyboardStroke.Up(modifier));
    }

    private static void SendStroke(KeyboardStroke stroke, ushort scanCode = 0)
    {
        var input = new NativeMethods.INPUT
        {
            Type = 1,
            Data = new NativeMethods.INPUTUNION
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = stroke.VirtualKey,
                    ScanCode = scanCode,
                    Flags = (stroke.IsKeyUp ? 0x0002u : 0) | (IsExtended(stroke.VirtualKey) ? 0x0001u : 0),
                },
            },
        };

        uint sent = NativeMethods.SendInput(1, [input], Marshal.SizeOf<NativeMethods.INPUT>());
        Assert(sent == 1, $"SendInput rejected a test event. Error: {Marshal.GetLastWin32Error()}.");
        Thread.Sleep(10);
    }

    private static bool IsExtended(ushort key) => key is
        VirtualKeys.LeftWindows or VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down;

    private static void AssertOptionModifiersReleased()
    {
        ushort[] keys =
        [
            VirtualKeys.LeftAlt,
            VirtualKeys.RightAlt,
            VirtualKeys.LeftControl,
            VirtualKeys.RightControl,
        ];
        ushort[] held = keys
            .Where(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)
            .ToArray();
        Assert(held.Length == 0,
            $"Option chord left modifier virtual keys held: {string.Join(", ", held.Select(key => $"0x{key:X2}"))}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record EditorState(
        string Text,
        int SelectionStart,
        int SelectionLength,
        long ObservedEvents,
        long TranslatedActions,
        bool IsForeground,
        bool IsFocused);

    private sealed class RightWindowsSpy : IDisposable
    {
        private const int LowLevelKeyboardHook = 13;
        private const int KeyDown = 0x0100;
        private const int KeyUp = 0x0101;
        private const int SystemKeyDown = 0x0104;
        private const int SystemKeyUp = 0x0105;

        private readonly NativeMethods.LowLevelKeyboardProc _callback;
        private nint _hook;
        private int _rightWindowsDownCount;
        private int _rightWindowsUpCount;
        private int _measuredPunctuationDownCount;
        private int _measuredPunctuationUpCount;
        private int _appExposeReplayDownCount;
        private int _appExposeReplayUpCount;

        internal RightWindowsSpy()
        {
            _callback = Observe;
        }

        internal int RightWindowsDownCount => Volatile.Read(ref _rightWindowsDownCount);
        internal int RightWindowsUpCount => Volatile.Read(ref _rightWindowsUpCount);
        internal int MeasuredPunctuationDownCount => Volatile.Read(ref _measuredPunctuationDownCount);
        internal int MeasuredPunctuationUpCount => Volatile.Read(ref _measuredPunctuationUpCount);
        internal int AppExposeReplayDownCount => Volatile.Read(ref _appExposeReplayDownCount);
        internal int AppExposeReplayUpCount => Volatile.Read(ref _appExposeReplayUpCount);

        internal void Start()
        {
            _hook = NativeMethods.SetWindowsHookEx(LowLevelKeyboardHook, _callback, nint.Zero, 0);
            Assert(_hook != nint.Zero, "The Right Win observer hook could not be installed.");
        }

        private nint Observe(int code, nint message, nint data)
        {
            if (code >= 0)
            {
                NativeMethods.KBDLLHOOKSTRUCT keyboard = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(data);
                if (keyboard.VirtualKey == VirtualKeys.RightWindows &&
                    keyboard.ExtraInfo == MacKeyboardHook.ReplayMarker)
                {
                    if (message.ToInt32() is KeyDown or SystemKeyDown)
                    {
                        Interlocked.Increment(ref _rightWindowsDownCount);
                    }
                    else if (message.ToInt32() is KeyUp or SystemKeyUp)
                    {
                        Interlocked.Increment(ref _rightWindowsUpCount);
                    }
                }
                else if (keyboard.VirtualKey == 0xBF && keyboard.ScanCode == 0x35)
                {
                    if (message.ToInt32() is KeyDown or SystemKeyDown)
                    {
                        Interlocked.Increment(ref _measuredPunctuationDownCount);
                    }
                    else if (message.ToInt32() is KeyUp or SystemKeyUp)
                    {
                        Interlocked.Increment(ref _measuredPunctuationUpCount);
                    }
                }
                else if (keyboard.VirtualKey == VirtualKeys.RightWindows && keyboard.ExtraInfo == 0x415850)
                {
                    if (message.ToInt32() is KeyDown or SystemKeyDown)
                    {
                        Interlocked.Increment(ref _appExposeReplayDownCount);
                    }
                    else if (message.ToInt32() is KeyUp or SystemKeyUp)
                    {
                        Interlocked.Increment(ref _appExposeReplayUpCount);
                    }
                }
            }

            return NativeMethods.CallNextHookEx(_hook, code, message, data);
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

    private sealed class HookHost : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new(initialState: false);
        private readonly bool _installTestHook;
        private Thread? _thread;
        private Form? _form;
        private TextBox? _editor;
        private MacKeyboardHook? _hook;
        private Exception? _startupException;
        private Keys? _lastAltShortcutKey;

        private readonly ushort? _copilotKey;
        private readonly bool _visibleTaskWindow;

        internal HookHost(
            bool installTestHook,
            ushort commandKey,
            ushort optionKey,
            ushort? copilotKey = null,
            bool visibleTaskWindow = false)
        {
            _installTestHook = installTestHook;
            _copilotKey = copilotKey;
            _visibleTaskWindow = visibleTaskWindow;
            CommandKey = commandKey;
            OptionKey = optionKey;
        }

        internal ushort CommandKey { get; }
        internal ushort OptionKey { get; }

        internal void Start()
        {
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "Mac Controls integration hook host",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The integration-test hook host did not start.");
            }
            if (_startupException is not null)
            {
                throw new InvalidOperationException("The integration-test hook host failed.", _startupException);
            }
        }

        internal void Reset(string text, int caret)
        {
            Invoke(() =>
            {
                _editor!.Text = text;
                _editor.SelectionStart = caret;
                _editor.SelectionLength = 0;
                _lastAltShortcutKey = null;
                FocusEditor(_form!, _editor);
            });
        }

        internal Keys? LastAltShortcutKey() => Invoke(() => _lastAltShortcutKey);

        internal EditorState State()
        {
            // The installed app deliberately posts Option edits until after the
            // originating low-level hook callback. Let that separate UI thread
            // drain its message before observing the editor.
            Thread.Sleep(75);
            return Invoke(() =>
            {
                Application.DoEvents();
                return new EditorState(
                    _editor!.Text,
                    _editor.SelectionStart,
                    _editor.SelectionLength,
                    _hook?.ObservedEventCount ?? 0,
                    _hook?.TranslatedActionCount ?? 0,
                    NativeMethods.GetForegroundWindow() == _form!.Handle,
                    _editor.Focused);
            });
        }

        private void RunMessageLoop()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(defaultValue: false);
                if (_installTestHook)
                {
                    // Test-only F23/F24 modifiers avoid the user's active
                    // mappings while exercising the same hook and output path.
                    _hook = new MacKeyboardHook(
                        commandKey: CommandKey,
                        optionKey: OptionKey,
                        copilotKey: _copilotKey);
                    if (!_hook.Start())
                    {
                        throw new InvalidOperationException("The low-level keyboard hook could not be installed.");
                    }
                }

                using var form = new Form
                {
                    Text = "Mac Controls integration check",
                    KeyPreview = true,
                    ShowInTaskbar = _visibleTaskWindow,
                    StartPosition = FormStartPosition.Manual,
                    Location = _visibleTaskWindow ? new Point(80, 80) : new Point(-2000, -2000),
                    Size = new Size(320, 120),
                };
                using var editor = new TextBox
                {
                    Multiline = true,
                    Dock = DockStyle.Fill,
                };
                _form = form;
                _editor = editor;
                form.KeyDown += (_, eventArgs) =>
                {
                    if (eventArgs.Alt)
                    {
                        _lastAltShortcutKey = eventArgs.KeyCode;
                    }
                };
                form.Controls.Add(editor);
                form.Shown += (_, _) =>
                {
                    FocusEditor(form, editor);
                    _ready.Set();
                };
                Application.Run(form);
            }
            catch (Exception exception)
            {
                _startupException = exception;
                _ready.Set();
            }
            finally
            {
                _hook?.Dispose();
                _hook = null;
            }
        }

        private static void FocusEditor(Form form, TextBox editor)
        {
            nint previousForeground = NativeMethods.GetForegroundWindow();
            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(previousForeground, out _);
            uint currentThread = NativeMethods.GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, attach: true);
            try
            {
                NativeMethods.BringWindowToTop(form.Handle);
                NativeMethods.SetForegroundWindow(form.Handle);
                form.Activate();
                NativeMethods.SetFocus(editor.Handle);
                editor.Focus();
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThread, foregroundThread, attach: false);
                }
            }
        }

        private void Invoke(Action action)
        {
            _form!.Invoke(action);
        }

        private T Invoke<T>(Func<T> action)
        {
            return (T)_form!.Invoke(action);
        }

        public void Dispose()
        {
            if (_form is not null && !_form.IsDisposed)
            {
                _form.BeginInvoke(_form.Close);
            }
            if (_thread is not null && !_thread.Join(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The integration-test hook host did not stop.");
            }
            _ready.Dispose();
        }
    }
}

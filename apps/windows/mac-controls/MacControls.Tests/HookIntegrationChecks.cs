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
            CheckLostOptionKeyUpRecovery,
        ];
        foreach (Action<HookHost> check in checks)
        {
            using var host = new HookHost(
                installTestHook: true,
                commandKey: VirtualKeys.F24,
                optionKey: VirtualKeys.F23);
            host.Start();
            check(host);
        }
    }

    internal static void RunInstalled()
    {
        if (System.Diagnostics.Process.GetProcessesByName("MacControls").Length != 1)
        {
            throw new InvalidOperationException("Exactly one installed MacControls process must be running.");
        }

        using var host = new HookHost(
            installTestHook: false,
            commandKey: VirtualKeys.LeftWindows,
            optionKey: VirtualKeys.LeftAlt);
        host.Start();

        RunChecks(host);
        CheckOwnedAltJ(host);
        CheckOptionAltGrSymbol(host);
    }

    internal static void RunInstalledSymbols()
    {
        if (System.Diagnostics.Process.GetProcessesByName("MacControls").Length != 1)
        {
            throw new InvalidOperationException("Exactly one installed MacControls process must be running.");
        }

        using var host = new HookHost(
            installTestHook: false,
            commandKey: VirtualKeys.LeftWindows,
            optionKey: VirtualKeys.LeftAlt);
        host.Start();

        CheckOwnedAltJ(host);
        CheckOptionAltGrSymbol(host);
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
            $"Command+Shift+Left produced selection {state.SelectionStart}:{state.SelectionLength}.");
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

    private static void CheckLostOptionKeyUpRecovery(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        SendStroke(KeyboardStroke.Down(host.OptionKey));
        SendStroke(KeyboardStroke.Down(0x37));
        SendStroke(KeyboardStroke.Up(0x37));
        Assert(host.OwnsSyntheticOptionModifier(),
            "The hook did not record ownership of its synthetic AltGr modifier.");

        host.RecoverReleasedModifiersUsingWindowsState();
        Assert(host.OwnsSyntheticOptionModifier(),
            "Recovery released AltGr while its physical source key was still down.");

        host.RecoverReleasedModifiersForTest();
        Assert(!host.OwnsSyntheticOptionModifier(),
            "Recovery did not release the synthetic AltGr modifier after a lost key-up.");

        // Balance the test-only physical F23 state after simulating a dropped
        // hook notification for its release.
        SendStroke(KeyboardStroke.Up(host.OptionKey));

        SendStroke(KeyboardStroke.Down(host.OptionKey));
        SendStroke(KeyboardStroke.Down(0x85));
        SendStroke(KeyboardStroke.Up(0x85));
        Assert(host.OwnsSyntheticOptionModifier(),
            "The hook did not record ownership of its synthetic ordinary Alt modifier.");
        host.RecoverReleasedModifiersForTest();
        Assert(!host.OwnsSyntheticOptionModifier(),
            "Recovery did not release synthetic ordinary Alt after a lost key-up.");
        SendStroke(KeyboardStroke.Up(host.OptionKey));
    }

    private static void CheckOwnedAltJ(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        PressChord(host.OptionKey, action: 0x4A);
        EditorState state = host.State();
        Assert(state.Text == "'",
            $"The owned Alt+J text mapping produced '{state.Text}'.");
    }

    private static void CheckOptionAltGrSymbol(HookHost host)
    {
        host.Reset(string.Empty, caret: 0);
        PressChord(host.OptionKey, action: 0x37);
        EditorState state = host.State();
        Assert(state.Text == "&",
            $"Left Alt+7 produced '{state.Text}' instead of '&' on the active Czech QWERTY layout.");
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

    private static void SendStroke(KeyboardStroke stroke)
    {
        var input = new NativeMethods.INPUT
        {
            Type = 1,
            Data = new NativeMethods.INPUTUNION
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = stroke.VirtualKey,
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

    private sealed class HookHost : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new(initialState: false);
        private readonly bool _installTestHook;
        private Thread? _thread;
        private Form? _form;
        private TextBox? _editor;
        private MacKeyboardHook? _hook;
        private Exception? _startupException;

        internal HookHost(bool installTestHook, ushort commandKey, ushort optionKey)
        {
            _installTestHook = installTestHook;
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
                _editor.Focus();
            });
        }

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

        internal bool OwnsSyntheticOptionModifier() =>
            Invoke(() => _hook?.OwnsSyntheticOptionModifier ?? false);

        internal void RecoverReleasedModifiersForTest() =>
            Invoke(() => _hook!.RecoverReleasedModifiers(_ => false));

        internal void RecoverReleasedModifiersUsingWindowsState() =>
            Invoke(() => _hook!.RecoverReleasedModifiers());

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
                        optionKey: OptionKey);
                    if (!_hook.Start())
                    {
                        throw new InvalidOperationException("The low-level keyboard hook could not be installed.");
                    }
                }

                using var form = new Form
                {
                    Text = "Mac Controls integration check",
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-2000, -2000),
                    Size = new Size(320, 120),
                };
                using var editor = new TextBox
                {
                    Multiline = true,
                    Dock = DockStyle.Fill,
                };
                _form = form;
                _editor = editor;
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

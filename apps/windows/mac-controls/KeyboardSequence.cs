namespace PlebTools.MacControls;

internal readonly record struct KeyboardStroke(ushort VirtualKey, bool IsKeyUp)
{
    internal static KeyboardStroke Down(ushort key) => new(key, IsKeyUp: false);

    internal static KeyboardStroke Up(ushort key) => new(key, IsKeyUp: true);
}

/// <summary>
/// Builds deterministic output sequences while the physical modifier state is held.
/// Physical Shift is reasserted in Command sequences. Option sequences balance
/// any synthetic Shift around their action so it cannot remain stuck.
/// </summary>
internal static class KeyboardSequence
{
    internal static IReadOnlyList<KeyboardStroke> ForCommand(ushort actionKey, ushort? heldShiftKey)
    {
        return actionKey switch
        {
            VirtualKeys.Left => WithoutMappedControl(TapWithHeldShift(VirtualKeys.Home, heldShiftKey)),
            VirtualKeys.Right => WithoutMappedControl(TapWithHeldShift(VirtualKeys.End, heldShiftKey)),
            VirtualKeys.Up => TapWithHeldShift(VirtualKeys.Home, heldShiftKey),
            VirtualKeys.Down => TapWithHeldShift(VirtualKeys.End, heldShiftKey),
            VirtualKeys.Backspace => DeleteToLineStart(heldShiftKey),
            _ => Array.Empty<KeyboardStroke>(),
        };
    }

    internal static IReadOnlyList<KeyboardStroke> ForOption(
        ushort actionKey,
        ushort? heldShiftKey = null)
    {
        if (!VirtualKeys.IsEditingAction(actionKey))
        {
            return Array.Empty<KeyboardStroke>();
        }

        var output = new List<KeyboardStroke>
        {
            KeyboardStroke.Down(VirtualKeys.RightControl),
        };
        if (heldShiftKey is ushort shiftKey)
        {
            // SendInput invoked inside a low-level hook can be interleaved with
            // the remainder of the originating batch. Reasserting held Shift
            // makes selection deterministic without releasing physical Shift.
            output.Add(KeyboardStroke.Down(shiftKey));
        }
        output.AddRange(Tap(actionKey));
        if (heldShiftKey is ushort balancedShiftKey)
        {
            output.Add(KeyboardStroke.Up(balancedShiftKey));
        }
        output.AddRange(
        [
            KeyboardStroke.Up(VirtualKeys.RightControl),
        ]);
        return output;
    }

    private static IReadOnlyList<KeyboardStroke> WithoutMappedControl(IReadOnlyList<KeyboardStroke> action)
    {
        var output = new List<KeyboardStroke>(action.Count + 2)
        {
            KeyboardStroke.Up(VirtualKeys.LeftControl),
        };
        output.AddRange(action);
        output.Add(KeyboardStroke.Down(VirtualKeys.LeftControl));
        return output;
    }

    private static IReadOnlyList<KeyboardStroke> DeleteToLineStart(ushort? heldShiftKey)
    {
        var output = new List<KeyboardStroke>
        {
            KeyboardStroke.Up(VirtualKeys.LeftControl),
        };

        ushort shiftKey = heldShiftKey ?? VirtualKeys.LeftShift;
        output.Add(KeyboardStroke.Down(shiftKey));

        output.AddRange(Tap(VirtualKeys.Home));

        if (heldShiftKey is null)
        {
            output.Add(KeyboardStroke.Up(shiftKey));
        }

        output.AddRange(Tap(VirtualKeys.Backspace));
        output.Add(KeyboardStroke.Down(VirtualKeys.LeftControl));
        return output;
    }

    private static IReadOnlyList<KeyboardStroke> Tap(ushort key) =>
    [
        KeyboardStroke.Down(key),
        KeyboardStroke.Up(key),
    ];

    private static IReadOnlyList<KeyboardStroke> TapWithHeldShift(ushort key, ushort? heldShiftKey)
    {
        var output = new List<KeyboardStroke>(heldShiftKey is null ? 2 : 3);
        if (heldShiftKey is ushort shiftKey)
        {
            output.Add(KeyboardStroke.Down(shiftKey));
        }
        output.AddRange(Tap(key));
        return output;
    }
}

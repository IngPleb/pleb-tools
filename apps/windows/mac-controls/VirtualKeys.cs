namespace PlebTools.MacControls;

internal static class VirtualKeys
{
    internal const ushort Backspace = 0x08;
    internal const ushort Shift = 0x10;
    internal const ushort Control = 0x11;
    internal const ushort Alt = 0x12;
    internal const ushort Home = 0x24;
    internal const ushort Left = 0x25;
    internal const ushort Up = 0x26;
    internal const ushort Right = 0x27;
    internal const ushort Down = 0x28;
    internal const ushort End = 0x23;
    internal const ushort LeftShift = 0xA0;
    internal const ushort RightShift = 0xA1;
    internal const ushort LeftControl = 0xA2;
    internal const ushort RightControl = 0xA3;
    internal const ushort LeftAlt = 0xA4;
    internal const ushort RightAlt = 0xA5;
    internal const ushort LeftWindows = 0x5B;
    internal const ushort RightWindows = 0x5C;
    internal const ushort F21 = 0x84;
    internal const ushort F22 = 0x85;
    internal const ushort F23 = 0x86;
    internal const ushort F24 = 0x87;

    internal const ushort Y = 0x59;
    internal const ushort Z = 0x5A;
    internal const ushort J = 0x4A;
    internal const ushort Oem5 = 0xDC;
    internal const ushort Oem102 = 0xE2;

    internal static bool IsNavigation(ushort key) => key is Left or Right or Up or Down;

    internal static bool IsEditingAction(ushort key) => IsNavigation(key) || key == Backspace;

    internal static bool IsPrintable(ushort key) =>
        key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x6F or
        >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xE2;
}

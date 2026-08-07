namespace PlebTools.AppExpose;

/// <summary>Describes a switchable top-level window owned by the focused application.</summary>
public sealed record AppWindow(nint Handle, string Title, string ProcessName);

using System.Text.Json;
using PlebTools.MacControls;

if (args.Contains("--installed", StringComparer.OrdinalIgnoreCase))
{
    HookIntegrationChecks.RunInstalled();
    Console.WriteLine("PASS installed Mac Controls edits a real Windows text control");
    return 0;
}

if (args.Contains("--installed-symbols", StringComparer.OrdinalIgnoreCase))
{
    HookIntegrationChecks.RunInstalledSymbols();
    Console.WriteLine("PASS installed Mac Controls owns personal mappings and the AltGr symbol layer");
    return 0;
}

if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
{
    HookIntegrationChecks.Run();
    Console.WriteLine("PASS low-level hook edits a real Windows text control");
    return 0;
}

var tests = new (string Name, Action Run)[]
{
    ("Command Left maps to line start", CommandLeftMapsToLineStart),
    ("Command Right maps to line end", CommandRightMapsToLineEnd),
    ("Command Up maps to document start", CommandUpMapsToDocumentStart),
    ("Command Down maps to document end", CommandDownMapsToDocumentEnd),
    ("Command Backspace deletes to line start", CommandBackspaceDeletesToLineStart),
    ("Command Shift Backspace preserves held Shift", CommandShiftBackspacePreservesShift),
    ("Command Right Shift preserves the correct Shift side", CommandRightShiftPreservesSide),
    ("Option Left maps to previous word", OptionLeftMapsToPreviousWord),
    ("Option Right maps to next word", OptionRightMapsToNextWord),
    ("Option Up maps to previous paragraph", OptionUpMapsToPreviousParagraph),
    ("Option Down maps to next paragraph", OptionDownMapsToNextParagraph),
    ("Option Backspace maps to previous-word deletion", OptionBackspaceMapsToWordDeletion),
    ("Unrelated keys have no translation", UnrelatedKeysHaveNoTranslation),
    ("PowerToys overlap is detected", PowerToysOverlapIsDetected),
    ("PowerToys app-specific overlap is detected", PowerToysAppSpecificOverlapIsDetected),
    ("Malformed PowerToys settings fail closed", MalformedPowerToysSettingsFailClosed),
    ("Unrelated PowerToys mappings are accepted", UnrelatedPowerToysMappingsAreAccepted),
    ("Owned personal key mappings are deterministic", OwnedPersonalKeyMappingsAreDeterministic),
    ("Printable Option chords use AltGr", PrintableOptionChordsUseAltGr),
    ("Synthetic Option releases match their injected modifier", SyntheticOptionReleasesMatchModifier),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static void CommandLeftMapsToLineStart()
{
    AssertSequence(
        KeyboardSequence.ForCommand(VirtualKeys.Left, heldShiftKey: null),
        KeyboardStroke.Up(VirtualKeys.LeftControl),
        KeyboardStroke.Down(VirtualKeys.Home),
        KeyboardStroke.Up(VirtualKeys.Home),
        KeyboardStroke.Down(VirtualKeys.LeftControl));
}

static void CommandRightMapsToLineEnd()
{
    AssertSequence(
        KeyboardSequence.ForCommand(VirtualKeys.Right, heldShiftKey: null),
        KeyboardStroke.Up(VirtualKeys.LeftControl),
        KeyboardStroke.Down(VirtualKeys.End),
        KeyboardStroke.Up(VirtualKeys.End),
        KeyboardStroke.Down(VirtualKeys.LeftControl));
}

static void CommandUpMapsToDocumentStart()
{
    AssertSequence(
        KeyboardSequence.ForCommand(VirtualKeys.Up, heldShiftKey: null),
        KeyboardStroke.Down(VirtualKeys.Home),
        KeyboardStroke.Up(VirtualKeys.Home));
}

static void CommandDownMapsToDocumentEnd()
{
    AssertSequence(
        KeyboardSequence.ForCommand(VirtualKeys.Down, VirtualKeys.LeftShift),
        KeyboardStroke.Down(VirtualKeys.LeftShift),
        KeyboardStroke.Down(VirtualKeys.End),
        KeyboardStroke.Up(VirtualKeys.End));
}

static void CommandBackspaceDeletesToLineStart()
{
    AssertSequence(
        KeyboardSequence.ForCommand(VirtualKeys.Backspace, heldShiftKey: null),
        KeyboardStroke.Up(VirtualKeys.LeftControl),
        KeyboardStroke.Down(VirtualKeys.LeftShift),
        KeyboardStroke.Down(VirtualKeys.Home),
        KeyboardStroke.Up(VirtualKeys.Home),
        KeyboardStroke.Up(VirtualKeys.LeftShift),
        KeyboardStroke.Down(VirtualKeys.Backspace),
        KeyboardStroke.Up(VirtualKeys.Backspace),
        KeyboardStroke.Down(VirtualKeys.LeftControl));
}

static void CommandShiftBackspacePreservesShift()
{
    IReadOnlyList<KeyboardStroke> sequence =
        KeyboardSequence.ForCommand(VirtualKeys.Backspace, VirtualKeys.LeftShift);
    Assert(sequence.Count(stroke => stroke == KeyboardStroke.Down(VirtualKeys.LeftShift)) == 1,
        "The sequence did not reassert held Shift exactly once.");
    Assert(!sequence.Any(stroke => stroke == KeyboardStroke.Up(VirtualKeys.LeftShift)),
        "The synchronous sequence released physical Shift.");
}

static void CommandRightShiftPreservesSide()
{
    IReadOnlyList<KeyboardStroke> sequence =
        KeyboardSequence.ForCommand(VirtualKeys.Left, VirtualKeys.RightShift);
    Assert(sequence.Any(stroke => stroke == KeyboardStroke.Down(VirtualKeys.RightShift)),
        "The sequence did not reassert Right Shift.");
    Assert(!sequence.Any(stroke => stroke.VirtualKey == VirtualKeys.LeftShift),
        "The sequence introduced Left Shift while only Right Shift was held.");
    Assert(!sequence.Any(stroke => stroke == KeyboardStroke.Up(VirtualKeys.RightShift)),
        "The synchronous sequence released physical Right Shift.");
}

static void OptionLeftMapsToPreviousWord()
{
    AssertSequence(
        KeyboardSequence.ForOption(VirtualKeys.Left),
        KeyboardStroke.Down(VirtualKeys.RightControl),
        KeyboardStroke.Down(VirtualKeys.Left),
        KeyboardStroke.Up(VirtualKeys.Left),
        KeyboardStroke.Up(VirtualKeys.RightControl));
}

static void OptionRightMapsToNextWord()
{
    AssertOptionAction(VirtualKeys.Right);
}

static void OptionUpMapsToPreviousParagraph()
{
    AssertOptionAction(VirtualKeys.Up);
}

static void OptionDownMapsToNextParagraph()
{
    AssertOptionAction(VirtualKeys.Down);
}

static void OptionBackspaceMapsToWordDeletion()
{
    IReadOnlyList<KeyboardStroke> sequence = KeyboardSequence.ForOption(VirtualKeys.Backspace);
    Assert(sequence.Any(stroke => stroke == KeyboardStroke.Down(VirtualKeys.Backspace)),
        "The sequence did not press Backspace.");
    Assert(sequence.First() == KeyboardStroke.Down(VirtualKeys.RightControl),
        "The sequence did not establish the word-navigation modifier.");
}

static void AssertOptionAction(ushort action)
{
    AssertSequence(
        KeyboardSequence.ForOption(action),
        KeyboardStroke.Down(VirtualKeys.RightControl),
        KeyboardStroke.Down(action),
        KeyboardStroke.Up(action),
        KeyboardStroke.Up(VirtualKeys.RightControl));
}

static void UnrelatedKeysHaveNoTranslation()
{
    Assert(KeyboardSequence.ForCommand(0x43, heldShiftKey: null).Count == 0,
        "Command+C was unexpectedly translated instead of using the modifier remap.");
    Assert(KeyboardSequence.ForOption(0x43).Count == 0,
        "Option+C was unexpectedly translated.");
}

static void PowerToysOverlapIsDetected()
{
    WithPowerToysConfiguration(
        """
        {
          "remapKeys": {"inProcess": [{"originalKeys": "91", "newRemapKeys": "162"}]},
          "remapShortcuts": {"global": [{"originalKeys": "164;37", "newRemapKeys": "163;37"}]}
        }
        """,
        report => Assert(report.Conflicts.Count == 2, $"Expected 2 conflicts, got {report.Conflicts.Count}."));
}

static void UnrelatedPowerToysMappingsAreAccepted()
{
    WithPowerToysConfiguration(
        """
        {
          "remapKeys": {"inProcess": [{"originalKeys": "65", "newRemapKeys": "66"}]},
          "remapShortcuts": {"global": []},
          "remapShortcutsToText": {"global": [{"originalKeys": "18;75", "unicodeText": "x"}]}
        }
        """,
        report =>
        {
            Assert(report.Conflicts.Count == 0, "Unrelated mappings were reported as conflicts.");
            Assert(report.LeftAltTextShortcuts.TryGetValue(75, out string? text) && text == "x",
                "The preserved Left Alt text shortcut was not loaded.");
        });
}

static void OwnedPersonalKeyMappingsAreDeterministic()
{
    Assert(MacKeyboardHook.RemapOwnedKey(VirtualKeys.Y) == VirtualKeys.Z, "Y did not map to Z.");
    Assert(MacKeyboardHook.RemapOwnedKey(VirtualKeys.Z) == VirtualKeys.Y, "Z did not map to Y.");
    Assert(MacKeyboardHook.RemapOwnedKey(VirtualKeys.Oem5) == VirtualKeys.Oem102,
        "OEM 5 did not map to OEM 102.");
    Assert(MacKeyboardHook.RemapOwnedKey(0x41) == 0x41, "An unrelated key was remapped.");
}

static void PrintableOptionChordsUseAltGr()
{
    Assert(MacKeyboardHook.OptionModifierFor(0x37) == VirtualKeys.RightAlt,
        "Option+7 did not select the AltGr layer.");
    Assert(MacKeyboardHook.OptionModifierFor(VirtualKeys.Left) == VirtualKeys.LeftAlt,
        "Option+Left was incorrectly classified as an AltGr chord.");
}

static void SyntheticOptionReleasesMatchModifier()
{
    Assert(MacKeyboardHook.OwnedOptionModifierFor(asAltGr: false, VirtualKeys.LeftAlt) == VirtualKeys.LeftAlt,
        "An ordinary Alt chord would release a different modifier than it pressed.");
    Assert(MacKeyboardHook.OwnedOptionModifierFor(asAltGr: true, VirtualKeys.LeftAlt) == VirtualKeys.RightAlt,
        "An AltGr chord would release a different modifier than it pressed.");
}

static void PowerToysAppSpecificOverlapIsDetected()
{
    WithPowerToysConfiguration(
        """
        {
          "remapKeys": {"inProcess": []},
          "remapShortcuts": {
            "global": [],
            "appSpecific": [{"originalKeys": "164;39", "newRemapKeys": "163;39", "targetApp": "notepad"}]
          }
        }
        """,
        report => Assert(report.Conflicts.Count == 1,
            $"Expected 1 app-specific conflict, got {report.Conflicts.Count}."));
}

static void MalformedPowerToysSettingsFailClosed()
{
    string directory = Path.Combine(Path.GetTempPath(), $"mac-controls-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(Path.Combine(directory, "settings.json"), "not json");
        PowerToysConflictReport report = PowerToysConflictDetector.Detect(directory);
        Assert(report.Conflicts.Count == 1, "Malformed settings did not produce a blocking conflict.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void WithPowerToysConfiguration(string configuration, Action<PowerToysConflictReport> assertion)
{
    string directory = Path.Combine(Path.GetTempPath(), $"mac-controls-tests-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(
            Path.Combine(directory, "settings.json"),
            JsonSerializer.Serialize(new
            {
                properties = new
                {
                    activeConfiguration = new { value = "default" },
                },
            }));
        File.WriteAllText(Path.Combine(directory, "default.json"), configuration);
        assertion(PowerToysConflictDetector.Detect(directory));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void AssertSequence(IReadOnlyList<KeyboardStroke> actual, params KeyboardStroke[] expected)
{
    Assert(actual.SequenceEqual(expected),
        $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

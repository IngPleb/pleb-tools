using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlebTools.MacControls;

internal sealed record PowerToysConflictReport(
    string? ConfigurationPath,
    IReadOnlyList<string> Conflicts,
    IReadOnlyDictionary<ushort, string> LeftAltTextShortcuts);

/// <summary>
/// Refuses to start when PowerToys would process the same physical chords.
/// This prevents hook-order-dependent double remapping.
/// </summary>
internal static class PowerToysConflictDetector
{
    private static readonly HashSet<string> EditingShortcutSources =
    [
        "91;8",
        "91;37",
        "91;38",
        "91;39",
        "91;40",
        "164;8",
        "164;37",
        "164;38",
        "164;39",
        "164;40",
    ];
    private static readonly HashSet<string> OwnedKeySources = ["89", "90", "91", "220"];
    private static readonly HashSet<string> OwnedTextShortcutSources = ["18;74", "164;74"];

    internal static PowerToysConflictReport Detect()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "PowerToys",
            "Keyboard Manager");
        return Detect(directory);
    }

    internal static PowerToysConflictReport Detect(string keyboardManagerDirectory)
    {
        string settingsPath = Path.Combine(keyboardManagerDirectory, "settings.json");
        if (!File.Exists(settingsPath))
        {
            return new PowerToysConflictReport(null, [], new Dictionary<ushort, string>());
        }

        try
        {
            JsonNode? settings = JsonNode.Parse(File.ReadAllText(settingsPath));
            string activeConfiguration = settings?["properties"]?["activeConfiguration"]?["value"]?.GetValue<string>()
                ?? "default";
            string configurationPath = Path.Combine(keyboardManagerDirectory, $"{activeConfiguration}.json");
            if (!File.Exists(configurationPath))
            {
                return new PowerToysConflictReport(configurationPath, [], new Dictionary<ushort, string>());
            }

            JsonNode? configuration = JsonNode.Parse(File.ReadAllText(configurationPath));
            var conflicts = new List<string>();
            var leftAltTextShortcuts = new Dictionary<ushort, string>();

            foreach (JsonNode? mapping in ReadMappings(configuration?["remapKeys"]))
            {
                string? source = mapping?["originalKeys"]?.GetValue<string>();
                if (source is not null && OwnedKeySources.Contains(source))
                {
                    conflicts.Add($"PowerToys key source {source}");
                }
            }

            foreach (JsonNode? mapping in ReadMappings(configuration?["remapShortcuts"]))
            {
                string? source = mapping?["originalKeys"]?.GetValue<string>();
                if (source is not null && EditingShortcutSources.Contains(source))
                {
                    conflicts.Add($"PowerToys shortcut source {source}");
                }
            }

            foreach (JsonNode? mapping in ReadMappings(configuration?["remapShortcutsToText"]))
            {
                string? source = mapping?["originalKeys"]?.GetValue<string>();
                string? text = mapping?["unicodeText"]?.GetValue<string>();
                string[] keys = source?.Split(';') ?? [];
                if (keys.Length == 2 && keys[0] is "18" or "164" &&
                    ushort.TryParse(keys[1], out ushort key) && text is not null)
                {
                    leftAltTextShortcuts[key] = text;
                }

                if (source is not null && OwnedTextShortcutSources.Contains(source))
                {
                    conflicts.Add($"PowerToys text shortcut source {source}");
                }
            }

            return new PowerToysConflictReport(
                configurationPath,
                conflicts.Distinct().ToArray(),
                leftAltTextShortcuts);
        }
        catch (Exception exception) when (exception is
            JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new PowerToysConflictReport(
                settingsPath,
                [$"PowerToys Keyboard Manager settings could not be verified safely: {exception.Message}"],
                new Dictionary<ushort, string>());
        }
    }

    private static IEnumerable<JsonNode?> ReadMappings(JsonNode? group)
    {
        if (group is not JsonObject objectGroup)
        {
            yield break;
        }

        foreach ((_, JsonNode? node) in objectGroup)
        {
            if (node is not JsonArray mappings)
            {
                continue;
            }

            foreach (JsonNode? mapping in mappings)
            {
                yield return mapping;
            }
        }
    }
}

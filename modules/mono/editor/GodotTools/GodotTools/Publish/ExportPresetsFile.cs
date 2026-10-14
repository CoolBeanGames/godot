using System.Collections.Generic;

namespace GodotTools.Publish
{
    public sealed record ExportPreset(string Name, string Platform, string ExportPath);

    /// <summary>
    /// Minimal reader for export_presets.cfg: only [preset.N] sections with
    /// their name/platform/export_path keys. Everything else (options blocks,
    /// runnable map, encryption sections) is ignored. BCL only.
    /// </summary>
    public static class ExportPresetsFile
    {
        public static IReadOnlyList<ExportPreset> Parse(string cfgText)
        {
            var sections = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, string>? current = null;

            foreach (string rawLine in cfgText.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";"))
                    continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2);
                    current = new Dictionary<string, string>();
                    sections[name] = current;
                    continue;
                }
                if (current == null)
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line.Substring(0, eq).Trim();
                string value = Unquote(line.Substring(eq + 1).Trim());
                current[key] = value;
            }

            var presets = new List<ExportPreset>();
            for (int i = 0; i < sections.Count; i++)
            {
                if (!sections.TryGetValue("preset." + i, out var kv))
                    break;
                if (!kv.TryGetValue("name", out string? name) || string.IsNullOrWhiteSpace(name))
                    continue;
                kv.TryGetValue("platform", out string? platform);
                kv.TryGetValue("export_path", out string? exportPath);
                presets.Add(new ExportPreset(name, platform ?? "", exportPath ?? ""));
            }
            return presets;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
            {
                string inner = value.Substring(1, value.Length - 2);
                return inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }
            return value;
        }

        /// <summary>
        /// Default itch.io channel for an export platform name.
        /// </summary>
        public static string DefaultChannel(string platform) => platform switch
        {
            "Windows Desktop" => "windows",
            "Linux" => "linux",
            "macOS" => "mac",
            "Web" => "html",
            "Android" => "android",
            _ => "other",
        };
    }
}

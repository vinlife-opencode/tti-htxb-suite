using System.IO;

namespace Htxb.Host;

/// <summary>
/// Per-tool persisted settings, stored in <c>configuration\tools.ini</c> next to
/// the EXEs (one section per tool id). Currently keeps the last-used open/save
/// directory so dialogs reopen where the user last picked.
/// </summary>
public sealed class ToolSettings
{
    private readonly string _iniPath;
    private readonly string _section;

    public ToolSettings(string? toolId)
    {
        _iniPath = Path.Combine(AppContext.BaseDirectory, "configuration", "tools.ini");
        _section = string.IsNullOrWhiteSpace(toolId) ? "default" : toolId.Trim();
    }

    /// <summary>Returns the tool's LastDir value, or "" when unset/unavailable.</summary>
    public string GetLastDir()
    {
        try
        {
            if (!File.Exists(_iniPath)) return "";
            var current = "";
            foreach (var raw in File.ReadAllLines(_iniPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                    current = line[1..^1].Trim();
                else if (current.Equals(_section, StringComparison.OrdinalIgnoreCase)
                         && line.StartsWith("LastDir=", StringComparison.OrdinalIgnoreCase))
                    return line["LastDir=".Length..].Trim();
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Sets the tool's LastDir value, creating/updating the INI as needed. Best effort.</summary>
    public void SetLastDir(string dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_iniPath)!);

            var lines = File.Exists(_iniPath)
                ? new List<string>(File.ReadAllLines(_iniPath))
                : new List<string>
                {
                    "; TTI HTXB Tools & Utilities - per-tool default open/save directories",
                };

            int secStart = -1, secEnd = lines.Count; // section range [secStart+1, secEnd)
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith('[') && t.EndsWith(']'))
                {
                    if (secStart >= 0) { secEnd = i; break; }          // next section begins
                    if (t[1..^1].Trim().Equals(_section, StringComparison.OrdinalIgnoreCase))
                        secStart = i;
                }
            }

            if (secStart < 0)
            {
                // append a new section at the end
                if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
                lines.Add($"[{_section}]");
                lines.Add($"LastDir={dir}");
            }
            else
            {
                bool replaced = false;
                for (int i = secStart + 1; i < secEnd; i++)
                {
                    if (lines[i].TrimStart().StartsWith("LastDir=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"LastDir={dir}";
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) lines.Insert(Math.Min(secStart + 1, lines.Count), $"LastDir={dir}");
            }

            File.WriteAllLines(_iniPath, lines);
        }
        catch
        {
            // best effort — never block a dialog over settings persistence
        }
    }
}
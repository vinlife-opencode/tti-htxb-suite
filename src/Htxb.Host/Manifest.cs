using System.Text.Json;

namespace Htxb.Host;

public sealed class ToolManifest
{
    public string? Name { get; set; }
    public string? Exe { get; set; }
    public string? Version { get; set; }
}

public sealed class RuntimeManifest
{
    public int Version { get; set; } = 1;
    public string? Generated { get; set; }
    public Dictionary<string, ToolManifest> Tools { get; set; } = new();
}

public static class Manifest
{
    public static RuntimeManifest? Load(Data2Loader data2)
    {
        string? text = data2.ReadText("manifest.json");
        if (text is null) return null;
        try
        {
            return JsonSerializer.Deserialize<RuntimeManifest>(text, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string? VersionFor(Data2Loader data2, string toolId)
        => Load(data2)?.Tools?.TryGetValue(toolId, out var m) == true ? m.Version : null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
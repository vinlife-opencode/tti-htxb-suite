using System.Text.Json;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Adapters;

/// <summary>
/// Reads a tester <c>.zadz</c> recipe.
///
/// The file is UTF-8 (often with a BOM) JSON:
///
///     { "PartName": "...", "PartLibrary": "...", "Options": "...",
///       "AgingProcess": [ { "Test Name": "...", "Vds(V)": "930", ... }, ... ] }
///
/// Field names contain spaces and parentheses, and values are strings in every
/// sample received so far. The reader therefore takes each step as a name/value
/// dictionary rather than binding a fixed schema: an unknown or newly added field
/// is preserved, and only a missing <c>"Test Name"</c> is fatal.
/// </summary>
public static class ZadzAdapter
{
    public static ZadzRecipe Read(string path)
    {
        string text = File.ReadAllText(path);          // drops a UTF-8 BOM
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a JSON object.");

        if (!root.TryGetProperty("AgingProcess", out JsonElement steps)
            || steps.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{Path.GetFileName(path)} has no AgingProcess array.");

        var list = new List<ZadzStep>();
        foreach (JsonElement element in steps.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
                fields[property.Name] = AsText(property.Value);

            string name = fields.TryGetValue("Test Name", out string? value) ? value : string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;   // not a step

            list.Add(new ZadzStep
            {
                Name = name.Trim(),
                Vds = fields.TryGetValue("Vds(V)", out string? vds) ? vds : string.Empty,
                Vgs = fields.TryGetValue("Vgs(V)", out string? vgs) ? vgs : string.Empty,
                Fields = fields,
            });
        }

        if (list.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} contains no named steps.");

        return new ZadzRecipe
        {
            SourcePath = Path.GetFullPath(path),
            PartName = Text(root, "PartName"),
            PartLibrary = Text(root, "PartLibrary"),
            Options = Text(root, "Options"),
            Steps = list,
        };
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) ? AsText(value) : string.Empty;

    private static string AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),          // numbers / booleans stay as written
    };
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Htxb.RecipeCore.Tests;

/// <summary>
/// The shared vector file, as written by the Python implementation.
/// See docs/test-vectors/normalise.json in the BidfGen2 repository.
/// </summary>
internal sealed class VectorFile
{
    public List<NormaliseVector>? Normalise { get; set; }
    public List<SwBinVector>? SwBin { get; set; }

    public static VectorFile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new CheckFailed(
                $"vector file not found: {path}\n" +
                $"Pass --vectors <path> or generate it in the BidfGen2 checkout.");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var file = JsonSerializer.Deserialize<VectorFile>(File.ReadAllText(path), options)
            ?? throw new CheckFailed($"{path} is not a vector file.");

        if (file.Normalise is not { Count: > 0 })
            throw new CheckFailed($"{path} has no 'normalise' vectors.");

        return file;
    }
}

internal sealed class NormaliseVector
{
    public string? Input { get; set; }
    public string? Expected { get; set; }
}

internal sealed class SwBinVector
{
    public string? Input { get; set; }
    public int? Hex { get; set; }
    public string? Legend { get; set; }

    /// <summary>The cell std_ingest writes back out; the table reader must agree.</summary>
    public string? Written { get; set; }
}

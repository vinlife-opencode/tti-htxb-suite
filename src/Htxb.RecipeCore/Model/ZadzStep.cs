namespace Htxb.RecipeCore.Model;

/// <summary>
/// One <c>AgingProcess</c> entry from a tester <c>.zadz</c> recipe.
///
/// Only the identity and the electrical setup are modelled. The remaining fields
/// (soak times, oven temperature, ramp) are kept verbatim in <see cref="Fields"/>
/// so a report can quote the machine without a second parse - but they are not
/// the spec: the machine carries its own internal pass/fail limits, while the
/// workbook carries the limits BiDF bins against.
/// </summary>
public sealed record ZadzStep
{
    /// <summary><c>"Test Name"</c> - the name the tester writes into the CSV.</summary>
    public required string Name { get; init; }

    /// <summary><c>"Vds(V)"</c> as written, e.g. <c>930</c>.</summary>
    public string Vds { get; init; } = string.Empty;

    /// <summary><c>"Vgs(V)"</c> as written, e.g. <c>-10</c>.</summary>
    public string Vgs { get; init; } = string.Empty;

    /// <summary>Every field as received, keyed by its JSON name.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>();
}

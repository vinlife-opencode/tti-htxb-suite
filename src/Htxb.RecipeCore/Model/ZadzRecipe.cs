namespace Htxb.RecipeCore.Model;

/// <summary>
/// A tester machine recipe, as exported to a <c>.zadz</c> file.
///
/// This is the <em>execution</em> side: what the tester is configured to run, and
/// therefore the names the CSV will contain. The customer workbook remains the
/// contract (see <see cref="Recipe"/>); the two are compared, never merged.
/// </summary>
public sealed record ZadzRecipe
{
    /// <summary>File the recipe was read from.</summary>
    public required string SourcePath { get; init; }

    /// <summary><c>PartName</c> - the recipe the tester has loaded.</summary>
    public string PartName { get; init; } = string.Empty;

    /// <summary><c>PartLibrary</c>, e.g. <c>ST Production</c>.</summary>
    public string PartLibrary { get; init; } = string.Empty;

    /// <summary><c>Options</c>, e.g. <c>ShortBIHr</c>.</summary>
    public string Options { get; init; } = string.Empty;

    /// <summary>The steps, in machine order.</summary>
    public required IReadOnlyList<ZadzStep> Steps { get; init; }

    /// <summary>Step names only - what the CSV will log.</summary>
    public IReadOnlyList<string> Names => Steps.Select(s => s.Name).ToList();
}

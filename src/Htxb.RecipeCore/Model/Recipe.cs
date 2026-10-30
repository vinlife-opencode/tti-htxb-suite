namespace Htxb.RecipeCore.Model;

/// <summary>
/// A customer recipe workbook, read as the authoritative description of a BI recipe.
///
/// The workbook - not the <c>.zadz</c> - is the reference. A <c>.zadz</c> is a
/// derived artifact the operator may edit by hand afterwards, so nothing here is
/// ever validated against one.
/// </summary>
public sealed record Recipe
{
    public required string SourcePath { get; init; }

    /// <summary>File name without extension; the recipe name the package uses.</summary>
    public required string FileStem { get; init; }

    /// <summary>Name of the worksheet that holds the plan (one per recipe).</summary>
    public required string PlanSheetName { get; init; }

    /// <summary>"BI Recipe rev :" value from the plan sheet header, if stated.</summary>
    public string SheetRecipeName { get; init; } = string.Empty;

    /// <summary>"Package:" value from the plan sheet header, e.g. <c>A0TM</c>.</summary>
    public string PackageCode { get; init; } = string.Empty;

    /// <summary>Plan sheet title, e.g. <c>HIP247 4 Lead Long_BRJ</c>.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The steps, in sheet order, with ignored rows removed.</summary>
    public required IReadOnlyList<PlanStep> Steps { get; init; }

    /// <summary>Rows that look like steps but are not (data transfers, totals).</summary>
    public IReadOnlyList<IgnoredRow> IgnoredRows { get; init; } = [];

    /// <summary>The <c>Reference Table</c> tab, when the delivery has one.</summary>
    public Profile? Profile { get; init; }

    /// <summary>Contradictions between file name, sheet name and declared recipe name.</summary>
    public IReadOnlyList<string> IdentityIssues { get; init; } = [];

    /// <summary>Non-fatal problems worth putting in the report.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string FileName => System.IO.Path.GetFileName(SourcePath);

    public int StepCount => Steps.Count;

    /// <summary>Counts of each step name, for duplicate detection.</summary>
    public IReadOnlyList<IGrouping<string, PlanStep>> DuplicateNames =>
        Steps.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
             .Where(g => g.Count() > 1)
             .ToList();

    /// <summary>Names that collide once normalised - the engine cannot tell them apart.</summary>
    public IReadOnlyList<IGrouping<string, PlanStep>> AmbiguousNormalisedKeys =>
        Steps.GroupBy(s => s.NormalisedKey, StringComparer.Ordinal)
             .Where(g => g.Key.Length > 0 && g.Count() > 1)
             .ToList();

    public override string ToString() => $"{FileStem} ({StepCount} steps)";
}

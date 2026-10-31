namespace Htxb.RecipeCore.Diff;

/// <summary>One paired step: the workbook's name and the machine's name.</summary>
/// <param name="Workbook">Name in the customer workbook.</param>
/// <param name="Machine">Name in the .zadz / CSV.</param>
/// <param name="How"><c>exact</c> when the normalised names match, <c>alias</c> otherwise.</param>
public sealed record DiffPair(string Workbook, string Machine, string How);

/// <summary>
/// The result of comparing one recipe workbook with one machine recipe.
///
/// Nothing here changes a file: it is evidence for a decision about which side
/// has to move. <see cref="Aligned"/> is deliberately strict - a renamed step is
/// a difference, even when the two names clearly mean the same test.
/// </summary>
public sealed record DiffResult
{
    public required string Recipe { get; init; }
    public required int WorkbookSteps { get; init; }
    public required int MachineSteps { get; init; }
    public required IReadOnlyList<DiffPair> Pairs { get; init; }

    /// <summary>Workbook-only and machine-only step names.</summary>
    public required IReadOnlyList<string> WorkbookOnly { get; init; }
    public required IReadOnlyList<string> MachineOnly { get; init; }

    /// <summary>Vds/Vgs values that differ, one human-readable line each.</summary>
    public required IReadOnlyList<string> ValueDiffs { get; init; }

    /// <summary>True when the two sides agree on every step name and value.</summary>
    public bool Aligned =>
        Pairs.All(p => p.How == "exact")
        && WorkbookOnly.Count == 0
        && MachineOnly.Count == 0
        && ValueDiffs.Count == 0;

    public IEnumerable<DiffPair> Renamed => Pairs.Where(p => p.How == "alias");
}

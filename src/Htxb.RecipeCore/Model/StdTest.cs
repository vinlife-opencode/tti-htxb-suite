namespace Htxb.RecipeCore.Model;

/// <summary>
/// One row of a <c>std_table</c> sheet - the limits table the engine matches a
/// tester's logged measurements against.
///
/// 17 columns are shipped; the customer conversion page emits 16 and omits
/// <c>Test Sect ID Relative</c>. That column is optional here, and its absence is
/// reported rather than silently tolerated.
/// </summary>
public sealed record StdTest
{
    /// <summary>1-based worksheet row.</summary>
    public required int Row { get; init; }

    /// <summary>"Test name" - the name the tester logs, and the lookup key.</summary>
    public required string Name { get; init; }

    public string SectionId { get; init; } = string.Empty;
    public string SectionIdRelative { get; init; } = string.Empty;
    public string TestIndex { get; init; } = string.Empty;
    public string Marker { get; init; } = string.Empty;

    /// <summary>"time (sec)".</summary>
    public double? TimeSeconds { get; init; }

    /// <summary>"Program Duration (min)".</summary>
    public double? ProgramMinutes { get; init; }

    public string Vds { get; init; } = string.Empty;
    public string Vgs { get; init; } = string.Empty;
    public string Min { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Max { get; init; } = string.Empty;
    public string Units { get; init; } = string.Empty;
    public string Sequence { get; init; } = string.Empty;

    /// <summary>"HW BIN"</summary>
    public string HwBin { get; init; } = string.Empty;

    /// <summary>"SW BIN Priority" verbatim.</summary>
    public string SwBinPriority { get; init; } = string.Empty;

    public string ExcludeWhenFail { get; init; } = string.Empty;
    public string Remarks { get; init; } = string.Empty;

    /// <summary>Every header/value pair verbatim, for reporting.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public string SwBinHex => TestName.SplitSwBin(SwBinPriority).Hex;
    public string SwBinLegend => TestName.SplitSwBin(SwBinPriority).LegendName;
    public int? SwBinCode => TestName.SwBinCode(SwBinPriority);
    public string NormalisedKey => TestName.Normalise(Name);

    public override string ToString() => $"{Row}. {Name}";
}

/// <summary>A parsed <c>std_table</c> sheet.</summary>
public sealed record StdTable
{
    public required string Path { get; init; }
    public required string SheetName { get; init; }
    public required IReadOnlyList<StdTest> Tests { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>True when the 17th column (<c>Test Sect ID Relative</c>) is present.</summary>
    public bool HasRelativeSectionColumn =>
        Columns.Any(c => Profile.Squash(c) == "testsectidrelative");

    /// <summary>A short label for reports: <c>Std_07_ST_1200V.xlsx</c>.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Names that appear more than once, normalised - the ambiguity source.</summary>
    public IReadOnlyList<IGrouping<string, StdTest>> DuplicateNormalisedKeys =>
        Tests.GroupBy(t => t.NormalisedKey, StringComparer.Ordinal)
             .Where(g => g.Key.Length > 0 && g.Count() > 1)
             .ToList();
}

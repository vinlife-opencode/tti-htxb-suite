namespace Htxb.RecipeCore.Model;

/// <summary>
/// One BI test step as the customer's plan sheet states it.
///
/// Column fields are named after the workbook header, not the std-table header,
/// because the workbook is the reference and the std table is the thing being
/// judged. <see cref="Fields"/> keeps every mapped cell verbatim so a report can
/// quote the source without a second parse.
/// </summary>
public sealed record PlanStep
{
    /// <summary>1-based worksheet row this step was read from.</summary>
    public required int Row { get; init; }

    /// <summary>Position in the recipe, 1-based, after ignored rows are removed.</summary>
    public required int Ordinal { get; init; }

    /// <summary>"Test name" (E) - the step identity the tester logs.</summary>
    public required string Name { get; init; }

    /// <summary>"Test Sect ID" (A), carried down over merged steps.</summary>
    public string SectionId { get; init; } = string.Empty;

    /// <summary>"Test Index" (B) - the workbook's own running number.</summary>
    public string TestIndex { get; init; } = string.Empty;

    /// <summary>"Marker" (C).</summary>
    public string Marker { get; init; } = string.Empty;

    /// <summary>"Test step" (D) - the group label, e.g. <c>PRETEST</c>, <c>HTGB1</c>.</summary>
    public string StepGroup { get; init; } = string.Empty;

    /// <summary>"time (sec)" (F).</summary>
    public double? TimeSeconds { get; init; }

    /// <summary>"Program Duration (min)" (G); may be blank in the workbook.</summary>
    public double? ProgramMinutes { get; init; }

    /// <summary>"temp. (ºC)" (H).</summary>
    public string Temp { get; init; } = string.Empty;

    /// <summary>"V_ds" (I) as written, e.g. <c>1200V</c>.</summary>
    public string Vds { get; init; } = string.Empty;

    /// <summary>"V_gs" (J) as written, e.g. <c>+32V</c>.</summary>
    public string Vgs { get; init; } = string.Empty;

    public string Min { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Max { get; init; } = string.Empty;
    public string Units { get; init; } = string.Empty;

    /// <summary>"HW BIN" (O).</summary>
    public string HwBin { get; init; } = string.Empty;

    /// <summary>"SW BIN Priority" (P) verbatim, e.g. <c>196'=F_PRE_ON</c>.</summary>
    public string SwBinPriority { get; init; } = string.Empty;

    /// <summary>"Exclude When Fail" (Q) as written (Yes/No/yes).</summary>
    public string ExcludeWhenFail { get; init; } = string.Empty;

    /// <summary>"Remarks" (R).</summary>
    public string Remarks { get; init; } = string.Empty;

    /// <summary>Every mapped header/value pair, verbatim, for reporting.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The hex half of <see cref="SwBinPriority"/>, e.g. <c>196</c>.</summary>
    public string SwBinHex => TestName.SplitSwBin(SwBinPriority).Hex;

    /// <summary>The legend half of <see cref="SwBinPriority"/>, e.g. <c>F_PRE_ON</c>.</summary>
    public string SwBinLegend => TestName.SplitSwBin(SwBinPriority).LegendName;

    /// <summary>The numeric bin code, hex as the customer writes it; null when non-numeric.</summary>
    public int? SwBinCode => TestName.SwBinCode(SwBinPriority);

    /// <summary>
    /// Programmed minutes for the step: the sheet's own value when present, else
    /// derived from <c>time (sec)</c> the way the std table stores it.
    /// </summary>
    public double? EffectiveProgramMinutes =>
        ProgramMinutes ?? (TimeSeconds is { } seconds ? seconds / 60.0 : null);

    public string NormalisedKey => TestName.Normalise(Name);

    public override string ToString() => $"{Ordinal,2}. {Name}";
}

/// <summary>A worksheet row that looks like a step but is not one, kept for audit.</summary>
public sealed record IgnoredRow(int Row, string Name, string Reason);

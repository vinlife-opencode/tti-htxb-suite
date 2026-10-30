using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Fit;

public enum FitVerdict
{
    /// <summary>Every step matched by exact name and every compared field agreed.</summary>
    Fits,

    /// <summary>Every field agreed, but some step names are spelled differently.</summary>
    NamesDiffer,

    /// <summary>At least one compared field, or the step set itself, disagreed.</summary>
    Mismatch,

    /// <summary>The recipe has no row in CustomerProfileSelection.csv.</summary>
    NoProfile,

    /// <summary>The profile row names a std table that is not in the folder.</summary>
    NoTable,

    /// <summary>The workbook could not be read.</summary>
    Error,
}

/// <summary>
/// What the tables folder has to offer a recipe that no profile row lists.
///
/// <see cref="Table"/> is set only when one table covers <b>every</b> step of the
/// program. The engine looks limits up by test name, so a step the table does not
/// list runs with no limit and no bin; and every table shares the generic pre-test
/// and ramp names, so "most names in common" is not "same product". Measured on
/// the 261028 delivery the closest shipped table covers 64-77% of any program, and
/// all six SQ programs scored a 650V SiC table at 9 of 14 names.
///
/// <see cref="Closest"/> is always the best overlap, so the gap can be reported
/// even when nothing is offered.
/// </summary>
public sealed record TableSuggestion(string? Table, int Hits, string? Closest, int ClosestHits, int Steps)
{
    public static readonly TableSuggestion None = new(null, 0, null, 0, 0);
}

public sealed record FieldDiff(string Field, string Recipe, string Table);

public sealed record StepComparison
{
    public required int Ordinal { get; init; }
    public required string Name { get; init; }
    public string? TableName { get; init; }
    public string? TableRowName { get; init; }
    public int? TableRow { get; init; }
    public bool NameExact { get; init; }
    public bool NameNormalised { get; init; }
    public IReadOnlyList<FieldDiff> Differences { get; init; } = [];

    /// <summary>A recipe step with no row in the table.</summary>
    public bool IsMissing => TableName is null && TableRowName is null;

    /// <summary>A table row the recipe does not have (a stale row or a drift pair).</summary>
    public bool IsTableOnly => TableName is null && TableRowName is not null;
}

public sealed record FitResult
{
    public required string Recipe { get; init; }
    public required string Family { get; init; }
    public required int StepCount { get; init; }

    /// <summary>The profile row found, or null.</summary>
    public ProfileRow? Profile { get; init; }

    /// <summary>The std table the profile selects (file name), or null.</summary>
    public string? TableFile { get; init; }

    public int TableRows { get; init; }

    /// <summary>When there is no profile row: the table that covers every step, or null.</summary>
    public string? SuggestedTable { get; init; }
    public int SuggestedHits { get; init; }

    public required FitVerdict Verdict { get; init; }

    public int MatchedExact { get; init; }
    public int MatchedNormalised { get; init; }
    public int Missing { get; init; }
    public int TableOnly { get; init; }

    public IReadOnlyList<StepComparison> Steps { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<string> IdentityIssues { get; init; } = [];

    public int FieldDifferenceCount => Steps.Sum(s => s.Differences.Count);

    /// <summary>True when the watcher would produce no BiDF for this recipe today.</summary>
    public bool IsReportOnly => Profile is null;

    public string VerdictLabel => Verdict switch
    {
        FitVerdict.Fits => "FITS",
        FitVerdict.NamesDiffer => "NAMES",
        FitVerdict.Mismatch => "MISMATCH",
        FitVerdict.NoProfile => "NO_PROFILE",
        FitVerdict.NoTable => "NO_TABLE",
        _ => "ERROR",
    };
}

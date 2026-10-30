using System.Globalization;
using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Fit;

/// <summary>
/// Judges one recipe workbook against one <c>std_table</c>.
///
/// The comparison is deliberately narrow and literal: it compares what the engine
/// will actually look up. Step names are aligned by LONGEST COMMON SUBSEQUENCE on
/// the normalised key, not by position, so one inserted or removed step shows up as
/// an insert/remove instead of shifting every later comparison by one and reporting
/// thirty false differences.
///
/// The verdict is a prediction. Only a real lot through the watcher proves a table;
/// <c>flowguard</c> is the observer, this is the forecast.
/// </summary>
public static class FitChecker
{
    /// <summary>Voltage and time tolerance: the sheets store one decimal at most.</summary>
    private const double Tolerance = 0.005;

    public static FitResult Check(Recipe recipe, StdTable table, ProfileRow? profile)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(table);

        var alignment = Align(recipe.Steps.Select(s => s.NormalisedKey).ToList(),
                              table.Tests.Select(t => t.NormalisedKey).ToList());

        var comparisons = new List<StepComparison>();
        var matched = new HashSet<int>();

        foreach (var (recipeIndex, tableIndex) in alignment)
        {
            if (recipeIndex is null)
            {
                comparisons.Add(new StepComparison
                {
                    Ordinal = 0,
                    Name = string.Empty,
                    TableRow = table.Tests[tableIndex!.Value].Row,
                    TableRowName = table.Tests[tableIndex.Value].Name,
                });
                continue;
            }

            var step = recipe.Steps[recipeIndex.Value];
            if (tableIndex is null)
            {
                comparisons.Add(new StepComparison
                {
                    Ordinal = step.Ordinal,
                    Name = step.Name,
                    TableName = null,
                });
                continue;
            }

            var std = table.Tests[tableIndex.Value];
            matched.Add(tableIndex.Value);

            comparisons.Add(new StepComparison
            {
                Ordinal = step.Ordinal,
                Name = step.Name,
                TableName = std.Name,
                TableRow = std.Row,
                TableRowName = std.Name,
                NameExact = string.Equals(step.Name, std.Name, StringComparison.Ordinal),
                NameNormalised = string.Equals(step.NormalisedKey, std.NormalisedKey, StringComparison.Ordinal),
                Differences = Compare(step, std),
            });
        }

        int tableOnly = table.Tests.Count - matched.Count;
        int exact = comparisons.Count(c => c.NameExact);
        int normalised = comparisons.Count(c => c.TableName is not null && c.NameNormalised);
        int missing = comparisons.Count(c => c.IsMissing);

        var notes = new List<string>();
        if (!table.HasRelativeSectionColumn)
            notes.Add("table has no 'Test Sect ID Relative' column (16 of 17)");
        if (table.DuplicateNormalisedKeys.Count > 0)
            notes.Add($"{table.DuplicateNormalisedKeys.Count} normalised name collision(s) in the table");
        if (recipe.AmbiguousNormalisedKeys.Count > 0)
            notes.Add($"{recipe.AmbiguousNormalisedKeys.Count} normalised name collision(s) in the recipe");

        bool anyFieldDifference = comparisons.Any(c => c.Differences.Count > 0);
        bool anyNameDifference = comparisons.Any(c => c.TableName is not null && !c.NameExact);

        FitVerdict verdict = missing > 0 || anyFieldDifference
            ? FitVerdict.Mismatch
            : anyNameDifference || tableOnly > 0
                ? FitVerdict.NamesDiffer
                : FitVerdict.Fits;

        return new FitResult
        {
            Recipe = recipe.FileStem,
            Family = FamilyOf(recipe.SourcePath),
            StepCount = recipe.StepCount,
            Profile = profile,
            TableFile = table.FileName,
            TableRows = table.Tests.Count,
            Verdict = verdict,
            MatchedExact = exact,
            MatchedNormalised = normalised,
            Missing = missing,
            TableOnly = tableOnly,
            Steps = comparisons,
            Notes = notes,
            IdentityIssues = recipe.IdentityIssues,
        };
    }

    /// <summary>
    /// The table to offer a recipe that no profile row lists. Mirrors
    /// <c>bidfgen.engine.choose_std_table</c>: a candidate must cover every step of
    /// the program, otherwise nothing is offered and the watcher uses its
    /// configured fallback table.
    ///
    /// Ties between fully-covering tables go to the most exact name matches, then
    /// to file name, so the answer is stable rather than dependent on disk order.
    /// </summary>
    public static TableSuggestion SuggestTable(Recipe recipe, IEnumerable<StdTable> tables)
    {
        var wanted = new HashSet<string>(
            recipe.Steps.Select(s => s.NormalisedKey).Where(k => k.Length > 0), StringComparer.Ordinal);

        if (wanted.Count == 0) return TableSuggestion.None;

        var exactNames = new HashSet<string>(
            recipe.Steps.Select(s => s.Name.Trim()), StringComparer.OrdinalIgnoreCase);

        string? closest = null;
        int closestHits = 0;
        string? cover = null;
        int coverExact = -1;

        foreach (StdTable table in tables.OrderBy(t => t.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var keys = table.Tests.Select(t => t.NormalisedKey).Distinct().ToList();
            int hits = keys.Count(wanted.Contains);

            if (hits > closestHits)
            {
                closest = table.FileName;
                closestHits = hits;
            }

            if (hits != wanted.Count) continue;

            int exact = table.Tests.Select(t => t.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                                         .Count(exactNames.Contains);
            if (exact > coverExact)
            {
                cover = table.FileName;
                coverExact = exact;
            }
        }

        return new TableSuggestion(cover, cover is null ? 0 : wanted.Count, closest, closestHits, wanted.Count);
    }

    /// <summary>The outcome when the recipe has no profile row.</summary>
    public static FitResult NoProfile(Recipe recipe, TableSuggestion suggestion)
    {
        var notes = new List<string>
        {
            "not listed in CustomerProfileSelection.csv",
            "the watcher treats an unlisted recipe as report-only: no drift, no BiDF, no summaries",
        };

        if (suggestion.Table is not null)
        {
            notes.Add($"one table covers every step: {suggestion.Table} ({suggestion.Hits} step(s))");
        }
        else if (suggestion.Closest is not null)
        {
            notes.Add(
                $"no table in the folder covers all {suggestion.Steps} step(s) - the closest is " +
                $"{suggestion.Closest} at {suggestion.ClosestHits} of {suggestion.Steps}, so the " +
                "watcher uses its configured fallback table, not that one");
        }

        return new FitResult
        {
            Recipe = recipe.FileStem,
            Family = FamilyOf(recipe.SourcePath),
            StepCount = recipe.StepCount,
            Verdict = FitVerdict.NoProfile,
            SuggestedTable = suggestion.Table,
            SuggestedHits = suggestion.Hits,
            Notes = notes,
            IdentityIssues = recipe.IdentityIssues,
        };
    }

    public static FitResult Failure(Recipe recipe, string message)
    {
        return new FitResult
        {
            Recipe = recipe.FileStem,
            Family = FamilyOf(recipe.SourcePath),
            StepCount = recipe.StepCount,
            Verdict = FitVerdict.Error,
            Notes = [message],
            IdentityIssues = recipe.IdentityIssues,
        };
    }

    // ------------------------------------------------------------------ compare

    private static List<FieldDiff> Compare(PlanStep step, StdTest std)
    {
        var diffs = new List<FieldDiff>();

        CompareNumber(diffs, "time (sec)", step.TimeSeconds, std.TimeSeconds);
        CompareVoltage(diffs, "V_ds", step.Vds, std.Vds);
        CompareVoltage(diffs, "V_gs", step.Vgs, std.Vgs);
        CompareText(diffs, "Units", step.Units, std.Units);
        CompareText(diffs, "min", step.Min, std.Min);
        CompareText(diffs, "max", step.Max, std.Max);
        CompareText(diffs, "HW BIN", NormaliseInteger(step.HwBin), NormaliseInteger(std.HwBin));
        CompareText(diffs, "Exclude When Fail", NormaliseYesNo(step.ExcludeWhenFail), NormaliseYesNo(std.ExcludeWhenFail));

        CompareText(diffs, "SW BIN legend", step.SwBinLegend, std.SwBinLegend);
        CompareSwBinCode(diffs, step, std);

        return diffs;
    }

    /// <summary>
    /// The bin code is a HEX number on both sides ("196" = 0x196), and the workbook
    /// adds an apostrophe or two that the table does not. Compare the number, not
    /// the spelling; fall back to text only when a side is not a number at all.
    /// </summary>
    private static void CompareSwBinCode(List<FieldDiff> diffs, PlanStep step, StdTest std)
    {
        int? recipe = step.SwBinCode;
        int? table = std.SwBinCode;

        if (recipe is not null && table is not null)
        {
            if (recipe.Value == table.Value) return;
            diffs.Add(new FieldDiff("SW BIN code",
                $"0x{recipe.Value:X}", $"0x{table.Value:X}"));
            return;
        }

        CompareText(diffs, "SW BIN code",
            step.SwBinHex.ToUpperInvariant(), std.SwBinHex.ToUpperInvariant());
    }

    private static void CompareText(List<FieldDiff> diffs, string field, string recipeValue, string tableValue)
    {
        if (string.Equals(recipeValue.Trim(), tableValue.Trim(), StringComparison.Ordinal)) return;
        if (recipeValue.Trim().Length == 0 && tableValue.Trim().Length == 0) return;
        diffs.Add(new FieldDiff(field, recipeValue.Trim(), tableValue.Trim()));
    }

    private static void CompareNumber(List<FieldDiff> diffs, string field, double? recipeValue, double? tableValue)
    {
        if (recipeValue is null && tableValue is null) return;
        if (recipeValue is null || tableValue is null)
        {
            diffs.Add(new FieldDiff(field, Format(recipeValue), Format(tableValue)));
            return;
        }
        if (Math.Abs(recipeValue.Value - tableValue.Value) <= Tolerance) return;
        diffs.Add(new FieldDiff(field, Format(recipeValue), Format(tableValue)));
    }

    private static void CompareVoltage(List<FieldDiff> diffs, string field, string recipeText, string tableText)
    {
        double? recipeValue = TestName.VoltageValue(recipeText);
        double? tableValue = TestName.VoltageValue(tableText);

        if (recipeValue is not null && tableValue is not null)
        {
            if (Math.Abs(recipeValue.Value - tableValue.Value) <= Tolerance) return;
            diffs.Add(new FieldDiff(field, recipeText.Trim(), tableText.Trim()));
            return;
        }

        CompareText(diffs, field, recipeText, tableText);
    }

    /// <summary>"3" and "3.0" are the same bin; the workbook and the table disagree on the type.</summary>
    private static string NormaliseInteger(string text)
    {
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
            Math.Abs(value - Math.Round(value)) < 1e-9)
        {
            return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
        }
        return text.Trim();
    }

    private static string NormaliseYesNo(string text)
    {
        string trimmed = text.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "yes" or "y" or "true" or "1" => "Yes",
            "no" or "n" or "false" or "0" or "" => "No",
            _ => text.Trim(),
        };
    }

    private static string Format(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

    // ------------------------------------------------------------------ align

    /// <summary>
    /// LCS alignment on normalised keys. Returns (recipeIndex, tableIndex) pairs
    /// where a null on one side marks an insertion or a removal.
    /// </summary>
    public static List<(int? Recipe, int? Table)> Align(IReadOnlyList<string> recipeKeys, IReadOnlyList<string> tableKeys)
    {
        int n = recipeKeys.Count, m = tableKeys.Count;
        var lengths = new int[n + 1, m + 1];

        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                lengths[i, j] = recipeKeys[i] == tableKeys[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var pairs = new List<(int?, int?)>(Math.Max(n, m));
        int a = 0, b = 0;

        while (a < n && b < m)
        {
            if (recipeKeys[a] == tableKeys[b])
            {
                pairs.Add((a++, b++));
            }
            else if (lengths[a + 1, b] >= lengths[a, b + 1])
            {
                pairs.Add((a++, null));
            }
            else
            {
                pairs.Add((null, b++));
            }
        }

        while (a < n) pairs.Add((a++, null));
        while (b < m) pairs.Add((null, b++));

        return pairs;
    }

    private static string FamilyOf(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        return directory is null ? string.Empty : System.IO.Path.GetFileName(directory);
    }
}

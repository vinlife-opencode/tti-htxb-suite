using System.Globalization;
using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Diff;

/// <summary>
/// Compares a customer recipe workbook with a machine <c>.zadz</c> recipe.
///
/// Only names and the electrical setup are compared. Limits are not: the machine
/// carries the tester's internal pass/fail limits (typically a -0.5 µA floor) and
/// the workbook carries the spec limits BiDF bins against, so they are different
/// quantities that happen to share a column name.
/// </summary>
public static class RecipeDiffer
{
    public static DiffResult Compare(
        string workbookPath,
        string zadzPath,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        Recipe recipe = PlanSheetAdapter.Read(workbookPath);
        ZadzRecipe machine = ZadzAdapter.Read(zadzPath);

        var workbookKeys = recipe.Steps.Select(s => TestName.Normalise(s.Name)).ToList();
        var machineKeys = machine.Steps.Select(s => TestName.Normalise(s.Name)).ToList();
        var alignment = RecipeAligner.Align(workbookKeys, machineKeys, aliases);

        var pairs = alignment.Pairs
            .Select(p => new DiffPair(
                recipe.Steps[p.Workbook].Name,
                machine.Steps[p.Machine].Name,
                p.How))
            .ToList();

        var valueDiffs = new List<string>();
        foreach (var (workbookIndex, machineIndex, _) in alignment.Pairs)
        {
            PlanStep step = recipe.Steps[workbookIndex];
            ZadzStep other = machine.Steps[machineIndex];
            CompareValue(step.Name, "Vds", step.Vds, other.Vds, valueDiffs);
            CompareValue(step.Name, "Vgs", step.Vgs, other.Vgs, valueDiffs);
        }

        return new DiffResult
        {
            Recipe = recipe.FileStem,
            WorkbookSteps = recipe.Steps.Count,
            MachineSteps = machine.Steps.Count,
            Pairs = pairs,
            WorkbookOnly = alignment.WorkbookOnly.Select(i => recipe.Steps[i].Name).ToList(),
            MachineOnly = alignment.MachineOnly.Select(i => machine.Steps[i].Name).ToList(),
            ValueDiffs = valueDiffs,
        };
    }

    private static void CompareValue(
        string step, string field, string workbookValue, string machineValue, List<string> diffs)
    {
        double? want = Number(workbookValue);
        double? have = Number(machineValue);
        if (want is null || have is null) return;
        if (Math.Abs(want.Value - have.Value) < 1e-9) return;
        diffs.Add($"{step}: {field} workbook {workbookValue} vs machine {machineValue}");
    }

    /// <summary>
    /// The digits, decimal point and minus sign of a value - so <c>930V</c>,
    /// <c>930</c> and <c>930.0</c> compare equal, while <c>-10V</c> keeps its sign.
    /// A leading <c>+</c> is dropped, as the Python implementation does.
    /// </summary>
    private static double? Number(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string digits = new(text.Where(ch => char.IsAsciiDigit(ch) || ch is '.' or '-').ToArray());
        if (digits.Length == 0) return null;
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }
}

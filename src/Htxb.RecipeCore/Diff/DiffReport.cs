using System.Text.Json;

namespace Htxb.RecipeCore.Diff;

/// <summary>
/// Renders a <see cref="DiffResult"/> for a human and for a machine.
///
/// The text form is the one an operator reads; the JSON form is the parity
/// contract with the Python tool of the same name, so both can be compared
/// structurally on the same delivery.
/// </summary>
public static class DiffReport
{
    public static string Text(DiffResult result)
    {
        int exact = result.Pairs.Count(p => p.How == "exact");
        int alias = result.Pairs.Count - exact;

        var lines = new List<string>
        {
            result.Recipe,
            $"  workbook steps : {result.WorkbookSteps}",
            $"  machine steps  : {result.MachineSteps}",
            $"  paired         : {result.Pairs.Count} ({exact} exact, {alias} by alias)",
        };

        if (alias > 0)
            lines.Add("  renamed        : " + string.Join(", ",
                result.Renamed.Select(p => $"{p.Workbook} -> {p.Machine}")));
        if (result.WorkbookOnly.Count > 0)
            lines.Add("  workbook only  : " + string.Join(", ", result.WorkbookOnly));
        if (result.MachineOnly.Count > 0)
            lines.Add("  machine only   : " + string.Join(", ", result.MachineOnly));
        foreach (string diff in result.ValueDiffs)
            lines.Add("  value differs  : " + diff);

        lines.Add("  RESULT         : " + (result.Aligned ? "aligned" : "differences above"));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Json(DiffResult result)
    {
        var payload = new
        {
            recipe = result.Recipe,
            workbookSteps = result.WorkbookSteps,
            machineSteps = result.MachineSteps,
            pairs = result.Pairs.Select(p => new { workbook = p.Workbook, machine = p.Machine, how = p.How }),
            renamed = result.Renamed.Select(p => new[] { p.Workbook, p.Machine }),
            workbookOnly = result.WorkbookOnly,
            machineOnly = result.MachineOnly,
            valueDiffs = result.ValueDiffs,
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}

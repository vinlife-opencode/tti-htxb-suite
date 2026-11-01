using Htxb.RecipeCore.Diff;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Ingest;

/// <summary>The rows a build produced, plus what it had to decide on the way.</summary>
public sealed record StdTableBuild(IReadOnlyList<object?[]> Rows, IReadOnlyList<string> Notes)
{
    public int Count => Rows.Count;

    /// <summary>How many rows were renamed to the name the tester logs.</summary>
    public int Renamed { get; init; }

    /// <summary>Rows whose <c>SW BIN Priority</c> carries no numeric code.</summary>
    public int WithoutSwBin => Rows.Count(r => r.Length > 13 &&
        TestName.SplitSwBin(System.Convert.ToString(r[13], System.Globalization.CultureInfo.InvariantCulture)).Hex.Length == 0);
}

/// <summary>
/// Builds a standard limits table from a customer workbook, optionally aligned to a
/// machine recipe.
///
/// Two rules decide everything:
///
/// * the **workbook** supplies limits, HW BIN, SW BIN and order - it is the
///   specification;
/// * a table used on a machine must carry the **tester's** step names, because the
///   engine looks limits up by normalised name and the tester logs what the
///   <c>.zadz</c> says (<c>HTGB1</c>, not <c>HTGB</c>).
///
/// Limits are stored in the unit the engine expects: gate-leakage steps
/// (IGSS/HTGB/ISGS) in nA, everything else in uA. Text such as <c>1,5mA</c> or
/// <c>50nA</c> is converted; nothing is copied from an older table.
///
/// This is the merged behaviour of <c>bidfgen.tools.std_ingest</c> and
/// <c>bidfgen.tools.std_align</c>. The two Python tools differed in one place -
/// std-ingest copied the workbook's min/max verbatim and std-align normalised them.
/// A single normaliser is used here because the engine reads <c>lsl_num</c> as nA
/// whatever the <c>Units</c> cell says, so a verbatim copy would mis-scale a limit
/// written in mA or A. For the delivered workbooks (all limits already stated in
/// uA or nA) the two agree cell for cell, which is asserted by the parity test.
/// </summary>
public static class StdTableBuilder
{
    /// <summary>The sixteen columns the shipped tables carry, in order.</summary>
    public static readonly string[] Columns =
    [
        "Test Sect ID", "Test Index", "Marker", "Test name", "time (sec)",
        "Program Duration (min)", "V_ds", "V_gs", "min", "max", "Units",
        "Sequence", "HW BIN", "SW BIN Priority", "Exclude When Fail", "Remarks",
    ];

    /// <summary>
    /// The seventeenth column <c>bidfgen.tools.std_ingest</c> appends. The shipped
    /// per-recipe tables stop at <c>Remarks</c>, so it is only written when a
    /// workbook is ingested on its own and something downstream may still read it.
    /// </summary>
    public const string RelativeColumn = "Test Sect ID Relative";

    public static readonly string[] IngestColumns = [.. Columns, RelativeColumn];

    /// <summary>Machine steps that the tester logs but no shipped table limits.</summary>
    private static readonly HashSet<string> NonTestSteps = new(StringComparer.Ordinal) { "rampdown" };

    private static readonly string[] GateKeywords = ["IGSS", "HTGB", "ISGS"];

    private sealed record MachineOnly(string Vds, string Vgs, double Low, double High,
        string Unit, int HwBin, string Sw);

    /// <summary>
    /// Steps the machine runs that these workbooks do not list. A step outside this
    /// map aborts the run - nothing is invented and nothing is copied from an older
    /// table.
    /// </summary>
    private static readonly Dictionary<string, MachineOnly> MachineOnlyRows = new(StringComparer.Ordinal)
    {
        ["IGSS_-10V_1"] = new("0V", "-10V", -80, 80, "nA", 2, "15F = F_ISGS_10V_1"),
        ["IGSS_-10V_3"] = new("0V", "-10V", -80, 80, "nA", 2, "173 = F_ISGS_10V_3"),
    };

    private static readonly Dictionary<string, double> UnitToMicroamps = new(StringComparer.Ordinal)
    {
        ["a"] = 1e6, ["ma"] = 1e3, ["ua"] = 1.0, ["µa"] = 1.0, ["na"] = 1e-3,
    };

    /// <summary>The workbook's own steps, in sheet order (as delivered).</summary>
    public static StdTableBuild BuildFromWorkbook(Recipe recipe)
    {
        var rows = new List<object?[]>();
        int index = 0;
        foreach (PlanStep step in recipe.Steps)
        {
            index++;
            rows.Add([.. Row(step, index, step.Name), 0]);
        }

        if (rows.Count == 0)
            throw new InvalidDataException($"no steps in {Path.GetFileName(recipe.SourcePath)}");

        return new StdTableBuild(rows, []);
    }

    /// <summary>The recipe as the machine runs it; notes list the machine-only rows.</summary>
    public static StdTableBuild BuildAligned(Recipe recipe, ZadzRecipe machine,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        aliases ??= RecipeAligner.DefaultAliases;

        var workbookKeys = recipe.Steps.Select(s => s.NormalisedKey).ToList();
        var machineKeys = machine.Steps.Select(s => TestName.Normalise(s.Name)).ToList();
        var alignment = RecipeAligner.Align(workbookKeys, machineKeys, aliases);
        var byMachine = alignment.Pairs.ToDictionary(p => p.Machine, p => p.Workbook);

        var rows = new List<object?[]>();
        var notes = new List<string>();
        var missing = new List<string>();

        for (int i = 0; i < machine.Steps.Count; i++)
        {
            ZadzStep step = machine.Steps[i];
            if (byMachine.TryGetValue(i, out int workbookIndex))
            {
                rows.Add(Row(recipe.Steps[workbookIndex], i + 1, step.Name));
                continue;
            }

            if (NonTestSteps.Contains(TestName.Normalise(step.Name)))
            {
                notes.Add($"{step.Name}: machine ramp step, not a table row");
                continue;
            }

            if (!MachineOnlyRows.TryGetValue(step.Name, out MachineOnly? only))
            {
                missing.Add(step.Name);
                continue;
            }

            rows.Add(MachineOnlyRow(recipe, step.Name, only, i + 1));
            notes.Add($"{step.Name}: not in the workbook, defined in the tool");
        }

        if (missing.Count > 0)
            throw new InvalidDataException("no source for machine step(s): " + string.Join(", ", missing));

        foreach (int i in alignment.WorkbookOnly)
            notes.Add($"{recipe.Steps[i].Name}: workbook step the machine does not run");

        var expected = machine.Steps
            .Where(s => !NonTestSteps.Contains(TestName.Normalise(s.Name)))
            .Select(s => TestName.Normalise(s.Name))
            .ToList();
        AssertMachineOrder(rows, expected);

        for (int i = 0; i < rows.Count; i++) rows[i][1] = i + 1;
        return new StdTableBuild(rows, notes);
    }

    /// <summary>
    /// Fallback for a recipe with no <c>.zadz</c>: the workbook's own steps, with
    /// the names mapped through the alias table.
    ///
    /// No step can be added this way - without the machine recipe there is no
    /// evidence that the machine runs anything the workbook omits, so the table
    /// stays provisional until the export arrives.
    /// </summary>
    public static StdTableBuild BuildWithoutMachine(Recipe recipe,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        aliases ??= RecipeAligner.DefaultAliases;

        var rows = new List<object?[]>();
        var notes = new List<string>();
        int index = 0;

        foreach (PlanStep step in recipe.Steps)
        {
            index++;
            string name = step.Name;
            if (aliases.TryGetValue(step.NormalisedKey, out string? machine))
            {
                notes.Add($"{step.Name} -> {machine} (alias; no .zadz to confirm)");
                name = machine;
            }
            rows.Add(Row(step, index, name));
        }

        if (rows.Count == 0)
            throw new InvalidDataException($"no steps in {Path.GetFileName(recipe.SourcePath)}");

        return new StdTableBuild(rows, notes);
    }

    /// <summary>
    /// Renames every row to the name the tester logs, verifying the pairing row by
    /// row. The pairing is positional, so each pair must agree once the re-issue
    /// decorations are removed (<see cref="TestName.Base"/>); anything else is an
    /// error, because silently pairing the wrong row would apply the wrong limits.
    /// </summary>
    public static StdTableBuild ApplyLoggedNames(StdTableBuild build, IReadOnlyList<string> logged)
    {
        if (logged.Count != build.Rows.Count)
        {
            throw new InvalidDataException(
                $"the log has {logged.Count} steps, the workbook {build.Rows.Count} - " +
                "the two are not the same plan; pair them by hand");
        }

        var rows = new List<object?[]>(build.Rows.Count);
        var notes = new List<string>(build.Notes);
        int renamed = 0;

        for (int i = 0; i < build.Rows.Count; i++)
        {
            object?[] row = (object?[])build.Rows[i].Clone();
            string workbookName = System.Convert.ToString(row[3], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            string loggedName = logged[i];

            if (TestName.Base(workbookName) != TestName.Base(loggedName))
            {
                throw new InvalidDataException(
                    $"step {i + 1}: workbook says '{workbookName}', the log says " +
                    $"'{loggedName}' - not the same step");
            }

            if (loggedName != workbookName)
            {
                notes.Add($"{workbookName} -> {loggedName} (taken from the log)");
                renamed++;
            }

            row[3] = loggedName;
            rows.Add(row);
        }

        return new StdTableBuild(rows, notes) { Renamed = renamed };
    }

    /// <summary>True when the machine may run this step with no row in the workbook.</summary>
    public static bool IsMachineOnly(string name) => MachineOnlyRows.ContainsKey(name);

    /// <summary>One table row for a workbook step.</summary>
    private static object?[] Row(PlanStep step, int index, string name)
    {
        (object? low, object? high, string unit) = NormaliseLimits(step);
        return
        [
            string.IsNullOrWhiteSpace(step.SectionId) ? index.ToString() : step.SectionId,
            index,
            step.Marker,
            name,
            Cell(step, "time(sec)", "time"),
            Cell(step, "programduration(min)"),
            step.Vds,
            step.Vgs,
            low, high, unit,
            string.Empty,
            Cell(step, "hwbin"),
            SwText(step),
            string.IsNullOrWhiteSpace(step.ExcludeWhenFail) ? "Yes" : step.ExcludeWhenFail,
            step.Remarks,
        ];
    }

    /// <summary>A machine-only row, timed from the workbook's own IGSS step.</summary>
    private static object?[] MachineOnlyRow(Recipe recipe, string name, MachineOnly only, int index)
    {
        PlanStep? family = recipe.Steps.FirstOrDefault(s => s.NormalisedKey.StartsWith("igss", StringComparison.Ordinal));
        return
        [
            family?.SectionId ?? string.Empty,
            index,
            string.Empty,
            name,
            family is null ? string.Empty : Cell(family, "time(sec)", "time"),
            family is null ? string.Empty : Cell(family, "programduration(min)"),
            only.Vds, only.Vgs, only.Low, only.High, only.Unit,
            string.Empty,
            only.HwBin,
            only.Sw,
            string.IsNullOrWhiteSpace(family?.ExcludeWhenFail) ? "Yes" : family!.ExcludeWhenFail,
            string.Empty,
        ];
    }

    private static void AssertMachineOrder(IReadOnlyList<object?[]> rows, IReadOnlyList<string> expected)
    {
        var written = rows
            .Select(r => TestName.Normalise(System.Convert.ToString(r[3], System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        if (!expected.SequenceEqual(written))
        {
            int at = 0;
            while (at < expected.Count && at < written.Count && expected[at] == written[at]) at++;
            string expectedAt = at < expected.Count ? expected[at] : "<end>";
            string writtenAt = at < written.Count ? written[at] : "<end>";
            throw new InvalidDataException(
                $"the generated table does not match the machine step list at step {at + 1}: " +
                $"expected {expectedAt}, wrote {writtenAt}");
        }
    }

    /// <summary>
    /// A workbook cell, written back as the type it was: a number when it reads as
    /// one, the text otherwise (<c>15sec</c>, <c>5 min</c>, <c>-</c>).
    ///
    /// The type matters. The tester's own export stores <c>time (sec)</c> as a
    /// number on the 650/900/1200 V plans and as <c>15sec</c> on the SQ plans, and
    /// both must survive a round trip - a table that turns the number into the text
    /// "60" is still readable, but one that turns "15sec" into 15 has thrown away
    /// what the customer wrote.
    /// </summary>
    private static object? Cell(PlanStep step, params string[] headers)
    {
        string? raw = null;
        foreach (string header in headers)
        {
            if (step.Fields.TryGetValue(header, out string? cell) && !string.IsNullOrWhiteSpace(cell))
            {
                raw = cell;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string text = raw.Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            return value == Math.Floor(value) && Math.Abs(value) < 1e15 ? (long)value : value;
        }

        return text;
    }

    /// <summary><c>196 = F_PRE_ON</c> - the plain form the std tables use.</summary>
    private static string SwText(PlanStep step) =>
        step.SwBinCode is { } code ? $"{code:X} = {step.SwBinLegend}" : step.SwBinLegend;

    /// <summary>
    /// The limit in the unit the engine expects: gate steps in nA (factor 1),
    /// everything else in uA (the engine scales x1000).
    /// </summary>
    public static (object? Low, object? High, string Unit) NormaliseLimits(PlanStep step)
    {
        bool gate = GateKeywords.Any(k => step.Name.ToUpperInvariant().Contains(k, StringComparison.Ordinal));
        object? low = LimitValue(step.Min, step.Units, gate);
        object? high = LimitValue(step.Max, step.Units, gate);
        return (low, high, gate ? "nA" : "µA");
    }

    private static object? LimitValue(string? raw, string? unitCell, bool gate)
    {
        (double? value, string unit) = SplitLimit(raw, unitCell);
        if (value is null) return null;
        double microamps = value.Value * UnitToMicroamps.GetValueOrDefault(unit, 1.0);
        return gate ? Math.Round(microamps * 1000) : Math.Round(microamps, 6);
    }

    /// <summary>(number, unit) from <c>500uA</c> / <c>1,5 mA</c>, or a number plus its unit cell.</summary>
    internal static (double? Value, string Unit) SplitLimit(string? raw, string? unitCell)
    {
        string cellUnit = (unitCell ?? string.Empty).Trim().ToLowerInvariant().Replace('μ', 'µ');
        if (string.IsNullOrWhiteSpace(raw)) return (null, cellUnit);

        string text = raw.Trim().Replace(',', '.').Replace('μ', 'µ');
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double plain))
            return (plain, cellUnit);

        var number = new System.Text.StringBuilder();
        var unit = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsDigit(c) || c is '.' or '-' or '+')
            {
                if (unit.Length > 0) break;
                number.Append(c);
            }
            else if (char.IsLetter(c) || c == 'µ')
            {
                unit.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                continue;
            }
            else
            {
                break;
            }
        }

        string letters = unit.Length > 0 ? unit.ToString().ToLowerInvariant() : cellUnit;
        return (double.TryParse(number.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : null, letters);
    }
}

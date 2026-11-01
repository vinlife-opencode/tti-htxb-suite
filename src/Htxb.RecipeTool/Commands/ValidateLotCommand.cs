using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Validate;

namespace Htxb.RecipeTool.Commands;

/// <summary>
/// <c>validate-lot</c> - check a processed lot against the limits table it should
/// have used.
///
/// Answers the questions that matter after a run: did every step of the program find
/// a row in the table, do the BiDF's limits equal the table's, are the bins sane, do
/// the counts add up, and does the slot summary advertise the bins the table defines.
/// It reads only.
///
/// The table is resolved through <c>CustomerProfileSelection.csv</c> - the one file
/// that says which table a recipe uses - unless <c>--table</c> names one explicitly,
/// so a run also proves which table the recipe actually points at.
///
/// Exit codes: 0 every check passed, 1 discrepancies found, 2 usage or IO error.
/// </summary>
internal static class ValidateLotCommand
{
    public static int Run(ArgReader args)
    {
        args.RejectUnknown("table", "tables", "json");

        string lot = args.Positional(0);
        if (lot.Length == 0)
            throw new UsageException(
                "usage: Htxb.RecipeTool validate-lot <lot folder> [--table Std_NN.xlsx] [--tables DIR] [--json]");

        (string lotFolder, _) = LotValidator.ResolveLot(lot);
        if (!Directory.Exists(lotFolder))
            throw new UsageException($"Lot folder not found: {lotFolder}");

        var bidfs = LotValidator.FindBidfs(lotFolder);
        if (bidfs.Count == 0)
            throw new UsageException($"No .bidf under {Path.Combine(lotFolder, "_BiDF")}.");

        string recipe = RecipeOf(bidfs[0]);
        string tablesFolder = args.Value("tables") ?? ToolPaths.InstalledTables;

        ProfileRow? profile = null;
        string tablePath;

        if (args.Value("table") is { } explicitTable)
        {
            tablePath = Path.IsPathRooted(explicitTable) || File.Exists(explicitTable)
                ? explicitTable
                : Path.Combine(tablesFolder, explicitTable);
            profile = FindProfile(tablesFolder, recipe);
        }
        else
        {
            profile = FindProfile(tablesFolder, recipe);
            if (profile is null || profile.StdTable.Length == 0)
                throw new UsageException(
                    $"No profile for '{recipe}' in {ProfileCsvAdapter.RelativePath} " +
                    $"(looked in {tablesFolder}) and no --table given.");

            tablePath = Path.Combine(tablesFolder, profile.StdTable);
        }

        if (!File.Exists(tablePath)) throw new UsageException($"Table not found: {tablePath}");

        LotReport report = LotValidator.Validate(lotFolder, tablePath, profile);
        Console.WriteLine(args.Has("json") ? Json(report) : Text(report));

        if (args.Has("json"))
        {
            Console.WriteLine();
            Console.WriteLine($"{report.Passed} passed, {report.Failed} failed");
        }

        if (!report.Ok)
        {
            var failed = report.Checks.Where(c => !c.Passed).Select(c => c.Name)
                .Concat(report.Slots.Where(s => s.Failed > 0).Select(s => $"slot {s.Slot}"))
                .ToList();
            Console.WriteLine("failed: " + string.Join(", ", failed));
            return 1;
        }

        Console.WriteLine("LOT VALIDATED");
        return 0;
    }

    private static string RecipeOf(string bidfPath)
    {
        using var bidf = new Htxb.RecipeCore.Bidf.BidfReader(bidfPath);
        return bidf.Scalar("select programname from tbl_run") ?? string.Empty;
    }

    private static ProfileRow? FindProfile(string tablesFolder, string recipe)
    {
        string csv = ProfileCsvAdapter.PathForTables(tablesFolder);
        if (!File.Exists(csv)) return null;
        return ProfileCsvAdapter.Find(ProfileCsvAdapter.Read(csv), recipe);
    }

    private static string Text(LotReport report)
    {
        var lines = new List<string>
        {
            $"{report.Lot}",
            $"  recipe : {report.Recipe}",
            $"  table  : {Path.GetFileName(report.TablePath)}  ({report.TableSteps} step(s))",
        };

        if (report.Profile is { } profile)
        {
            lines.Add($"  profile: DUT '{profile.DutName}', package '{profile.DutPackage}', " +
                      $"drift {(profile.DriftEn.Length > 0 ? profile.DriftEn : "-")}");
        }

        lines.Add("");
        lines.Add($"  {report.Slots.Count} slot(s), {report.SlotsPassed} clean");

        foreach (LotCheck check in report.Checks)
            lines.Add("  " + check);

        foreach (SlotReport slot in report.Slots.Where(s => s.Failed > 0))
        {
            lines.Add("");
            lines.Add($"  -- slot {slot.Slot}  {slot.Bidf}");
            foreach (LotCheck check in slot.Checks.Where(c => !c.Passed))
                lines.Add("    " + check);
        }

        var totals = report.Slots
            .SelectMany(s => s.Bins)
            .GroupBy(b => b.Key)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}x{g.Sum(b => b.Value)}");

        lines.Add("");
        lines.Add("  bins (all slots): " + string.Join(", ", totals));
        lines.Add($"  {report.Passed} passed, {report.Failed} failed");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Json(LotReport report)
    {
        var payload = new
        {
            lot = report.Lot,
            recipe = report.Recipe,
            table = Path.GetFileName(report.TablePath),
            tableSteps = report.TableSteps,
            profile = report.Profile is { } p
                ? new { dut = p.DutName, package = p.DutPackage, driftEn = p.DriftEn, driftIsgs = p.DriftIsgs }
                : null,
            passed = report.Passed,
            failed = report.Failed,
            ok = report.Ok,
            checks = report.Checks.Select(c => new { name = c.Name, passed = c.Passed, detail = c.Detail }),
            slots = report.Slots.Select(s => new
            {
                slot = s.Slot,
                bidf = s.Bidf,
                steps = s.Steps,
                parts = s.Parts,
                good = s.Good,
                tested = s.Tested,
                bins = s.Bins.OrderBy(b => b.Key).ToDictionary(b => b.Key, b => b.Value),
                dut = s.DutName,
                package = s.Package,
                failed = s.Failed,
                failures = s.Checks.Where(c => !c.Passed)
                    .Select(c => new { name = c.Name, detail = c.Detail }),
            }),
        };

        return System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}

using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Ingest;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeTool.Commands;

/// <summary>
/// <c>std-ingest</c> - build a <c>Std_*.xlsx</c> limits table from a customer recipe
/// workbook.
///
/// The workbook is the specification: its steps, limits, HW BIN and SW BIN are what
/// the table states. <c>--names-from</c> renames each row to the name the tester
/// actually logs, pairing position by position and refusing to pair two rows whose
/// names disagree once the re-issue decorations are removed - a re-issued recipe
/// keeps its plan but renames its steps, and a table written with the old names
/// silently loses those steps in the engine.
///
/// This writes the seventeen-column sheet. For a table that is meant to run on a
/// machine, use <c>std-align</c>: it also takes the machine's own extra steps.
/// </summary>
internal static class StdIngestCommand
{
    public static int Run(ArgReader args)
    {
        args.RejectUnknown("out", "o", "sheet", "names-from");

        string workbook = args.Positional(0);
        if (workbook.Length == 0)
            throw new UsageException(
                "usage: Htxb.RecipeTool std-ingest <workbook.xlsx> -o <Std_NN.xlsx> [--sheet NAME] [--names-from LOT]");

        string output = args.Value("out") ?? args.Value("o")
            ?? throw new UsageException("std-ingest needs -o <path of the Std_*.xlsx to write>.");

        if (!File.Exists(workbook)) throw new UsageException($"Workbook not found: {workbook}");

        Recipe recipe = PlanSheetAdapter.Read(workbook);
        StdTableBuild build = StdTableBuilder.BuildFromWorkbook(recipe);

        string? namesFrom = args.Value("names-from");
        if (namesFrom is not null)
        {
            var logs = SlotCsvReader.CollectSlotLogs(namesFrom);
            if (logs.Count == 0)
                throw new UsageException($"No slot log under {namesFrom}.");

            build = StdTableBuilder.ApplyLoggedNames(build, SlotCsvReader.ReadTestNames(logs[0]));
        }

        StdTableWriter.Write(output, build, withRelativeColumn: true);

        Console.WriteLine($"wrote {output}");
        Console.WriteLine($"  tests      : {build.Count}");
        Console.WriteLine($"  renamed    : {build.Renamed} (taken from the log)");
        Console.WriteLine($"  without bin: {build.WithoutSwBin}");

        foreach (string note in build.Notes) Console.WriteLine("  " + note);
        return 0;
    }
}

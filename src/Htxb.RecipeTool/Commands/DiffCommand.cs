using Htxb.RecipeCore.Diff;

namespace Htxb.RecipeTool.Commands;

/// <summary>
/// <c>diff</c> - compare one customer workbook with one machine <c>.zadz</c>.
///
/// The workbook is the contract; the <c>.zadz</c> is what the tester will run and
/// therefore what the CSV will contain. The command reports the differences and
/// changes nothing. <c>--strict</c> makes any difference a non-zero exit so a
/// delivery can be gated.
/// </summary>
internal static class DiffCommand
{
    public static int Run(ArgReader args)
    {
        args.RejectUnknown("json", "strict");

        string workbook = args.Positional(0);
        string zadz = args.Positional(1);
        if (workbook.Length == 0 || zadz.Length == 0)
            throw new UsageException(
                "usage: Htxb.RecipeTool diff <workbook.xlsx> <recipe.zadz> [--json] [--strict]");

        if (!File.Exists(workbook)) throw new UsageException($"Workbook not found: {workbook}");
        if (!File.Exists(zadz)) throw new UsageException($"Machine recipe not found: {zadz}");

        DiffResult result = RecipeDiffer.Compare(workbook, zadz);

        Console.WriteLine(args.Has("json") ? DiffReport.Json(result) : DiffReport.Text(result));
        return args.Has("strict") && !result.Aligned ? 1 : 0;
    }
}

using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Diff;
using Htxb.RecipeCore.Ingest;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeTool.Commands;

/// <summary>
/// <c>std-align</c> - build the per-recipe table a machine will actually run.
///
/// A workbook and the machine's <c>.zadz</c> disagree in two ways that both break
/// processing if ignored: the tester logs the machine's names (<c>HTGB1</c>, not
/// <c>HTGB</c>), and the machine can run a step the workbook does not list (the two
/// <c>IGSS_-10V</c> rows on five BR recipes). This writes the machine's order and
/// names, keeps the workbook's limits - the machine's own limits are the tester's
/// internal pass/fail, not the specification - and takes the extra steps from the
/// two rows defined in <see cref="StdTableBuilder"/>, never from an older table.
/// </summary>
internal static class StdAlignCommand
{
    public static int Run(ArgReader args)
    {
        args.RejectUnknown("zadz", "out", "o", "aliases");

        string workbook = args.Positional(0);
        string? zadz = args.Value("zadz");

        if (workbook.Length == 0 || zadz is null)
            throw new UsageException(
                "usage: Htxb.RecipeTool std-align <workbook.xlsx> --zadz <recipe.zadz> -o <Std_NN.xlsx> [--aliases FILE]");

        string output = args.Value("out") ?? args.Value("o")
            ?? throw new UsageException("std-align needs -o <path of the Std_*.xlsx to write>.");

        if (!File.Exists(workbook)) throw new UsageException($"Workbook not found: {workbook}");
        if (!File.Exists(zadz)) throw new UsageException($"Machine recipe not found: {zadz}");

        IReadOnlyDictionary<string, string>? aliases = null;
        if (args.Value("aliases") is { } aliasPath)
        {
            if (!File.Exists(aliasPath)) throw new UsageException($"Alias file not found: {aliasPath}");
            aliases = AliasFile.Read(aliasPath);
        }

        Recipe recipe = PlanSheetAdapter.Read(workbook);
        ZadzRecipe machine = ZadzAdapter.Read(zadz);

        StdTableBuild build = StdTableBuilder.BuildAligned(recipe, machine, aliases);
        StdTableWriter.Write(output, build);

        Console.WriteLine($"{Path.GetFileName(output)}  {build.Count} step(s)");
        foreach (string note in build.Notes) Console.WriteLine("  " + note);
        return 0;
    }
}

/// <summary>
/// The shared workbook -&gt; machine alias map (<c>docs/test-vectors/recipe-aliases.json</c>),
/// the same file the test suites assert <see cref="RecipeAligner.DefaultAliases"/>
/// against.
/// </summary>
internal static class AliasFile
{
    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("aliases", out var aliases) ||
            aliases.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new UsageException($"{Path.GetFileName(path)} has no 'aliases' object.");

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in aliases.EnumerateObject())
        {
            if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String &&
                property.Value.GetString() is { Length: > 0 } target)
            {
                map[property.Name] = target;
            }
        }

        return map;
    }
}

using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeTool.Commands;

/// <summary>
/// <c>recipes</c> - parse every workbook in a delivery and show what the reader saw.
///
/// This is the C0 acceptance command: the counts and identity issues it prints are
/// the ones measured independently with openpyxl, so a mismatch is a parser bug and
/// not an opinion.
/// </summary>
internal static class RecipesCommand
{
    public static int Run(ArgReader args)
    {
        args.RejectUnknown("samples", "json");

        string root = args.Value("samples") ?? ToolPaths.DefaultSamples;
        if (!Directory.Exists(root)) throw new UsageException($"Samples folder not found: {root}");

        var files = Directory.EnumerateFiles(root, "*.xlsx", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0) throw new UsageException($"No .xlsx workbooks under {root}");

        Console.WriteLine($"Samples : {root}");
        Console.WriteLine($"Workbooks: {files.Count}");
        Console.WriteLine();

        int problems = 0;

        Console.WriteLine($"{"recipe",-26} {"family",-16} {"steps",5} {"ign",4}  {"line",-6} {"pkg",-6} details");
        Console.WriteLine(new string('-', 118));

        foreach (string file in files)
        {
            string family = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);

            try
            {
                var recipe = PlanSheetAdapter.Read(file);
                problems += recipe.IdentityIssues.Count;

                var notes = new List<string>();
                notes.AddRange(recipe.IdentityIssues.Select(i => "IDENTITY: " + i));
                notes.AddRange(recipe.Warnings);

                Console.WriteLine(
                    $"{recipe.FileStem,-26} {family,-16} {recipe.StepCount,5} {recipe.IgnoredRows.Count,4}  " +
                    $"{recipe.Profile?.Line ?? "-",-6} {recipe.PackageCode,-6} {string.Join("; ", notes)}");
            }
            catch (Exception error)
            {
                problems++;
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(file),-26} {family,-16} {"-",5} {"-",4}  " +
                                  $"{string.Join("; ", new[] { error.GetType().Name + ": " + error.Message })}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"identity issues: {problems}");
        return problems == 0 ? 0 : 1;
    }

    /// <summary><c>steps &lt;workbook&gt;</c> - dump one plan's steps, for eyeballing a parse.</summary>
    public static int Steps(ArgReader args)
    {
        args.RejectUnknown("file");
        string file = args.Value("file") ?? args.Positional(0);
        if (file.Length == 0) throw new UsageException("steps: give a workbook path.");

        var recipe = PlanSheetAdapter.Read(file);

        Console.WriteLine($"file   : {recipe.FileName}");
        Console.WriteLine($"sheet  : {recipe.PlanSheetName}");
        Console.WriteLine($"title  : {recipe.Title}");
        Console.WriteLine($"recipe : {recipe.SheetRecipeName}");
        Console.WriteLine($"package: {recipe.PackageCode}");
        Console.WriteLine($"steps  : {recipe.StepCount}   ignored rows: {recipe.IgnoredRows.Count}");
        if (recipe.Profile is { } profile)
        {
            Console.WriteLine($"profile: line={profile.Line} bvdss={profile.NominalBvdss} " +
                              $"cp={profile.Cp} pkg={profile.Package}");
        }
        Console.WriteLine();

        Console.WriteLine($"{"#",3} {"row",4} {"sect",4} {"name",-22} {"sec",6} {"min",6} {"vds",7} {"vgs",6} {"swbin",-18} {"legend",-18}");
        Console.WriteLine(new string('-', 118));
        foreach (var step in recipe.Steps)
        {
            Console.WriteLine(
                $"{step.Ordinal,3} {step.Row,4} {step.SectionId,4} {step.Name,-22} " +
                $"{step.TimeSeconds?.ToString("0.##") ?? "",6} " +
                $"{step.EffectiveProgramMinutes?.ToString("0.##") ?? "",6} " +
                $"{step.Vds,7} {step.Vgs,6} {step.SwBinHex,-18} {step.SwBinLegend,-18}");
        }

        foreach (var issue in recipe.IdentityIssues) Console.WriteLine($"IDENTITY: {issue}");
        foreach (var warning in recipe.Warnings) Console.WriteLine($"WARNING : {warning}");

        return recipe.IdentityIssues.Count == 0 ? 0 : 1;
    }
}

using System.Text;
using System.Text.Json;
using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Fit;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeTool.Commands;

/// <summary>
/// <c>fit</c> - for every recipe in a delivery, answer three questions:
///
///   1. is the recipe in CustomerProfileSelection.csv at all?   (if not: no BiDF)
///   2. which std table would the engine use?
///   3. do the workbook's steps agree with that table, field by field?
///
/// The verdicts are predictions, not proof: only a lot through the watcher proves a
/// table. <c>flowguard</c> is the observer; this is the forecast.
/// </summary>
internal static class FitCommand
{
    public static int Run(ArgReader args)
    {
        args.RejectUnknown("samples", "tables", "profile", "json", "strict", "quiet");

        string samples = args.Value("samples") ?? ToolPaths.DefaultSamples;
        string tables = args.Value("tables") ?? ToolPaths.DefaultTables;
        bool json = args.Has("json");
        bool strict = args.Has("strict");
        bool quiet = args.Has("quiet");

        if (!Directory.Exists(samples)) throw new UsageException($"Samples folder not found: {samples}");
        if (!Directory.Exists(tables)) throw new UsageException($"Tables folder not found: {tables}");

        string profilePath = args.Value("profile") ?? ProfileCsvAdapter.PathForTables(tables);
        if (!File.Exists(profilePath))
            throw new UsageException($"Profile CSV not found: {profilePath} (pass --profile)");

        var profileRows = ProfileCsvAdapter.Read(profilePath);
        var tableCache = new Dictionary<string, StdTable>(StringComparer.OrdinalIgnoreCase);

        var results = new List<FitResult>();

        foreach (string file in Directory.EnumerateFiles(samples, "*.xlsx", SearchOption.AllDirectories)
                                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            Recipe recipe;
            try
            {
                recipe = PlanSheetAdapter.Read(file);
            }
            catch (Exception error)
            {
                results.Add(new FitResult
                {
                    Recipe = Path.GetFileNameWithoutExtension(file),
                    Family = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty),
                    StepCount = 0,
                    Verdict = FitVerdict.Error,
                    Notes = [error.GetType().Name + ": " + error.Message],
                });
                continue;
            }

            var profile = ProfileCsvAdapter.Find(profileRows, recipe.FileStem);

            if (profile is null)
            {
                results.Add(FitChecker.NoProfile(
                    recipe, FitChecker.SuggestTable(recipe, LoadTables(tables, tableCache))));
                continue;
            }

            string tablePath = Path.Combine(tables, profile.StdTable);
            if (profile.StdTable.Length == 0 || !File.Exists(tablePath))
            {
                results.Add(new FitResult
                {
                    Recipe = recipe.FileStem,
                    Family = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty),
                    StepCount = recipe.StepCount,
                    Profile = profile,
                    TableFile = profile.StdTable,
                    Verdict = FitVerdict.NoTable,
                    Notes = [$"profile selects '{profile.StdTable}', which is not in {tables}"],
                    IdentityIssues = recipe.IdentityIssues,
                });
                continue;
            }

            if (!tableCache.TryGetValue(tablePath, out StdTable? table))
            {
                table = StdTableAdapter.Read(tablePath);
                tableCache[tablePath] = table;
            }

            results.Add(FitChecker.Check(recipe, table, profile));
        }

        if (json) WriteJson(results, samples, tables, profilePath);
        else WriteReport(results, samples, tables, quiet);

        bool allFit = results.Count > 0 && results.All(r => r.Verdict == FitVerdict.Fits);
        return strict && !allFit ? 1 : 0;
    }

    // ------------------------------------------------------------------ output

    private static void WriteReport(List<FitResult> results, string samples, string tables, bool quiet)
    {
        var text = new StringBuilder();

        text.AppendLine($"Samples : {samples}");
        text.AppendLine($"Tables  : {tables}");
        text.AppendLine($"Recipes : {results.Count}");
        text.AppendLine();

        text.AppendLine($"{"recipe",-26} {"family",-16} {"steps",5} {"verdict",-11} {"table",-24} {"ex/norm",8} {"miss",4} {"extra",5} {"diffs",5}");
        text.AppendLine(new string('-', 126));

        foreach (var result in results)
        {
            string table = result.TableFile ?? result.SuggestedTable ?? "-";
            string counts = result.TableFile is null ? "-" : $"{result.MatchedExact}/{result.MatchedNormalised}";

            text.AppendLine(
                $"{result.Recipe,-26} {result.Family,-16} {result.StepCount,5} {result.VerdictLabel,-11} " +
                $"{Trim(table, 24),-24} {counts,8} {result.Missing,4} {result.TableOnly,5} {result.FieldDifferenceCount,5}");
        }

        text.AppendLine();
        AppendNotes(text, results, quiet);
        text.AppendLine(Summary(results));

        Console.Write(text.ToString());
    }

    private static void AppendNotes(StringBuilder text, List<FitResult> results, bool quiet)
    {
        foreach (var result in results)
        {
            bool interesting = result.Verdict is not FitVerdict.Fits;
            if (!interesting && quiet) continue;

            bool hasDetail = result.IdentityIssues.Count > 0 ||
                             result.Notes.Count > 0 ||
                             result.Steps.Any(s => s.IsMissing || s.Differences.Count > 0) ||
                             result.Steps.Any(s => s.TableName is not null && !s.NameExact);

            if (!hasDetail) continue;

            text.AppendLine($"== {result.Recipe}  [{result.VerdictLabel}]");

            foreach (string issue in result.IdentityIssues) text.AppendLine($"   identity : {issue}");
            foreach (string note in result.Notes) text.AppendLine($"   note     : {note}");

            foreach (var step in result.Steps.Where(s => s.IsMissing))
                text.AppendLine($"   missing  : {step.Ordinal,2}. {step.Name}  (no row in the table)");

            foreach (var step in result.Steps.Where(s => s.TableName is not null && !s.NameExact))
            {
                string marker = step.NameNormalised ? "=" : "!";
                text.AppendLine($"   name {marker}   : {step.Name,-22} <-> {step.TableName}");
            }

            foreach (var step in result.Steps.Where(s => s.Differences.Count > 0))
            {
                foreach (var diff in step.Differences)
                    text.AppendLine($"   diff     : {step.Name,-22} {diff.Field,-18} recipe={diff.Recipe,-12} table={diff.Table}");
            }

            var tableOnly = result.Steps.Where(s => s.TableName is null && s.TableRowName is not null).Select(s => s.TableRowName!).ToList();
            if (tableOnly.Count > 0)
                text.AppendLine($"   table    : {tableOnly.Count} row(s) not in the recipe: {string.Join(", ", tableOnly)}");

            text.AppendLine();
        }
    }

    private static string Summary(List<FitResult> results)
    {
        var counts = results
            .GroupBy(r => r.VerdictLabel)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}={g.Count()}");

        int fits = results.Count(r => r.Verdict == FitVerdict.Fits);
        return $"{fits}/{results.Count} FITS   ({string.Join(", ", counts)})";
    }

    private static void WriteJson(List<FitResult> results, string samples, string tables, string profilePath)
    {
        var payload = new
        {
            samples,
            tables,
            profile = profilePath,
            total = results.Count,
            fits = results.Count(r => r.Verdict == FitVerdict.Fits),
            recipes = results.Select(r => new
            {
                recipe = r.Recipe,
                family = r.Family,
                stepCount = r.StepCount,
                verdict = r.VerdictLabel,
                table = r.TableFile,
                tableRows = r.TableRows,
                suggestedTable = r.SuggestedTable,
                suggestedHits = r.SuggestedHits,
                profile = r.Profile is null ? null : new
                {
                    customer = r.Profile.Customer,
                    recipeGroup = r.Profile.RecipeGroup,
                    stdTable = r.Profile.StdTable,
                    typeCode = r.Profile.TypeCode,
                    bidfEn = r.Profile.BidfEn,
                },
                matchedExact = r.MatchedExact,
                matchedNormalised = r.MatchedNormalised,
                missing = r.Missing,
                tableOnly = r.TableOnly,
                fieldDifferences = r.FieldDifferenceCount,
                identityIssues = r.IdentityIssues,
                notes = r.Notes,
                steps = r.Steps
                    .Where(s => s.IsMissing || s.Differences.Count > 0 || (s.TableName is not null && !s.NameExact))
                    .Select(s => new
                    {
                        ordinal = s.Ordinal,
                        name = s.Name,
                        tableName = s.TableName,
                        tableRowName = s.TableRowName,
                        nameExact = s.NameExact,
                        nameNormalised = s.NameNormalised,
                        missing = s.IsMissing,
                        differences = s.Differences.Select(d => new { field = d.Field, recipe = d.Recipe, table = d.Table }),
                    }),
            }),
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(payload, options));
    }

    private static string Trim(string text, int width) =>
        text.Length <= width ? text : "…" + text[^(width - 1)..];

    // ------------------------------------------------------------------ suggest

    /// <summary>
    /// Every readable table in the folder, cached for the run. A folder holding a
    /// stray or corrupt workbook must not abort the audit, so an unreadable file is
    /// skipped rather than thrown.
    /// </summary>
    private static List<StdTable> LoadTables(string tablesDirectory, Dictionary<string, StdTable> cache)
    {
        var tables = new List<StdTable>();

        foreach (string path in Directory.EnumerateFiles(tablesDirectory, "*.xlsx")
                                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (cache.TryGetValue(path, out StdTable? cached))
            {
                tables.Add(cached);
                continue;
            }

            try
            {
                var table = StdTableAdapter.Read(path);
                cache[path] = table;
                tables.Add(table);
            }
            catch (Exception)
            {
                // not a limits table; keep going
            }
        }

        return tables;
    }
}

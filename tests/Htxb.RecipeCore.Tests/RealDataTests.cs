using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Fit;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Tests;

/// <summary>
/// Checks against the real 261028 delivery and the real std tables.
///
/// These are golden values measured independently with openpyxl, so they pin the
/// parser to a fact rather than to its own previous output. If the customer sends
/// a corrected delivery, the numbers here change and the change is deliberate.
/// </summary>
internal sealed class Delivery
{
    private readonly Dictionary<string, Recipe> _recipes = new(StringComparer.OrdinalIgnoreCase);

    public required string SamplesRoot { get; init; }
    public required string TablesRoot { get; init; }

    public IReadOnlyList<ProfileRow> ProfileRows { get; private set; } = [];

    public void Load()
    {
        string profilePath = Path.Combine(TablesRoot, ProfileCsvAdapter.RelativePath);
        ProfileRows = ProfileCsvAdapter.Read(profilePath);

        foreach (string file in Directory.EnumerateFiles(SamplesRoot, "*.xlsx", SearchOption.AllDirectories))
        {
            var recipe = PlanSheetAdapter.Read(file);
            _recipes[recipe.FileStem] = recipe;
        }
    }

    public Recipe this[string stem] => _recipes.TryGetValue(stem, out Recipe? recipe)
        ? recipe
        : throw new CheckFailed($"recipe '{stem}' not found in {SamplesRoot}");

    public IReadOnlyCollection<Recipe> All => _recipes.Values;

    public StdTable Table(string fileName) => StdTableAdapter.Read(Path.Combine(TablesRoot, fileName));

    /// <summary>Every limits table in the folder — what the suggestion rule chooses from.</summary>
    public IReadOnlyList<StdTable> AllTables() => _tables ??=
        Directory.EnumerateFiles(TablesRoot, "*.xlsx")
                 .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                 .Select(p => StdTableAdapter.Read(p))
                 .ToList();

    private IReadOnlyList<StdTable>? _tables;

    /// <summary>The fit verdict for one recipe, using the profile row the watcher would use.</summary>
    public FitResult Fit(string stem)
    {
        var recipe = this[stem];
        var profile = ProfileCsvAdapter.Find(ProfileRows, recipe.FileStem);

        if (profile is null)
            return FitChecker.NoProfile(recipe, FitChecker.SuggestTable(recipe, AllTables()));

        return FitChecker.Check(recipe, Table(profile.StdTable), profile);
    }
}

internal static class RealDataTests
{
    /// <summary>Every workbook in the delivery, with the counts openpyxl produced.</summary>
    private static readonly (string Stem, int Steps, int Ignored)[] ExpectedCounts =
    [
        ("P_TM_BRI9_B_07_4_32", 32, 25),
        ("P_TM_BRIC_B_05_4_32", 32, 25),
        ("P_TM_BRIG_B_04_4_32", 32, 25),
        ("P_TM_BRJ7_B_07_4_32", 32, 25),
        ("P_TM_BRJJ_B_09_4_32", 32, 25),
        ("P_TM_BRJO_B_06_4_32", 32, 25),
        ("P_TM_BRF5_B_07_4_32", 32, 25),
        ("P_TM_BRF7_B_07_4_32", 32, 25),
        ("P_TM_BRF9_B_05_4_32", 32, 25),
        ("P_TM_BRFC_B_05_4_32", 32, 25),
        ("P_TM_BR9M_B_05_4_32", 32, 25),
        ("P_TM_SQFJ_B_03_4_5",  15, 13),
        ("P_TM_SQGW_B_02_4_5",  15,  6),
        ("P_TM_SQJJ_B_03_4_5",  15, 13),
        ("P_TM_SQK9_B_03_4_5",  15, 13),
        ("P_TM_SQKC_B_03_4_5",  15, 13),
        ("P_TM_SQKW_B_03_4_5",  15, 13),
    ];

    public static void Register(Runner runner, Delivery delivery)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        delivery.Load();
        clock.Stop();

        Log.Section($"real delivery ({clock.ElapsedMilliseconds} ms to parse)");

        runner.Check("delivery holds 17 workbooks", () =>
            Assert.Equal(17, delivery.All.Count, "workbook count"));

        runner.Check("every step/ignored count matches the openpyxl measurement", () =>
        {
            foreach (var (stem, steps, ignored) in ExpectedCounts)
            {
                Assert.Equal(steps, delivery[stem].StepCount, $"{stem} steps");
                Assert.Equal(ignored, delivery[stem].IgnoredRows.Count, $"{stem} ignored rows");
            }
        });

        // The two defects this suite was written for. Both are merge handling, and
        // both used to inflate the step count without any other symptom.
        runner.Check("whole-row merge paragraph never becomes a step name", () =>
        {
            foreach (var recipe in delivery.All)
            {
                foreach (var step in recipe.Steps)
                {
                    Assert.True(step.Name.Length is > 0 and < 40,
                        $"{recipe.FileStem}: step name looks like prose: \"{step.Name}\"");
                    Assert.True(!step.Name.Contains("Formula", StringComparison.OrdinalIgnoreCase),
                        $"{recipe.FileStem}: change-log text leaked into a step name: \"{step.Name}\"");
                }
            }
        });

        runner.Check("a vertically merged step name is emitted once", () =>
        {
            // BRF7 merges D7:D10 and E69:F70; SQGW merges D8:D9 etc. A duplicate
            // here means the anchor rule regressed.
            foreach (var stem in new[] { "P_TM_BRF7_B_07_4_32", "P_TM_SQGW_B_02_4_5" })
            {
                var recipe = delivery[stem];
                var repeated = recipe.Steps
                    .GroupBy(s => (s.Name, s.Row))
                    .Where(g => g.Count() > 1)
                    .ToList();
                Assert.Equal(0, repeated.Count, $"{stem} rows emitted twice");
            }
        });

        runner.Check("only BRFC has an identity contradiction, and it names the right pair", () =>
        {
            var broken = delivery.All.Where(r => r.IdentityIssues.Count > 0)
                                     .Select(r => r.FileStem)
                                     .OrderBy(n => n, StringComparer.Ordinal)
                                     .ToList();
            Assert.SequenceEqual(new[] { "P_TM_BRFC_B_05_4_32" }, broken, "recipes with identity issues");

            var issues = delivery["P_TM_BRFC_B_05_4_32"].IdentityIssues;
            Assert.Equal(2, issues.Count, "BRFC identity issue count");
            Assert.True(issues.All(i => i.Contains("P_TM_BRFC_B_04_5_32", StringComparison.Ordinal)),
                "BRFC issues must name the declared recipe P_TM_BRFC_B_04_5_32");
        });

        // Every workbook declares its recipe name in the Reference Table, but not in
        // a fixed column: AD for the 1200 V/650 V sheets, Y for SQ, Q for SQGW. Only
        // a header-text lookup finds all seventeen.
        runner.Check("all 17 workbooks declare their own recipe name in the Reference Table", () =>
        {
            foreach (var recipe in delivery.All)
            {
                Assert.True(recipe.Profile is not null, $"{recipe.FileStem} has no Reference Table");

                // BRFC declares a different recipe; that contradiction has its own
                // check above, so it is excluded from the "matches" rule rather than
                // being quietly allowed to pass here.
                if (recipe.IdentityIssues.Count > 0) continue;

                Assert.Equal(recipe.FileStem, recipe.Profile!.BiRecipeName,
                    $"{recipe.FileStem} BI recipe name");
            }
        });

        runner.Check("SQGW keeps its variant layout: name column D, header row 8, 15 steps", () =>
        {
            var recipe = delivery["P_TM_SQGW_B_02_4_5"];
            Assert.Equal(15, recipe.StepCount, "SQGW steps");
            Assert.True(recipe.Steps.All(s => s.Row >= 10), "SQGW steps must start below header row 8");
        });

        runner.Check("profile CSV has 7 STM rows and finds BRI9", () =>
        {
            int stm = delivery.ProfileRows.Count(r => r.Customer == "STM");
            Assert.Equal(7, stm, "STM profile row count");

            var row = ProfileCsvAdapter.Find(delivery.ProfileRows, "P_TM_BRI9_B_07_4_32");
            Assert.True(row is not null, "BRI9 must have a profile row");
            Assert.Equal("Std_02_ST_TO247.xlsx", row!.StdTable, "BRI9 std table");
        });

        runner.Check("std table column counts: Std_02 has 16, Std_07 has 17", () =>
        {
            var to247 = delivery.Table("Std_02_ST_TO247.xlsx");
            var v1200 = delivery.Table("Std_07_ST_1200V.xlsx");

            Assert.Equal(16, to247.Columns.Count, "Std_02 column count");
            Assert.Equal(17, v1200.Columns.Count, "Std_07 column count");
            Assert.True(!to247.HasRelativeSectionColumn, "Std_02 must report the missing relative column");
            Assert.True(v1200.HasRelativeSectionColumn, "Std_07 must have 'Test Sect ID Relative'");
        });

        runner.Check("the measured fit matrix: 0/17 FITS, 4 MISMATCH, 13 NO_PROFILE", () =>
        {
            var verdicts = new Dictionary<string, FitVerdict>(StringComparer.Ordinal);

            foreach (var recipe in delivery.All)
            {
                var profile = ProfileCsvAdapter.Find(delivery.ProfileRows, recipe.FileStem);
                FitResult result = profile is null
                    ? FitChecker.NoProfile(recipe, FitChecker.SuggestTable(recipe, delivery.AllTables()))
                    : FitChecker.Check(recipe, delivery.Table(profile.StdTable), profile);

                verdicts[recipe.FileStem] = result.Verdict;
            }

            Assert.Equal(0, verdicts.Values.Count(v => v == FitVerdict.Fits), "FITS");
            Assert.Equal(4, verdicts.Values.Count(v => v == FitVerdict.Mismatch), "MISMATCH");
            Assert.Equal(13, verdicts.Values.Count(v => v == FitVerdict.NoProfile), "NO_PROFILE");
        });

        runner.Check("the four profiled recipes each report 24 exact / 25 normalised", () =>
        {
            foreach (var (stem, table) in new[]
            {
                ("P_TM_BRI9_B_07_4_32", "Std_02_ST_TO247.xlsx"),
                ("P_TM_BRJ7_B_07_4_32", "Std_02_ST_TO247.xlsx"),
                ("P_TM_BRJJ_B_09_4_32", "Std_07_ST_1200V.xlsx"),
                ("P_TM_BRF9_B_05_4_32", "Std_02b_ST_BRF9.xlsx"),
            })
            {
                var result = delivery.Fit(stem);
                Assert.Equal(table, result.TableFile, $"{stem} table");
                Assert.Equal(24, result.MatchedExact, $"{stem} exact matches");
                Assert.Equal(25, result.MatchedNormalised, $"{stem} normalised matches");
            }
        });

        // This is the finding the whole audit was built to produce, so it is pinned
        // rather than described: the same seven names are absent from every table.
        runner.Check("the seven `_R`/`HTGB` names are missing from every profiled recipe", () =>
        {
            string[] expected =
            [
                "HTGB", "HTRB6_R", "HTGB_HT6_R", "HTRB7_R", "HTGB_HT7_R", "HTRB8_R", "HTGB_HT8_R",
            ];

            foreach (var stem in new[]
            {
                "P_TM_BRI9_B_07_4_32", "P_TM_BRJ7_B_07_4_32",
                "P_TM_BRJJ_B_09_4_32", "P_TM_BRF9_B_05_4_32",
            })
            {
                var missing = delivery.Fit(stem).Steps
                    .Where(s => s.IsMissing)
                    .Select(s => s.Name)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();

                Assert.SequenceEqual(
                    expected.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                    missing,
                    $"{stem} missing step names");
            }
        });

        runner.Check("IGSS_20V_1 differs from IGSS_+20V_1 only by normalisation", () =>
        {
            var bridged = delivery.Fit("P_TM_BRI9_B_07_4_32").Steps
                .Where(s => s.TableName is not null && !s.NameExact && s.NameNormalised)
                .ToList();

            Assert.Equal(1, bridged.Count, "normalisation-only name differences");
            Assert.Equal("IGSS_20V_1", bridged[0].Name, "recipe name");
            Assert.Equal("IGSS_+20V_1", bridged[0].TableName!, "table name");
        });

        // The customer's rule: an SQ part is never handed a SiC part's limits. The
        // tables carry no product column, and every one of them shares the generic
        // pre-test and ramp names, so "most names in common" once chose a 650V SiC
        // table for all six SQ programs. A table must now cover every step.
        runner.Check("no recipe is offered a table that misses one of its steps", () =>
        {
            foreach (var recipe in delivery.All)
            {
                var suggestion = FitChecker.SuggestTable(recipe, delivery.AllTables());
                Assert.True(suggestion.Table is null || suggestion.Hits == suggestion.Steps,
                    $"{recipe.FileStem}: offered {suggestion.Table} at " +
                    $"{suggestion.Hits} of {suggestion.Steps} steps");
            }
        });

        runner.Check("SQ is offered no table at all, and the note says why", () =>
        {
            var sq = delivery.All
                .Where(r => r.FileStem.StartsWith("P_TM_SQ", StringComparison.Ordinal))
                .OrderBy(r => r.FileStem, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(6, sq.Count, "SQ recipe count");

            foreach (var recipe in sq)
            {
                // 15 steps for SQGW but 14 distinct names: it repeats IGSS_10V_1.
                var suggestion = FitChecker.SuggestTable(recipe, delivery.AllTables());
                Assert.True(suggestion.Table is null, $"{recipe.FileStem} was offered {suggestion.Table}");
                Assert.Equal(14, suggestion.Steps, $"{recipe.FileStem} distinct step names");
                Assert.Equal("Std_02b_ST_BRF9.xlsx", suggestion.Closest ?? "", $"{recipe.FileStem} closest table");
                Assert.Equal(9, suggestion.ClosestHits, $"{recipe.FileStem} closest hits");

                var notes = delivery.Fit(recipe.FileStem).Notes;
                Assert.True(notes.Any(n => n.Contains("not that one", StringComparison.Ordinal)),
                    string.Join(" | ", notes));
                Assert.True(!notes.Any(n => n.Contains("covers every step", StringComparison.Ordinal)),
                    string.Join(" | ", notes));
            }
        });
    }
}

using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Ingest;
using Htxb.RecipeCore.Model;
using Htxb.RecipeCore.Validate;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Tests;

/// <summary>
/// Checks for the three tools ported from <c>bidfgen.tools</c>: <c>std-align</c>,
/// <c>std-ingest</c> and <c>validate-lot</c>.
///
/// The parity baseline is the set of tables the Python tools produced, which are the
/// ones the site runs with. Every cell of all seventeen is compared here, because a
/// single limit or SW bin drifting is exactly what nobody would notice by eye.
///
/// Measured before the port, on the 261028 delivery: 7536 cells over 17 tables,
/// 43 differing, every one of them <c>V_gs</c> - see
/// <see cref="ShippedTablesDifferOnlyInGates"/> for why that is not a regression.
/// </summary>
internal static class ToolTests
{

    public static void Register(Runner runner, string samplesRoot, string zadzRoot, string tablesRoot)
    {
        Log.Section("tools: writer");
        RegisterWriter(runner);

        Log.Section("tools: ingest");
        RegisterIngest(runner, samplesRoot);

        Log.Section("tools: std-align");
        RegisterAligned(runner, samplesRoot, zadzRoot, tablesRoot);

        Log.Section("tools: validate-lot");
        RegisterValidate(runner, samplesRoot, tablesRoot);
    }

    // ------------------------------------------------------------------- ingest

    private static void RegisterIngest(Runner runner, string samplesRoot)
    {
        string? log = FindSlotLog(samplesRoot);
        if (log is null)
        {
            runner.Skip("the slot log names each step once", "no BRJJ slot log on this machine");
            return;
        }

        runner.Check("the slot log names each step once", () =>
        {
            // The Test Data block carries one row per measurement, so a step measured
            // twice appears twice. A limits table needs the plan, not the measurements
            // - the first version of this reader returned all 66 rows and every
            // --names-from pairing was refused as "the two are not the same plan".
            var names = SlotCsvReader.ReadStepNames(log);
            Assert.Equal(32, names.Count, "distinct step names");
            Assert.Equal(names.Distinct(StringComparer.Ordinal).Count(), names.Count, "duplicates");
            Assert.True(!names.Any(n => TestName.Normalise(n) == "rampdown"), "RAMP DOWN is not a step");
        });

        runner.Check("logged names are applied only when the plans really match", () =>
        {
            var names = SlotCsvReader.ReadStepNames(log);

            // Rename one row of a synthetic build so the guard has something to
            // refuse: HTRB6_R -> HTRB6 is the real re-issue, and HTGB1 is not.
            var build = new StdTableBuild(
                names.Select((n, i) => new object?[]
                {
                    (i + 1).ToString(), i + 1, string.Empty, n, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, 2, "196 = F_PRE_ON", "Yes", string.Empty,
                }).ToList(), []);

            StdTableBuild renamed = StdTableBuilder.ApplyLoggedNames(build, names);
            Assert.Equal(0, renamed.Renamed, "renamed with identical names");

            var shorter = names.Take(names.Count - 1).ToList();
            bool refused = false;
            try { StdTableBuilder.ApplyLoggedNames(build, shorter); }
            catch (InvalidDataException) { refused = true; }
            Assert.True(refused, "a shorter log must be refused, not paired");

            var swapped = names.ToList();
            swapped[0] = "SOMETHING ELSE";
            refused = false;
            try { StdTableBuilder.ApplyLoggedNames(build, swapped); }
            catch (InvalidDataException) { refused = true; }
            Assert.True(refused, "a row that is not the same step must be refused");
        });

        runner.Check("the re-issue decorations are the only thing ignored", () =>
        {
            Assert.Equal(TestName.Base("HTGB"), TestName.Base("HTGB1"), "HTGB == HTGB1");
            Assert.Equal(TestName.Base("HTRB6_R"), TestName.Base("HTRB6"), "HTRB6_R == HTRB6");
            Assert.Equal(TestName.Base("IGSS_20V_1"), TestName.Base("IGSS_+20V_1"), "IGSS_20V_1 == IGSS_+20V_1");
            Assert.True(TestName.Base("HTGB") != TestName.Base("HTRB6"), "HTGB != HTRB6");
        });
    }

    // ------------------------------------------------------------------- writer

    private static void RegisterWriter(Runner runner)
    {
        runner.Check("the writer round-trips every cell type", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"htxb-writer-{Guid.NewGuid():N}.xlsx");
            try
            {
                var rows = new List<IReadOnlyList<object?>>
                {
                    new object?[] { "text", 42, 17.5, string.Empty, "µA", "15sec" },
                    new object?[] { "a<b&c>d\"e", -8, 0, "0", null, "end" },
                };

                XlsxWriter.Write(path, "std_table", rows);

                using var workbook = XlsxWorkbook.Open(path);
                XlsxSheet sheet = workbook.GetSheet("std_table");

                Assert.Equal("text", sheet.TextOrEmpty(1, 1), "A1");
                Assert.Equal("42", sheet.TextOrEmpty(1, 2), "B1");
                Assert.Equal("17.5", sheet.TextOrEmpty(1, 3), "C1");
                Assert.Equal(string.Empty, sheet.TextOrEmpty(1, 4), "D1");
                Assert.Equal("µA", sheet.TextOrEmpty(1, 5), "E1");
                Assert.Equal("15sec", sheet.TextOrEmpty(1, 6), "F1");

                Assert.Equal("a<b&c>d\"e", sheet.TextOrEmpty(2, 1), "A2 escaped");
                Assert.Equal(-8, sheet.Number(2, 2) is { } n ? (int)n : 0, "B2");
                Assert.Equal(0, sheet.Number(2, 3) is { } z ? (int)z : -1, "C2");
                Assert.Equal("0", sheet.TextOrEmpty(2, 4), "D2");
                Assert.Equal("end", sheet.TextOrEmpty(2, 6), "F2");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });

        runner.Check("a written table reads back as a std table", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"htxb-table-{Guid.NewGuid():N}.xlsx");
            try
            {
                var build = new StdTableBuild(
                [
                    new object?[]
                    {
                        "7", 7, "16", "IDSS_650V_1", 300, 5, "650V", string.Empty, string.Empty, 8,
                        "µA", string.Empty, 2, "2C3 = F_IDSS_650V_1", "Yes", string.Empty,
                    },
                ], []);

                StdTableWriter.Write(path, build);
                StdTable table = StdTableAdapter.Read(path);

                Assert.Equal(1, table.Tests.Count, "rows");
                StdTest test = table.Tests[0];
                Assert.Equal("IDSS_650V_1", test.Name, "name");
                Assert.Equal("650V", test.Vds, "V_ds");
                Assert.Equal("8", test.Max, "max");
                Assert.Equal("µA", test.Units, "units");
                Assert.Equal("2", test.HwBin, "hw bin");
                Assert.Equal(0x2C3, test.SwBinCode ?? -1, "sw bin code");
                Assert.Equal("F_IDSS_650V_1", test.SwBinLegend, "sw bin legend");
                Assert.Equal(300, test.TimeSeconds is { } t ? (int)t : -1, "time");
                Assert.Equal(false, table.HasRelativeSectionColumn, "relative column");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });

        // The 17-column shape std-ingest writes - no shipped table carries it (Std_02b
        // was the last one and was withdrawn on 2026-11-01), so the reader's support for
        // the optional 'Test Sect ID Relative' column is proved here instead.
        runner.Check("the 17-column ingest shape round-trips", () =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"htxb-ingest-{Guid.NewGuid():N}.xlsx");
            try
            {
                var build = new StdTableBuild(
                [
                    new object?[]
                    {
                        "1", 1, "1000", "PRETEST CLOSED", 60, 1, "20V", "10V", 170, 225, "µA",
                        string.Empty, 3, "196 = F_PRE_ON", "Yes", "for MOSBTS", 0,
                    },
                ], []);

                StdTableWriter.Write(path, build, withRelativeColumn: true);
                StdTable table = StdTableAdapter.Read(path);

                Assert.Equal(17, table.Columns.Count, "columns");
                Assert.True(table.HasRelativeSectionColumn, "must report the relative column");
                Assert.Equal("0", table.Tests[0].SectionIdRelative, "relative section id");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });

        runner.Check("a limit in a unit the engine cannot read is converted, not copied", () =>
        {
            var step = new PlanStep
            {
                Row = 10,
                Ordinal = 1,
                Name = "IDSS_650V_2",
                Min = "1,5mA",
                Max = "500uA",
                Units = "µA",
            };

            var (low, high, unit) = StdTableBuilder.NormaliseLimits(step);
            Assert.Equal("µA", unit, "unit");
            Assert.Equal(1500.0, Convert.ToDouble(low), "min (µA)");
            Assert.Equal(500.0, Convert.ToDouble(high), "max (µA)");
        });

        runner.Check("a gate step keeps its nA limit unscaled", () =>
        {
            var step = new PlanStep
            {
                Row = 11,
                Ordinal = 1,
                Name = "IGSS_20V_1",
                Max = "80nA",
                Units = "nA",
            };

            var (low, high, unit) = StdTableBuilder.NormaliseLimits(step);
            Assert.Equal("nA", unit, "unit");
            Assert.True(low is null, "min should stay empty");
            Assert.Equal(80, (int)Convert.ToDouble(high), "max (nA)");
        });

        runner.Check("limits scale the way the engine writes them into the BiDF", () =>
        {
            // PRETEST CLOSED 170/225 uA is stored as 170000/225000 nA; a gate row is
            // not scaled; a missing minimum falls back to the engine's default.
            Assert.Equal((170000L, 225000L), StdLimits.For("PRETEST CLOSED", "170", "225"), "closed");
            Assert.Equal((-80L, 80L), StdLimits.For("IGSS_20V_1", "", "80"), "gate");
            Assert.Equal((-1000L, 8000L), StdLimits.For("IDSS_650V_1", "", "8"), "default lsl");
            Assert.Equal((0L, 0L), StdLimits.For("IDSS_Drift", "1", "2"), "drift");
        });
    }

    // ---------------------------------------------------------------- std-align

    private static void RegisterAligned(Runner runner, string samplesRoot, string zadzRoot, string tables)
    {
        if (!Directory.Exists(samplesRoot) || !Directory.Exists(zadzRoot))
        {
            runner.Skip("std-align parity", "delivery or machine recipes not found");
            return;
        }

                string? profileCsv = ProfileCsvAdapter.PathForTables(tables) is { } csv && File.Exists(csv) ? csv : null;
        if (profileCsv is null)
        {
            runner.Skip("std-align parity", $"profile CSV not found under {tables}");
            return;
        }

        var profiles = ProfileCsvAdapter.Read(profileCsv)
            .Where(p => p.Recipe.StartsWith("P_TM_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (ProfileRow profile in profiles)
        {
            string workbook = Directory
                .EnumerateFiles(samplesRoot, profile.Recipe + ".xlsx", SearchOption.AllDirectories)
                .FirstOrDefault() ?? string.Empty;
            string zadz = Path.Combine(zadzRoot, profile.Recipe + ".zadz");
            string shipped = Path.Combine(tables, profile.StdTable);

            if (workbook.Length == 0 || !File.Exists(zadz))
            {
                runner.Skip($"std-align {profile.StdTable}", "workbook or .zadz missing");
                continue;
            }

            if (!File.Exists(shipped))
            {
                runner.Skip($"std-align {profile.StdTable}", $"shipped table not found: {shipped}");
                continue;
            }

            runner.Check($"std-align {profile.StdTable} matches the shipped table", () =>
            {
                Recipe recipe = PlanSheetAdapter.Read(workbook);
                ZadzRecipe machine = ZadzAdapter.Read(zadz);
                StdTableBuild build = StdTableBuilder.BuildAligned(recipe, machine);

                string path = Path.Combine(Path.GetTempPath(), $"htxb-{Guid.NewGuid():N}.xlsx");
                try
                {
                    StdTableWriter.Write(path, build);
                    StdTable generated = StdTableAdapter.Read(path);
                    StdTable expected = StdTableAdapter.Read(shipped);

                    Assert.Equal(expected.Tests.Count, generated.Tests.Count, "steps");
                    StandardFieldsEqual(expected, generated);
                    ShippedTablesDifferOnlyInGates(expected, generated);
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            });
        }
    }

    /// <summary>Everything the engine reads - name, limits, bins, timing, exclusion.</summary>
    private static void StandardFieldsEqual(StdTable expected, StdTable generated)
    {
        for (int i = 0; i < expected.Tests.Count; i++)
        {
            StdTest a = expected.Tests[i];
            StdTest b = generated.Tests[i];
            string at = $"step {i + 1} ({a.Name})";

            Assert.Equal(a.Name, b.Name, at + " name");
            Assert.Equal(a.SectionId, b.SectionId, at + " section");
            Assert.Equal(a.TestIndex, b.TestIndex, at + " index");
            Assert.Equal(a.Marker, b.Marker, at + " marker");
            Assert.Equal(a.Vds, b.Vds, at + " V_ds");
            Assert.Equal(a.Min, b.Min, at + " min");
            Assert.Equal(a.Max, b.Max, at + " max");
            Assert.Equal(a.Units, b.Units, at + " units");
            Assert.Equal(a.HwBin, b.HwBin, at + " hw bin");
            Assert.Equal(a.SwBinCode, b.SwBinCode, at + " sw bin code");
            Assert.Equal(a.SwBinLegend, b.SwBinLegend, at + " sw bin legend");
            Assert.Equal(a.ExcludeWhenFail, b.ExcludeWhenFail, at + " exclude");
            Assert.Equal(a.Remarks, b.Remarks, at + " remarks");
            Assert.Equal(a.TimeSeconds, b.TimeSeconds, at + " time");
            Assert.Equal(a.ProgramMinutes, b.ProgramMinutes, at + " duration");
        }
    }

    /// <summary>
    /// The one deliberate difference: <c>V_gs</c>.
    ///
    /// <c>bidfgen.tools.std_ingest</c> wrote <c>str(cell("vgs") or "").strip()</c>, and
    /// in Python the number 0 is falsy - so a step that applies exactly 0 V to the
    /// gate lost its value and the shipped table shows a blank. The port keeps the
    /// "0", because it is what the customer wrote and nothing downstream invents it
    /// back. Asserted here, narrowly, so the difference can never quietly grow into
    /// a real one.
    /// </summary>
    private static void ShippedTablesDifferOnlyInGates(StdTable expected, StdTable generated)
    {
        for (int i = 0; i < expected.Tests.Count; i++)
        {
            StdTest a = expected.Tests[i];
            StdTest b = generated.Tests[i];

            if (a.Vgs == b.Vgs) continue;

            Assert.True(a.Vgs.Length == 0 && b.Vgs == "0",
                $"step {i + 1} ({a.Name}): V_gs is '{b.Vgs}' but the shipped table says '{a.Vgs}'");
        }
    }

    // ------------------------------------------------------------- validate-lot

    /// <summary>
    /// The processed lots of the post-change run. They sit beside the delivery
    /// folder, not inside it, so the search covers the parent as well.
    /// </summary>
    private static string? FindPostRun(string samplesRoot) =>
        SearchBeside(samplesRoot, "2108 post log files");

    /// <summary>A BRJJ slot log from the processed run, used to prove name handling.</summary>
    private static string? FindSlotLog(string samplesRoot)
    {
        string? run = FindPostRun(samplesRoot);
        if (run is null) return null;

        return Directory.EnumerateFiles(run, "*_SLOT1_*.csv", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).StartsWith("P_TM_BRJJ_B_09_4_32", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>A directory that may sit inside the delivery folder or beside it.</summary>
    private static string? SearchBeside(string samplesRoot, string name)
    {
        foreach (string? candidate in new[]
                 {
                     samplesRoot,
                     Directory.Exists(samplesRoot) ? Directory.GetParent(samplesRoot)?.FullName : null,
                 })
        {
            if (candidate is null || !Directory.Exists(candidate)) continue;
            string? hit = Directory
                .EnumerateDirectories(candidate, name, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }

    private static void RegisterValidate(Runner runner, string samplesRoot, string tables)
    {
                string? profileCsv = ProfileCsvAdapter.PathForTables(tables) is { } csv && File.Exists(csv) ? csv : null;

        var post = FindPostRun(samplesRoot);

        if (post is null || profileCsv is null)
        {
            runner.Skip("validate-lot", "the post-run lots or the profile CSV are not on this machine");
            return;
        }

        var lots = Directory.EnumerateDirectories(post)
            .Where(d => !d.EndsWith("_CSV", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        runner.Check($"validate-lot finds one BiDF per slot ({lots.Count} lot(s))", () =>
        {
            Assert.True(lots.Count > 0, "no lots to check");
            foreach (string lot in lots)
            {
                var bidfs = LotValidator.FindBidfs(lot);
                Assert.True(bidfs.Count > 0, $"{Path.GetFileName(lot)}: no .bidf");

                var slots = bidfs.Select(LotValidator.SlotOf).ToList();
                Assert.Equal(slots.Distinct().Count(), slots.Count, $"{Path.GetFileName(lot)}: duplicate slot id");
                Assert.True(slots.All(s => s.Length > 0), $"{Path.GetFileName(lot)}: unreadable slot id");
            }
        });

        foreach (string lot in lots)
        {
            string name = Path.GetFileName(lot);
            runner.Check($"validate-lot {name}", () =>
            {
                var bidfs = LotValidator.FindBidfs(lot);
                using var first = new Htxb.RecipeCore.Bidf.BidfReader(bidfs[0]);
                string recipe = first.Scalar("select programname from tbl_run") ?? string.Empty;

                ProfileRow? profile = ProfileCsvAdapter.Find(ProfileCsvAdapter.Read(profileCsv), recipe);
                Assert.True(profile is not null, $"no profile for {recipe}");

                string table = Path.Combine(tables, profile!.StdTable);
                Assert.True(File.Exists(table), $"table not found: {table}");

                LotReport report = LotValidator.Validate(lot, table, profile);
                var failures = report.Checks.Where(c => !c.Passed)
                    .Concat(report.Slots.SelectMany(s => s.Checks.Where(c => !c.Passed)))
                    .Select(c => c.Name + (c.Detail.Length > 0 ? " - " + c.Detail : string.Empty))
                    .Take(6)
                    .ToList();

                Assert.True(report.Ok, string.Join("; ", failures));
                Assert.True(report.Slots.Count > 0, "no slots");
            });
        }
    }
}

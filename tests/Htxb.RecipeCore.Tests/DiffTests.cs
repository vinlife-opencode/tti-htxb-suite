using System.Text.Json;
using Htxb.RecipeCore.Diff;

namespace Htxb.RecipeCore.Tests;

/// <summary>
/// Checks for <c>recipe-diff</c>: the pairing rules, then the whole 261028
/// delivery against its machine recipe export.
///
/// The baseline below was measured with the Python tool before the port and is
/// asserted per recipe, not in total: a single recipe drifting is the thing worth
/// catching, and it is what the two implementations must agree on.
/// </summary>
internal static class DiffTests
{
    private sealed record Expectation(
        string Recipe, int WorkbookSteps, int MachineSteps, int Exact, int Alias, string[] MachineOnly);

    private static readonly Expectation[] Expected =
    [
        new("P_TM_BR9M_B_05_4_32", 32, 32, 25, 7, []),
        new("P_TM_BRF5_B_07_4_32", 32, 34, 25, 7, ["IGSS_-10V_1", "IGSS_-10V_3"]),
        new("P_TM_BRF9_B_05_4_32", 32, 34, 25, 7, ["IGSS_-10V_1", "IGSS_-10V_3"]),
        new("P_TM_BRFC_B_05_4_32", 32, 34, 25, 7, ["IGSS_-10V_1", "IGSS_-10V_3"]),
        new("P_TM_BRI9_B_07_4_32", 32, 34, 25, 7, ["IGSS_-10V_1", "IGSS_-10V_3"]),
        new("P_TM_BRIC_B_05_4_32", 32, 32, 25, 7, []),
        new("P_TM_BRIG_B_04_4_32", 32, 32, 25, 7, []),
        new("P_TM_BRJ7_B_07_4_32", 32, 34, 25, 7, ["IGSS_-10V_1", "IGSS_-10V_3"]),
        new("P_TM_BRJJ_B_09_4_32", 32, 32, 25, 7, []),
        new("P_TM_BRJO_B_06_4_32", 32, 32, 25, 7, []),
        new("P_TM_SQFJ_B_03_4_5", 15, 16, 14, 1, ["RAMP_DOWN"]),
        new("P_TM_SQGW_B_02_4_5", 15, 16, 14, 1, ["RAMP_DOWN"]),
        new("P_TM_SQJJ_B_03_4_5", 15, 16, 14, 1, ["RAMP_DOWN"]),
        new("P_TM_SQK9_B_03_4_5", 15, 16, 14, 1, ["RAMP_DOWN"]),
        new("P_TM_SQKC_B_03_4_5", 15, 16, 14, 1, ["RAMP_DOWN"]),
        new("P_TM_SQKW_B_03_4_5", 15, 16, 14, 1, ["RAMP_DOWN"]),
    ];

    public static void Register(Runner runner, string samplesRoot, string zadzRoot, string aliasesPath)
    {
        Log.Section("recipe diff");

        runner.Check("the shared alias file matches the tool", () =>
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(aliasesPath));
            var shared = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty pair in document.RootElement.GetProperty("aliases").EnumerateObject())
                shared[pair.Name] = pair.Value.GetString() ?? string.Empty;

            Assert.Equal(shared.Count, RecipeAligner.DefaultAliases.Count, "alias count");
            foreach ((string key, string value) in shared)
            {
                Assert.True(RecipeAligner.DefaultAliases.TryGetValue(key, out string? mine),
                    $"alias '{key}' is missing from the tool");
                Assert.Equal(value, mine, $"alias '{key}'");
            }
        });

        runner.Check("a renamed step is paired through the alias map", () =>
        {
            var alignment = RecipeAligner.Align(
                ["htgb", "htrb6r", "idss930v"], ["htgb1", "htrb6", "idss930v"]);
            Assert.Equal(3, alignment.Pairs.Count, "pairs");
            Assert.True(alignment.Pairs.Any(p => p.How == "alias"), "no alias pair");
            Assert.Equal(0, alignment.WorkbookOnly.Count, "workbook only");
            Assert.Equal(0, alignment.MachineOnly.Count, "machine only");
        });

        runner.Check("numbered variants pair in order, not through one key", () =>
        {
            var alignment = RecipeAligner.Align(["igss20v", "igss20v", "igss20v"],
                                                ["igss20v", "igss20v", "igss20v"]);
            Assert.SequenceEqual([(0, 0), (1, 1), (2, 2)],
                alignment.Pairs.Select(p => (p.Workbook, p.Machine)).ToList(), "pairs");
        });

        runner.Check("a step only the machine has is reported", () =>
        {
            var alignment = RecipeAligner.Align(
                ["pretestclosed", "igss20v"], ["pretestclosed", "igss10v", "igss20v"]);
            Assert.SequenceEqual([1], alignment.MachineOnly, "machine only");
            Assert.Equal(0, alignment.WorkbookOnly.Count, "workbook only");
        });

        runner.Check("a step only the workbook has is reported", () =>
        {
            var alignment = RecipeAligner.Align(
                ["pretestclosed", "igss20v"], ["pretestclosed"]);
            Assert.SequenceEqual([1], alignment.WorkbookOnly, "workbook only");
            Assert.Equal(0, alignment.MachineOnly.Count, "machine only");
        });

        runner.Check("the second SQ ramp spelling maps to the same machine step", () =>
        {
            var alignment = RecipeAligner.Align(["htgbrampup"], ["htgbramp", "rampdown"]);
            Assert.Equal(1, alignment.Pairs.Count, "pairs");
            Assert.Equal("alias", alignment.Pairs[0].How, "how");
            Assert.SequenceEqual([1], alignment.MachineOnly, "machine only");
        });

        // -- the delivery ----------------------------------------------------
        if (!Directory.Exists(zadzRoot))
        {
            runner.Skip("recipe-diff delivery", $"machine recipes not found: {zadzRoot}");
            return;
        }

        if (!Directory.Exists(samplesRoot))
        {
            runner.Skip("recipe-diff delivery", $"delivery not found: {samplesRoot}");
            return;
        }

        foreach (Expectation expected in Expected)
        {
            string workbook = Directory
                .EnumerateFiles(samplesRoot, expected.Recipe + ".xlsx", SearchOption.AllDirectories)
                .FirstOrDefault() ?? string.Empty;
            string zadz = Path.Combine(zadzRoot, expected.Recipe + ".zadz");

            if (workbook.Length == 0 || !File.Exists(zadz))
            {
                runner.Skip($"diff {expected.Recipe}", "workbook or .zadz missing");
                continue;
            }

            runner.Check($"diff {expected.Recipe}", () =>
            {
                DiffResult result = RecipeDiffer.Compare(workbook, zadz);
                Assert.Equal(expected.WorkbookSteps, result.WorkbookSteps, "workbook steps");
                Assert.Equal(expected.MachineSteps, result.MachineSteps, "machine steps");
                Assert.Equal(expected.Exact, result.Pairs.Count(p => p.How == "exact"), "exact pairs");
                Assert.Equal(expected.Alias, result.Pairs.Count(p => p.How == "alias"), "alias pairs");
                Assert.SequenceEqual(expected.MachineOnly, result.MachineOnly, "machine only");
                Assert.Equal(0, result.WorkbookOnly.Count, "workbook only");
                Assert.Equal(0, result.ValueDiffs.Count, "value differences");
            });
        }
    }
}

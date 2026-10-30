using Htxb.RecipeCore.Fit;
using Htxb.RecipeCore.Model;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Tests;

/// <summary>Checks that need no files: the rules, stated once and asserted.</summary>
internal static class UnitTests
{
    public static void Register(Runner runner, VectorFile vectors)
    {
        Log.Section("shared vectors");

        runner.Check($"normalise: {vectors.Normalise!.Count} vectors from the Python implementation", () =>
        {
            foreach (var vector in vectors.Normalise!)
            {
                Assert.Equal(vector.Expected ?? string.Empty, TestName.Normalise(vector.Input),
                    $"normalise({Show(vector.Input)})");
            }
        });

        if (vectors.SwBin is { Count: > 0 } swBin)
        {
            runner.Check($"swbin: {swBin.Count} vectors, including the workbook apostrophe forms", () =>
            {
                foreach (var vector in swBin)
                {
                    var (hex, legend) = TestName.SplitSwBin(vector.Input);
                    Assert.Equal(vector.Legend ?? string.Empty, legend, $"legend of {Show(vector.Input)}");
                    Assert.Equal(vector.Hex, TestName.SwBinCode(vector.Input), $"code of {Show(vector.Input)}");

                    // The written form is what std_ingest puts in the table; reading it
                    // back must produce the same number, or the round trip is broken.
                    if (!string.IsNullOrEmpty(vector.Written))
                    {
                        Assert.Equal(vector.Hex, TestName.SwBinCode(vector.Written),
                            $"round trip code of {Show(vector.Written)}");
                        Assert.Equal(vector.Legend ?? string.Empty, TestName.SplitSwBin(vector.Written).LegendName,
                            $"round trip legend of {Show(vector.Written)}");
                    }

                    // The hex token must never carry punctuation into a comparison.
                    Assert.True(hex.All(char.IsLetterOrDigit), $"code token of {Show(vector.Input)}: '{hex}'");
                }
            });
        }
        else
        {
            runner.Skip("swbin vectors", "the vector file carries no 'swbin' section");
        }

        Log.Section("name parsing");

        runner.Check("SplitSwBin reads the three measured spellings to one code", () =>
        {
            // workbook, workbook with a leading apostrophe, std_table generated form
            foreach (string raw in new[] { "196'=F_PRE_ON", "'196 = F_PRE_ON", "  '196'=F_PRE_ON  ", "196 = F_PRE_ON" })
            {
                Assert.Equal(0x196, TestName.SwBinCode(raw), $"code of {Show(raw)}");
                Assert.Equal("F_PRE_ON", TestName.SplitSwBin(raw).LegendName, $"legend of {Show(raw)}");
            }
        });

        runner.Check("a free-text bin yields no code rather than zero", () =>
        {
            Assert.Equal(null, TestName.SwBinCode("n/a"), "code of 'n/a'");
            Assert.Equal(null, TestName.SwBinCode(""), "code of empty");
            Assert.Equal(null, TestName.SwBinCode(null), "code of null");
            Assert.Equal("", TestName.SplitSwBin("").Hex, "hex of empty");
        });

        runner.Check("ParseDurationSeconds handles the SQ text durations", () =>
        {
            var cases = new (string? Text, double? Seconds)[]
            {
                ("300", 300),
                ("0", 0),
                ("15sec", 15),
                ("5s", 5),
                ("10 min", 600),
                ("1.5h", 5400),
                ("-", null),
                ("", null),
                (null, null),
                ("abc", null),
                ("12fortnight", null),
            };

            foreach (var (text, seconds) in cases)
            {
                Assert.Equal(seconds, TestName.ParseDurationSeconds(text), $"duration of {Show(text)}");
            }
        });

        runner.Check("VoltageValue reads the written voltages", () =>
        {
            Assert.Equal(1200d, TestName.VoltageValue("1200V"), "1200V");
            Assert.Equal(32d, TestName.VoltageValue("+32V"), "+32V");
            Assert.Equal(-21d, TestName.VoltageValue("-21V"), "-21V");
            Assert.Equal(0d, TestName.VoltageValue("0V"), "0V");
            Assert.Equal(690d, TestName.VoltageValue("690v"), "690v");
            Assert.Equal(null, TestName.VoltageValue(""), "empty");
            Assert.Equal(null, TestName.VoltageValue("abc"), "abc");
        });

        Log.Section("cell references");

        runner.Check("column letters and numbers round-trip", () =>
        {
            Assert.Equal(1, CellRef.ColumnIndex("A"), "A");
            Assert.Equal(26, CellRef.ColumnIndex("Z"), "Z");
            Assert.Equal(27, CellRef.ColumnIndex("AA"), "AA");
            Assert.Equal(30, CellRef.ColumnIndex("AD"), "AD");
            Assert.Equal(16384, CellRef.ColumnIndex("XFD"), "XFD");

            foreach (int column in new[] { 1, 26, 27, 30, 52, 703, 16384 })
                Assert.Equal(column, CellRef.ColumnIndex(CellRef.ColumnName(column)), $"round trip {column}");
        });

        runner.Check("A1 references parse to (row, column)", () =>
        {
            Assert.Equal((2, 30), CellRef.Parse("AD2"), "AD2");
            Assert.Equal((81, 1), CellRef.Parse("A81"), "A81");
            Assert.Equal((1, 18), CellRef.Parse("R1"), "R1");
            Assert.Equal("AD2", CellRef.Format(2, 30), "format(2,30)");
        });

        runner.Check("merged ranges parse and contain correctly", () =>
        {
            var range = XlsxRange.Parse("E69:F70");
            Assert.True(range.Contains(69, 5), "E69 inside");
            Assert.True(range.Contains(70, 6), "F70 inside");
            Assert.True(!range.Contains(71, 5), "row 71 outside");
            Assert.True(!range.Contains(69, 4), "column D outside");

            var single = XlsxRange.Parse("A5");
            Assert.Equal((5, 1, 5, 1), (single.FromRow, single.FromColumn, single.ToRow, single.ToColumn), "single cell");
        });

        Log.Section("step alignment");

        runner.Check("LCS alignment keeps identical sequences positional", () =>
        {
            var pairs = FitChecker.Align(["a", "b", "c"], ["a", "b", "c"]);
            Assert.SequenceEqual([(1, 1), (2, 2), (3, 3)], Normalise(pairs), "aligned pairs");
        });

        runner.Check("LCS alignment marks an inserted step instead of shifting everything", () =>
        {
            var pairs = FitChecker.Align(["a", "x", "b"], ["a", "b"]);
            Assert.SequenceEqual([(1, 1), (2, 0), (3, 2)], Normalise(pairs), "recipe-side insertion");

            var added = FitChecker.Align(["a", "b"], ["a", "x", "b"]);
            Assert.SequenceEqual([(1, 1), (0, 2), (2, 3)], Normalise(added), "table-side insertion");
        });

        runner.Check("LCS alignment handles empty sides", () =>
        {
            Assert.SequenceEqual([(1, 0), (2, 0)], Normalise(FitChecker.Align(["a", "b"], [])), "empty table");
            Assert.SequenceEqual([(0, 1), (0, 2)], Normalise(FitChecker.Align([], ["a", "b"])), "empty recipe");
            Assert.Equal(0, FitChecker.Align([], []).Count, "both empty");
        });

        runner.Check("a normalised pair is aligned, and reported as a name difference", () =>
        {
            var pairs = FitChecker.Align(
                [TestName.Normalise("IGSS_20V_1")],
                [TestName.Normalise("IGSS_+20V_1")]);
            Assert.SequenceEqual([(1, 1)], Normalise(pairs), "normalised pair");
        });
    }

    /// <summary>(recipe, table) pairs as 1-based "found" flags, so a failure reads clearly.</summary>
    private static List<(int, int)> Normalise(List<(int? Recipe, int? Table)> pairs) =>
        pairs.Select(p => (p.Recipe is null ? 0 : p.Recipe.Value + 1,
                           p.Table is null ? 0 : p.Table.Value + 1)).ToList();

    private static string Show(string? text) => text is null ? "<null>" : "\"" + text + "\"";
}

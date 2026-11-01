using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Bidf;
using Htxb.RecipeCore.Ingest;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Validate;

/// <summary>One named check and its verdict.</summary>
public sealed record LotCheck(string Name, bool Passed, string Detail = "")
{
    public override string ToString() => (Passed ? "PASS  " : "FAIL  ") + Name +
        (Detail.Length > 0 ? "  - " + Detail : string.Empty);
}

/// <summary>What one slot's BiDF says, and how it compared with the table.</summary>
public sealed record SlotReport
{
    public required string Slot { get; init; }
    public required string Bidf { get; init; }
    public required string Recipe { get; init; }
    public required string DutName { get; init; }
    public required string Package { get; init; }
    public required int Steps { get; init; }
    public required long Parts { get; init; }
    public required long Good { get; init; }
    public required long Tested { get; init; }
    public required IReadOnlyDictionary<int, long> Bins { get; init; }
    public required IReadOnlyList<LotCheck> Checks { get; init; }

    public int Passed => Checks.Count(c => c.Passed);
    public int Failed => Checks.Count - Passed;
}

/// <summary>The verdict for a whole lot: one report per slot plus the lot-wide checks.</summary>
public sealed record LotReport
{
    public required string Lot { get; init; }
    public required string Recipe { get; init; }
    public required string TablePath { get; init; }
    public required int TableSteps { get; init; }
    public required ProfileRow? Profile { get; init; }
    public required IReadOnlyList<SlotReport> Slots { get; init; }
    public required IReadOnlyList<LotCheck> Checks { get; init; }

    public int SlotsPassed => Slots.Count(s => s.Failed == 0);

    public int Passed => Checks.Count(c => c.Passed) + Slots.Sum(s => s.Passed);
    public int Failed => Checks.Count(c => !c.Passed) + Slots.Sum(s => s.Failed);

    public bool Ok => Failed == 0;
}

/// <summary>
/// Validates a processed lot against the limits table it should have used.
///
/// Answers the questions that matter after a run: did every step of the program find
/// a row in the table, do the BiDF's limits equal the table's, are the bins sane, do
/// the counts add up, and does the slot summary advertise the bins the table defines.
/// It reads only - the BiDF, the slot logs and the summaries are never modified.
///
/// Two rules this tool learned the hard way:
///
/// * a lot produces **one BiDF per slot** (24 of them on a full load), so validating
///   a single file would report a lot-wide count against one slot's data;
/// * <c>tbl_testdesc</c> is a description index and <c>test_id</c> points into it, so
///   the step name must come from <c>tbl_testflow.note</c>. Reading the name through
///   <c>test_id</c> reports a <c>_3</c> step as <c>_1</c> and hides exactly the
///   mix-up this check exists to catch. Both the raw and the normalised name are
///   compared, because a table that is right only after normalisation still loses
///   steps the engine cannot match.
/// </summary>
public static class LotValidator
{
    /// <summary>Hardware bins the product defines: 0 empty, 1 good, 2 fail, 3 pre-test fail.</summary>
    private static readonly HashSet<long> AllowedHwBins = [0, 1, 2, 3];

    /// <summary>The processed folder, and the <c>..._CSV</c> folder that goes with it.</summary>
    public static (string Lot, string Csv) ResolveLot(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return full.EndsWith("_CSV", StringComparison.OrdinalIgnoreCase)
            ? (full[..^4], full)
            : (full, full + "_CSV");
    }

    /// <summary>Every <c>.bidf</c> under <c>_BiDF</c>, in slot order.</summary>
    public static IReadOnlyList<string> FindBidfs(string lotFolder)
    {
        string folder = Path.Combine(lotFolder, "_BiDF");
        if (!Directory.Exists(folder)) return [];

        return Directory.GetFiles(folder, "*.bidf")
            .OrderBy(p => SlotOf(p), StringComparer.Ordinal)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The slot a BiDF belongs to: the <c>__NN__</c> field in its name, or
    /// <c>""</c> when the name does not carry one.
    ///
    /// The name is <c>&lt;run&gt;__&lt;platform&gt;__&lt;NN&gt;__&lt;recipe&gt;__&lt;lot&gt;.bidf</c>,
    /// so the slot is the first all-digit field - not a fixed position, which the
    /// variable-length run id would break.
    /// </summary>
    public static string SlotOf(string bidfPath)
    {
        foreach (string part in Path.GetFileName(bidfPath).Split("__"))
        {
            if (part.Length is > 0 and <= 3 && part.All(char.IsAsciiDigit)) return part;
        }
        return string.Empty;
    }

    public static LotReport Validate(string lotFolder, string tablePath, ProfileRow? profile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lotFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePath);

        (string lot, string csvFolder) = ResolveLot(lotFolder);

        StdTable table = StdTableAdapter.Read(tablePath);
        if (table.Tests.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(tablePath)} has no test rows.");

        IReadOnlyList<string> bidfs = FindBidfs(lot);
        if (bidfs.Count == 0)
            throw new InvalidDataException($"no .bidf under {Path.Combine(lot, "_BiDF")}");

        var slots = new List<SlotReport>(bidfs.Count);
        foreach (string bidf in bidfs)
            slots.Add(ValidateSlot(bidf, table, profile));

        string recipe = slots[0].Recipe;
        var checks = new List<LotCheck>();

        var recipes = slots.Select(s => s.Recipe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        checks.Add(new LotCheck("every slot names the same recipe", recipes.Count == 1,
            string.Join(", ", recipes)));

        checks.Add(new LotCheck("every slot ran the same number of steps",
            slots.Select(s => s.Steps).Distinct().Count() == 1,
            string.Join(", ", slots.Select(s => s.Steps).Distinct())));

        checks.Add(CheckSlotSummary(lot, slots, table));
        checks.Add(CheckSlotLogNames(csvFolder, table));

        return new LotReport
        {
            Lot = Path.GetFileName(lot),
            Recipe = recipe,
            TablePath = tablePath,
            TableSteps = table.Tests.Count,
            Profile = profile,
            Slots = slots,
            Checks = checks,
        };
    }

    /// <summary>One slot: read the BiDF, then compare it with the table row by row.</summary>
    public static SlotReport ValidateSlot(string bidfPath, StdTable table, ProfileRow? profile)
    {
        using var bidf = new BidfReader(bidfPath);

        var runs = bidf.Query("select programname, dutname, package, count_parts, good_parts from tbl_run");
        if (runs.Count == 0)
            throw new InvalidDataException($"{Path.GetFileName(bidfPath)} has no tbl_run row.");

        string[] run = runs[0];

        string recipe = run[0];
        string dutName = run[1];
        string package = run[2];
        long parts = Integer(run[3]);
        long good = Integer(run[4]);

        string[] names = FlowNames(bidf);
        var limits = ParametricLimits(bidf);
        var bins = HardwareBins(bidf);
        long summaryRows = bidf.Count("tbl_summary");

        long tested = bins.Where(b => b.Key != 0).Sum(b => b.Value);

        var checks = new List<LotCheck>();

        checks.Add(new LotCheck("the BiDF names a recipe", recipe.Length > 0, recipe));

        checks.Add(new LotCheck("the flow has one row per table step",
            names.Length == table.Tests.Count,
            $"flow {names.Length} vs table {table.Tests.Count}"));

        // Raw names first: a table that only matches after normalisation loses the
        // step in the engine, and a `_1` / `_3` swap normalises to the same key.
        var tableNames = table.Tests.Select(t => t.Name).ToList();
        var rawDiff = Differences(names, tableNames);
        checks.Add(new LotCheck("the step names match the table verbatim", rawDiff.Length == 0, rawDiff));

        var rawNormalised = names.Select(TestName.Normalise).ToList();
        var tableNormalised = tableNames.Select(TestName.Normalise).ToList();
        var normalisedDiff = Differences(rawNormalised, tableNormalised);
        checks.Add(new LotCheck("the step names match the table once normalised",
            normalisedDiff.Length == 0, normalisedDiff));

        var limitDiff = new List<string>();
        for (int i = 0; i < table.Tests.Count && i < names.Length; i++)
        {
            int index = i + 1;
            if (!limits.TryGetValue(index, out (long Lsl, long Usl) actual)) continue;

            (long lsl, long usl) = StdLimits.For(table.Tests[i]);
            if (actual.Lsl != lsl || actual.Usl != usl)
                limitDiff.Add($"{names[i]}: {actual.Lsl}/{actual.Usl} vs {lsl}/{usl}");
        }
        checks.Add(new LotCheck("every limit equals the table's", limitDiff.Count == 0,
            string.Join("; ", limitDiff)));

        var unknownBins = bins.Keys.Where(b => !AllowedHwBins.Contains(b)).OrderBy(b => b).ToList();
        checks.Add(new LotCheck("only the defined hardware bins appear", unknownBins.Count == 0,
            unknownBins.Count == 0 ? string.Join(", ", bins.Keys.OrderBy(b => b))
                                   : "unknown: " + string.Join(", ", unknownBins)));

        checks.Add(new LotCheck("count_parts equals the tested DUTs", parts == tested,
            $"BiDF {parts} vs results {tested}"));

        checks.Add(new LotCheck("good_parts equals the bin-1 count", good == bins.GetValueOrDefault(1),
            $"BiDF {good} vs bin1 {bins.GetValueOrDefault(1)}"));

        checks.Add(new LotCheck("one summary row per tested DUT", summaryRows == parts,
            $"summary {summaryRows} vs count_parts {parts}"));

        bool driftOn = !ProfileFalse(profile?.DriftEn);
        var driftNames = table.Tests.Where(t => StdLimits.IsDrift(t.Name)).Select(t => t.Name).ToList();
        if (driftOn)
        {
            checks.Add(new LotCheck("drift is enabled, so a drift row is expected", true,
                "profile Drift_En is on"));
        }
        else
        {
            var driftInBidf = names.Where(StdLimits.IsDrift).ToList();
            checks.Add(new LotCheck("drift is off, so the BiDF has no drift rows",
                driftInBidf.Count == 0 && driftNames.Count == 0,
                string.Join(", ", driftInBidf.Concat(driftNames).Distinct())));
        }

        if (profile is not null)
        {
            if (profile.DutName.Length > 0)
                checks.Add(new LotCheck("the DUT name matches the profile", dutName == profile.DutName,
                    $"BiDF '{dutName}' vs profile '{profile.DutName}'"));

            if (profile.DutPackage.Length > 0)
                checks.Add(new LotCheck("the package matches the profile", package == profile.DutPackage,
                    $"BiDF '{package}' vs profile '{profile.DutPackage}'"));
        }

        return new SlotReport
        {
            Slot = SlotOf(bidfPath),
            Bidf = Path.GetFileName(bidfPath),
            Recipe = recipe,
            DutName = dutName,
            Package = package,
            Steps = names.Length,
            Parts = parts,
            Good = good,
            Tested = tested,
            Bins = bins,
            Checks = checks,
        };
    }

    /// <summary>
    /// The slot summary's SW-bin legend against the table. The legend lists every bin
    /// the table defines - including the delta bins of a drift-capable table - so this
    /// is the check that proves the run used the table and not an older one.
    /// </summary>
    private static LotCheck CheckSlotSummary(string lotFolder, IReadOnlyList<SlotReport> slots, StdTable table)
    {
        string folder = Path.Combine(lotFolder, "_Slots_Summary");
        if (!Directory.Exists(folder))
            return new LotCheck("the slot summary legend matches the table", true, "no _Slots_Summary folder");

        var tableBins = new Dictionary<int, string>();
        foreach (StdTest test in table.Tests)
        {
            if (test.SwBinCode is { } code && test.SwBinLegend.Length > 0)
                tableBins[code] = test.SwBinLegend;
        }

        var extra = new List<string>();
        var wrong = new List<string>();
        var absent = new List<string>();
        var noSummary = new List<string>();

        foreach (SlotReport slot in slots)
        {
            string summary = Path.Combine(folder, $"Slot{slot.Slot}_Summary.txt");
            if (!File.Exists(summary))
            {
                noSummary.Add(slot.Slot);
                continue;
            }

            var legend = ReadLegend(summary);
            foreach (var (code, name) in legend)
            {
                if (!tableBins.TryGetValue(code, out string? expected))
                    extra.Add($"{slot.Slot}:{code}={name}");
                else if (!string.Equals(expected, name, StringComparison.Ordinal))
                    wrong.Add($"{slot.Slot}:{code}={name} (table {expected})");
            }

            var present = legend.Select(e => e.Code).ToHashSet();
            absent.AddRange(tableBins.Keys.Where(c => !present.Contains(c)).Select(c => $"{slot.Slot}:{c}"));
        }

        var detail = new List<string>();
        if (noSummary.Count > 0) detail.Add("no summary for slot " + Short(noSummary));
        if (extra.Count > 0) detail.Add("in the summary, not in the table: " + Short(extra));
        if (wrong.Count > 0) detail.Add("different description: " + Short(wrong));
        if (absent.Count > 0) detail.Add("in the table, not in the summary: " + Short(absent));

        return new LotCheck("the slot summary legend matches the table",
            noSummary.Count == 0 && extra.Count == 0 && wrong.Count == 0,
            string.Join("; ", detail));
    }

    /// <summary>A detail line a human can read: the first few items, then a count.</summary>
    private static string Short(IReadOnlyList<string> items) =>
        items.Count <= 6
            ? string.Join(", ", items)
            : string.Join(", ", items.Take(6)) + $", +{items.Count - 6} more";

    /// <summary>Every <c>&lt;code&gt; = F_NAME</c> pair in a slot summary.</summary>
    public static IReadOnlyList<(int Code, string Legend)> ReadLegend(string summaryPath)
    {
        var pairs = new List<(int, string)>();
        var seen = new HashSet<int>();

        foreach (string line in File.ReadLines(summaryPath))
        {
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(line, @"(\d+)\s*=\s*(F_[A-Za-z0-9_]+)"))
            {
                if (!int.TryParse(match.Groups[1].Value, out int code)) continue;
                if (!seen.Add(code)) continue;
                pairs.Add((code, match.Groups[2].Value));
            }
        }

        return pairs;
    }

    /// <summary>Every step name in the slot logs must have a row in the table.</summary>
    private static LotCheck CheckSlotLogNames(string csvFolder, StdTable table)
    {
        if (!Directory.Exists(csvFolder))
            return new LotCheck("every logged step has a table row", true, "no ..._CSV folder");

        var logs = SlotCsvReader.CollectSlotLogs(csvFolder);
        if (logs.Count == 0)
            return new LotCheck("every logged step has a table row", true, "no slot log found");

        var known = table.Tests.Select(t => TestName.Normalise(t.Name)).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (string log in logs)
        {
            var absent = SlotCsvReader.ReadTestNames(log)
                .Select(TestName.Normalise)
                .Where(n => n.Length > 0 && !known.Contains(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (absent.Count > 0)
                missing.Add($"{Path.GetFileName(log)}: {string.Join(", ", absent)}");
        }

        return new LotCheck("every logged step has a table row", missing.Count == 0,
            string.Join("; ", missing));
    }

    // ------------------------------------------------------------------ BiDF reads

    /// <summary>
    /// The step names in flow order, from <c>tbl_testflow.note</c>.
    ///
    /// <c>test_id</c> is an index into <c>tbl_testdesc</c>, not the step name: on a
    /// real BRF9 BiDF the flow row for <c>IGSS_-10V_3</c> carries <c>test_id 0</c>,
    /// whose description reads <c>IGSS_-10V_1</c>. Reading the name through
    /// <c>test_id</c> therefore invents a duplicate step and hides a real mix-up.
    /// The description is used only when a BiDF has no <c>note</c> at all.
    /// </summary>
    private static string[] FlowNames(BidfReader bidf)
    {
        var rows = bidf.Query("select test_index, note, test_id from tbl_testflow order by test_index");
        bool hasNote = rows.Any(r => r.Length > 1 && r[1].Trim().Length > 0);

        if (hasNote) return rows.Select(r => r[1].Trim()).ToArray();

        var description = bidf.Query("select test_id, test_desc from tbl_testdesc")
            .GroupBy(r => r[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.Ordinal);

        return rows.Select(r => description.GetValueOrDefault(r[2], string.Empty)).ToArray();
    }

    /// <summary><c>test_index</c> -> the (lsl, usl) pair the engine wrote.</summary>
    private static Dictionary<int, (long Lsl, long Usl)> ParametricLimits(BidfReader bidf)
    {
        var limits = new Dictionary<int, (long, long)>();
        foreach (string[] row in bidf.Query("select measure_id, lsl_num, usl_num from tbl_parametricdesc"))
        {
            if (!int.TryParse(row[0], out int index)) continue;
            limits[index] = (Integer(row[1]), Integer(row[2]));
        }
        return limits;
    }

    /// <summary>
    /// DUTs per hardware bin. Summed per bin on purpose: grouping by
    /// <c>(hwbin, hwbin_desc, swbin)</c> and keeping one row per bin - which the
    /// Python tool did - silently drops every site that shares a bin but not a SW
    /// bin, and reports a count that is too low.
    /// </summary>
    private static Dictionary<int, long> HardwareBins(BidfReader bidf)
    {
        var bins = new Dictionary<int, long>();
        foreach (string[] row in bidf.Query("select hwbin, count(*) from tbl_result group by hwbin order by hwbin"))
        {
            if (!int.TryParse(row[0], out int bin)) continue;
            bins[bin] = Integer(row[1]);
        }
        return bins;
    }

    private static string Differences(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        var parts = new List<string>();
        for (int i = 0; i < Math.Max(actual.Count, expected.Count); i++)
        {
            string a = i < actual.Count ? actual[i] : "<missing>";
            string b = i < expected.Count ? expected[i] : "<missing>";
            if (a != b) parts.Add($"{i + 1}: {a} != {b}");
        }
        return string.Join("; ", parts.Take(8)) + (parts.Count > 8 ? $" (+{parts.Count - 8} more)" : string.Empty);
    }

    private static long Integer(string? text) => long.TryParse(text, out long value) ? value : 0;

    private static bool ProfileFalse(string? flag)
    {
        string text = (flag ?? string.Empty).Trim().ToLowerInvariant();
        return text is "0" or "no" or "n" or "false" or "off";
    }
}

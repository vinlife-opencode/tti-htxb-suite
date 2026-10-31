namespace Htxb.RecipeCore.Diff;

/// <summary>
/// Pairs the steps of a workbook with the steps of a machine recipe.
///
/// Alignment is positional (longest common subsequence over normalised names),
/// not a dictionary lookup: <see cref="Model.TestName.Normalise"/> deliberately
/// collapses a trailing <c>_&lt;n&gt;</c>, so <c>IGSS_20V_1</c> / <c>_2</c> /
/// <c>_3</c> share one key and a lookup would pair all three with the first
/// machine row.
///
/// The alias map is applied only to steps the LCS could not match, and only when
/// the machine has an unused step with exactly the mapped name. Everything else
/// is reported as one-sided rather than guessed.
/// </summary>
public static class RecipeAligner
{
    /// <summary>
    /// Workbook name -> machine name, keyed by the normalised form.
    ///
    /// Asserted against <c>BidfGen2/docs/test-vectors/recipe-aliases.json</c> by
    /// both test suites. Evidence: the customer's own <c>SW BIN Priority</c> text
    /// names the tester feature (<c>HTRB6_R</c> is labelled <c>F_HTRB6</c>), and
    /// the machine exports confirm it against the workbooks.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["htgb"] = "htgb1",
            ["htrb6r"] = "htrb6",
            ["htgbht6r"] = "htgbht6",
            ["htrb7r"] = "htrb7",
            ["htgbht7r"] = "htgbht7",
            ["htrb8r"] = "htrb8",
            ["htgbht8r"] = "htgbht8",
            // SQ: one workbook row becomes the ramp step, plus a trailing RAMP_DOWN.
            // Two spellings occur in the same delivery.
            ["chrampuphtgb"] = "htgbramp",
            ["htgbrampup"] = "htgbramp",
        };

    /// <summary>One pairing result; indices refer to the two input lists.</summary>
    public sealed record Alignment(
        IReadOnlyList<(int Workbook, int Machine, string How)> Pairs,
        IReadOnlyList<int> WorkbookOnly,
        IReadOnlyList<int> MachineOnly);

    public static Alignment Align(
        IReadOnlyList<string> workbookKeys,
        IReadOnlyList<string> machineKeys,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        aliases ??= DefaultAliases;

        var exact = LongestCommonSubsequence(workbookKeys, machineKeys);
        var usedWorkbook = new HashSet<int>(exact.Select(p => p.Workbook));
        var usedMachine = new HashSet<int>(exact.Select(p => p.Machine));

        var pairs = exact.Select(p => (p.Workbook, p.Machine, "exact")).ToList();
        var workbookOnly = new List<int>();
        var machineOnly = new List<int>();

        for (int i = 0; i < workbookKeys.Count; i++)
        {
            if (usedWorkbook.Contains(i)) continue;

            if (aliases.TryGetValue(workbookKeys[i], out string? target))
            {
                int match = -1;
                for (int j = 0; j < machineKeys.Count; j++)
                {
                    if (!usedMachine.Contains(j) && machineKeys[j] == target)
                    {
                        match = j;
                        break;
                    }
                }

                if (match >= 0)
                {
                    pairs.Add((i, match, "alias"));
                    usedWorkbook.Add(i);
                    usedMachine.Add(match);
                    continue;
                }
            }

            workbookOnly.Add(i);
        }

        for (int j = 0; j < machineKeys.Count; j++)
            if (!usedMachine.Contains(j)) machineOnly.Add(j);

        pairs.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return new Alignment(pairs, workbookOnly, machineOnly);
    }

    private static List<(int Workbook, int Machine)> LongestCommonSubsequence(
        IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int n = a.Count, m = b.Count;
        var length = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                length[i, j] = a[i] == b[j]
                    ? length[i + 1, j + 1] + 1
                    : Math.Max(length[i + 1, j], length[i, j + 1]);

        var pairs = new List<(int, int)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                pairs.Add((x, y));
                x++;
                y++;
            }
            else if (length[x + 1, y] >= length[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }
}

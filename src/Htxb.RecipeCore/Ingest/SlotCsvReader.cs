using System.Text;
using Htxb.RecipeCore.Model;

namespace Htxb.RecipeCore.Ingest;

/// <summary>
/// Reads the test rows of a tester slot log.
///
/// Only the <c>Test Data</c> section is needed here - enough to answer "which step
/// names did the tester actually log", which is the one thing a limits table cannot
/// be written without (the engine looks measurements up by that name).
///
/// This mirrors <c>bidfgen.csvparse.parse_slot_csv</c> for the section it reads:
/// a banner row holds the section name in column A and nothing else, the next row
/// is the header, and the section ends at the next banner. A row is a measurement
/// only when it has a test name and a <c>TestTime</c> that is neither blank nor
/// <c>NA</c> - the tester's Excel export repeats its drift/bin summary rows inside
/// the block, and those must never be mistaken for steps.
/// </summary>
public static class SlotCsvReader
{
    private static readonly HashSet<string> Banners = new(StringComparer.Ordinal)
    {
        "System Information",
        "Burn-In Setup Parameters",
        "Invalid Data",
        "Test Data",
    };

    /// <summary>The step names in the slot's <c>Test Data</c> section, in log order.</summary>
    public static IReadOnlyList<string> ReadTestNames(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("slot log not found", path);

        List<List<string>> rows = ReadRows(path);

        int banner = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count > 0 && rows[i][0].Trim() == "Test Data" && IsBlank(rows[i], 1))
            {
                banner = i;
                break;
            }
        }

        if (banner < 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} has no 'Test Data' section.");
        if (banner + 1 >= rows.Count)
            return [];

        List<string> header = rows[banner + 1].Select(c => c.Trim()).ToList();

        int end = rows.Count;
        for (int j = banner + 2; j < rows.Count; j++)
        {
            if (rows[j].Count > 0 && Banners.Contains(rows[j][0].Trim()) && IsBlank(rows[j], 1))
            {
                end = j;
                break;
            }
        }

        int nameColumn = header.FindIndex(h => Key(h) == "testname");
        if (nameColumn < 0)
            throw new InvalidDataException($"{Path.GetFileName(path)} has no 'Test Name' column in 'Test Data'.");

        int timeColumn = header.FindIndex(h => Key(h) == "testtime");

        var names = new List<string>();
        for (int r = banner + 2; r < end; r++)
        {
            List<string> row = rows[r];
            if (nameColumn >= row.Count) continue;

            string name = row[nameColumn].Trim();
            if (name.Length == 0) continue;

            if (timeColumn >= 0)
            {
                if (timeColumn >= row.Count) continue;
                string stamp = row[timeColumn].Trim();
                if (stamp.Length == 0 || stamp.Equals("NA", StringComparison.OrdinalIgnoreCase)) continue;
            }

            names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Slot logs from a lot folder, its <c>..._CSV</c> sibling, or a folder of them.
    /// Same search order as <c>bidfgen.tools.std_ingest.collect_csvs</c>.
    /// </summary>
    public static IReadOnlyList<string> CollectSlotLogs(string path)
    {
        if (File.Exists(path))
            return path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? [path] : [];

        if (!Directory.Exists(path)) return [];

        var direct = Directory.GetFiles(path, "*.csv")
            .Where(p => !Path.GetFileName(p).Equals("status.txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (direct.Count > 0 && direct.Any(p => System.Text.RegularExpressions.Regex.IsMatch(
                Path.GetFileName(p), @"slot\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
        {
            return direct;
        }

        var nested = Sorted(Directory.GetDirectories(path, "Slot*")
            .SelectMany(d => Directory.GetFiles(d, "*.csv")));
        if (nested.Count == 0)
        {
            nested = Sorted(Directory.GetDirectories(path)
                .SelectMany(d => Directory.GetDirectories(d, "Slot*"))
                .SelectMany(d => Directory.GetFiles(d, "*.csv")));
        }
        return nested;
    }

    private static List<string> Sorted(IEnumerable<string> paths) =>
        paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    private static string Key(string header) =>
        new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsBlank(IReadOnlyList<string> row, int from)
    {
        for (int i = from; i < row.Count; i++)
            if (row[i].Trim().Length > 0) return false;
        return true;
    }

    /// <summary>
    /// The file as a matrix of strings. Quoted fields may contain commas and line
    /// breaks, so this is a real reader rather than a <c>Split(',')</c>.
    /// </summary>
    private static List<List<string>> ReadRows(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);   // drops a UTF-8 BOM
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];

            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}

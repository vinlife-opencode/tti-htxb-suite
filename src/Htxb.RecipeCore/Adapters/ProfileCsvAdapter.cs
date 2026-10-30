using System.Globalization;
using Htxb.RecipeCore.Model;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Adapters;

/// <summary>One row of <c>CustomerProfileSelection.csv</c>.</summary>
public sealed record ProfileRow
{
    public required string Customer { get; init; }
    public required string Recipe { get; init; }
    public string RecipeGroup { get; init; } = string.Empty;
    public string FuncLoct { get; init; } = string.Empty;
    public string DriftEn { get; init; } = string.Empty;
    public string DriftIsgs { get; init; } = string.Empty;
    public string BidfEn { get; init; } = string.Empty;
    public string SlotSumEn { get; init; } = string.Empty;
    public string DutName { get; init; } = string.Empty;
    public string DutPackage { get; init; } = string.Empty;
    public string StdTable { get; init; } = string.Empty;
    public string TypeCode { get; init; } = string.Empty;
}

/// <summary>
/// Reads <c>CustomerProfileSelection.csv</c>.
///
/// The file is not a clean CSV: row 1 is a banner ("-,Customer Profile Selection,,
/// Rev 1.1,25-Jul-26,K.C"), row 2 is blank, row 3 is the header, and the data starts
/// at row 4. Columns are therefore located by HEADER TEXT, never by position.
///
/// This file is the only thing that says which std table a recipe uses. A recipe
/// that is absent from it is produced report-only by the watcher - it gets no BiDF
/// at all - so "is the recipe listed?" is the first question the audit must answer.
/// </summary>
public static class ProfileCsvAdapter
{
    public const string RelativePath = @"Cust_Profile_Select\CustomerProfileSelection.csv";

    public static IReadOnlyList<ProfileRow> Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Profile CSV not found: {path}", path);

        string[] lines = File.ReadAllLines(path);
        int headerIndex = -1;
        int recipeColumn = -1;

        for (int i = 0; i < Math.Min(lines.Length, 20); i++)
        {
            var cells = SplitCsv(lines[i]);
            int candidate = IndexOf(cells, "recipegroup");
            if (candidate >= 0) { headerIndex = i; recipeColumn = candidate; break; }
        }

        if (headerIndex < 0)
            throw new InvalidDataException($"'{Path.GetFileName(path)}' has no 'Recipe Group' header row.");

        var headers = SplitCsv(lines[headerIndex]);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 0; c < headers.Count; c++)
        {
            string key = Profile.Squash(headers[c]);
            if (key.Length > 0) index.TryAdd(key, c);
        }

        int Column(string name) => index.TryGetValue(Profile.Squash(name), out int c) ? c : -1;

        int customer = Column("Customer");
        int groupName = Column("Recipe Group");
        int group = Column("FuncLoct");
        int driftEn = Column("Drift_En");
        int driftIsgs = Column("DriftISGS");
        int bidfEn = Column("BiDF_En");
        int slotSumEn = Column("SlotSum_En");
        int dutName = Column("DUT Name");
        int dutPackage = Column("DUT Package");
        int stdTable = Column("Std_Table");
        int typeCode = Column("Type Code");

        var rows = new List<ProfileRow>();

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = SplitCsv(lines[i]);

            string recipe = At(cells, groupName);
            if (recipe.Length == 0) continue;

            rows.Add(new ProfileRow
            {
                Customer = At(cells, customer),
                Recipe = recipe,
                RecipeGroup = recipe,
                FuncLoct = At(cells, group),
                DriftEn = At(cells, driftEn),
                DriftIsgs = At(cells, driftIsgs),
                BidfEn = At(cells, bidfEn),
                SlotSumEn = At(cells, slotSumEn),
                DutName = At(cells, dutName),
                DutPackage = At(cells, dutPackage),
                StdTable = At(cells, stdTable),
                TypeCode = At(cells, typeCode),
            });
        }

        _ = recipeColumn;
        return rows;
    }

    /// <summary>Case-insensitive recipe lookup, the way <c>Config.profile_for_recipe</c> does it.</summary>
    public static ProfileRow? Find(IEnumerable<ProfileRow> rows, string recipe)
    {
        foreach (var row in rows)
        {
            if (string.Equals(row.Recipe, recipe, StringComparison.OrdinalIgnoreCase)) return row;
        }

        // Config falls back to a case-insensitive scan; keep that behaviour visible.
        string lowered = recipe.Trim().ToLowerInvariant();
        return rows.FirstOrDefault(r => r.Recipe.Trim().ToLowerInvariant() == lowered);
    }

    private static string At(IReadOnlyList<string> cells, int index) =>
        index >= 0 && index < cells.Count ? cells[index].Trim() : string.Empty;

    private static int IndexOf(IReadOnlyList<string> cells, string squashed)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (Profile.Squash(cells[i]) == squashed) return i;
        }
        return -1;
    }

    /// <summary>
    /// RFC-4180-ish split: the file is hand-edited and may quote a field that
    /// contains a comma (a DUT package name, for instance).
    /// </summary>
    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var buffer = new System.Text.StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];

            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { buffer.Append('"'); i++; }
                    else quoted = false;
                }
                else buffer.Append(ch);
                continue;
            }

            if (ch == '"') { quoted = true; continue; }
            if (ch == ',') { cells.Add(buffer.ToString()); buffer.Clear(); continue; }
            buffer.Append(ch);
        }

        cells.Add(buffer.ToString());
        return cells;
    }

    /// <summary>Convenience: the CSV beside a std-table folder.</summary>
    public static string PathForTables(string tablesDirectory) =>
        System.IO.Path.Combine(tablesDirectory, RelativePath);

    internal static string Describe(ProfileRow row) =>
        string.Format(CultureInfo.InvariantCulture, "{0}/{1}", row.Customer, row.RecipeGroup);
}

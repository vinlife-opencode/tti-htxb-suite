using Htxb.RecipeCore.Model;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Adapters;

/// <summary>
/// Reads a <c>std_table</c> sheet - the limits table the engine matches logged
/// measurements against.
///
/// The shipped tables carry 17 columns; the customer conversion page emits 16 and
/// stops at <c>Remarks</c>, omitting <c>Test Sect ID Relative</c>. That column is
/// read when present and its absence is reported, never silently accepted.
/// </summary>
public static class StdTableAdapter
{
    public const string DefaultSheet = "std_table";

    public static StdTable Read(string path, string sheetName = DefaultSheet)
    {
        using var workbook = XlsxWorkbook.Open(path);

        if (!workbook.TryGetSheet(sheetName, out XlsxSheet sheet))
        {
            throw new InvalidDataException(
                $"'{System.IO.Path.GetFileName(path)}' has no '{sheetName}' sheet. " +
                $"Available: {string.Join(", ", workbook.SheetNames)}");
        }

        int headerRow = FindHeaderRow(sheet);
        var columns = MapColumns(sheet, headerRow);

        if (!columns.TryGetValue("testname", out int nameColumn))
        {
            throw new InvalidDataException(
                $"'{System.IO.Path.GetFileName(path)}' sheet '{sheet.Name}' has no 'Test name' column.");
        }

        var headers = new List<string>();
        for (int column = 1; column <= sheet.ColumnCount; column++)
        {
            string header = sheet.TextOrEmpty(headerRow, column);
            if (header.Length > 0) headers.Add(header);
        }

        var tests = new List<StdTest>();
        for (int row = headerRow + 1; row <= sheet.RowCount; row++)
        {
            string name = sheet.TextOrEmpty(row, nameColumn);
            if (name.Length == 0) continue;

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, column) in columns)
            {
                string value = sheet.TextOrEmpty(row, column);
                if (value.Length > 0) fields[key] = value;
            }

            tests.Add(new StdTest
            {
                Row = row,
                Name = name,
                SectionId = Field(fields, "testsectid"),
                SectionIdRelative = Field(fields, "testsectidrelative"),
                TestIndex = Field(fields, "testindex"),
                Marker = Field(fields, "marker"),
                TimeSeconds = Number(fields, "time(sec)", "time"),
                ProgramMinutes = Number(fields, "programduration(min)", "programduration"),
                Vds = Field(fields, "v_ds", "vds"),
                Vgs = Field(fields, "v_gs", "vgs"),
                Min = Field(fields, "min"),
                Target = Field(fields, "target"),
                Max = Field(fields, "max"),
                Units = Field(fields, "units"),
                Sequence = Field(fields, "sequence"),
                HwBin = Field(fields, "hwbin"),
                SwBinPriority = Field(fields, "swbinpriority"),
                ExcludeWhenFail = Field(fields, "excludewhenfail"),
                Remarks = Field(fields, "remarks"),
                Fields = fields,
            });
        }

        return new StdTable
        {
            Path = path,
            SheetName = sheet.Name,
            Tests = tests,
            Columns = headers,
        };
    }

    private static int FindHeaderRow(XlsxSheet sheet)
    {
        int limit = Math.Min(sheet.RowCount, 40);
        for (int row = 1; row <= limit; row++)
        {
            for (int column = 1; column <= sheet.ColumnCount; column++)
            {
                if (Profile.Squash(sheet.TextOrEmpty(row, column)) == "testname") return row;
            }
        }
        return 1;
    }

    private static Dictionary<string, int> MapColumns(XlsxSheet sheet, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int column = 1; column <= sheet.ColumnCount; column++)
        {
            string key = Profile.Squash(sheet.TextOrEmpty(headerRow, column));
            if (key.Length == 0) continue;
            map.TryAdd(key, column);
        }
        return map;
    }

    private static string Field(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (fields.TryGetValue(key, out string? value) && value.Length > 0) return value;
        }
        return string.Empty;
    }

    private static double? Number(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        string text = Field(fields, keys);
        return double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }
}

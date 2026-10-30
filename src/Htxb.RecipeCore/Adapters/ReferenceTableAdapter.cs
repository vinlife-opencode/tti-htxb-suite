using Htxb.RecipeCore.Model;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Adapters;

/// <summary>
/// Reads the <c>Reference Table</c> tab of a customer workbook.
///
/// The tab is row-oriented - headers in one row, the single delivery's values in
/// the next - and it is the only place a workbook states the package code, the CP,
/// the MC code and the stress voltages. Several headers are formulas; the reader
/// takes Excel's cached result, so the values are the ones the customer sees.
///
/// The tab's name is not reliable: <c>P_TM_SQGW_B_02_4_5.xlsx</c> spells it
/// "Reference table" with a lower-case t.
/// </summary>
public static class ReferenceTableAdapter
{
    private const string SheetKey = "referencetable";

    public static Profile? TryRead(XlsxWorkbook workbook, List<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        string? sheetName = workbook.SheetNames
            .FirstOrDefault(n => Profile.Squash(n) == SheetKey);
        if (sheetName is null)
        {
            warnings?.Add("no 'Reference Table' tab; package code and stress levels unavailable");
            return null;
        }

        var sheet = workbook.GetSheet(sheetName);

        int headerRow = FindHeaderRow(sheet);
        if (headerRow <= 0)
        {
            warnings?.Add($"'{sheetName}' has no recognisable header row");
            return null;
        }

        int valueRow = FindValueRow(sheet, headerRow);
        if (valueRow <= 0)
        {
            warnings?.Add($"'{sheetName}' has headers but no value row");
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int column = 1; column <= sheet.ColumnCount; column++)
        {
            // Direct text only: a merged header (K1:L1) must be recorded once, at
            // its left cell, or the unit cell beside it would overwrite the value.
            string header = sheet.TextOrEmpty(headerRow, column);
            if (header.Length == 0) continue;

            string key = Profile.Squash(header);
            if (key.Length == 0) continue;

            fields[key] = sheet.TextOrEmpty(valueRow, column);

            // The unit of a merged limit header sits in the next column, which has
            // no header of its own ("µA", "nA", "V").
            int next = column + 1;
            if (next <= sheet.ColumnCount && sheet.TextOrEmpty(headerRow, next).Length == 0)
            {
                string unit = sheet.TextOrEmpty(valueRow, next);
                if (unit.Length > 0) fields[key + "|unit"] = unit;
            }
        }

        if (!fields.ContainsKey("line") || fields["line"].Length == 0)
            warnings?.Add($"'{sheetName}' has no 'Line' value");

        return new Profile { Fields = fields };
    }

    private static int FindHeaderRow(XlsxSheet sheet)
    {
        int limit = Math.Min(sheet.RowCount, 30);
        for (int row = 1; row <= limit; row++)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int column = 1; column <= sheet.ColumnCount; column++)
                keys.Add(Profile.Squash(sheet.TextOrEmpty(row, column)));

            if (keys.Contains("line") && keys.Contains("packagecode")) return row;
        }
        return 0;
    }

    private static int FindValueRow(XlsxSheet sheet, int headerRow)
    {
        int lineColumn = 0;
        for (int column = 1; column <= sheet.ColumnCount; column++)
        {
            if (Profile.Squash(sheet.TextOrEmpty(headerRow, column)) == "line") { lineColumn = column; break; }
        }

        for (int row = headerRow + 1; row <= Math.Min(sheet.RowCount, headerRow + 5); row++)
        {
            string candidate = lineColumn > 0 ? sheet.TextOrEmpty(row, lineColumn) : string.Empty;
            if (candidate.Length > 0) return row;
        }

        return headerRow + 1 <= sheet.RowCount ? headerRow + 1 : 0;
    }
}

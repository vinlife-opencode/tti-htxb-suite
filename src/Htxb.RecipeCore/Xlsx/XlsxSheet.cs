using System.Globalization;
using System.Xml.Linq;

namespace Htxb.RecipeCore.Xlsx;

/// <summary>A worksheet: sparse cell text plus its merged ranges.</summary>
public sealed class XlsxSheet
{
    private readonly Dictionary<long, string> _cells;
    private readonly List<XlsxRange> _merged;

    public string Name { get; }
    public int RowCount { get; }
    public int ColumnCount { get; }
    public IReadOnlyList<XlsxRange> MergedRanges => _merged;

    /// <summary>Number of cells that actually carry a value (used to rank sheets).</summary>
    public int CellCount => _cells.Count;

    private XlsxSheet(string name, Dictionary<long, string> cells, List<XlsxRange> merged)
    {
        Name = name;
        _cells = cells;
        _merged = merged;

        int maxRow = 0, maxColumn = 0;
        foreach (long key in cells.Keys)
        {
            int row = (int)(key >> 32), column = (int)(key & 0xFFFF);
            if (row > maxRow) maxRow = row;
            if (column > maxColumn) maxColumn = column;
        }
        foreach (var range in merged)
        {
            if (range.ToRow > maxRow) maxRow = range.ToRow;
            if (range.ToColumn > maxColumn) maxColumn = range.ToColumn;
        }
        RowCount = maxRow;
        ColumnCount = maxColumn;
    }

    internal static XlsxSheet FromXml(string name, XDocument doc, XNamespace ns, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<long, string>();

        foreach (var row in doc.Root?.Element(ns + "sheetData")?.Elements(ns + "row") ?? [])
        {
            int rowIndex = ParseRowNumber((string?)row.Attribute("r"));
            int runningColumn = 0;

            foreach (var cell in row.Elements(ns + "c"))
            {
                string? reference = (string?)cell.Attribute("r");
                int column, cellRow;

                if (!string.IsNullOrEmpty(reference))
                {
                    try { (cellRow, column) = CellRef.Parse(reference); }
                    catch (FormatException) { cellRow = rowIndex; column = runningColumn + 1; }
                }
                else
                {
                    cellRow = rowIndex;
                    column = runningColumn + 1;
                }

                runningColumn = column;
                if (cellRow < 1) cellRow = Math.Max(rowIndex, 1);

                string? text = ReadCell(cell, ns, sharedStrings);
                if (text is null) continue;

                cells[Key(cellRow, column)] = text;
            }
        }

        var merged = new List<XlsxRange>();
        foreach (var merge in doc.Root?.Element(ns + "mergeCells")?.Elements(ns + "mergeCell") ?? [])
        {
            string? reference = (string?)merge.Attribute("ref");
            if (string.IsNullOrEmpty(reference)) continue;
            try { merged.Add(XlsxRange.Parse(reference)); }
            catch (FormatException) { /* malformed merge range: ignore, never fatal */ }
        }

        return new XlsxSheet(name, cells, merged);
    }

    private static string? ReadCell(XElement cell, XNamespace ns, IReadOnlyList<string> sharedStrings)
    {
        string? type = (string?)cell.Attribute("t");

        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
        {
            var inline = cell.Element(ns + "is");
            if (inline is null) return null;
            return string.Concat(inline.Descendants(ns + "t").Select(t => t.Value));
        }

        // The cached value. For a formula cell this is Excel's last computed result,
        // which is exactly what openpyxl data_only=True returns.
        string? raw = cell.Element(ns + "v")?.Value;

        if (string.Equals(type, "s", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) return null;
            return index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : null;
        }

        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private static long Key(int row, int column) => ((long)row << 32) | (uint)column;

    private static int ParseRowNumber(string? attribute)
    {
        if (!string.IsNullOrEmpty(attribute) &&
            int.TryParse(attribute, NumberStyles.Integer, CultureInfo.InvariantCulture, out int row) && row > 0)
        {
            return row;
        }
        return 1;
    }

    // ---------------------------------------------------------------- accessors

    /// <summary>Raw cell text, or null when the cell is empty.</summary>
    public string? Text(int row, int column) =>
        row >= 1 && column >= 1 && _cells.TryGetValue(Key(row, column), out string? value) ? value : null;

    /// <summary>Trimmed cell text, or the empty string when empty.</summary>
    public string TextOrEmpty(int row, int column) => (Text(row, column) ?? string.Empty).Trim();

    public string? Text(string reference)
    {
        var (row, column) = CellRef.Parse(reference);
        return Text(row, column);
    }

    public string TextOrEmpty(string reference) => (Text(reference) ?? string.Empty).Trim();

    /// <summary>
    /// Numeric view of a cell. Handles the plain numbers Excel stores as text too
    /// ("1200", " 1200 ") and returns null for anything non-numeric.
    /// </summary>
    public double? Number(int row, int column)
    {
        string? text = Text(row, column);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    /// <summary>The merged range covering this cell, if any.</summary>
    public XlsxRange? MergeAt(int row, int column)
    {
        foreach (var range in _merged)
        {
            if (range.Contains(row, column)) return range;
        }
        return null;
    }

    /// <summary>
    /// Value of a merged block, for a cell that is the ANCHOR of a vertical merge:
    /// a "Test name" such as <c>TOT STRESS TIME</c> merged over <c>E69:F70</c> must
    /// read the same for both rows.
    ///
    /// Deliberately does NOT expand a merge that starts in an earlier column. Some
    /// sheets merge an entire row (<c>A81:XFD81</c>) to hold a change-log paragraph;
    /// expanding that would turn the paragraph into a step name in column E.
    /// </summary>
    public string TextOrEmptyExpanded(int row, int column)
    {
        string direct = TextOrEmpty(row, column);
        if (direct.Length > 0) return direct;

        var merge = MergeAt(row, column);
        if (merge is null || merge.Value.FromColumn != column) return string.Empty;

        return TextOrEmpty(merge.Value.FromRow, column);
    }

    /// <summary>Rows that contain at least one non-empty cell, ascending.</summary>
    public IEnumerable<int> NonEmptyRows()
    {
        var rows = new SortedSet<int>();
        foreach (long key in _cells.Keys) rows.Add((int)(key >> 32));
        return rows;
    }
}

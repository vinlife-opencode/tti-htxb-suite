using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Ingest;

/// <summary>
/// Writes a <c>std_table</c> sheet from built rows.
///
/// The sheet is what <see cref="Adapters.StdTableAdapter"/> reads back: header on
/// row 1, one row per test, sixteen columns (seventeen when
/// <see cref="StdTableBuilder.RelativeColumn"/> is included).
///
/// Values keep their type - a limit stays a number so Excel and the engine both see
/// a number, while <c>15sec</c> in a time cell stays the text the tester wrote. That
/// is the one rule the whole writer exists for.
/// </summary>
public static class StdTableWriter
{
    public const string DefaultSheet = "std_table";

    /// <summary>The shipped shape: the sixteen columns, no <c>Test Sect ID Relative</c>.</summary>
    public static void Write(string path, StdTableBuild build, bool withRelativeColumn = false,
        string sheet = DefaultSheet)
    {
        ArgumentNullException.ThrowIfNull(build);
        IReadOnlyList<string> columns = withRelativeColumn
            ? StdTableBuilder.IngestColumns
            : StdTableBuilder.Columns;

        Write(path, build.Rows, columns, sheet);
    }

    /// <summary>Writes an explicit column set, for a caller that owns its own layout.</summary>
    public static void Write(string path, IReadOnlyList<object?[]> rows,
        IReadOnlyList<string>? columns = null, string sheet = DefaultSheet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(rows);

        columns ??= StdTableBuilder.Columns;

        var all = new List<IReadOnlyList<object?>>(rows.Count + 1)
        {
            columns.Cast<object?>().ToList(),
        };

        foreach (object?[] row in rows)
        {
            var padded = new List<object?>(Math.Max(row.Length, columns.Count));
            padded.AddRange(row);

            // The header decides the width; a short row is padded with blanks so the
            // reader never sees a column that exists in some rows and not others.
            while (padded.Count < columns.Count) padded.Add(null);
            all.Add(padded);
        }

        XlsxWriter.Write(path, sheet, all);
    }
}

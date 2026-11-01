using System.Text;

namespace Htxb.RecipeCore.History;

/// <summary>
/// Writes one lot's history report: a one-row CSV named
/// <c>&lt;stamp&gt;_&lt;site&gt;_&lt;lot&gt;.csv</c>.
///
/// CSV on purpose - it is the durable, greppable record an operator can open in
/// Excel or mail to somebody, and the meaning lives in the <c>state</c> and
/// <c>status</c> columns rather than in cell colours (a CSV has none; the grid that
/// reads these files carries the colour).
/// </summary>
public static class LotReportWriter
{
    /// <summary>
    /// Write the report and return its full path. The folder is created when missing;
    /// re-scanning a lot overwrites its own file and nothing else.
    /// </summary>
    public static string Write(LotHistory lot, string outDirectory)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outDirectory);

        Directory.CreateDirectory(outDirectory);
        string path = Path.Combine(outDirectory, lot.ReportName);

        var text = new StringBuilder();
        text.AppendLine(string.Join(',', LotHistory.Columns.Select(Escape)));
        text.AppendLine(string.Join(',', lot.Cells().Select(Escape)));

        // UTF-8 without a BOM: Excel reads it and nothing downstream sees a stray
        // character at the start of the header.
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return Path.GetFullPath(path);
    }

    /// <summary>Quote a cell only when it would otherwise break the row.</summary>
    private static string Escape(string? value)
    {
        string text = value ?? string.Empty;
        if (text.Length == 0) return string.Empty;
        bool needs = text.Contains(',') || text.Contains('"') ||
                     text.Contains('\n') || text.Contains('\r');
        return needs ? '"' + text.Replace("\"", "\"\"") + '"' : text;
    }
}

using System.IO.Compression;
using System.Text;

namespace Htxb.RecipeCore.Xlsx;

/// <summary>
/// Writes a single-sheet .xlsx without a NuGet package.
///
/// An xlsx file is a zip of small XML parts; this writer emits the minimum set a
/// reader needs (content types, relationships, workbook, one worksheet) and uses
/// inline strings so there is no shared-string table to build. Values stay in the
/// type they are given: a number is written as a number, everything else as text -
/// which is what the std tables rely on (SQ writes <c>15sec</c> into a numeric
/// column, and the engine expects to see that text).
/// </summary>
public static class XlsxWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(string path, string sheetName, IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string sheet = SheetName(sheetName);

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);
        if (File.Exists(path)) File.Delete(path);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", ContentTypes);
        Add(zip, "_rels/.rels", RootRels);
        Add(zip, "xl/workbook.xml", Workbook(sheet));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
        Add(zip, "xl/worksheets/sheet1.xml", Sheet(rows));
    }

    private static void Add(ZipArchive zip, string name, string xml)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        byte[] bytes = Utf8.GetBytes(xml);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string SheetName(string name)
    {
        string clean = string.IsNullOrWhiteSpace(name) ? "Sheet1" : name.Trim();
        foreach (char invalid in new[] { '\\', '/', '*', '[', ']', ':', '?' })
            clean = clean.Replace(invalid, '_');
        return clean.Length > 31 ? clean[..31] : clean;
    }

    private static string Sheet(IEnumerable<IReadOnlyList<object?>> rows)
    {
        var xml = new StringBuilder();
        xml.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        xml.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        xml.Append("<sheetData>");

        int rowNumber = 0;
        foreach (IReadOnlyList<object?> row in rows)
        {
            rowNumber++;
            xml.Append("<row r=\"").Append(rowNumber).Append("\">");
            for (int column = 0; column < row.Count; column++)
            {
                object? value = row[column];
                if (value is null) continue;

                string reference = CellRef.Format(rowNumber, column + 1);
                if (value is int or long or short or byte or float or double or decimal)
                {
                    xml.Append("<c r=\"").Append(reference).Append("\"><v>")
                       .Append(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture))
                       .Append("</v></c>");
                }
                else
                {
                    string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    if (text.Length == 0) continue;
                    xml.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                       .Append(Escape(text)).Append("</t></is></c>");
                }
            }
            xml.Append("</row>");
        }

        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
        </Types>
        """;

    private const string RootRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private const string WorkbookRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
        </Relationships>
        """;

    private static string Workbook(string sheet) =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" +
        """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" """ +
        """xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">""" +
        $"<sheets><sheet name=\"{Escape(sheet)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
}

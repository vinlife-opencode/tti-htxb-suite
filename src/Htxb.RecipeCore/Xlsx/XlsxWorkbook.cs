using System.IO.Compression;
using System.Xml.Linq;

namespace Htxb.RecipeCore.Xlsx;

/// <summary>
/// Read-only .xlsx workbook access built on <see cref="ZipArchive"/> + XLinq.
///
/// No NuGet: the whole file is a zip of XML parts, and we only ever need five of
/// them (workbook, its rels, shared strings, the sheet, and the sheet's merged
/// ranges). Cell values are returned as the TEXT Excel cached, so a formula cell
/// resolves to its last computed result exactly as openpyxl's <c>data_only=True</c>
/// does - we never attempt to evaluate formulas.
///
/// Known limits, deliberate:
///   * date-formatted numbers come back as the raw serial (no numFmt styling);
///   * .xls (BIFF8) is not supported, only .xlsx/.xlsm (OOXML).
/// Neither is needed by the recipe reader: no recipe field we act on is a date.
/// </summary>
public sealed class XlsxWorkbook : IDisposable
{
    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, string> _sheetTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sharedStrings;
    private readonly Dictionary<string, XlsxSheet> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public string Path { get; }
    public IReadOnlyList<string> SheetNames { get; }

    private XlsxWorkbook(string path, ZipArchive zip)
    {
        Path = path;
        _zip = zip;
        _sharedStrings = ReadSharedStrings(zip);

        var workbook = LoadXml(zip, "xl/workbook.xml")
            ?? throw new InvalidDataException($"'{path}' has no xl/workbook.xml - not an OOXML workbook.");

        var rels = ReadRelationships(zip);
        var names = new List<string>();

        foreach (var sheet in workbook.Root!.Element(MainNs + "sheets")?.Elements(MainNs + "sheet") ?? [])
        {
            string? name = (string?)sheet.Attribute("name");
            string? relId = (string?)sheet.Attribute(RelNs + "id");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(relId)) continue;
            if (!rels.TryGetValue(relId, out string? target)) continue;

            names.Add(name);
            _sheetTargets[name] = NormaliseTarget(target);
        }

        SheetNames = names;
    }

    public static XlsxWorkbook Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException($"Workbook not found: {path}", path);

        // FileShare.ReadWrite: a workbook open in Excel must still be readable.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return new XlsxWorkbook(path, new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Sheet lookup by name, case-insensitive and whitespace-insensitive.</summary>
    public bool TryGetSheet(string name, out XlsxSheet sheet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string wanted = Squash(name);

        foreach (string candidate in SheetNames)
        {
            if (!string.Equals(Squash(candidate), wanted, StringComparison.Ordinal)) continue;
            sheet = GetSheet(candidate);
            return true;
        }

        sheet = null!;
        return false;
    }

    /// <summary>Sheet lookup by name; throws with the available names listed.</summary>
    public XlsxSheet GetSheet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string actual = SheetNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.Ordinal))
            ?? SheetNames.FirstOrDefault(n => string.Equals(Squash(n), Squash(name), StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(
                $"Sheet '{name}' not found in '{System.IO.Path.GetFileName(Path)}'. " +
                $"Available: {string.Join(", ", SheetNames)}");

        if (_loaded.TryGetValue(actual, out XlsxSheet? cached)) return cached;

        string target = _sheetTargets[actual];
        var doc = LoadXml(_zip, target)
            ?? throw new InvalidDataException($"Sheet part '{target}' missing from '{Path}'.");

        var sheet = XlsxSheet.FromXml(actual, doc, MainNs, _sharedStrings);
        _loaded[actual] = sheet;
        return sheet;
    }

    /// <summary>The sheet whose name matches the file stem, else the one with the most cells.</summary>
    public XlsxSheet GetPlanSheet()
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(Path);
        if (TryGetSheet(stem, out XlsxSheet? byStem)) return byStem;

        XlsxSheet? best = null;
        foreach (string name in SheetNames)
        {
            var sheet = GetSheet(name);
            if (best is null || sheet.CellCount > best.CellCount) best = sheet;
        }
        return best ?? throw new InvalidDataException($"'{Path}' has no worksheets.");
    }

    public void Dispose() => _zip.Dispose();

    // ---------------------------------------------------------------- internals

    private static string Squash(string text) =>
        new(text.Where(ch => !char.IsWhiteSpace(ch)).ToArray());

    private static string NormaliseTarget(string target) =>
        target.StartsWith('/') ? target[1..] : "xl/" + target;

    private XDocument? LoadXml(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive zip)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (entry is null) return map;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        foreach (var rel in doc.Root?.Elements(PackageRelNs + "Relationship") ?? [])
        {
            string? id = (string?)rel.Attribute("Id");
            string? target = (string?)rel.Attribute("Target");
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target)) map[id] = target;
        }
        return map;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        foreach (var si in doc.Root?.Elements(MainNs + "si") ?? [])
        {
            // <si> holds either a single <t> or a run list <r><t>..</t></r>; phonetic
            // guides (<rPh>) are annotations, never part of the cell text.
            var parts = si.Descendants(MainNs + "t")
                .Where(t => !t.Ancestors(MainNs + "rPh").Any())
                .Select(t => t.Value);

            list.Add(string.Concat(parts));
        }
        return list;
    }
}

using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Htxb.Host;

/// <summary>
/// COM-visible host object exposed to the page as <c>window.tools</c> via
/// CoreWebView2.AddHostObjectToScript. It replaces browser APIs that WebView2
/// does not provide (File System Access API, save-as pickers) with native ones,
/// and mirrors read/write file-system operations. Dialog methods are marshalled
/// to the WinForms UI thread.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class NativeBridge
{
    private readonly Control? _owner;
    private readonly ToolSettings _settings;

    public NativeBridge(Control? owner = null, string? toolId = null)
    {
        _owner = owner;
        _settings = new ToolSettings(toolId);
    }

    // ---- Dialogs (UI-thread marshalled) ------------------------------------

    public string SelectDirectory()
        => RunDialog(() =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select a folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            var lastDir = _settings.GetLastDir();
            if (lastDir != "" && Directory.Exists(lastDir)) dlg.SelectedPath = lastDir;
            if (dlg.ShowDialog() != DialogResult.OK) return "";
            _settings.SetLastDir(dlg.SelectedPath ?? "");
            return dlg.SelectedPath ?? "";
        });

    public string SelectFile(string filter)
        => RunDialog(() =>
        {
            using var dlg = new OpenFileDialog();
            if (!string.IsNullOrEmpty(filter)) dlg.Filter = filter;
            SetInitialDir(dlg);
            if (dlg.ShowDialog() != DialogResult.OK) return "";
            RememberDir(dlg.FileName);
            return dlg.FileName;
        });

    public string SelectFiles(string filter)
        => RunDialog(() =>
        {
            using var dlg = new OpenFileDialog();
            if (!string.IsNullOrEmpty(filter)) dlg.Filter = filter;
            dlg.Multiselect = true;
            SetInitialDir(dlg);
            if (dlg.ShowDialog() != DialogResult.OK) return "[]";
            RememberDir(dlg.FileNames.Length > 0 ? dlg.FileNames[0] : null);
            return JsonSerializer.Serialize(dlg.FileNames);
        });

    public string SaveFile(string defaultName)
        => RunDialog(() =>
        {
            using var dlg = new SaveFileDialog
            {
                FileName = string.IsNullOrWhiteSpace(defaultName) ? "output" : defaultName,
                OverwritePrompt = true,
            };
            SetInitialDir(dlg);
            if (dlg.ShowDialog() != DialogResult.OK) return "";
            RememberDir(dlg.FileName);
            return dlg.FileName;
        });

    private void SetInitialDir(FileDialog dlg)
    {
        try
        {
            var lastDir = _settings.GetLastDir();
            if (lastDir != "" && Directory.Exists(lastDir)) dlg.InitialDirectory = lastDir;
        }
        catch
        {
            // fall back to the dialog default
        }
    }

    private void RememberDir(string? fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;
            var dir = Path.GetDirectoryName(fileName);
            if (!string.IsNullOrWhiteSpace(dir)) _settings.SetLastDir(dir);
        }
        catch
        {
            // best effort
        }
    }

    // ---- Path helpers -------------------------------------------------------

    /// <summary>
    /// Returns the current user's Downloads folder (created if missing),
    /// or "" when it cannot be resolved. Used by tools that export files
    /// directly to Downloads without a save dialog (e.g. Barcode Generator).
    /// </summary>
    public string GetDownloadsFolder()
    {
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile)) return "";
            var dl = Path.Combine(profile, "Downloads");
            Directory.CreateDirectory(dl);
            return dl;
        }
        catch
        {
            return "";
        }
    }

    public string JoinPath(string a, string b)
        => Path.Combine(a ?? "", b ?? "");

    public string LeafName(string path)
    {
        var trimmed = (path ?? "").TrimEnd('/', '\\');
        return trimmed.Length == 0
            ? ""
            : Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return Path.GetFullPath(path);
    }

    // ---- File-system IO -----------------------------------------------------

    public string ReadFile(string path)
    {
        var p = NormalizePath(path);
        return File.Exists(p) ? File.ReadAllText(p) : "";
    }

    public byte[] ReadFileBytes(string path)
    {
        var p = NormalizePath(path);
        return File.Exists(p) ? File.ReadAllBytes(p) : Array.Empty<byte>();
    }

    public void WriteFile(string path, string content)
    {
        var p = NormalizePath(path);
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(p, content ?? "");
    }

    public void WriteFileBytes(string path, byte[] data)
    {
        var p = NormalizePath(path);
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(p, data ?? Array.Empty<byte>());
    }

    public void CreateDirectory(string path)
    {
        var p = NormalizePath(path);
        if (!string.IsNullOrEmpty(p)) Directory.CreateDirectory(p);
    }

    /// <summary>Reads a file and returns its content as a Base64 string (for binary-safe transfer to JS).</summary>
    public string ReadFileBase64(string path)
    {
        var p = NormalizePath(path);
        return File.Exists(p) ? Convert.ToBase64String(File.ReadAllBytes(p)) : "";
    }

    /// <summary>Writes a Base64-encoded payload from JS to a file (creates parent dirs as needed).</summary>
    public void WriteFileBase64(string path, string base64)
    {
        var p = NormalizePath(path);
        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(p, Convert.FromBase64String(base64 ?? ""));
    }

    /// <summary>
    /// Returns <c>[{"p":"absolute path","name":"...","isDir":true|false}, ...]</c>
    /// for the immediate children of <paramref name="path"/>.
    /// </summary>
    public string ListDirectory(string path)
    {
        var p = NormalizePath(path);
        if (!Directory.Exists(p)) return "[]";
        var items = new System.Collections.Generic.List<object>();
        foreach (var d in Directory.GetDirectories(p))
            items.Add(new { p = d, name = Path.GetFileName(d), isDir = true });
        foreach (var f in Directory.GetFiles(p))
            items.Add(new { p = f, name = Path.GetFileName(f), isDir = false });
        return JsonSerializer.Serialize(items);
    }

    /// <summary>
    /// Fast recursive folder summary (no file content is read), returned as
    /// <c>{"totalFiles":N,"csvFiles":M,"subfolders":D}</c>. Used to show the
    /// page how many files/CSVs a search folder contains before any parsing.
    /// </summary>
    public string CountFiles(string path)
    {
        var p = NormalizePath(path);
        if (!Directory.Exists(p))
            return "{\"totalFiles\":0,\"csvFiles\":0,\"subfolders\":0}";

        long total = 0, csv = 0, dirs = 0;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(p, "*", SearchOption.AllDirectories))
                dirs++;
            foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
            {
                total++;
                if (f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) csv++;
            }
        }
        catch
        {
            // ignore inaccessible subtrees; report what we could count
        }
        return "{\"totalFiles\":" + total + ",\"csvFiles\":" + csv + ",\"subfolders\":" + dirs + "}";
    }

    // ---- Lot Extractor scan (native, out of the WebView2 renderer) ----------
    // The page normally reads every CSV into the WebView2 heap (File blobs +
    // full parsed rows), which can exceed 4 GB for a large slot folder and white
    // out the UI. These methods move the scan/parse fully to native code on a
    // background thread and hand the page only compact per-file summaries.
    // window.tools.BeginScanLotCsvs(root, dateFrom, dateTo) starts the scan;
    // window.tools.PollScanLotCsvs() returns progress until the JSON result is
    // ready. Parsing mirrors the page's parseCSV/extractStatus/extractDateTime*
    // logic exactly so the summaries are identical to what the page would build.

    private readonly object _lotScanLock = new();
    private bool _lotScanRunning;
    private long _lotScanProcessed;
    private long _lotScanTotal;
    private string? _lotScanResult;
    private string? _lotScanError;

    private static readonly string[] SlotKeywords =
    {
        "slot", "slot#", "slot no", "slot number", "position", "socket", "site", "index",
    };

    public void BeginScanLotCsvs(string root, string dateFrom, string dateTo)
    {
        var p = NormalizePath(root ?? "");
        var from = NormalizeDateKey(dateFrom);
        var to = NormalizeDateKey(dateTo);
        lock (_lotScanLock)
        {
            if (_lotScanRunning) return;
            _lotScanRunning = true;
            _lotScanProcessed = 0;
            _lotScanTotal = 0;
            _lotScanResult = null;
            _lotScanError = null;
        }
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var json = ScanLotCsvsCore(p, from, to);
                lock (_lotScanLock) _lotScanResult = json;
            }
            catch (Exception ex)
            {
                lock (_lotScanLock) _lotScanError = ex.Message;
            }
            finally
            {
                lock (_lotScanLock) _lotScanRunning = false;
            }
        });
    }

    /// <summary>
    /// Polls the background scan. Returns <c>{"running":true,"processed":N,"total":M}</c>
    /// while working, and <c>{"running":false,"processed":N,"total":M,"result":[...]}</c>
    /// (or <c>{"running":false,"error":"..."}</c>) when finished.
    /// </summary>
    public string PollScanLotCsvs()
    {
        lock (_lotScanLock)
        {
            if (_lotScanRunning)
                return "{\"running\":true,\"processed\":" + _lotScanProcessed + ",\"total\":" + _lotScanTotal + "}";
            if (_lotScanError != null)
                return "{\"running\":false,\"error\":\"" + JsonEscape(_lotScanError) + "\",\"result\":[]}";
            return "{\"running\":false,\"processed\":" + _lotScanProcessed + ",\"total\":" + _lotScanTotal
                   + ",\"result\":" + (_lotScanResult ?? "[]") + "}";
        }
    }

    private string ScanLotCsvsCore(string root, string from, string to)
    {
        var all = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "*.csv", SearchOption.AllDirectories))
                all.Add(f);
        }
        catch
        {
            // ignore inaccessible subtrees
        }

        // Pre-pass: only files whose filename date is inside the requested range
        // are parsed (mirrors the page's date pre-filter).
        var inRange = new List<string>(all.Count);
        foreach (var f in all)
        {
            var key = ExtractDateTimeKey(Path.GetFileName(f));
            if (CsvDateInRange(key, from, to)) inRange.Add(f);
        }

        lock (_lotScanLock) _lotScanTotal = inRange.Count;

        var items = new List<object>(inRange.Count);
        for (int i = 0; i < inRange.Count; i++)
        {
            var fp = inRange[i];
            try
            {
                var name = Path.GetFileName(fp);
                var text = File.ReadAllText(fp);
                var rows = ParseCsv(text);
                if (rows.Count < 3) continue; // page skips files with < 3 rows

                var lot = GetCell(rows[2], 4).Trim();
                var status = ExtractStatus(rows);
                var slot = FindSlot(rows, name);

                items.Add(new
                {
                    name = name,
                    path = fp,
                    dtKey = ExtractDateTimeKey(name),
                    dtCode = ExtractDateTimeCode(name),
                    lot = lot,
                    slot = slot,
                    status = status,
                    rowCount = rows.Count,
                });
            }
            catch
            {
                // unreadable file -> skip, keep scanning
            }
            lock (_lotScanLock) _lotScanProcessed = i + 1;
        }

        return JsonSerializer.Serialize(items);
    }

    private static string NormalizeDateKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.Trim().Replace("-", "").Replace("/", "").Replace(":", "");
    }

    private static string GetCell(List<string> row, int idx)
        => idx >= 0 && idx < row.Count ? row[idx] : "";

    private static bool CsvDateInRange(string dtKey, string from, string to)
    {
        var raw = dtKey.Length >= 8 ? dtKey.Substring(0, 8) : "";
        if (raw.Length != 8)
            // No parseable timestamp in the filename (e.g. a stray "null.csv").
            // It only belongs to a full, unfiltered scan; never to a specific
            // date window, or it would produce a phantom match that yields no
            // parsed file and misleads the "No CSV files found" message.
            return string.IsNullOrEmpty(from) && string.IsNullOrEmpty(to);
        if (from.Length == 8 && string.CompareOrdinal(raw, from) < 0) return false;
        if (to.Length == 8 && string.CompareOrdinal(raw, to) > 0) return false;
        return true;
    }

    /// <summary>Port of the page's parseCSV(): logical lines, delimiter detect, quoted fields.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var lines = new List<string>();
        bool inQuotes = false;
        var cur = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cur.Append('"'); i++; }
                    else { inQuotes = false; }
                }
                else { cur.Append(ch); }
            }
            else
            {
                if (ch == '"') { inQuotes = true; }
                else if (ch == '\n') { if (cur.Length > 0 || lines.Count > 0) lines.Add(cur.ToString()); cur.Clear(); }
                else if (ch == '\r') { /* skip */ }
                else { cur.Append(ch); }
            }
        }
        if (cur.Length > 0 || lines.Count > 0) lines.Add(cur.ToString());

        if (lines.Count == 0) return new List<List<string>>();

        var delimiter = DetectDelimiter(lines[0]);
        var result = new List<List<string>>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Trim() == "" && result.Count > 0) continue;
            var fields = SplitRow(line, delimiter);
            bool hasContent = false;
            for (int i = 0; i < fields.Count; i++)
                if (fields[i].Trim() != "") { hasContent = true; break; }
            if (fields.Count > 0 && hasContent) result.Add(fields);
        }
        return result;
    }

    private static char DetectDelimiter(string firstLine)
    {
        char[] delims = { ',', '\t', '|', ';' };
        int max = -1;
        char best = ',';
        foreach (var d in delims)
        {
            int c = 0;
            for (int i = 0; i < firstLine.Length; i++)
                if (firstLine[i] == d) c++;
            if (c > max) { max = c; best = d; }
        }
        return best;
    }

    private static List<string> SplitRow(string line, char delimiter)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQ = false;
        for (int ci = 0; ci < line.Length; ci++)
        {
            char c = line[ci];
            if (inQ)
            {
                if (c == '"')
                {
                    if (ci + 1 < line.Length && line[ci + 1] == '"') { sb.Append('"'); ci++; }
                    else { inQ = false; }
                }
                else { sb.Append(c); }
            }
            else
            {
                if (c == '"') { inQ = true; }
                else if (c == delimiter) { fields.Add(sb.ToString()); sb.Clear(); }
                else { sb.Append(c); }
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static int FindColumnIndex(List<string> headers, string[] keywords)
    {
        var lower = new string[headers.Count];
        for (int i = 0; i < headers.Count; i++) lower[i] = headers[i].ToLowerInvariant().Trim();
        foreach (var kw in keywords)
            for (int i = 0; i < lower.Length; i++)
                if (lower[i] == kw) return i;
        foreach (var kw in keywords)
            for (int i2 = 0; i2 < lower.Length; i2++)
                if (lower[i2].Contains(kw, StringComparison.Ordinal)) return i2;
        return -1;
    }

    /// <summary>Port of the page's extractStatus(rows): header row[1], data row[2].</summary>
    private static string ExtractStatus(List<List<string>> rows)
    {
        var header = rows.Count > 1 ? rows[1] : new List<string>();
        var data = rows.Count > 2 ? rows[2] : new List<string>();
        int statusIdx = -1;
        for (int i = 0; i < header.Count; i++)
            if (header[i].Trim().ToLowerInvariant() == "status") { statusIdx = i; break; }
        if (statusIdx >= 0 && statusIdx < data.Count)
        {
            var v = data[statusIdx].Trim();
            if (v != "") return v;
        }
        if (data.Count > 8 && data[8].Trim() != "") return data[8].Trim();
        if (data.Count > 9 && data[9].Trim() != "") return data[9].Trim();
        return "";
    }

    /// <summary>
    /// Port of the page's slot logic: first non-empty value in the slot column
    /// (found via SLOT_KEYWORDS on the header), else a "SLOT N" filename match.
    /// </summary>
    private static string FindSlot(List<List<string>> rows, string name)
    {
        int slotIdx = rows.Count > 0 ? FindColumnIndex(rows[0], SlotKeywords) : -1;
        if (slotIdx >= 0)
        {
            for (int ri = 1; ri < rows.Count; ri++)
            {
                if (slotIdx < rows[ri].Count)
                {
                    var sv = rows[ri][slotIdx].Trim();
                    if (sv != "") return sv;
                }
            }
        }
        var m = Regex.Match(name, @"[Ss][Ll][Oo][Tt]\s*(\d+)");
        if (m.Success) return m.Groups[1].Value;
        return "";
    }

    /// <summary>Port of the page's extractDateTimeKey(filename) -> YYYYMMDDHHMMSS-ish key.</summary>
    private static string ExtractDateTimeKey(string filename)
    {
        var n = Regex.Replace(filename, "\\.csv$", "", RegexOptions.IgnoreCase);
        var m = Regex.Match(n, @"(\d{4})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})");
        if (m.Success) return m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + m.Groups[4].Value + m.Groups[5].Value + m.Groups[6].Value;
        var m2 = Regex.Match(n, @"(\d{4})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})");
        if (m2.Success) return m2.Groups[1].Value + m2.Groups[2].Value + m2.Groups[3].Value + "0000";
        var m3 = Regex.Match(n, @"(\d{4})[-_]?(\d{2})[-_]?(\d{2})");
        if (m3.Success) return m3.Groups[1].Value + m3.Groups[2].Value + m3.Groups[3].Value + "000000";
        return "";
    }

    /// <summary>Port of the page's extractDateTimeCode(filename) -> "YYYY-MM-DD HH:MM[:SS]".</summary>
    private static string ExtractDateTimeCode(string filename)
    {
        var n = Regex.Replace(filename, "\\.csv$", "", RegexOptions.IgnoreCase);
        var m = Regex.Match(n, @"(\d{4})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})");
        if (m.Success)
            return string.Format("{0}-{1}-{2} {3}:{4}:{5}", m.Groups[1], m.Groups[2], m.Groups[3], m.Groups[4], m.Groups[5], m.Groups[6]);
        m = Regex.Match(n, @"(\d{4})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})[-_]?(\d{2})");
        if (m.Success)
            return string.Format("{0}-{1}-{2} {3}:{4}", m.Groups[1], m.Groups[2], m.Groups[3], m.Groups[4], m.Groups[5]);
        m = Regex.Match(n, @"(\d{4})[-_]?(\d{2})[-_]?(\d{2})");
        if (m.Success)
            return string.Format("{0}-{1}-{2}", m.Groups[1], m.Groups[2], m.Groups[3]);
        m = Regex.Match(n, @"(\d{2})[-_]?(\d{2})[-_]?(\d{4})");
        if (m.Success)
            return string.Format("{0}-{1}-{2}", m.Groups[3], m.Groups[1], m.Groups[2]);
        return "";
    }

    private static string JsonEscape(string s)
        => JsonSerializer.Serialize(s);

    /// <summary>
    /// Launches a sibling tool EXE that lives next to the current one
    /// (distribution layout: all EXEs at the same root). The child is told —
    /// via <see cref="HostOptions.ReserveLeftEnvVar"/> — how wide this window
    /// is, so corner-docked launchers never get covered. Returns "ok" or an
    /// error description; "" means the EXE was not found.
    /// </summary>
    public string LaunchExe(string exeName)
    {
        var name = Path.GetFileName(exeName ?? ""); // reject any path component
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "invalid";
        var full = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(full)) return "";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(full) { UseShellExecute = false };
            psi.EnvironmentVariables[HostOptions.ReserveLeftEnvVar] = GetOwnerWidth().ToString();
            System.Diagnostics.Process.Start(psi);
            return "ok";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private int GetOwnerWidth()
    {
        try
        {
            if (_owner is null) return 0;
            // A collapsed launcher still reserves its full width so siblings
            // never land under it once it expands again.
            if (_owner.InvokeRequired)
                return (int)_owner.Invoke(new Func<int>(() =>
                    _owner is HostForm hf ? hf.LaunchReserveWidth : _owner.Width));
            return _owner is HostForm hf2 ? hf2.LaunchReserveWidth : _owner.Width;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Lets a page tell the host it is busy (e.g. an in-page overlay open for
    /// typing) so edge auto-hide stays expanded until <c>false</c> is passed.
    /// Additive; no effect on tools without auto-hide.
    /// </summary>
    public void SetBusy(bool busy)
    {
        try { if (_owner is HostForm hf) hf.PageBusy = busy; }
        catch { /* best-effort */ }
    }

    // ---- internals ----------------------------------------------------------

    private string RunDialog(Func<string> action)
    {
        // A native modal dialog open means the user is interacting: keep edge
        // auto-hide expanded until it closes.
        var hf = _owner as HostForm;
        if (hf is not null) Interlocked.Increment(ref hf.AutoHideSuppress);
        try
        {
            if (_owner is not null && _owner.InvokeRequired)
                return (string)_owner.Invoke(action);
            return action();
        }
        catch
        {
            return "";
        }
        finally
        {
            if (hf is not null) Interlocked.Decrement(ref hf.AutoHideSuppress);
        }
    }
}
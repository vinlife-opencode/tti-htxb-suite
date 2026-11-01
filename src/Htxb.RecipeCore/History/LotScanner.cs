using System.Globalization;
using System.Text.RegularExpressions;
using Htxb.RecipeCore.Adapters;
using Htxb.RecipeCore.Ingest;
using Htxb.RecipeCore.Model;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.History;

/// <summary>One lot found in the processed folders, as the history report states it.</summary>
public sealed record LotHistory
{
    /// <summary>Folder name without the <c>_CSV</c> suffix, e.g. <c>10110904594401KA_20261031224911</c>.</summary>
    public required string Folder { get; init; }

    /// <summary>The 14-digit lot id, e.g. <c>10110904594401KA</c>.</summary>
    public required string LotId { get; init; }

    /// <summary>The 14-digit run stamp from the folder name.</summary>
    public required string Stamp { get; init; }

    /// <summary>The vendor's machine id, as the log itself reports it (<c>T0010</c>).</summary>
    public string Machine { get; init; } = string.Empty;

    /// <summary>The site id the report writes (<c>TTSZ-2101</c>), from <c>config.toml</c>.</summary>
    public string Site { get; init; } = string.Empty;

    public string LotNumber { get; init; } = string.Empty;
    public string StartTime { get; init; } = string.Empty;

    /// <summary>Creation time of <c>1_Log_Final\&lt;lot&gt;</c>, local time, or empty.</summary>
    public string CreatedFinal { get; init; } = string.Empty;

    /// <summary>Creation time of <c>3_Log Archive\&lt;lot&gt;_CSV</c>, local time, or empty.</summary>
    public string CreatedArchive { get; init; } = string.Empty;

    public int Slots { get; init; }
    public int Completed { get; init; }
    public int ManualStop { get; init; }
    public int OtherStatus { get; init; }

    /// <summary><c>Completed</c>, <c>Manual Stop</c>, a mix, or <c>unknown</c>.</summary>
    public string Status { get; init; } = "unknown";

    public string Recipe { get; init; } = string.Empty;

    /// <summary><c>yes</c> / <c>no</c> - is the recipe a row of the profile CSV?</summary>
    public string RecipeInProfile { get; init; } = "no";

    /// <summary><c>full</c> (BiDF + summaries), <c>report-only</c> (RPT + total summary only) or <c>none</c>.</summary>
    public string Outputs { get; init; } = "none";

    public int BidfFiles { get; init; }
    public int SummaryFiles { get; init; }

    /// <summary>
    /// <c>current</c>, <c>stale</c> (the raw CSV was produced after the output),
    /// <c>not processed</c> or <c>raw missing</c>.
    /// </summary>
    public required string State { get; init; }

    public string Note { get; init; } = string.Empty;

    /// <summary>The report file this lot gets: <c>&lt;stamp&gt;_&lt;site&gt;_&lt;lot&gt;.csv</c>.</summary>
    public string ReportName
    {
        get
        {
            string site = Safe(Site.Length > 0 ? Site : Machine);
            if (site.Length == 0) site = "unknown-machine";
            return $"{Stamp}_{site}_{LotId}.csv";
        }
    }

    private static string Safe(string text) =>
        new(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());

    /// <summary>Header of the report file, in column order.</summary>
    public static readonly string[] Columns =
    [
        "lot_folder", "lot_id", "stamp", "machine", "site", "lot_number", "start_time",
        "created_final", "created_archive", "slots", "completed", "manual_stop",
        "other_status", "status", "recipe", "recipe_in_profile", "outputs",
        "bidf_files", "summary_files", "state", "note",
    ];

    public IReadOnlyList<string> Cells() =>
    [
        Folder, LotId, Stamp, Machine, Site, LotNumber, StartTime,
        CreatedFinal, CreatedArchive,
        Slots.ToString(CultureInfo.InvariantCulture),
        Completed.ToString(CultureInfo.InvariantCulture),
        ManualStop.ToString(CultureInfo.InvariantCulture),
        OtherStatus.ToString(CultureInfo.InvariantCulture),
        Status, Recipe, RecipeInProfile, Outputs,
        BidfFiles.ToString(CultureInfo.InvariantCulture),
        SummaryFiles.ToString(CultureInfo.InvariantCulture),
        State, Note,
    ];
}

/// <summary>Where the two folders are, and how to read the site id and the profile list.</summary>
public sealed record HistoryOptions
{
    public required string FinalDirectory { get; init; }
    public required string ArchiveDirectory { get; init; }

    /// <summary>Folder holding <c>Std_*.xlsx</c> and the profile CSV.</summary>
    public required string TablesDirectory { get; init; }

    /// <summary><c>config.toml</c> to read the site id (<c>[plant] machine_id</c>) from.</summary>
    public string ConfigPath { get; init; } = string.Empty;
}

/// <summary>
/// Reads one lot of the processed history: what ran, on which machine, how many
/// slots, whether it finished, what the watcher produced for it, and whether that
/// output still belongs to the raw CSV beside it.
///
/// One lot per call on purpose - the caller runs it once per CSV folder, so a lot
/// that cannot be read fails on its own instead of taking a sweep down with it.
/// </summary>
public static class LotScanner
{
    /// <summary>
    /// <c>&lt;14 digits&gt;&lt;2 letters&gt;_&lt;14 digits&gt;</c>, optionally with the
    /// <c>_CSV</c> suffix and a duplicate's " 2".
    /// </summary>
    public static readonly Regex FolderPattern = new(
        @"^(?<lot>\d{14}[A-Za-z]{2})_(?<stamp>\d{14})(?:_CSV)?(?: \d+)?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SlotFileName = new(
        @"^(?<recipe>.+?)_SLOT(?<slot>\d+)_(?<stamp>\d{14})(?:_Rpt)?\.(?:csv|xlsx)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Resolve the lot from either side of the pair - the processed folder or its
    /// <c>_CSV</c> sibling - and read both.
    /// </summary>
    public static LotHistory Scan(string lotPath, HistoryOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lotPath);
        ArgumentNullException.ThrowIfNull(options);

        string name = Path.GetFileName(Path.GetFullPath(lotPath).TrimEnd(Path.DirectorySeparatorChar));
        Match match = FolderPattern.Match(name);
        if (!match.Success)
            throw new InvalidDataException(
                $"'{name}' is not a lot folder (expected <14 digits><2 letters>_<14 digits>, " +
                "optionally with _CSV).");

        string lotId = match.Groups["lot"].Value;
        string stamp = match.Groups["stamp"].Value;
        string folder = $"{lotId}_{stamp}";

        string finalPath = Path.Combine(options.FinalDirectory, folder);
        bool hasFinal = Directory.Exists(finalPath);

        var archiveFolders = Directory.Exists(options.ArchiveDirectory)
            ? Directory.GetDirectories(options.ArchiveDirectory, folder + "_CSV*")
                .Where(p => FolderPattern.IsMatch(Path.GetFileName(p)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        string? archivePath = archiveFolders.FirstOrDefault();
        bool hasArchive = archivePath is not null;

        if (!hasFinal && !hasArchive)
            throw new InvalidDataException($"neither '{folder}' nor '{folder}_CSV' was found.");

        var notes = new List<string>();
        if (archiveFolders.Count > 1)
            notes.Add("duplicate folder present: " +
                      string.Join(", ", archiveFolders.Select(p => Path.GetFileName(p))));

        // The raw log is the tester's own record: it is where the machine id, the
        // lot number and the Status live. Without it the lot is only half known.
        var slots = hasArchive ? SlotFiles(archivePath!, "*.csv") : SlotFiles(finalPath, "*_Rpt.xlsx");
        if (slots.Count == 0 && hasFinal) slots = SlotFiles(finalPath, "*_Rpt.xlsx");
        if (slots.Count == 0)
            throw new InvalidDataException($"'{folder}' holds no slot log.");

        int completed = 0, manual = 0, other = 0;
        string machine = string.Empty, lotNumber = string.Empty, startTime = string.Empty;
        foreach (string slot in slots)
        {
            SlotCsvReader.SlotHeader header;
            try { header = ReadHeader(slot); }
            catch (InvalidDataException error)
            {
                // A slot log we cannot read is counted and named, never silently lost:
                // "why is this lot 23 slots?" has to be answerable from the report.
                other++;
                notes.Add($"{Path.GetFileName(slot)}: {error.Message}");
                continue;
            }

            if (machine.Length == 0) machine = header.MachineId;
            if (lotNumber.Length == 0) lotNumber = header.LotNumber;
            if (startTime.Length == 0) startTime = header.StartTime;

            string status = header.Status;
            if (status.StartsWith("Completed", StringComparison.OrdinalIgnoreCase)) completed++;
            else if (status.StartsWith("Manual Stop", StringComparison.OrdinalIgnoreCase)) manual++;
            else other++;
        }

        string status2 = manual == 0 && other == 0 ? "Completed"
            : completed == 0 && other == 0 ? "Manual Stop"
            : $"{completed} completed / {manual} manual stop / {other} other";

        string recipe = RecipeOf(slots[0]);
        int bidf = hasFinal ? CountFiles(Path.Combine(finalPath, "_BiDF"), "*.bidf") : 0;
        int summaries = hasFinal ? CountFiles(Path.Combine(finalPath, "_Slots_Summary"), "*.txt") : 0;
        string outputs = !hasFinal ? "none" : bidf > 0 ? "full" : "report-only";

        string createdFinal = hasFinal ? TimeOf(finalPath) : string.Empty;
        string createdArchive = hasArchive ? TimeOf(archivePath!) : string.Empty;

        string state =
            !hasFinal ? "not processed"
            : !hasArchive ? "raw missing"
            : ArchiveNewer(archivePath!, finalPath) ? "stale"
            : "current";

        if (state == "raw missing") notes.Add("no raw CSV folder for this lot");
        if (state == "stale") notes.Add("the raw CSV was produced after the output - the output predates it");

        (string configSite, string siteNote) = SiteId(options);
        if (siteNote.Length > 0) notes.Add(siteNote);

        // The report ours wrote carries the site id that was configured when the lot
        // ran. Prefer it: that is literally "the site id our report writes", and it
        // is right even when the setting has been changed since.
        string site = configSite;
        if (hasFinal)
        {
            string? rpt = FirstSlotFile(finalPath, "*_Rpt.xlsx");
            string reported = rpt is null ? string.Empty : SiteIdFromReport(rpt);
            if (reported.Length > 0)
            {
                if (configSite.Length > 0 && !string.Equals(configSite, reported, StringComparison.Ordinal))
                    notes.Add($"config.toml says {configSite}; the report says {reported}");
                site = reported;
            }
        }
        else
        {
            // Never processed, so this lot has no report to read the id from. The site
            // id is one value for the whole site, so take it from a lot that does have
            // one - otherwise the file names of the missing lots would disagree with
            // every other file in the same folder.
            string? anyReport = FirstReportAnywhere(options.FinalDirectory);
            string reported = anyReport is null ? string.Empty : SiteIdFromReport(anyReport);
            if (reported.Length > 0)
            {
                if (configSite.Length > 0 && !string.Equals(configSite, reported, StringComparison.Ordinal))
                    notes.Add($"site id taken from another lot's report ({reported}); config.toml says {configSite}");
                site = reported;
            }
        }

        string inProfile = "no";
        if (recipe.Length > 0 && options.TablesDirectory.Length > 0)
        {
            string csv = ProfileCsvAdapter.PathForTables(options.TablesDirectory);
            if (File.Exists(csv))
                inProfile = ProfileCsvAdapter.Find(ProfileCsvAdapter.Read(csv), recipe) is null ? "no" : "yes";
            else
                notes.Add("profile CSV not found");
        }

        return new LotHistory
        {
            Folder = folder,
            LotId = lotId,
            Stamp = stamp,
            Machine = machine,
            Site = site,
            LotNumber = lotNumber,
            StartTime = startTime,
            CreatedFinal = createdFinal,
            CreatedArchive = createdArchive,
            Slots = slots.Count,
            Completed = completed,
            ManualStop = manual,
            OtherStatus = other,
            Status = status2,
            Recipe = recipe,
            RecipeInProfile = inProfile,
            Outputs = outputs,
            BidfFiles = bidf,
            SummaryFiles = summaries,
            State = state,
            Note = string.Join("; ", notes),
        };
    }

    /// <summary>Slot logs of one lot: <c>SlotNN</c> folders holding a matching file.</summary>
    private static List<string> SlotFiles(string lotPath, string pattern)
    {
        if (!Directory.Exists(lotPath)) return [];

        return Directory.GetDirectories(lotPath, "Slot*")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .SelectMany(d => Directory.GetFiles(d, pattern)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(1))
            .ToList();
    }

    /// <summary>The first matching file under the lot's first slot folder, or null.</summary>
    private static string? FirstSlotFile(string lotPath, string pattern) =>
        SlotFiles(lotPath, pattern).FirstOrDefault();

    /// <summary>Any slot RPT in the processed folder - used only to learn the site id.</summary>
    private static string? FirstReportAnywhere(string finalDirectory)
    {
        if (!Directory.Exists(finalDirectory)) return null;

        foreach (string lot in Directory.GetDirectories(finalDirectory)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string? report = FirstSlotFile(lot, "*_Rpt.xlsx");
            if (report is not null) return report;
        }
        return null;
    }

    /// <summary>The recipe from a slot file name: <c>P_TM_BRF9_B_05_4_32_SLOT1_2026….csv</c>.</summary>
    private static string RecipeOf(string slotFile)
    {
        Match match = SlotFileName.Match(Path.GetFileName(slotFile));
        return match.Success ? match.Groups["recipe"].Value : string.Empty;
    }

    /// <summary>
    /// The <c>Machine ID</c> our own report wrote into a slot RPT: the site id as it
    /// was configured when that lot ran. Read from the sheet rather than guessed,
    /// because the setting can be changed afterwards (it is, on this site).
    /// </summary>
    public static string SiteIdFromReport(string reportPath)
    {
        try { return HeaderFromReport(reportPath).MachineId; }
        catch (Exception) { return string.Empty; }
    }

    /// <summary>A slot log's <c>System Information</c> row, whatever the file type is.</summary>
    private static SlotCsvReader.SlotHeader ReadHeader(string path) =>
        path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? HeaderFromReport(path)
            : SlotCsvReader.ReadSlotHeader(path);

    /// <summary>
    /// The <c>System Information</c> block of a slot RPT. The lot's own report is the
    /// only record left when the raw CSV has been deleted, so it has to be readable.
    /// </summary>
    private static SlotCsvReader.SlotHeader HeaderFromReport(string reportPath)
    {
        using var workbook = XlsxWorkbook.Open(reportPath);
        var sheet = workbook.GetSheet(workbook.SheetNames[0]);

        int headerRow = -1;
        for (int row = 1; row <= Math.Min(sheet.RowCount, 40) && headerRow < 0; row++)
        {
            for (int column = 1; column <= sheet.ColumnCount; column++)
            {
                if (Profile.Squash(sheet.TextOrEmpty(row, column)) != "machineid") continue;
                headerRow = row;
                break;
            }
        }
        if (headerRow < 0)
            throw new InvalidDataException($"{Path.GetFileName(reportPath)} has no 'Machine ID' header.");

        var heads = new List<string>();
        for (int column = 1; column <= sheet.ColumnCount; column++)
            heads.Add(Profile.Squash(sheet.TextOrEmpty(headerRow, column)));

        string[] values = [];
        for (int row = headerRow + 1; row <= Math.Min(sheet.RowCount, headerRow + 3); row++)
        {
            var cells = new string[heads.Count];
            for (int i = 0; i < heads.Count; i++) cells[i] = sheet.TextOrEmpty(row, i + 1);
            if (cells.Any(c => c.Length > 0)) { values = cells; break; }
        }

        string Get(string name)
        {
            int index = heads.IndexOf(Profile.Squash(name));
            return index >= 0 && index < values.Length ? values[index].Trim() : string.Empty;
        }

        return new SlotCsvReader.SlotHeader(
            Get("Machine ID"), Get("Machine Type"), Get("Position"), Get("Lot Number"),
            Get("Start Time"), Get("Board ID"), Get("Status"));
    }


    private static int CountFiles(string folder, string pattern) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).Length : 0;

    private static string TimeOf(string folder) =>
        Directory.GetCreationTime(folder).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// The raw side is "newer" only when it is clearly newer. Copying both folders
    /// from one backup gives them creation times seconds apart in either order
    /// (measured on this site: 08:39:56 and 08:40:13 for the whole tree), and that
    /// is not a re-produced lot. Six hours is far longer than any copy and far
    /// shorter than a re-run days later.
    /// </summary>
    private static readonly TimeSpan StaleTolerance = TimeSpan.FromHours(6);

    private static bool ArchiveNewer(string archive, string final) =>
        Directory.GetCreationTime(archive) - Directory.GetCreationTime(final) > StaleTolerance;

    /// <summary>
    /// The site id the report writes: <c>[plant] machine_id</c> from config.toml.
    /// Falls back to the log's own machine id, and says so, rather than inventing one.
    /// </summary>
    public static (string Site, string Note) SiteId(HistoryOptions options)
    {
        string path = options.ConfigPath;
        if (path.Length == 0 || !File.Exists(path)) return (string.Empty, "config.toml not found - site id from the log");

        string section = string.Empty;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (!section.Equals("plant", StringComparison.OrdinalIgnoreCase)) continue;
            int equals = line.IndexOf('=');
            if (equals < 0) continue;
            if (!line[..equals].Trim().Equals("machine_id", StringComparison.OrdinalIgnoreCase)) continue;

            string value = TomlString(line[(equals + 1)..]);
            return value.Length > 0 ? (value, string.Empty)
                                    : (string.Empty, "config.toml has no [plant] machine_id");
        }

        return (string.Empty, "config.toml has no [plant] machine_id");
    }

    /// <summary>
    /// The value of a TOML scalar, without its trailing comment.
    /// <c>machine_id = "TTSZ-2101"   # displayed in the report "Machine ID"</c> must
    /// yield <c>TTSZ-2101</c>. Stripping quotes from the whole remainder instead put
    /// the comment - quotes, '#' and all - into a file name.
    /// </summary>
    private static string TomlString(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return string.Empty;

        char quote = trimmed[0];
        if (quote is '"' or '\'')
        {
            int end = trimmed.IndexOf(quote, 1);
            return end > 1 ? trimmed[1..end] : string.Empty;
        }

        int hash = trimmed.IndexOf('#');
        return (hash >= 0 ? trimmed[..hash] : trimmed).Trim();
    }
}

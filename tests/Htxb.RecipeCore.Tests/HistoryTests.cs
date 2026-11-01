using Htxb.RecipeCore.History;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Tests;

/// <summary>
/// Checks for <c>history report</c>: one lot per process, one CSV per lot.
///
/// The synthetic cases build a small processed/archive pair in a temp folder, so
/// every state (current, stale, not processed, raw missing) and the duplicate
/// folder rule are proved without the site's own data. The real section, when the
/// site folders are on this machine, pins what the 22 archive folders yield today.
/// </summary>
internal static class HistoryTests
{
    private const string Recipe = "P_TM_SQFJ_B_03_4_5";
    private const string Lot = "10110904594401KA";
    private const string Stamp = "20261031224911";

    public static void Register(Runner runner, string reportsRoot)
    {
        Log.Section("history report");
        RegisterRules(runner);
        RegisterReal(runner, reportsRoot);
    }

    // ------------------------------------------------------------------- rules

    private static void RegisterRules(Runner runner)
    {
        runner.Check("a lot folder name is recognised, with or without _CSV", () =>
        {
            Assert.True(LotScanner.FolderPattern.IsMatch($"{Lot}_{Stamp}"), "plain folder");
            Assert.True(LotScanner.FolderPattern.IsMatch($"{Lot}_{Stamp}_CSV"), "_CSV sibling");
            Assert.True(LotScanner.FolderPattern.IsMatch($"{Lot}_{Stamp}_CSV 2"), "duplicate folder");
            Assert.True(!LotScanner.FolderPattern.IsMatch("Slot01"), "Slot01 is not a lot");
            Assert.True(!LotScanner.FolderPattern.IsMatch("BRF9-Test2_20261031153345"), "an engineering lot");
        });

        runner.Check("the report of a processed lot: current, full, completed", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 3, summaries: 3);
            LotHistory lot = site.Scan();

            // The fixture must be readable before the lot is judged: if this line is
            // what fails, the problem is the fixture, not the scanner.
            string firstSlot = System.IO.Directory.GetFiles(
                site.ArchivePath(Lot, Stamp), "*.csv", SearchOption.AllDirectories)[0];
            Assert.Equal("Completed",
                Htxb.RecipeCore.Ingest.SlotCsvReader.ReadSlotHeader(firstSlot).Status,
                "fixture slot status");

            Assert.True(lot.OtherStatus == 0, $"unreadable slot logs: {lot.Note}");
            Assert.Equal("current", lot.State, "state");
            Assert.Equal("full", lot.Outputs, "outputs");
            Assert.Equal("Completed", lot.Status, "status");
            Assert.Equal(2, lot.Slots, "slots");
            Assert.Equal(Recipe, lot.Recipe, "recipe");
            Assert.Equal("yes", lot.RecipeInProfile, "recipe in profile");
            Assert.Equal("T0010", lot.Machine, "machine id from the log");
            Assert.Equal("TTSZ-2101", lot.Site, "site id from our report");
            Assert.Equal($"{Stamp}_TTSZ-2101_{Lot}.csv", lot.ReportName, "file name");
        });

        runner.Check("a lot the watcher skipped is reported as not processed", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Manual Stop", withFinal: false, withArchive: true, bidf: 0, summaries: 0);
            LotHistory lot = site.Scan();

            Assert.Equal("not processed", lot.State, "state");
            Assert.Equal("none", lot.Outputs, "outputs");
            Assert.Equal("Manual Stop", lot.Status, "status");
            Assert.Equal(0, lot.Completed, "completed count");
            Assert.Equal(2, lot.ManualStop, "manual stop count");
        });

        runner.Check("a report-only lot is visible as such", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 0, summaries: 0);
            LotHistory lot = site.Scan();

            Assert.Equal("report-only", lot.Outputs, "outputs");
            Assert.Equal(0, lot.BidfFiles, "BiDF files");
            Assert.Equal("current", lot.State, "state");
        });

        runner.Check("a raw CSV produced long after the output marks the lot stale", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 3, summaries: 3);
            Directory.SetCreationTime(site.ArchivePath(Lot, Stamp), DateTime.Now);
            Directory.SetCreationTime(site.FinalPath(Lot, Stamp), DateTime.Now.AddDays(-3));
            LotHistory lot = site.Scan();

            Assert.Equal("stale", lot.State, "state");
            Assert.True(lot.Note.Contains("predates", StringComparison.Ordinal), lot.Note);
        });

        runner.Check("a copy-order difference of seconds is not stale", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 3, summaries: 3);
            // measured on the site: the two folders were copied 17 s apart
            Directory.SetCreationTime(site.ArchivePath(Lot, Stamp), DateTime.Now);
            Directory.SetCreationTime(site.FinalPath(Lot, Stamp), DateTime.Now.AddSeconds(-17));
            Assert.Equal("current", site.Scan().State, "state");
        });

        runner.Check("an output with its raw CSV deleted is reported as raw missing", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: false, bidf: 3, summaries: 3);
            LotHistory lot = site.Scan();

            Assert.Equal("raw missing", lot.State, "state");
            Assert.Equal(2, lot.Slots, "slots still counted from the reports");
            Assert.Equal("Completed", lot.Status, "status read from the report");
        });

        runner.Check("a duplicate archive folder is noted, one report is written", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 3, summaries: 3);
            Directory.CreateDirectory(site.ArchivePath(Lot, Stamp) + " 2");

            LotHistory lot = site.Scan();
            Assert.True(lot.Note.Contains("duplicate folder present", StringComparison.Ordinal), lot.Note);
            Assert.Equal($"{Stamp}_TTSZ-2101_{Lot}.csv", lot.ReportName, "file name");
        });

        runner.Check("the writer creates the folder and quotes only when it must", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 3, summaries: 3);
            string outDir = Path.Combine(site.Root, "reports", "nested");
            string path = LotReportWriter.Write(site.Scan(), outDir);

            Assert.True(File.Exists(path), $"not written: {path}");
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length, "header + one row");
            Assert.Equal(string.Join(',', LotHistory.Columns), lines[0], "header");
            Assert.Equal(LotHistory.Columns.Length, lines[1].Split(',').Length, "cell count");
        });

        runner.Check("a folder that is not a lot is refused", () =>
        {
            using var site = new Site();
            string folder = Path.Combine(site.Root, "Slot01");
            Directory.CreateDirectory(folder);
            bool refused = false;
            try { LotScanner.Scan(folder, site.Options); }
            catch (InvalidDataException) { refused = true; }
            Assert.True(refused, "a non-lot folder must be refused, not reported");
        });

        // -- recovery ----------------------------------------------------------
        runner.Check("only a Completed lot without a BiDF can be recovered", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 0, summaries: 0);
            Assert.True(LotRecovery.CanRecover(site.Scan(), site.Options.ArchiveDirectory, site.ProcessPath).Can,
                "a report-only lot should be recoverable");

            var full = new Site();
            try
            {
                full.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 2, summaries: 2);
                Assert.True(!LotRecovery.CanRecover(full.Scan(), full.Options.ArchiveDirectory, full.ProcessPath).Can,
                    "a lot with a BiDF must not be offered");

                var stopped = new Site();
                try
                {
                    stopped.AddLot(RawStatus: "Manual Stop", withFinal: false, withArchive: true, bidf: 0, summaries: 0);
                    Assert.True(!LotRecovery.CanRecover(stopped.Scan(), stopped.Options.ArchiveDirectory, stopped.ProcessPath).Can,
                        "a hand-stopped lot must not be offered");
                }
                finally { stopped.Dispose(); }
            }
            finally { full.Dispose(); }
        });

        runner.Check("recovery backs the old output up and stages the raw CSVs", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 0, summaries: 0);
            LotHistory lot = site.Scan();

            string history = Path.Combine(site.Root, "history");
            RecoveryOutcome outcome = LotRecovery.Recover(lot, site.Options.FinalDirectory,
                site.Options.ArchiveDirectory, site.ProcessPath, history, new DateTime(2026, 11, 1, 12, 0, 0));

            Assert.True(!Directory.Exists(site.FinalPath(Lot, Stamp)), "the old output must be gone from 1_Log_Final");
            Assert.True(Directory.Exists(outcome.BackupPath), $"no backup at {outcome.BackupPath}");
            Assert.True(File.Exists(Path.Combine(outcome.BackupPath, "Slot01",
                    $"{Recipe}_SLOT1_{Stamp}_Rpt.xlsx")), "the old report must be in the backup");
            Assert.True(Directory.Exists(site.ArchivePath(Lot, Stamp)), "the archive copy must stay");

            string staged = Path.Combine(site.ProcessPath, $"{Lot}_{Stamp}");
            Assert.Equal(staged, outcome.StagedPath, "staged folder");
            Assert.True(File.Exists(Path.Combine(staged, "Slot01", $"{Recipe}_SLOT1_{Stamp}.csv")),
                "the raw CSV must be in the process folder");
        });

        runner.Check("recovery refuses a lot whose process folder is already taken", () =>
        {
            using var site = new Site();
            site.AddLot(RawStatus: "Completed", withFinal: true, withArchive: true, bidf: 0, summaries: 0);
            LotHistory lot = site.Scan();
            Directory.CreateDirectory(Path.Combine(site.ProcessPath, $"{Lot}_{Stamp}"));

            (bool can, string why) = LotRecovery.CanRecover(lot, site.Options.ArchiveDirectory, site.ProcessPath);
            Assert.True(!can, "must refuse");
            Assert.True(why.Contains("process folder", StringComparison.Ordinal), why);
        });
    }

    // -------------------------------------------------------------------- real

    private static void RegisterReal(Runner runner, string reportsRoot)
    {
        string final = TestPaths.ProcessedLots;
        string archive = TestPaths.ArchivedLots;
        if (!Directory.Exists(final) || !Directory.Exists(archive))
        {
            runner.Skip("history report on the site folders", $"not on this machine: {archive}");
            return;
        }

        runner.Check("every archive folder yields a report, duplicates collapse", () =>
        {
            var options = new HistoryOptions
            {
                FinalDirectory = final,
                ArchiveDirectory = archive,
                TablesDirectory = TestPaths.InstalledTables,
                ConfigPath = TestPaths.InstalledConfig,
            };

            string outDir = Path.Combine(Path.GetTempPath(), $"htxb-hist-{Guid.NewGuid():N}");
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int folders = 0;
                foreach (string folder in Directory.GetDirectories(archive))
                {
                    LotHistory lot = LotScanner.Scan(folder, options);
                    LotReportWriter.Write(lot, outDir);
                    names.Add(lot.ReportName);
                    folders++;
                }

                Assert.True(folders >= 20, $"archive folders: {folders}");
                Assert.Equal(folders - 1, names.Count, "one file per lot (one duplicate folder)");

                var reports = Directory.GetFiles(outDir, "*.csv")
                    .Select(p => File.ReadAllLines(p).Last().Split(','))
                    .ToList();
                int column = Array.IndexOf(LotHistory.Columns, "state");
                Assert.True(column > 0, "state column");

                int current = reports.Count(c => c[column] == "current");
                int notProcessed = reports.Count(c => c[column] == "not processed");
                int reportOnly = reports.Count(c => Array.IndexOf(LotHistory.Columns, "outputs") is var o && c[o] == "report-only");

                Assert.True(current > 0, "some lots are current");
                Assert.True(notProcessed > 0, "the manual-stop lots are reported as not processed");
                Assert.True(reportOnly > 0, "the report-only lots are visible");

                foreach (var cells in reports)
                {
                    int name = Array.IndexOf(LotHistory.Columns, "site");
                    Assert.True(cells[name].Trim('"').Length > 0, "every report names a site");
                }
            }
            finally
            {
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            }
        });
    }

    /// <summary>A throwaway processed/archive pair, shaped like the site's.</summary>
    private sealed class Site : IDisposable
    {
        public Site()
        {
            Root = Path.Combine(Path.GetTempPath(), $"htxb-site-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "final"));
            Directory.CreateDirectory(Path.Combine(Root, "archive"));
            Directory.CreateDirectory(Path.Combine(Root, "tables", "Cust_Profile_Select"));
            File.WriteAllText(Path.Combine(Root, "tables", "Cust_Profile_Select",
                "CustomerProfileSelection.csv"),
                "-,Customer Profile Selection,,,,\n" +
                ",Customer,Recipe Group,FuncLoct,Drift_En,Std_Table\n" +
                $"1,STM,{Recipe},STM-01,0,Std_17_SQFJ_B.xlsx\n");
            File.WriteAllText(Path.Combine(Root, "config.toml"),
                "[plant]\nmachine_id   = \"TTI-0001\"   # a different value on purpose\n");
        }

        public string Root { get; }

        public string FinalPath(string lot, string stamp) => Path.Combine(Root, "final", $"{lot}_{stamp}");

        public string ArchivePath(string lot, string stamp) =>
            Path.Combine(Root, "archive", $"{lot}_{stamp}_CSV");

        /// <summary>The folder the watcher polls.</summary>
        public string ProcessPath => Path.Combine(Root, "process");

        public HistoryOptions Options => new()
        {
            FinalDirectory = Path.Combine(Root, "final"),
            ArchiveDirectory = Path.Combine(Root, "archive"),
            TablesDirectory = Path.Combine(Root, "tables"),
            ConfigPath = Path.Combine(Root, "config.toml"),
        };

        public LotHistory Scan() => LotScanner.Scan(FinalPath(Lot, Stamp), Options);

        /// <summary>Two slots per side, so a count of 2 is meaningful.</summary>
        public void AddLot(string RawStatus, bool withFinal, bool withArchive, int bidf, int summaries)
        {
            if (withArchive)
            {
                for (int slot = 1; slot <= 2; slot++)
                {
                    string dir = Path.Combine(ArchivePath(Lot, Stamp), $"Slot{slot:00}");
                    Directory.CreateDirectory(dir);
                    File.WriteAllLines(Path.Combine(dir, $"{Recipe}_SLOT{slot}_{Stamp}.csv"),
                    [
                        "System Information",
                        "Machine ID,Machine Type,Position,Driver ID,Lot Number,Start Time,Board ID,User ID,Options,Status",
                        $"T0010,COBISIIP2,SLOT {slot},303003,{Lot},31/10/2026 22:49:11,082F,,," +
                        RawStatus,
                        "",
                    ]);
                }
            }

            if (!withFinal) return;

            for (int slot = 1; slot <= 2; slot++)
            {
                string dir = Path.Combine(FinalPath(Lot, Stamp), $"Slot{slot:00}");
                Directory.CreateDirectory(dir);
                XlsxWriter.Write(Path.Combine(dir, $"{Recipe}_SLOT{slot}_{Stamp}_Rpt.xlsx"), "Sheet1",
                [
                    new object?[] { "System Information" },
                    new object?[] { "Machine ID", "Machine Type", "Position", "Driver ID", "Lot Number",
                                    "Start Time", "Board ID", "User ID", "Options", "Status" },
                    new object?[] { "TTSZ-2101", "COBISIIP2", $"SLOT {slot}", "303003", Lot,
                                    "31/10/2026 22:49:11", "082F", "", "", "Completed" },
                ]);
            }

            for (int i = 0; i < bidf; i++)
            {
                string dir = Path.Combine(FinalPath(Lot, Stamp), "_BiDF");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"slot{i + 1}.bidf"), "x");
            }
            for (int i = 0; i < summaries; i++)
            {
                string dir = Path.Combine(FinalPath(Lot, Stamp), "_Slots_Summary");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"Slot{i + 1:00}_Summary.txt"), "x");
            }
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
            catch (IOException) { /* a locked temp folder is not a test failure */ }
        }
    }
}

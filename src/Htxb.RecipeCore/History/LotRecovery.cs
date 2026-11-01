using System.Globalization;

namespace Htxb.RecipeCore.History;

/// <summary>What a recovery did, for the operator and for the log.</summary>
public sealed record RecoveryOutcome(
    string Lot,
    string BackupPath,
    string StagedPath,
    string Note);

/// <summary>
/// Puts a lot the watcher skipped back through it.
///
/// It does not convert anything itself: the conversion lives in the watcher, and the
/// supported way to re-run a lot is to give it back to the watcher. Two steps make
/// that safe on the site's own folders:
///
/// 1. **the old output is moved aside**, into
///    <c>4_Log_Processed_History\&lt;lot&gt;\previous\&lt;stamp&gt;\</c>. It must be a move,
///    not a copy: the watcher writes its output with <c>shutil.move</c> onto the lot
///    name, and a folder already sitting there would be nested inside the new one;
/// 2. **the raw CSVs are copied into the process folder** under the lot name, which
///    is what the watcher polls. The archive copy stays where it is; the watcher's
///    own archive step appends " 2" to a name that is taken, which the history
///    report already reports as a duplicate folder.
/// </summary>
public static class LotRecovery
{
    /// <summary>True when this lot can be recovered right now, and why not when it cannot.</summary>
    public static (bool Can, string Why) CanRecover(LotHistory lot, string archiveDirectory, string processDirectory)
    {
        ArgumentNullException.ThrowIfNull(lot);

        if (lot.State == "not processed")
            return (false, "the run was stopped by hand - the watcher only converts Completed lots");
        if (lot.State == "raw missing")
            return (false, "the raw CSV folder is gone, so there is nothing to re-run");
        if (lot.Outputs == "full")
            return (false, "it already has a BiDF and slot summaries");

        DirectoryInfo? staged = FindArchive(lot, archiveDirectory);
        if (staged is null) return (false, "the raw CSV folder is gone");

        string target = Path.Combine(processDirectory, lot.Folder);
        if (Directory.Exists(target))
            return (false, $"the process folder already holds '{lot.Folder}'");

        return (true, string.Empty);
    }

    /// <summary>
    /// Back the old output up and hand the lot back to the watcher. Nothing is
    /// deleted: the previous output stays on disk under <paramref name="historyDirectory"/>.
    /// </summary>
    public static RecoveryOutcome Recover(LotHistory lot, string finalDirectory, string archiveDirectory,
        string processDirectory, string historyDirectory, DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(lot);
        (bool can, string why) = CanRecover(lot, archiveDirectory, processDirectory);
        if (!can) throw new InvalidOperationException($"{lot.Folder}: {why}");

        string stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string notes = string.Empty;

        // 1. the old output out of the way
        string backup = string.Empty;
        string finalPath = Path.Combine(finalDirectory, lot.Folder);
        if (Directory.Exists(finalPath))
        {
            string perLot = Path.Combine(historyDirectory, lot.Folder);
            backup = Path.Combine(perLot, "previous", stamp);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            Directory.Move(finalPath, backup);
        }
        else
        {
            notes = "no output folder was present; nothing to back up";
        }

        // 2. the raw CSVs back into the polled folder
        DirectoryInfo archive = FindArchive(lot, archiveDirectory)!;
        string staged = Path.Combine(processDirectory, lot.Folder);
        Directory.CreateDirectory(processDirectory);
        CopyTree(archive.FullName, staged);

        return new RecoveryOutcome(lot.Folder, backup, staged,
            $"{lot.Recipe} - copied {Directory.GetDirectories(staged, "Slot*").Length} slot folder(s)" +
            (notes.Length > 0 ? "; " + notes : string.Empty));
    }

    /// <summary>
    /// The archive folder for this lot: its own name first, then a duplicate's
    /// " 2", " 3" - the same order the report uses.
    /// </summary>
    public static DirectoryInfo? FindArchive(LotHistory lot, string archiveDirectory)
    {
        if (!Directory.Exists(archiveDirectory)) return null;

        return Directory.GetDirectories(archiveDirectory, lot.Folder + "_CSV*")
            .Where(p => LotScanner.FolderPattern.IsMatch(Path.GetFileName(p)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new DirectoryInfo(p))
            .FirstOrDefault();
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.GetDirectories(from))
            CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}

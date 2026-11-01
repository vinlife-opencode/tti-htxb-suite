using Htxb.RecipeCore.History;

namespace Htxb.RecipeTool.Commands;

/// <summary>
/// <c>history report</c> - one lot of the processed history, written as one CSV.
///
/// The unit is a single lot: the caller runs this once per CSV folder, so a lot that
/// cannot be read fails on its own instead of taking a sweep down. It reads both
/// sides of the pair (the processed folder and the raw <c>_CSV</c>) because
/// "stale" and "raw missing" are comparisons between them, and it never changes
/// anything - the archive is the site's record.
///
/// Exit codes: 0 written, 1 the lot cannot be read, 2 usage.
/// </summary>
internal static class HistoryCommand
{
    public static int Run(ArgReader args)
    {
        if (args.Positional(0) is not ("report" or ""))
            throw new UsageException("usage: Htxb.RecipeTool history report --lot <folder> [--out DIR]");

        args.RejectUnknown("lot", "out", "final", "archive", "tables", "config");

        string lot = args.Value("lot") ?? args.Positional(1);
        if (lot.Length == 0)
            throw new UsageException(
                "usage: Htxb.RecipeTool history report --lot <processed folder | its _CSV sibling> [--out DIR] " +
                "[--final DIR] [--archive DIR] [--tables DIR] [--config FILE]");

        if (!Directory.Exists(lot)) throw new UsageException($"Lot folder not found: {lot}");

        var options = new HistoryOptions
        {
            FinalDirectory = args.Value("final") ?? ToolPaths.ProcessedLots,
            ArchiveDirectory = args.Value("archive") ?? ToolPaths.ArchivedLots,
            TablesDirectory = args.Value("tables") ?? ToolPaths.InstalledTables,
            ConfigPath = args.Value("config") ?? ToolPaths.InstalledConfig,
        };
        string outDirectory = args.Value("out") ?? ToolPaths.HistoryReports;

        LotHistory found;
        try
        {
            found = LotScanner.Scan(lot, options);
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine("error: " + error.Message);
            return 1;
        }

        string path = LotReportWriter.Write(found, outDirectory);

        Console.WriteLine($"{found.Folder}");
        Console.WriteLine($"  lot id  : {found.LotId}   ({found.Stamp})");
        Console.WriteLine($"  machine : {found.Machine}   site: {(found.Site.Length > 0 ? found.Site : "(from the log)")}");
        Console.WriteLine($"  slots   : {found.Slots}   {found.Status}");
        Console.WriteLine($"  recipe  : {found.Recipe}   in the profile list: {found.RecipeInProfile}");
        Console.WriteLine($"  output  : {found.Outputs} ({found.BidfFiles} BiDF, {found.SummaryFiles} summary)");
        Console.WriteLine($"  state   : {found.State}");
        if (found.Note.Length > 0) Console.WriteLine($"  note    : {found.Note}");
        Console.WriteLine($"  wrote   : {path}");
        return 0;
    }
}

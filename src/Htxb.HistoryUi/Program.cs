using Htxb.RecipeCore.History;

namespace Htxb.HistoryUi;

/// <summary>
/// "Unlisted lots…" - the list of lots the watcher could not convert, and the one
/// button that puts a ticked lot back through it.
///
/// One lot per process stays true for the report; this window is a listener over the
/// folders the report reads, and every recovery is one lot at a time.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = new HistoryOptions
        {
            FinalDirectory = Value(args, "--final") ?? @"D:\COBIS-II-632\COBIS-II\1_Log_Final",
            ArchiveDirectory = Value(args, "--archive") ?? @"D:\COBIS-II-632\COBIS-II\3_Log Archive",
            TablesDirectory = Value(args, "--tables") ?? @"C:\TTI_PostDataProcessor\Configuration\Recipe_Std_Table",
            ConfigPath = Value(args, "--config") ?? @"C:\TTI_PostDataProcessor\config.toml",
        };
        string history = Value(args, "--out") ?? @"D:\COBIS-II-632\COBIS-II\4_Log_Processed_History";
        string process = Value(args, "--process") ?? @"D:\COBIS-II-632\COBIS-II\0_Log_Process";

        Application.Run(new HistoryForm(options, history, process));
    }

    private static string? Value(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}

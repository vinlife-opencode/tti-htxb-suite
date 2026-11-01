namespace Htxb.RecipeCore.Tests;

/// <summary>
/// Locations the runner needs. Overridable from the command line so the same EXE
/// works on the development machine, on a build machine and in a fresh clone.
/// </summary>
internal static class TestPaths
{
    /// <summary>
    /// The shared vector file. It lives in the BidfGen2 repository because the
    /// Python implementation owns the behaviour it describes; the C# side is
    /// pinned to the same file so the two can never drift apart quietly.
    /// </summary>
    public const string DefaultVectors =
        @"D:\_TTI-Dev\_Proj_BidfGen2\docs\test-vectors\normalise.json";

    /// <summary>The STM 261028 delivery, as received (17 workbooks, 4 layout variants).</summary>
    public const string DefaultSamples =
        @"D:\_TTI-Dev\_Proj_BidfGen2\Samples\TTSZ Zhou Rong 261029 New Recipes\261028 TTSZ Zhou Rong 17 Test Plan\Trio-tech_BI_Recipes";

    /// <summary>The tracked std tables and the profile CSV that selects them.</summary>
    public const string DefaultTables =
        @"D:\_TTI-Dev\_Proj_BidfGen2\handover\fixtures\deploy-config\Recipe_Std_Table";

    /// <summary>The machine recipe export that goes with the 261028 delivery.</summary>
    public const string DefaultZadz =
        @"D:\_TTI-Dev\_Proj_BidfGen2\Samples\TTSZ Zhou Rong 261029 New Recipes\261028 TTSZ Zhou Rong 17 Test Plan\Trio-tech_BI_Recipes\2108 Recipe 261031";

    /// <summary>The same recipes, pinned in the repository for a fresh clone.</summary>
    public const string FixtureZadz =
        @"D:\_TTI-Dev\_Proj_BidfGen2\handover\fixtures\samples\TTSZ Zhou Rong 261029 New Recipes\261028 TTSZ Zhou Rong 17 Test Plan\Trio-tech_BI_Recipes\2108 Recipe 261031";

    /// <summary>The alias map shared with the Python tool, asserted by both suites.</summary>
    public const string DefaultAliases =
        @"D:\_TTI-Dev\_Proj_BidfGen2\docs\test-vectors\recipe-aliases.json";

    /// <summary>Where the watcher puts a lot it has processed.</summary>
    public const string ProcessedLots = @"D:\COBIS-II-632\COBIS-II\1_Log_Final";

    /// <summary>Where the watcher archives the raw CSVs of a lot.</summary>
    public const string ArchivedLots = @"D:\COBIS-II-632\COBIS-II\3_Log Archive";

    /// <summary>The installed configuration: the site id the history report prints.</summary>
    public const string InstalledConfig = @"C:\TTI_PostDataProcessor\config.toml";

    /// <summary>
    /// The tables and profile CSV the site actually runs with. Preferred over the
    /// tracked fixture when it is on this machine, because it is the configuration
    /// the checks are about; the fixture is a byte-identical copy for a fresh clone.
    /// `--tables DIR` overrides both.
    /// </summary>
    public const string InstalledTables = @"C:\TTI_PostDataProcessor\Configuration\Recipe_Std_Table";
}

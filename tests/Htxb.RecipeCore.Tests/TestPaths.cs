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
}

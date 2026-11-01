namespace Htxb.RecipeTool;

/// <summary>
/// Default locations, overridable on every command.
///
/// The defaults exist so an operator on this workstation can type
/// <c>Htxb.RecipeTool recipes</c> and get the delivery under test. Anything
/// automated passes paths explicitly.
/// </summary>
internal static class ToolPaths
{
    /// <summary>The STM 261028 delivery, as received.</summary>
    public const string DefaultSamples =
        @"D:\_TTI-Dev\_Proj_BidfGen2\Samples\TTSZ Zhou Rong 261029 New Recipes\261028 TTSZ Zhou Rong 17 Test Plan\Trio-tech_BI_Recipes";

    /// <summary>
    /// The tracked std tables in the BidfGen2 repository. This is the authoritative
    /// copy; the installed one under C:\TTI_PostDataProcessor is a deployed copy of it.
    /// </summary>
    public const string DefaultTables =
        @"D:\_TTI-Dev\_Proj_BidfGen2\handover\fixtures\deploy-config\Recipe_Std_Table";

    /// <summary>
    /// The tables the site actually runs with, plus the profile CSV that names the
    /// table per recipe. Preferred by <c>validate-lot</c>, which is normally pointed
    /// at a lot this machine processed.
    /// </summary>
    public const string InstalledTables = @"C:\TTI_PostDataProcessor\Configuration\Recipe_Std_Table";
}

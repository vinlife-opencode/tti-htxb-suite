namespace Htxb.RecipeCore.Model;

/// <summary>
/// The customer's profile row, read from the workbook's <c>Reference Table</c> tab.
///
/// That tab is row-oriented: headers in row 1, the single value in row 2. It is
/// the only place a delivery states the package code, the CP, the MC code and the
/// stress voltages, and until now nothing in either toolchain read it.
///
/// Several headers are Excel formulas (<c>="HTGB"&amp;"  "&amp;"IGSS "&amp;AC2&amp;"..."</c>);
/// the reader takes Excel's cached result, never a recomputation, so the values
/// match what the customer sees.
/// </summary>
public sealed record Profile
{
    /// <summary>Every header/value pair verbatim, keyed by the squashed header text.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>"Line" - the family token, e.g. <c>BRJ7</c>.</summary>
    public string Line => Field("Line");

    /// <summary>"Nominal BVDSS [V]" - e.g. <c>1200</c>.</summary>
    public string NominalBvdss => Field("Nominal BVDSS [V]");

    /// <summary>"CP" - the DUT, e.g. <c>SCT070W120G3-4AG</c>.</summary>
    public string Cp => Field("CP");

    /// <summary>"Package", e.g. <c>HIP247 4 Lead Long</c>.</summary>
    public string Package => Field("Package");

    /// <summary>"PACKAGE CODE", e.g. <c>A0TM</c>.</summary>
    public string PackageCode => Field("PACKAGE CODE");

    /// <summary>"MC CODE".</summary>
    public string McCode => Field("MC CODE");

    /// <summary>"VDS [V] @ RTRB".</summary>
    public string VdsRtrb => Field("VDS [V] @ RTRB");

    /// <summary>"VDS [V] @ HTRB".</summary>
    public string VdsHtrb => Field("VDS [V] @ HTRB");

    /// <summary>"VGS_HTGB [V]", e.g. <c>+32V</c>.</summary>
    public string VgsHtgb => Field("VGS_HTGB [V]");

    /// <summary>"BI recipe name" - the identity the profile row and CSV carry.</summary>
    public string BiRecipeName => Field("BI recipe name");

    public string Field(string header)
    {
        if (Fields.TryGetValue(Squash(header), out string? value)) return value;
        return string.Empty;
    }

    /// <summary>
    /// Header text with whitespace removed and lower-cased. The customer writes
    /// "HTRB IDSS@140°C  MAX" (two spaces) and "VDS [V] @ RTRB"; comparing squashed
    /// keys means a stray space or a recased header never silently loses a field.
    /// </summary>
    public static string Squash(string text) =>
        new((text ?? string.Empty).Where(ch => !char.IsWhiteSpace(ch)).Select(char.ToLowerInvariant).ToArray());

    public bool IsEmpty => Fields.Count == 0;
}

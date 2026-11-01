using System.Globalization;

namespace Htxb.RecipeCore.Model;

/// <summary>
/// The <c>lsl_num</c> / <c>usl_num</c> a limits-table row will be written with.
///
/// Mirrors <c>bidfgen.stdtable._derive_limits</c> and <c>bidfgen.config.LimitsConfig</c>
/// exactly - it is the engine's rule, so a validator that used a different one would
/// report a difference that does not exist (or miss one that does).
///
/// <list type="bullet">
/// <item>a drift row (<c>&lt;X&gt;_Drift</c>) carries no limit; both are 0;</item>
/// <item>a gate row (<c>IGSS</c>/<c>HTGB</c>/<c>ISGS</c>) is already in nA and is
/// not scaled; a missing minimum becomes <c>-usl</c>;</item>
/// <item>every other row is stated in uA and is multiplied by
/// <see cref="ScaleFactor"/>; a missing minimum becomes
/// <see cref="DefaultLowerLimitScaled"/>.</item>
/// </list>
/// </summary>
public static class StdLimits
{
    public const int ScaleFactor = 1000;
    public const int DefaultLowerLimitScaled = -1000;

    public static readonly string[] GateKeywords = ["IGSS", "HTGB", "ISGS"];

    public static bool IsDrift(string? name) =>
        (name ?? string.Empty).ToUpperInvariant().Contains("DRIFT", StringComparison.Ordinal);

    public static bool IsGate(string? name)
    {
        string upper = (name ?? string.Empty).ToUpperInvariant();
        return GateKeywords.Any(k => upper.Contains(k, StringComparison.Ordinal));
    }

    /// <summary>The factor the engine applies to this row's stated limits.</summary>
    public static int Factor(string? name) => IsGate(name) ? 1 : ScaleFactor;

    /// <summary>(lsl, usl) for one table row, in the unit the BiDF stores.</summary>
    public static (long Lsl, long Usl) For(StdTest test) => For(test.Name, test.Min, test.Max);

    public static (long Lsl, long Usl) For(string? name, string? min, string? max)
    {
        if (IsDrift(name)) return (0, 0);

        bool gate = IsGate(name);
        int factor = gate ? 1 : ScaleFactor;

        long usl = Parse(max) is { } high ? Round(high * factor) : 0;
        long lsl = Parse(min) is { } low ? Round(low * factor) : (gate ? -usl : DefaultLowerLimitScaled);
        return (lsl, usl);
    }

    /// <summary>Python's <c>round()</c>: nearest, ties to even.</summary>
    public static long Round(double value) => (long)Math.Round(value, MidpointRounding.ToEven);

    private static double? Parse(string? text) =>
        double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
}

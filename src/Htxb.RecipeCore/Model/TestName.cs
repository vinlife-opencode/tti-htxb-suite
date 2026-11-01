using System.Globalization;
using System.Text.RegularExpressions;

namespace Htxb.RecipeCore.Model;

/// <summary>
/// Test-name normalisation, mirrored byte-for-byte from <c>bidfgen.engine.normalised_test_key</c>
///
///     re.sub(r"[^a-z0-9]", "", re.sub(r"_\d+$", "", name.lower()))
///
/// i.e. drop a trailing <c>_&lt;n&gt;</c>, lower-case, then strip every non-alphanumeric.
/// This ties a program's step names to the limits table when the tester spells a
/// step differently ("IGSS20V" vs "IGSS_+20V_1").
///
/// The two implementations must agree, because the Python engine chooses the std
/// table with it and the C# tool reports on the same choice. See
/// docs/test-vectors/normalise.json - one vector file, both test suites.
/// </summary>
public static class TestName
{
    private static readonly Regex TrailingIndex = new(@"_(\d+)$", RegexOptions.CultureInvariant);
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]", RegexOptions.CultureInvariant);

    public static string Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string lowered = name.Trim().ToLowerInvariant();
        lowered = TrailingIndex.Replace(lowered, string.Empty);
        return NonAlphanumeric.Replace(lowered, string.Empty);
    }

    /// <summary>
    /// True when two names normalise the same. Deliberately narrow: it is the
    /// engine's rule and nothing more, so a difference the engine cannot bridge
    /// is never reported as equivalent here.
    /// </summary>
    public static bool Equivalent(string? a, string? b) =>
        Normalise(a).Length > 0 && Normalise(a) == Normalise(b);

    private static readonly Regex TrailingReissue = new(@"[_ ]r$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TrailingDigits = new(@"\d+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// A name with the re-issue decorations removed, so <c>HTGB1</c>, <c>HTGB</c>
    /// and <c>HTGB_R</c> share one base.
    ///
    /// Mirrors <c>bidfgen.tools.std_ingest._base</c>: a re-issued recipe renames
    /// steps without changing the plan (<c>HTRB6_R</c> becomes <c>HTRB6</c>,
    /// <c>IGSS_20V_1</c> becomes <c>IGSS_+20V_1</c>), and the pairing of workbook
    /// row to logged name is positional - so this is a guard against pairing the
    /// wrong two rows, never a lookup key.
    /// </summary>
    public static string Base(string? name)
    {
        string plain = TrailingReissue.Replace((name ?? string.Empty).Trim(), string.Empty);
        return TrailingDigits.Replace(Normalise(plain), string.Empty);
    }

    /// <summary>
    /// The <c>HEX'=F_NAME</c> pair the customer writes in <c>SW BIN Priority</c> -
    /// hex bin code plus the slot-summary legend label.
    ///
    /// Three spellings occur in the wild and all must resolve to the same code:
    ///   workbook   <c>196'=F_PRE_ON</c>
    ///   workbook   <c>'101 = F_PRE_ON</c>   (leading apostrophe forcing text)
    ///   std table  <c>196 = F_PRE_ON</c>     (what std_ingest writes back out)
    ///
    /// Mirrors <c>bidfgen.tools.std_ingest.parse_swbin</c> exactly, including its
    /// stripping of whitespace and apostrophes/quotes from the code token. The
    /// shipped <c>stdtable._parse_swbin</c> does NOT strip them, which is why the
    /// generated table always carries the plain <c>HEX = NAME</c> form - see
    /// docs/test-vectors/normalise.json, whose generator asserts that round trip.
    /// </summary>
    public static (string Hex, string LegendName) SplitSwBin(string? raw)
    {
        string text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return (string.Empty, string.Empty);

        int equals = text.IndexOf('=');
        string left = equals < 0 ? text : text[..equals];
        string legend = equals < 0 ? string.Empty : text[(equals + 1)..].Trim();

        string hex = new(left.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('\'' or '"')).ToArray());
        return (hex, legend);
    }

    /// <summary>
    /// The numeric bin code. <c>"196"</c> is hexadecimal (the workbook says the
    /// tester writes hex), so this is <c>0x196 = 406</c>. Returns null when the
    /// token is not a number at all, so a free-text bin is compared as text rather
    /// than silently becoming zero.
    /// </summary>
    public static int? SwBinCode(string? raw)
    {
        string hex = SplitSwBin(raw).Hex;
        if (hex.Length == 0) return null;

        if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) return code;
        return int.TryParse(hex, NumberStyles.Integer, CultureInfo.InvariantCulture, out code) ? code : null;
    }

    /// <summary>
    /// A duration written either as a number of seconds or as text. The 1200 V and
    /// 650 V plans store <c>time (sec)</c> numerically; the SQ plans write
    /// <c>15sec</c>, <c>5s</c>, <c>10 min</c> or <c>-</c> in the same position.
    /// Returns SECONDS.
    /// </summary>
    public static double? ParseDurationSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string trimmed = text.Trim();
        if (trimmed is "-" or "—" or "n/a" or "N/A") return null;

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain))
            return plain;

        var match = Regex.Match(trimmed, @"^([\d.]+)\s*([a-zA-Z]*)$", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return null;

        string unit = match.Groups[2].Value.ToLowerInvariant();
        return unit switch
        {
            "" or "s" or "sec" or "secs" or "second" or "seconds" => value,
            "m" or "min" or "mins" or "minute" or "minutes" => value * 60.0,
            "h" or "hr" or "hour" or "hours" => value * 3600.0,
            _ => null,
        };
    }

    /// <summary>"1200V" -> 1200, "955V" -> 955, "0V" -> 0, non-numeric -> null.</summary>
    public static double? VoltageValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim().TrimEnd('V', 'v').Trim();
        if (trimmed.Length == 0) return null;

        // "+32" / "-10" are legitimate; anything else textual is not a voltage.
        bool negative = trimmed[0] == '-';
        if (trimmed[0] is '+' or '-') trimmed = trimmed[1..];

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? (negative ? -value : value)
            : null;
    }
}

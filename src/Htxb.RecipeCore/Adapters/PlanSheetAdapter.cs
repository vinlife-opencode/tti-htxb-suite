using Htxb.RecipeCore.Model;
using Htxb.RecipeCore.Xlsx;

namespace Htxb.RecipeCore.Adapters;

/// <summary>
/// Reads the customer's 261028-style plan layout: a worksheet named after the
/// recipe, a two-row header, and one row per test step.
///
/// Measured shape (all 17 workbooks in the 261028 delivery):
/// <code>
///   D1        "HIP247 4 Lead Long_BRJ"          (merged D1:S1)
///   D2/E2     "BI Recipe rev :" | "P_TM_..."
///   G3/H3     "Package:"        | "A0TM"
///   row 5     group headers   (Test name, test conditions, test limits, ...)
///   row 6     sub headers     (time (sec), Program Duration (min), V_ds, ...)
///   row 7+    data
/// </code>
///
/// Columns are resolved by HEADER TEXT, never by position: the SQ workbooks carry
/// 18-20 columns where the 1200 V ones carry 18, and one spells the profile tab
/// "Reference table" with a lower-case t. Position-based parsing would have looked
/// correct on the first family and silently mis-read the rest.
/// </summary>
public static class PlanSheetAdapter
{
    /// <summary>
    /// Row names that sit in the "Test name" column but are not test steps.
    /// Proven by count: BRJ7's 1200 V plan has 44 named rows, minus these 12
    /// "Data Transfer" rows and 2 totals, leaving exactly 32 - the row count of
    /// the shipped Std_07 table.
    ///
    /// This is the same list as <c>bidfgen.tools.std_ingest.SKIP</c>, and it has to
    /// be: a step the ingester drops when it writes a table cannot be a step this
    /// reader demands back from one. "Ramp down" is the case that proved it - the SQ
    /// plans (variant B) end with one, carrying no limit and no bin, and it is
    /// ignored there exactly as the ingester ignores it. Variant A has no such row,
    /// which is why the omission went unnoticed until the SQ delivery.
    /// </summary>
    private static readonly HashSet<string> NotAStep = new(StringComparer.Ordinal)
    {
        "datatransfer",
        "rampdown",
        "totstresstime",
        "totbitime",
    };

    public static Recipe Read(string path)
    {
        using var workbook = XlsxWorkbook.Open(path);
        return Read(workbook);
    }

    public static Recipe Read(XlsxWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var sheet = workbook.GetPlanSheet();
        var warnings = new List<string>();
        var issues = new List<string>();

        int headerRow = FindHeaderRow(sheet, warnings);
        var columns = MapColumns(sheet, headerRow);

        if (!columns.TryGetValue("testname", out int nameColumn))
        {
            throw new InvalidDataException(
                $"'{sheet.Name}' has no 'Test name' column in its header rows " +
                $"(searched rows {headerRow}-{headerRow + 1} of {sheet.RowCount}).");
        }

        var steps = new List<PlanStep>();
        var ignored = new List<IgnoredRow>();

        for (int row = headerRow + 2; row <= sheet.RowCount; row++)
        {
            string name = sheet.TextOrEmptyExpanded(row, nameColumn);
            if (name.Length == 0) continue;

            // A vertical merge in the name column (D10:D11, E69:F70) repeats the
            // step's name on the row below. Only the anchor row is the step.
            if (sheet.MergeAt(row, nameColumn) is { } merge &&
                merge.FromColumn == nameColumn && merge.FromRow != row)
            {
                continue;
            }

            // A repeated header block (some deliveries restate the header mid-sheet).
            if (columns.ContainsKey(Profile.Squash(name))) continue;

            if (NotAStep.Contains(Profile.Squash(name)))
            {
                ignored.Add(new IgnoredRow(row, name, "not-a-step"));
                continue;
            }

            steps.Add(ReadStep(sheet, row, columns, name, steps.Count + 1));
        }

        if (steps.Count == 0)
            warnings.Add($"'{sheet.Name}': no test steps found below header row {headerRow}.");

        var (title, declared, package) = ReadHeaderBlock(sheet, headerRow);

        string stem = System.IO.Path.GetFileNameWithoutExtension(workbook.Path);
        if (!string.Equals(stem, sheet.Name, StringComparison.Ordinal))
            issues.Add($"plan sheet is named '{sheet.Name}', file is '{stem}'");

        if (declared.Length > 0 && !string.Equals(declared, stem, StringComparison.Ordinal))
            issues.Add($"plan sheet declares recipe '{declared}', file is '{stem}'");

        Profile? profile = ReferenceTableAdapter.TryRead(workbook, warnings);

        if (profile is not null)
        {
            string declaredByProfile = profile.BiRecipeName;
            if (declaredByProfile.Length > 0 &&
                !string.Equals(declaredByProfile, stem, StringComparison.Ordinal))
            {
                issues.Add($"Reference Table declares recipe '{declaredByProfile}', file is '{stem}'");
            }
            else if (declaredByProfile.Length == 0)
            {
                warnings.Add("Reference Table has no 'BI recipe name' (AD2)");
            }

            if (profile.PackageCode.Length > 0 && package.Length > 0 &&
                !string.Equals(profile.PackageCode, package, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"package code '{package}' on the plan sheet but " +
                           $"'{profile.PackageCode}' in the Reference Table");
            }
        }

        return new Recipe
        {
            SourcePath = workbook.Path,
            FileStem = stem,
            PlanSheetName = sheet.Name,
            SheetRecipeName = declared,
            PackageCode = package.Length > 0 ? package : profile?.PackageCode ?? string.Empty,
            Title = title,
            Steps = steps,
            IgnoredRows = ignored,
            Profile = profile,
            IdentityIssues = issues,
            Warnings = warnings,
        };
    }

    // ------------------------------------------------------------------ helpers

    private static int FindHeaderRow(XlsxSheet sheet, List<string> warnings)
    {
        int limit = Math.Min(sheet.RowCount, 60);
        for (int row = 1; row <= limit; row++)
        {
            for (int column = 1; column <= sheet.ColumnCount; column++)
            {
                if (Profile.Squash(sheet.TextOrEmpty(row, column)) == "testname") return row;
            }
        }

        warnings.Add($"no 'Test name' header row in the first {limit} rows; assuming row 5.");
        return 5;
    }

    /// <summary>
    /// Header text per column. The sub-header row wins where it has text, because
    /// the group header above it names a merged block ("test conditions",
    /// "test limits") rather than a column.
    /// </summary>
    private static Dictionary<string, int> MapColumns(XlsxSheet sheet, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int column = 1; column <= sheet.ColumnCount; column++)
        {
            string sub = sheet.TextOrEmpty(headerRow + 1, column);
            string top = sheet.TextOrEmpty(headerRow, column);
            string header = sub.Length > 0 ? sub : top;
            if (header.Length == 0) continue;

            string key = Profile.Squash(header);
            if (key.Length == 0) continue;
            map.TryAdd(key, column);
        }

        return map;
    }

    private static PlanStep ReadStep(
        XlsxSheet sheet, int row, Dictionary<string, int> columns, string name, int ordinal)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, column) in columns)
        {
            string value = sheet.TextOrEmpty(row, column);
            if (value.Length > 0) fields[key] = value;
        }

        double? programMinutes = Number(fields, "programduration(min)", "programduration", "progduration");
        double? timeSeconds = TestName.ParseDurationSeconds(Field(fields, "time(sec)", "time"));

        return new PlanStep
        {
            Row = row,
            Ordinal = ordinal,
            Name = name,
            SectionId = sheet.TextOrEmptyExpanded(row, columns.GetValueOrDefault("testsectid", 1)),
            TestIndex = Field(fields, "testindex"),
            Marker = Field(fields, "marker"),
            StepGroup = Field(fields, "teststep"),
            TimeSeconds = timeSeconds,
            ProgramMinutes = programMinutes,
            Temp = Field(fields, "temp.(ºc)", "temp", "temperature"),
            Vds = Field(fields, "v_ds", "vds"),
            Vgs = Field(fields, "v_gs", "vgs"),
            Min = Field(fields, "min"),
            Target = Field(fields, "target"),
            Max = Field(fields, "max"),
            Units = Field(fields, "units"),
            HwBin = Field(fields, "hwbin"),
            SwBinPriority = Field(fields, "swbinpriority"),
            ExcludeWhenFail = Field(fields, "excludewhenfail"),
            Remarks = Field(fields, "remarks", "remark"),
            Fields = fields,
        };
    }

    private static string Field(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (fields.TryGetValue(key, out string? value) && value.Length > 0) return value;
        }
        return string.Empty;
    }

    private static double? Number(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        string text = Field(fields, keys);
        return double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    /// <summary>
    /// Reads the title block above the header: title (row 1), the recipe name that
    /// follows the "BI Recipe rev :" label, and the package code that follows
    /// "Package:". Values sit to the RIGHT of their labels, and the label cells are
    /// not in a fixed column, so each is located by text.
    /// </summary>
    private static (string Title, string DeclaredRecipe, string PackageCode) ReadHeaderBlock(
        XlsxSheet sheet, int headerRow)
    {
        string title = string.Empty;
        for (int column = 1; column <= sheet.ColumnCount; column++)
        {
            string text = sheet.TextOrEmptyExpanded(1, column);
            if (text.Length > 0) { title = text; break; }
        }

        string declared = ValueRightOf(sheet, headerRow, "bireciperev:");
        string package = ValueRightOf(sheet, headerRow, "package:");
        return (title, declared, package);
    }

    private static string ValueRightOf(XlsxSheet sheet, int lastRow, string squashedLabel)
    {
        for (int row = 1; row < lastRow; row++)
        {
            for (int column = 1; column < sheet.ColumnCount; column++)
            {
                if (Profile.Squash(sheet.TextOrEmpty(row, column)) != squashedLabel) continue;

                for (int next = column + 1; next <= sheet.ColumnCount; next++)
                {
                    string value = sheet.TextOrEmptyExpanded(row, next);
                    if (value.Length > 0) return value;
                }
            }
        }
        return string.Empty;
    }
}

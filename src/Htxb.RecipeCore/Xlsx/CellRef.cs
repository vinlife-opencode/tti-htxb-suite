namespace Htxb.RecipeCore.Xlsx;

/// <summary>Column/index conversion and A1-style reference parsing.</summary>
public static class CellRef
{
    /// <summary>"A" -> 1, "Z" -> 26, "AA" -> 27, "AD" -> 30.</summary>
    public static int ColumnIndex(string letters)
    {
        int value = 0;
        foreach (char ch in letters)
        {
            if (ch is >= 'A' and <= 'Z') value = value * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') value = value * 26 + (ch - 'a' + 1);
            else throw new FormatException($"Not a column reference: '{letters}'");
        }
        if (value == 0) throw new FormatException($"Not a column reference: '{letters}'");
        return value;
    }

    /// <summary>1 -> "A", 27 -> "AA".</summary>
    public static string ColumnName(int index)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        Span<char> buffer = stackalloc char[8];
        int pos = buffer.Length;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            buffer[--pos] = (char)('A' + rem);
            index = (index - 1) / 26;
        }
        return new string(buffer[pos..]);
    }

    /// <summary>"AD2" -> (row 2, column 30).</summary>
    public static (int Row, int Column) Parse(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        int split = 0;
        while (split < reference.Length && char.IsLetter(reference[split])) split++;
        if (split == 0 || split == reference.Length)
            throw new FormatException($"Not a cell reference: '{reference}'");
        int column = ColumnIndex(reference[..split]);
        if (!int.TryParse(reference[split..], out int row) || row < 1)
            throw new FormatException($"Not a cell reference: '{reference}'");
        return (row, column);
    }

    public static string Format(int row, int column) => ColumnName(column) + row.ToString();
}

/// <summary>A rectangular merged range, inclusive, 1-based.</summary>
public readonly record struct XlsxRange(int FromRow, int FromColumn, int ToRow, int ToColumn)
{
    public bool Contains(int row, int column) =>
        row >= FromRow && row <= ToRow && column >= FromColumn && column <= ToColumn;

    public override string ToString() =>
        $"{CellRef.Format(FromRow, FromColumn)}:{CellRef.Format(ToRow, ToColumn)}";

    /// <summary>Parses <c>"A5:A6"</c> or a single-cell <c>"A5"</c>.</summary>
    public static XlsxRange Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        int colon = text.IndexOf(':');
        if (colon < 0)
        {
            var (row, column) = CellRef.Parse(text);
            return new XlsxRange(row, column, row, column);
        }
        var (r1, c1) = CellRef.Parse(text[..colon]);
        var (r2, c2) = CellRef.Parse(text[(colon + 1)..]);
        return new XlsxRange(Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));
    }
}

namespace Htxb.RecipeTool;

/// <summary>
/// Minimal <c>--name value</c> / <c>-n value</c> / <c>--flag</c> reader.
///
/// Every command takes paths, so nothing here needs a full parser - but a typo
/// must fail loudly rather than be ignored, so unknown options are rejected by
/// <see cref="RejectUnknown"/>. A single-letter option (<c>-o</c>) is accepted
/// because that is how the tools it replaces were called.
/// </summary>
internal sealed class ArgReader
{
    private readonly List<string> _positional = new();
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    private ArgReader() { }

    public static ArgReader Parse(IEnumerable<string> args)
    {
        var reader = new ArgReader();
        var list = args.ToList();

        for (int i = 0; i < list.Count; i++)
        {
            string arg = list[i];

            if (!IsOption(arg))
            {
                reader._positional.Add(arg);
                continue;
            }

            string name = arg.TrimStart('-');
            if (name.Length == 0) throw new UsageException($"Empty option '{arg}'.");

            // A value follows unless the next token is another option.
            if (i + 1 < list.Count && !IsOption(list[i + 1]))
            {
                reader._options[name] = list[++i];
            }
            else
            {
                reader._options[name] = string.Empty;
            }
        }

        return reader;
    }

    /// <summary><c>--name</c>, or a short <c>-n</c>; never a bare <c>-</c> or a path.</summary>
    private static bool IsOption(string arg) =>
        arg.StartsWith("--", StringComparison.Ordinal)
        || (arg.Length == 2 && arg[0] == '-' && char.IsAsciiLetter(arg[1]));

    public string? Value(string name) =>
        _options.TryGetValue(name, out string? value) && value.Length > 0 ? value : null;

    public bool Has(string name) => _options.ContainsKey(name);

    public IReadOnlyList<string> Positionals => _positional;

    public string Positional(int index, string fallback = "") =>
        index < _positional.Count ? _positional[index] : fallback;

    public void RejectUnknown(params string[] allowed)
    {
        var known = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        foreach (string name in _options.Keys)
        {
            if (!known.Contains(name))
                throw new UsageException($"Unknown option '--{name}'. Allowed: {string.Join(", ", allowed.Select(a => (a.Length == 1 ? "-" : "--") + a))}");
        }
    }
}

internal sealed class UsageException(string message) : Exception(message);

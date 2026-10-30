namespace Htxb.RecipeTool;

/// <summary>
/// Minimal <c>--name value</c> / <c>--flag</c> reader.
///
/// Every command takes paths, so nothing here needs a full parser - but a typo
/// must fail loudly rather than be ignored, so unknown options are rejected by
/// <see cref="RejectUnknown"/>.
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

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                reader._positional.Add(arg);
                continue;
            }

            string name = arg[2..];
            if (name.Length == 0) throw new UsageException("Empty option '--'.");

            // A value follows unless the next token is another option.
            if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
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
                throw new UsageException($"Unknown option '--{name}'. Allowed: {string.Join(", ", allowed.Select(a => "--" + a))}");
        }
    }
}

internal sealed class UsageException(string message) : Exception(message);

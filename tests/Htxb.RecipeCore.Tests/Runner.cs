using System.Diagnostics;

namespace Htxb.RecipeCore.Tests;

/// <summary>
/// The whole test harness: named checks that either pass or throw.
///
/// Deliberately tiny - a suite that needs a framework has to be installed before
/// it can tell you the tree is broken, which is the wrong order.
/// </summary>
internal sealed class Runner
{
    private readonly List<string> _failures = new();
    private readonly List<string> _skips = new();

    public int Passed { get; private set; }
    public int Skipped => _skips.Count;
    public IReadOnlyList<string> Failures => _failures;

    /// <summary>Runs one check; an exception is a failure, never a crash of the run.</summary>
    public void Check(string name, Action body)
    {
        try
        {
            body();
            Passed++;
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception error)
        {
            _failures.Add(name);
            Console.WriteLine($"FAIL  {name}");
            Console.WriteLine($"        {Describe(error)}");
        }
    }

    /// <summary>
    /// A check that could not run. Skipping is printed loudly and is never counted
    /// as a pass: a gate that silently skips is worse than no gate.
    /// </summary>
    public void Skip(string name, string reason)
    {
        _skips.Add(name);
        Console.WriteLine($"SKIP  {name}");
        Console.WriteLine($"        {reason}");
    }

    private static string Describe(Exception error)
    {
        string text = error is CheckFailed ? error.Message : $"{error.GetType().Name}: {error.Message}";
        return text.Replace("\r", string.Empty).Replace("\n", "\n        ");
    }
}

/// <summary>An assertion failure with a message written for a human.</summary>
internal sealed class CheckFailed(string message) : Exception(message);

/// <summary>Assertions, kept blunt on purpose.</summary>
internal static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new CheckFailed(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
        throw new CheckFailed($"{what}: expected {Show(expected)}, got {Show(actual)}");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string what)
    {
        if (expected.Count != actual.Count)
            throw new CheckFailed($"{what}: expected {expected.Count} item(s), got {actual.Count}");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new CheckFailed($"{what}[{i}]: expected {Show(expected[i])}, got {Show(actual[i])}");
        }
    }

    private static string Show<T>(T value) => value switch
    {
        null => "<null>",
        string text => "\"" + text + "\"",
        _ => value.ToString() ?? "<null>",
    };
}

internal static class Log
{
    public static void Line() => Console.WriteLine();

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
    }

    /// <summary>Elapsed milliseconds, so a slow parse is visible rather than guessed at.</summary>
    public static T Timed<T>(Func<T> body, out long milliseconds)
    {
        var watch = Stopwatch.StartNew();
        T result = body();
        watch.Stop();
        milliseconds = watch.ElapsedMilliseconds;
        return result;
    }
}

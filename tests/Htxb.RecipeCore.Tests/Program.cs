namespace Htxb.RecipeCore.Tests;

/// <summary>
/// Entry point. Exit code 0 = every check passed, 1 = at least one failed.
///
/// The real-data half is skipped, loudly, when the delivery or the std tables are
/// not on this machine: a fresh clone can still verify the rules, and the skip is
/// visible in the summary so nobody mistakes it for a pass.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Htxb.RecipeCore.Tests - checks for the recipe reader and the fit checker

          --vectors DIR|FILE   the shared vector file (default: the BidfGen2 copy)
          --samples DIR        the customer delivery (default: the STM 261028 package)
          --tables  DIR        folder with Std_*.xlsx + Cust_Profile_Select (default: fixtures)
            --zadz    DIR        the machine recipe export (default: live delivery, else fixtures)
            --zadz    DIR        the machine recipe export (default: live delivery, else fixtures)
          --quiet              print failures only

        Exit code 0 = all checks passed, 1 = a check failed.
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string vectorsPath = TestPaths.DefaultVectors;
        string samplesRoot = TestPaths.DefaultSamples;
        string tablesRoot = TestPaths.DefaultTables;
        string zadzRoot = Directory.Exists(TestPaths.DefaultZadz)
            ? TestPaths.DefaultZadz : TestPaths.FixtureZadz;
        bool quiet = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vectors" when i + 1 < args.Length: vectorsPath = args[++i]; break;
                case "--samples" when i + 1 < args.Length: samplesRoot = args[++i]; break;
                case "--tables" when i + 1 < args.Length: tablesRoot = args[++i]; break;
                case "--zadz" when i + 1 < args.Length: zadzRoot = args[++i]; break;
                case "--quiet": quiet = true; break;
                case "-h" or "--help": Console.WriteLine(Usage); return 0;
                default:
                    Console.Error.WriteLine($"unknown argument '{args[i]}'");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        Console.WriteLine("Htxb.RecipeCore.Tests");
        Console.WriteLine($"  vectors : {vectorsPath}");
        Console.WriteLine($"  samples : {samplesRoot}");
        Console.WriteLine($"  tables  : {tablesRoot}");

        var runner = new Runner();

        VectorFile vectors;
        try
        {
            vectors = VectorFile.Load(vectorsPath);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"cannot start: {error.Message}");
            return 1;
        }

        UnitTests.Register(runner, vectors);

        DiffTests.Register(runner, samplesRoot, zadzRoot, TestPaths.DefaultAliases);

        ToolTests.Register(runner, samplesRoot, zadzRoot, tablesRoot);

        bool haveDelivery = Directory.Exists(samplesRoot);
        bool haveTables = Directory.Exists(tablesRoot);

        if (haveDelivery && haveTables)
        {
            try
            {
                RealDataTests.Register(runner, new Delivery
                {
                    SamplesRoot = samplesRoot,
                    TablesRoot = tablesRoot,
                });
            }
            catch (Exception error)
            {
                Console.WriteLine($"FAIL  real delivery section");
                Console.WriteLine($"        {error.GetType().Name}: {error.Message}");
                return 1;
            }
        }
        else
        {
            Log.Section("real delivery");
            runner.Skip("real delivery", haveDelivery
                ? $"std tables not found: {tablesRoot}"
                : $"delivery not found: {samplesRoot}");
        }

        Console.WriteLine();
        Console.WriteLine($"{runner.Passed} passed, {runner.Failures.Count} failed, {runner.Skipped} skipped");

        if (runner.Failures.Count > 0)
        {
            Console.WriteLine("failed: " + string.Join(", ", runner.Failures));
            Console.WriteLine("CHECKS FAILED");
            return 1;
        }

        Console.WriteLine(quiet ? "CHECKS PASSED" : "ALL RECIPE CORE CHECKS PASSED");
        return 0;
    }
}

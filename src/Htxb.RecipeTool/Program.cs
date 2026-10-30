using System.Reflection;
using Htxb.RecipeTool.Commands;

namespace Htxb.RecipeTool;

internal static class Program
{
    private const string Usage = """
        Htxb.RecipeTool - customer recipe audit for the BiDF pipeline

        Commands
          recipes [--samples DIR] [--json]   parse every workbook in a delivery
          steps   <workbook.xlsx>            dump one plan sheet's steps
          fit     [--samples DIR] [--tables DIR] [--json] [--strict]
                                            judge each recipe against a std table

        Options
          --samples DIR   delivery folder (default: the STM 261028 package)
          --tables  DIR   folder holding Std_*.xlsx (default: BidfGen2 fixtures)
          --json          machine-readable output
          --strict        non-zero exit unless every recipe FITS

        Exit codes
          0  no problem found
          1  a problem was found (or --strict and some recipe does not fit)
          2  usage error
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return 2;
        }

        try
        {
            string command = args[0].ToLowerInvariant();
            var reader = ArgReader.Parse(args.Skip(1));

            return command switch
            {
                "version" or "--version" or "-v" => PrintVersion(),
                "help" or "--help" or "-h" => PrintUsage(),
                "recipes" => RecipesCommand.Run(reader),
                "steps" => RecipesCommand.Steps(reader),
                "fit" => FitCommand.Run(reader),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException error)
        {
            Console.Error.WriteLine("error: " + error.Message);
            Console.Error.WriteLine("run 'Htxb.RecipeTool help' for usage.");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
            return 1;
        }
    }

    private static int PrintUsage()
    {
        Console.WriteLine(Usage);
        return 0;
    }

    private static int PrintVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
        Console.WriteLine($"Htxb.RecipeTool {version}");
        Console.WriteLine($"core      {typeof(Htxb.RecipeCore.Model.Recipe).Assembly.GetName().Version}");
        return 0;
    }
}

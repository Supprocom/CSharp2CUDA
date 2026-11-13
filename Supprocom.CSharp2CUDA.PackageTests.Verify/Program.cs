using System.ComponentModel;

namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
            return UsageError(null);
        var command = args[0];
        try
        {
            return command switch
            {
                "package" => await PackageCommand.RunAsync(args).ConfigureAwait(false),
                "tool" => await ToolCommand.RunAsync(args).ConfigureAwait(false),
                "source-suite" => await SourceSuiteCommand.RunAsync(args).ConfigureAwait(false),
                _ => UsageError(null)
            };
        }
        catch (InvalidDataException exception)
        {
            return Report(exception.Message);
        }
        catch (IOException exception)
        {
            return Report($"{command}: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Report($"{command}: {exception.Message}");
        }
        catch (Win32Exception exception)
        {
            return Report($"{command}: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return Report($"{command}: {exception.Message}");
        }
    }

    public static int UsageError(string? usage)
    {
        Console.Error.WriteLine(
            "Usage: Supprocom.CSharp2CUDA.PackageTests.Verify <package|tool|source-suite> [options]");
        Console.Error.WriteLine("  " + (usage ?? PackageCommand.Usage));
        if (usage is null)
        {
            Console.Error.WriteLine("  " + ToolCommand.Usage);
            Console.Error.WriteLine("  " + SourceSuiteCommand.Usage);
        }

        return 2;
    }

    private static int Report(string message)
    {
        Console.Error.WriteLine("FAIL: " + message);
        return 1;
    }
}

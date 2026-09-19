namespace Supprocom.CSharp2CUDA.Tool;

internal static class CommandLine
{
    public static ToolCommand Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 ||
            arguments[0] is "--help" or "-h" or "help")
        {
            return new ToolCommand(ToolCommandKind.Help, null, null, null, null);
        }

        var kind = arguments[0] switch
        {
            "check" => ToolCommandKind.Check,
            "scaffold" => ToolCommandKind.Scaffold,
            "refresh" => ToolCommandKind.Refresh,
            _ => throw new ToolException(
                "CS2CUDA100",
                $"Command '{arguments[0]}' is not valid.")
        };
        var values = ParseOptions(arguments.Skip(1).ToArray());
        ValidateOptionNames(kind, values.Keys);
        return kind switch
        {
            ToolCommandKind.Check => new ToolCommand(
                kind,
                GetRequired(values, "project"),
                GetRequired(values, "method"),
                null,
                null),
            ToolCommandKind.Scaffold => new ToolCommand(
                kind,
                GetRequired(values, "project"),
                GetRequired(values, "method"),
                ParseMapping(GetRequired(values, "mapping")),
                GetRequired(values, "output")),
            ToolCommandKind.Refresh => new ToolCommand(
                kind,
                null,
                null,
                null,
                GetRequired(values, "output")),
            _ => throw new InvalidOperationException("The command kind is not valid.")
        };
    }

    public static string HelpText => """
        CSharp2CUDA project tool

        Usage:
          csharp2cuda check --project <project.csproj> --method <type.method(signature)>
          csharp2cuda scaffold --project <project.csproj> --method <type.method(signature)> --mapping <elementwise|single-thread> --output <directory>
          csharp2cuda refresh --output <directory>
        """;

    private static Dictionary<string, string> ParseOptions(IReadOnlyList<string> arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) ||
                name.Length == 2 ||
                index + 1 >= arguments.Count)
            {
                throw new ToolException(
                    "CS2CUDA100",
                    $"Argument '{name}' requires a name and a value.");
            }
            name = name[2..];
            if (!result.TryAdd(name, arguments[index + 1]))
            {
                throw new ToolException(
                    "CS2CUDA100",
                    $"Argument '--{name}' is specified more than once.");
            }
        }
        return result;
    }

    private static string GetRequired(
        IReadOnlyDictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ToolException(
                "CS2CUDA100",
                $"Argument '--{name}' is required.");
        }
        return value;
    }

    private static CudaAdapterMapping ParseMapping(string value) => value switch
    {
        "elementwise" => CudaAdapterMapping.Elementwise,
        "single-thread" => CudaAdapterMapping.SingleThread,
        _ => throw new ToolException(
            "CS2CUDA100",
            $"Mapping '{value}' is not valid.")
    };

    private static void ValidateOptionNames(
        ToolCommandKind kind,
        IEnumerable<string> names)
    {
        var allowed = kind switch
        {
            ToolCommandKind.Check => new[] { "project", "method" },
            ToolCommandKind.Scaffold => new[] { "project", "method", "mapping", "output" },
            ToolCommandKind.Refresh => new[] { "output" },
            _ => []
        };
        var accepted = allowed.ToHashSet(StringComparer.Ordinal);
        var invalid = names.FirstOrDefault(name => !accepted.Contains(name));
        if (invalid is not null)
        {
            throw new ToolException(
                "CS2CUDA100",
                $"Argument '--{invalid}' is not valid for this command.");
        }
    }
}

internal sealed class ToolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

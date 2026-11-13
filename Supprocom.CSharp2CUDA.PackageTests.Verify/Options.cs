namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class Options
{
    public static Dictionary<string, string>? Parse(string[] args, string[] required, string[] optional)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                return null;
            var key = args[index][2..];
            if (Array.IndexOf(required, key) < 0 && Array.IndexOf(optional, key) < 0)
                return null;
            if (!values.TryAdd(key, args[index + 1]))
                return null;
        }

        foreach (var key in required)
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                return null;
        }

        return values;
    }

    public static string? Optional(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}

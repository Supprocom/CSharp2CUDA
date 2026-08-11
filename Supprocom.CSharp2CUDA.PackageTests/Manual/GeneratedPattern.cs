using System.Text.RegularExpressions;

namespace Supprocom.CSharp2CUDA.PackageTests.Manual;

internal static partial class GeneratedPattern
{
    public static bool Matches(string value)
    {
        return Pattern().IsMatch(value);
    }

    [GeneratedRegex("^cuda$")]
    private static partial Regex Pattern();
}

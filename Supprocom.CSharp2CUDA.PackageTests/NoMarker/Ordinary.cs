using System.Text.RegularExpressions;

public static partial class Ordinary
{
    public static int Add(int left, int right)
    {
        return left + right;
    }

    public static int Main()
    {
        return GeneratedPattern().IsMatch("cuda") ? 0 : 1;
    }

    [GeneratedRegex("^cuda$")]
    private static partial Regex GeneratedPattern();
}

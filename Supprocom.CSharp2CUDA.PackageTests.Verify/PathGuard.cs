namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class PathGuard
{
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static void RequireOutside(string path, string root, string caseName, string reason)
    {
        Check.That(!IsInside(path, root), caseName, reason);
    }

    public static void RequireInside(string path, string root, string caseName, string reason)
    {
        Check.That(IsInside(path, root), caseName, reason);
    }

    public static string WithTrailingSeparator(string parent, string name)
    {
        return Path.Combine(parent, name) + Path.DirectorySeparatorChar;
    }

    private static bool IsInside(string path, string root)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, Comparison);
    }
}

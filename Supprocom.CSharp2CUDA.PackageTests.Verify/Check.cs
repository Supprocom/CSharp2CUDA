using System.Diagnostics.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal static class Check
{
    public static void That(bool condition, string caseName, string reason)
    {
        if (!condition)
            Fail(caseName, reason);
    }

    [DoesNotReturn]
    public static void Fail(string caseName, string reason)
    {
        throw new InvalidDataException($"{caseName}: {reason}");
    }
}

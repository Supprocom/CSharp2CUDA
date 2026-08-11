extern alias Lookalike;

namespace Supprocom.CSharp2CUDA.PackageTests.LookalikeAttributed;

[Lookalike::Supprocom.CSharp2CUDA.TranspileToCUDA]
public static class Kernel
{
    public static int Add(int left, int right)
    {
        return left + right;
    }
}

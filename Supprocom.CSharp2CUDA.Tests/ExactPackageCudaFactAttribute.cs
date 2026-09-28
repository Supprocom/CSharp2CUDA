using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public sealed class ExactPackageCudaFactAttribute : FactAttribute
{
#pragma warning restore CA1515
    public ExactPackageCudaFactAttribute()
    {
        if (!CudaTestRuntime.IsAvailable(out var reason))
            Skip = reason;
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                     "CSHARP2CUDA_EXACT_PACKAGE_CUDA")))
            Skip = "The exact-package CUDA source is not available.";
    }
}

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public sealed class CudaFactAttribute : FactAttribute
{
#pragma warning restore CA1515
    public CudaFactAttribute()
    {
        if (!CudaTestRuntime.IsAvailable(out var reason))
            Skip = reason;
    }
}

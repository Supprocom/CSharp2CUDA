using System.Runtime.InteropServices;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaTestRuntimeTests
{
    [Fact]
    public void Utf8OptionPointersRoundTripWithoutCuda()
    {
        string[] options = ["--std=c++17", "--include-path=π"];
        var decoded = CudaTestRuntime.WithUtf8StringArray(options, (pointers, count) =>
        {
            var values = new string?[count];
            for (var index = 0; index < count; index++)
            {
                var pointer = Marshal.ReadIntPtr(pointers, index * IntPtr.Size);
                values[index] = Marshal.PtrToStringUTF8(pointer);
            }
            return values;
        });

        Assert.Equal(options, decoded);
    }
}

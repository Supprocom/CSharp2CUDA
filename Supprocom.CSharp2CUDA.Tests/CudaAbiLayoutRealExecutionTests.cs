using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaAbiLayoutRealExecutionTests
{
    private const string AbiSource = """
        using System.Runtime.InteropServices;
        using Supprocom.CSharp2CUDA;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct Entry
        {
            public byte Kind;
            public int Value;
        }

        [TranspileToCUDA]
        internal static unsafe class AbiLayoutModule
        {
            [CudaGlobal(Name = "abi_layout")]
            private static void Run(Entry* entries, int* output)
            {
                output[0] = entries[0].Value + entries[0].Kind;
            }
        }
        """;

    [CudaFact]
    public void CudaAbiLayoutChecksCompileWithoutAnIncludePath()
    {
        var result = CudaTestCompiler.Transpile(AbiSource);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains("static_assert(__builtin_offsetof(Entry, Value) == 4", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("stddef.h", result.Source, StringComparison.Ordinal);

        // CudaTestRuntime compiles with NVRTC and passes no include path, so any header the
        // source needed would fail here.
        using var runtime = CudaTestRuntime.Create(result.Source);
        var entries = runtime.Allocate<byte>([3, 0, 0, 0, 40, 0, 0, 0]);
        var output = runtime.Allocate<int>(new int[1]);
        try
        {
            runtime.Launch("abi_layout", 1, 1, 0, entries, output);

            Assert.Equal(43, runtime.Read<int>(output, 1)[0]);
        }
        finally
        {
            runtime.Free(output);
            runtime.Free(entries);
        }
    }
}

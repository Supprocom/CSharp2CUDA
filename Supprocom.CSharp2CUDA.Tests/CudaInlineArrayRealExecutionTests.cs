using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Supprocom.CSharp2CUDA.Tests.InlineArrayProbe;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaInlineArrayRealExecutionTests
{
    private const int PairCount = 5;

    [CudaFact]
    public void CudaInlineArrayAddMatchesTheHostBitForBit()
    {
        var result = InlineArrayProbeSources.Transpile(InlineArrayProbeSources.KernelsSource, "ProbeKernels.cs");
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));

        var sentinel = CreateValue(7, 8, 9, [-1, -2, -3, -4], [-5, -6, -7, -8]);
        DType[] inputs =
        [
            // Pair 0 and 1 stay in range for every element.
            CreateValue(1, 2, 3, [1, 2, 3, 4], [2, 3, 4, 5]),
            CreateValue(1, 2, 3, [-5, 7, -11, 13], [3, 5, 7, 9]),
            CreateValue(4, 5, 6, [1000, 2000, 3000, 4000], [7, 11, 13, 17]),
            CreateValue(4, 5, 6, [3, 5, 7, 9], [19, 23, 29, 31]),
            // Pair 2 overflows the numerator above int.MaxValue at element 0.
            CreateValue(4, 5, 6, [int.MaxValue, 1, 1, 1], [1, 1, 1, 1]),
            CreateValue(4, 5, 6, [1, 1, 1, 1], [2, 1, 1, 1]),
            // Pair 3 stores element 0, then overflows the numerator below int.MinValue at element 1.
            CreateValue(1, 1, 1, [1, int.MinValue, 1, 1], [1, 1, 1, 1]),
            CreateValue(1, 1, 1, [1, -1, 1, 1], [1, 1, 1, 1]),
            // Pair 4 overflows only the denominator at element 2.
            CreateValue(2, 2, 2, [1, 1, 0, 1], [1, 1, int.MaxValue, 1]),
            CreateValue(2, 2, 2, [1, 1, 0, 1], [1, 1, 2, 1])
        ];
        var expected = new DType[PairCount];
        for (var index = 0; index < PairCount; index++)
        {
            expected[index] = sentinel;
            Algo.Add(ref expected[index], in inputs[index * 2], in inputs[index * 2 + 1]);
        }
        Assert.Equal(1, expected[0].kind);
        Assert.Equal(4, expected[1].kind);
        Assert.Equal(-1, expected[2].kind);
        Assert.Equal(-1, expected[3].kind);
        Assert.Equal(-1, expected[4].kind);
        var initial = new DType[PairCount];
        Array.Fill(initial, sentinel);

        using var runtime = CudaTestRuntime.Create(result.Source);
        var inputPointer = runtime.Allocate<DType>(inputs);
        var outputPointer = runtime.Allocate<DType>(initial);
        try
        {
            runtime.Launch("inline_array_add", 1, 1, 0, inputPointer, outputPointer, PairCount);
            var actual = runtime.Read<DType>(outputPointer, PairCount);

            Assert.Equal(
                MemoryMarshal.AsBytes(expected.AsSpan()).ToArray(),
                MemoryMarshal.AsBytes(actual.AsSpan()).ToArray());
        }
        finally
        {
            runtime.Free(outputPointer);
            runtime.Free(inputPointer);
        }
    }

    [CudaFact]
    public void CudaInlineArrayLayoutMatchesTheHost()
    {
        var result = InlineArrayProbeSources.Transpile(InlineArrayProbeSources.KernelsSource, "ProbeKernels.cs");
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));

        using var runtime = CudaTestRuntime.Create(result.Source);
        var probe = runtime.Allocate<DType>([default]);
        var output = runtime.Allocate<ulong>(new ulong[6]);
        try
        {
            runtime.Launch("inline_array_layout", 1, 1, 0, probe, output);
            var device = runtime.Read<ulong>(output, 6);

            Assert.Equal(44UL, (ulong)Unsafe.SizeOf<DType>());
            Assert.Equal(
                [
                    (ulong)Unsafe.SizeOf<DType>(),
                    (ulong)Marshal.OffsetOf<DType>(nameof(DType.kind)).ToInt64(),
                    (ulong)Marshal.OffsetOf<DType>(nameof(DType.rows)).ToInt64(),
                    (ulong)Marshal.OffsetOf<DType>(nameof(DType.columns)).ToInt64(),
                    (ulong)Marshal.OffsetOf<DType>(nameof(DType.num)).ToInt64(),
                    (ulong)Marshal.OffsetOf<DType>(nameof(DType.den)).ToInt64()
                ],
                device);
            Assert.Equal([44UL, 0UL, 4UL, 8UL, 12UL, 28UL], device);
        }
        finally
        {
            runtime.Free(output);
            runtime.Free(probe);
        }
    }

    [CudaFact]
    public void CudaInlineArrayOutOfRangeIndexTrapsTheLaunch()
    {
        var result = InlineArrayProbeSources.Transpile(InlineArrayProbeSources.KernelsSource, "ProbeKernels.cs");
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));

        // Each CudaTestRuntime owns its context, so the context poisoned by the trap is destroyed
        // with this runtime and no other test observes it. The device memory is released with the
        // context; calling Free on it afterwards would fail.
        using var runtime = CudaTestRuntime.Create(result.Source);
        var data = runtime.Allocate<DType>([default]);

        runtime.Launch("inline_array_trap", 1, 1, 0, data, 3UL);
        Assert.Equal(1, runtime.Read<DType>(data, 1)[0].num[3]);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            runtime.Launch("inline_array_trap", 1, 1, 0, data, 4UL));
        Assert.Contains("cuCtxSynchronize", failure.Message, StringComparison.Ordinal);
    }

    private static DType CreateValue(
        int kind,
        int rows,
        int columns,
        ReadOnlySpan<int> numerators,
        ReadOnlySpan<int> denominators)
    {
        var value = new DType { kind = kind, rows = rows, columns = columns };
        for (var index = 0; index < 4; index++)
        {
            value.num[index] = numerators[index];
            value.den[index] = denominators[index];
        }
        return value;
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}

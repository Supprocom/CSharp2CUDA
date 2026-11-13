using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaReadOnlyAddressTests
{
    [Theory]
    [InlineData("[CudaReadOnly] Block* blocks", "Probe(blocks)", "return Take(in blocks[0]);", "Block")]
    [InlineData("[CudaReadOnly] Block* blocks", "Probe(blocks)", "return TakeInt(in blocks[0].rows);", "int")]
    [InlineData("[CudaReadOnly] Block* blocks", "Probe(blocks)", "return TakeInt(in blocks->rows);", "int")]
    [InlineData("[CudaReadOnly] Block* blocks", "Probe(blocks)", "return TakeInt(in (*blocks).rows);", "int")]
    [InlineData(
        "[CudaReadOnly] Block* blocks",
        "Probe(blocks)",
        "ref readonly Block local = ref blocks[1]; return Take(in local);",
        "Block")]
    [InlineData(
        "[CudaReadOnly] Block* blocks",
        "Probe(blocks)",
        "ulong address = (ulong)&blocks[0]; return (int)address;",
        "Block")]
    [InlineData(
        "[CudaReadOnly] Block* blocks",
        "Probe(blocks)",
        "ulong address = (ulong)(blocks + 1); return (int)address;",
        "Block")]
    [InlineData("in Block value", "Probe(in blocks[0])", "return Take(in value);", "Block")]
    [InlineData("in Block value", "Probe(in blocks[0])", "return TakeInt(in value.rows);", "int")]
    [InlineData("in Block value", "Probe(in blocks[0])", "return TakeInt(in value.fixedValue);", "int")]
    [InlineData("Block* blocks", "Probe(blocks)", "return TakeInt(in blocks->fixedValue);", "int")]
    public void TranspileDeclaresConstPointerTemporariesForReadOnlyPlaces(
        string parameter,
        string call,
        string body,
        string pointee)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            internal struct Block
            {
                public int rows;
                public readonly int fixedValue;
            }

            [TranspileToCUDA]
            internal static unsafe class ReadOnlyAddressModule
            {
                [CudaDevice]
                private static int Take(in Block value) => value.rows;

                [CudaDevice]
                private static int TakeInt(in int value) => value;

                [CudaDevice]
                private static int Probe({{parameter}})
                {
                    {{body}}
                }

                [CudaGlobal(Name = "read_only_address")]
                private static void Run([CudaReadOnly] Block* blocks, int* output)
                {
                    output[0] = {{call}};
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var lines = result.Source.Split('\n');
        Assert.Contains(
            lines,
            line => line.TrimStart().StartsWith(
                $"const {pointee}* csharp2cuda_temp_",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            lines,
            line => line.TrimStart().StartsWith(
                $"{pointee}* csharp2cuda_temp_",
                StringComparison.Ordinal));
    }
}

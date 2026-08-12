using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaMaintenanceContractsTests
{
    [Fact]
    public void Transpile_EmitsVolatileMappedMemoryAndGlobalTimer()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class MappedModule
            {
                [CudaGlobal(Name = "mapped_contracts")]
                private static void Run(
                    byte* mapped,
                    int* typedInt,
                    ulong* typedUInt64,
                    ulong* output)
                {
                    int first = Cuda.VolatileLoad(typedInt);
                    ulong second = Cuda.VolatileLoad(typedUInt64);
                    int third = Cuda.VolatileLoadInt32(mapped, 4UL);
                    ulong fourth = Cuda.VolatileLoadUInt64(mapped, 8UL);
                    Cuda.VolatileStore(typedInt, first);
                    Cuda.VolatileStore(typedUInt64, second);
                    Cuda.VolatileStoreInt32(mapped, 16UL, third);
                    Cuda.VolatileStoreUInt64(mapped, 24UL, fourth);
                    output[0] = Cuda.GlobalTimer();
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "return *((const volatile int*)address);",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "return *((const volatile unsigned long long*)address);",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "csharp2cuda_volatile_load_i32_bytes(mapped, 4ull)",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "csharp2cuda_volatile_store_u64_bytes(mapped, 24ull, fourth);",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "asm volatile(\"mov.u64 %0, %%globaltimer;\" : \"=l\"(value));",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "output[0] = csharp2cuda_global_timer();",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("atomicAdd", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EmitsPrimitiveAndStructureInlineArrays()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InlineArrayModule
            {
                public struct ResearchEvolutionNode
                {
                    public int operation;
                    public ulong cost;
                }

                public struct ResearchEvolutionOperation
                {
                    [CudaInlineArray(3)]
                    public int* input_kinds;
                }

                public struct ResearchEvolutionEntry
                {
                    [CudaInlineArray(3)]
                    public int* operands;

                    [CudaInlineArray(3)]
                    public int* operand_kinds;

                    [CudaInlineArray(5)]
                    public ResearchEvolutionNode* nodes;
                }

                public struct ResearchEvolutionState
                {
                    [CudaInlineArray(4)]
                    public ulong* runtime_invalid_by_operation;
                }

                [CudaGlobal(Name = "inline_arrays")]
                private static void Run(ResearchEvolutionEntry* entry, int* output)
                {
                    entry->operands[1] = 7;
                    entry->nodes[2].operation = 11;
                    int* operands = entry->operands;
                    ResearchEvolutionNode* nodes = entry->nodes;
                    output[0] = operands[1] + nodes[2].operation;
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("int input_kinds[3];", result.Source, StringComparison.Ordinal);
        Assert.Contains("int operands[3];", result.Source, StringComparison.Ordinal);
        Assert.Contains("int operand_kinds[3];", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "ResearchEvolutionNode nodes[5];",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "unsigned long long runtime_invalid_by_operation[4];",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("int* operands = entry->operands;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "ResearchEvolutionNode* nodes = entry->nodes;",
            result.Source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[CudaInlineArray(0)] public int* values;")]
    [InlineData("[CudaInlineArray(3)] public int value;")]
    [InlineData("[CudaInlineArray(3)] public void* values;")]
    [InlineData("[CudaInlineArray(3)] public int** values;")]
    [InlineData("[CudaInlineArray(3)] public decimal* values;")]
    [InlineData("[CudaInlineArray(3)] public char* values;")]
    public void Transpile_RejectsInvalidInlineArrays(string field)
    {
        var source = $$"""
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InvalidInlineArrayModule
            {
                public struct InvalidStorage
                {
                    {{field}}
                }

                [CudaGlobal]
                private static void Run()
                {
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        AssertOnlyInlineArrayDiagnostic(result);
    }

    [Fact]
    public void Transpile_RejectsAnExternalStructureInlineArray()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InvalidInlineArrayModule
            {
                [CudaExternal]
                public struct NativeValue
                {
                }

                public struct InvalidStorage
                {
                    [CudaInlineArray(3)]
                    public NativeValue* values;
                }

                [CudaGlobal]
                private static void Run()
                {
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        AssertOnlyInlineArrayDiagnostic(result);
    }

    [Fact]
    public void Transpile_AcceptsSupportedInlineArrayElementTypes()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class InlineArrayBoundaryModule
            {
                public struct Element
                {
                    public int value;
                }

                public struct SupportedStorage
                {
                    [CudaInlineArray(2)] public bool* booleans;
                    [CudaInlineArray(2)] public sbyte* signedBytes;
                    [CudaInlineArray(2)] public byte* bytes;
                    [CudaInlineArray(2)] public short* int16Values;
                    [CudaInlineArray(2)] public ushort* uint16Values;
                    [CudaInlineArray(2)] public int* int32Values;
                    [CudaInlineArray(2)] public uint* uint32Values;
                    [CudaInlineArray(2)] public long* int64Values;
                    [CudaInlineArray(2)] public ulong* uint64Values;
                    [CudaInlineArray(2)] public float* singleValues;
                    [CudaInlineArray(2)] public double* doubleValues;
                    [CudaInlineArray(2)] public Element* structureValues;
                }

                [CudaGlobal]
                private static void Run(SupportedStorage* storage)
                {
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        string[] fields =
        [
            "bool booleans[2];",
            "signed char signedBytes[2];",
            "unsigned char bytes[2];",
            "short int16Values[2];",
            "unsigned short uint16Values[2];",
            "int int32Values[2];",
            "unsigned int uint32Values[2];",
            "long long int64Values[2];",
            "unsigned long long uint64Values[2];",
            "float singleValues[2];",
            "double doubleValues[2];",
            "Element structureValues[2];"
        ];
        foreach (var field in fields)
            Assert.Contains(field, result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_EmitsExternalDevicePrototypeWithoutBody()
    {
        const string source = """
            using System;
            using Supprocom.CSharp2CUDA;

            [TranspileToCUDA]
            internal static unsafe class DispatchModule
            {
                public struct MathBlockSlot
                {
                    public double scalar_value;
                    public int valid;
                }

                [CudaExternalDevice(Name = "mathblocks_operation_dispatch")]
                private static void Dispatch(
                    int family,
                    int opcode,
                    [CudaReadOnly] MathBlockSlot** inputs,
                    int inputCount,
                    MathBlockSlot* output) => throw new NotSupportedException();

                [CudaGlobal(Name = "dispatch_consumer")]
                private static void Run(MathBlockSlot** inputs, MathBlockSlot* output)
                {
                    Dispatch(1, 2, inputs, 1, output);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "__device__ void mathblocks_operation_dispatch(\n" +
            "    int family,\n" +
            "    int opcode,\n" +
            "    const MathBlockSlot* const* inputs,\n" +
            "    int inputCount,\n" +
            "    MathBlockSlot* output);",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NotSupportedException", result.Source, StringComparison.Ordinal);
        Assert.Equal(
            1,
            result.Source.Split(
                "__device__ void mathblocks_operation_dispatch(",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ManagedDeviceOnlyMaintenanceContractsRejectExecution()
    {
        Assert.Throws<InvalidOperationException>(() => Cuda.GlobalTimer());
        unsafe
        {
            Assert.Throws<InvalidOperationException>(() => Cuda.VolatileLoad((int*)0));
            Assert.Throws<InvalidOperationException>(() => Cuda.VolatileLoad((ulong*)0));
            Assert.Throws<InvalidOperationException>(() => Cuda.VolatileLoadInt32((byte*)0, 0UL));
            Assert.Throws<InvalidOperationException>(() => Cuda.VolatileLoadUInt64((byte*)0, 0UL));
        }
    }

    [Fact]
    public void PublicApi_DoesNotRestoreRawSourceOrTranslationUnitContracts()
    {
        var rawSourceOverload = typeof(CudaTranspiler).GetMethods()
            .Where(method => method.Name == nameof(CudaTranspiler.Transpile))
            .SingleOrDefault(method => method.GetParameters().FirstOrDefault()?.ParameterType ==
                typeof(string));

        Assert.Null(rawSourceOverload);
        Assert.Null(typeof(CudaTranspiler).Assembly.GetType(
            "Supprocom.CSharp2CUDA.CudaTranslationUnitAttribute"));
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));

    private static void AssertOnlyInlineArrayDiagnostic(CudaTranspilationResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        var diagnostic = Assert.Single(result.Diagnostics.Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal("CS2CUDA024", diagnostic.Id);
    }
}

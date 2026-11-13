using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaInlineArrayTests
{
    private const string ShapesSource = """
        using System.Runtime.CompilerServices;
        using Supprocom.CSharp2CUDA;

        internal enum Mode : byte
        {
            Off,
            On
        }

        internal struct Pair
        {
            public int left;
            public int right;
        }

        [InlineArray(3)]
        internal struct Modes
        {
            private Mode _e0;
        }

        [InlineArray(2)]
        internal struct Pairs
        {
            private Pair _e0;
        }

        internal struct Holder
        {
            public Modes modes;
            public Pairs pairs;
            public long tail;
        }

        [TranspileToCUDA]
        internal static unsafe class InlineArrayShapesModule
        {
            [CudaDevice]
            private static int Pick(ref Holder holder, int index)
            {
                Mode current = holder.modes[0];
                holder.modes[index] = Mode.On;
                holder.modes[1] = current;
                holder.pairs[^1].right += 5;
                holder.pairs[0] = holder.pairs[1];
                return holder.pairs[index].left;
            }

            [CudaGlobal(Name = "inline_array_shapes")]
            private static void Run(Holder* holders, int* output)
            {
                output[0] = Pick(ref holders[0], 1);
            }
        }
        """;

    [Fact]
    public void TranspileEmitsMathBlocksInlineArrayExample()
    {
        var result = InlineArrayProbeSources.Transpile(InlineArrayProbeSources.UnitSource, "Unit.cs");

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Empty(result.Diagnostics.Where(static diagnostic =>
            diagnostic.Id.StartsWith("CS2CUDA", StringComparison.Ordinal)));
        Assert.Contains("int _e0[4];", result.Source, StringComparison.Ordinal);
        Assert.Contains("#pragma pack(push, 4)", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(Int4) == 16", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(alignof(Int4) == 4", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(Int4, _e0) == 0",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(DType) == 44", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(alignof(DType) == 4", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(DType, kind) == 0",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(DType, rows) == 4",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(DType, columns) == 8",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(DType, num) == 12",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(DType, den) == 28",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "static __device__ __forceinline__ int csharp2cuda_inline_index(int index, int length)",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "._e0[csharp2cuda_inline_index(i, 4)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("static_assert(offsetof(", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("stddef.h", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileAcceptsEnumStructureAndFromEndInlineArrayAccess()
    {
        var result = CudaTestCompiler.Transpile(ShapesSource);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("unsigned char _e0[3];", result.Source, StringComparison.Ordinal);
        Assert.Contains("Pair _e0[2];", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(Modes) == 3", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(alignof(Modes) == 1", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(Pairs) == 16", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(alignof(Pairs) == 4", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(Holder) == 32", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(Holder, pairs) == 4",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(Holder, tail) == 24",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("static_assert(offsetof(", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "csharp2cuda_inline_index(csharp2cuda_index_from_end(2, 1), 2)",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "csharp2cuda_inline_index(index, 3)",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileEmitsFixedBufferWithoutBoundsCheck()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            public unsafe struct F
            {
                public fixed int a[4];
            }

            [TranspileToCUDA]
            internal static unsafe class FixedBufferModule
            {
                [CudaDevice]
                private static int Read(F* value)
                {
                    value->a[1] = 5;
                    return value->a[1];
                }

                [CudaGlobal(Name = "fixed_buffer")]
                private static void Run(int* output, F* value)
                {
                    output[0] = Read(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("int a[4];", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(F) == 16", result.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(alignof(F) == 4", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(F, a) == 0",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_inline_index", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileAcceptsUnreferencedTopLevelStructureInNonAttributedMode()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            public struct Unreferenced
            {
                public int first;
                public long second;
            }

            [TranspileToCUDA]
            internal static unsafe class TopLevelStructureModule
            {
                [CudaGlobal]
                private static void Run(int* output)
                {
                    output[0] = 1;
                }
            }
            """;

        var result = CudaTranspiler.Transpile(CudaTestCompiler.CreateCompilation(source));

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("struct Unreferenced", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspileStillRejectsOtherTopLevelMembersInNonAttributedMode()
    {
        const string source = """
            using Supprocom.CSharp2CUDA;

            public interface IUnsupported
            {
            }

            [TranspileToCUDA]
            internal static unsafe class TopLevelInterfaceModule
            {
                [CudaGlobal]
                private static void Run(int* output)
                {
                    output[0] = 1;
                }
            }
            """;

        var result = CudaTranspiler.Transpile(CudaTestCompiler.CreateCompilation(source));

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Id, "CS2CUDA005", StringComparison.Ordinal));
    }

    [Fact]
    public void TranspileRejectsInlineArrayWithUnsupportedElementType()
    {
        var result = CudaTestCompiler.Transpile(CreateInlineArrayModule(
            "[InlineArray(2)] internal struct Bad { private decimal _e0; }",
            "Bad* value"));

        AssertRejectedWith(result, "CS2CUDA041");
    }

    [Fact]
    public void TranspileRejectsInlineArrayLongerThanTheDefaultLimit()
    {
        var result = CudaTestCompiler.Transpile(CreateInlineArrayModule(
            "[InlineArray(4097)] internal struct Long { private int _e0; }",
            "Long* value"));

        AssertRejectedWith(result, "CS2CUDA043");
    }

    [Fact]
    public void TranspileAcceptsInlineArrayAtTheDefaultLimit()
    {
        var result = CudaTestCompiler.Transpile(CreateInlineArrayModule(
            "[InlineArray(4096)] internal struct Long { private int _e0; }",
            "Long* value"));

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains("int _e0[4096];", result.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void TranspileHonorsTheConfiguredInlineArrayLimit(int length, bool accepted)
    {
        var options = new CudaTranspilationOptions { MaxInlineArrayLength = 8 };

        var result = CudaTestCompiler.Transpile(
            CreateInlineArrayModule(
                $"[InlineArray({length})] internal struct Sized {{ private int _e0; }}",
                "Sized* value"),
            options);

        if (accepted)
            Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        else
            AssertRejectedWith(result, "CS2CUDA043");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TranspileRejectsANonPositiveInlineArrayLimitOption(int limit)
    {
        var options = new CudaTranspilationOptions { MaxInlineArrayLength = limit };

        Assert.Throws<ArgumentException>(() => CudaTestCompiler.Transpile(
            CreateInlineArrayModule(
                "[InlineArray(2)] internal struct Sized { private int _e0; }",
                "Sized* value"),
            options));
    }

    [Fact]
    public void TranspileRejectsInlineArrayWithAutomaticLayout()
    {
        var result = CudaTestCompiler.Transpile(CreateInlineArrayModule(
            "[InlineArray(4)] [StructLayout(LayoutKind.Auto)] internal struct Automatic { private int _e0; }",
            "Automatic* value"));

        AssertRejectedWith(result, "CS2CUDA044");
    }

    [Theory]
    [InlineData("Span<int> span = value; return span[0];")]
    [InlineData("ReadOnlySpan<int> span = value; return span[0];")]
    [InlineData("Span<int> span = value[1..3]; return span[0];")]
    public void TranspileReportsInlineArrayToSpanConversion(string statements)
    {
        var source = $$"""
            using System;
            using System.Runtime.CompilerServices;
            using Supprocom.CSharp2CUDA;

            [InlineArray(4)]
            internal struct Quad
            {
                private int _e0;
            }

            [TranspileToCUDA]
            internal static unsafe class InlineArraySpanModule
            {
                [CudaDevice]
                private static int Read(Quad value)
                {
                    {{statements}}
                }

                [CudaGlobal(Name = "inline_array_span")]
                private static void Run(int* output)
                {
                    Quad value = default;
                    output[0] = Read(value);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        AssertRejectedWith(result, "CS2CUDA045");
    }

    [Fact]
    public void TranspileKeepsOutputWithoutInlineArraysFreeOfInlineArrayHelpers()
    {
        const string source = """
            using System.Runtime.InteropServices;
            using Supprocom.CSharp2CUDA;

            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            internal struct Entry
            {
                public byte Kind;
                public int Value;
            }

            internal struct Plain
            {
                public int First;
                public long Second;
            }

            [TranspileToCUDA]
            internal static unsafe class PlainModule
            {
                [CudaDevice]
                private static long Sum(Plain value) => value.First + value.Second;

                [CudaGlobal]
                private static void Run(Entry* entries, long* output)
                {
                    Plain plain = default;
                    plain.First = entries[0].Value;
                    output[0] = Sum(plain);
                }
            }
            """;

        var result = CudaTestCompiler.Transpile(source);

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "static_assert(__builtin_offsetof(Entry, Kind) == 0",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "static_assert(__builtin_offsetof(Entry, Value) == 4",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("stddef.h", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("static_assert(offsetof(", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("static_assert(sizeof(Plain)", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("csharp2cuda_inline_index", result.Source, StringComparison.Ordinal);
    }

    private static string CreateInlineArrayModule(string declaration, string parameter) => $$"""
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using Supprocom.CSharp2CUDA;

        {{declaration}}

        [TranspileToCUDA]
        internal static unsafe class InlineArrayDeclarationModule
        {
            [CudaGlobal(Name = "inline_array_declaration")]
            private static void Run({{parameter}})
            {
            }
        }
        """;

    private static void AssertRejectedWith(CudaTranspilationResult result, string id)
    {
        Assert.False(result.Succeeded);
        Assert.Empty(result.Source);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Id, id, StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Id, "CS2CUDA005", StringComparison.Ordinal));
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));
}

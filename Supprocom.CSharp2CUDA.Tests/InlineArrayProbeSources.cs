namespace Supprocom.CSharp2CUDA.Tests;

internal static class InlineArrayProbeSources
{
    public const string AlgoSource = """
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        namespace Probe;
        [InlineArray(4)] public struct Int4 { private int _e0; }
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct DType { public int kind; public int rows; public int columns; public Int4 num; public Int4 den; }
        public static class Algo
        {
            public static void Add(ref DType o, in DType x, in DType y)
            {
                for (int i = 0; i < 4; i++)
                {
                    long n = (long)x.num[i] * y.den[i] + (long)y.num[i] * x.den[i];
                    long d = (long)x.den[i] * y.den[i];
                    if (n > int.MaxValue || n < int.MinValue || d > int.MaxValue) { o.kind = -1; return; }
                    o.num[i] = (int)n; o.den[i] = (int)d;
                }
                o.kind = x.kind; o.rows = x.rows; o.columns = x.columns;
            }
        }
        """;

    public const string UnitSource = """
        using Supprocom.CSharp2CUDA;
        using Probe;

        [TranspileToCUDA]
        internal static unsafe class Unit
        {
            [CudaDevice]
            public static void mathblocks_probe(int rule, [CudaReadOnly] int* p, [CudaReadOnly] DType* inputs, int count, DType* output)
                => Algo.Add(ref *output, in inputs[0], in inputs[1]);
        }
        """;

    public const string KernelsSource = """
        using Supprocom.CSharp2CUDA;
        using Probe;

        [TranspileToCUDA]
        internal static unsafe class ProbeKernels
        {
            [CudaGlobal(Name = "inline_array_add")]
            private static void RunAdd([CudaReadOnly] DType* inputs, DType* output, int count)
            {
                for (int index = 0; index < count; index++)
                    Algo.Add(ref output[index], in inputs[index * 2], in inputs[index * 2 + 1]);
            }

            [CudaGlobal(Name = "inline_array_layout")]
            private static void RunLayout(DType* probe, ulong* output)
            {
                ulong start = (ulong)probe;
                output[0] = (ulong)(probe + 1) - start;
                output[1] = (ulong)&probe->kind - start;
                output[2] = (ulong)&probe->rows - start;
                output[3] = (ulong)&probe->columns - start;
                output[4] = (ulong)&probe->num - start;
                output[5] = (ulong)&probe->den - start;
            }

            [CudaGlobal(Name = "inline_array_trap")]
            private static void RunTrap(DType* data, int index)
            {
                data->num[index] = 1;
            }
        }
        """;

    // Compiles the two files the way TranspileFiles does and transpiles every class as a unit,
    // so the top-level structures of the first file take the non-attributed input path.
    public static CudaTranspilationResult Transpile(string unitSource, string unitPath) =>
        CudaTranspiler.Transpile(CudaTestCompiler.AddSource(
            CudaTestCompiler.CreateCompilation(AlgoSource, "Algo.cs"),
            unitSource,
            unitPath));
}

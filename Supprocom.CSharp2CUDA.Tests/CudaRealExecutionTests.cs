using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaRealExecutionTests
{
    [CudaFact]
    public void Nvrtc_CompilesEveryAcceptedAdvancedProbe()
    {
        using var runtime = CreateRuntime();
        Assert.NotNull(runtime);
    }

    [CudaFact]
    public void Cuda_ExecutesFixedAndLaunchSizedSharedStorage()
    {
        using var runtime = CreateRuntime();
        var fixedOutput = runtime.Allocate<int>([0]);
        var dynamicOutput = runtime.Allocate<double>([0.0, 0.0]);
        try
        {
            runtime.Launch("fixed_shared", 1, 8, 0, fixedOutput);
            runtime.Launch("dynamic_shared", 1, 8, 96, dynamicOutput);

            Assert.Equal(47, runtime.Read<int>(fixedOutput, 1)[0]);
            Assert.Equal([32.0, 56.0], runtime.Read<double>(dynamicOutput, 2));
        }
        finally
        {
            runtime.Free(dynamicOutput);
            runtime.Free(fixedOutput);
        }
    }

    [CudaFact]
    public void Cuda_ExecutesAtomicContentionForEveryAcceptedTypeAndOperation()
    {
        using var runtime = CreateRuntime();
        var signed32 = runtime.Allocate<int>([0, 0, 0, 0, int.MaxValue]);
        var unsigned32 = runtime.Allocate<uint>([0, 0, 0, 0, uint.MaxValue]);
        var signed64 = runtime.Allocate<long>([0, 0, 0, 0, long.MaxValue]);
        var unsigned64 = runtime.Allocate<ulong>([0, 0, 0, 0, ulong.MaxValue]);
        try
        {
            runtime.Launch("atomic_contention", 1, 255, 0, signed32, unsigned32, signed64, unsigned64);

            Assert.Equal([255, 7, 9, 1, 0], runtime.Read<int>(signed32, 5));
            Assert.Equal([255u, 7u, 9u, 1u, 0u], runtime.Read<uint>(unsigned32, 5));
            Assert.Equal([255L, 7L, 9L, 1L, 0L], runtime.Read<long>(signed64, 5));
            Assert.Equal([255UL, 7UL, 9UL, 1UL, 0UL], runtime.Read<ulong>(unsigned64, 5));
        }
        finally
        {
            runtime.Free(unsigned64);
            runtime.Free(signed64);
            runtime.Free(unsigned32);
            runtime.Free(signed32);
        }
    }

    [CudaFact]
    public void Cuda_PublishesMappedCheckpointsForBothFenceScopes()
    {
        using var runtime = CreateRuntime();
        using var deviceFence = runtime.AllocateMappedInt32(2);
        using var systemFence = runtime.AllocateMappedInt32(2);

        runtime.Launch("device_checkpoint", 1, 1, 0, deviceFence.DevicePointer);
        Assert.Equal(42, deviceFence.Read(0));
        Assert.Equal(1, deviceFence.Read(1));

        runtime.LaunchAsync("system_checkpoint", 1, 1, 0, systemFence.DevicePointer);
        Assert.True(systemFence.WaitForValue(1, 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(84, systemFence.Read(0));
        runtime.Synchronize();
    }

    [CudaFact]
    public void Cuda_ExecutesPartialMaskWarpOperations()
    {
        using var runtime = CreateRuntime();
        var output = runtime.Allocate<int>(new int[32]);
        try
        {
            runtime.Launch("partial_warp", 1, 32, 0, output);

            var expected = Enumerable.Range(0, 32)
                .Select(lane => lane < 15 ? lane + 2 : lane == 15 ? 16 : -1)
                .ToArray();
            Assert.Equal(expected, runtime.Read<int>(output, 32));
        }
        finally
        {
            runtime.Free(output);
        }
    }

    [CudaFact]
    public void Cuda_ExactDoubleOperationsMatchManagedBits()
    {
        const double left = 1.0000000000000002;
        const double right = 1.0000000000000002;
        const double third = -1.0000000000000004;
        using var runtime = CreateRuntime();
        var output = runtime.Allocate<double>(new double[5]);
        try
        {
            runtime.Launch(
                "exact_double",
                1,
                1,
                0,
                BitConverter.DoubleToUInt64Bits(left),
                BitConverter.DoubleToUInt64Bits(right),
                BitConverter.DoubleToUInt64Bits(third),
                output);

            double[] expected =
            [
                Cuda.DoubleAddRoundNearest(left, right),
                Cuda.DoubleSubtractRoundNearest(left, right),
                Cuda.DoubleMultiplyRoundNearest(left, right),
                Cuda.DoubleDivideRoundNearest(left, right),
                Cuda.DoubleAddRoundNearest(
                    Cuda.DoubleMultiplyRoundNearest(left, right),
                    third)
            ];
            var actual = runtime.Read<double>(output, expected.Length);
            Assert.Equal(
                expected.Select(BitConverter.DoubleToUInt64Bits),
                actual.Select(BitConverter.DoubleToUInt64Bits));
            Assert.Equal(0UL, BitConverter.DoubleToUInt64Bits(actual[4]));
        }
        finally
        {
            runtime.Free(output);
        }
    }

    [CudaFact]
    public void Cuda_ExecutesNamedMathWithoutConsumerWrappers()
    {
        const double value = 0.5;
        using var runtime = CreateRuntime();
        var output = runtime.Allocate<double>(new double[5]);
        try
        {
            runtime.Launch(
                "named_math",
                1,
                1,
                0,
                BitConverter.DoubleToUInt64Bits(value),
                output);

            var actual = runtime.Read<double>(output, 5);
            Assert.Equal(double.LogP1(value), actual[0], 14);
            Assert.Equal(Math.Sqrt(value), actual[1], 14);
            Assert.Equal(Math.Exp(value), actual[2], 14);
            Assert.Equal(Math.Pow(value, 3.0), actual[3], 14);
            Assert.True(double.IsNaN(actual[4]));
        }
        finally
        {
            runtime.Free(output);
        }
    }

    [CudaFact]
    public void Cuda_ReadsDeviceConstantArrayInitializers()
    {
        using var runtime = CreateRuntime();
        var output = runtime.Allocate<int>(new int[3]);
        try
        {
            runtime.Launch("constant_array", 1, 3, 0, output);
            Assert.Equal([11, 22, 33], runtime.Read<int>(output, 3));
        }
        finally
        {
            runtime.Free(output);
        }
    }

    private static CudaTestRuntime CreateRuntime()
    {
        var result = CudaTestCompiler.Transpile(IntegrationSource);
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        return CudaTestRuntime.Create(result.Source);
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));

    private const string IntegrationSource = """
        using Supprocom.CSharp2CUDA;

        [TranspileToCUDA]
        internal static unsafe class RuntimeModule
        {
            [CudaConstant]
            private static readonly int[] ConstantValues = [11, 22, 33];

            [CudaGlobal(Name = "fixed_shared")]
            private static void FixedShared(int* output)
            {
                int lane = Cuda.ThreadIdx.X;
                int sharedTotal = Cuda.Shared<int>();
                int* sharedValues = Cuda.SharedArray<int>(8);
                sharedValues[lane] = lane + 1;
                if (lane == 0)
                    sharedTotal = 0;
                Cuda.SyncThreads();
                if (lane == 0)
                {
                    for (int index = 0; index < 8; index++)
                        sharedTotal += sharedValues[index];
                    output[0] = sharedTotal + ConstantValues[0];
                }
            }

            [CudaGlobal(Name = "dynamic_shared")]
            private static void DynamicShared(double* output)
            {
                int lane = Cuda.ThreadIdx.X;
                byte* storage = Cuda.DynamicSharedBytes(8);
                double* doubles = Cuda.DynamicSharedView<double>(storage, 0UL);
                int* integers = Cuda.DynamicSharedView<int>(storage, 16UL);
                doubles[lane] = lane + 0.5;
                integers[lane] = lane * 2;
                Cuda.SyncThreads();
                if (lane == 0)
                {
                    double doubleTotal = 0.0;
                    int integerTotal = 0;
                    for (int index = 0; index < 8; index++)
                    {
                        doubleTotal += doubles[index];
                        integerTotal += integers[index];
                    }
                    output[0] = doubleTotal;
                    output[1] = integerTotal;
                }
            }

            [CudaGlobal(Name = "atomic_contention")]
            private static void AtomicContention(
                int* signed32,
                uint* unsigned32,
                long* signed64,
                ulong* unsigned64)
            {
                int lane = Cuda.ThreadIdx.X;
                Cuda.AtomicAdd(ref signed32[0], 1);
                Cuda.AtomicExchange(ref signed32[1], 7);
                Cuda.AtomicCompareExchange(ref signed32[2], 0, 9);
                Cuda.AtomicXor(ref signed32[3], 1);
                Cuda.AtomicMin(ref signed32[4], lane);

                Cuda.AtomicAdd(ref unsigned32[0], 1u);
                Cuda.AtomicExchange(ref unsigned32[1], 7u);
                Cuda.AtomicCompareExchange(ref unsigned32[2], 0u, 9u);
                Cuda.AtomicXor(ref unsigned32[3], 1u);
                Cuda.AtomicMin(ref unsigned32[4], (uint)lane);

                Cuda.AtomicAdd(ref signed64[0], 1L);
                Cuda.AtomicExchange(ref signed64[1], 7L);
                Cuda.AtomicCompareExchange(ref signed64[2], 0L, 9L);
                Cuda.AtomicXor(ref signed64[3], 1L);
                Cuda.AtomicMin(ref signed64[4], (long)lane);

                Cuda.AtomicAdd(ref unsigned64[0], 1UL);
                Cuda.AtomicExchange(ref unsigned64[1], 7UL);
                Cuda.AtomicCompareExchange(ref unsigned64[2], 0UL, 9UL);
                Cuda.AtomicXor(ref unsigned64[3], 1UL);
                Cuda.AtomicMin(ref unsigned64[4], (ulong)(uint)lane);
            }

            [CudaGlobal(Name = "device_checkpoint")]
            private static void DeviceCheckpoint(int* output)
            {
                output[0] = 42;
                Cuda.ThreadFence();
                output[1] = 1;
                Cuda.NanoSleep(32u);
            }

            [CudaGlobal(Name = "system_checkpoint")]
            private static void SystemCheckpoint(int* output)
            {
                output[0] = 84;
                Cuda.ThreadFenceSystem();
                output[1] = 1;
                Cuda.NanoSleep(32u);
            }

            [CudaGlobal(Name = "partial_warp")]
            private static void PartialWarp(int* output)
            {
                int lane = Cuda.ThreadIdx.X;
                if (lane < 16)
                {
                    Cuda.SyncWarp(0x0000ffffu);
                    output[lane] = Cuda.ShuffleDownSync(
                        0x0000ffffu,
                        lane + 1,
                        1u,
                        16);
                }
                else
                {
                    output[lane] = -1;
                }
            }

            [CudaGlobal(Name = "exact_double")]
            private static void ExactDouble(
                double left,
                double right,
                double third,
                double* output)
            {
                output[0] = Cuda.DoubleAddRoundNearest(left, right);
                output[1] = Cuda.DoubleSubtractRoundNearest(left, right);
                output[2] = Cuda.DoubleMultiplyRoundNearest(left, right);
                output[3] = Cuda.DoubleDivideRoundNearest(left, right);
                output[4] = Cuda.DoubleAddRoundNearest(
                    Cuda.DoubleMultiplyRoundNearest(left, right),
                    third);
            }

            [CudaGlobal(Name = "named_math")]
            private static void NamedMath(double value, double* output)
            {
                output[0] = Cuda.Log1p(value);
                output[1] = Cuda.Sqrt(value);
                output[2] = Cuda.Exp(value);
                output[3] = Cuda.Pow(value, 3.0);
                output[4] = Cuda.NaN();
            }

            [CudaGlobal(Name = "constant_array")]
            private static void ConstantArray(int* output)
            {
                int lane = Cuda.ThreadIdx.X;
                if (lane < 3)
                    output[lane] = ConstantValues[lane];
            }
        }
        """;
}

using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaMaintenanceRealExecutionTests
{
    private const int SlotCount = 2;
    private const int SlotStride = 32;
    private const int AckOffset = 64;
    private const int CompleteOffset = 72;
    private const int CompleteSequenceOffset = 80;
    private const int SequenceCount = 8;

    [CudaFact]
    public async Task Cuda_MappedRingObservesHostClearsAndRejectsStaleHeaders()
    {
        var result = CudaTestCompiler.Transpile(MappedRingSource, path: "MappedRingModule.cs");
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        RetainSource("mapped-ring.generated.cu", result.Source);
        using var runtime = CudaTestRuntime.Create(result.Source);
        using var mapped = runtime.AllocateMappedMemory(96);
        var observed = new List<ulong>();

        var worker = Task.Run(() =>
        {
            for (var expected = 1; expected <= SequenceCount; expected++)
            {
                var slot = (expected - 1) % SlotCount;
                var slotOffset = slot * SlotStride;
                Assert.True(
                    mapped.WaitForInt32(slotOffset, 1, TimeSpan.FromSeconds(10)),
                    $"Mapped slot {slot} did not become ready for sequence {expected}.");
                var sequence = mapped.ReadUInt64(slotOffset + 8);
                var header = mapped.ReadInt32(slotOffset + 20);
                var payload = mapped.ReadInt32(slotOffset + 16);
                Assert.Equal((ulong)expected, sequence);
                Assert.Equal(expected, header);
                Assert.Equal(expected * 10, payload);
                observed.Add(sequence);
                mapped.WriteInt32(slotOffset, 0);
                mapped.WriteUInt64(AckOffset, (ulong)expected);
            }
        });

        runtime.LaunchAsync("mapped_ring", 1, 1, 0, mapped.DevicePointer);
        await worker.WaitAsync(TimeSpan.FromSeconds(15));
        runtime.Synchronize();

        Assert.Equal(
            Enumerable.Range(1, SequenceCount).Select(static value => (ulong)value),
            observed);
        Assert.Equal(1, mapped.ReadInt32(CompleteOffset));
        Assert.Equal((ulong)SequenceCount, mapped.ReadUInt64(CompleteSequenceOffset));
    }

    [CudaFact]
    public void Cuda_GlobalTimerProducesStrictlyOrderedReadings()
    {
        var result = CudaTestCompiler.Transpile(GlobalTimerSource, path: "GlobalTimerModule.cs");
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "asm volatile(\"mov.u64 %0, %%globaltimer;\" : \"=l\"(value));",
            result.Source,
            StringComparison.Ordinal);
        RetainSource("global-timer.generated.cu", result.Source);
        using var runtime = CudaTestRuntime.Create(result.Source);
        var output = runtime.Allocate<ulong>(new ulong[8]);
        try
        {
            runtime.Launch("global_timer_order", 1, 1, 0, output);
            var readings = runtime.Read<ulong>(output, 8);
            for (var index = 1; index < readings.Length; index++)
                Assert.True(readings[index] > readings[index - 1]);
        }
        finally
        {
            runtime.Free(output);
        }
    }

    [CudaFact]
    public void Cuda_InlineArrayAbiMatchesNativeReference()
    {
        var result = CudaTestCompiler.Transpile(InlineArraySource, path: "InlineArrayModule.cs");
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        RetainSource("inline-array-abi.generated.cu", result.Source);
        var nativeSource = ReadNativeSource("InlineArrayAbiReference.cu");
        var generated = RunAbiProbe(result.Source, "generated_inline_array_abi");
        var native = RunAbiProbe(nativeSource, "native_inline_array_abi");

        Assert.Equal(native, generated);
        Assert.Equal(native[1], generated[1]);
        Assert.Equal([17UL, 23UL, 29UL, 31UL, 37UL, 41UL], generated[10..16]);
    }

    [CudaFact]
    public void Cuda_LinksExternalDeviceProducerAndConsumerUnits()
    {
        var result = CudaTestCompiler.Transpile(
            ExternalDeviceConsumerSource,
            path: "ExternalDeviceConsumerModule.cs");
        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.Contains(
            "const MathBlockSlot* const* inputs",
            result.Source,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            result.Source.Split(
                "__device__ void mathblocks_operation_dispatch(",
                StringSplitOptions.None).Length - 1);
        var producer = ReadNativeSource("ExternalDeviceProducer.cu");
        RetainSource("external-device-consumer.generated.cu", result.Source);
        RetainSource("external-device-producer.cu", producer);

        using var runtime = CudaTestRuntime.CreateLinked(result.Source, producer);
        var output = runtime.Allocate<double>([0.0, 0.0]);
        try
        {
            runtime.Launch("external_device_consumer", 1, 1, 0, output);
            var actual = runtime.Read<double>(output, 2);
            Assert.Equal(13.75, actual[0]);
            Assert.Equal(1.0, actual[1]);
            RetainText(
                "external-device-linkage.txt",
                $"consumer=generated{Environment.NewLine}" +
                $"producer=native{Environment.NewLine}" +
                $"scalar={actual[0].ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}" +
                $"valid={actual[1].ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}");
        }
        finally
        {
            runtime.Free(output);
        }
    }

    [ExactPackageCudaFact]
    public void Cuda_ExactPackageMtsBoundaryCompilesAndLinks()
    {
        var generatedPath = Environment.GetEnvironmentVariable(
            "CSHARP2CUDA_EXACT_PACKAGE_CUDA");
        Assert.False(string.IsNullOrWhiteSpace(generatedPath));
        var generated = File.ReadAllText(generatedPath);
        var producer = ReadNativeSource("ExternalDeviceProducer.cu");

        using var runtime = CudaTestRuntime.CreateLinked(generated, producer);
        RetainText(
            "exact-package-cuda-linkage.txt",
            $"consumer={Path.GetFullPath(generatedPath)}{Environment.NewLine}" +
            $"producer=ExternalDeviceProducer.cu{Environment.NewLine}" +
            $"link=pass{Environment.NewLine}");
    }

    private static ulong[] RunAbiProbe(string source, string functionName)
    {
        using var runtime = CudaTestRuntime.Create(source);
        var storage = runtime.Allocate<byte>(new byte[512]);
        var output = runtime.Allocate<ulong>(new ulong[16]);
        try
        {
            runtime.Launch(functionName, 1, 1, 0, storage, output);
            return runtime.Read<ulong>(output, 16);
        }
        finally
        {
            runtime.Free(output);
            runtime.Free(storage);
        }
    }

    private static string ReadNativeSource(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Native", fileName));

    private static void RetainSource(string fileName, string source) =>
        RetainText(fileName, source);

    private static void RetainText(string fileName, string text)
    {
        var directory = Environment.GetEnvironmentVariable("CSHARP2CUDA_EVIDENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), text);
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            diagnostic.ToString()));

    private const string MappedRingSource = """
        using Supprocom.CSharp2CUDA;

        [TranspileToCUDA]
        internal static unsafe class MappedRingModule
        {
            [CudaGlobal(Name = "mapped_ring")]
            private static void Run(byte* mapped)
            {
                for (int sequence = 1; sequence <= 8; sequence++)
                {
                    int slot = (sequence - 1) % 2;
                    int slotOffset = slot * 32;
                    while (Cuda.VolatileLoadInt32(mapped, (ulong)slotOffset) != 0)
                        Cuda.NanoSleep(128u);
                    while (Cuda.VolatileLoad((ulong*)(mapped + 64)) < (ulong)(sequence - 1))
                        Cuda.NanoSleep(128u);

                    Cuda.VolatileStoreUInt64(mapped, (ulong)(slotOffset + 8), (ulong)sequence);
                    Cuda.VolatileStore((int*)(mapped + slotOffset + 16), sequence * 10);
                    Cuda.VolatileStoreInt32(mapped, (ulong)(slotOffset + 20), sequence);
                    Cuda.ThreadFenceSystem();
                    Cuda.VolatileStore((int*)(mapped + slotOffset), 1);
                    Cuda.ThreadFenceSystem();
                }

                while (Cuda.VolatileLoad((ulong*)(mapped + 64)) < 8UL)
                    Cuda.NanoSleep(128u);
                Cuda.VolatileStore((ulong*)(mapped + 80), 8UL);
                Cuda.ThreadFenceSystem();
                Cuda.VolatileStoreInt32(mapped, 72UL, 1);
                Cuda.ThreadFenceSystem();
            }
        }
        """;

    private const string GlobalTimerSource = """
        using Supprocom.CSharp2CUDA;

        [TranspileToCUDA]
        internal static unsafe class GlobalTimerModule
        {
            [CudaGlobal(Name = "global_timer_order")]
            private static void Run(ulong* output)
            {
                for (int index = 0; index < 8; index++)
                {
                    output[index] = Cuda.GlobalTimer();
                    Cuda.NanoSleep(1000u);
                }
            }
        }
        """;

    private const string InlineArraySource = """
        using Supprocom.CSharp2CUDA;

        [TranspileToCUDA]
        internal static unsafe class InlineArrayModule
        {
            public struct InlineElement
            {
                public int code;
                public ulong value;
            }

            public struct InlinePayload
            {
                public byte prefix;

                [CudaInlineArray(3)]
                public int* input_kinds;

                [CudaInlineArray(3)]
                public int* operands;

                [CudaInlineArray(3)]
                public int* operand_kinds;

                [CudaInlineArray(5)]
                public InlineElement* nodes;

                [CudaInlineArray(4)]
                public ulong* runtime_invalid_by_operation;

                public int suffix;
            }

            public struct InlineAlignmentProbe
            {
                public byte prefix;
                public InlinePayload payload;
            }

            [CudaGlobal(Name = "generated_inline_array_abi")]
            private static void Run(byte* storage, ulong* output)
            {
                InlineAlignmentProbe* probe = (InlineAlignmentProbe*)storage;
                InlinePayload* value = &probe->payload;
                ulong address = (ulong)value;
                output[0] = (ulong)(value + 1) - address;
                output[1] = (ulong)&probe->payload - (ulong)probe;
                output[2] = (ulong)&value->input_kinds[0] - address;
                output[3] = (ulong)&value->operands[0] - address;
                output[4] = (ulong)&value->operand_kinds[0] - address;
                output[5] = (ulong)&value->nodes[0] - address;
                output[6] = (ulong)&value->runtime_invalid_by_operation[0] - address;
                output[7] = (ulong)&value->suffix - address;
                output[8] = (ulong)(&value->nodes[1] + 1) - (ulong)&value->nodes[1];
                output[9] = (ulong)&value->nodes[1].value - (ulong)&value->nodes[1];

                value->input_kinds[2] = 17;
                value->operand_kinds[1] = 23;
                value->runtime_invalid_by_operation[3] = 29UL;
                int* primitive = value->operands;
                primitive[2] = 31;
                InlineElement* structures = value->nodes;
                structures[4].code = 37;
                structures[4].value = 41UL;
                output[10] = (ulong)value->input_kinds[2];
                output[11] = (ulong)value->operand_kinds[1];
                output[12] = value->runtime_invalid_by_operation[3];
                output[13] = (ulong)primitive[2];
                output[14] = (ulong)structures[4].code;
                output[15] = structures[4].value;
            }
        }
        """;

    private const string ExternalDeviceConsumerSource = """
        using System;
        using Supprocom.CSharp2CUDA;

        [TranspileToCUDA]
        internal static unsafe class ExternalDeviceConsumerModule
        {
            public struct MathBlockSlot
            {
                public double scalar_value;
                public ulong data_pointer;
                public ulong scratch_pointer;
                public int boolean_value;
                public int valid;
                public int rows;
                public int columns;
                public int count;
                public int capacity;
            }

            [CudaExternalDevice(Name = "mathblocks_operation_dispatch")]
            private static void Dispatch(
                int family,
                int opcode,
                [CudaReadOnly] MathBlockSlot** inputs,
                int input_count,
                MathBlockSlot* output) => throw new NotSupportedException();

            [CudaGlobal(Name = "external_device_consumer")]
            private static void Run(double* output)
            {
                MathBlockSlot* slots = stackalloc MathBlockSlot[3];
                slots[0].scalar_value = 1.5;
                slots[1].scalar_value = 2.25;
                MathBlockSlot** inputs = stackalloc MathBlockSlot*[2];
                inputs[0] = &slots[0];
                inputs[1] = &slots[1];
                Dispatch(3, 5, inputs, 2, &slots[2]);
                output[0] = slots[2].scalar_value;
                output[1] = (double)slots[2].valid;
            }
        }
        """;
}

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tests;

public sealed class CudaFactAttribute : FactAttribute
{
    public CudaFactAttribute()
    {
        if (!CudaTestRuntime.IsAvailable(out var reason))
            Skip = reason;
    }
}

public sealed class ExactPackageCudaFactAttribute : FactAttribute
{
    public ExactPackageCudaFactAttribute()
    {
        if (!CudaTestRuntime.IsAvailable(out var reason))
            Skip = reason;
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                     "CSHARP2CUDA_EXACT_PACKAGE_CUDA")))
            Skip = "The exact-package CUDA source is not available.";
    }
}

internal sealed unsafe class CudaTestRuntime : IDisposable
{
    private const int ComputeCapabilityMajorAttribute = 75;
    private const int ComputeCapabilityMinorAttribute = 76;
    private const uint ContextMapHost = 8;
    private const uint HostAllocatePortable = 1;
    private const uint HostAllocateDeviceMap = 2;
    private static readonly object InitializationLock = new();
    private static bool initialized;
    private static nint nvrtcHandle;
    private static string unavailableReason = "CUDA initialization did not run.";
    private readonly nint context;
    private readonly nint module;
    private bool disposed;

    private CudaTestRuntime(params byte[][] ptxUnits)
    {
        Require(cuDeviceGet(out var device, 0), "cuDeviceGet");
        Require(cuCtxCreate(out context, ContextMapHost, device), "cuCtxCreate");
        try
        {
            var image = ptxUnits.Length == 1 ? ptxUnits[0] : LinkPtx(ptxUnits);
            Require(cuModuleLoadData(out module, image), "cuModuleLoadData");
        }
        catch
        {
            _ = cuCtxDestroy(context);
            throw;
        }
    }

    public static bool IsAvailable(out string reason)
    {
        EnsureInitialized();
        reason = unavailableReason;
        return nvrtcHandle != 0 && string.IsNullOrEmpty(unavailableReason);
    }

    public static CudaTestRuntime Create(string cudaSource)
    {
        EnsureInitialized();
        if (!IsAvailable(out var reason))
            throw new InvalidOperationException(reason);
        return new CudaTestRuntime(CompilePtx(cudaSource));
    }

    public static CudaTestRuntime CreateLinked(params string[] cudaSources)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cudaSources.Length, 2);
        EnsureInitialized();
        if (!IsAvailable(out var reason))
            throw new InvalidOperationException(reason);
        var units = cudaSources.Select((source, index) =>
            CompilePtx(source, $"csharp2cuda-linked-{index}.cu", true)).ToArray();
        return new CudaTestRuntime(units);
    }

    public ulong Allocate<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        SetCurrent();
        var bytes = checked(values.Length * Unsafe.SizeOf<T>());
        Require(cuMemAlloc(out var pointer, (nuint)bytes), "cuMemAlloc");
        try
        {
            fixed (T* source = values)
                Require(cuMemcpyHtoD(pointer, (nint)source, (nuint)bytes), "cuMemcpyHtoD");
            return pointer;
        }
        catch
        {
            _ = cuMemFree(pointer);
            throw;
        }
    }

    public T[] Read<T>(ulong pointer, int length) where T : unmanaged
    {
        SetCurrent();
        var result = new T[length];
        var bytes = checked(length * Unsafe.SizeOf<T>());
        fixed (T* destination = result)
            Require(cuMemcpyDtoH((nint)destination, pointer, (nuint)bytes), "cuMemcpyDtoH");
        return result;
    }

    public void Free(ulong pointer)
    {
        SetCurrent();
        Require(cuMemFree(pointer), "cuMemFree");
    }

    public MappedInt32Memory AllocateMappedInt32(int length)
    {
        SetCurrent();
        var bytes = checked(length * sizeof(int));
        Require(
            cuMemHostAlloc(
                out var hostPointer,
                (nuint)bytes,
                HostAllocatePortable | HostAllocateDeviceMap),
            "cuMemHostAlloc");
        try
        {
            new Span<byte>((void*)hostPointer, bytes).Clear();
            Require(
                cuMemHostGetDevicePointer(out var devicePointer, hostPointer, 0),
                "cuMemHostGetDevicePointer");
            return new MappedInt32Memory(hostPointer, devicePointer, length);
        }
        catch
        {
            _ = cuMemFreeHost(hostPointer);
            throw;
        }
    }

    public MappedMemory AllocateMappedMemory(int byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        SetCurrent();
        Require(
            cuMemHostAlloc(
                out var hostPointer,
                (nuint)byteLength,
                HostAllocatePortable | HostAllocateDeviceMap),
            "cuMemHostAlloc");
        try
        {
            new Span<byte>((void*)hostPointer, byteLength).Clear();
            Require(
                cuMemHostGetDevicePointer(out var devicePointer, hostPointer, 0),
                "cuMemHostGetDevicePointer");
            return new MappedMemory(hostPointer, devicePointer, byteLength);
        }
        catch
        {
            _ = cuMemFreeHost(hostPointer);
            throw;
        }
    }

    public void Launch(
        string functionName,
        uint gridSize,
        uint blockSize,
        uint dynamicSharedBytes,
        params ulong[] arguments)
    {
        LaunchAsync(functionName, gridSize, blockSize, dynamicSharedBytes, arguments);
        Synchronize();
    }

    public void LaunchAsync(
        string functionName,
        uint gridSize,
        uint blockSize,
        uint dynamicSharedBytes,
        params ulong[] arguments)
    {
        SetCurrent();
        Require(cuModuleGetFunction(out var function, module, functionName), "cuModuleGetFunction");
        var values = stackalloc ulong[Math.Max(1, arguments.Length)];
        var pointers = stackalloc nint[Math.Max(1, arguments.Length)];
        for (var index = 0; index < arguments.Length; index++)
        {
            values[index] = arguments[index];
            pointers[index] = (nint)(values + index);
        }
        Require(
            cuLaunchKernel(
                function,
                gridSize,
                1,
                1,
                blockSize,
                1,
                1,
                dynamicSharedBytes,
                0,
                (nint)pointers,
                0),
            "cuLaunchKernel");
    }

    public void Synchronize()
    {
        SetCurrent();
        Require(cuCtxSynchronize(), "cuCtxSynchronize");
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        _ = cuCtxSetCurrent(context);
        _ = cuModuleUnload(module);
        _ = cuCtxDestroy(context);
    }

    private static void EnsureInitialized()
    {
        lock (InitializationLock)
        {
            if (initialized)
                return;
            initialized = true;
            try
            {
                if (!TryLoadNvrtc(out nvrtcHandle, out unavailableReason))
                    return;
                NativeLibrary.SetDllImportResolver(
                    typeof(CudaTestRuntime).Assembly,
                    ResolveLibrary);
                Require(cuInit(0), "cuInit");
                Require(cuDeviceGet(out var device, 0), "cuDeviceGet");
                Require(
                    cuDeviceGetAttribute(
                        out _,
                        ComputeCapabilityMajorAttribute,
                        device),
                    "cuDeviceGetAttribute");
                unavailableReason = string.Empty;
            }
            catch (Exception exception)
            {
                unavailableReason = $"CUDA tests are unavailable: {exception.Message}";
                nvrtcHandle = 0;
            }
        }
    }

    private static bool TryLoadNvrtc(out nint handle, out string reason)
    {
        var candidates = new List<string>();
        var configuredLibrary = Environment.GetEnvironmentVariable(
            "CSHARP2CUDA_NVRTC_LIBRARY");
        if (!string.IsNullOrWhiteSpace(configuredLibrary))
            candidates.Add(configuredLibrary);

        var configuredDirectory = Environment.GetEnvironmentVariable(
            "CSHARP2CUDA_CUDA_NATIVE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            candidates.Add(Path.Combine(configuredDirectory, "nvrtc64_120_0.dll"));
            candidates.Add(Path.Combine(configuredDirectory, "libnvrtc.so.12"));
            candidates.Add(Path.Combine(configuredDirectory, "libnvrtc.so"));
        }

        if (OperatingSystem.IsWindows())
        {
            var packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
            AddPackageCandidates(packageRoot, "nvrtc64_120_0.dll", candidates);
            AddNativeDirectoriesToPath(packageRoot);
        }
        else
        {
            candidates.Add("libnvrtc.so.13");
            candidates.Add("libnvrtc.so.12");
            candidates.Add("libnvrtc.so");
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (NativeLibrary.TryLoad(candidate, out handle))
            {
                reason = string.Empty;
                return true;
            }
        }

        handle = 0;
        reason = "CUDA tests are unavailable because NVRTC was not found.";
        return false;
    }

    private static void AddPackageCandidates(
        string packageRoot,
        string fileName,
        ICollection<string> candidates)
    {
        if (!Directory.Exists(packageRoot))
            return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         packageRoot,
                         fileName,
                         SearchOption.AllDirectories))
            {
                candidates.Add(file);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AddNativeDirectoriesToPath(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
            return;
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in new[] { "nvrtc-builtins*.dll", "nvJitLink*.dll" })
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             packageRoot,
                             pattern,
                             SearchOption.AllDirectories))
                {
                    directories.Add(Path.GetDirectoryName(file)!);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        if (directories.Count == 0)
            return;
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable(
            "PATH",
            string.Join(Path.PathSeparator, directories) + Path.PathSeparator + currentPath);
    }

    private static nint ResolveLibrary(
        string libraryName,
        System.Reflection.Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        return libraryName == "nvrtc64_120_0.dll" ? nvrtcHandle : 0;
    }

    private static byte[] CompilePtx(
        string source,
        string programName = "csharp2cuda-tests.cu",
        bool relocatable = false)
    {
        Require(
            nvrtcCreateProgram(out var program, source, programName, 0, null, null),
            "nvrtcCreateProgram");
        try
        {
            Require(cuDeviceGet(out var device, 0), "cuDeviceGet");
            Require(
                cuDeviceGetAttribute(
                    out var major,
                    ComputeCapabilityMajorAttribute,
                    device),
                "cuDeviceGetAttribute");
            Require(
                cuDeviceGetAttribute(
                    out var minor,
                    ComputeCapabilityMinorAttribute,
                    device),
                "cuDeviceGetAttribute");
            var options = new List<string>
            {
                $"--gpu-architecture=compute_{major}{minor}",
                "--std=c++17",
                "--fmad=false",
                "--prec-div=true",
                "--prec-sqrt=true"
            };
            if (relocatable)
                options.Add("--relocatable-device-code=true");
            var result = nvrtcCompileProgram(program, options.Count, [.. options]);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"nvrtcCompileProgram failed with result {result}. {GetProgramLog(program)}");
            }
            Require(nvrtcGetPTXSize(program, out var size), "nvrtcGetPTXSize");
            var ptx = GC.AllocateUninitializedArray<byte>(checked((int)size));
            Require(nvrtcGetPTX(program, ptx), "nvrtcGetPTX");
            return ptx;
        }
        finally
        {
            _ = nvrtcDestroyProgram(ref program);
        }
    }

    private static byte[] LinkPtx(IReadOnlyList<byte[]> units)
    {
        Require(cuLinkCreate(0, 0, 0, out var state), "cuLinkCreate");
        try
        {
            for (var index = 0; index < units.Count; index++)
            {
                fixed (byte* data = units[index])
                {
                    Require(
                        cuLinkAddData(
                            state,
                            1,
                            (nint)data,
                            (nuint)units[index].Length,
                            $"csharp2cuda-linked-{index}.ptx",
                            0,
                            0,
                            0),
                        "cuLinkAddData");
                }
            }
            Require(cuLinkComplete(state, out var image, out var size), "cuLinkComplete");
            return new ReadOnlySpan<byte>((void*)image, checked((int)size)).ToArray();
        }
        finally
        {
            _ = cuLinkDestroy(state);
        }
    }

    private static string GetProgramLog(nint program)
    {
        _ = nvrtcGetProgramLogSize(program, out var size);
        var bytes = new byte[checked((int)size)];
        _ = nvrtcGetProgramLog(program, bytes);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private void SetCurrent() => Require(cuCtxSetCurrent(context), "cuCtxSetCurrent");

    private static void Require(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation} failed with result {result}.");
    }

    [DllImport("nvrtc64_120_0.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int nvrtcCreateProgram(
        out nint program,
        string source,
        string name,
        int headerCount,
        string[]? headers,
        string[]? includeNames);

    [DllImport("nvrtc64_120_0.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvrtcCompileProgram(
        nint program,
        int optionCount,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] options);

    [DllImport("nvrtc64_120_0.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvrtcGetPTXSize(nint program, out nuint size);

    [DllImport("nvrtc64_120_0.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvrtcGetPTX(nint program, byte[] ptx);

    [DllImport("nvrtc64_120_0.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvrtcGetProgramLogSize(nint program, out nuint size);

    [DllImport("nvrtc64_120_0.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvrtcGetProgramLog(nint program, byte[] log);

    [DllImport("nvrtc64_120_0.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int nvrtcDestroyProgram(ref nint program);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuInit(uint flags);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuDeviceGet(out int device, int ordinal);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuDeviceGetAttribute(out int value, int attribute, int device);

    [DllImport("nvcuda.dll", EntryPoint = "cuCtxCreate_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuCtxCreate(out nint context, uint flags, int device);

    [DllImport("nvcuda.dll", EntryPoint = "cuCtxDestroy_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuCtxDestroy(nint context);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuCtxSetCurrent(nint context);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuCtxSynchronize();

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuModuleLoadData(out nint module, byte[] image);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int cuModuleGetFunction(out nint function, nint module, string name);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuModuleUnload(nint module);

    [DllImport("nvcuda.dll", EntryPoint = "cuLinkCreate_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuLinkCreate(
        uint optionCount,
        nint options,
        nint optionValues,
        out nint state);

    [DllImport("nvcuda.dll", EntryPoint = "cuLinkAddData_v2", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int cuLinkAddData(
        nint state,
        int inputType,
        nint data,
        nuint size,
        string name,
        uint optionCount,
        nint options,
        nint optionValues);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuLinkComplete(nint state, out nint image, out nuint size);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuLinkDestroy(nint state);

    [DllImport("nvcuda.dll", EntryPoint = "cuMemAlloc_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuMemAlloc(out ulong devicePointer, nuint bytes);

    [DllImport("nvcuda.dll", EntryPoint = "cuMemFree_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuMemFree(ulong devicePointer);

    [DllImport("nvcuda.dll", EntryPoint = "cuMemcpyHtoD_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuMemcpyHtoD(ulong destination, nint source, nuint bytes);

    [DllImport("nvcuda.dll", EntryPoint = "cuMemcpyDtoH_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuMemcpyDtoH(nint destination, ulong source, nuint bytes);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuMemHostAlloc(out nint pointer, nuint bytes, uint flags);

    [DllImport("nvcuda.dll", EntryPoint = "cuMemHostGetDevicePointer_v2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuMemHostGetDevicePointer(
        out ulong devicePointer,
        nint hostPointer,
        uint flags);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuMemFreeHost(nint pointer);

    [DllImport("nvcuda.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cuLaunchKernel(
        nint function,
        uint gridX,
        uint gridY,
        uint gridZ,
        uint blockX,
        uint blockY,
        uint blockZ,
        uint sharedMemoryBytes,
        nint stream,
        nint kernelParameters,
        nint extra);

    internal sealed class MappedInt32Memory(nint hostPointer, ulong devicePointer, int length) :
        IDisposable
    {
        private bool disposed;

        public ulong DevicePointer { get; } = devicePointer;

        public int Read(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, length);
            return Volatile.Read(ref Unsafe.AsRef<int>((void*)(hostPointer + index * sizeof(int))));
        }

        public bool WaitForValue(int index, int expected, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (Read(index) == expected)
                    return true;
                Thread.SpinWait(64);
            }
            return false;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            _ = cuMemFreeHost(hostPointer);
        }
    }

    internal sealed class MappedMemory(nint hostPointer, ulong devicePointer, int byteLength) :
        IDisposable
    {
        private bool disposed;

        public ulong DevicePointer { get; } = devicePointer;

        public int ReadInt32(int byteOffset)
        {
            ValidateAccess(byteOffset, sizeof(int));
            return Volatile.Read(ref Unsafe.AsRef<int>((void*)(hostPointer + byteOffset)));
        }

        public ulong ReadUInt64(int byteOffset)
        {
            ValidateAccess(byteOffset, sizeof(ulong));
            return unchecked((ulong)Volatile.Read(
                ref Unsafe.AsRef<long>((void*)(hostPointer + byteOffset))));
        }

        public void WriteInt32(int byteOffset, int value)
        {
            ValidateAccess(byteOffset, sizeof(int));
            Volatile.Write(ref Unsafe.AsRef<int>((void*)(hostPointer + byteOffset)), value);
        }

        public void WriteUInt64(int byteOffset, ulong value)
        {
            ValidateAccess(byteOffset, sizeof(ulong));
            Volatile.Write(
                ref Unsafe.AsRef<long>((void*)(hostPointer + byteOffset)),
                unchecked((long)value));
        }

        public bool WaitForInt32(int byteOffset, int expected, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (ReadInt32(byteOffset) == expected)
                    return true;
                Thread.SpinWait(64);
            }
            return false;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            _ = cuMemFreeHost(hostPointer);
        }

        private void ValidateAccess(int byteOffset, int size)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteOffset);
            if (byteOffset > byteLength - size || byteOffset % size != 0)
                throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }
    }
}

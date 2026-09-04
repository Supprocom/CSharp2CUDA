# Supprocom.CSharp2CUDA

Write CUDA kernels as normal, compile-checked C# files. CSharp2CUDA uses Roslyn
to validate the code and emit deterministic CUDA C++.

This package is a transpiler, not a CUDA runtime. Your application still
compiles CUDA, owns memory, launches kernels, and manages streams.

## Start with one project

Create a .NET 10 class library and add the package. The project below checks
its C# code but produces CUDA instead of a managed assembly.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TranspileToCUDA>true</TranspileToCUDA>
    <TranspileToCUDAOutputPath>cuda/SquareValues.cu</TranspileToCUDAOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Supprocom.CSharp2CUDA" Version="0.2.1" />
  </ItemGroup>
</Project>
```

Add an ordinary `.cs` file. Visual Studio and Roslyn check the types, symbols,
attributes, and method bodies before CUDA emission.

```csharp
using Supprocom.CSharp2CUDA;

internal static unsafe class SquareValuesModule
{
    [CudaDevice]
    private static int Square(int value)
    {
        return value * value;
    }

    [CudaGlobal(Name = "square_values")]
    private static void SquareValues(int* input, int* output)
    {
        int index = Cuda.BlockIdx.X * Cuda.BlockDim.X + Cuda.ThreadIdx.X;
        output[index] = Square(input[index]);
    }
}
```

Build the project in Visual Studio 2026 or run this command.

```text
dotnet build -c Release
```

The build writes `cuda/SquareValues.cu` below the assembly output directory.
It emits `square_values` as an `extern "C" __global__` function.

## Choose the input that fits your code

All four inputs use Roslyn symbols. No public API accepts an unchecked C# string.

| Input | Best use | Result |
| --- | --- | --- |
| `<TranspileToCUDA>true</TranspileToCUDA>` | A project that exists only to produce one CUDA module | The build writes one `.cu` file and suppresses the managed assembly. |
| `[TranspileToCUDA]` | Selected classes inside a normal managed project | The build keeps the managed assembly and writes one `.cu` file. |
| `TranspileFile` or `TranspileFiles` | A tool that selects normal `.cs` files itself | The call returns CUDA source and diagnostics without writing a file. |
| `Transpile(CSharpCompilation)` | A Roslyn tool that already owns a complete compilation | The call uses the supplied syntax trees, references, options, and generator output. |

## Keep the managed assembly

Mark a class when only part of a managed project must become CUDA. The path is
relative to the managed assembly directory.

```csharp
using Supprocom.CSharp2CUDA;

[TranspileToCUDA("cuda/SquareValues.cu")]
internal static unsafe class SquareValuesModule
{
    [CudaGlobal(Name = "square_values")]
    private static void SquareValues(int* input, int* output)
    {
        int index = Cuda.BlockIdx.X * Cuda.BlockDim.X + Cuda.ThreadIdx.X;
        output[index] = input[index] * input[index];
    }
}
```

Use `[TranspileToCUDA]` for the default `<AssemblyName>.cu` path. All marked
classes in one project become one CUDA module.

## Select files manually

Use the manual API when another tool controls file selection. It returns empty
source when Roslyn or CSharp2CUDA reports an error.

```csharp
using System;
using System.IO;
using Supprocom.CSharp2CUDA;

CudaTranspilationResult result =
    CudaTranspiler.TranspileFile("SquareValues.cs");

if (!result.Succeeded)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine(diagnostic);

    return;
}

File.WriteAllText("SquareValues.cu", result.Source);
```

`TranspileFile` and `TranspileFiles` compile only the selected files. Use
`Transpile(CSharpCompilation)` when source-generator output must be present.

## Use CUDA without hiding it from C#

The `Cuda` class gives Roslyn typed symbols for CUDA operations. CSharp2CUDA
replaces each accepted call with its CUDA form.

| Need | C# surface | CUDA result |
| --- | --- | --- |
| Thread and grid coordinates | `Cuda.ThreadIdx`, `Cuda.BlockIdx`, `Cuda.BlockDim`, `Cuda.GridDim` | `threadIdx`, `blockIdx`, `blockDim`, `gridDim` |
| Block and warp synchronization | `Cuda.SyncThreads`, `Cuda.SyncWarp` | `__syncthreads`, `__syncwarp` |
| Memory publication | `Cuda.ThreadFence`, `Cuda.ThreadFenceSystem` | `__threadfence`, `__threadfence_system` |
| Shared storage | `Cuda.Shared`, `Cuda.SharedArray`, `Cuda.DynamicSharedBytes` | Static or launch-sized `__shared__` storage |
| Atomic operations | `Cuda.AtomicAdd`, `Cuda.AtomicExchange`, `Cuda.AtomicCompareExchange`, `Cuda.AtomicXor`, `Cuda.AtomicMin` | Typed CUDA atomic calls |
| Host-mapped polling | `Cuda.VolatileLoad`, `Cuda.VolatileStore` | Volatile mapped-memory access |
| Device time | `Cuda.GlobalTimer()` | A direct `mov.u64` read from `%globaltimer` |
| Exact double operations | `Cuda.DoubleAddRoundNearest` and related methods | `__dadd_rn`, `__dsub_rn`, `__dmul_rn`, `__ddiv_rn` |
| Named math | `Cuda.Log`, `Cuda.Log1p`, `Cuda.Sqrt`, `Cuda.Exp`, `Cuda.Pow`, `Cuda.NaN` | Direct CUDA math calls |

This kernel uses typed shared storage and synchronization. The same source
remains valid C# for editor checks and semantic analysis.

```csharp
using Supprocom.CSharp2CUDA;

[TranspileToCUDA]
internal static unsafe class PairSumModule
{
    [CudaGlobal(Name = "sum_pair")]
    private static void SumPair(int* input, int* output)
    {
        int* values = Cuda.SharedArray<int>(2);
        int lane = Cuda.ThreadIdx.X;
        values[lane] = input[lane];
        Cuda.SyncThreads();

        if (lane == 0)
            output[0] = values[0] + values[1];
    }
}
```

## Know the strict boundary

CSharp2CUDA accepts code only when it has an explicit, semantics-preserving
CUDA rule. A rejected translation contains diagnostics and empty generated
source.

| Rule | Practical effect |
| --- | --- |
| C# compilation must succeed first. | Misspelled symbols, bad types, and source-generator failures stop translation. |
| Translation units are static, non-generic classes. | CUDA functions and supported structures stay in a predictable module boundary. |
| Methods need `CudaDeviceAttribute` or `CudaGlobalAttribute`. | Device functions and kernels are intentional and visible in the source. |
| CUDA identifiers use ASCII letters, digits, and underscores. | C++ keywords, CUDA names, double underscores, and `csharp2cuda_` names are rejected. |
| Integer operations preserve unchecked C# behavior. | Generated helpers handle overflow, masked shifts, and failing division. |
| Evaluation order must be equivalent. | The validator rejects expressions whose C# effects can change under C++ ordering. |
| Constant memory is read-only. | Assignment, mutation, and atomic access to a `CudaConstant` array fail before emission. |
| Unsupported managed features stop translation. | Managed allocation, optional parameters, enums, `char`, and unknown .NET members do not leak into CUDA. |

Use `ushort` when code needs a 16-bit character value. CUDA C++ `char` does not
match the width of `System.Char`.

## Describe storage and linked code

These attributes keep CUDA layout and linkage requirements in compile-checked C# declarations.

| Attribute | Purpose |
| --- | --- |
| `CudaConstantAttribute` | Emits a nonempty, compile-time `int` array in device constant memory. |
| `CudaInlineArrayAttribute` | Emits fixed inline storage from a supported pointer field inside a structure. |
| `CudaReadOnlyAttribute` | Gives a pointer parameter a deep read-only contract. |
| `CudaExternalAttribute` | Uses a structure or function that another CUDA source unit supplies. |
| `CudaExternalDeviceAttribute` | Emits one device prototype without a function body for relocatable linking. |

The inline-array field stays a pointer in C#. CUDA emission gives the structure
fixed storage with the requested length.

```csharp
using Supprocom.CSharp2CUDA;

[TranspileToCUDA]
public static unsafe class OperationModule
{
    public struct Operation
    {
        public int kind;

        [CudaInlineArray(3)]
        public int* operands;
    }

    [CudaGlobal(Name = "clear_operation")]
    private static void ClearOperation(Operation* operation)
    {
        operation->operands[0] = 0;
    }
}
```

## Bring your own CUDA runtime

CSharp2CUDA stops after source emission. It does not invoke NVCC or NVRTC,
allocate device memory, copy buffers, launch kernels, or manage streams.

## Read more when you need it

| Document | Open it when |
| --- | --- |
| [Getting started](docs/getting-started.md) | You need detailed build behavior, diagnostics, storage examples, or external declarations. |
| [Public CUDA surface](Supprocom.CSharp2CUDA/Cuda.cs) | You need the exact typed intrinsic signatures. |
| [Public attributes](Supprocom.CSharp2CUDA/CudaAttributes.cs) | You need the exact declaration and selection attributes. |
| [Contributing](.github/CONTRIBUTING.md) | You want to build, test, or change the transpiler. |
| [Third-party notices](THIRD-PARTY-NOTICES.md) | You need dependency and license identities. |
| [AGPL-3.0-only license](LICENSE.md) | You need the complete license terms. |

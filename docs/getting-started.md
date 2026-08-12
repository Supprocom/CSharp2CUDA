# Getting started with CSharp2CUDA

This guide shows how to create a compile-checked CUDA project.
It also explains class markers and manual file selection.

## Before you start

Install the .NET 10 SDK. Use Visual Studio 2026 or the `dotnet` command when you build the repository.

You do not need the CUDA toolkit to translate C# source. You need a CUDA toolchain later when you compile and run the generated `.cu` file.

Add the `Supprocom.CSharp2CUDA` package to a .NET 10 project.
Version 0.2.1 supplies the file APIs and build integration in this guide.

Add the package directly to each project that uses automatic build transpilation.

## Create a CUDA project

Create a class library project.
Set `TranspileToCUDA` to select the complete project for CUDA emission.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TranspileToCUDA>true</TranspileToCUDA>
    <TranspileToCUDAOutputPath>cuda/HelloCuda.cu</TranspileToCUDAOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Supprocom.CSharp2CUDA" Version="0.2.1" />
  </ItemGroup>
</Project>
```

The project still passes through Roslyn compile checking.
Automatic transpilation uses that accepted compilation and its generated syntax
trees.
The build does not keep a managed assembly in its output directory.
It writes the selected `.cu` file relative to that directory.

## Create a translation unit

Create `HelloCuda.cs` in Visual Studio 2026.
The file is ordinary C# source, and the editor checks it as C#.

```csharp
using Supprocom.CSharp2CUDA;

internal static unsafe class HelloCuda
{
    [CudaDevice]
    private static int Add(int left, int right)
    {
        return left + right;
    }

    [CudaGlobal(Name = "hello_kernel")]
    private static void Kernel(int* output)
    {
        output[0] = Add(Cuda.ThreadIdx.X, 1);
    }
}
```

This source defines one device function and one global function.
The global function writes the thread index plus one to the first output
element.

The `unsafe` modifier is required because the example uses an `int*`
parameter.
Pointer types provide the direct memory model that CUDA code needs.

## Build the CUDA project

Build the project in Visual Studio 2026 or with the .NET CLI.

```text
dotnet build -c Release
```

The build writes `cuda/HelloCuda.cu` below the assembly output directory.
Omit `TranspileToCUDAOutputPath` to use `<AssemblyName>.cu`.

A failed C# compilation stops before CUDA emission.
A CSharp2CUDA error also stops the build and removes the previous generated
output.

The compiler analyzer stages one intermediate payload during `CoreCompile`.
The build publishes that payload only after the compiler succeeds.
The build also checks the exact class marker in the managed compiler output.
A missing payload for a marked class produces `CS2CUDA023`.

The output path must identify a relative `.cu` file below the assembly output
directory.
An absolute path or directory traversal produces `CS2CUDA021`.

## Mark a class in a managed project

Use `TranspileToCUDAAttribute` when the project must keep its managed assembly.
The build selects only marked classes.

```csharp
using Supprocom.CSharp2CUDA;

[TranspileToCUDA("cuda/HelloCuda.cu")]
internal static unsafe class HelloCuda
{
    [CudaGlobal(Name = "hello_kernel")]
    private static void Kernel(int* output)
    {
        output[0] = Cuda.ThreadIdx.X + 1;
    }
}
```

Use `[TranspileToCUDA]` or `[TranspileToCUDA("")]` to select
`<AssemblyName>.cu`.
All marked classes form one module.
Their nonempty custom paths must match.

## Select files manually

Use `TranspileFile` for one normal C# file.
Use `TranspileFiles` when one CUDA module spans multiple files.

```csharp
var result = CudaTranspiler.TranspileFile("HelloCuda.cs");
if (!result.Succeeded)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine(diagnostic);
}
```

`result.Source` contains CUDA C++ when `result.Succeeded` is `true`.
The source is empty when an error diagnostic exists.

`result.Diagnostics` contains Roslyn diagnostics and CSharp2CUDA diagnostics.
Always inspect the diagnostics when translation fails.

The manual file APIs select all top-level classes in the supplied files.
They ignore class marker selection and output paths.

```csharp
var module = CudaTranspiler.TranspileFiles(
    ["Kernel.cs", "DeviceFunctions.cs"]);
```

Use `CudaFileCompilationOptions` when selected files need extra metadata
references, preprocessor symbols, or compilation settings.
Manual APIs return source and never write an output file.

Set `OutputKind` and `MainTypeName` when executable source needs those Roslyn compilation settings.

Use `CudaTranspiler.Transpile(CSharpCompilation)` when another Roslyn-based tool
already owns the selected syntax trees, references, and compilation options.
This overload includes source-generator trees that the compilation owner added.
`TranspileFile` and `TranspileFiles` do not run project source generators.

## Understand the generated source

The emitter writes the integer semantics helper block when the module uses functions that need it. The helper block preserves C# integer behavior in CUDA C++.

The emitter writes struct forward declarations before struct definitions. It writes function prototypes before function definitions. This order supports calls between translated functions and references between translated structs.

The `Add` method becomes a `__device__` function. The `Kernel` method becomes an `extern "C" __global__` function named `hello_kernel`.

The assignment to `output[0]` becomes a CUDA pointer write. The `Cuda.ThreadIdx.X` reference becomes `threadIdx.x`.

The integer addition can become a call to a generated helper such as `csharp2cuda_i32_add`. The helper name is reserved and cannot be used by source identifiers.

## Use the CUDA bindings

The `Cuda` type makes CUDA operations visible to the C# compiler. Dimension, barrier, and atomic members are translation markers, while conversion helpers also provide managed C# behavior.

Use the dimension properties to read thread and grid coordinates.

```csharp
int thread = Cuda.ThreadIdx.X;
int block = Cuda.BlockIdx.X;
```

Use `Cuda.SyncThreads` for a block barrier.

```csharp
Cuda.SyncThreads();
```

Use a typed atomic method with a `ref` argument for an atomic memory operation.

```csharp
Cuda.AtomicExchange(ref output->valid, 0);
```

Atomic add, exchange, compare-exchange, XOR, and minimum operations support `int`, `uint`, `long`, and `ulong` locations.

Use `Cuda.ThreadFence` for device publication. Use `Cuda.ThreadFenceSystem` before a mapped host checkpoint becomes ready.

Use volatile mapped-memory operations when a kernel polls host writes or publishes host-visible
fields. The typed methods accept `int*` and `ulong*` addresses.

```csharp
while (Cuda.VolatileLoad((int*)mapped) == 0)
    Cuda.NanoSleep(128u);

Cuda.VolatileStoreUInt64(mapped, 8UL, sequence);
Cuda.ThreadFenceSystem();
Cuda.VolatileStoreInt32(mapped, 0UL, 1);
```

The byte-view offset counts bytes. These methods emit volatile accesses and do not replace
polling loads with atomic reads.

Use `Cuda.GlobalTimer()` when the device needs the CUDA global timer. It returns `ulong` and
emits a direct `mov.u64` read from `%globaltimer`.

Use `Cuda.SyncWarp` for warp synchronization. A supplied mask must be a nonzero compile-time `uint` value.

`Cuda.ShuffleDownSync` accepts an `int` value, an unsigned delta, and an explicit width. The width must be a valid compile-time warp width.

## Use shared and constant storage

Use typed local initializers for shared storage inside a global function.

```csharp
int ready = Cuda.Shared<int>();
int* lanes = Cuda.SharedArray<int>(8);
byte* storage = Cuda.DynamicSharedBytes(8);
double* totals = Cuda.DynamicSharedView<double>(storage, 0UL);
int* counts = Cuda.DynamicSharedView<int>(storage, 16UL);
```

`Cuda.SharedArray` requires a positive compile-time length. The dynamic storage alignment must be `1`, `2`, `4`, `8`, or `16`.

The dynamic view offset counts elements of the requested type. In the example, the `int` view starts after eight `double` values.

Mark a static read-only `int` array with `CudaConstantAttribute` for device constant storage.

```csharp
[CudaConstant]
private static readonly int[] Thresholds = [2, 4, 8, 16];
```

Each constant array needs a nonempty compile-time initializer. CSharp2CUDA emits `__device__ __constant__` storage with the exact values.

Do not write to a device constant array. The transpiler reports `CS2CUDA020` and returns empty CUDA source.

Use `CudaInlineArrayAttribute` on a structure pointer field for exact fixed CUDA storage.

```csharp
public struct EvolutionNode
{
    [CudaInlineArray(3)]
    public int* operands;
}
```

The generated CUDA field is `int operands[3]`. Primitive and user-structure element types
support indexed reads, indexed writes, and pointer access.

The user structure must be in the emitted module. The element cannot be `void`, another
pointer, an enum, an external structure, or an unsupported managed type.

## Use exact and named math

Use the round-to-nearest double methods when each operation boundary must remain exact.

```csharp
double product = Cuda.DoubleMultiplyRoundNearest(left, right);
double result = Cuda.DoubleAddRoundNearest(product, bias);
```

The generated source calls `__dmul_rn` and `__dadd_rn`. CUDA compilation cannot contract these calls into one multiply-add operation.

Use `Cuda.Log`, `Cuda.Log1p`, `Cuda.Sqrt`, `Cuda.Exp`, `Cuda.Pow`, and `Cuda.NaN` for named
CUDA math calls.

`Cuda.Log` has `Math.Log` behavior during managed execution and emits direct CUDA `log`.

Use `Cuda.Bool`, `Cuda.Int`, and `Cuda.Unsigned` when the generated expression needs an explicit C++ conversion.

Use `Cuda.FloatingRemainder` instead of `%` for a floating-point remainder. The `%` operator is reserved for integral operands.

## Declare read-only data

Use `CudaReadOnlyAttribute` on a pointer parameter when the function reads the reachable memory without writing it.

```csharp
[CudaDevice]
private static double Sum([CudaReadOnly] double* values, int count)
{
    double total = 0.0;
    for (int index = 0; index < count; index++)
        total += values[index];
    return total;
}
```

Use `Cuda.ReadOnly(pointer)` when a local pointer expression must keep a deep read-only type.

```csharp
double* readOnlyValues = Cuda.ReadOnly(values);
```

The read-only contract is part of semantic validation. The transpiler rejects `CudaReadOnlyAttribute` on a non-pointer parameter.

## Use external CUDA declarations

Use `CudaExternalAttribute` for a type or method that another CUDA source unit provides. CSharp2CUDA uses the declaration for C# binding and does not emit it again.

```csharp
using System;
using Supprocom.CSharp2CUDA;

[TranspileToCUDA]
internal static unsafe class ExternalModule
{
    [CudaExternal(IsPure = true)]
    private static double ExternalSquareRoot(double value) =>
        throw new NotSupportedException();

    [CudaDevice]
    private static double UseExternal(double value)
    {
        return ExternalSquareRoot(value);
    }
}
```

Set `IsPure = true` only when the external method has no observable effects. Its result must depend only on argument values and reachable read-only memory.

Mark every pointer parameter on a pure external method with `CudaReadOnlyAttribute`. Do not use a pure contract for a method that writes memory, changes external state, throws, or traps.

Use `CudaExternalDeviceAttribute` for a device function that a different relocatable CUDA unit
defines. CSharp2CUDA emits a prototype and does not emit a body.

```csharp
[CudaExternalDevice(Name = "external_device_operation")]
private static void Dispatch(
    [CudaReadOnly] DeviceSlot** inputs,
    DeviceSlot* output) => throw new NotSupportedException();
```

The read-only pointer-to-pointer emits as `const DeviceSlot* const*`. The CUDA link must
include the producer unit that defines `external_device_operation`.

## Follow the source rules

Use static methods with block bodies. Use `CudaDeviceAttribute` or `CudaGlobalAttribute` on every emitted method.

Do not use optional parameters, `params`, `ref`, or `out` parameters. Use `in` for a read-only value reference. Pointer parameters cannot use `in`.

Use only supported C# types and members. The transpiler rejects enums, `System.Char`, managed allocation, dynamic stack allocation, unsupported .NET members, and syntax without an explicit CUDA rule.

Keep every emitted identifier within the CUDA identifier rules. Avoid C++ keywords, CUDA runtime names, double underscores, and names that start with `csharp2cuda_`.

Keep expression effects visible when C# and C++ can evaluate operands in different orders. The validator rejects unsafe calls, assignments, mutations, and pointer expressions instead of emitting uncertain source.

## Build and test the repository

Run the full test suite from the repository root.

```text
dotnet test Supprocom.CSharp2CUDA.slnx -c Release
```

The tests compare complete generated CUDA modules with checked-in golden files. They also cover diagnostics, arithmetic helpers, CUDA intrinsics, external declarations, and evaluation-order rules.

Build a package only when the repository provenance properties are available.

```powershell
$commit = git rev-parse HEAD
$branch = git branch --show-current
dotnet pack Supprocom.CSharp2CUDA/Supprocom.CSharp2CUDA.csproj -c Release `
    -p:RepositoryCommit=$commit `
    -p:RepositoryBranch=$branch
```

The package is written to `artifacts/packages`. The package ID is `Supprocom.CSharp2CUDA`.

## Continue learning

Read the public API in `Supprocom.CSharp2CUDA/Cuda.cs` and `Supprocom.CSharp2CUDA/CudaAttributes.cs`. Read `Supprocom.CSharp2CUDA.Tests/Golden` for larger translation units.

Read [README.md](../README.md) for the project overview, semantic limits, and contribution links.

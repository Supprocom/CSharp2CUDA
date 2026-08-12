# CSharp2CUDA

CSharp2CUDA translates a restricted set of valid C# source into deterministic CUDA C++ source.
Roslyn provides syntax analysis, symbol resolution, type information, and C# diagnostics.
CSharp2CUDA adds strict validation and CUDA emission.

[Get started with a first translation](docs/getting-started.md) or read the sections below for the project model and limits.

## What CSharp2CUDA does

CSharp2CUDA reads a C# translation unit and emits CUDA C++ source. The translation keeps the source structure, resolves symbols through Roslyn, and applies explicit rules for types, calls, operators, declarations, and CUDA intrinsics.

The output is deterministic. Struct declarations and function prototypes appear before function definitions. Integer helpers preserve C# wraparound rules, masked shift counts, and guarded integer division.

The input must compile as C# before translation starts. Any error diagnostic produces an empty CUDA source result. The result also contains all Roslyn and CSharp2CUDA diagnostics.

## What CSharp2CUDA does not do

CSharp2CUDA does not compile CUDA C++ source. It does not allocate device memory, transfer data, launch kernels, or manage a CUDA stream. The calling application owns those operations.

Translation stops when the source uses a C# feature without an exact CUDA rule. This strict behavior prevents an unsupported construct from producing misleading CUDA output.

## How translation works

The public input boundary has three forms.
A dedicated project can set `TranspileToCUDA` and make CUDA source its build
output.
A normal project can mark selected classes with `TranspileToCUDAAttribute`.
Manual code can call `TranspileFile`, `TranspileFiles`, or
`Transpile(CSharpCompilation)`.

CSharp2CUDA does not accept raw C# source strings.
File input reads normal `.cs` files.
The compilation input uses a Roslyn compilation that the caller constructed
from selected syntax trees and references.

Automatic build modes use the compilation that the project compiler accepts.
This compilation includes syntax trees from the project source generators.
A project without the project property or an exact class marker does not start
automatic translation.

`Transpile(CSharpCompilation)` uses the exact supplied compilation, including
generator output that its owner added. The file APIs do not run project source
generators because their boundary is the selected files.

The compilation then passes through a semantic plan, a syntax validator, and a CUDA emitter. The plan registers translation units, structs, functions, parameters, locals, identifiers, and expression helper rewrites.

The validator accepts only syntax with an explicit CUDA rule. It also checks C# and C++ evaluation order before it accepts assignments, calls, binary operators, pointer operations, and compound assignments.

The emitter writes the integer semantics helpers, struct declarations, struct definitions, function prototypes, and function definitions. The `CudaTranspilationResult` reports success, diagnostics, and generated source.

## Quick start

Add the `Supprocom.CSharp2CUDA` package to a .NET 10 class library.
Set `TranspileToCUDA` to make the complete project a CUDA project.

Add the package directly to each project that uses automatic build transpilation.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TranspileToCUDA>true</TranspileToCUDA>
    <TranspileToCUDAOutputPath>cuda/HelloCuda.cu</TranspileToCUDAOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Supprocom.CSharp2CUDA" Version="0.2.1" />
  </ItemGroup>
</Project>
```

Create `HelloCuda.cs` as an ordinary compile-checked C# file.
Mark device methods with `CudaDeviceAttribute`.
Mark kernel methods with `CudaGlobalAttribute`.

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

Build the project in Visual Studio 2026 or with `dotnet build`.
Roslyn checks the C# source before CUDA emission.
The compiler analyzer creates an intermediate payload from that accepted
compilation. The build publishes the CUDA file only after `CoreCompile`
succeeds.
The output directory contains `cuda/HelloCuda.cu` and does not contain the
project managed assembly.

Omit `TranspileToCUDAOutputPath` to use `<AssemblyName>.cu`.
A custom project path follows the same assembly-relative rules as a class
marker path.

Use a class marker when a normal managed project must also emit one CUDA
module.
The managed assembly remains in the output directory.

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

Use `[TranspileToCUDA]` or `[TranspileToCUDA("")]` for the default path.
The default path is `<AssemblyName>.cu`.
A custom path is relative to the managed assembly output directory.

Use a manual API when code selects files explicitly.

```csharp
var result = CudaTranspiler.TranspileFile("HelloCuda.cs");
if (!result.Succeeded)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine(diagnostic);
}
```

`TranspileFiles` accepts all files for one CUDA module.
`Transpile(CSharpCompilation)` accepts a caller-owned Roslyn compilation.
Manual APIs return source and never write a file.

The generated source contains a `__device__` function for `Add`.
It also contains an `extern "C" __global__` function named `hello_kernel`.
The emitter adds the integer helper functions that the source requires.

## Source model

A translation unit is a selected static, non-generic class.
A dedicated project selects all top-level classes.
It ignores class markers and uses the project output path.

A manual API selects all top-level classes in its supplied files or
compilation.
It ignores class marker selection and output paths.
A normal managed build selects classes marked with
`TranspileToCUDAAttribute`.

All marked classes in one managed project form one CUDA module.
Their nonempty output paths must match.
An invalid path produces `CS2CUDA021`.
Conflicting paths produce `CS2CUDA022`.

Mark a static read-only `int` array with `CudaConstantAttribute`. Each element must have a compile-time value, and the array cannot be empty.

Device constant arrays are read-only in translated functions. A write attempt produces `CS2CUDA020` and no CUDA source.

A struct becomes a CUDA `struct`. Its fields must have supported types and must not be
static, constant, read-only, volatile, or initialized.

Mark a pointer field with `CudaInlineArrayAttribute` to emit fixed storage inside the CUDA
structure. The attribute length must be a positive constant.

```csharp
public struct EvolutionEntry
{
    [CudaInlineArray(3)]
    public int* operands;
}
```

The field remains a pointer in compile-checked C# source. CUDA emission changes it to
`int operands[3]`, which supports indexing and pointer access.

The element must be a supported scalar or an emitted user structure. The element cannot be
`void`, another pointer, an enum, an external structure, or an unsupported managed type.

A method with `CudaDeviceAttribute` becomes a `__device__` function. A method with `CudaGlobalAttribute` becomes a `__global__` function. Both attributes support a custom CUDA `Name`.

Global functions use `extern "C"` by default. Set `ExternC = false` when the generated function must not use C linkage.

All emitted identifiers must start with an ASCII letter and use only ASCII letters, digits, and underscores. C++ keywords, CUDA runtime names, double underscores, and generated `csharp2cuda_` names are reserved.

## CUDA surface

The `Cuda` type supplies typed bindings for CUDA dimensions, storage, synchronization, atomics, exact operations, math calls, and explicit conversions.

Storage, synchronization, and atomic members throw if managed code executes them. Math and conversion members provide managed behavior for Roslyn binding and parity tests.

`Cuda.ThreadIdx`, `Cuda.BlockIdx`, `Cuda.BlockDim`, and `Cuda.GridDim` provide the `X`, `Y`, and `Z` dimensions. `Cuda.SyncThreads` emits `__syncthreads`.

Use `Cuda.Shared<T>` for a shared scalar. Use `Cuda.SharedArray<T>` for a fixed shared array with a positive compile-time length.

Use `Cuda.DynamicSharedBytes` once in a kernel for launch-sized storage. Its alignment must be `1`, `2`, `4`, `8`, or `16`.

Use `Cuda.DynamicSharedView<T>` to create an aligned pointer view. Its offset counts `T` elements, which keeps every accepted view naturally aligned.

Shared storage supports `int`, `uint`, `long`, `ulong`, and `double`. Shared declarations are valid only inside global functions.

`Cuda.ThreadFence` and `Cuda.ThreadFenceSystem` emit the device and system fence intrinsics. `Cuda.NanoSleep` emits `__nanosleep`.

`Cuda.SyncWarp` accepts no mask or a nonzero compile-time `uint` mask. `Cuda.ShuffleDownSync` requires an explicit mask and valid compile-time width.

`Cuda.AtomicAdd`, `Cuda.AtomicExchange`, `Cuda.AtomicCompareExchange`, `Cuda.AtomicXor`, and `Cuda.AtomicMin` support `int`, `uint`, `long`, and `ulong` locations.

Use the atomic add operation with zero for an atomic read. Unsupported atomic types produce `CS2CUDA019`.

Use `Cuda.VolatileLoad` and `Cuda.VolatileStore` for typed mapped-memory `int*` and `ulong*`
addresses. Use the `Int32` and `UInt64` byte-view methods with a `byte*` base and byte offset.

These operations emit volatile CUDA loads and stores. They do not use device-scope atomic
reads. Call `Cuda.ThreadFenceSystem` before the device publishes completion to the host.

Use `Cuda.GlobalTimer()` for a direct `ulong` read from CUDA `%globaltimer`. The package emits
the required `mov.u64` instruction, so the C# source does not contain inline assembly.

`Cuda.Bool` converts an integer to a nonzero test. `Cuda.Int` converts a Boolean value to `0` or `1`. `Cuda.Unsigned` converts a signed `long` value to `unsigned long long` before an unsigned operation.

Use `Cuda.FloatingRemainder` for floating-point remainder. Use `Cuda.NearbyInteger` and `Cuda.SignBit` for the original CUDA math mappings.

`Cuda.DoubleAddRoundNearest`, `Cuda.DoubleSubtractRoundNearest`, `Cuda.DoubleMultiplyRoundNearest`, and `Cuda.DoubleDivideRoundNearest` emit exact round-to-nearest intrinsics.

These calls keep explicit operation boundaries. CUDA compilation cannot contract them into a multiply-add instruction.

`Cuda.Log`, `Cuda.Log1p`, `Cuda.Sqrt`, `Cuda.Exp`, `Cuda.Pow`, and `Cuda.NaN` emit package-owned
named CUDA math calls.

`Cuda.Log` has `Math.Log` behavior during managed execution and emits direct CUDA `log`.

Use `Cuda.ReadOnly` for a local pointer value that must be read-only through the expression tree. Use `CudaReadOnlyAttribute` on a pointer parameter when the external contract guarantees deep read-only access.

## Semantics and limits

The transpiler preserves C# integer behavior with generated helpers. This includes unchecked `int` and `long` arithmetic, shift masking, and division checks that call `__trap` for a C# division failure.

The compilation overload rejects `CSharpCompilationOptions.CheckOverflow` when it is enabled. Generated integer helpers implement unchecked C# arithmetic.

C# and CUDA C++ can evaluate expressions in different orders. The validator accepts an expression only when its effect analysis proves an equivalent result. Calls, assignments, pointer expressions, and mutations can therefore produce a diagnostic even when the C# compiler accepts them.

The body validator supports explicit rules for declarations, blocks, `if`, `for`, `while`, `switch`, returns, pointer access, arithmetic, comparisons, logical operators, and selected calls. Managed allocation, dynamic stack allocation, enums, optional parameters, unsupported members, and unsupported .NET types are rejected.

`System.Char` has no CUDA translation because its width does not match CUDA C++ `char`. Use `ushort` when the source requires a 16-bit code unit.

Use a C# `in` parameter for a read-only value reference. The emitter writes it as a CUDA C++ `const T&` parameter. Pointer parameters cannot use `in`.

Assign `stackalloc T[constant]` to a `T*`, `Span<T>`, or `ReadOnlySpan<T>` local for a fixed CUDA local array. The length must be a positive compile-time constant. Dynamic lengths are rejected.

## External declarations

Use `CudaExternalAttribute` for a struct or method supplied by an earlier CUDA source unit. The declaration remains available to Roslyn, but CSharp2CUDA does not emit it again.

External methods have unknown effects by default. Set `CudaExternalAttribute.IsPure` only when the method does not write memory, change external state, throw, or trap. Its result must depend only on argument values and reachable read-only memory.

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

Mark every pointer parameter on a pure external method with `CudaReadOnlyAttribute`. An incorrect pure contract can change program behavior.

Use `CudaExternalDeviceAttribute` when this module needs a prototype for a separately linked
device function. CSharp2CUDA emits the prototype once and does not emit a function body.

```csharp
[CudaExternalDevice(Name = "external_device_operation")]
private static void Dispatch(
    [CudaReadOnly] DeviceSlot** inputs,
    DeviceSlot* output) => throw new NotSupportedException();
```

The deep read-only pointer emits as `const DeviceSlot* const*`. A different relocatable
CUDA unit must define the device function before the final link.

## Build and test

Run the test suite from the repository root.

```text
dotnet test Supprocom.CSharp2CUDA.slnx -c Release
```

The compatibility suite translates complete C# catalog files and compares the output with exact CUDA golden files.

Focused tests cover diagnostics, identifiers, declarations, integer behavior, storage, synchronization, atomics, math, and evaluation order.

CUDA-capable test machines also compile the generated module with NVRTC. Real-device tests verify storage, contention, fences, warp masks, constants, and exact bits.

Create a package after setting the repository provenance properties.

```powershell
$commit = git rev-parse HEAD
$branch = git branch --show-current
dotnet pack Supprocom.CSharp2CUDA/Supprocom.CSharp2CUDA.csproj -c Release `
    -p:RepositoryCommit=$commit `
    -p:RepositoryBranch=$branch
```

The package output goes to `artifacts/packages`. The package ID is `Supprocom.CSharp2CUDA`.

## Project documentation

Read [Getting started](docs/getting-started.md) for a complete first translation. Read [CONTRIBUTING.md](.github/CONTRIBUTING.md) before submitting a contribution.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for dependency license identities. See [LICENSE.md](LICENSE.md) for the complete `AGPL-3.0-only` license text.

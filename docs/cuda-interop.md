# CUDA interop

Most reusable algorithms should stay ordinary C#. Put CUDA-specific details in
a thin adapter or in small, explicit interop helpers. The `Cuda` type gives
Roslyn typed markers that CSharp2CUDA replaces during source emission.

## Threads, synchronization, and atomics

Use `Cuda.ThreadIdx`, `Cuda.BlockIdx`, `Cuda.BlockDim`, and `Cuda.GridDim` for
launch coordinates. `Cuda.SyncThreads()` emits a block barrier;
`Cuda.SyncWarp(mask)` emits a warp barrier and requires a nonzero compile-time
mask. `Cuda.ShuffleDownSync` requires a compile-time width from 1 through 32.

Typed atomic add, exchange, compare-exchange, XOR, and minimum operations are
available for supported `int`, `uint`, `long`, and `ulong` locations. Use
`Cuda.ThreadFence()` for device publication and `Cuda.ThreadFenceSystem()`
before publishing a mapped-host checkpoint.

Mapped-memory polling uses the typed `Cuda.VolatileLoad` and
`Cuda.VolatileStore` methods. Their offsets count bytes. `Cuda.NanoSleep` can
back off a polling loop, and `Cuda.GlobalTimer()` reads `%globaltimer` as an
`ulong`.

## Shared, dynamic, constant, and inline storage

Shared storage is declared through typed local initializers in a kernel:

```csharp
int ready = Cuda.Shared<int>();
int* lanes = Cuda.SharedArray<int>(8);
byte* bytes = Cuda.DynamicSharedBytes(8);
double* totals = Cuda.DynamicSharedView<double>(bytes, 0UL);
int* counts = Cuda.DynamicSharedView<int>(bytes, 16UL);
```

`Cuda.SharedArray` needs a positive compile-time length. Dynamic alignment must
be 1, 2, 4, 8, or 16; dynamic-view offsets count elements of the requested
type.

`[CudaConstant]` on a static read-only `int[]` emits constant storage from a
nonempty compile-time initializer:

```csharp
[CudaConstant]
private static readonly int[] Thresholds = [2, 4, 8, 16];
```

Constant storage is deeply read-only. Writes, mutations, and atomics are
diagnosed before emission.

`[CudaInlineArray(length)]` on a supported pointer field emits exact fixed
storage in a CUDA structure:

```csharp
public unsafe struct EvolutionNode
{
    [CudaInlineArray(3)]
    public int* operands;
}
```

The field remains a pointer in the compile-checked C# declaration and becomes
`int operands[3]` in CUDA. The element must be a supported primitive or emitted
structure; nested pointers, enums, external structures, and managed types are
not valid inline elements.

## Read-only pointers

`[CudaReadOnly]` gives a pointer parameter a deep read-only contract.
`Cuda.ReadOnly(pointer)` retains that contract in a local expression. The
compiler rejects any path that turns the read-only memory writable.

```csharp
[CudaDevice]
private static unsafe double Sum([CudaReadOnly] double* values, int count)
{
    double total = 0.0;
    for (int index = 0; index < count; index++)
        total += values[index];
    return total;
}
```

## Exact and named math

`Cuda.DoubleAddRoundNearest`, `DoubleSubtractRoundNearest`,
`DoubleMultiplyRoundNearest`, and `DoubleDivideRoundNearest` emit CUDA
round-to-nearest intrinsics so the compiler cannot contract an operation
boundary. Named methods include `Cuda.Log`, `Log1p`, `Sqrt`, `Exp`, `Pow`, and
`NaN`. `Cuda.FloatingRemainder` supplies floating-point remainder; ordinary `%`
is reserved for integral operands.

`Cuda.Bool`, `Cuda.Int`, and `Cuda.Unsigned` request explicit generated C++
conversions.

## External CUDA declarations

`[CudaExternal]` describes a type or method supplied by another CUDA source
unit. CSharp2CUDA binds calls but does not emit the implementation. Mark a
method `IsPure = true` only when it has no observable effects and depends only
on its arguments and reachable read-only memory.

```csharp
[CudaExternal(IsPure = true)]
private static double ExternalSquareRoot(double value) =>
    throw new NotSupportedException();
```

`[CudaExternalDevice(Name = "external_device_operation")]` emits a device
prototype for relocatable device linking and omits the body. The final CUDA
link must include its producer unit. Deep read-only pointer-to-pointer
parameters emit the corresponding nested `const` qualifiers.

External names and layouts are ABI commitments. Device-internal tuples,
positional record structs, array/span views, and reference parameters cannot
cross this boundary.

## Runtime responsibility

CSharp2CUDA ends at deterministic `.cu` source. It does not invoke NVCC or
NVRTC, select a GPU, allocate or copy device memory, load a module, launch a
kernel, manage streams, or link external device objects. Those choices remain
with the host application and its CUDA runtime binding.

See the exact public signatures in
[`Cuda.cs`](../Supprocom.CSharp2CUDA/Cuda.cs) and declaration attributes in
[`CudaAttributes.cs`](../Supprocom.CSharp2CUDA/CudaAttributes.cs).

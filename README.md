# CSharp2CUDA

Regular C# static methods can become CUDA code. CSharp2CUDA lets you reuse
existing numerical algorithms and translate the sections that benefit from GPU
acceleration without first rewriting those algorithms in CUDA or requiring a
C# developer to learn CUDA syntax.

CSharp2CUDA is intentionally selective. It is useful for moving bounded,
value-oriented parts of a C# program onto a GPU; it is not intended to become
the default way to maintain a large application or to reproduce the managed
.NET runtime on a device.

## Translate an existing algorithm

Keep the reusable algorithm as ordinary C#. A small adapter supplies the CUDA
entry point and its pointer-based launch boundary.

```csharp
using System;
using Supprocom.CSharp2CUDA;

internal static class ExistingAlgorithm
{
    public static float Apply(float value, float scale)
    {
        float positive = MathF.Max(value, 0.0f);
        return positive * scale;
    }
}

[TranspileToCUDA]
internal static unsafe class ApplyOnGpu
{
    [CudaGlobal(Name = "apply_values")]
    private static void Kernel(float* input, float* output, int count, float scale)
    {
        int index = Cuda.BlockIdx.X * Cuda.BlockDim.X + Cuda.ThreadIdx.X;
        if (index < count)
            output[index] = ExistingAlgorithm.Apply(input[index], scale);
    }
}
```

Roslyn checks both methods as C#. CSharp2CUDA follows the call from `Kernel`,
emits `ExistingAlgorithm.Apply` as a CUDA device helper, and emits `Kernel` as
the `apply_values` global function. The CPU version remains directly callable
from ordinary managed code.

## Add the package

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <LangVersion>14.0</LangVersion>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Supprocom.CSharp2CUDA" Version="0.3.1" />
</ItemGroup>
```

Build normally with `dotnet build`. A marked class keeps the managed assembly
and adds a `.cu` output; a dedicated transpilation project can instead set
`<TranspileToCUDA>true</TranspileToCUDA>`.

The package emits CUDA C++ source. Your application still chooses a CUDA
toolchain, owns device memory, and launches kernels.

## Where to go next

- [Getting started](docs/getting-started.md) explains project selection,
  generated files, diagnostics, and manual Roslyn APIs.
- [Portable C# profile](docs/portable-csharp.md) lists supported C# features,
  restrictions, traps, lifetime rules, and 0.3.1 examples.
- [CUDA interop](docs/cuda-interop.md) covers intrinsics, shared and constant
  storage, ABI rules, and external CUDA linkage.
- [Tool package](docs/tool.md) explains when to use
  `Supprocom.CSharp2CUDA.Tool` instead of the build-integrated package.
- [Contributing](.github/CONTRIBUTING.md) covers repository builds and tests.

CSharp2CUDA fails closed: when a construct cannot be translated with the
required C# semantics, it reports a diagnostic and returns no partial CUDA
source.

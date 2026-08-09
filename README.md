# CSharp2CUDA

CSharp2CUDA translates a restricted set of valid C# source into deterministic CUDA C++
source. Roslyn supplies syntax analysis, symbol resolution, type information, and source
diagnostics. CSharp2CUDA supplies strict validation and CUDA emission.

The project does not compile CUDA, allocate device memory, transfer data, or launch kernels.
The calling software owns those operations. Translation stops when the source uses a C#
feature without an exact CUDA rule.

The compatibility suite uses complete CUDA translation units as golden output. It verifies
deterministic source text, production-style ordering, and multi-unit composition.

The package targets .NET 10 only. A translation unit is a static class with
`CudaTranslationUnitAttribute`. Nested structs become CUDA structs. Methods with
`CudaDeviceAttribute` become device functions. Methods with `CudaGlobalAttribute` become
global functions.

The input must compile as C# before translation starts. Use unsafe pointers for CUDA pointers.
Use `CudaReadOnlyAttribute` for a deeply read-only pointer parameter. The `Cuda` type supplies
thread dimensions, barriers, atomics, and explicit C++ conversion markers.

Use a C# `in` parameter for a read-only value reference. The transpiler emits that parameter
as a CUDA C++ `const T&` parameter.

Assign `stackalloc T[constant]` to a `T*` or `Span<T>` local for a fixed CUDA local array.
Use `ReadOnlySpan<T>` to emit a `const` array. Dynamic lengths are rejected.

Use `CudaExternalAttribute` for a type or method supplied by an earlier CUDA source unit. The
declaration remains available to Roslyn, but the transpiler does not emit it again.

Call `CudaTranspiler.Transpile` with C# source or a Roslyn `CSharpCompilation`. The result
contains the CUDA source and all Roslyn diagnostics. An error always causes an empty CUDA
source result.

```csharp
var result = CudaTranspiler.Transpile(csharpSource);
if (!result.Succeeded)
    throw new InvalidOperationException(string.Join("\n", result.Diagnostics));

string cudaSource = result.Source;
```

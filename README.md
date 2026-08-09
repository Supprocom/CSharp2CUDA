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

Every identifier that reaches CUDA must be one ASCII CUDA identifier. The rule covers struct,
field, function, parameter, and local names. It also covers custom `Name` values.

The transpiler removes valid C# escape markers and rewrites each matching reference. It rejects
CUDA C++ keywords, CUDA runtime names, generated helper names, and reserved forms.

Before emission, a semantic plan binds each accepted type, identifier, member, call, literal,
operator, and declaration. Unmapped .NET members, enums, static struct fields, optional
parameters, and signature collisions cause errors.

Emission places struct declarations and function prototypes before function definitions.
Integer operations use generated CUDA helpers for C# wraparound and masked shift counts.
Guarded integer division calls `__trap` for a C# division failure.

The input must compile as C# before translation starts. Use unsafe pointers for CUDA pointers.
Use `CudaReadOnlyAttribute` for a deeply read-only pointer parameter. The `Cuda` type supplies
thread dimensions, barriers, atomics, and explicit C++ conversion markers.

Conversion markers emit explicit expressions. `Cuda.Bool` tests for a nonzero value.
`Cuda.Unsigned` converts its input to `unsigned long long` before an unsigned operation.

Use a C# `in` parameter for a read-only value reference. The transpiler emits that parameter
as a CUDA C++ `const T&` parameter.

Assign `stackalloc T[constant]` to a `T*` or `Span<T>` local for a fixed CUDA local array.
Use `ReadOnlySpan<T>` to emit a `const` array. Dynamic lengths are rejected.

Use `CudaExternalAttribute` for a type or method supplied by an earlier CUDA source unit. The
declaration remains available to Roslyn, but the transpiler does not emit it again.

External methods have unknown effects by default. Set `CudaExternalAttribute.IsPure` only for
a method that does not write memory, change external state, throw, or trap.

Its result must depend only on argument values and reachable read-only memory. Mark each pointer
parameter with `CudaReadOnlyAttribute`. An incorrect contract can change program behavior.

C# and CUDA C++ evaluate some expressions in different orders. The semantic plan accepts these
expressions only when its effect analysis proves an equivalent result.

Unsafe assignment targets, binary operands, compound assignments, and call arguments cause an
error. The transpiler does not emit source for these inputs.

The body validator accepts only syntax with an explicit CUDA rule. The `%` operator accepts
only integral operands. Use `Cuda.FloatingRemainder` for floating-point operands.

`System.Char` does not have a CUDA translation because its width does not match CUDA C++
`char`. Use `ushort` when the source needs a 16-bit code unit.

Call `CudaTranspiler.Transpile` with C# source or a Roslyn `CSharpCompilation`. The result
contains the CUDA source and all Roslyn diagnostics. An error always causes an empty CUDA
source result.

The compilation overload rejects `CSharpCompilationOptions.CheckOverflow` when its value is
`true`. Generated integer helpers implement unchecked C# arithmetic.

```csharp
var result = CudaTranspiler.Transpile(csharpSource);
if (!result.Succeeded)
    throw new InvalidOperationException(string.Join("\n", result.Diagnostics));

string cudaSource = result.Source;
```

## Build and test

Open `CSharp2CUDA.slnx` in Visual Studio 2026. You can also run
`dotnet test CSharp2CUDA.slnx -c Release` from the repository root.

Package creation requires `RepositoryCommit` and `RepositoryBranch` MSBuild properties.
These properties bind package metadata to the reviewed Git source.

## License

CSharp2CUDA is licensed under `AGPL-3.0-only`. See [LICENSE.md](LICENSE.md) for
the complete license text.

Contributions use the terms in [.github/CONTRIBUTING.md](.github/CONTRIBUTING.md).
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for dependency license identities.

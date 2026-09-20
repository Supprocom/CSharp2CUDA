# CSharp2CUDA tool package

`Supprocom.CSharp2CUDA` is the compiler/build-integration package. Add it to a
project when source in that project should produce CUDA, or call its Roslyn API
from another program.

`Supprocom.CSharp2CUDA.Tool` is an optional .NET global tool. It helps an
existing project adopt the compiler without editing the original algorithm:
it checks whether an ordinary static method fits a supported adapter shape and
can create a separate, compile-checked CUDA adapter project.

## Install

```text
dotnet tool install --global Supprocom.CSharp2CUDA.Tool --version 0.3.1
csharp2cuda --help
```

## Check a method

```text
csharp2cuda check \
  --project src/Algorithms/Algorithms.csproj \
  --method "Algorithms.Arithmetic.Twice(int)"
```

The command loads the project through MSBuild and tests both supported adapter
mappings. `elementwise` maps one input element to one output element;
`single-thread` invokes the selected algorithm once. Exit code 0 means at least
one mapping is compatible, 1 means neither mapping is compatible, and 2 means
the command or project failed.

## Create a separate adapter project

```text
csharp2cuda scaffold \
  --project src/Algorithms/Algorithms.csproj \
  --method "Algorithms.Arithmetic.Twice(int)" \
  --mapping elementwise \
  --output cuda/Algorithms.Cuda
```

The output directory must be empty. The tool creates a normal C# project, a
generated `CudaAdapter.cs`, and `csharp2cuda.json`. It links the source files
needed to compile the selected method and references the matching 0.3.1 core
package. It does not modify the source project.

The generated adapter project can be built normally:

```text
dotnet build cuda/Algorithms.Cuda/Algorithms.Cuda.csproj -c Release
```

## Refresh generated files

```text
csharp2cuda refresh --output cuda/Algorithms.Cuda
```

Refresh reloads the source project and deterministically regenerates files the
tool owns. It verifies their recorded hashes first and refuses to overwrite a
generated file that has been edited. Keep custom launch/runtime code outside
the owned scaffold files.

The tool is an adoption helper, not a CUDA compiler or runtime. It neither
builds `.cu` with NVCC/NVRTC nor launches the generated kernel.

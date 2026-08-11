using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA;

public sealed class CudaFileCompilationOptions
{
    public string AssemblyName { get; init; } = "Supprocom.CSharp2CUDA.Input";

    public bool AllowUnsafe { get; init; } = true;

    public bool CheckOverflow { get; init; }

    public bool Optimize { get; init; } = true;

    public OutputKind OutputKind { get; init; } = OutputKind.DynamicallyLinkedLibrary;

    public string LanguageVersion { get; init; } = "14.0";

    public string? MainTypeName { get; init; }

    public string Nullable { get; init; } = "enable";

    public bool UseDefaultReferences { get; init; } = true;

    public IReadOnlyCollection<string> MetadataReferencePaths { get; init; } = [];

    public IReadOnlyCollection<string> PreprocessorSymbols { get; init; } = [];
}

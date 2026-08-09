using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharp2CUDA;

public sealed class CudaTranspilationResult
{
    internal CudaTranspilationResult(string source, ImmutableArray<Diagnostic> diagnostics)
    {
        Source = source;
        Diagnostics = diagnostics;
    }

    public string Source { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool Succeeded => Diagnostics.All(static diagnostic =>
        diagnostic.Severity != DiagnosticSeverity.Error);
}

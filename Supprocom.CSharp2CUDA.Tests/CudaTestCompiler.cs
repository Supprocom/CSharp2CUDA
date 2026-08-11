using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Supprocom.CSharp2CUDA.Tests;

internal static class CudaTestCompiler
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(CreateReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    public static CudaTranspilationResult Transpile(
        string source,
        CudaTranspilationOptions? options = null,
        string path = "input.cs")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var compilation = CreateCompilation(source, path);

        return CudaTranspiler.Transpile(compilation, WithAttributeSelection(options));
    }

    public static CSharpCompilation CreateCompilation(
        string source,
        string path = "input.cs")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.CSharp14),
            path);
        return CSharpCompilation.Create(
            "Supprocom.CSharp2CUDA.Tests.Input",
            [syntaxTree],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: false,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    public static CudaTranspilationResult Transpile(
        CSharpCompilation compilation,
        CudaTranspilationOptions? options = null) =>
        CudaTranspiler.Transpile(compilation, WithAttributeSelection(options));

    private static CudaTranspilationOptions WithAttributeSelection(
        CudaTranspilationOptions? options) => new()
        {
            NewLine = options?.NewLine ?? "\n",
            TranspileAttributedClassesOnly = true
        };

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var references = new Dictionary<string, MetadataReference>(
            StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
                references[path] = MetadataReference.CreateFromFile(path);
        }

        var assemblyPath = typeof(Cuda).Assembly.Location;
        references[assemblyPath] = MetadataReference.CreateFromFile(assemblyPath);
        return references.Values.ToImmutableArray();
    }
}

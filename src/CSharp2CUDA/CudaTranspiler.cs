using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharp2CUDA;

public static class CudaTranspiler
{
    private const string TranslationUnitAttributeName =
        "CSharp2CUDA.CudaTranslationUnitAttribute";

    private static readonly Lazy<ImmutableArray<MetadataReference>> DefaultReferences =
        new(CreateDefaultReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    public static CudaTranspilationResult Transpile(
        string source,
        CudaTranspilationOptions? options = null,
        string path = "input.cs")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.CSharp14),
            path);
        var compilation = CSharpCompilation.Create(
            "CSharp2CUDA.Input",
            [syntaxTree],
            DefaultReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: false,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));

        return Transpile(compilation, options);
    }

    public static CudaTranspilationResult Transpile(
        CSharpCompilation compilation,
        CudaTranspilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        options ??= new CudaTranspilationOptions();
        ValidateOptions(options);

        var diagnostics = compilation.GetDiagnostics().ToBuilder();
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new CudaTranspilationResult(string.Empty, diagnostics.ToImmutable());

        var attribute = compilation.GetTypeByMetadataName(TranslationUnitAttributeName);
        var units = FindTranslationUnits(compilation, attribute);
        if (units.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(CudaDiagnostics.MissingTranslationUnit, Location.None));
            return new CudaTranspilationResult(string.Empty, diagnostics.ToImmutable());
        }

        using var output = new StringWriter { NewLine = options.NewLine };
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var model = compilation.GetSemanticModel(unit.SyntaxTree, ignoreAccessibility: true);
            var emitter = new CudaModuleEmitter(model, diagnostics, options);
            var source = emitter.Emit(unit);
            if (index > 0)
                output.Write(options.NewLine + options.NewLine);
            output.Write(source);
        }

        var completedDiagnostics = diagnostics.ToImmutable();
        var failed = completedDiagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        return new CudaTranspilationResult(
            failed ? string.Empty : output.ToString(),
            completedDiagnostics);
    }

    private static void ValidateOptions(CudaTranspilationOptions options)
    {
        if (options.NewLine is not "\n" and not "\r\n")
        {
            throw new ArgumentException(
                "NewLine must be either a line feed or a carriage return followed by a line feed.",
                nameof(options));
        }
    }

    private static List<ClassDeclarationSyntax> FindTranslationUnits(
        CSharpCompilation compilation,
        INamedTypeSymbol? attribute)
    {
        var units = new List<ClassDeclarationSyntax>();
        if (attribute is null)
            return units;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var candidate in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol symbol)
                    continue;
                if (symbol.GetAttributes().Any(item =>
                        SymbolEqualityComparer.Default.Equals(item.AttributeClass, attribute)))
                {
                    units.Add(candidate);
                }
            }
        }

        return units;
    }

    private static ImmutableArray<MetadataReference> CreateDefaultReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
                references[path] = MetadataReference.CreateFromFile(path);
        }

        var assemblyPath = typeof(CudaTranslationUnitAttribute).Assembly.Location;
        references[assemblyPath] = MetadataReference.CreateFromFile(assemblyPath);
        return references.Values.ToImmutableArray();
    }
}

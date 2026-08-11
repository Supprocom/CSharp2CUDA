using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Supprocom.CSharp2CUDA;

public static class CudaTranspiler
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> DefaultReferences =
        new(CreateDefaultReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    private const string TranspileAttributeName =
        "Supprocom.CSharp2CUDA.TranspileToCUDAAttribute";

    public static CudaTranspilationResult TranspileFile(
        string sourcePath,
        CudaFileCompilationOptions? compilationOptions = null,
        CudaTranspilationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        return TranspileFiles([sourcePath], compilationOptions, options);
    }

    public static CudaTranspilationResult TranspileFiles(
        IEnumerable<string> sourcePaths,
        CudaFileCompilationOptions? compilationOptions = null,
        CudaTranspilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        compilationOptions ??= new CudaFileCompilationOptions();
        ValidateFileCompilationOptions(compilationOptions);

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var paths = new List<string>();
        var uniquePaths = new HashSet<string>(pathComparer);
        foreach (var sourcePath in sourcePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            var path = Path.GetFullPath(sourcePath);
            if (!string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Source path '{path}' must identify a .cs file.",
                    nameof(sourcePaths));
            }
            if (!uniquePaths.Add(path))
            {
                throw new ArgumentException(
                    $"Source path '{path}' is specified more than once.",
                    nameof(sourcePaths));
            }
            if (!File.Exists(path))
                throw new FileNotFoundException("The C# source file does not exist.", path);
            paths.Add(path);
        }

        if (paths.Count == 0)
            throw new ArgumentException("At least one C# source file is required.", nameof(sourcePaths));

        if (!LanguageVersionFacts.TryParse(
                compilationOptions.LanguageVersion,
                out var languageVersion))
        {
            throw new ArgumentException(
                $"Language version '{compilationOptions.LanguageVersion}' is invalid.",
                nameof(compilationOptions));
        }

        var parseOptions = new CSharpParseOptions(
            languageVersion,
            preprocessorSymbols: compilationOptions.PreprocessorSymbols);
        var syntaxTrees = paths.Select(path =>
        {
            using var stream = File.OpenRead(path);
            var text = SourceText.From(
                stream,
                encoding: null,
                checksumAlgorithm: SourceHashAlgorithm.Sha256);
            return CSharpSyntaxTree.ParseText(text, parseOptions, path);
        });

        var references = CreateReferences(compilationOptions);
        var compilation = CSharpCompilation.Create(
            compilationOptions.AssemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                compilationOptions.OutputKind,
                optimizationLevel: compilationOptions.Optimize
                    ? OptimizationLevel.Release
                    : OptimizationLevel.Debug,
                checkOverflow: compilationOptions.CheckOverflow,
                allowUnsafe: compilationOptions.AllowUnsafe,
                nullableContextOptions: ParseNullableContext(compilationOptions.Nullable),
                mainTypeName: compilationOptions.MainTypeName));

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
        if (compilation.Options.CheckOverflow)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.CheckedOverflowCompilation,
                Location.None));
        }
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new CudaTranspilationResult(string.Empty, diagnostics.ToImmutable());

        var attribute = compilation.GetTypeByMetadataName(TranspileAttributeName);
        var selection = FindTranslationUnits(
            compilation,
            attribute,
            options.TranspileAttributedClassesOnly,
            diagnostics);
        var units = selection.Units;
        if (units.Count == 0)
        {
            if (diagnostics.Any(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error))
            {
                return new CudaTranspilationResult(string.Empty, diagnostics.ToImmutable());
            }
            if (options.TranspileAttributedClassesOnly)
                return new CudaTranspilationResult(string.Empty, diagnostics.ToImmutable());
            diagnostics.Add(Diagnostic.Create(CudaDiagnostics.MissingTranslationUnit, Location.None));
            return new CudaTranspilationResult(string.Empty, diagnostics.ToImmutable());
        }

        var plan = CudaEmissionPlan.Create(compilation, units, diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            return new CudaTranspilationResult(string.Empty, diagnostics.ToImmutable());

        var emitter = new CudaModuleEmitter(plan, diagnostics, options);
        var source = emitter.Emit();

        var completedDiagnostics = diagnostics.ToImmutable();
        var failed = completedDiagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        return new CudaTranspilationResult(
            failed ? string.Empty : source,
            completedDiagnostics,
            selection.RequestedOutputPath);
    }

    internal static bool HasAttributedTranslationUnit(CSharpCompilation compilation)
    {
        var attribute = compilation.GetTypeByMetadataName(TranspileAttributeName);
        if (attribute is null)
            return false;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var candidate in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(candidate) is ISymbol symbol &&
                    GetMarker(symbol, attribute) is not null)
                {
                    return true;
                }
            }
        }

        return false;
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

    private static TranslationUnitSelection FindTranslationUnits(
        CSharpCompilation compilation,
        INamedTypeSymbol? attribute,
        bool attributedClassesOnly,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var units = new List<ClassDeclarationSyntax>();
        string? requestedOutputPath = null;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            if (attributedClassesOnly)
            {
                foreach (var candidate in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                             .Where(candidate => candidate.Ancestors()
                                 .OfType<TypeDeclarationSyntax>()
                                 .Any()))
                {
                    if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol nestedSymbol)
                        continue;
                    var nestedMarker = GetMarker(nestedSymbol, attribute);
                    if (nestedMarker is null)
                        continue;

                    diagnostics.Add(Diagnostic.Create(
                        CudaDiagnostics.InvalidTranslationUnit,
                        candidate.Identifier.GetLocation(),
                        candidate.Identifier.ValueText));
                }
            }

            foreach (var member in EnumerateTopLevelMembers(root))
            {
                if (member is not ClassDeclarationSyntax candidate)
                {
                    var declaredSymbol = model.GetDeclaredSymbol(member);
                    var nonClassMarker = declaredSymbol is null
                        ? null
                        : GetMarker(declaredSymbol, attribute);
                    if (!attributedClassesOnly)
                    {
                        diagnostics.Add(Diagnostic.Create(
                            CudaDiagnostics.UnsupportedSyntax,
                            member.GetLocation(),
                            member.Kind().ToString()));
                    }
                    else if (nonClassMarker is not null)
                    {
                        diagnostics.Add(Diagnostic.Create(
                            CudaDiagnostics.InvalidTranslationUnit,
                            member.GetLocation(),
                            declaredSymbol!.Name));
                    }
                    continue;
                }

                if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol symbol)
                    continue;
                if (!attributedClassesOnly)
                {
                    units.Add(candidate);
                    continue;
                }

                var marker = GetMarker(symbol, attribute);
                if (marker is null)
                    continue;
                units.Add(candidate);
                ValidateRequestedOutputPath(
                    marker,
                    candidate,
                    diagnostics,
                    ref requestedOutputPath);
            }
        }

        return new TranslationUnitSelection(units, requestedOutputPath);
    }

    private static AttributeData? GetMarker(ISymbol symbol, INamedTypeSymbol? attribute) =>
        attribute is null
            ? null
            : symbol.GetAttributes().FirstOrDefault(item =>
                SymbolEqualityComparer.Default.Equals(item.AttributeClass, attribute));

    private static IEnumerable<MemberDeclarationSyntax> EnumerateTopLevelMembers(
        SyntaxNode root)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            yield break;

        foreach (var member in EnumerateMembers(compilationUnit.Members))
            yield return member;
    }

    private static IEnumerable<MemberDeclarationSyntax> EnumerateMembers(
        SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                foreach (var nested in EnumerateMembers(namespaceDeclaration.Members))
                    yield return nested;
            }
            else
            {
                yield return member;
            }
        }
    }

    private static void ValidateRequestedOutputPath(
        AttributeData marker,
        ClassDeclarationSyntax candidate,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ref string? requestedOutputPath)
    {
        if (marker.ConstructorArguments.Length == 0)
            return;

        var argument = marker.ConstructorArguments[0];
        var path = argument.Value as string;
        var location = marker.ApplicationSyntaxReference?.GetSyntax().GetLocation() ??
            candidate.Identifier.GetLocation();
        if (argument.IsNull)
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidOutputPath,
                location,
                "null"));
            return;
        }
        if (string.IsNullOrEmpty(path))
            return;
        if (!CudaOutputPath.TryNormalize(path, out var normalizedPath))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.InvalidOutputPath,
                location,
                path));
            return;
        }

        if (requestedOutputPath is null)
        {
            requestedOutputPath = normalizedPath;
        }
        else if (!StringComparer.OrdinalIgnoreCase.Equals(
            requestedOutputPath,
            normalizedPath))
        {
            diagnostics.Add(Diagnostic.Create(
                CudaDiagnostics.ConflictingOutputPaths,
                location,
                normalizedPath,
                requestedOutputPath));
        }
    }

    private static void ValidateFileCompilationOptions(CudaFileCompilationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LanguageVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Nullable);
        if (options.MainTypeName is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(options.MainTypeName);
        ArgumentNullException.ThrowIfNull(options.MetadataReferencePaths);
        ArgumentNullException.ThrowIfNull(options.PreprocessorSymbols);
        if (!Enum.IsDefined(options.OutputKind))
        {
            throw new ArgumentException(
                $"Output kind '{options.OutputKind}' is invalid.",
                nameof(options));
        }
    }

    private static NullableContextOptions ParseNullableContext(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "disable" => NullableContextOptions.Disable,
            "enable" => NullableContextOptions.Enable,
            "warnings" => NullableContextOptions.Warnings,
            "annotations" => NullableContextOptions.Annotations,
            _ => throw new ArgumentException(
                $"Nullable context '{value}' is invalid.",
                nameof(CudaFileCompilationOptions))
        };

    private static ImmutableArray<MetadataReference> CreateReferences(
        CudaFileCompilationOptions options)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var references = new Dictionary<string, MetadataReference>(comparer);
        if (options.UseDefaultReferences)
        {
            foreach (var reference in DefaultReferences.Value)
            {
                if (reference.Display is not null)
                    references[reference.Display] = reference;
            }
        }

        var productPath = typeof(TranspileToCUDAAttribute).Assembly.Location;
        var productFileName = Path.GetFileName(productPath);
        foreach (var referencePath in options.MetadataReferencePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(referencePath);
            var path = Path.GetFullPath(referencePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("The metadata reference does not exist.", path);
            if (string.Equals(
                    Path.GetFileName(path),
                    productFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            references[path] = MetadataReference.CreateFromFile(path);
        }

        references[productPath] = MetadataReference.CreateFromFile(productPath);
        return references.Values.ToImmutableArray();
    }

    private static ImmutableArray<MetadataReference> CreateDefaultReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
                references[path] = MetadataReference.CreateFromFile(path);
        }

        var assemblyPath = typeof(TranspileToCUDAAttribute).Assembly.Location;
        references[assemblyPath] = MetadataReference.CreateFromFile(assemblyPath);
        return references.Values.ToImmutableArray();
    }

    private sealed record TranslationUnitSelection(
        List<ClassDeclarationSyntax> Units,
        string? RequestedOutputPath);
}

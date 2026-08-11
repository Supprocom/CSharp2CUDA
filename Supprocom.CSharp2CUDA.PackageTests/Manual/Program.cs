using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Supprocom.CSharp2CUDA;
using Supprocom.CSharp2CUDA.PackageTests.Manual;
using Supprocom.CSharp2CUDA.PackageTests.Manual.Inputs;

Require(SingleKernel.Double(4) == 8, "The managed source result is incorrect.");
Require(GeneratedPattern.Matches("cuda"), "The generated managed source is incorrect.");

var inputRoot = Path.Combine(AppContext.BaseDirectory, "Inputs");
var singlePath = Path.Combine(inputRoot, "SingleKernel.cs");
var helperPath = Path.Combine(inputRoot, "MultiHelper.cs");
var kernelPath = Path.Combine(inputRoot, "MultiKernel.cs");

var single = CudaTranspiler.TranspileFile(singlePath);
Require(single.Succeeded, FormatDiagnostics(single.Diagnostics));
Require(
    single.Source.Contains("__device__ int Double(int value)", StringComparison.Ordinal),
    "TranspileFile did not emit the selected function.");

var files = CudaTranspiler.TranspileFiles([helperPath, kernelPath]);
Require(files.Succeeded, FormatDiagnostics(files.Diagnostics));
Require(
    files.Source.Contains("__device__ int AddOne(int value);", StringComparison.Ordinal),
    "TranspileFiles did not emit the selected files.");

var compilation = CreateCompilation([helperPath, kernelPath]);
var compiled = CudaTranspiler.Transpile(compilation);
Require(compiled.Succeeded, FormatDiagnostics(compiled.Diagnostics));
Require(
    compiled.Source.Contains("return AddOne(value);", StringComparison.Ordinal),
    "Transpile(CSharpCompilation) did not emit the selected compilation.");

const string generatorInput = """
    using Supprocom.CSharp2CUDA;

    namespace Supprocom.CSharp2CUDA.PackageTests.Manual.Generated;

    public static class GeneratedCaller
    {
        [CudaDevice]
        public static int Invoke(int value)
        {
            return GeneratedHelper.Increment(value);
        }
    }
    """;
var generatorCompilation = CreateCompilationFromTrees(
    [CSharpSyntaxTree.ParseText(
        generatorInput,
        new CSharpParseOptions(LanguageVersion.CSharp14),
        "GeneratedCaller.cs")],
    "ManualGeneratedCompilation");
GeneratorDriver generatorDriver = CSharpGeneratorDriver.Create(
    [new ManualCudaGenerator().AsSourceGenerator()],
    parseOptions: new CSharpParseOptions(LanguageVersion.CSharp14));
generatorDriver.RunGeneratorsAndUpdateCompilation(
    generatorCompilation,
    out var generatedCompilation,
    out var generatorDiagnostics);
Require(
    !generatorDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
    "The manual source generator failed.");
var generated = CudaTranspiler.Transpile((CSharpCompilation)generatedCompilation);
Require(generated.Succeeded, FormatDiagnostics(generated.Diagnostics));
Require(
    generated.Source.Contains("__device__ int Increment(int value)", StringComparison.Ordinal) &&
    generated.Source.Contains("return Increment(value);", StringComparison.Ordinal),
    "Transpile(CSharpCompilation) did not emit the generated dependency.");

var productAssembly = typeof(CudaTranspiler).Assembly;
Require(
    productAssembly.GetName().Version?.ToString() == "0.2.0.0",
    "The package assembly version is incorrect.");
var repositoryCommit = productAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "RepositoryCommit")
    .Value;
Require(!string.IsNullOrWhiteSpace(repositoryCommit), "The repository commit is missing.");
var informationalVersion = productAssembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion;
Require(
    informationalVersion == $"0.2.0+{repositoryCommit}",
    "The informational version does not match the repository commit.");

Require(args.Length == 2, "The package tool assembly paths are missing.");
var taskAssemblyPath = Path.GetFullPath(args[0]);
Require(File.Exists(taskAssemblyPath), "The build task assembly is missing.");
var taskAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(taskAssemblyPath);
Require(
    taskAssembly.GetName().Name == "Supprocom.CSharp2CUDA.Build" &&
    taskAssembly.GetName().Version?.ToString() == "0.2.0.0",
    "The build task assembly identity is incorrect.");
var taskRepositoryCommit = taskAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "RepositoryCommit")
    .Value;
var taskInformationalVersion = taskAssembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion;
Require(
    taskRepositoryCommit == repositoryCommit &&
    taskInformationalVersion == $"0.2.0+{repositoryCommit}",
    "The build task provenance is incorrect.");

var compilerAssemblyPath = Path.GetFullPath(args[1]);
Require(File.Exists(compilerAssemblyPath), "The compiler assembly is missing.");
var compilerAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(compilerAssemblyPath);
Require(
    compilerAssembly.GetName().Name == "Supprocom.CSharp2CUDA.Compiler" &&
    compilerAssembly.GetName().Version?.ToString() == "0.2.0.0",
    "The compiler assembly identity is incorrect.");
var compilerRepositoryCommit = compilerAssembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "RepositoryCommit")
    .Value;
var compilerInformationalVersion = compilerAssembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion;
Require(
    compilerRepositoryCommit == repositoryCommit &&
    compilerInformationalVersion == $"0.2.0+{repositoryCommit}",
    "The compiler provenance is incorrect.");

static CSharpCompilation CreateCompilation(IEnumerable<string> paths)
{
    var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14);
    var syntaxTrees = paths.Select(path => CSharpSyntaxTree.ParseText(
        File.ReadAllText(path),
        parseOptions,
        path));
    return CreateCompilationFromTrees(syntaxTrees, "ManualCompilation");
}

static CSharpCompilation CreateCompilationFromTrees(
    IEnumerable<SyntaxTree> syntaxTrees,
    string assemblyName)
{
    var references = new Dictionary<string, MetadataReference>(
        StringComparer.OrdinalIgnoreCase);
    if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
    {
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            references[path] = MetadataReference.CreateFromFile(path);
    }

    var productPath = typeof(CudaTranspiler).Assembly.Location;
    references[productPath] = MetadataReference.CreateFromFile(productPath);
    return CSharpCompilation.Create(
        assemblyName,
        syntaxTrees,
        references.Values,
        new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: true));
}

static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
    string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString()));

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class ManualCudaGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output => output.AddSource(
            "GeneratedHelper.g.cs",
            SourceText.From(
                """
                using Supprocom.CSharp2CUDA;

                namespace Supprocom.CSharp2CUDA.PackageTests.Manual.Generated;

                public static class GeneratedHelper
                {
                    [CudaDevice]
                    public static int Increment(int value)
                    {
                        return value + 1;
                    }
                }
                """,
                Encoding.UTF8)));
    }
}

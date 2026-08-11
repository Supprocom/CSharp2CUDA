using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Supprocom.CSharp2CUDA;
using Supprocom.CSharp2CUDA.PackageTests.Manual.Inputs;

Require(SingleKernel.Double(4) == 8, "The managed source result is incorrect.");

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

Require(args.Length == 1, "The build task assembly path is missing.");
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

static CSharpCompilation CreateCompilation(IEnumerable<string> paths)
{
    var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14);
    var syntaxTrees = paths.Select(path => CSharpSyntaxTree.ParseText(
        File.ReadAllText(path),
        parseOptions,
        path));
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
        "ManualCompilation",
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

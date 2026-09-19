using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Supprocom.CSharp2CUDA;
using Supprocom.CSharp2CUDA.Compiler;

if (args.Length != 3)
    throw new ArgumentException("Specify the corpus directory, output file, and source commit.");

var corpusDirectory = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var sourceCommit = args[2];
if (!Directory.Exists(corpusDirectory))
    throw new DirectoryNotFoundException(corpusDirectory);
if (sourceCommit.Length != 40 || !sourceCommit.All(Uri.IsHexDigit))
    throw new ArgumentException("The source commit is invalid.");

var corpusCompilation = CreateCompilation(
    Directory.GetFiles(corpusDirectory, "*.cs", SearchOption.AllDirectories)
        .OrderBy(static path => path, StringComparer.Ordinal)
        .Select(path => CSharpSyntaxTree.ParseText(
            File.ReadAllText(path),
            PerformanceSettings.ParseOptions,
            path)),
    "PortabilityCorpus");
RequireNoCompilerErrors(corpusCompilation);

var analyzerOptions = new AnalyzerOptions(
    ImmutableArray<AdditionalText>.Empty,
    new PerformanceAnalyzerOptionsProvider(new Dictionary<string, string>
    {
        ["build_property.SupprocomCSharp2CUDAEnabled"] = "true",
        ["build_property.TranspileToCUDA"] = "false",
        ["build_property.DesignTimeBuild"] = "true",
        ["build_property.IsCrossTargetingBuild"] = "false",
        ["build_property.SupprocomCSharp2CUDASourceRoot"] = corpusDirectory
    }));
await RunAnalyzerAsync(corpusCompilation, analyzerOptions);
var diagnosticSamples = new List<double>();
for (var iteration = 0; iteration < 10; iteration++)
{
    var timer = Stopwatch.StartNew();
    await RunAnalyzerAsync(corpusCompilation, analyzerOptions);
    timer.Stop();
    diagnosticSamples.Add(timer.Elapsed.TotalMilliseconds);
}

var transpilationOptions = CreateAttributedOptions(corpusDirectory);
var warmCorpus = CudaTranspiler.Transpile(corpusCompilation, transpilationOptions);
RequireSuccess(warmCorpus, "The portability corpus warm-up failed.");
var deterministicHashes = new List<string>();
for (var iteration = 0; iteration < 10; iteration++)
{
    var result = CudaTranspiler.Transpile(corpusCompilation, transpilationOptions);
    RequireSuccess(result, "The portability corpus transpilation failed.");
    deterministicHashes.Add(Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(result.Source))));
}

const int reachabilityWarmupTranspilations = 50;
const int reachabilityTranspilationsPerSample = 200;
var reachabilityCases = new[] { 32, 64, 128, 256 }
    .Select(operationCount => (
        OperationCount: operationCount,
        Compilation: CreateReachabilityCompilation(operationCount),
        Options: CreateAttributedOptions(Path.GetTempPath()),
        Samples: new List<double>()))
    .ToArray();
for (var caseIndex = reachabilityCases.Length - 1; caseIndex >= 0; caseIndex--)
{
    var reachabilityCase = reachabilityCases[caseIndex];
    for (var warmupIndex = 0;
         warmupIndex < reachabilityWarmupTranspilations;
         warmupIndex++)
    {
        var warm = CudaTranspiler.Transpile(
            reachabilityCase.Compilation,
            reachabilityCase.Options);
        RequireSuccess(
            warm,
            $"The {reachabilityCase.OperationCount}-operation warm-up failed.");
    }
}
for (var iteration = 0; iteration < 10; iteration++)
{
    for (var orderIndex = 0; orderIndex < reachabilityCases.Length; orderIndex++)
    {
        var caseIndex = iteration % 2 == 0
            ? orderIndex
            : reachabilityCases.Length - 1 - orderIndex;
        var reachabilityCase = reachabilityCases[caseIndex];
        var timer = Stopwatch.StartNew();
        for (var batchIndex = 0;
             batchIndex < reachabilityTranspilationsPerSample;
             batchIndex++)
        {
            var result = CudaTranspiler.Transpile(
                reachabilityCase.Compilation,
                reachabilityCase.Options);
            RequireSuccess(
                result,
                $"The {reachabilityCase.OperationCount}-operation transpilation failed.");
        }
        timer.Stop();
        reachabilityCase.Samples.Add(
            timer.Elapsed.TotalMilliseconds / reachabilityTranspilationsPerSample);
    }
}
var reachability = reachabilityCases.Select(reachabilityCase =>
    new ReachabilityMeasurement(
        reachabilityCase.OperationCount,
        reachabilityCase.Samples,
        Median(reachabilityCase.Samples),
        Percentile(reachabilityCase.Samples, 0.95))).ToList();

var diagnosticP95 = Percentile(diagnosticSamples, 0.95);
var reachabilityRatios = new List<double>();
for (var index = 1; index < reachability.Count; index++)
{
    var ratio = reachability[index].MedianMilliseconds /
        reachability[index - 1].MedianMilliseconds;
    reachabilityRatios.Add(ratio);
}

var report = new PerformanceReport(
    sourceCommit,
    Environment.Version.ToString(),
    Environment.OSVersion.ToString(),
    Environment.ProcessorCount,
    Directory.GetFiles(corpusDirectory, "*.cs", SearchOption.AllDirectories).Length,
    reachabilityWarmupTranspilations,
    reachabilityTranspilationsPerSample,
    diagnosticSamples,
    Median(diagnosticSamples),
    diagnosticP95,
    deterministicHashes[0],
    reachability,
    reachabilityRatios);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(
    outputPath,
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n",
    new UTF8Encoding(false));
if (diagnosticP95 >= 1000.0)
    throw new InvalidOperationException($"Design-time diagnostic p95 is {diagnosticP95:F3} ms.");
if (deterministicHashes.Distinct(StringComparer.Ordinal).Count() != 1)
    throw new InvalidOperationException("Repeated transpilation produced different CUDA bytes.");
var failingRatio = reachabilityRatios.FirstOrDefault(static ratio => ratio > 2.2);
if (failingRatio > 2.2)
{
    throw new InvalidOperationException(
        $"Reachability growth ratio {failingRatio:F3} exceeds 2.2.");
}
Console.WriteLine($"DesignTimeDiagnosticP95Milliseconds={diagnosticP95:F3}");
Console.WriteLine($"DeterministicCudaSha256={deterministicHashes[0]}");
Console.WriteLine($"MaximumReachabilityGrowthRatio={reachabilityRatios.Max():F3}");

static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees, string assemblyName)
{
    var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
    if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedAssemblies)
    {
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            references[path] = MetadataReference.CreateFromFile(path);
    }
    var productPath = typeof(CudaTranspiler).Assembly.Location;
    references[productPath] = MetadataReference.CreateFromFile(productPath);
    return CSharpCompilation.Create(
        assemblyName,
        trees,
        references.Values,
        new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: true));
}

static CSharpCompilation CreateReachabilityCompilation(int operationCount)
{
    var builder = new StringBuilder();
    builder.AppendLine("using Supprocom.CSharp2CUDA;");
    builder.AppendLine("[TranspileToCUDA] internal static unsafe class Adapter {");
    builder.AppendLine("[CudaGlobal] private static void Run(int value, int* output) { output[0] = Chain.F0(value); }");
    builder.AppendLine("}");
    builder.AppendLine("internal static class Chain {");
    for (var index = 0; index < operationCount - 1; index++)
    {
        builder.Append("public static int F").Append(index).Append("(int value) => F")
            .Append(index + 1).AppendLine("(value) + 1;");
    }
    builder.Append("public static int F").Append(operationCount - 1)
        .AppendLine("(int value) => value + 1;");
    builder.AppendLine("}");
    var path = Path.Combine(Path.GetTempPath(), $"Reachability{operationCount}.cs");
    return CreateCompilation(
        [CSharpSyntaxTree.ParseText(builder.ToString(), PerformanceSettings.ParseOptions, path)],
        $"Reachability{operationCount}");
}

static CudaTranspilationOptions CreateAttributedOptions(string sourceRoot)
{
    var options = new CudaTranspilationOptions { SourceRoot = sourceRoot };
    var property = typeof(CudaTranspilationOptions).GetProperty(
        "TranspileAttributedClassesOnly",
        BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("The attributed selection option is missing.");
    property.SetValue(options, true);
    return options;
}

static async Task RunAnalyzerAsync(
    CSharpCompilation compilation,
    AnalyzerOptions analyzerOptions)
{
    var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new CudaTranspilationAnalyzer());
    var options = new CompilationWithAnalyzersOptions(
        analyzerOptions,
        null,
        concurrentAnalysis: true,
        logAnalyzerExecutionTime: false,
        reportSuppressedDiagnostics: false);
    var diagnostics = await compilation.WithAnalyzers(analyzers, options)
        .GetAnalyzerDiagnosticsAsync();
    var errors = diagnostics.Where(static diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0)
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors.AsEnumerable()));
}

static void RequireNoCompilerErrors(CSharpCompilation compilation)
{
    var errors = compilation.GetDiagnostics().Where(static diagnostic =>
        diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0)
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors.AsEnumerable()));
}

static void RequireSuccess(CudaTranspilationResult result, string message)
{
    if (result.Succeeded)
        return;
    throw new InvalidOperationException(
        message + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
}

static double Median(IReadOnlyCollection<double> values) => Percentile(values, 0.5);

static double Percentile(IReadOnlyCollection<double> values, double percentile)
{
    var ordered = values.Order().ToArray();
    var rank = percentile * (ordered.Length - 1);
    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);
    if (lower == upper)
        return ordered[lower];
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (rank - lower);
}

internal sealed class PerformanceAnalyzerOptionsProvider(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
{
    private static readonly AnalyzerConfigOptions Empty = new PerformanceAnalyzerConfigOptions(
        new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions { get; } =
        new PerformanceAnalyzerConfigOptions(values);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
}

internal sealed class PerformanceAnalyzerConfigOptions(
    IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, out string value) =>
        values.TryGetValue(key, out value!);
}

internal sealed record ReachabilityMeasurement(
    int OperationCount,
    IReadOnlyList<double> SamplesMilliseconds,
    double MedianMilliseconds,
    double P95Milliseconds);

internal sealed record PerformanceReport(
    string SourceCommit,
    string RuntimeVersion,
    string OperatingSystem,
    int ProcessorCount,
    int CorpusFileCount,
    int ReachabilityWarmupTranspilations,
    int ReachabilityTranspilationsPerSample,
    IReadOnlyList<double> DiagnosticSamplesMilliseconds,
    double DiagnosticMedianMilliseconds,
    double DiagnosticP95Milliseconds,
    string DeterministicCudaSha256,
    IReadOnlyList<ReachabilityMeasurement> Reachability,
    IReadOnlyList<double> ReachabilityGrowthRatios);

internal static class PerformanceSettings
{
    public static CSharpParseOptions ParseOptions { get; } =
        new(LanguageVersion.CSharp14);
}

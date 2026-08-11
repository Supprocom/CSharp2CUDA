using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Supprocom.CSharp2CUDA.Compiler;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CudaTranspilationAnalyzer : DiagnosticAnalyzer
{
    private const string PropertyPrefix = "build_property.";
    private const string EnabledProperty = PropertyPrefix + "SupprocomCSharp2CUDAEnabled";
    private const string EntireProjectProperty = PropertyPrefix + "TranspileToCUDA";
    private const string ProjectOutputPathProperty =
        PropertyPrefix + "TranspileToCUDAOutputPath";
    private const string IntermediatePayloadPathProperty =
        PropertyPrefix + "SupprocomCSharp2CUDAIntermediatePayloadPath";
    private const string DesignTimeBuildProperty = PropertyPrefix + "DesignTimeBuild";
    private const string CrossTargetingBuildProperty = PropertyPrefix + "IsCrossTargetingBuild";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        CudaDiagnostics.All;

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze |
            GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var globalOptions = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!GetBoolean(globalOptions, EnabledProperty) ||
            GetBoolean(globalOptions, DesignTimeBuildProperty) ||
            GetBoolean(globalOptions, CrossTargetingBuildProperty) ||
            context.Compilation is not CSharpCompilation compilation)
        {
            return;
        }

        try
        {
            var entireProject = GetBoolean(globalOptions, EntireProjectProperty);
            if (!entireProject && !CudaTranspiler.HasAttributedTranslationUnit(compilation))
                return;
            Transpile(context, globalOptions, compilation, entireProject);
        }
        catch (Exception exception)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CudaDiagnostics.BuildFailure,
                Location.None,
                exception.Message));
        }
    }

    private static void Transpile(
        CompilationAnalysisContext context,
        AnalyzerConfigOptions globalOptions,
        CSharpCompilation compilation,
        bool entireProject)
    {
        var result = CudaTranspiler.Transpile(
            compilation,
            new CudaTranspilationOptions
            {
                TranspileAttributedClassesOnly = !entireProject
            });
        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.Id.StartsWith("CS2CUDA", StringComparison.Ordinal))
                context.ReportDiagnostic(diagnostic);
        }
        if (!result.Succeeded || string.IsNullOrEmpty(result.Source))
            return;

        var relativePath = entireProject
            ? GetProperty(globalOptions, ProjectOutputPathProperty)
            : result.RequestedOutputPath;
        if (string.IsNullOrWhiteSpace(relativePath))
            relativePath = compilation.AssemblyName + ".cu";
        if (!CudaOutputPath.TryNormalize(relativePath, out var normalizedPath))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CudaDiagnostics.InvalidOutputPath,
                Location.None,
                relativePath));
            return;
        }

        var payloadPath = GetRequiredProperty(
            globalOptions,
            IntermediatePayloadPathProperty);
        WritePayload(payloadPath, normalizedPath, result.Source);
    }

    private static void WritePayload(string payloadPath, string relativePath, string source)
    {
        payloadPath = Path.GetFullPath(payloadPath);
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
        var temporaryPath = payloadPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                relativePath + "\n" + source,
                new UTF8Encoding(false));
            File.Move(temporaryPath, payloadPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string GetRequiredProperty(
        AnalyzerConfigOptions options,
        string name)
    {
        var value = GetProperty(options, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Compiler property '{name}' is missing.");
        return value;
    }

    private static string? GetProperty(AnalyzerConfigOptions options, string name) =>
        options.TryGetValue(name, out var value) ? value : null;

    private static bool GetBoolean(AnalyzerConfigOptions options, string name) =>
        bool.TryParse(GetProperty(options, name), out var value) && value;
}

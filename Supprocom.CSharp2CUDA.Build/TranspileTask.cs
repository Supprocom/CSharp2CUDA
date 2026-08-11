using System.Globalization;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Build;

public sealed class TranspileTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    public ITaskItem[] SourceFiles { get; set; } = [];

    public ITaskItem[] ReferencePaths { get; set; } = [];

    public ITaskItem[] ManagedOutputFiles { get; set; } = [];

    public string AssemblyName { get; set; } = string.Empty;

    public string ProjectOutputPath { get; set; } = string.Empty;

    public string StartupObject { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    public string LanguageVersion { get; set; } = "14.0";

    public string NullableMode { get; set; } = "disable";

    public string OutputType { get; set; } = "Library";

    public bool TranspileEntireProject { get; set; }

    public bool AllowUnsafe { get; set; }

    public bool CheckOverflow { get; set; }

    public bool Optimize { get; set; }

    public bool CleanOnly { get; set; }

    [Output]
    public string GeneratedFile { get; private set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            ExecuteCore();
        }
        catch (Exception exception)
        {
            Log.LogError(
                subcategory: null,
                errorCode: "CS2CUDA023",
                helpKeyword: null,
                file: null,
                lineNumber: 0,
                columnNumber: 0,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: exception.Message);
        }

        return !Log.HasLoggedErrors;
    }

    private void ExecuteCore()
    {
        var outputRoot = Path.GetFullPath(OutputDirectory);
        DeleteTrackedOutputs(outputRoot);
        if (TranspileEntireProject)
            DeleteManagedOutputs(outputRoot);
        if (CleanOnly)
            return;

        var sourcePaths = GetExistingPaths(SourceFiles);
        if (sourcePaths.Count == 0)
        {
            if (TranspileEntireProject)
                throw new InvalidOperationException("The CUDA project does not contain a C# source file.");
            WriteManifest(null);
            return;
        }

        var compilationOptions = new CudaFileCompilationOptions
        {
            AssemblyName = string.IsNullOrWhiteSpace(AssemblyName)
                ? "Supprocom.CSharp2CUDA.Build.Input"
                : AssemblyName,
            AllowUnsafe = AllowUnsafe,
            CheckOverflow = CheckOverflow,
            Optimize = Optimize,
            OutputKind = ParseOutputKind(OutputType),
            LanguageVersion = string.IsNullOrWhiteSpace(LanguageVersion)
                ? "14.0"
                : LanguageVersion,
            MainTypeName = string.IsNullOrWhiteSpace(StartupObject) ? null : StartupObject,
            Nullable = string.IsNullOrWhiteSpace(NullableMode) ? "disable" : NullableMode,
            UseDefaultReferences = false,
            MetadataReferencePaths = GetExistingPaths(ReferencePaths),
            PreprocessorSymbols = DefineConstants.Split(
                [';', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
        var options = new CudaTranspilationOptions
        {
            TranspileAttributedClassesOnly = !TranspileEntireProject
        };
        var result = CudaTranspiler.TranspileFiles(sourcePaths, compilationOptions, options);
        LogDiagnostics(result.Diagnostics);
        if (!result.Succeeded || string.IsNullOrEmpty(result.Source))
        {
            if (result.Succeeded)
                WriteManifest(null);
            return;
        }

        var relativePath = TranspileEntireProject
            ? ProjectOutputPath
            : result.RequestedOutputPath;
        if (string.IsNullOrWhiteSpace(relativePath))
            relativePath = AssemblyName + ".cu";

        var outputPath = ResolveOutputPath(outputRoot, relativePath);
        if (string.IsNullOrEmpty(outputPath))
            return;
        WriteOutput(outputPath, result.Source);
        WriteManifest(outputPath);
        GeneratedFile = outputPath;
        Log.LogMessage(MessageImportance.High, "Generated CUDA source: {0}", outputPath);
    }

    private void DeleteTrackedOutputs(string outputRoot)
    {
        if (!File.Exists(ManifestPath))
            return;

        foreach (var line in File.ReadAllLines(ManifestPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var path = Path.GetFullPath(line.Trim());
            if (IsBelowOutputRoot(outputRoot, path) &&
                !HasReparsePointBelowRoot(outputRoot, path) &&
                string.Equals(Path.GetExtension(path), ".cu", PathComparison) &&
                File.Exists(path))
            {
                File.Delete(path);
            }
        }

        File.Delete(ManifestPath);
    }

    private void DeleteManagedOutputs(string outputRoot)
    {
        foreach (var item in ManagedOutputFiles)
        {
            var value = item.GetMetadata("FullPath");
            if (string.IsNullOrWhiteSpace(value))
                continue;
            var path = Path.GetFullPath(value);
            if (IsBelowOutputRoot(outputRoot, path) && File.Exists(path))
                File.Delete(path);
        }
    }

    private static IReadOnlyCollection<string> GetExistingPaths(IEnumerable<ITaskItem> items)
    {
        var paths = new HashSet<string>(PathComparer);
        foreach (var item in items)
        {
            var value = item.GetMetadata("FullPath");
            if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
                paths.Add(Path.GetFullPath(value));
        }

        return paths;
    }

    private static OutputKind ParseOutputKind(string value) => value switch
    {
        "Exe" => OutputKind.ConsoleApplication,
        "WinExe" => OutputKind.WindowsApplication,
        "Module" => OutputKind.NetModule,
        "AppContainerExe" => OutputKind.WindowsRuntimeApplication,
        _ => OutputKind.DynamicallyLinkedLibrary
    };

    private void LogDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            var lineSpan = diagnostic.Location.GetLineSpan();
            var start = lineSpan.StartLinePosition;
            var end = lineSpan.EndLinePosition;
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                Log.LogError(
                    subcategory: null,
                    errorCode: diagnostic.Id,
                    helpKeyword: null,
                    file: lineSpan.Path,
                    lineNumber: start.Line + 1,
                    columnNumber: start.Character + 1,
                    endLineNumber: end.Line + 1,
                    endColumnNumber: end.Character + 1,
                    message: message);
            }
            else if (diagnostic.Severity == DiagnosticSeverity.Warning)
            {
                if (!diagnostic.Id.StartsWith("CS2CUDA", StringComparison.Ordinal))
                    continue;
                Log.LogWarning(
                    subcategory: null,
                    warningCode: diagnostic.Id,
                    helpKeyword: null,
                    file: lineSpan.Path,
                    lineNumber: start.Line + 1,
                    columnNumber: start.Character + 1,
                    endLineNumber: end.Line + 1,
                    endColumnNumber: end.Character + 1,
                    message: message);
            }
        }
    }

    private string ResolveOutputPath(string outputRoot, string relativePath)
    {
        if (!CudaOutputPath.TryNormalize(relativePath, out var normalizedPath))
            return ReportInvalidOutputPath(relativePath);

        string outputPath;
        try
        {
            outputPath = Path.GetFullPath(Path.Combine(
                outputRoot,
                CudaOutputPath.ToPlatformPath(normalizedPath)));
        }
        catch (Exception exception) when (exception is ArgumentException or
            NotSupportedException or PathTooLongException)
        {
            return ReportInvalidOutputPath(relativePath);
        }
        if (!IsBelowOutputRoot(outputRoot, outputPath) ||
            HasReparsePointBelowRoot(outputRoot, outputPath) ||
            !string.Equals(Path.GetExtension(outputPath), ".cu", PathComparison))
        {
            return ReportInvalidOutputPath(relativePath);
        }

        return outputPath;
    }

    private string ReportInvalidOutputPath(string relativePath)
    {
        Log.LogError(
            subcategory: null,
            errorCode: "CS2CUDA021",
            helpKeyword: null,
            file: null,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: $"CUDA output path '{relativePath}' must be a relative .cu file path below the assembly output directory.");
        return string.Empty;
    }

    private static bool IsBelowOutputRoot(string outputRoot, string path)
    {
        var root = Path.GetFullPath(outputRoot);
        var prefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(prefix, PathComparison);
    }

    private static bool HasReparsePointBelowRoot(string outputRoot, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        string? current = Path.GetFullPath(path);
        while (current is not null &&
            !string.Equals(current, root, PathComparison))
        {
            if (IsReparsePoint(current))
                return true;
            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException or
            DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void WriteOutput(string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, source, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void WriteManifest(string? outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        var contents = outputPath is null ? string.Empty : outputPath + Environment.NewLine;
        File.WriteAllText(ManifestPath, contents, new UTF8Encoding(false));
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

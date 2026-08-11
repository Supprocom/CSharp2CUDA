using System.Text;
using Microsoft.Build.Framework;

namespace Supprocom.CSharp2CUDA.Build;

public sealed class CudaOutputTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    [Required]
    public string IntermediatePayloadPath { get; set; } = string.Empty;

    public ITaskItem[] ManagedOutputFiles { get; set; } = [];

    public bool TranspileEntireProject { get; set; }

    public bool Publish { get; set; }

    [Output]
    public string GeneratedFile { get; private set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            var outputRoot = Path.GetFullPath(OutputDirectory);
            if (Publish)
                PublishPayload(outputRoot);
            else
                Prepare(outputRoot);
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

    private void Prepare(string outputRoot)
    {
        DeleteIntermediatePayload();
        DeleteTrackedOutputs(outputRoot);
        if (TranspileEntireProject)
            DeleteManagedOutputs(outputRoot);
    }

    private void PublishPayload(string outputRoot)
    {
        if (!File.Exists(IntermediatePayloadPath))
            return;

        var payload = File.ReadAllText(IntermediatePayloadPath, Encoding.UTF8);
        DeleteIntermediatePayload();
        var separator = payload.IndexOf('\n');
        if (separator <= 0)
            throw new InvalidOperationException("The CUDA compiler payload is invalid.");

        var relativePath = payload[..separator].TrimEnd('\r');
        var source = payload[(separator + 1)..];
        var outputPath = ResolveOutputPath(outputRoot, relativePath);
        if (string.IsNullOrEmpty(outputPath))
            return;

        var outputWritten = false;
        try
        {
            WriteOutput(outputPath, source);
            outputWritten = true;
            WriteManifest(outputPath);
        }
        catch
        {
            if (outputWritten && File.Exists(outputPath))
                File.Delete(outputPath);
            throw;
        }
        GeneratedFile = outputPath;
        Log.LogMessage(MessageImportance.High, "Generated CUDA source: {0}", outputPath);
    }

    private void DeleteIntermediatePayload()
    {
        if (File.Exists(IntermediatePayloadPath))
            File.Delete(IntermediatePayloadPath);
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

    private void WriteManifest(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        var temporaryPath = ManifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                outputPath + Environment.NewLine,
                new UTF8Encoding(false));
            File.Move(temporaryPath, ManifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

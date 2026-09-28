using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Supprocom.CSharp2CUDA.Tool;

internal static class CudaScaffoldStore
{
    public const string ManifestFileName = "csharp2cuda.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Create(CudaScaffoldPlan plan)
    {
        var output = ValidateOutputDirectory(plan.OutputDirectory, requireManifest: false);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            throw new ToolException(
                "CS2CUDA112",
                $"Output directory '{output}' must be empty.");
        }
        Directory.CreateDirectory(output);
        WritePlan(plan);
    }

    public static CudaScaffoldManifest ReadAndVerify(string outputDirectory)
    {
        var output = ValidateOutputDirectory(outputDirectory, requireManifest: true);
        var manifestPath = Path.Combine(output, ManifestFileName);
        CudaScaffoldManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CudaScaffoldManifest>(
                File.ReadAllText(manifestPath, Encoding.UTF8),
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new ToolException(
                "CS2CUDA113",
                $"Manifest '{manifestPath}' is not valid: {exception.Message}");
        }
        if (manifest is null || manifest.SchemaVersion != 1)
        {
            throw new ToolException(
                "CS2CUDA113",
                $"Manifest '{manifestPath}' has an unsupported schema.");
        }
        foreach (var file in manifest.GeneratedFiles)
        {
            var path = ResolveOwnedPath(output, file.Path);
            if (!File.Exists(path) ||
                !string.Equals(ComputeFileHash(path), file.Sha256, StringComparison.Ordinal))
            {
                throw new ToolException(
                    "CS2CUDA114",
                    $"Tool-owned file '{file.Path}' changed after scaffold creation.");
            }
        }
        return manifest;
    }

    public static void Refresh(CudaScaffoldPlan plan, CudaScaffoldManifest previous)
    {
        var output = ValidateOutputDirectory(plan.OutputDirectory, requireManifest: true);
        var previousPaths = previous.GeneratedFiles
            .Select(static file => file.Path)
            .ToHashSet(StringComparer.Ordinal);
        var nextPaths = plan.Files.Keys.ToHashSet(StringComparer.Ordinal);
        if (!previousPaths.SetEquals(nextPaths))
        {
            throw new ToolException(
                "CS2CUDA115",
                "Refresh cannot change the names of tool-owned files.");
        }
        WritePlan(plan with { OutputDirectory = output });
    }

    public static string ResolveSourceProject(
        string outputDirectory,
        CudaScaffoldManifest manifest) => Path.GetFullPath(Path.Combine(
        Path.GetFullPath(outputDirectory),
        manifest.SourceProject.Replace('/', Path.DirectorySeparatorChar)));

    private static void WritePlan(CudaScaffoldPlan plan)
    {
        foreach (var file in plan.Files)
        {
            var path = ResolveOwnedPath(plan.OutputDirectory, file.Key);
            WriteAtomically(path, file.Value);
        }
        var manifestText = JsonSerializer.Serialize(plan.Manifest, JsonOptions) + "\n";
        WriteAtomically(
            Path.Combine(plan.OutputDirectory, ManifestFileName),
            manifestText);
    }

    private static string ValidateOutputDirectory(string path, bool requireManifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var output = Path.GetFullPath(path);
        if (string.Equals(Path.GetPathRoot(output), output, StringComparison.Ordinal))
        {
            throw new ToolException(
                "CS2CUDA112",
                "A file-system root cannot be a scaffold output directory.");
        }
        if (Directory.Exists(output) &&
            new DirectoryInfo(output).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new ToolException(
                "CS2CUDA112",
                "A reparse point cannot be a scaffold output directory.");
        }
        if (requireManifest && !File.Exists(Path.Combine(output, ManifestFileName)))
        {
            throw new ToolException(
                "CS2CUDA113",
                $"Output directory '{output}' does not contain {ManifestFileName}.");
        }
        return output;
    }

    private static string ResolveOwnedPath(string outputDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathFullyQualified(relativePath))
        {
            throw new ToolException(
                "CS2CUDA113",
                $"Manifest path '{relativePath}' is not a relative file path.");
        }
        var output = Path.GetFullPath(outputDirectory);
        var path = Path.GetFullPath(Path.Combine(
            output,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(output, path);
        if (string.Equals(relative, "..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative))
        {
            throw new ToolException(
                "CS2CUDA113",
                $"Manifest path '{relativePath}' leaves the scaffold directory.");
        }
        return path;
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string ComputeFileHash(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path)));
}

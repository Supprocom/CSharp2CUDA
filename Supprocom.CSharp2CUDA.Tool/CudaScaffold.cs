using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Supprocom.CSharp2CUDA.Tool;

internal static class CudaScaffoldBuilder
{
    public const string CorePackageVersion = "0.3.0";

    public static CudaScaffoldPlan Build(
        CudaProjectModel model,
        CudaMethodSelection method,
        CudaAdapterMapping mapping,
        string outputDirectory,
        CudaAdapterSource adapter)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        var projectFileName = NormalizeFileName(model.ProjectName) + ".Cuda.csproj";
        const string adapterFileName = "CudaAdapter.cs";
        var projectSource = CreateProject(model, outputDirectory, projectFileName, adapterFileName);
        var files = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        files.Add(projectFileName, projectSource);
        files.Add(adapterFileName, NormalizeText(adapter.Source));

        var sourceProject = NormalizeRelativePath(Path.GetRelativePath(
            outputDirectory,
            model.ProjectPath));
        var sourceFiles = model.SourceFiles
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(outputDirectory, path)))
            .ToImmutableArray();
        var generatedFiles = files
            .Select(static item => new CudaManifestFile(item.Key, ComputeHash(item.Value)))
            .OrderBy(static item => item.Path, StringComparer.Ordinal)
            .ToImmutableArray();
        var manifest = new CudaScaffoldManifest(
            1,
            sourceProject,
            method.CanonicalName,
            FormatMapping(mapping),
            projectFileName,
            adapterFileName,
            adapter.CudaOutputPath,
            sourceFiles,
            generatedFiles);
        return new CudaScaffoldPlan(outputDirectory, files.ToImmutable(), manifest);
    }

    public static string FormatMapping(CudaAdapterMapping mapping) => mapping switch
    {
        CudaAdapterMapping.Elementwise => "elementwise",
        CudaAdapterMapping.SingleThread => "single-thread",
        _ => throw new InvalidOperationException("The CUDA mapping is not valid.")
    };

    public static CudaAdapterMapping ParseMapping(string mapping) => mapping switch
    {
        "elementwise" => CudaAdapterMapping.Elementwise,
        "single-thread" => CudaAdapterMapping.SingleThread,
        _ => throw new ToolException(
            "CS2CUDA111",
            $"Manifest mapping '{mapping}' is not valid.")
    };

    private static string CreateProject(
        CudaProjectModel model,
        string outputDirectory,
        string projectFileName,
        string adapterFileName)
    {
        var root = new XElement(
            "Project",
            new XAttribute("Sdk", "Microsoft.NET.Sdk"));
        var properties = new XElement(
            "PropertyGroup",
            new XElement("TargetFramework", "net10.0"),
            new XElement("OutputType", "Library"),
            new XElement("AllowUnsafeBlocks", "true"),
            new XElement("EnableDefaultCompileItems", "false"),
            new XElement("AssemblyName", NormalizeFileName(model.ProjectName) + ".Cuda"),
            new XElement("RootNamespace", NormalizeIdentifier(model.ProjectName) + ".Cuda"));
        AddProperty(properties, "Nullable", model.Nullable);
        AddProperty(properties, "ImplicitUsings", model.ImplicitUsings);
        AddProperty(properties, "LangVersion", model.LangVersion);
        AddProperty(properties, "DefineConstants", model.DefineConstants);
        AddProperty(
            properties,
            "CheckForOverflowUnderflow",
            model.CheckForOverflowUnderflow);
        AddProperty(properties, "Optimize", model.Optimize);
        root.Add(properties);

        var packageGroup = new XElement(
            "ItemGroup",
            new XElement(
                "PackageReference",
                new XAttribute("Include", "Supprocom.CSharp2CUDA"),
                new XAttribute("Version", CorePackageVersion)));
        foreach (var package in model.PackageReferences.Where(static item =>
                     !string.Equals(
                         item.Include,
                         "Supprocom.CSharp2CUDA",
                         StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(
                         item.Include,
                         "Supprocom.CSharp2CUDA.Tool",
                         StringComparison.OrdinalIgnoreCase)))
        {
            packageGroup.Add(CreateReferenceElement("PackageReference", package, null));
        }
        root.Add(packageGroup);

        var compileGroup = new XElement(
            "ItemGroup",
            new XElement("Compile", new XAttribute("Include", adapterFileName)));
        foreach (var sourceFile in model.SourceFiles)
        {
            compileGroup.Add(new XElement(
                "Compile",
                new XAttribute(
                    "Include",
                    ToProjectPath(Path.GetRelativePath(outputDirectory, sourceFile))),
                new XElement("Link", CreateLinkPath(model.ProjectDirectory, sourceFile))));
        }
        root.Add(compileGroup);

        AddPathItems(
            root,
            "ProjectReference",
            model.ProjectReferences,
            outputDirectory);
        AddPathItems(root, "AdditionalFiles", model.AdditionalFiles, outputDirectory);
        AddPathItems(root, "Analyzer", model.AnalyzerFiles, outputDirectory);

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        return NormalizeText(document.ToString(SaveOptions.None));
    }

    private static XElement CreateReferenceElement(
        string name,
        CudaProjectItem item,
        string? includeOverride)
    {
        var element = new XElement(
            name,
            new XAttribute("Include", includeOverride ?? item.Include));
        var names = name == "PackageReference"
            ? new[]
            {
                "Version",
                "VersionOverride",
                "PrivateAssets",
                "IncludeAssets",
                "ExcludeAssets",
                "Aliases",
                "GeneratePathProperty"
            }
            : new[]
            {
                "ReferenceOutputAssembly",
                "OutputItemType",
                "PrivateAssets",
                "Aliases"
            };
        foreach (var metadataName in names)
        {
            if (item.Metadata.TryGetValue(metadataName, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                element.Add(new XElement(metadataName, value));
            }
        }
        return element;
    }

    private static void AddPathItems(
        XElement root,
        string itemName,
        ImmutableArray<CudaProjectItem> items,
        string outputDirectory)
    {
        var usable = items.Where(static item => item.FullPath is not null).ToArray();
        if (usable.Length == 0)
            return;
        var group = new XElement("ItemGroup");
        foreach (var item in usable)
        {
            group.Add(CreateReferenceElement(
                itemName,
                item,
                ToProjectPath(Path.GetRelativePath(outputDirectory, item.FullPath!))));
        }
        root.Add(group);
    }

    private static void AddProperty(XElement group, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            group.Add(new XElement(name, value));
    }

    private static string CreateLinkPath(string projectDirectory, string sourceFile)
    {
        var relative = Path.GetRelativePath(projectDirectory, sourceFile);
        if (!Path.IsPathFullyQualified(relative) &&
            relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return ToProjectPath(Path.Combine("Imported", relative));
        }
        var hash = ComputeHash(sourceFile)[..12];
        return ToProjectPath(Path.Combine(
            "Imported",
            "External",
            hash,
            Path.GetFileName(sourceFile)));
    }

    private static string NormalizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '_');
        if (builder.Length == 0 || char.IsDigit(builder[0]))
            builder.Insert(0, '_');
        return builder.ToString();
    }

    private static string NormalizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(invalid.Contains(character) ? '_' : character);
        return builder.Length == 0 ? "CudaScaffold" : builder.ToString();
    }

    private static string ToProjectPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static string NormalizeText(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n') + "\n";

    private static string ComputeHash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

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
        if (Path.GetPathRoot(output) == output)
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
        if (relative == ".." ||
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

internal sealed record CudaScaffoldPlan(
    string OutputDirectory,
    ImmutableDictionary<string, string> Files,
    CudaScaffoldManifest Manifest);

internal sealed record CudaScaffoldManifest(
    int SchemaVersion,
    string SourceProject,
    string Method,
    string Mapping,
    string ProjectFile,
    string AdapterFile,
    string CudaOutputPath,
    ImmutableArray<string> SourceFiles,
    ImmutableArray<CudaManifestFile> GeneratedFiles);

internal sealed record CudaManifestFile(
    string Path,
    string Sha256);

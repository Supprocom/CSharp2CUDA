using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Supprocom.CSharp2CUDA.Tool;

internal static class CudaScaffoldBuilder
{
    public const string CorePackageVersion = "0.3.2";

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
        root.Add(CreatePropertyGroup(model));

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

    private static XElement CreatePropertyGroup(CudaProjectModel model)
    {
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
        return properties;
    }

    private static XElement CreateReferenceElement(
        string name,
        CudaProjectItem item,
        string? includeOverride)
    {
        var element = new XElement(
            name,
            new XAttribute("Include", includeOverride ?? item.Include));
        var names = string.Equals(name, "PackageReference"
, StringComparison.Ordinal) ? new[]
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
!string.Equals(relative, "..", StringComparison.Ordinal) &&
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

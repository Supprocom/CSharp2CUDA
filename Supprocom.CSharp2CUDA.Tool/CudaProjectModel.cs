using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace Supprocom.CSharp2CUDA.Tool;

internal sealed record CudaProjectModel(
    string ProjectPath,
    string ProjectDirectory,
    string ProjectName,
    bool IsSdkStyle,
    string TargetFramework,
    string TargetFrameworks,
    string OutputType,
    string Nullable,
    string ImplicitUsings,
    string LangVersion,
    string DefineConstants,
    string CheckForOverflowUnderflow,
    string Optimize,
    ImmutableArray<string> SourceFiles,
    ImmutableArray<CudaProjectItem> PackageReferences,
    ImmutableArray<CudaProjectItem> ProjectReferences,
    ImmutableArray<CudaProjectItem> AdditionalFiles,
    ImmutableArray<CudaProjectItem> AnalyzerFiles)
{
    public static CudaProjectModel Load(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var document = XDocument.Load(projectPath, LoadOptions.None);
        var root = document.Root;
        var isSdkStyle = root?.Attribute("Sdk") is not null ||
            root?.Elements().Any(static element => string.Equals(element.Name.LocalName, "Sdk", StringComparison.Ordinal)) == true;

        var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = "Release"
        };
        var collection = new ProjectCollection(globalProperties);
        try
        {
            var project = collection.LoadProject(projectPath);
            var sourceFiles = GetSourceFiles(project, projectDirectory);

            return new CudaProjectModel(
                projectPath,
                projectDirectory,
                Path.GetFileNameWithoutExtension(projectPath),
                isSdkStyle,
                project.GetPropertyValue("TargetFramework"),
                project.GetPropertyValue("TargetFrameworks"),
                project.GetPropertyValue("OutputType"),
                project.GetPropertyValue("Nullable"),
                project.GetPropertyValue("ImplicitUsings"),
                project.GetPropertyValue("LangVersion"),
                project.GetPropertyValue("DefineConstants"),
                project.GetPropertyValue("CheckForOverflowUnderflow"),
                project.GetPropertyValue("Optimize"),
                sourceFiles,
                GetItems(project, "PackageReference", projectDirectory),
                GetItems(project, "ProjectReference", projectDirectory),
                GetItems(project, "AdditionalFiles", projectDirectory),
                GetItems(
                    project,
                    "Analyzer",
                    projectDirectory,
                    item => string.Equals(
                        item.Xml.ContainingProject.FullPath,
                        projectPath,
                        StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            collection.UnloadAllProjects();
            collection.Dispose();
        }
    }

    private static ImmutableArray<string> GetSourceFiles(
        Microsoft.Build.Evaluation.Project project,
        string projectDirectory)
    {
        var intermediateRoot = GetFullDirectory(
            projectDirectory,
            FirstNonempty(
                project.GetPropertyValue("BaseIntermediateOutputPath"),
                "obj"));
        return project.GetItems("Compile")
            .Where(static item => !GetBooleanMetadata(item, "AutoGen") &&
                !GetBooleanMetadata(item, "DesignTime") &&
                !GetBooleanMetadata(item, "Generated"))
            .Select(item => GetItemFullPath(item, projectDirectory))
            .Where(path => path is not null &&
                File.Exists(path) &&
                !IsBelow(path, intermediateRoot))
            .Select(static path => path!)
            .Distinct(PathComparer)
            .OrderBy(static path => path, PathComparer)
            .ToImmutableArray();
    }

    private static ImmutableArray<CudaProjectItem> GetItems(
        Microsoft.Build.Evaluation.Project project,
        string itemType,
        string projectDirectory,
        Func<ProjectItem, bool>? predicate = null) => project.GetItems(itemType)
        .Where(item => predicate?.Invoke(item) ?? true)
        .Select(item => new CudaProjectItem(
            item.EvaluatedInclude,
            GetItemFullPath(item, projectDirectory),
            item.Metadata
                .Where(static metadata => !string.IsNullOrWhiteSpace(metadata.EvaluatedValue))
                .ToImmutableDictionary(
                    static metadata => metadata.Name,
                    static metadata => metadata.EvaluatedValue,
                    StringComparer.OrdinalIgnoreCase)))
        .OrderBy(static item => item.Include, StringComparer.OrdinalIgnoreCase)
        .ToImmutableArray();

    private static string? GetItemFullPath(ProjectItem item, string projectDirectory)
    {
        var fullPath = item.GetMetadataValue("FullPath");
        if (!string.IsNullOrWhiteSpace(fullPath))
            return Path.GetFullPath(fullPath);
        if (string.IsNullOrWhiteSpace(item.EvaluatedInclude))
            return null;
        return Path.GetFullPath(Path.Combine(projectDirectory, item.EvaluatedInclude));
    }

    private static bool GetBooleanMetadata(ProjectItem item, string name) =>
        bool.TryParse(item.GetMetadataValue(name), out var value) && value;

    private static string GetFullDirectory(string projectDirectory, string path) =>
        Path.GetFullPath(Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(projectDirectory, path));

    private static bool IsBelow(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !string.Equals(relative, "..", StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathFullyQualified(relative);
    }

    private static string FirstNonempty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

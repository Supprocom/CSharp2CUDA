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

internal static class CudaProjectLoader
{
    private static readonly object RegistrationLock = new();

    public static async Task<CudaProjectContext> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath))
        {
            throw new ToolException(
                "CS2CUDA101",
                $"Project '{projectPath}' does not exist.");
        }
        if (!string.Equals(
                Path.GetExtension(projectPath),
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolException(
                "CS2CUDA101",
                $"Project '{projectPath}' is not a C# project.");
        }

        await EnsureRestoreAsync(projectPath, cancellationToken).ConfigureAwait(false);
        RegisterMSBuild();
        var model = CudaProjectModel.Load(projectPath);
        ValidateProject(model);

        var failures = new List<string>();
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["Configuration"] = "Release"
        });
        workspace.RegisterWorkspaceFailedHandler(eventArguments =>
        {
            if (eventArguments.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                failures.Add(eventArguments.Diagnostic.Message);
        });

        try
        {
            var project = await workspace.OpenProjectAsync(
                projectPath,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                throw new ToolException(
                    "CS2CUDA102",
                    $"Roslyn could not load the complete project: {failures[0]}");
            }
            if (project.Language != LanguageNames.CSharp ||
                await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not
                    CSharpCompilation compilation)
            {
                throw new ToolException(
                    "CS2CUDA102",
                    "Roslyn did not create a C# compilation for the project.");
            }

            compilation = ReplaceCoreReference(compilation);
            var compilationErrors = compilation.GetDiagnostics(cancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            if (!compilationErrors.IsEmpty)
            {
                throw new ToolException(
                    "CS2CUDA103",
                    $"The source project does not compile: {compilationErrors[0]}");
            }
            return new CudaProjectContext(workspace, project, compilation, model);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static void RegisterMSBuild()
    {
        lock (RegistrationLock)
        {
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
        }
    }

    private static async Task EnsureRestoreAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("restore");
        process.StartInfo.ArgumentList.Add(projectPath);
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("--verbosity");
        process.StartInfo.ArgumentList.Add("quiet");
        if (!process.Start())
        {
            throw new ToolException(
                "CS2CUDA102",
                "The .NET SDK restore process did not start.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryStop(process);
            throw new ToolException(
                "CS2CUDA102",
                "The .NET SDK restore did not finish within two minutes.");
        }
        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var detail = FirstMessage(error) ?? FirstMessage(output) ??
                $"dotnet restore returned exit code {process.ExitCode}.";
            throw new ToolException(
                "CS2CUDA102",
                $"The source project restore failed: {detail}");
        }
    }

    private static string? FirstMessage(string value) => value
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault();

    private static void TryStop(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ValidateProject(CudaProjectModel model)
    {
        if (!model.IsSdkStyle)
        {
            throw new ToolException(
                "CS2CUDA104",
                "The source project must use the SDK project format.");
        }
        if (!string.Equals(model.TargetFramework, "net10.0", StringComparison.Ordinal))
        {
            throw new ToolException(
                "CS2CUDA104",
                "The source project must target only net10.0.");
        }
        if (!string.IsNullOrWhiteSpace(model.TargetFrameworks))
        {
            throw new ToolException(
                "CS2CUDA104",
                "A multi-target source project is not supported.");
        }
        if (!string.IsNullOrWhiteSpace(model.OutputType) &&
            !string.Equals(model.OutputType, "Library", StringComparison.OrdinalIgnoreCase))
        {
            throw new ToolException(
                "CS2CUDA104",
                "The source project must be a library.");
        }
    }

    private static CSharpCompilation ReplaceCoreReference(CSharpCompilation compilation)
    {
        var references = compilation.References
            .Where(static reference => !IsCSharp2CudaReference(reference))
            .Append(MetadataReference.CreateFromFile(typeof(Cuda).Assembly.Location));
        return compilation
            .WithReferences(references)
            .WithOptions(compilation.Options.WithAllowUnsafe(true));
    }

    private static bool IsCSharp2CudaReference(MetadataReference reference)
    {
        if (reference is not PortableExecutableReference { FilePath: { } filePath })
            return false;
        try
        {
            return string.Equals(
                AssemblyName.GetAssemblyName(filePath).Name,
                "Supprocom.CSharp2CUDA",
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException or BadImageFormatException or FileLoadException or
                FileNotFoundException)
        {
            return false;
        }
    }
}

internal sealed class CudaProjectContext(
    MSBuildWorkspace workspace,
    Microsoft.CodeAnalysis.Project project,
    CSharpCompilation compilation,
    CudaProjectModel model) : IDisposable
{
    public Microsoft.CodeAnalysis.Project Project { get; } = project;
    public CSharpCompilation Compilation { get; } = compilation;
    public CudaProjectModel Model { get; } = model;

    public void Dispose() => workspace.Dispose();
}

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
            root?.Elements().Any(static element => element.Name.LocalName == "Sdk") == true;

        var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = "Release"
        };
        var collection = new ProjectCollection(globalProperties);
        try
        {
            var project = collection.LoadProject(projectPath);
            var intermediateRoot = GetFullDirectory(
                projectDirectory,
                FirstNonempty(
                    project.GetPropertyValue("BaseIntermediateOutputPath"),
                    "obj"));
            var sourceFiles = project.GetItems("Compile")
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
        return relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathFullyQualified(relative);
    }

    private static string FirstNonempty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

internal sealed record CudaProjectItem(
    string Include,
    string? FullPath,
    ImmutableDictionary<string, string> Metadata);

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
    private static readonly Lock RegistrationLock = new();

    public static async Task<CudaProjectContext> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        projectPath = ValidateProjectPath(projectPath);
        await EnsureRestoreAsync(projectPath, cancellationToken).ConfigureAwait(false);
        RegisterMSBuild();
        var model = CudaProjectModel.Load(projectPath);
        ValidateProject(model);

        var failures = new List<string>();
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>(StringComparer.Ordinal)
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
            if (!string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal) ||
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

    private static string ValidateProjectPath(string projectPath)
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

        return projectPath;
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

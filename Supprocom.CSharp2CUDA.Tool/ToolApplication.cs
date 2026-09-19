using Microsoft.CodeAnalysis;

namespace Supprocom.CSharp2CUDA.Tool;

internal static class ToolApplication
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = CommandLine.Parse(arguments);
            return command.Kind switch
            {
                ToolCommandKind.Help => ShowHelp(output),
                ToolCommandKind.Check => await CheckAsync(
                    command,
                    output,
                    cancellationToken).ConfigureAwait(false),
                ToolCommandKind.Scaffold => await ScaffoldAsync(
                    command,
                    output,
                    cancellationToken).ConfigureAwait(false),
                ToolCommandKind.Refresh => await RefreshAsync(
                    command,
                    output,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("The command kind is not valid.")
            };
        }
        catch (ToolException exception)
        {
            await error.WriteLineAsync($"{exception.Code}: {exception.Message}").ConfigureAwait(false);
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("CS2CUDA199: The operation was canceled.").ConfigureAwait(false);
            return 2;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync(
                $"CS2CUDA199: The tool failed: {exception.Message}").ConfigureAwait(false);
            return 2;
        }
    }

    private static int ShowHelp(TextWriter output)
    {
        output.WriteLine(CommandLine.HelpText);
        return 0;
    }

    private static async Task<int> CheckAsync(
        ToolCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        using var project = await CudaProjectLoader.LoadAsync(
            command.ProjectPath!,
            cancellationToken).ConfigureAwait(false);
        var method = CudaMethodSelector.Select(project.Compilation, command.Method!);
        var rows = new List<CudaCompatibilityRow>();
        foreach (var mapping in new[]
                 {
                     CudaAdapterMapping.Elementwise,
                     CudaAdapterMapping.SingleThread
                 })
        {
            rows.Add(CheckMapping(project, method, mapping));
        }

        await output.WriteLineAsync($"Project: {project.Model.ProjectPath}").ConfigureAwait(false);
        await output.WriteLineAsync($"Method: {method.CanonicalName}").ConfigureAwait(false);
        await output.WriteLineAsync(string.Empty).ConfigureAwait(false);
        await output.WriteLineAsync("Mapping       Status       Detail").ConfigureAwait(false);
        await output.WriteLineAsync("------------- ------------ ----------------------------------------")
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            await output.WriteLineAsync(
                $"{CudaScaffoldBuilder.FormatMapping(row.Mapping),-13} {row.Status,-12} {row.Detail}")
                .ConfigureAwait(false);
        }
        return rows.Any(static row => row.Supported) ? 0 : 1;
    }

    private static CudaCompatibilityRow CheckMapping(
        CudaProjectContext project,
        CudaMethodSelection method,
        CudaAdapterMapping mapping)
    {
        try
        {
            var adapter = CudaAdapterGenerator.Generate(
                project.Compilation,
                method,
                mapping,
                "cuda/check.cu");
            var result = CudaAdapterGenerator.Transpile(
                project.Compilation,
                adapter,
                project.Model.ProjectDirectory,
                Path.Combine(
                    project.Model.ProjectDirectory,
                    ".csharp2cuda",
                    "CudaAdapter.g.cs"));
            if (!result.Succeeded)
            {
                return new CudaCompatibilityRow(
                    mapping,
                    false,
                    "Not supported",
                    GetFirstError(result));
            }
            var entryPoint = result.EntryPoints.Single();
            return new CudaCompatibilityRow(
                mapping,
                true,
                "Compatible",
                entryPoint.CudaName);
        }
        catch (ToolException exception)
        {
            return new CudaCompatibilityRow(
                mapping,
                false,
                "Not supported",
                exception.Message);
        }
    }

    private static async Task<int> ScaffoldAsync(
        ToolCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetFullPath(command.OutputPath!);
        using var project = await CudaProjectLoader.LoadAsync(
            command.ProjectPath!,
            cancellationToken).ConfigureAwait(false);
        var method = CudaMethodSelector.Select(project.Compilation, command.Method!);
        var cudaOutputPath = "cuda/" + NormalizeFileName(project.Model.ProjectName) + ".cu";
        var adapter = CudaAdapterGenerator.Generate(
            project.Compilation,
            method,
            command.Mapping!.Value,
            cudaOutputPath);
        ValidateTranslation(project, adapter);
        var plan = CudaScaffoldBuilder.Build(
            project.Model,
            method,
            command.Mapping.Value,
            outputDirectory,
            adapter);
        CudaScaffoldStore.Create(plan);

        await output.WriteLineAsync($"Created CUDA scaffold: {outputDirectory}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Project: {plan.Manifest.ProjectFile}").ConfigureAwait(false);
        await output.WriteLineAsync($"Adapter: {plan.Manifest.AdapterFile}").ConfigureAwait(false);
        await output.WriteLineAsync($"CUDA output: {plan.Manifest.CudaOutputPath}")
            .ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RefreshAsync(
        ToolCommand command,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetFullPath(command.OutputPath!);
        var previous = CudaScaffoldStore.ReadAndVerify(outputDirectory);
        var sourceProject = CudaScaffoldStore.ResolveSourceProject(
            outputDirectory,
            previous);
        using var project = await CudaProjectLoader.LoadAsync(
            sourceProject,
            cancellationToken).ConfigureAwait(false);
        var method = CudaMethodSelector.Select(project.Compilation, previous.Method);
        var mapping = CudaScaffoldBuilder.ParseMapping(previous.Mapping);
        var adapter = CudaAdapterGenerator.Generate(
            project.Compilation,
            method,
            mapping,
            previous.CudaOutputPath);
        ValidateTranslation(project, adapter);
        var plan = CudaScaffoldBuilder.Build(
            project.Model,
            method,
            mapping,
            outputDirectory,
            adapter);
        CudaScaffoldStore.Refresh(plan, previous);

        await output.WriteLineAsync($"Refreshed CUDA scaffold: {outputDirectory}")
            .ConfigureAwait(false);
        return 0;
    }

    private static void ValidateTranslation(
        CudaProjectContext project,
        CudaAdapterSource adapter)
    {
        var result = CudaAdapterGenerator.Transpile(
            project.Compilation,
            adapter,
            project.Model.ProjectDirectory,
            Path.Combine(
                project.Model.ProjectDirectory,
                ".csharp2cuda",
                "CudaAdapter.g.cs"));
        if (!result.Succeeded || result.EntryPoints.Length != 1)
        {
            throw new ToolException(
                "CS2CUDA116",
                $"The generated adapter is not GPU-portable: {GetFirstError(result)}");
        }
    }

    private static string GetFirstError(CudaTranspilationResult result) =>
        result.Diagnostics.FirstOrDefault(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error)?.ToString() ??
        "The transpiler did not create one CUDA entry point.";

    private static string NormalizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "CudaScaffold" : result;
    }
}

internal sealed record CudaCompatibilityRow(
    CudaAdapterMapping Mapping,
    bool Supported,
    string Status,
    string Detail);

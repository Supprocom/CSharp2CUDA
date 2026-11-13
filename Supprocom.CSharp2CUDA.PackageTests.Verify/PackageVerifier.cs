namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

/// <summary>Builds every consumer fixture against the exact package and checks the CUDA outputs.</summary>
internal sealed class PackageVerifier(RunContext context, string version)
{
    private static readonly string[] ManagedPatterns = ["*.dll", "*.pdb", "*.deps.json", "*.runtimeconfig.json"];
    private static readonly string[] AnalyzersDisabled = ["-p:RunAnalyzers=false"];
    private string generatorAssembly = string.Empty;
    private string lookalikeAssembly = string.Empty;

    public async Task RunAsync(string commit, string branch)
    {
        await AuditAsync(commit, branch).ConfigureAwait(false);
        generatorAssembly = await BuildSupportAsync(
            "Generator",
            "The package-test source generator is missing.").ConfigureAwait(false);
        lookalikeAssembly = await BuildSupportAsync(
            "Lookalike",
            "The package-test lookalike assembly is missing.").ConfigureAwait(false);
        await RunLookalikeAndAttributedAsync().ConfigureAwait(false);
        await RunContractsAndManualAsync().ConfigureAwait(false);
        await RunDedicatedAndNoMarkerAsync().ConfigureAwait(false);
        await RunGeneratedAsync().ConfigureAwait(false);
        await RunFailureCasesAsync().ConfigureAwait(false);
    }

    private static bool AnyFile(string root, string pattern, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(root, pattern, option).Any();
    }

    private static void NoCompilerPayload(BuildCase item)
    {
        Check.That(
            !AnyFile(item.IntermediateRoot, "Supprocom.CSharp2CUDA.payload", true),
            item.Name,
            "The build kept an intermediate CUDA compiler payload.");
    }

    private static void NoAutomaticState(BuildCase item)
    {
        NoCompilerPayload(item);
        Check.That(
            !AnyFile(item.IntermediateRoot, "Supprocom.CSharp2CUDA.outputs", true),
            item.Name,
            "The build kept an automatic output manifest.");
    }

    private static void RequireReported(string output, string code, string caseName, string reason)
    {
        Check.That(output.Contains(code, StringComparison.Ordinal), caseName, reason);
    }

    private static void NoCuda(BuildCase item, string reason)
    {
        Check.That(!AnyFile(item.Root, "*.cu", true), item.Name, reason);
    }

    private static void HasFile(string path, string caseName, string reason)
    {
        Check.That(File.Exists(path), caseName, reason);
    }

    private static void NoFile(string path, string caseName, string reason)
    {
        Check.That(!File.Exists(path), caseName, reason);
    }

    private static void NoManagedOutput(BuildCase item, string prefix)
    {
        foreach (var pattern in ManagedPatterns)
        {
            Check.That(
                !AnyFile(item.Root, pattern, false),
                item.Name,
                $"{prefix} managed output that matches {pattern}.");
        }
    }

    private static async Task<string> ReadAsync(string path)
    {
        return await File.ReadAllTextAsync(path).ConfigureAwait(false);
    }

    private Task<string> DotNetAsync(string name, int expectedExitCode, string[] arguments)
    {
        return context.Runner.DotNetAsync(name, expectedExitCode, arguments);
    }

    private async Task AuditAsync(string commit, string branch)
    {
        var project = Path.Combine(
            context.RepositoryRoot,
            "Supprocom.CSharp2CUDA.PackageTests.Audit",
            "Supprocom.CSharp2CUDA.PackageTests.Audit.csproj");
        string[] common =
        [
            "-p:BaseOutputPath=" + PathGuard.WithTrailingSeparator(Path.Combine(context.RunRoot, "Audit"), "bin"),
            "-p:BaseIntermediateOutputPath="
                + PathGuard.WithTrailingSeparator(Path.Combine(context.RunRoot, "Audit"), "obj")
        ];
        await DotNetAsync("Audit-restore", 0, context.RestoreArguments(project, common)).ConfigureAwait(false);
        await DotNetAsync(
            "Audit-run",
            0,
            [
                "run",
                "--project",
                project,
                "--configuration",
                "Release",
                "--no-restore",
                .. common,
                "--",
                context.PackageRoot,
                context.RepositoryRoot,
                commit,
                branch,
                version
            ]).ConfigureAwait(false);
    }

    private async Task<string> BuildSupportAsync(string name, string missingMessage)
    {
        var projectName = "Supprocom.CSharp2CUDA.PackageTests." + name;
        var project = Path.Combine(context.RepositoryRoot, projectName, projectName + ".csproj");
        var baseRoot = Path.Combine(context.RunRoot, name);
        var output = PathGuard.WithTrailingSeparator(baseRoot, "bin");
        string[] common =
        [
            "-p:BaseOutputPath=" + output,
            "-p:BaseIntermediateOutputPath=" + PathGuard.WithTrailingSeparator(baseRoot, "obj")
        ];
        await DotNetAsync(name + "-restore", 0, context.RestoreArguments(project, common)).ConfigureAwait(false);
        await DotNetAsync(
            name + "-build",
            0,
            ["build", project, "--configuration", "Release", "--no-restore", .. common]).ConfigureAwait(false);
        var assembly = Path.Combine(output, "Release", "net10.0", projectName + ".dll");
        HasFile(assembly, name, missingMessage);
        return assembly;
    }

    private async Task<BuildCase> InvokeCaseAsync(
        string name,
        bool expectSuccess,
        string? projectName = null,
        string[]? buildArguments = null)
    {
        var fixture = projectName ?? name;
        var project = Path.Combine(
            context.RepositoryRoot,
            "Supprocom.CSharp2CUDA.PackageTests",
            fixture,
            fixture + ".csproj");
        var caseRoot = Path.Combine(context.RunRoot, name);
        PathGuard.RequireInside(
            caseRoot,
            context.RunRoot,
            name,
            "The package test path is outside the test output directory.");
        if (Directory.Exists(caseRoot))
            Directory.Delete(caseRoot, recursive: true);
        var baseOutput = PathGuard.WithTrailingSeparator(caseRoot, "bin");
        var baseIntermediate = PathGuard.WithTrailingSeparator(caseRoot, "obj");
        string[] common =
        [
            "-p:BaseOutputPath=" + baseOutput,
            "-p:BaseIntermediateOutputPath=" + baseIntermediate,
            "-p:CSharp2CUDAPackageVersion=" + version,
            "-p:CSharp2CUDAGeneratorAssembly=" + generatorAssembly,
            "-p:CSharp2CUDALookalikeAssembly=" + lookalikeAssembly
        ];
        await DotNetAsync(name + "-restore", 0, context.RestoreArguments(project, common)).ConfigureAwait(false);
        var output = await DotNetAsync(
            name + "-build",
            expectSuccess ? 0 : 1,
            [
                "build",
                project,
                "--configuration",
                "Release",
                "--no-restore",
                .. buildArguments ?? [],
                .. common
            ]).ConfigureAwait(false);
        return new BuildCase(
            name,
            Path.Combine(baseOutput, "Release", "net10.0"),
            baseIntermediate,
            output,
            project,
            common);
    }

    private async Task RepeatAsync(BuildCase item)
    {
        await DotNetAsync(
            item.Name + "-repeat-build",
            0,
            ["build", item.Project, "--configuration", "Release", "--no-restore", .. item.Common])
            .ConfigureAwait(false);
        NoCompilerPayload(item);
    }

    private Task<string> RebuildWithoutAnalyzersAsync(string stepName, BuildCase item, int expectedExitCode)
    {
        return DotNetAsync(
            stepName,
            expectedExitCode,
            [
                "build",
                item.Project,
                "--configuration",
                "Release",
                "--no-restore",
                "-t:Rebuild",
                .. AnalyzersDisabled,
                .. item.Common
            ]);
    }

    private async Task RunLookalikeAndAttributedAsync()
    {
        var lookalike = await InvokeCaseAsync("LookalikeAttributed", true, null, AnalyzersDisabled)
            .ConfigureAwait(false);
        NoAutomaticState(lookalike);
        HasFile(
            Path.Combine(lookalike.Root, "LookalikeAttributed.dll"),
            lookalike.Name,
            "The lookalike-marker project did not create its managed assembly.");
        NoCuda(lookalike, "The lookalike-marker project created CUDA source.");

        var disabled = await InvokeCaseAsync("AttributedDisabledFirst", false, "Attributed", AnalyzersDisabled)
            .ConfigureAwait(false);
        RequireReported(
            disabled.Output,
            "CS2CUDA023",
            disabled.Name,
            "The first disabled class-marker build did not report CS2CUDA023.");
        NoAutomaticState(disabled);
        NoCuda(disabled, "The first disabled class-marker build created CUDA source.");

        await RunAttributedAsync().ConfigureAwait(false);
        await RunAttributedDefaultAsync().ConfigureAwait(false);
    }

    private async Task RunAttributedAsync()
    {
        var attributed = await InvokeCaseAsync("Attributed", true).ConfigureAwait(false);
        NoCompilerPayload(attributed);
        var cuda = Path.Combine(attributed.Root, "cuda", "Attributed.cu");
        HasFile(cuda, attributed.Name, "The attributed project did not create CUDA source.");
        HasFile(
            Path.Combine(attributed.Root, "Attributed.dll"),
            attributed.Name,
            "The attributed project did not keep its managed assembly.");
        await RepeatAsync(attributed).ConfigureAwait(false);
        HasFile(cuda, attributed.Name, "The repeated attributed build did not create CUDA source.");
        var output = await RebuildWithoutAnalyzersAsync("Attributed-analyzers-disabled-build", attributed, 1)
            .ConfigureAwait(false);
        RequireReported(
            output,
            "CS2CUDA023",
            attributed.Name,
            "The repeated disabled class-marker build did not report CS2CUDA023.");
        NoAutomaticState(attributed);
        NoFile(cuda, attributed.Name, "The repeated disabled class-marker build kept stale CUDA source.");
    }

    private async Task RunAttributedDefaultAsync()
    {
        var item = await InvokeCaseAsync("AttributedDefault", true).ConfigureAwait(false);
        NoCompilerPayload(item);
        var cuda = Path.Combine(item.Root, "AttributedDefault.cu");
        HasFile(cuda, item.Name, "The empty attribute path did not select the default CUDA output.");
        await RepeatAsync(item).ConfigureAwait(false);
        HasFile(cuda, item.Name, "The repeated default-path build did not create CUDA source.");
    }

    private async Task RunContractsAndManualAsync()
    {
        var item = await InvokeCaseAsync("MaintenanceContracts", true).ConfigureAwait(false);
        NoCompilerPayload(item);
        var cuda = Path.Combine(item.Root, "cuda", "MaintenanceContracts.cu");
        HasFile(cuda, item.Name, "The maintenance-contract project did not create CUDA source.");
        var source = await ReadAsync(cuda).ConfigureAwait(false);
        Check.That(
            source.Contains("int values[3];", StringComparison.Ordinal)
            && source.Contains("const volatile int", StringComparison.Ordinal)
            && source.Contains("volatile int*", StringComparison.Ordinal)
            && source.Contains("mov.u64 %0, %%globaltimer;", StringComparison.Ordinal)
            && source.Contains("csharp2cuda_global_timer()", StringComparison.Ordinal),
            item.Name,
            "The automatic compiler payload omitted a maintenance contract.");
        await RepeatAsync(item).ConfigureAwait(false);
        HasFile(cuda, item.Name, "The repeated maintenance-contract build did not create CUDA source.");
        await RunManualAsync().ConfigureAwait(false);
    }

    private async Task RunManualAsync()
    {
        var manual = await InvokeCaseAsync("Manual", true).ConfigureAwait(false);
        NoCompilerPayload(manual);
        var package = Path.Combine(context.PackagesRoot, "supprocom.csharp2cuda", version, "build");
        string[] runArguments =
        [
            Path.Combine(manual.Root, "Manual.dll"),
            Path.Combine(package, "task", "Supprocom.CSharp2CUDA.Build.dll"),
            Path.Combine(package, "compiler", "Supprocom.CSharp2CUDA.Compiler.dll"),
            context.EvidenceRoot,
            version
        ];
        await DotNetAsync("Manual-run", 0, runArguments).ConfigureAwait(false);
        NoCuda(manual, "The manual API project created automatic CUDA source.");
        await RepeatAsync(manual).ConfigureAwait(false);
        NoCuda(manual, "The repeated manual build created automatic CUDA source.");
        await RebuildWithoutAnalyzersAsync("Manual-analyzers-disabled-build", manual, 0).ConfigureAwait(false);
        NoCompilerPayload(manual);
        await DotNetAsync("Manual-analyzers-disabled-run", 0, runArguments).ConfigureAwait(false);
        NoCuda(manual, "The manual project created CUDA source when analyzers were disabled.");
    }

    private async Task RunDedicatedAndNoMarkerAsync()
    {
        var dedicated = await InvokeCaseAsync("Dedicated", true).ConfigureAwait(false);
        NoCompilerPayload(dedicated);
        var cuda = Path.Combine(dedicated.Root, "cuda", "Dedicated.cu");
        HasFile(cuda, dedicated.Name, "The dedicated project did not create CUDA source.");
        NoFile(
            Path.Combine(dedicated.Root, "Dedicated.dll"),
            dedicated.Name,
            "The dedicated project created a managed assembly.");
        NoManagedOutput(dedicated, "The dedicated project created");
        await RepeatAsync(dedicated).ConfigureAwait(false);
        HasFile(cuda, dedicated.Name, "The repeated dedicated build did not create CUDA source.");
        NoManagedOutput(dedicated, "The repeated dedicated build created");
        await RunNoMarkerAsync().ConfigureAwait(false);
    }

    private async Task RunNoMarkerAsync()
    {
        var item = await InvokeCaseAsync("NoMarker", true).ConfigureAwait(false);
        NoCompilerPayload(item);
        var assembly = Path.Combine(item.Root, "NoMarker.dll");
        NoCuda(item, "The unmarked project created CUDA source.");
        HasFile(assembly, item.Name, "The unmarked project did not create its managed assembly.");
        await DotNetAsync("NoMarker-run", 0, [assembly]).ConfigureAwait(false);
        await RepeatAsync(item).ConfigureAwait(false);
        NoCuda(item, "The repeated unmarked build created CUDA source.");
        await RebuildWithoutAnalyzersAsync("NoMarker-analyzers-disabled-build", item, 0).ConfigureAwait(false);
        NoCompilerPayload(item);
        await DotNetAsync("NoMarker-analyzers-disabled-run", 0, [assembly]).ConfigureAwait(false);
        NoCuda(item, "The unmarked project created CUDA source when analyzers were disabled.");
    }

    private static void RequireGeneratedDependency(string source, string caseName, string reason)
    {
        Check.That(
            source.Contains("__device__ int Increment(int value)", StringComparison.Ordinal)
            && source.Contains("return Increment(value);", StringComparison.Ordinal),
            caseName,
            reason);
    }

    private async Task RunGeneratedAsync()
    {
        var attributed = await InvokeCaseAsync("GeneratedAttributed", true).ConfigureAwait(false);
        NoCompilerPayload(attributed);
        var cuda = Path.Combine(attributed.Root, "cuda", "GeneratedAttributed.cu");
        HasFile(cuda, attributed.Name, "The generated attributed project did not create CUDA source.");
        RequireGeneratedDependency(
            await ReadAsync(cuda).ConfigureAwait(false),
            attributed.Name,
            "The attributed CUDA source does not contain the generated dependency.");
        HasFile(
            Path.Combine(attributed.Root, "GeneratedAttributed.dll"),
            attributed.Name,
            "The generated attributed project did not keep its managed assembly.");
        await RepeatAsync(attributed).ConfigureAwait(false);
        HasFile(cuda, attributed.Name, "The repeated generated attributed build did not create CUDA source.");
        var output = await RebuildWithoutAnalyzersAsync(
            "GeneratedAttributed-analyzers-disabled-build",
            attributed,
            1).ConfigureAwait(false);
        RequireReported(
            output,
            "CS2CUDA023",
            attributed.Name,
            "The disabled generated class-marker build did not report CS2CUDA023.");
        NoAutomaticState(attributed);
        NoFile(cuda, attributed.Name, "The disabled generated class-marker build kept stale CUDA source.");
        await RunGeneratedProjectAsync().ConfigureAwait(false);
    }

    private async Task RunGeneratedProjectAsync()
    {
        var item = await InvokeCaseAsync("GeneratedProject", true).ConfigureAwait(false);
        NoCompilerPayload(item);
        var cuda = Path.Combine(item.Root, "cuda", "GeneratedProject.cu");
        var assembly = Path.Combine(item.Root, "GeneratedProject.dll");
        HasFile(cuda, item.Name, "The generated complete project did not create CUDA source.");
        RequireGeneratedDependency(
            await ReadAsync(cuda).ConfigureAwait(false),
            item.Name,
            "The complete-project CUDA source does not contain the generated dependency.");
        NoFile(assembly, item.Name, "The generated complete project created a managed assembly.");
        await RepeatAsync(item).ConfigureAwait(false);
        HasFile(cuda, item.Name, "The repeated generated project build did not create CUDA source.");
        NoFile(assembly, item.Name, "The repeated generated project build created a managed assembly.");
        var output = await RebuildWithoutAnalyzersAsync(
            "GeneratedProject-analyzers-disabled-build",
            item,
            1).ConfigureAwait(false);
        RequireReported(
            output,
            "CS2CUDA023",
            item.Name,
            "The disabled automatic analyzer did not report CS2CUDA023.");
        NoCompilerPayload(item);
        NoFile(cuda, item.Name, "The disabled automatic analyzer kept stale CUDA source.");
    }

    private async Task RunFailureCasesAsync()
    {
        var invalidProject = await InvokeCaseAsync("InvalidProjectPath", false).ConfigureAwait(false);
        NoCompilerPayload(invalidProject);
        RequireReported(
            invalidProject.Output,
            "CS2CUDA021",
            invalidProject.Name,
            "The invalid project path did not report CS2CUDA021.");

        var invalidClass = await InvokeCaseAsync("InvalidClassPath", false).ConfigureAwait(false);
        NoCompilerPayload(invalidClass);
        RequireReported(
            invalidClass.Output,
            "CS2CUDA021",
            invalidClass.Name,
            "The invalid class path did not report CS2CUDA021.");

        var compileFailure = await InvokeCaseAsync("CompileFailure", false).ConfigureAwait(false);
        NoCompilerPayload(compileFailure);
        RequireReported(
            compileFailure.Output,
            "CS0103",
            compileFailure.Name,
            "The compile failure did not report the C# compiler error.");
        NoFile(
            Path.Combine(compileFailure.Root, "CompileFailure.cu"),
            compileFailure.Name,
            "The failed C# compilation created CUDA source.");
        await RunStaleOutputAsync().ConfigureAwait(false);
    }

    private async Task RunStaleOutputAsync()
    {
        var item = await InvokeCaseAsync("StaleOutput", true).ConfigureAwait(false);
        NoCompilerPayload(item);
        var cuda = Path.Combine(item.Root, "StaleOutput.cu");
        HasFile(cuda, item.Name, "The stale-output probe did not create its initial CUDA source.");
        await DotNetAsync(
            "StaleOutput-failed-build",
            1,
            [
                "build",
                item.Project,
                "--configuration",
                "Release",
                "--no-restore",
                "-p:DefineConstants=FAIL",
                .. item.Common
            ]).ConfigureAwait(false);
        NoCompilerPayload(item);
        NoFile(cuda, item.Name, "A failed C# compilation kept stale CUDA source.");
    }
}

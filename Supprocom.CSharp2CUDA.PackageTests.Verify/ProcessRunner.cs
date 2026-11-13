using System.Diagnostics;

namespace Supprocom.CSharp2CUDA.PackageTests.Verify;

internal sealed class ProcessRunner(
    string evidenceRoot,
    (string Key, string Value)[] environment,
    string? workingDirectory)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(300);

    public static async Task<string> ReadGitAsync(string repositoryRoot, string failure, params string[] arguments)
    {
        var result = await CaptureAsync(
            "git",
            ["-C", repositoryRoot, .. arguments],
            null,
            [],
            "git").ConfigureAwait(false);
        var text = result.StandardOutput.Trim();
        Check.That(result.ExitCode == 0 && text.Length > 0, "git", failure);
        return text;
    }

    public async Task<string> RunAsync(
        string name,
        string fileName,
        string[] arguments,
        int expectedExitCode = 0)
    {
        var result = await CaptureAsync(fileName, arguments, workingDirectory, environment, name)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, name + ".stdout.txt"), result.StandardOutput)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot, name + ".stderr.txt"), result.StandardError)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(evidenceRoot, name + ".exit.txt"),
            result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false);
        Check.That(
            result.ExitCode == expectedExitCode,
            name,
            $"{fileName} returned {result.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)} for {name}.");
        return result.StandardOutput + result.StandardError;
    }

    public Task<string> DotNetAsync(string name, int expectedExitCode, string[] arguments)
    {
        return RunAsync(name, "dotnet", arguments, expectedExitCode);
    }

    private static async Task<ProcessResult> CaptureAsync(
        string fileName,
        string[] arguments,
        string? directory,
        (string Key, string Value)[] variables,
        string name)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (directory is not null)
            startInfo.WorkingDirectory = directory;
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in variables)
            startInfo.Environment[key] = value;

        using var process = new Process { StartInfo = startInfo };
        Check.That(process.Start(), name, $"Cannot start {fileName}.");
        using var timeout = new CancellationTokenSource(Timeout);
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var texts = await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, texts[0], texts[1]);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            Check.Fail(name, $"{fileName} timed out for {name}.");
            throw;
        }
    }
}

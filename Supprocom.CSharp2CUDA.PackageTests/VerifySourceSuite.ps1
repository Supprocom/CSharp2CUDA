param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [string] $ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$packageRoot = [System.IO.Path]::GetFullPath($PackageDirectory)
$package = Get-Item -LiteralPath (
    Join-Path $packageRoot 'Supprocom.CSharp2CUDA.0.2.1.nupkg')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash
$runRoot = Join-Path $repositoryRoot (
    'artifacts/package-source-tests/' + $hash.Substring(0, 16))
$evidenceRoot = Join-Path $runRoot 'evidence'
$packagesRoot = Join-Path $runRoot 'packages'
$sourceRoot = Join-Path $runRoot 'source'
$testSourceRoot = Join-Path $sourceRoot 'Supprocom.CSharp2CUDA.Tests'
$packageBuildEvidenceRoot = Join-Path $repositoryRoot (
    'artifacts/package-tests/' + $hash.Substring(0, 16) + '/evidence')
$exactPackageCuda = Join-Path $packageBuildEvidenceRoot (
    'mts-remaining-boundary.generated.cu')
if (-not (Test-Path -LiteralPath $exactPackageCuda)) {
    throw 'The package build matrix did not retain the MTS boundary CUDA source.'
}
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $evidenceRoot 'trx'
}
else {
    $ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
}
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$sourcePath = [System.IO.Path]::GetFullPath($sourceRoot)
$runPrefix = [System.IO.Path]::GetFullPath($runRoot) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $sourcePath.StartsWith(
        $runPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The source-suite path is outside the test output directory.'
}
if (Test-Path -LiteralPath $sourcePath) {
    Remove-Item -LiteralPath $sourcePath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $testSourceRoot | Out-Null

$originalTestRoot = Join-Path $repositoryRoot 'Supprocom.CSharp2CUDA.Tests'
Get-ChildItem -LiteralPath $originalTestRoot -Force |
    Where-Object { $_.Name -notin @('bin', 'obj', 'TestResults') } |
    Copy-Item -Destination $testSourceRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') `
    -Destination (Join-Path $sourceRoot 'Directory.Build.props') `
    -Force

$escapedPackageRoot = [System.Security.SecurityElement]::Escape($packageRoot)
$nugetConfiguration = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="$escapedPackageRoot" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="candidate">
      <package pattern="Supprocom.CSharp2CUDA" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.*" />
      <package pattern="System.*" />
      <package pattern="xunit" />
      <package pattern="xunit.*" />
      <package pattern="Newtonsoft.Json" />
      <package pattern="runtime.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
$nugetPath = Join-Path $runRoot 'NuGet.config'
[System.IO.File]::WriteAllText($nugetPath, $nugetConfiguration)

function Invoke-DotNet {
    param(
        [string] $Name,
        [string[]] $Arguments
    )

    $standardOutputPath = Join-Path $evidenceRoot ($Name + '.stdout.txt')
    $standardErrorPath = Join-Path $evidenceRoot ($Name + '.stderr.txt')
    $exitPath = Join-Path $evidenceRoot ($Name + '.exit.txt')
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if ($null -ne $startInfo.ArgumentList) {
        foreach ($argument in $Arguments) {
            $startInfo.ArgumentList.Add($argument)
        }
    }
    else {
        $quotedArguments = foreach ($argument in $Arguments) {
            if ($argument -match '[\s"]') {
                '"' + $argument.Replace('"', '\"') + '"'
            }
            else {
                $argument
            }
        }
        $startInfo.Arguments = $quotedArguments -join ' '
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Cannot start dotnet for $Name."
    }

    $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(180000)) {
        $process.Kill()
        throw "dotnet timed out for $Name."
    }

    $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
    $standardError = $standardErrorTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($standardOutputPath, $standardOutput)
    [System.IO.File]::WriteAllText($standardErrorPath, $standardError)
    [System.IO.File]::WriteAllText($exitPath, $process.ExitCode.ToString())
    if ($process.ExitCode -ne 0) {
        throw "dotnet returned $($process.ExitCode) for $Name."
    }
}

$testProject = Join-Path $sourceRoot (
    'Supprocom.CSharp2CUDA.Tests/Supprocom.CSharp2CUDA.Tests.csproj')
$common = @(
    "-p:CSharp2CUDAPackageDirectory=$packageRoot",
    '-p:CSharp2CUDAPackageVersion=0.2.1'
)
$env:CSHARP2CUDA_EXACT_PACKAGE_CUDA = $exactPackageCuda
$env:CSHARP2CUDA_EVIDENCE_DIRECTORY = Join-Path $evidenceRoot 'generated'
Invoke-DotNet -Name 'restore' -Arguments (@(
    'restore',
    $testProject,
    '--configfile',
    $nugetPath,
    '--packages',
    $packagesRoot,
    '--force',
    '--no-cache'
) + $common)
Invoke-DotNet -Name 'test' -Arguments (@(
    'test',
    $testProject,
    '--configuration',
    'Release',
    '--no-restore',
    '--logger',
    'console;verbosity=minimal',
    '--logger',
    'trx;LogFileName=Supprocom.CSharp2CUDA.PackageTests.trx',
    '--results-directory',
    $ResultsDirectory
) + $common)

$packageAssembly = Join-Path $packagesRoot (
    'supprocom.csharp2cuda/0.2.1/lib/net10.0/Supprocom.CSharp2CUDA.dll')
$testAssembly = Join-Path $testSourceRoot (
    'bin/Release/net10.0/Supprocom.CSharp2CUDA.dll')
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $packageAssembly).Hash -ne
    (Get-FileHash -Algorithm SHA256 -LiteralPath $testAssembly).Hash) {
    throw 'The test output does not contain the exact package assembly.'
}

$summaryPath = Join-Path $evidenceRoot 'summary.txt'
$summary = "Package SHA-256: $hash`nThe exact-package source suite passed.`n"
[System.IO.File]::WriteAllText($summaryPath, $summary)
Write-Output $summary.TrimEnd()

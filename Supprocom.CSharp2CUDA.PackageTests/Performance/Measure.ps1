param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string] $RunDirectory,

    [Parameter(Mandatory = $true)]
    [string] $PackageVersion,

    [Parameter(Mandatory = $true)]
    [string] $SourceCommit
)

$ErrorActionPreference = 'Stop'
if ($SourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'The source commit is invalid.'
}
$packageRoot = [System.IO.Path]::GetFullPath($PackageDirectory)
$package = Get-Item -LiteralPath (
    Join-Path $packageRoot "Supprocom.CSharp2CUDA.$PackageVersion.nupkg")
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runBase = [System.IO.Path]::GetFullPath($RunDirectory)
$repositoryPrefix = [System.IO.Path]::GetFullPath($repositoryRoot) +
    [System.IO.Path]::DirectorySeparatorChar
if ($runBase.StartsWith(
        $repositoryPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The performance directory must be outside the source checkout.'
}

$packageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash
$runRoot = Join-Path $runBase ('performance/' + $packageHash.Substring(0, 16))
$evidenceRoot = Join-Path $runRoot 'evidence'
$packagesRoot = Join-Path $runRoot 'packages'
$performanceRoot = Join-Path $runRoot 'semantic'
$corpusRoot = Join-Path $runRoot 'corpus'
$buildRoot = Join-Path $runRoot 'build'
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
New-Item -ItemType Directory -Force -Path $performanceRoot | Out-Null
New-Item -ItemType Directory -Force -Path $corpusRoot | Out-Null
New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet-home'
$env:NUGET_PACKAGES = $packagesRoot
$env:NUGET_HTTP_CACHE_PATH = Join-Path $runRoot 'nuget-http-cache'
$env:TEMP = Join-Path $runRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null
New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES | Out-Null
New-Item -ItemType Directory -Force -Path $env:NUGET_HTTP_CACHE_PATH | Out-Null
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

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
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
$nugetPath = Join-Path $runRoot 'NuGet.config'
[System.IO.File]::WriteAllText($nugetPath, $nugetConfiguration)

function Invoke-Process {
    param(
        [string] $Name,
        [string] $FileName,
        [string[]] $Arguments,
        [int] $ExpectedExitCode = 0
    )

    $standardOutputPath = Join-Path $evidenceRoot ($Name + '.stdout.txt')
    $standardErrorPath = Join-Path $evidenceRoot ($Name + '.stderr.txt')
    $exitPath = Join-Path $evidenceRoot ($Name + '.exit.txt')
    $commandPath = Join-Path $evidenceRoot ($Name + '.command.txt')
    [System.IO.File]::WriteAllText(
        $commandPath,
        $FileName + ' ' + ($Arguments -join ' ') + [Environment]::NewLine)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $runRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) {
        throw "Cannot start $FileName for $Name."
    }
    $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(300000)) {
        $process.Kill($true)
        throw "$FileName timed out for $Name."
    }
    $timer.Stop()
    $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
    $standardError = $standardErrorTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($standardOutputPath, $standardOutput)
    [System.IO.File]::WriteAllText($standardErrorPath, $standardError)
    [System.IO.File]::WriteAllText($exitPath, $process.ExitCode.ToString())
    if ($process.ExitCode -ne $ExpectedExitCode) {
        throw "$FileName returned $($process.ExitCode) for $Name."
    }
    return [pscustomobject]@{
        Output = $standardOutput + $standardError
        ElapsedMilliseconds = $timer.Elapsed.TotalMilliseconds
    }
}

Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Copy-Item -Destination $performanceRoot -Force
$corpusSource = Join-Path $repositoryRoot (
    'Supprocom.CSharp2CUDA.Tests/PortabilityCorpus')
Get-ChildItem -LiteralPath $corpusSource -File -Recurse | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($corpusSource, $_.FullName)
    $destination = Join-Path $corpusRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
}

$performanceProject = Join-Path $performanceRoot 'Performance.csproj'
$compilerAssembly = Join-Path $packagesRoot (
    "supprocom.csharp2cuda/$PackageVersion/build/compiler/" +
    'Supprocom.CSharp2CUDA.Compiler.dll')
$performanceProperties = @(
    "-p:CSharp2CUDAPackageVersion=$PackageVersion",
    "-p:CSharp2CUDACompilerAssembly=$compilerAssembly"
)
Invoke-Process -Name 'semantic-restore' -FileName 'dotnet' -Arguments (@(
    'restore',
    $performanceProject,
    '--configfile',
    $nugetPath,
    '--packages',
    $packagesRoot,
    '--force',
    '--no-cache'
) + $performanceProperties) | Out-Null
if (-not (Test-Path -LiteralPath $compilerAssembly)) {
    throw 'The exact package compiler assembly is missing.'
}
Invoke-Process -Name 'semantic-build' -FileName 'dotnet' -Arguments (@(
    'build',
    $performanceProject,
    '--configuration',
    'Release',
    '--no-restore'
) + $performanceProperties) | Out-Null
$performanceAssembly = Join-Path $performanceRoot (
    'bin/Release/net10.0/Performance.dll')
$semanticOutput = Join-Path $evidenceRoot 'semantic-performance.json'
Invoke-Process -Name 'semantic-run' -FileName 'dotnet' -Arguments @(
    $performanceAssembly,
    $corpusRoot,
    $semanticOutput,
    $SourceCommit
) | Out-Null

$projectTemplate = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
{0}
</Project>
"@
$packageReference = @"
  <ItemGroup>
    <PackageReference Include="Supprocom.CSharp2CUDA" Version="[$PackageVersion]" />
  </ItemGroup>
"@
$ordinarySource = @"
namespace BuildPerformance;

internal static unsafe class BuildProbe
{
    public static void Run(int value, int* output)
    {
        var result = value;
        for (var index = 0; index < 64; index++)
            result = (result * 31) ^ index;
        output[0] = result;
    }
}
"@
$markedSource = @"
using Supprocom.CSharp2CUDA;

namespace BuildPerformance;

[TranspileToCUDA("cuda/Marked.cu")]
internal static unsafe class BuildProbe
{
    [CudaGlobal(Name = "performance_probe")]
    private static void Run(int value, int* output)
    {
        var result = value;
        for (var index = 0; index < 64; index++)
            result = (result * 31) ^ index;
        output[0] = result;
    }
}
"@
$projects = @{}
foreach ($name in @('Baseline', 'Unmarked', 'Marked')) {
    $projectRoot = Join-Path $buildRoot $name
    New-Item -ItemType Directory -Force -Path $projectRoot | Out-Null
    $reference = if ($name -eq 'Baseline') { '' } else { $packageReference }
    $source = if ($name -eq 'Marked') { $markedSource } else { $ordinarySource }
    $projectPath = Join-Path $projectRoot "$name.csproj"
    [System.IO.File]::WriteAllText(
        $projectPath,
        [string]::Format($projectTemplate, $reference))
    [System.IO.File]::WriteAllText(
        (Join-Path $projectRoot 'BuildProbe.cs'),
        $source)
    $projects[$name] = $projectPath
    Invoke-Process -Name "$name-restore" -FileName 'dotnet' -Arguments @(
        'restore',
        $projectPath,
        '--configfile',
        $nugetPath,
        '--packages',
        $packagesRoot,
        '--force',
        '--no-cache'
    ) | Out-Null
    Invoke-Process -Name "$name-warm" -FileName 'dotnet' -Arguments @(
        'build',
        $projectPath,
        '--configuration',
        'Release',
        '--no-restore',
        '--nologo',
        '--verbosity',
        'quiet',
        '-t:Rebuild'
    ) | Out-Null
}

$samples = [System.Collections.Generic.List[object]]::new()
for ($iteration = 0; $iteration -lt 10; $iteration++) {
    $order = switch ($iteration % 3) {
        0 { @('Baseline', 'Unmarked', 'Marked') }
        1 { @('Unmarked', 'Marked', 'Baseline') }
        2 { @('Marked', 'Baseline', 'Unmarked') }
    }
    foreach ($name in $order) {
        $measurement = Invoke-Process `
            -Name ("$name-build-{0:D2}" -f ($iteration + 1)) `
            -FileName 'dotnet' `
            -Arguments @(
                'build',
                $projects[$name],
                '--configuration',
                'Release',
                '--no-restore',
                '--nologo',
                '--verbosity',
                'quiet',
                '-t:Rebuild'
            )
        $samples.Add([pscustomobject]@{
            Project = $name
            Iteration = $iteration + 1
            ElapsedMilliseconds = $measurement.ElapsedMilliseconds
        })
    }
}

function Get-Percentile {
    param(
        [double[]] $Values,
        [double] $Percentile
    )
    $ordered = @($Values | Sort-Object)
    $rank = $Percentile * ($ordered.Count - 1)
    $lower = [Math]::Floor($rank)
    $upper = [Math]::Ceiling($rank)
    if ($lower -eq $upper) {
        return $ordered[$lower]
    }
    return $ordered[$lower] +
        ($ordered[$upper] - $ordered[$lower]) * ($rank - $lower)
}

$statistics = @{}
foreach ($name in @('Baseline', 'Unmarked', 'Marked')) {
    $values = @($samples | Where-Object Project -eq $name |
        Select-Object -ExpandProperty ElapsedMilliseconds)
    $statistics[$name] = [pscustomobject]@{
        MedianMilliseconds = Get-Percentile -Values $values -Percentile 0.5
        P95Milliseconds = Get-Percentile -Values $values -Percentile 0.95
    }
}
$unmarkedDelta = $statistics.Unmarked.MedianMilliseconds -
    $statistics.Baseline.MedianMilliseconds
$unmarkedLimit = [Math]::Max(
    $statistics.Baseline.MedianMilliseconds * 0.05,
    100.0)
$markedDelta = $statistics.Marked.MedianMilliseconds -
    $statistics.Unmarked.MedianMilliseconds
$markedLimit = [Math]::Max(
    $statistics.Unmarked.MedianMilliseconds * 0.20,
    1000.0)
$buildPass = $unmarkedDelta -le $unmarkedLimit -and $markedDelta -le $markedLimit
$samples | Export-Csv -LiteralPath (Join-Path $evidenceRoot 'build-samples.csv') -NoTypeInformation

$processor = Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name
$computer = Get-CimInstance Win32_ComputerSystem
$video = @(Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name)
$sdk = (Invoke-Process -Name 'dotnet-version' -FileName 'dotnet' -Arguments @('--version')).Output.Trim()
$nvrtcPath = $env:CSHARP2CUDA_NVRTC_LIBRARY
$nvrtcHash = if (-not [string]::IsNullOrWhiteSpace($nvrtcPath) -and
    (Test-Path -LiteralPath $nvrtcPath)) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $nvrtcPath).Hash
}
else {
    $null
}
$summary = [ordered]@{
    SourceCommit = $SourceCommit
    PackageVersion = $PackageVersion
    PackageSha256 = $packageHash
    DotNetSdk = $sdk
    OperatingSystem = [Environment]::OSVersion.ToString()
    Processor = $processor
    LogicalProcessorCount = [Environment]::ProcessorCount
    MemoryBytes = [long]$computer.TotalPhysicalMemory
    VideoControllers = $video
    NvrtcPath = $nvrtcPath
    NvrtcSha256 = $nvrtcHash
    Baseline = $statistics.Baseline
    Unmarked = $statistics.Unmarked
    Marked = $statistics.Marked
    UnmarkedOverheadMilliseconds = $unmarkedDelta
    UnmarkedLimitMilliseconds = $unmarkedLimit
    MarkedOverheadMilliseconds = $markedDelta
    MarkedLimitMilliseconds = $markedLimit
    BuildBudgetsPassed = $buildPass
}
[System.IO.File]::WriteAllText(
    (Join-Path $evidenceRoot 'build-performance.json'),
    ($summary | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
if (-not $buildPass) {
    throw 'A build performance budget failed.'
}

Write-Output ("UnmarkedOverheadMilliseconds={0:F3}" -f $unmarkedDelta)
Write-Output ("UnmarkedLimitMilliseconds={0:F3}" -f $unmarkedLimit)
Write-Output ("MarkedOverheadMilliseconds={0:F3}" -f $markedDelta)
Write-Output ("MarkedLimitMilliseconds={0:F3}" -f $markedLimit)
Write-Output 'BuildBudgetsPassed=true'

param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string] $RunDirectory,

    [Parameter(Mandatory = $true)]
    [string] $PackageVersion
)

$ErrorActionPreference = 'Stop'
$packageRoot = [System.IO.Path]::GetFullPath($PackageDirectory)
$toolPackage = Get-Item -LiteralPath (
    Join-Path $packageRoot "Supprocom.CSharp2CUDA.Tool.$PackageVersion.nupkg")
$corePackage = Get-Item -LiteralPath (
    Join-Path $packageRoot "Supprocom.CSharp2CUDA.$PackageVersion.nupkg")
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runBase = [System.IO.Path]::GetFullPath($RunDirectory)
$repositoryPrefix = [System.IO.Path]::GetFullPath($repositoryRoot) +
    [System.IO.Path]::DirectorySeparatorChar
if ($runBase.StartsWith(
        $repositoryPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The tool test directory must be outside the source checkout.'
}

$toolHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $toolPackage.FullName).Hash
$coreHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $corePackage.FullName).Hash
$runRoot = Join-Path $runBase ('tool-package-tests/' + $toolHash.Substring(0, 16))
$evidenceRoot = Join-Path $runRoot 'evidence'
$packagesRoot = Join-Path $runRoot 'packages'
$toolRoot = Join-Path $runRoot 'tool'
$inputRoot = Join-Path $runRoot 'input'
$scaffoldRoot = Join-Path $runRoot 'scaffold'
$runPrefix = [System.IO.Path]::GetFullPath($runRoot) +
    [System.IO.Path]::DirectorySeparatorChar
foreach ($path in @($inputRoot, $scaffoldRoot, $toolRoot)) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith(
            $runPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'A tool test path is outside the test run directory.'
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
New-Item -ItemType Directory -Force -Path $inputRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet-home'
$env:NUGET_PACKAGES = $packagesRoot
$env:NUGET_HTTP_CACHE_PATH = Join-Path $runRoot 'nuget-http-cache'
$env:TEMP = Join-Path $runRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null
New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES | Out-Null
New-Item -ItemType Directory -Force -Path $env:NUGET_HTTP_CACHE_PATH | Out-Null
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

$fixtureRoot = Join-Path $PSScriptRoot 'ToolInput'
Get-ChildItem -LiteralPath $fixtureRoot -File |
    Copy-Item -Destination $inputRoot -Force
$inputProject = Join-Path $inputRoot 'ToolInput.csproj'
$inputSource = Join-Path $inputRoot 'Algorithm.cs'
$inputHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $inputSource).Hash

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
      <package pattern="Supprocom.CSharp2CUDA.Tool" />
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
    if (-not $process.Start()) {
        throw "Cannot start $FileName for $Name."
    }

    $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(300000)) {
        $process.Kill($true)
        throw "$FileName timed out for $Name."
    }

    $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
    $standardError = $standardErrorTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($standardOutputPath, $standardOutput)
    [System.IO.File]::WriteAllText($standardErrorPath, $standardError)
    [System.IO.File]::WriteAllText($exitPath, $process.ExitCode.ToString())
    if ($process.ExitCode -ne $ExpectedExitCode) {
        throw "$FileName returned $($process.ExitCode) for $Name."
    }
    return $standardOutput + $standardError
}

Invoke-Process -Name 'tool-install' -FileName 'dotnet' -Arguments @(
    'tool',
    'install',
    'Supprocom.CSharp2CUDA.Tool',
    '--tool-path',
    $toolRoot,
    '--version',
    $PackageVersion,
    '--configfile',
    $nugetPath,
    '--no-cache'
) | Out-Null

$toolExecutable = @(
    Join-Path $toolRoot 'csharp2cuda'
    Join-Path $toolRoot 'csharp2cuda.exe'
) | Where-Object {
    Test-Path -LiteralPath $_ -PathType Leaf
} | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($toolExecutable)) {
    throw 'The installed csharp2cuda command is missing.'
}

Invoke-Process -Name 'input-restore' -FileName 'dotnet' -Arguments @(
    'restore',
    $inputProject,
    '--configfile',
    $nugetPath,
    '--packages',
    $packagesRoot,
    '--force',
    '--no-cache'
) | Out-Null

$method = 'ExistingAlgorithms.DistanceTransform.Calculate(double)'
$checkOutput = Invoke-Process -Name 'tool-check' -FileName $toolExecutable -Arguments @(
    'check',
    '--project',
    $inputProject,
    '--method',
    $method
)
if ($checkOutput -notmatch 'elementwise\s+Compatible' -or
    $checkOutput -notmatch 'single-thread\s+Compatible') {
    throw 'The installed tool did not report both supported mappings.'
}

Invoke-Process -Name 'tool-scaffold' -FileName $toolExecutable -Arguments @(
    'scaffold',
    '--project',
    $inputProject,
    '--method',
    $method,
    '--mapping',
    'elementwise',
    '--output',
    $scaffoldRoot
) | Out-Null
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $inputSource).Hash -ne $inputHashBefore) {
    throw 'The scaffold command changed the existing algorithm source.'
}

$scaffoldProject = @(Get-ChildItem -LiteralPath $scaffoldRoot -File -Filter '*.csproj')
if ($scaffoldProject.Count -ne 1) {
    throw 'The scaffold does not contain exactly one project.'
}
$scaffoldProjectPath = $scaffoldProject[0].FullName
$projectSource = Get-Content -Raw -LiteralPath $scaffoldProjectPath
if ($projectSource -notmatch
    ('PackageReference Include="Supprocom.CSharp2CUDA" Version="' +
        [regex]::Escape($PackageVersion) + '"')) {
    throw 'The scaffold does not use the exact core package version.'
}

Invoke-Process -Name 'scaffold-restore' -FileName 'dotnet' -Arguments @(
    'restore',
    $scaffoldProjectPath,
    '--configfile',
    $nugetPath,
    '--packages',
    $packagesRoot,
    '--force',
    '--no-cache'
) | Out-Null
Invoke-Process -Name 'scaffold-build' -FileName 'dotnet' -Arguments @(
    'build',
    $scaffoldProjectPath,
    '--configuration',
    'Release',
    '--no-restore'
) | Out-Null

$cudaFiles = @(Get-ChildItem -LiteralPath $scaffoldRoot -Recurse -File -Filter '*.cu')
if ($cudaFiles.Count -ne 1) {
    throw 'The scaffold build did not create exactly one CUDA file.'
}
$cudaSource = Get-Content -Raw -LiteralPath $cudaFiles[0].FullName
if ($cudaSource -notmatch 'extern "C" __global__ void cs2cuda_DistanceTransform_Calculate_elementwise_' -or
    $cudaSource -notmatch 'sqrt\(') {
    throw 'The scaffold CUDA source does not contain the selected algorithm.'
}
$firstCudaHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cudaFiles[0].FullName).Hash

Invoke-Process -Name 'tool-refresh' -FileName $toolExecutable -Arguments @(
    'refresh',
    '--output',
    $scaffoldRoot
) | Out-Null
Invoke-Process -Name 'scaffold-rebuild' -FileName 'dotnet' -Arguments @(
    'build',
    $scaffoldProjectPath,
    '--configuration',
    'Release',
    '--no-restore',
    '-t:Rebuild'
) | Out-Null
$cudaFiles = @(Get-ChildItem -LiteralPath $scaffoldRoot -Recurse -File -Filter '*.cu')
if ($cudaFiles.Count -ne 1 -or
    (Get-FileHash -Algorithm SHA256 -LiteralPath $cudaFiles[0].FullName).Hash -ne
        $firstCudaHash) {
    throw 'The refreshed scaffold did not reproduce the same CUDA bytes.'
}

$summary = @"
ToolPackageSha256=$toolHash
CorePackageSha256=$coreHash
AlgorithmSourceSha256=$inputHashBefore
GeneratedCudaSha256=$firstCudaHash
InstalledTool=passed
ScaffoldBuild=passed
RefreshDeterminism=passed
"@
[System.IO.File]::WriteAllText((Join-Path $evidenceRoot 'summary.txt'), $summary)
Write-Output $summary.TrimEnd()

param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [string] $ExpectedCommit
)

$ErrorActionPreference = 'Stop'
$packageRoot = [System.IO.Path]::GetFullPath($PackageDirectory)
$package = Get-Item -LiteralPath (
    Join-Path $packageRoot 'Supprocom.CSharp2CUDA.0.2.0.nupkg')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $ExpectedCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot read the repository commit.'
    }
}
if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'The expected repository commit is invalid.'
}
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $package.FullName).Hash
$runRoot = Join-Path $repositoryRoot (
    'artifacts/package-tests/' + $hash.Substring(0, 16))
$evidenceRoot = Join-Path $runRoot 'evidence'
$packagesRoot = Join-Path $runRoot 'packages'
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

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

function Invoke-DotNet {
    param(
        [string] $Name,
        [string[]] $Arguments,
        [int] $ExpectedExitCode
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
    if (-not $process.WaitForExit(120000)) {
        $process.Kill()
        throw "dotnet timed out for $Name."
    }

    $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
    $standardError = $standardErrorTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($standardOutputPath, $standardOutput)
    [System.IO.File]::WriteAllText($standardErrorPath, $standardError)
    [System.IO.File]::WriteAllText($exitPath, $process.ExitCode.ToString())
    if ($process.ExitCode -ne $ExpectedExitCode) {
        throw "dotnet returned $($process.ExitCode) for $Name."
    }

    return $standardOutput + $standardError
}

function Invoke-Case {
    param(
        [string] $Name,
        [bool] $ExpectSuccess
    )

    $project = Join-Path $PSScriptRoot "$Name/$Name.csproj"
    $caseRoot = Join-Path $runRoot $Name
    $casePath = [System.IO.Path]::GetFullPath($caseRoot)
    $runPrefix = [System.IO.Path]::GetFullPath($runRoot) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $casePath.StartsWith(
            $runPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'The package test path is outside the test output directory.'
    }
    if (Test-Path -LiteralPath $casePath) {
        Remove-Item -LiteralPath $casePath -Recurse -Force
    }
    $baseOutput = Join-Path $caseRoot 'bin/'
    $baseIntermediate = Join-Path $caseRoot 'obj/'
    $common = @(
        "-p:BaseOutputPath=$baseOutput",
        "-p:BaseIntermediateOutputPath=$baseIntermediate"
    )
    Invoke-DotNet -Name "$Name-restore" -ExpectedExitCode 0 -Arguments (@(
        'restore',
        $project,
        '--configfile',
        $nugetPath,
        '--packages',
        $packagesRoot,
        '--force',
        '--no-cache'
    ) + $common) | Out-Null
    $expectedCode = if ($ExpectSuccess) { 0 } else { 1 }
    $output = Invoke-DotNet -Name "$Name-build" -ExpectedExitCode $expectedCode -Arguments (@(
        'build',
        $project,
        '--configuration',
        'Release',
        '--no-restore'
    ) + $common)
    return [pscustomobject]@{
        Root = Join-Path $baseOutput 'Release/net10.0'
        Output = $output
        Project = $project
        Common = $common
    }
}

$auditProject = Join-Path $PSScriptRoot 'Audit/Audit.csproj'
Invoke-DotNet -Name 'Audit-restore' -ExpectedExitCode 0 -Arguments @(
    'restore',
    $auditProject,
    '--configfile',
    $nugetPath,
    '--packages',
    $packagesRoot,
    '--force',
    '--no-cache'
) | Out-Null
Invoke-DotNet -Name 'Audit-run' -ExpectedExitCode 0 -Arguments @(
    'run',
    '--project',
    $auditProject,
    '--configuration',
    'Release',
    '--no-restore',
    '--',
    $packageRoot,
    $repositoryRoot,
    $ExpectedCommit
) | Out-Null

$attributed = Invoke-Case -Name 'Attributed' -ExpectSuccess $true
$attributedCuda = Join-Path $attributed.Root 'cuda/Attributed.cu'
if (-not (Test-Path -LiteralPath $attributedCuda)) {
    throw 'The attributed project did not create CUDA source.'
}
if (-not (Test-Path -LiteralPath (Join-Path $attributed.Root 'Attributed.dll'))) {
    throw 'The attributed project did not keep its managed assembly.'
}

$attributedDefault = Invoke-Case -Name 'AttributedDefault' -ExpectSuccess $true
$attributedDefaultCuda = Join-Path $attributedDefault.Root 'AttributedDefault.cu'
if (-not (Test-Path -LiteralPath $attributedDefaultCuda)) {
    throw 'The empty attribute path did not select the default CUDA output.'
}

$manual = Invoke-Case -Name 'Manual' -ExpectSuccess $true
$manualAssembly = Join-Path $manual.Root 'Manual.dll'
$taskAssembly = Join-Path $packagesRoot (
    'supprocom.csharp2cuda/0.2.0/build/task/Supprocom.CSharp2CUDA.Build.dll')
Invoke-DotNet -Name 'Manual-run' -ExpectedExitCode 0 -Arguments @(
    $manualAssembly,
    $taskAssembly
) | Out-Null
if (Get-ChildItem -LiteralPath $manual.Root -Recurse -File -Filter '*.cu') {
    throw 'The manual API project created automatic CUDA source.'
}

$dedicated = Invoke-Case -Name 'Dedicated' -ExpectSuccess $true
$dedicatedCuda = Join-Path $dedicated.Root 'cuda/Dedicated.cu'
if (-not (Test-Path -LiteralPath $dedicatedCuda)) {
    throw 'The dedicated project did not create CUDA source.'
}
if (Test-Path -LiteralPath (Join-Path $dedicated.Root 'Dedicated.dll')) {
    throw 'The dedicated project created a managed assembly.'
}
$managedPatterns = @('*.dll', '*.pdb', '*.deps.json', '*.runtimeconfig.json')
foreach ($pattern in $managedPatterns) {
    if (Get-ChildItem -LiteralPath $dedicated.Root -File -Filter $pattern) {
        throw "The dedicated project created managed output that matches $pattern."
    }
}

$noMarker = Invoke-Case -Name 'NoMarker' -ExpectSuccess $true
if (Get-ChildItem -LiteralPath $noMarker.Root -Recurse -File -Filter '*.cu') {
    throw 'The unmarked project created CUDA source.'
}
if (-not (Test-Path -LiteralPath (Join-Path $noMarker.Root 'NoMarker.dll'))) {
    throw 'The unmarked project did not create its managed assembly.'
}

$invalidProject = Invoke-Case -Name 'InvalidProjectPath' -ExpectSuccess $false
if ($invalidProject.Output -notmatch 'CS2CUDA021') {
    throw 'The invalid project path did not report CS2CUDA021.'
}

$invalidClass = Invoke-Case -Name 'InvalidClassPath' -ExpectSuccess $false
if ($invalidClass.Output -notmatch 'CS2CUDA021') {
    throw 'The invalid class path did not report CS2CUDA021.'
}

$compileFailure = Invoke-Case -Name 'CompileFailure' -ExpectSuccess $false
if ($compileFailure.Output -notmatch 'CS0103') {
    throw 'The compile failure did not report the C# compiler error.'
}
if (Test-Path -LiteralPath (Join-Path $compileFailure.Root 'CompileFailure.cu')) {
    throw 'The failed C# compilation created CUDA source.'
}

$staleOutput = Invoke-Case -Name 'StaleOutput' -ExpectSuccess $true
$staleCuda = Join-Path $staleOutput.Root 'StaleOutput.cu'
if (-not (Test-Path -LiteralPath $staleCuda)) {
    throw 'The stale-output probe did not create its initial CUDA source.'
}
Invoke-DotNet -Name 'StaleOutput-failed-build' -ExpectedExitCode 1 -Arguments (@(
    'build',
    $staleOutput.Project,
    '--configuration',
    'Release',
    '--no-restore',
    '-p:DefineConstants=FAIL'
) + $staleOutput.Common) | Out-Null
if (Test-Path -LiteralPath $staleCuda) {
    throw 'A failed C# compilation kept stale CUDA source.'
}

$summaryPath = Join-Path $evidenceRoot 'summary.txt'
$summary = "Package SHA-256: $hash`nAll package build-integration checks passed.`n"
[System.IO.File]::WriteAllText($summaryPath, $summary)
Write-Output $summary.TrimEnd()

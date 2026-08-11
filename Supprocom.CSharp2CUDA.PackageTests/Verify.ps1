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
        [bool] $ExpectSuccess,
        [string] $ProjectName,
        [string[]] $BuildArguments = @()
    )

    if ([string]::IsNullOrWhiteSpace($ProjectName)) {
        $ProjectName = $Name
    }
    $project = Join-Path $PSScriptRoot "$ProjectName/$ProjectName.csproj"
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
    ) + $BuildArguments + $common)
    return [pscustomobject]@{
        Root = Join-Path $baseOutput 'Release/net10.0'
        IntermediateRoot = $baseIntermediate
        Output = $output
        Project = $project
        Common = $common
    }
}

function Assert-NoCompilerPayload {
    param([object] $Case)

    if (Get-ChildItem -LiteralPath $Case.IntermediateRoot -Recurse -File -Filter (
            'Supprocom.CSharp2CUDA.payload')) {
        throw 'The build kept an intermediate CUDA compiler payload.'
    }
}

function Assert-NoAutomaticState {
    param([object] $Case)

    Assert-NoCompilerPayload -Case $Case
    if (Get-ChildItem -LiteralPath $Case.IntermediateRoot -Recurse -File -Filter (
            'Supprocom.CSharp2CUDA.outputs')) {
        throw 'The build kept an automatic output manifest.'
    }
}

function Invoke-RepeatCase {
    param(
        [string] $Name,
        [object] $Case
    )

    Invoke-DotNet -Name "$Name-repeat-build" -ExpectedExitCode 0 -Arguments (@(
        'build',
        $Case.Project,
        '--configuration',
        'Release',
        '--no-restore'
    ) + $Case.Common) | Out-Null
    Assert-NoCompilerPayload -Case $Case
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

$generatorProject = Join-Path $repositoryRoot (
    'Supprocom.CSharp2CUDA.PackageTests.Generator/' +
    'Supprocom.CSharp2CUDA.PackageTests.Generator.csproj')
Invoke-DotNet -Name 'Generator-restore' -ExpectedExitCode 0 -Arguments @(
    'restore',
    $generatorProject,
    '--configfile',
    $nugetPath,
    '--packages',
    $packagesRoot,
    '--force',
    '--no-cache'
) | Out-Null
Invoke-DotNet -Name 'Generator-build' -ExpectedExitCode 0 -Arguments @(
    'build',
    $generatorProject,
    '--configuration',
    'Release',
    '--no-restore'
) | Out-Null

$lookalikeProject = Join-Path $repositoryRoot (
    'Supprocom.CSharp2CUDA.PackageTests.Lookalike/' +
    'Supprocom.CSharp2CUDA.PackageTests.Lookalike.csproj')
Invoke-DotNet -Name 'Lookalike-restore' -ExpectedExitCode 0 -Arguments @(
    'restore',
    $lookalikeProject,
    '--configfile',
    $nugetPath,
    '--packages',
    $packagesRoot,
    '--force',
    '--no-cache'
) | Out-Null
Invoke-DotNet -Name 'Lookalike-build' -ExpectedExitCode 0 -Arguments @(
    'build',
    $lookalikeProject,
    '--configuration',
    'Release',
    '--no-restore'
) | Out-Null

$lookalikeAttributed = Invoke-Case `
    -Name 'LookalikeAttributed' `
    -ExpectSuccess $true `
    -BuildArguments @('-p:RunAnalyzers=false')
Assert-NoAutomaticState -Case $lookalikeAttributed
if (-not (Test-Path -LiteralPath (
        Join-Path $lookalikeAttributed.Root 'LookalikeAttributed.dll'))) {
    throw 'The lookalike-marker project did not create its managed assembly.'
}
if (Get-ChildItem -LiteralPath $lookalikeAttributed.Root -Recurse -File -Filter '*.cu') {
    throw 'The lookalike-marker project created CUDA source.'
}

$disabledAttributed = Invoke-Case `
    -Name 'AttributedDisabledFirst' `
    -ProjectName 'Attributed' `
    -ExpectSuccess $false `
    -BuildArguments @('-p:RunAnalyzers=false')
if ($disabledAttributed.Output -notmatch 'CS2CUDA023') {
    throw 'The first disabled class-marker build did not report CS2CUDA023.'
}
Assert-NoAutomaticState -Case $disabledAttributed
if (Get-ChildItem -LiteralPath $disabledAttributed.Root -Recurse -File -Filter '*.cu') {
    throw 'The first disabled class-marker build created CUDA source.'
}

$attributed = Invoke-Case -Name 'Attributed' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $attributed
$attributedCuda = Join-Path $attributed.Root 'cuda/Attributed.cu'
if (-not (Test-Path -LiteralPath $attributedCuda)) {
    throw 'The attributed project did not create CUDA source.'
}
if (-not (Test-Path -LiteralPath (Join-Path $attributed.Root 'Attributed.dll'))) {
    throw 'The attributed project did not keep its managed assembly.'
}
Invoke-RepeatCase -Name 'Attributed' -Case $attributed
if (-not (Test-Path -LiteralPath $attributedCuda)) {
    throw 'The repeated attributed build did not create CUDA source.'
}
$disabledAttributedOutput = Invoke-DotNet `
    -Name 'Attributed-analyzers-disabled-build' `
    -ExpectedExitCode 1 `
    -Arguments (@(
        'build',
        $attributed.Project,
        '--configuration',
        'Release',
        '--no-restore',
        '-t:Rebuild',
        '-p:RunAnalyzers=false'
    ) + $attributed.Common)
if ($disabledAttributedOutput -notmatch 'CS2CUDA023') {
    throw 'The repeated disabled class-marker build did not report CS2CUDA023.'
}
Assert-NoAutomaticState -Case $attributed
if (Test-Path -LiteralPath $attributedCuda) {
    throw 'The repeated disabled class-marker build kept stale CUDA source.'
}

$attributedDefault = Invoke-Case -Name 'AttributedDefault' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $attributedDefault
$attributedDefaultCuda = Join-Path $attributedDefault.Root 'AttributedDefault.cu'
if (-not (Test-Path -LiteralPath $attributedDefaultCuda)) {
    throw 'The empty attribute path did not select the default CUDA output.'
}
Invoke-RepeatCase -Name 'AttributedDefault' -Case $attributedDefault
if (-not (Test-Path -LiteralPath $attributedDefaultCuda)) {
    throw 'The repeated default-path build did not create CUDA source.'
}

$manual = Invoke-Case -Name 'Manual' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $manual
$manualAssembly = Join-Path $manual.Root 'Manual.dll'
$taskAssembly = Join-Path $packagesRoot (
    'supprocom.csharp2cuda/0.2.0/build/task/Supprocom.CSharp2CUDA.Build.dll')
$compilerAssembly = Join-Path $packagesRoot (
    'supprocom.csharp2cuda/0.2.0/build/compiler/' +
    'Supprocom.CSharp2CUDA.Compiler.dll')
Invoke-DotNet -Name 'Manual-run' -ExpectedExitCode 0 -Arguments @(
    $manualAssembly,
    $taskAssembly,
    $compilerAssembly
) | Out-Null
if (Get-ChildItem -LiteralPath $manual.Root -Recurse -File -Filter '*.cu') {
    throw 'The manual API project created automatic CUDA source.'
}
Invoke-RepeatCase -Name 'Manual' -Case $manual
if (Get-ChildItem -LiteralPath $manual.Root -Recurse -File -Filter '*.cu') {
    throw 'The repeated manual build created automatic CUDA source.'
}
Invoke-DotNet `
    -Name 'Manual-analyzers-disabled-build' `
    -ExpectedExitCode 0 `
    -Arguments (@(
        'build',
        $manual.Project,
        '--configuration',
        'Release',
        '--no-restore',
        '-t:Rebuild',
        '-p:RunAnalyzers=false'
    ) + $manual.Common) | Out-Null
Assert-NoCompilerPayload -Case $manual
Invoke-DotNet -Name 'Manual-analyzers-disabled-run' -ExpectedExitCode 0 -Arguments @(
    $manualAssembly,
    $taskAssembly,
    $compilerAssembly
) | Out-Null
if (Get-ChildItem -LiteralPath $manual.Root -Recurse -File -Filter '*.cu') {
    throw 'The manual project created CUDA source when analyzers were disabled.'
}

$dedicated = Invoke-Case -Name 'Dedicated' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $dedicated
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
Invoke-RepeatCase -Name 'Dedicated' -Case $dedicated
if (-not (Test-Path -LiteralPath $dedicatedCuda)) {
    throw 'The repeated dedicated build did not create CUDA source.'
}
foreach ($pattern in $managedPatterns) {
    if (Get-ChildItem -LiteralPath $dedicated.Root -File -Filter $pattern) {
        throw "The repeated dedicated build created managed output that matches $pattern."
    }
}

$noMarker = Invoke-Case -Name 'NoMarker' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $noMarker
if (Get-ChildItem -LiteralPath $noMarker.Root -Recurse -File -Filter '*.cu') {
    throw 'The unmarked project created CUDA source.'
}
if (-not (Test-Path -LiteralPath (Join-Path $noMarker.Root 'NoMarker.dll'))) {
    throw 'The unmarked project did not create its managed assembly.'
}
Invoke-DotNet -Name 'NoMarker-run' -ExpectedExitCode 0 -Arguments @(
    (Join-Path $noMarker.Root 'NoMarker.dll')
) | Out-Null
Invoke-RepeatCase -Name 'NoMarker' -Case $noMarker
if (Get-ChildItem -LiteralPath $noMarker.Root -Recurse -File -Filter '*.cu') {
    throw 'The repeated unmarked build created CUDA source.'
}
Invoke-DotNet `
    -Name 'NoMarker-analyzers-disabled-build' `
    -ExpectedExitCode 0 `
    -Arguments (@(
        'build',
        $noMarker.Project,
        '--configuration',
        'Release',
        '--no-restore',
        '-t:Rebuild',
        '-p:RunAnalyzers=false'
    ) + $noMarker.Common) | Out-Null
Assert-NoCompilerPayload -Case $noMarker
Invoke-DotNet -Name 'NoMarker-analyzers-disabled-run' -ExpectedExitCode 0 -Arguments @(
    (Join-Path $noMarker.Root 'NoMarker.dll')
) | Out-Null
if (Get-ChildItem -LiteralPath $noMarker.Root -Recurse -File -Filter '*.cu') {
    throw 'The unmarked project created CUDA source when analyzers were disabled.'
}

$generatedAttributed = Invoke-Case -Name 'GeneratedAttributed' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $generatedAttributed
$generatedAttributedCuda = Join-Path (
    $generatedAttributed.Root) 'cuda/GeneratedAttributed.cu'
if (-not (Test-Path -LiteralPath $generatedAttributedCuda)) {
    throw 'The generated attributed project did not create CUDA source.'
}
$generatedAttributedSource = Get-Content -Raw -LiteralPath $generatedAttributedCuda
if ($generatedAttributedSource -notmatch '__device__ int Increment\(int value\)' -or
    $generatedAttributedSource -notmatch 'return Increment\(value\);') {
    throw 'The attributed CUDA source does not contain the generated dependency.'
}
if (-not (Test-Path -LiteralPath (
        Join-Path $generatedAttributed.Root 'GeneratedAttributed.dll'))) {
    throw 'The generated attributed project did not keep its managed assembly.'
}
Invoke-RepeatCase -Name 'GeneratedAttributed' -Case $generatedAttributed
if (-not (Test-Path -LiteralPath $generatedAttributedCuda)) {
    throw 'The repeated generated attributed build did not create CUDA source.'
}
$disabledGeneratedOutput = Invoke-DotNet `
    -Name 'GeneratedAttributed-analyzers-disabled-build' `
    -ExpectedExitCode 1 `
    -Arguments (@(
        'build',
        $generatedAttributed.Project,
        '--configuration',
        'Release',
        '--no-restore',
        '-t:Rebuild',
        '-p:RunAnalyzers=false'
    ) + $generatedAttributed.Common)
if ($disabledGeneratedOutput -notmatch 'CS2CUDA023') {
    throw 'The disabled generated class-marker build did not report CS2CUDA023.'
}
Assert-NoAutomaticState -Case $generatedAttributed
if (Test-Path -LiteralPath $generatedAttributedCuda) {
    throw 'The disabled generated class-marker build kept stale CUDA source.'
}

$generatedProject = Invoke-Case -Name 'GeneratedProject' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $generatedProject
$generatedProjectCuda = Join-Path $generatedProject.Root 'cuda/GeneratedProject.cu'
if (-not (Test-Path -LiteralPath $generatedProjectCuda)) {
    throw 'The generated complete project did not create CUDA source.'
}
$generatedProjectSource = Get-Content -Raw -LiteralPath $generatedProjectCuda
if ($generatedProjectSource -notmatch '__device__ int Increment\(int value\)' -or
    $generatedProjectSource -notmatch 'return Increment\(value\);') {
    throw 'The complete-project CUDA source does not contain the generated dependency.'
}
if (Test-Path -LiteralPath (Join-Path $generatedProject.Root 'GeneratedProject.dll')) {
    throw 'The generated complete project created a managed assembly.'
}
Invoke-RepeatCase -Name 'GeneratedProject' -Case $generatedProject
if (-not (Test-Path -LiteralPath $generatedProjectCuda)) {
    throw 'The repeated generated project build did not create CUDA source.'
}
if (Test-Path -LiteralPath (Join-Path $generatedProject.Root 'GeneratedProject.dll')) {
    throw 'The repeated generated project build created a managed assembly.'
}
$disabledProjectOutput = Invoke-DotNet `
    -Name 'GeneratedProject-analyzers-disabled-build' `
    -ExpectedExitCode 1 `
    -Arguments (@(
        'build',
        $generatedProject.Project,
        '--configuration',
        'Release',
        '--no-restore',
        '-t:Rebuild',
        '-p:RunAnalyzers=false'
    ) + $generatedProject.Common)
if ($disabledProjectOutput -notmatch 'CS2CUDA023') {
    throw 'The disabled automatic analyzer did not report CS2CUDA023.'
}
Assert-NoCompilerPayload -Case $generatedProject
if (Test-Path -LiteralPath $generatedProjectCuda) {
    throw 'The disabled automatic analyzer kept stale CUDA source.'
}

$invalidProject = Invoke-Case -Name 'InvalidProjectPath' -ExpectSuccess $false
Assert-NoCompilerPayload -Case $invalidProject
if ($invalidProject.Output -notmatch 'CS2CUDA021') {
    throw 'The invalid project path did not report CS2CUDA021.'
}

$invalidClass = Invoke-Case -Name 'InvalidClassPath' -ExpectSuccess $false
Assert-NoCompilerPayload -Case $invalidClass
if ($invalidClass.Output -notmatch 'CS2CUDA021') {
    throw 'The invalid class path did not report CS2CUDA021.'
}

$compileFailure = Invoke-Case -Name 'CompileFailure' -ExpectSuccess $false
Assert-NoCompilerPayload -Case $compileFailure
if ($compileFailure.Output -notmatch 'CS0103') {
    throw 'The compile failure did not report the C# compiler error.'
}
if (Test-Path -LiteralPath (Join-Path $compileFailure.Root 'CompileFailure.cu')) {
    throw 'The failed C# compilation created CUDA source.'
}

$staleOutput = Invoke-Case -Name 'StaleOutput' -ExpectSuccess $true
Assert-NoCompilerPayload -Case $staleOutput
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
Assert-NoCompilerPayload -Case $staleOutput
if (Test-Path -LiteralPath $staleCuda) {
    throw 'A failed C# compilation kept stale CUDA source.'
}

$summaryPath = Join-Path $evidenceRoot 'summary.txt'
$summary = "Package SHA-256: $hash`nAll package build-integration checks passed.`n"
[System.IO.File]::WriteAllText($summaryPath, $summary)
Write-Output $summary.TrimEnd()

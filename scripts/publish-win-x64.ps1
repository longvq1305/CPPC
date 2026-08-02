[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputDirectory = 'artifacts\publish\win-x64'
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $scriptRoot
}
$resolvedRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$distribution = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $OutputDirectory))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
if (-not $distribution.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output must remain under the repository artifacts directory.'
}

& (Join-Path $scriptRoot 'verify-toolchain.ps1') -RepositoryRoot $resolvedRoot

$solution = Join-Path $resolvedRoot 'PolygonAiBuilder.slnx'
& dotnet 'build' $solution '-c' 'Release'
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
& dotnet 'test' $solution '-c' 'Release' '--no-build'
if ($LASTEXITCODE -ne 0) { throw 'Full test suite failed.' }

if (Test-Path -LiteralPath $distribution) {
    Remove-Item -LiteralPath $distribution -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($distribution) | Out-Null

$project = Join-Path $resolvedRoot 'src\PolygonAiBuilder.Web\PolygonAiBuilder.Web.csproj'
$publishArguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $distribution)
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Self-contained win-x64 publish failed.' }

$sourceToolchain = Join-Path $resolvedRoot 'toolchain'
$destinationToolchain = Join-Path $distribution 'toolchain'
[System.IO.Directory]::CreateDirectory($destinationToolchain) | Out-Null
foreach ($name in @('manifest.json', 'README.md', 'testlib', 'checkers', 'mingw64')) {
    $source = Join-Path $sourceToolchain $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing publish toolchain asset: $source" }
    Copy-Item -LiteralPath $source -Destination $destinationToolchain -Recurse -Force
}

$destinationScripts = Join-Path $distribution 'scripts'
[System.IO.Directory]::CreateDirectory($destinationScripts) | Out-Null
Copy-Item -LiteralPath (Join-Path $scriptRoot 'verify-toolchain.ps1') -Destination $destinationScripts -Force
& (Join-Path $destinationScripts 'verify-toolchain.ps1') -RepositoryRoot $distribution

$executable = Join-Path $distribution 'PolygonAiBuilder.Web.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Published app executable is missing.'
}

Write-Host "Verified self-contained distribution: $distribution"

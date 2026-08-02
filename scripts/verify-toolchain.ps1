[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $scriptRoot
}
$resolvedRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$toolchainRoot = Join-Path $resolvedRoot 'toolchain'
$manifestPath = Join-Path $toolchainRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Missing toolchain manifest: $manifestPath"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($file in $manifest.testlib.files) {
    $relative = [string]$file.relativePath
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $toolchainRoot $relative))
    $prefix = $toolchainRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path in manifest: $relative"
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Missing pinned source: $relative"
    }
    $actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
    if (-not $actual.Equals([string]$file.sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Checksum mismatch: $relative"
    }
}

$compiler = Join-Path $toolchainRoot 'mingw64\bin\g++.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw "Missing bundled compiler: $compiler. Run scripts/acquire-toolchain.ps1 first."
}

$version = & $compiler '--version' 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Bundled compiler could not run."
}

$verificationRoot = Join-Path $toolchainRoot ("verify-script-" + [Guid]::NewGuid().ToString('N'))
$prefix = $toolchainRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $verificationRoot.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved verification path escaped toolchain root.'
}

try {
    [System.IO.Directory]::CreateDirectory($verificationRoot) | Out-Null
    $source = Join-Path $verificationRoot 'smoke.cpp'
    $executable = Join-Path $verificationRoot 'smoke.exe'
    [System.IO.File]::WriteAllText($source, "#include <optional>`n#include <iostream>`nint main(){std::optional<int> x=17;std::cout<<*x<<'\n';}`n", [System.Text.UTF8Encoding]::new($false))
    $compileArguments = @($source, '-std=gnu++17', '-O2', '-pipe', '-Wall', '-Wextra', '-o', $executable)
    & $compiler @compileArguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw 'Bundled compiler failed the GNU C++17 smoke test.'
    }
    $smokeOutput = & $executable
    if ($LASTEXITCODE -ne 0 -or ($smokeOutput -join '').Trim() -ne '17') {
        throw 'The compiled GNU C++17 smoke program returned unexpected output.'
    }
}
finally {
    if (Test-Path -LiteralPath $verificationRoot) {
        Remove-Item -LiteralPath $verificationRoot -Recurse -Force
    }
}

Write-Host ("Toolchain verified: " + [string]$version[0])
Write-Host ("testlib revision: " + [string]$manifest.testlib.revision)

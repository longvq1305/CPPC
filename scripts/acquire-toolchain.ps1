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
$downloadsRoot = Join-Path $toolchainRoot 'downloads'
[System.IO.Directory]::CreateDirectory($downloadsRoot) | Out-Null

function Get-SafeToolchainPath([string]$relativePath) {
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $toolchainRoot $relativePath))
    $prefix = $toolchainRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped toolchain root: $relativePath"
    }
    return $candidate
}

function Test-ExpectedHash([string]$path, [string]$expected) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.Equals($expected, [System.StringComparison]::OrdinalIgnoreCase)
}

function Download-Verified([string]$url, [string]$destination, [string]$sha256) {
    if (Test-ExpectedHash $destination $sha256) { return }
    $parent = Split-Path -Parent $destination
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        Invoke-WebRequest -Uri $url -OutFile $temporary -UseBasicParsing
        if (-not (Test-ExpectedHash $temporary $sha256)) {
            throw "Downloaded file checksum does not match: $url"
        }
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Force
        }
        [System.IO.File]::Move($temporary, $destination)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Copy-DirectoryTree([string]$sourceRoot, [string]$destinationRoot) {
    [System.IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
    foreach ($directory in [System.IO.Directory]::EnumerateDirectories($sourceRoot, '*', [System.IO.SearchOption]::AllDirectories)) {
        $relative = $directory.Substring($sourceRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
        [System.IO.Directory]::CreateDirectory((Join-Path $destinationRoot $relative)) | Out-Null
    }
    foreach ($file in [System.IO.Directory]::EnumerateFiles($sourceRoot, '*', [System.IO.SearchOption]::AllDirectories)) {
        $relative = $file.Substring($sourceRoot.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
        $destination = Join-Path $destinationRoot $relative
        [System.IO.File]::Copy($file, $destination, $false)
    }
}

$archivePath = Join-Path $downloadsRoot ([string]$manifest.compiler.archiveFileName)
Download-Verified ([string]$manifest.compiler.archiveUrl) $archivePath ([string]$manifest.compiler.sha256)

$compiler = Join-Path $toolchainRoot 'mingw64\bin\g++.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stagingRoot = Get-SafeToolchainPath ("staging-script-" + [Guid]::NewGuid().ToString('N'))
    $targetRoot = Get-SafeToolchainPath 'mingw64'
    $backupRoot = Get-SafeToolchainPath ("mingw64-backup-script-" + [Guid]::NewGuid().ToString('N'))
    try {
        [System.IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
        $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            $stagingPrefix = $stagingRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            foreach ($entry in $zip.Entries) {
                $relative = $entry.FullName.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
                $destination = [System.IO.Path]::GetFullPath((Join-Path $stagingRoot $relative))
                if (-not $destination.StartsWith($stagingPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Compiler archive contains an unsafe path: $($entry.FullName)"
                }
                if ([string]::IsNullOrEmpty($entry.Name)) {
                    [System.IO.Directory]::CreateDirectory($destination) | Out-Null
                    continue
                }
                [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
            }
        }
        finally { $zip.Dispose() }

        $stagedCompilerRoot = Join-Path $stagingRoot ([string]$manifest.compiler.archiveRoot)
        if (-not (Test-Path -LiteralPath (Join-Path $stagedCompilerRoot 'bin\g++.exe') -PathType Leaf)) {
            throw 'Compiler archive does not contain the expected mingw64/bin/g++.exe.'
        }
        if (Test-Path -LiteralPath $targetRoot) { [System.IO.Directory]::Move($targetRoot, $backupRoot) }
        try {
            try {
                [System.IO.Directory]::Move($stagedCompilerRoot, $targetRoot)
            }
            catch [System.IO.IOException] {
                if (Test-Path -LiteralPath $targetRoot) { Remove-Item -LiteralPath $targetRoot -Recurse -Force }
                Copy-DirectoryTree $stagedCompilerRoot $targetRoot
            }
        }
        catch {
            if (-not (Test-Path -LiteralPath $targetRoot) -and (Test-Path -LiteralPath $backupRoot)) {
                [System.IO.Directory]::Move($backupRoot, $targetRoot)
            }
            throw
        }
        if (Test-Path -LiteralPath $backupRoot) { Remove-Item -LiteralPath $backupRoot -Recurse -Force }
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
    }
}

foreach ($file in $manifest.testlib.files) {
    $destination = Get-SafeToolchainPath ([string]$file.relativePath)
    Download-Verified ([string]$file.url) $destination ([string]$file.sha256)
}

& (Join-Path $PSScriptRoot 'verify-toolchain.ps1') -RepositoryRoot $resolvedRoot

[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$SignParams = ""
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
Set-Location -LiteralPath $here
& (Join-Path $here 'Audit-Source.ps1')

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Version must be a three-part SemVer value, for example 0.1.0 or 1.0.0-beta.1."
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet -or -not (& $dotnet.Source --list-sdks)) {
    $userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $userDotnet)) {
        throw 'No .NET SDK was found. Install the .NET 8 SDK before publishing.'
    }
    $dotnetPath = $userDotnet
}
else {
    $dotnetPath = $dotnet.Source
}

$artifactRoot = Join-Path $here 'artifacts'
$publishDir = Join-Path $artifactRoot 'publish'
$releaseDir = Join-Path $artifactRoot 'releases'

$resolvedHere = [IO.Path]::GetFullPath($here).TrimEnd('\')
$resolvedArtifacts = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\')
if (-not $resolvedArtifacts.StartsWith($resolvedHere + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an artifact directory outside the repository: $resolvedArtifacts"
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

& $dotnetPath tool restore
if ($LASTEXITCODE -ne 0) { throw 'Failed to restore the Velopack command-line tool.' }

& $dotnetPath publish (Join-Path $here 'KpcLauncher.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    --artifacts-path (Join-Path $artifactRoot 'build') `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$publishedFiles = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'KpcLauncher.exe') {
    throw 'Publish must produce exactly one self-contained KpcLauncher.exe.'
}

$packArguments = @(
    'tool', 'run', 'vpk', 'pack',
    '--packId', 'KPCLauncher',
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'KpcLauncher.exe',
    '--packTitle', 'KPC Launcher',
    '--packAuthors', 'aloisakp',
    '--outputDir', $releaseDir,
    '--noPortable'
)

if (-not [string]::IsNullOrWhiteSpace($SignParams)) {
    $packArguments += @('--signParams', $SignParams)
}

& $dotnetPath @packArguments
if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }

Write-Host "Release $Version created in $releaseDir"

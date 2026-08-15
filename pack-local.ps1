# Packs DiffEngine into ./nugets under a version no feed will ever publish, so a consumer in
# another repo can reference the working tree rather than the last release.
#
# The version is fixed rather than stamped with the time, so the consumer's pin stays put across
# rebuilds. That only works because the cached copy is deleted first: NuGet caches by id and
# version, and would otherwise keep serving the package from the previous run forever.
#
#   ./pack-local.ps1                 pack as 20.0.0-local
#   ./pack-local.ps1 -Version 1.2.3  pack as 1.2.3
#
# Consuming it from Verify is two lines, both in src: a local <add> in nuget.config pointing at
# this folder, and the DiffEngine PackageVersion set to the same version.
[CmdletBinding()]
param(
    [string] $Version = '20.0.0-local'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$output = Join-Path $root 'nugets'

# The cached copy of a version already restored once, which is what makes a fixed version safe
$cached = Join-Path $env:USERPROFILE ".nuget\packages\diffengine\$Version"
if (Test-Path $cached)
{
    Write-Host "Removing cached $Version"
    Remove-Item $cached -Recurse -Force
}

$package = Join-Path $output "DiffEngine.$Version.nupkg"
if (Test-Path $package)
{
    Remove-Item $package -Force
}

# A build rather than a pack: ProjectDefaults sets GeneratePackageOnBuild for Release, and the
# viewer heads DiffEngine bundles are published by targets that only run on the way through.
Write-Host "Packing $Version"
dotnet build (Join-Path $root 'src\DiffEngine\DiffEngine.csproj') --configuration Release -p:Version=$Version
if ($LASTEXITCODE -ne 0)
{
    throw "Build failed with $LASTEXITCODE"
}

if (-not (Test-Path $package))
{
    throw "No package at $package"
}

Write-Host "Packed $package"
Write-Host 'Consumers should clear obj/ or restore with --no-cache if they had this version already.'

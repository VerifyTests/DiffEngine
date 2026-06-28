<#
.SYNOPSIS
    Collects code coverage for the DiffEngine and DiffEngineTray test projects.

.DESCRIPTION
    Uses the Microsoft.Testing.Extensions.CodeCoverage (dotnet-coverage) engine that is
    referenced by both test projects. Produces one Cobertura file per project under /coverage
    and prints a per-class summary for the two product assemblies.

    Notes:
      * Builds with -p:DebugType=portable because the coverage engine needs separate .pdb files
        (the repo default is embedded PDBs, which the engine does not read).
      * Passes coverage.config to override dotnet-coverage's default exclusion of strong-name
        signed assemblies (DiffEngine/DiffEngineTray are signed with key.snk).
      * Defaults to the Debug configuration so the #if DEBUG WinForms snapshot tests are included.
        CI runs Release, where those tests (OptionsForm/HotKeyControl/SolutionDirectoryFinder) are
        compiled out and therefore not covered.

.EXAMPLE
    pwsh src/coverage.ps1
    pwsh src/coverage.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$repo = Split-Path $src -Parent
$settings = Join-Path $src 'coverage.config'
$outDir = Join-Path $repo 'coverage'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$projects = @(
    @{ Name = 'DiffEngine.Tests';     Tfm = 'net10.0';         },
    @{ Name = 'DiffEngineTray.Tests'; Tfm = 'net10.0-windows'; }
)

foreach ($project in $projects)
{
    $projectDir = Join-Path $src $project.Name
    Write-Host "Building $($project.Name) ($Configuration, $($project.Tfm)) with portable PDBs..." -ForegroundColor Cyan
    dotnet build $projectDir --configuration $Configuration --framework $project.Tfm `
        -p:DebugType=portable -p:DebugSymbols=true | Out-Null

    $exe = Join-Path $projectDir "bin/$Configuration/$($project.Tfm)/$($project.Name).exe"
    Write-Host "Collecting coverage for $($project.Name)..." -ForegroundColor Cyan
    # --coverage-output takes a bare filename resolved against --results-directory (absolute paths are rejected).
    # Test failures return a non-zero exit code; coverage is still written, so don't abort.
    & $exe --coverage --coverage-settings $settings `
        --coverage-output-format cobertura --coverage-output "$($project.Name).cobertura.xml" --results-directory $outDir *> $null
}

function Show-Summary([string] $file)
{
    if (-not (Test-Path $file)) { return }
    [xml]$xml = Get-Content $file
    foreach ($package in $xml.coverage.packages.package)
    {
        $rows = foreach ($class in $package.classes.class)
        {
            $lines = @($class.lines.line)
            if ($lines.Count -eq 0) { continue }
            $covered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
            [pscustomobject]@{ Full = $class.name; Covered = $covered; Total = $lines.Count }
        }
        $byClass = $rows | Group-Object Full | ForEach-Object {
            $covered = ($_.Group | Measure-Object Covered -Sum).Sum
            $total = ($_.Group | Measure-Object Total -Sum).Sum
            # collapse compiler-generated nested names (<>c, <Method>d__n) onto the outer type
            $name = ($_.Name -replace '/.*$', '') -replace '\+.*$', ''
            [pscustomobject]@{ Class = ($name -replace '^.*\.', ''); Covered = $covered; Total = $total }
        } | Group-Object Class | ForEach-Object {
            $covered = ($_.Group | Measure-Object Covered -Sum).Sum
            $total = ($_.Group | Measure-Object Total -Sum).Sum
            [pscustomobject]@{
                Class   = $_.Name
                Covered = $covered
                Total   = $total
                Percent = if ($total) { [math]::Round(100.0 * $covered / $total, 1) } else { 0 }
            }
        }
        $lineRate = [double]$package.'line-rate'
        $branchRate = [double]$package.'branch-rate'
        Write-Host ""
        Write-Host ("{0}  —  line {1:P1}, branch {2:P1}" -f $package.name, $lineRate, $branchRate) -ForegroundColor Green
        $byClass | Sort-Object Percent, Total |
            Format-Table Class, Covered, Total, @{ n = 'Percent'; e = { '{0,5:N1}' -f $_.Percent } } -AutoSize |
            Out-String -Width 120 | Write-Host
    }
}

Write-Host "`n================ Coverage summary ================" -ForegroundColor Yellow
Get-ChildItem $outDir -Filter *.cobertura.xml | ForEach-Object { Show-Summary $_.FullName }
Write-Host "Cobertura files written to: $outDir" -ForegroundColor Yellow
Write-Host "For an HTML report: dotnet tool install -g dotnet-reportgenerator-globaltool; reportgenerator -reports:$outDir/*.cobertura.xml -targetdir:$outDir/html" -ForegroundColor DarkGray

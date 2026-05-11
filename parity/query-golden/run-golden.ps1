param(
    [string]$CorpusDir = "parity/query-golden",
    [string]$OutputDir = "parity/reports/query-golden",
    [switch]$Bless
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent | Split-Path -Parent
$TestProject = Join-Path $Root "BogDb.Tests/BogDb.Tests.csproj"

function Resolve-RepoPath([string]$PathValue) {
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return [System.IO.Path]::GetFullPath((Join-Path $Root $PathValue))
}

function Invoke-GoldenTests([string]$Filter) {
    $output = & dotnet test $TestProject `
        --filter $Filter `
        --logger "console;verbosity=normal" `
        --no-build 2>&1

    $output | ForEach-Object { Write-Host $_ }
    $script:LastTestOutput = ($output -join [Environment]::NewLine)
    return $LASTEXITCODE
}

$ResolvedCorpusDir = Resolve-RepoPath $CorpusDir
$ResolvedOutputDir = Resolve-RepoPath $OutputDir

$env:BOGDB_GOLDEN_CORPUS_DIR = $ResolvedCorpusDir
$env:BOGDB_GOLDEN_REPORT_DIR = $ResolvedOutputDir

New-Item -ItemType Directory -Force $ResolvedOutputDir | Out-Null

if ($Bless) {
    Write-Host "[golden] Blessing all golden files..."
    $exitCode = Invoke-GoldenTests "FullyQualifiedName~GoldenBlessCommand"
    if ($LastTestOutput -match "No test matches the given testcase filter") {
        Write-Host "[golden] ERROR: bless filter matched zero tests."
        exit 1
    }

    Write-Host "[golden] Bless complete. Review $ResolvedCorpusDir/golden/ before committing."
    exit $exitCode
}

Write-Host "[golden] Running golden diff tests..."
$exitCode = Invoke-GoldenTests "FullyQualifiedName~GoldenDiffTests"

if ($LastTestOutput -match "No test matches the given testcase filter") {
    Write-Host "[golden] ERROR: diff filter matched zero tests."
    exit 1
}

if ($exitCode -eq 0) {
    Write-Host "[golden] All golden diffs PASS."
}
else {
    Write-Host "[golden] Golden diffs FAILED. See output above for per-query diff details."
    Write-Host "[golden] To re-bless intentional changes: ./run-golden.ps1 -Bless"
}

exit $exitCode

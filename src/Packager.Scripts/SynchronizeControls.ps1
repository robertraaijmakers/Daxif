# SynchronizeControls - Build and sync PCF controls solution to Development environment
# This script builds and deploys the bol_OnePRMControls solution using XrmPackager

param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev"
)

Import-Module $PSScriptRoot\_InitXrmPackager.ps1 -ArgumentList $EnvironmentName -force

Write-Host "Syncing PCF Controls Solution..." -ForegroundColor Cyan
Write-Host "Solution: bol_OnePRMControls" -ForegroundColor Gray
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray
Write-Host ""

try {
    # Path to solution zip (use cross-platform path joining)
    $solutionZip = Join-Path $PSScriptRoot ".." "controls" "bol_OnePRMControls" "bin" "Debug" "bol_OnePRMControls.zip"
    $solutionZip = Resolve-Path $solutionZip -ErrorAction SilentlyContinue

    if (-not $solutionZip -or -not (Test-Path $solutionZip)) {
        throw "Solution zip not found. Did you run 'npm run build' first?"
    }

    Write-Host "Found solution package: $solutionZip" -ForegroundColor Green
    Write-Host ""

    # Import solution using XrmPackager
    Write-Host "Importing solution to environment..." -ForegroundColor Yellow
    Write-Host "Note: This may take several minutes." -ForegroundColor Gray
    Write-Host ""

    Invoke-XrmPackager -Arguments @(
        "solution",
        "import",
        "--zip", $solutionZip.Path,
        "--publish",
        "--overwrite"
    )

    Write-Host ""
    Write-Host "✓ Solution imported successfully!" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "❌ Error: $_" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "  $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
    exit 1
}

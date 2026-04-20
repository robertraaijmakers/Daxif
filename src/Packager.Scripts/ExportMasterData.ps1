##
# ExportMasterData - Export master data from Dataverse to JSON files
# Uses XrmPackager CLI wrapper
##

param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev",
    [string]$ConfigFile = "$PSScriptRoot/_Config.ps1",
    [Parameter(Mandatory=$true)]
    [string]$SchemaPath,
    [Parameter(Mandatory=$true)]
    [string]$DataFolder
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ConfigFile)) {
    throw "Configuration file not found: $ConfigFile"
}

$config = & $ConfigFile
$envConfig = $config.Environments[$EnvironmentName]

if (-not $envConfig) {
    $availableEnvironments = @($config.Environments.Keys) -join ", "
    throw "Environment '$EnvironmentName' not found. Available environments: $availableEnvironments"
}

$initScript = Join-Path $PSScriptRoot "_InitXrmPackager.ps1"
$config = Import-Module $initScript -ArgumentList $envConfig -Force

Write-Host "Exporting Master Data..." -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray
Write-Host "Schema: $SchemaPath" -ForegroundColor Gray
Write-Host "Data folder: $DataFolder" -ForegroundColor Gray

# Verify schema file exists
if (-not (Test-Path $SchemaPath)) {
    Write-Host "  Schema file not found: $SchemaPath" -ForegroundColor Red
    Read-Host -Prompt "Press Enter to exit"
    exit 1
}

# Ensure output folder exists
if (-not (Test-Path $DataFolder)) {
    New-Item -ItemType Directory -Path $DataFolder -Force | Out-Null
    Write-Host "  Created data folder: $DataFolder" -ForegroundColor Gray
}

try {
    $xrmPackagerArguments = @(
        "masterdata",
        "export",
        "--schema", $SchemaPath,
        "--folder", $DataFolder
    )

    Invoke-XrmPackager -Arguments $xrmPackagerArguments

    Write-Host "Master data exported successfully!" -ForegroundColor Green
} catch {
    Write-Host "Error exporting master data:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "  $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
}

Read-Host -Prompt "Press Enter to exit"

##
# ImportMasterData - Import master data from JSON files into Dataverse
# Uses XrmPackager CLI wrapper
##

param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev",
    [string]$ConfigFile = "$PSScriptRoot/_Config.ps1",
    [Parameter(Mandatory=$true)]
    [string]$SchemaPath,
    [Parameter(Mandatory=$true)]
    [string]$DataFolder,
    [switch]$DryRun,
    [switch]$LogChanges
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

Write-Host "Importing Master Data..." -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray
Write-Host "Schema: $SchemaPath" -ForegroundColor Gray
Write-Host "Data folder: $DataFolder" -ForegroundColor Gray
if ($DryRun) { Write-Host "Mode: DRY RUN (no changes will be written)" -ForegroundColor Yellow }
if ($LogChanges) { Write-Host "Mode: LOG CHANGES (changed fields will be reported)" -ForegroundColor Yellow }

# Verify schema file exists
if (-not (Test-Path $SchemaPath)) {
    Write-Host "  Schema file not found: $SchemaPath" -ForegroundColor Red
    Read-Host -Prompt "Press Enter to exit"
    exit 1
}

# Verify data folder exists
if (-not (Test-Path $DataFolder)) {
    Write-Host "  Data folder not found: $DataFolder" -ForegroundColor Red
    Write-Host "  Run ExportMasterData.ps1 first to populate the data folder." -ForegroundColor Yellow
    Read-Host -Prompt "Press Enter to exit"
    exit 1
}

try {
    $xrmPackagerArguments = @(
        "masterdata",
        "import",
        "--schema", $SchemaPath,
        "--folder", $DataFolder
    )
    if ($DryRun) { $xrmPackagerArguments += "--dry-run" }
    if ($LogChanges) { $xrmPackagerArguments += "--log-changes" }

    Invoke-XrmPackager -Arguments $xrmPackagerArguments

    Write-Host "Master data imported successfully!" -ForegroundColor Green
} catch {
    Write-Host "Error importing master data:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "  $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
}

Read-Host -Prompt "Press Enter to exit"

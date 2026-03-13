##
# WebResourceSyncDev - Sync web resources to Development environment
# Uses XrmPackager CLI wrapper
##

param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev",
    [switch]$DryRun,
    [switch]$NoPublish,
    [switch]$NoDelete
)

$config = Import-Module $PSScriptRoot\_InitXrmPackager.ps1 -ArgumentList $EnvironmentName -force

Write-Host "Syncing Web Resources..." -ForegroundColor Cyan
Write-Host "Solution: $($config.SolutionInfoWebresources.name)" -ForegroundColor Gray
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray

# Verify web resource folder exists
$webResourceFolder = $config.Path.webResourceProject
if (-not $webResourceFolder) {
    Write-Host "  Web resource folder not configured in _Config.ps1" -ForegroundColor Red
    Write-Host "  Please set Path.webResourceProject in the configuration" -ForegroundColor Yellow
    Read-Host -Prompt "Press Enter to exit"
    exit 1
}

if (-not (Test-Path $webResourceFolder)) {
    Write-Host "  Web resource folder not found: $webResourceFolder" -ForegroundColor Red
    Write-Host "  Please verify the path in _Config.ps1" -ForegroundColor Yellow
    Read-Host -Prompt "Press Enter to exit"
    exit 1
}

Write-Host "  Folder: $webResourceFolder" -ForegroundColor Gray

try {
    # Call XrmPackager CLI to sync web resources
    $xrmPackagerArguments = @(
        "webresource",
        "sync",
        "--folder", $webResourceFolder,
        "--solution", $config.SolutionInfoWebresources.name
    )

    if ($DryRun) {
        $xrmPackagerArguments += "--dry-run"
    }

    if ($NoPublish) {
        $xrmPackagerArguments += "--no-publish"
    }

    if ($NoDelete) {
        $xrmPackagerArguments += "--no-delete"
    }

    Invoke-XrmPackager -Arguments $xrmPackagerArguments

    Write-Host "Web resources synced successfully!" -ForegroundColor Green
} catch {
    Write-Host "Error syncing web resources:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "  $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
}

Read-Host -Prompt "Press Enter to exit"

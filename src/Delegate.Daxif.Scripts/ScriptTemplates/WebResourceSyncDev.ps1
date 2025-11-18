##
# WebResourceSyncDev - Sync web resources to Development environment
# Uses Daxif CLI wrapper (fixes assembly loading issues)
##

$config = Import-Module $PSScriptRoot\_InitDaxif.ps1 -force

Write-Host "`nSyncing Web Resources..." -ForegroundColor Cyan
Write-Host "Solution: $($config.SolutionInfoWebresources.name)" -ForegroundColor Gray

# Verify web resource folder exists
$webResourceFolder = $config.Path.webResourceProject
if (-not $webResourceFolder) {
    Write-Host "`n  ✗ Web resource folder not configured in _Config.ps1" -ForegroundColor Red
    Write-Host "  Please set Path.webResourceProject in the configuration" -ForegroundColor Yellow
    Read-Host -Prompt "Press Enter to exit"
    exit 1
}

if (-not (Test-Path $webResourceFolder)) {
    Write-Host "`n  ✗ Web resource folder not found: $webResourceFolder" -ForegroundColor Red
    Write-Host "  Please verify the path in _Config.ps1" -ForegroundColor Yellow
    Read-Host -Prompt "Press Enter to exit"
    exit 1
}

Write-Host "  Folder: $webResourceFolder" -ForegroundColor Gray

try {
    # Call Daxif CLI to sync web resources
    Invoke-Daxif -Arguments @(
        "webresource",
        "sync",
        "--folder", $webResourceFolder,
        "--solution", $config.SolutionInfoWebresources.name
    )
    
    Write-Host "`n✓ Web resources synced successfully!" -ForegroundColor Green
} catch {
    Write-Host "`n✗ Error syncing web resources:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "  $($_.Exception.InnerException.Message)" -ForegroundColor Red
    }
}

Read-Host -Prompt "Press Enter to exit"


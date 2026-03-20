##
# TestConnection - Verify Dataverse connection works
# Uses XrmPackager CLI wrapper
##

param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev"
)

try {
    Import-Module $PSScriptRoot\_InitXrmPackager.ps1 -ArgumentList $EnvironmentName -force

    Write-Host ""

    # Test connection using XrmPackager CLI
    Invoke-XrmPackager -Arguments @("test-connection")

    Write-Host "XrmPackager is ready to use!" -ForegroundColor Green
    Write-Host "You can now run:" -ForegroundColor White
    Write-Host "  - SynchronizePlugins.ps1 to sync plugins" -ForegroundColor Gray
    Write-Host "  - SynchronizeWebresources.ps1 to sync web resources" -ForegroundColor Gray

} catch {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Dataverse Connection Test" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Connection Status: FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error Details:"
    Write-Host "  $($_.Exception.Message)"
    Write-Host ""
    Write-Host "Troubleshooting:"
    Write-Host "  1. Verify your credentials in _Config.ps1 or environment variables"
    Write-Host "  2. Check that the Dataverse URL is correct (base URL only, no path)"
    Write-Host "  3. Ensure you have network connectivity to Dataverse"
    Write-Host "  4. If using OAuth, try deleting the token cache and re-authenticating"
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
}

Write-Host ""
Read-Host -Prompt "Press Enter to exit"

##
# TestConnection - Verify Dataverse connection works
# Uses Daxif CLI wrapper (fixes assembly loading issues)
##

try {
    $config = Import-Module $PSScriptRoot\_InitDaxif.ps1 -force
    
    Write-Host ""
    
    # Test connection using Daxif CLI
    Invoke-Daxif -Arguments @("test-connection")
    
    Write-Host "`n✓ Daxif is ready to use!" -ForegroundColor Green
    Write-Host "`nYou can now run:" -ForegroundColor White
    Write-Host "  - PluginSyncDev.ps1 to sync plugins" -ForegroundColor Gray
    Write-Host "  - WebResourceSyncDev.ps1 to sync web resources" -ForegroundColor Gray
    
} catch {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Dataverse Connection Test" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "✗ Connection Status: FAILED" -ForegroundColor Red
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
    Write-Host "========================================`n" -ForegroundColor Cyan
}

Write-Host ""
Read-Host -Prompt "Press Enter to exit"


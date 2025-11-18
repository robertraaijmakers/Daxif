##
# PluginSyncDev - Sync plugins to Development environment
# Uses Daxif CLI wrapper (fixes assembly loading issues)
##

$config = Import-Module $PSScriptRoot\_InitDaxif.ps1 -force

Write-Host "`nSyncing Plugins..." -ForegroundColor Cyan
Write-Host "Solution: $($config.SolutionInfoPlugins.name)" -ForegroundColor Gray

$config.Plugins.projects | ForEach-Object {
    $projectName = $_
    Write-Host "`nProcessing project: $projectName" -ForegroundColor Yellow
    
    # Try to find plugin DLL in various configurations
    $possiblePaths = @(
        "$($config.Path.solutionRoot)\$projectName\bin\Debug\net462\$projectName.dll",
        "$($config.Path.solutionRoot)\$projectName\bin\Release\net462\$projectName.dll",
        "$($config.Path.solutionRoot)\$projectName\bin\Debug\net8.0\$projectName.dll",
        "$($config.Path.solutionRoot)\$projectName\bin\Release\net8.0\$projectName.dll"
    )
    
    $pluginDll = $null
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $pluginDll = (Get-Item $path).FullName
            break
        }
    }
    
    if (-not $pluginDll) {
        Write-Host "  ✗ Plugin DLL not found. Tried:" -ForegroundColor Red
        $possiblePaths | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
        Write-Host "  Build the project first!" -ForegroundColor Red
        continue
    }
    
    Write-Host "  Assembly: $pluginDll" -ForegroundColor Gray
    
    try {
        # Call Daxif CLI to sync plugins
        Invoke-Daxif -Arguments @(
            "plugin",
            "sync",
            "--assembly", $pluginDll,
            "--solution", $config.SolutionInfoPlugins.name
        )
        
        Write-Host "  ✓ Sync completed" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ Sync failed: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.InnerException) {
            Write-Host "  $($_.Exception.InnerException.Message)" -ForegroundColor Red
        }
    }
}

Write-Host "`n✓ All plugins processed!" -ForegroundColor Green
Read-Host -Prompt "Press Enter to exit"

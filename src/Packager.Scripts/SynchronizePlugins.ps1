##
# PluginSyncDev - Sync plugins to Development environment
# Uses XrmPackager CLI wrapper
##

param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev",
    [switch]$DryRun
)

$config = Import-Module $PSScriptRoot\_InitXrmPackager.ps1 -ArgumentList $EnvironmentName -force

$transcriptStarted = $false
if ($DryRun) {
    $logsPath = Join-Path $PSScriptRoot "logs"
    if (-not (Test-Path $logsPath)) {
        New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $logFile = Join-Path $logsPath "pluginsync_dryrun_$timestamp.log"
    Start-Transcript -Path $logFile -Force | Out-Null
    $transcriptStarted = $true
    Write-Host "Dry-run log file: $logFile" -ForegroundColor Gray
}

Write-Host "Syncing Plugins..." -ForegroundColor Cyan
Write-Host "Solution: $($config.SolutionInfoPlugins.name)" -ForegroundColor Gray
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray

$pluginDlls = @()

$config.Plugins.projects | ForEach-Object {
    $projectName = $_
    Write-Host "Processing project: $projectName" -ForegroundColor Yellow

    # Try to find plugin DLL in various configurations
    $possiblePaths = @(
        "$($config.Path.solutionRoot)\plugins\$projectName\bin\Debug\net462\$projectName.dll",
        "$($config.Path.solutionRoot)\plugins\$projectName\bin\Release\net462\$projectName.dll",
        "$($config.Path.solutionRoot)\plugins\$projectName\bin\Debug\net8.0\$projectName.dll",
        "$($config.Path.solutionRoot)\plugins\$projectName\bin\Release\net8.0\$projectName.dll"
    )

    $pluginDll = $null
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $pluginDll = (Get-Item $path).FullName
            break
        }
    }

    if (-not $pluginDll) {
        Write-Host "  Plugin DLL not found. Tried:" -ForegroundColor Red
        $possiblePaths | ForEach-Object { Write-Host "    $_" -ForegroundColor Gray }
        Write-Host "  Build the project first!" -ForegroundColor Red
        continue
    }

    Write-Host "  Assembly: $pluginDll" -ForegroundColor Gray

    $pluginDlls += $pluginDll
}

if ($pluginDlls.Count -eq 0) {
    Write-Host "No plugin assemblies found. Nothing to synchronize." -ForegroundColor Red
} else {
    try {
        $xrmPackagerArguments = @(
            "plugin",
            "sync"
        )

        foreach ($pluginDll in $pluginDlls) {
            $xrmPackagerArguments += @("--assembly", $pluginDll)
        }

        $xrmPackagerArguments += @(
            "--solution", $config.SolutionInfoPlugins.name
        )

        if ($DryRun) {
            $xrmPackagerArguments += "--dry-run"
            Write-Host "Dry run enabled" -ForegroundColor Yellow
        }

        Invoke-XrmPackager -Arguments $xrmPackagerArguments
        Write-Host "All plugins synchronized." -ForegroundColor Green
    } catch {
        Write-Host "Plugin synchronization failed: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.InnerException) {
            Write-Host "  $($_.Exception.InnerException.Message)" -ForegroundColor Red
        }
    }
}

if ($transcriptStarted) {
    Stop-Transcript | Out-Null
}

Read-Host -Prompt "Press Enter to exit"

# Build script for Daxif (Windows)

param(
    [string]$Configuration = "Release"
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Building Daxif" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Navigate to src directory
Push-Location "$PSScriptRoot/src"

try {
    Write-Host "Cleaning previous builds..." -ForegroundColor Gray
    dotnet clean -c $Configuration > $null 2>&1

    Write-Host "Restoring packages..." -ForegroundColor Gray
    dotnet restore

    Write-Host ""
    Write-Host "Building Delegate.Daxif..." -ForegroundColor Gray
    dotnet build Delegate.Daxif/Delegate.Daxif.fsproj -c $Configuration

    Write-Host ""
    Write-Host "Building Delegate.Daxif.Console..." -ForegroundColor Gray
    dotnet build Delegate.Daxif.Console/Delegate.Daxif.Console.fsproj -c $Configuration

    Write-Host ""
    Write-Host "Building Delegate.Daxif.Scripts..." -ForegroundColor Gray
    dotnet build Delegate.Daxif.Scripts/Delegate.Daxif.Scripts.fsproj -c $Configuration

    Write-Host ""
    Write-Host "✓ Build completed successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Outputs:" -ForegroundColor White
    Write-Host "  - Library: src/Delegate.Daxif/bin/$Configuration/net10.0/Delegate.Daxif.dll"
    Write-Host "  - CLI Tool: src/Delegate.Daxif.Console/bin/$Configuration/net10.0/daxif.dll"
    Write-Host "  - Scripts: src/Delegate.Daxif.Scripts/ScriptTemplates/Daxif/"
    Write-Host ""
    Write-Host "Quick test:" -ForegroundColor White
    Write-Host "  cd src/Delegate.Daxif.Console/bin/$Configuration/net10.0"
    Write-Host "  dotnet daxif.dll help"
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Cyan
}
catch {
    Write-Host ""
    Write-Host "✗ Build failed!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}

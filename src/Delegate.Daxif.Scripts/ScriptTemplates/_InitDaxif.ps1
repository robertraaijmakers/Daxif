# Initialize Daxif Configuration
# This script sets up environment variables for the Daxif CLI wrapper

$config = Import-Module "$PSScriptRoot\_Config.ps1" -force

# Verify Daxif CLI exists
$daxifCli = Join-Path $config.Path.toolsFolder "daxif.dll"
if (-not (Test-Path $daxifCli)) {
    Write-Host "`n✗ Daxif CLI not found!" -ForegroundColor Red
    Write-Host "  Expected location: $daxifCli" -ForegroundColor Yellow
    Write-Host "`nPlease build the solution first:" -ForegroundColor Yellow
    Write-Host "  cd src" -ForegroundColor Gray
    Write-Host "  dotnet build Delegate.Daxif.sln -c Release" -ForegroundColor Gray
    Write-Host "  dotnet build Delegate.Daxif.Console/Delegate.Daxif.Console.fsproj -c Release" -ForegroundColor Gray
    Write-Host "`nThen copy daxif.dll to the Daxif folder." -ForegroundColor Yellow
    throw "Daxif CLI not found"
}

# Helper function to set environment variables from config
function Set-DataverseEnvironmentVariables {
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Environment
    )
    
    $env:DATAVERSE_URL = $Environment.url
    $env:DATAVERSE_AUTH_TYPE = $Environment.authType
    
    switch ($Environment.authType) {
        "OAuth" {
            $env:DATAVERSE_APP_ID = $Environment.appId
            $env:DATAVERSE_REDIRECT_URI = $Environment.redirectUri
            
            if ($Environment.username) {
                $env:DATAVERSE_USERNAME = $Environment.username
            }
            if ($Environment.password) {
                $env:DATAVERSE_PASSWORD = $Environment.password
            }
            if ($Environment.tokenCacheStorePath) {
                # Ensure token cache directory exists
                $tokenCacheDir = Split-Path $Environment.tokenCacheStorePath -Parent
                if (-not (Test-Path $tokenCacheDir)) {
                    New-Item -ItemType Directory -Path $tokenCacheDir -Force | Out-Null
                }
                $env:DATAVERSE_TOKEN_CACHE = $Environment.tokenCacheStorePath
            }
        }
        "ClientSecret" {
            if (-not $Environment.clientId -or -not $Environment.clientSecret) {
                throw "ClientId and ClientSecret are required for ClientSecret authentication"
            }
            $env:DATAVERSE_CLIENT_ID = $Environment.clientId
            $env:DATAVERSE_CLIENT_SECRET = $Environment.clientSecret
        }
        "Certificate" {
            if (-not $Environment.clientId -or -not $Environment.thumbprint) {
                throw "ClientId and Thumbprint are required for Certificate authentication"
            }
            $env:DATAVERSE_CLIENT_ID = $Environment.clientId
            $env:DATAVERSE_THUMBPRINT = $Environment.thumbprint
        }
        default {
            throw "Unknown authentication type: $($Environment.authType)"
        }
    }
}

# Helper function to run Daxif CLI
function Invoke-Daxif {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$Arguments
    )
    
    $daxifCli = Join-Path $config.Path.toolsFolder "daxif.dll"
    
    Write-Host "Executing: dotnet $daxifCli $($Arguments -join ' ')" -ForegroundColor Gray
    Write-Host "" # Empty line for readability
    
    # Run the command and stream output in real-time
    & dotnet $daxifCli @Arguments
    
    if ($LASTEXITCODE -ne 0) {
        throw "Daxif command failed with exit code $LASTEXITCODE"
    }
}

# Get the environment configuration (default to Dev)
$environment = $config.Environments.Dev

# Set environment variables
Set-DataverseEnvironmentVariables -Environment $environment

Write-Host "✓ Daxif environment configured" -ForegroundColor Green
Write-Host "  Environment: $($environment.name)" -ForegroundColor Gray
Write-Host "  URL: $($environment.url)" -ForegroundColor Gray
Write-Host "  Auth Type: $($environment.authType)" -ForegroundColor Gray

return $config

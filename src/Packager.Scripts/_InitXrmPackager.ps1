# Initialize XrmPackager configuration
# This script sets up environment variables for the XrmPackager CLI wrapper

param(
    [object]$EnvironmentInput = "Dev"
)

$config = Import-Module "$PSScriptRoot\_Config.ps1" -force

# Verify XrmPackager CLI exists
$xrmPackagerCli = Join-Path $config.Path.toolsFolder "xrmpackager.dll"
if (-not (Test-Path $xrmPackagerCli)) {
    Write-Host "XrmPackager CLI not found!" -ForegroundColor Red
    Write-Host "  Expected location: $xrmPackagerCli" -ForegroundColor Yellow
    Write-Host "Please build the solution first:" -ForegroundColor Yellow
    Write-Host "  ./ExecutionScripts/BuildXrmPackager.ps1" -ForegroundColor Gray
    throw "XrmPackager CLI not found"
}

# Helper function to set environment variables from config
function Set-DataverseEnvironmentVariables {
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$EnvironmentConfig
    )

    $loginHintCachePath = Join-Path $PSScriptRoot "login-hints.json"

    $env:DATAVERSE_URL = $EnvironmentConfig.url
    $env:DATAVERSE_AUTH_TYPE = $EnvironmentConfig.authType
    $env:DATAVERSE_LOGIN_HINT_CACHE_PATH = $loginHintCachePath

    switch ($EnvironmentConfig.authType) {
        "OAuth" {
            function Get-CachedLoginHint {
                param([Parameter(Mandatory=$true)][string]$Url)

                try {
                    $cachePath = $env:DATAVERSE_LOGIN_HINT_CACHE_PATH
                    if (-not (Test-Path $cachePath)) {
                        return $null
                    }

                    $cache = Get-Content $cachePath -Raw | ConvertFrom-Json
                    if (-not $cache -or -not $cache.Hints) {
                        return $null
                    }

                    $key = ([uri]$Url).GetLeftPart([System.UriPartial]::Authority).TrimEnd('/').ToLowerInvariant()
                    return $cache.Hints.$key
                }
                catch {
                    return $null
                }
            }

            $env:DATAVERSE_APP_ID = $EnvironmentConfig.appId
            $env:DATAVERSE_REDIRECT_URI = $EnvironmentConfig.redirectUri

            Remove-Item Env:DATAVERSE_LOGIN_HINT -ErrorAction SilentlyContinue

            if ($EnvironmentConfig.username) {
                $env:DATAVERSE_USERNAME = $EnvironmentConfig.username
            }
            if ($EnvironmentConfig.password) {
                $env:DATAVERSE_PASSWORD = $EnvironmentConfig.password
            }
            if ($EnvironmentConfig.tokenCacheStorePath) {
                # Ensure token cache directory exists
                $tokenCacheDir = Split-Path $EnvironmentConfig.tokenCacheStorePath -Parent
                if (-not (Test-Path $tokenCacheDir)) {
                    New-Item -ItemType Directory -Path $tokenCacheDir -Force | Out-Null
                }
                $env:DATAVERSE_TOKEN_CACHE = $EnvironmentConfig.tokenCacheStorePath
            }
            if ($EnvironmentConfig.loginPrompt) {
                $env:DATAVERSE_LOGIN_PROMPT = $EnvironmentConfig.loginPrompt
            }
            if ($EnvironmentConfig.loginHint) {
                $env:DATAVERSE_LOGIN_HINT = $EnvironmentConfig.loginHint
            } else {
                $cachedLoginHint = Get-CachedLoginHint -Url $EnvironmentConfig.url
                if ($cachedLoginHint) {
                    $env:DATAVERSE_LOGIN_HINT = $cachedLoginHint
                    Write-Host "  Using cached login hint: $cachedLoginHint" -ForegroundColor Gray
                }
            }
        }
        "ClientSecret" {
            if (-not $EnvironmentConfig.clientId -or -not $EnvironmentConfig.clientSecret) {
                throw "ClientId and ClientSecret are required for ClientSecret authentication"
            }
            $env:DATAVERSE_CLIENT_ID = $EnvironmentConfig.clientId
            $env:DATAVERSE_CLIENT_SECRET = $EnvironmentConfig.clientSecret
        }
        "Certificate" {
            if (-not $EnvironmentConfig.clientId -or -not $EnvironmentConfig.thumbprint) {
                throw "ClientId and Thumbprint are required for Certificate authentication"
            }
            $env:DATAVERSE_CLIENT_ID = $EnvironmentConfig.clientId
            $env:DATAVERSE_THUMBPRINT = $EnvironmentConfig.thumbprint
        }
        default {
            throw "Unknown authentication type: $($EnvironmentConfig.authType)"
        }
    }
}

# Helper function to run XrmPackager CLI
function Invoke-XrmPackager {
    param(
        [Parameter(Mandatory=$true)]
        [string[]]$Arguments
    )

    $xrmPackagerCli = Join-Path $config.Path.toolsFolder "xrmpackager.dll"

    Write-Host "Executing: dotnet $xrmPackagerCli $($Arguments -join ' ')" -ForegroundColor Gray
    Write-Host "" # Empty line for readability

    # Run the command and stream output in real-time
    & dotnet $xrmPackagerCli @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "XrmPackager command failed with exit code $LASTEXITCODE"
    }
}

# Resolve the environment configuration
$selectedEnvironment = $null

if ($EnvironmentInput -is [hashtable]) {
    $selectedEnvironment = $EnvironmentInput
} else {
    $environmentKey = if ([string]::IsNullOrWhiteSpace([string]$EnvironmentInput)) { "Dev" } else { [string]$EnvironmentInput }
    $selectedEnvironment = $config.Environments[$environmentKey]

    if (-not $selectedEnvironment) {
        $availableEnvironments = @($config.Environments.Keys) -join ", "
        throw "Unknown environment '$environmentKey'. Available environments: $availableEnvironments"
    }
}

# Set environment variables
Set-DataverseEnvironmentVariables -EnvironmentConfig $selectedEnvironment

Write-Host "  XrmPackager environment configured" -ForegroundColor Green
Write-Host "  Environment: $($selectedEnvironment.name)" -ForegroundColor Gray
Write-Host "  URL: $($selectedEnvironment.url)" -ForegroundColor Gray
Write-Host "  Auth Type: $($selectedEnvironment.authType)" -ForegroundColor Gray
Write-Host "  Login hint cache: $env:DATAVERSE_LOGIN_HINT_CACHE_PATH" -ForegroundColor Gray

return $config

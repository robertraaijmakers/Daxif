param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev",
    [string]$ConfigFile = "$PSScriptRoot/_Config.ps1",
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$AdditionalArguments
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

if (-not $config.Path.kotlinOutput) {
    throw "Path.kotlinOutput is not configured in _Config.ps1"
}

$config = Import-Module $PSScriptRoot\_InitXrmPackager.ps1 -ArgumentList $envConfig -Force

$xrmPackagerArguments = @(
    "kotlin",
    "generate",
    "--out", $config.Path.kotlinOutput
)

if ($config.Kotlin.basePackage) {
    $xrmPackagerArguments += @("--package", $config.Kotlin.basePackage)
}

if ($config.Kotlin.entities -and $config.Kotlin.entities.Count -gt 0) {
    $entitiesList = $config.Kotlin.entities -join ","
    $xrmPackagerArguments += @("--entities", $entitiesList)
}

if ($AdditionalArguments) {
    $xrmPackagerArguments += $AdditionalArguments
}

Write-Host "Generating Kotlin entity classes..." -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray
Write-Host "Output: $($config.Path.kotlinOutput)" -ForegroundColor Gray
Write-Host "Package: $($config.Kotlin.basePackage)" -ForegroundColor Gray

Invoke-XrmPackager -Arguments $xrmPackagerArguments

Write-Host "Kotlin entity generation completed." -ForegroundColor Green

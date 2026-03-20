param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev",
    [string]$ConfigFile = "$PSScriptRoot/_Config.ps1",
    [switch]$MultiFile,
    [switch]$OneFile,
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

if (-not $config.Path.typescriptOutput) {
    throw "Path.typescriptOutput is not configured in _Config.ps1"
}

$config = Import-Module $PSScriptRoot\_InitXrmPackager.ps1 -ArgumentList $envConfig -Force

$xrmPackagerArguments = @(
    "xrmdt",
    "generate",
    "--out", $config.Path.typescriptOutput
)

if ($config.WebResources.entities -and $config.WebResources.entities.Count -gt 0) {
    $entitiesList = $config.WebResources.entities -join ","
    $xrmPackagerArguments += @("--entities", $entitiesList)
}

if ($config.SolutionInfoWebresources.name) {
    $xrmPackagerArguments += @("--solution", $config.SolutionInfoWebresources.name)
}

if ($MultiFile) {
    $xrmPackagerArguments += "--multi-file"
} elseif ($OneFile) {
    $xrmPackagerArguments += "--one-file"
}

if ($AdditionalArguments) {
    $xrmPackagerArguments += $AdditionalArguments
}

Write-Host "Generating TypeScript definitions..." -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray
Write-Host "Output: $($config.Path.typescriptOutput)" -ForegroundColor Gray

Invoke-XrmPackager -Arguments $xrmPackagerArguments

Write-Host "TypeScript definitions generation completed." -ForegroundColor Green

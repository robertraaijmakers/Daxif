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

if (-not $config.Path.entityFolder) {
    throw "Path.entityFolder is not configured in _Config.ps1"
}

$config = Import-Module $PSScriptRoot\_InitXrmPackager.ps1 -ArgumentList $envConfig -Force

$contextOutputPath = $config.Path.entityFolder
if (-not $MultiFile) {
    $contextOutputPath = Join-Path $config.Path.entityFolder "XrmContext.cs"
}

$xrmPackagerArguments = @(
    "context",
    "generate",
    "--out", $contextOutputPath
)

if ($config.Plugins.entityNamespace) {
    $xrmPackagerArguments += @("--namespace", $config.Plugins.entityNamespace)
}

if ($config.Plugins.entities -and $config.Plugins.entities.Count -gt 0) {
    $entitiesList = $config.Plugins.entities -join ","
    $xrmPackagerArguments += @("--entities", $entitiesList)
}

if ($config.SolutionInfoPlugins.name) {
    $xrmPackagerArguments += @("--solution", $config.SolutionInfoPlugins.name)
}

if ($MultiFile) {
    $xrmPackagerArguments += "--multi-file"
} elseif ($OneFile) {
    $xrmPackagerArguments += "--one-file"
}

if ($AdditionalArguments) {
    $xrmPackagerArguments += $AdditionalArguments
}

Write-Host "Generating Xrm Context..." -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray
Write-Host "Output: $contextOutputPath" -ForegroundColor Gray

Invoke-XrmPackager -Arguments $xrmPackagerArguments

Write-Host "Xrm Context generation completed." -ForegroundColor Green

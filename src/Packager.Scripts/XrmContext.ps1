param(
    [Alias("Environment")]
    [string]$EnvironmentName = "Dev",
    [string]$ConfigFile = "$PSScriptRoot/_Config.ps1",
    [switch]$OneFile,
    [switch]$Format,
    [switch]$ConsolidateHelpers,
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

$initScript = Join-Path $PSScriptRoot "_InitXrmPackager.ps1"
$config = Import-Module $initScript -ArgumentList $envConfig -Force

$normalizedEntityFolder = [IO.Path]::GetFullPath(
    ($config.Path.entityFolder -replace "[\\/]", [IO.Path]::DirectorySeparatorChar)
)

$contextOutputPath = $normalizedEntityFolder
if ($OneFile) {
    $contextOutputPath = Join-Path $normalizedEntityFolder "XrmContext.cs"
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

# Default to multi-file mode
if ($OneFile) {
    $xrmPackagerArguments += "--one-file"
} else {
    # Multi-file is the default
    $xrmPackagerArguments += "--multi-file"
}

# Add formatting if requested (requires csharpier to be installed)
if ($Format) {
    $xrmPackagerArguments += "--format"
}

# Add consolidate helpers if requested
if ($ConsolidateHelpers) {
    $xrmPackagerArguments += "--consolidate-helpers"
}

if ($AdditionalArguments) {
    $xrmPackagerArguments += $AdditionalArguments
}

Write-Host "Generating Xrm Context..." -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentName" -ForegroundColor Gray
Write-Host "Output: $contextOutputPath" -ForegroundColor Gray

Invoke-XrmPackager -Arguments $xrmPackagerArguments

Write-Host "Xrm Context generation completed." -ForegroundColor Green

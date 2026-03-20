param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Framework = "net10.0",

    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$srcRoot = Join-Path $repoRoot "src"
$solutionPath = Join-Path $srcRoot "XrmPackager.sln"
$consoleProjectPath = Join-Path $srcRoot "Packager.Console/Packager.Console.csproj"
$buildRoot = Join-Path $repoRoot "build"
$publishFolder = Join-Path $buildRoot "XrmPackager"
$legacyPublishFolder = Join-Path $repoRoot "ExecutionScripts/XrmPackager"
$sourceScriptsFolder = Join-Path $srcRoot "Packager.Scripts"

if (-not (Test-Path $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}

if (-not (Test-Path $consoleProjectPath)) {
    throw "Console project not found: $consoleProjectPath"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build XrmPackager" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host "Framework:     $Framework" -ForegroundColor Gray
Write-Host "Solution:      $solutionPath" -ForegroundColor Gray
Write-Host "Build root:    $buildRoot" -ForegroundColor Gray
Write-Host "Publish to:    $publishFolder" -ForegroundColor Gray
Write-Host ""

if ($Clean -and (Test-Path $buildRoot)) {
    Write-Host "Cleaning existing build folder..." -ForegroundColor Yellow
    Remove-Item -Path $buildRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null

$legacyScriptsFolder = Join-Path $buildRoot "Packager.Scripts"
if (Test-Path $legacyScriptsFolder) {
    Remove-Item -Path $legacyScriptsFolder -Recurse -Force
}

if (Test-Path $publishFolder) {
    Write-Host "Cleaning existing publish folder..." -ForegroundColor Yellow
    Remove-Item -Path $publishFolder -Recurse -Force
}

if (Test-Path $legacyPublishFolder) {
    Write-Host "Removing legacy publish folder..." -ForegroundColor Yellow
    Remove-Item -Path $legacyPublishFolder -Recurse -Force
}

Write-Host "Restoring packages..." -ForegroundColor Yellow
& dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

Write-Host "Building solution..." -ForegroundColor Yellow
& dotnet build $solutionPath -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

Write-Host "Publishing console runtime..." -ForegroundColor Yellow
& dotnet publish $consoleProjectPath -c $Configuration -f $Framework -o $publishFolder -p:PublishDir="$publishFolder/" --self-contained false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Cleaning runtime output (remove symbols and language packs)..." -ForegroundColor Yellow
Get-ChildItem -Path $publishFolder -Filter "*.pdb" -File -Recurse | Remove-Item -Force

$languageFolders = @(
    "cs",
    "de",
    "es",
    "fr",
    "it",
    "ja",
    "ko",
    "pl",
    "pt-BR",
    "ru",
    "tr",
    "zh-Hans",
    "zh-Hant"
)

foreach ($languageFolder in $languageFolders) {
    $languagePath = Join-Path $publishFolder $languageFolder
    if (Test-Path $languagePath) {
        Remove-Item -Path $languagePath -Recurse -Force
    }
}

Write-Host "Copying PowerShell scripts to build output..." -ForegroundColor Yellow
Get-ChildItem -Path $sourceScriptsFolder -Filter "*.ps1" -File | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $buildRoot $_.Name) -Force
}

Write-Host "Copying .dotnet-tools.json to build output..." -ForegroundColor Yellow
$toolsManifest = Join-Path $repoRoot ".config/dotnet-tools.json"
if (-not (Test-Path $toolsManifest)) {
    $legacyManifest = Join-Path $repoRoot ".dotnet-tools.json"
    if (Test-Path $legacyManifest) {
        $toolsManifest = $legacyManifest
    }
}

if (Test-Path $toolsManifest) {
    $buildConfigFolder = Join-Path $buildRoot ".config"
    $publishConfigFolder = Join-Path $publishFolder ".config"
    New-Item -ItemType Directory -Path $buildConfigFolder -Force | Out-Null
    New-Item -ItemType Directory -Path $publishConfigFolder -Force | Out-Null

    Copy-Item -Path $toolsManifest -Destination (Join-Path $buildConfigFolder "dotnet-tools.json") -Force
    Copy-Item -Path $toolsManifest -Destination (Join-Path $publishConfigFolder "dotnet-tools.json") -Force
} else {
    Write-Host "Warning: dotnet tool manifest not found in repository root" -ForegroundColor Yellow
}

$requiredPublishFiles = @(
    "xrmpackager.dll",
    "xrmpackager.runtimeconfig.json",
    "xrmpackager.deps.json",
    ".config/dotnet-tools.json"
)

$missingPublishFiles = @()
foreach ($file in $requiredPublishFiles) {
    $fullPath = Join-Path $publishFolder $file
    if (-not (Test-Path $fullPath)) {
        $missingPublishFiles += $file
    }
}

if ($missingPublishFiles.Count -gt 0) {
    throw "Publish output is missing required files: $($missingPublishFiles -join ', ')"
}

Write-Host ""
Write-Host "Build and publish completed successfully." -ForegroundColor Green
Write-Host ""
Write-Host "Published files:" -ForegroundColor Cyan
Get-ChildItem -Path $publishFolder -File -Force | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Build scripts folder:" -ForegroundColor Cyan
Write-Host "  $buildRoot" -ForegroundColor Gray

Write-Host ""
Write-Host "Build runtime folder:" -ForegroundColor Cyan
Write-Host "  $publishFolder" -ForegroundColor Gray

Write-Host ""
Write-Host "Next step:" -ForegroundColor Cyan
Write-Host "  Run $buildRoot/TestConnection.ps1" -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Cyan

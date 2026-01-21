<#
.SYNOPSIS
    Imports a solution to Dataverse (Dev environment).
#>

$config = Import-Module $PSScriptRoot\_InitDaxif.ps1 -force

# Configuration
# Update these values as needed
$solutionZip = "$PSScriptRoot/MySolution.zip"
$publish = $true
$overwrite = $true
$skipDependencies = $false
$convertToManaged = $false

$arguments = @("solution", "import", "--zip", $solutionZip)
if ($publish) {
    $arguments += "--publish"
}
if ($overwrite) {
    $arguments += "--overwrite"
}
if ($skipDependencies) {
    $arguments += "--skip-dependencies"
}
if ($convertToManaged) {
    $arguments += "--convert-to-managed"
}

# Execute
Invoke-Daxif -Arguments $arguments
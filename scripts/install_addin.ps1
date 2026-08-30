# PowerShell script to deploy the Add-in to ArcGIS Pro AddIns folder

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$addinSrc = Join-Path $projectRoot "geometery tools\GeometryTransferTool.esriAddInX"

if (-not (Test-Path $addinSrc)) {
    $addinSrc = Join-Path $projectRoot "GeometryTransferTool\bin\Release\GeometryTransferTool.esriAddInX"
}

if (-not (Test-Path $addinSrc)) {
    Write-Error "Add-in file not found at $addinSrc. Please build the project first using scripts\build_addin.ps1"
    exit 1
}

$guid = "{8F1E54D7-4B3B-49DF-B3F7-9C9D3A4B5E21}"
$userDir = $env:USERPROFILE
$targetDirs = @(
    (Join-Path $userDir "Documents\ArcGIS\AddIns\ArcGISPro\$guid"),
    (Join-Path $userDir "OneDrive\Documents\ArcGIS\AddIns\ArcGISPro\$guid")
)

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Installing GeometryTransferTool to ArcGIS Pro...   " -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

foreach ($dir in $targetDirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    Copy-Item -Path $addinSrc -Destination (Join-Path $dir "GeometryTransferTool.esriAddInX") -Force
    Write-Host " -> Installed to: $dir" -ForegroundColor Green
}

Write-Host ""
Write-Host "Installation completed successfully." -ForegroundColor Green

# PowerShell script to build the solution and package GeometryTransferTool.esriAddInX

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$projectDir = Join-Path $projectRoot "GeometryTransferTool"
$binRelease = Join-Path $projectDir "bin\Release\win-x64"

# Find MSBuild
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $msBuildExe = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
}
if (-not $msBuildExe -or -not (Test-Path $msBuildExe)) {
    $msBuildExe = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msBuildExe)) {
    $msBuildExe = "MSBuild.exe"
}

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Building GeometryTransferTool in Release mode...   " -ForegroundColor Cyan
Write-Host " Using MSBuild: $msBuildExe" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

& $msBuildExe (Join-Path $projectDir "GeometryTransferTool.csproj") /p:Configuration=Release /v:minimal

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit 1
}

# Target output directories
$outputAddinPath = Join-Path $projectDir "bin\Release\GeometryTransferTool.esriAddInX"
$addinPackageDir = Join-Path $projectRoot "Addin_Package"
if (-not (Test-Path $addinPackageDir)) {
    New-Item -ItemType Directory -Path $addinPackageDir -Force | Out-Null
}
$addinPackagePath = Join-Path $addinPackageDir "GeometryTransferTool.esriAddInX"
$stagingDir = Join-Path $projectDir "obj\Release\win-x64\package_staging"

if (Test-Path $stagingDir) {
    Remove-Item -Path $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# Copy Config.daml
Copy-Item -Path (Join-Path $projectDir "Config.daml") -Destination (Join-Path $stagingDir "Config.daml") -Force

# Copy Images
$imgDest = Join-Path $stagingDir "Images"
New-Item -ItemType Directory -Path $imgDest -Force | Out-Null
Copy-Item -Path (Join-Path $projectDir "Images\*") -Destination $imgDest -Recurse -Force

# Copy Install assemblies
$installDest = Join-Path $stagingDir "Install"
New-Item -ItemType Directory -Path $installDest -Force | Out-Null
Copy-Item -Path (Join-Path $binRelease "GeometryTransferTool.dll") -Destination (Join-Path $installDest "GeometryTransferTool.dll") -Force
Copy-Item -Path (Join-Path $binRelease "GeometryTransferTool.pdb") -Destination (Join-Path $installDest "GeometryTransferTool.pdb") -Force
Copy-Item -Path (Join-Path $binRelease "GeometryTransferTool.deps.json") -Destination (Join-Path $installDest "GeometryTransferTool.deps.json") -Force

# Create ZIP as .esriAddInX
if (Test-Path $outputAddinPath) { Remove-Item -Path $outputAddinPath -Force }
if (Test-Path $addinPackagePath) { Remove-Item -Path $addinPackagePath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $outputAddinPath)
Copy-Item -Path $outputAddinPath -Destination $addinPackagePath -Force

Write-Host ""
Write-Host "====================================================" -ForegroundColor Green
Write-Host " Successfully generated ArcGIS Pro Add-in package:  " -ForegroundColor Green
Write-Host " -> $addinPackagePath" -ForegroundColor Green
Write-Host " -> $outputAddinPath" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green

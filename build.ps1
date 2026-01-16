# Build ANDO using its own build script
# This script bootstraps the build by running dotnet run on the build.ando file

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

# Clear dist directory before building
Write-Host "Cleaning dist directory..."
if (Test-Path "dist") {
    Remove-Item -Path "dist/*" -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Building ANDO..."
dotnet run --project src/Ando/Ando.csproj -- @args

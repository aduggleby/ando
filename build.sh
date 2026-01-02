#!/bin/bash
set -e

# Build ANDO using its own build script
# This script bootstraps the build by running dotnet run on the build.ando file

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

# Clear dist directory before building
echo "Cleaning dist directory..."
rm -rf dist/*

echo "Building ANDO..."
dotnet run --project src/Ando/Ando.csproj -- run --local "$@"

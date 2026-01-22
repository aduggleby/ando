#!/usr/bin/env bash
# Run the local development build of ando with push profile
# Uses --dind to enable Docker-in-Docker for building container images
# After push, waits for NuGet to index the package and updates global tool
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/../src/Ando"

# Clean up Syncthing conflict files first
"$SCRIPT_DIR/clean.sh"

# Export GITHUB_TOKEN from gh CLI if not already set
if [[ -z "${GITHUB_TOKEN:-}" ]]; then
    export GITHUB_TOKEN=$(gh auth token 2>/dev/null)
    if [[ -z "$GITHUB_TOKEN" ]]; then
        echo "Error: GITHUB_TOKEN not set and 'gh auth token' failed."
        echo "Please run 'gh auth login' or set GITHUB_TOKEN environment variable."
        exit 1
    fi
    echo "Using GitHub token from gh CLI"
fi

# Get version from project file
VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$PROJECT_DIR/Ando.csproj")
echo "Building and pushing version: $VERSION"

# Run the build with push profile
dotnet run --project "$PROJECT_DIR/Ando.csproj" -- run -p push --dind --read-env "$@"

# Wait for NuGet to index the new version
echo ""
echo "=== Waiting for NuGet to index version $VERSION ==="
MAX_ATTEMPTS=30
ATTEMPT=0

while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
    ATTEMPT=$((ATTEMPT + 1))

    # Check if version is available on NuGet
    if dotnet tool search ando --detail 2>/dev/null | grep -q "$VERSION"; then
        echo "Version $VERSION is now available on NuGet!"
        break
    fi

    if [ $ATTEMPT -eq $MAX_ATTEMPTS ]; then
        echo "Warning: Timed out waiting for NuGet to index version $VERSION"
        echo "You can manually update later with: dotnet tool update -g ando"
        exit 0
    fi

    echo "Waiting for NuGet to index... (attempt $ATTEMPT/$MAX_ATTEMPTS)"
    sleep 10
done

# Update the global tool
echo ""
echo "=== Updating global ando tool ==="
dotnet tool update -g ando

# Verify installation
echo ""
echo "=== Verifying installation ==="
ando --version

#!/usr/bin/env bash
# Run the local development build of ando with push profile
# Uses --dind to enable Docker-in-Docker for building container images
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

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

dotnet run --project "$SCRIPT_DIR/../src/Ando/Ando.csproj" -- run -p push --dind "$@"

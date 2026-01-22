#!/usr/bin/env bash
# Run the local development build of ando
# Uses --dind by default to enable Docker-in-Docker for E2E tests
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Clean up Syncthing conflict files first
"$SCRIPT_DIR/clean.sh"

dotnet run --project "$SCRIPT_DIR/../src/Ando/Ando.csproj" -- --dind "$@"

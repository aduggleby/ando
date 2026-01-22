#!/usr/bin/env bash
# Run the local development build of ando
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Clean up Syncthing conflict files first
"$SCRIPT_DIR/clean.sh"

dotnet run --project "$SCRIPT_DIR/../src/Ando/Ando.csproj" -- "$@"

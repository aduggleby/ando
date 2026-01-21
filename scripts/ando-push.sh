#!/usr/bin/env bash
# Run the local development build of ando with push profile
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
dotnet run --project "$SCRIPT_DIR/../src/Ando/Ando.csproj" -- run -p push "$@"

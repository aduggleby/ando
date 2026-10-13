#!/bin/bash
# =============================================================================
# add-migration.sh
#
# Adds an EF Core migration for BOTH database providers so the SQL Server and
# PostgreSQL migration sets cannot diverge by forgetting one.
#
#   ./scripts/add-migration.sh <MigrationName>
#
# Uses the dotnet-ef version pinned in .config/dotnet-tools.json and the
# design-time factories in src/Ando.Server/Data/Design (no database needed).
#
# The two steps are not atomic. If the PostgreSQL step fails after the SQL
# Server step succeeded, the script names the SQL Server files it just created,
# prints the command that removes them, and exits non-zero. It never deletes
# anything itself, so unrelated work in Migrations/ is never touched.
# =============================================================================

set -euo pipefail

if [ $# -ne 1 ] || [ -z "$1" ]; then
    echo "Usage: $0 <MigrationName>" >&2
    exit 1
fi

NAME="$1"
cd "$(dirname "$0")/.."

PROJECT="src/Ando.Server/Ando.Server.csproj"

dotnet tool restore

SQLSERVER_DIR="src/Ando.Server/Migrations/SqlServer"

list_sqlserver_files() {
    find "$SQLSERVER_DIR" -maxdepth 1 -type f -name '*.cs' | sort
}

BEFORE="$(list_sqlserver_files)"

dotnet ef migrations add "$NAME" -p "$PROJECT" \
    --context SqlServerAndoDbContext -o Migrations/SqlServer

if ! dotnet ef migrations add "$NAME" -p "$PROJECT" \
    --context PostgreSqlAndoDbContext -o Migrations/PostgreSql; then
    CREATED="$(comm -13 <(printf '%s\n' "$BEFORE") <(list_sqlserver_files) | sed '/^$/d')"
    {
        echo ""
        echo "ERROR: the PostgreSQL migration '$NAME' could not be created, but the SQL Server"
        echo "migration was. The migration sets are now out of sync."
        echo ""
        echo "SQL Server migration files created by this run:"
        if [ -n "$CREATED" ]; then
            printf '    %s\n' $CREATED
        else
            echo "    (none detected)"
        fi
        echo "    (and $SQLSERVER_DIR/AndoDbContextModelSnapshot.cs was updated)"
        echo ""
        echo "Remove the SQL Server migration (this also reverts the snapshot), fix the cause, then rerun:"
        echo "    dotnet ef migrations remove -p $PROJECT --context SqlServerAndoDbContext"
        echo "If no development database is reachable, EF refuses to check whether it was applied;"
        echo "add --force only if this migration was never applied to any database."
    } >&2
    exit 1
fi

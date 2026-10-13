#!/usr/bin/env bash
# =============================================================================
# run-dev.sh
#
# Starts a tmux dev session: database container, Ando.Server via dotnet run,
# and the Vite client dev server.
#
# Usage:
#   ./run-dev.sh                 # SQL Server (default)
#   ./run-dev.sh --db sqlserver  # SQL Server
#   ./run-dev.sh --db postgres   # PostgreSQL (docker-compose.postgres.yml)
# =============================================================================
set -euo pipefail

DB_PROVIDER="sqlserver"
while [ $# -gt 0 ]; do
  case "$1" in
    --db)
      if [ $# -lt 2 ]; then
        echo "--db requires a value: sqlserver or postgres"
        exit 1
      fi
      DB_PROVIDER="$2"
      shift 2
      ;;
    --db=*)
      DB_PROVIDER="${1#--db=}"
      shift
      ;;
    -h|--help)
      echo "Usage: $0 [--db sqlserver|postgres]"
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      echo "Usage: $0 [--db sqlserver|postgres]"
      exit 1
      ;;
  esac
done

SESSION_NAME="${TMUX_SESSION_NAME:-ando-dev}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.yml"
POSTGRES_COMPOSE_FILE="$SCRIPT_DIR/docker-compose.postgres.yml"
CLIENT_APP_DIR="$SCRIPT_DIR/src/Ando.Server/ClientApp"
SERVER_PROJECT="$SCRIPT_DIR/src/Ando.Server/Ando.Server.csproj"
LOCAL_DATA_ROOT="$SCRIPT_DIR/.dev-data"
LOCAL_KEYS_PATH="$LOCAL_DATA_ROOT/keys"
LOCAL_ARTIFACTS_PATH="$LOCAL_DATA_ROOT/artifacts"
LOCAL_REPOS_PATH="$LOCAL_DATA_ROOT/repos"

# Keep local server aligned with dev docker defaults.
# The provider decides the database service, the compose files, and the default
# connection string; an explicitly exported connection string still wins.
case "$DB_PROVIDER" in
  sqlserver)
    DB_SERVICE="sqlserver"
    DATABASE_PROVIDER="SqlServer"
    COMPOSE_ARGS="-f \"$COMPOSE_FILE\""
    DEFAULT_CONNECTION_STRING="Server=localhost,17133;Database=AndoServer;User Id=sa;Password=YourPassword123!;TrustServerCertificate=true"
    ;;
  postgres)
    DB_SERVICE="postgres"
    DATABASE_PROVIDER="PostgreSql"
    COMPOSE_ARGS="-f \"$COMPOSE_FILE\" -f \"$POSTGRES_COMPOSE_FILE\""
    DEFAULT_CONNECTION_STRING="Host=localhost;Port=17134;Database=AndoServer;Username=ando;Password=YourPassword123!"
    ;;
  *)
    echo "Unknown --db value: $DB_PROVIDER (expected sqlserver or postgres)"
    exit 1
    ;;
esac
DEV_CONNECTION_STRING="${ConnectionStrings__DefaultConnection:-${CONNECTIONSTRINGS__DEFAULTCONNECTION:-$DEFAULT_CONNECTION_STRING}}"
DEV_ENCRYPTION_KEY="${Encryption__Key:-${ENCRYPTION__KEY:-dGhpc2lzYWRldmVsb3BtZW50a2V5b25seTMyYnl0ZXM=}}"

if ! command -v tmux >/dev/null 2>&1; then
  echo "tmux is required but not installed."
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  echo "docker is required but not installed."
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but not installed."
  exit 1
fi

if ! command -v npm >/dev/null 2>&1; then
  echo "npm is required but not installed."
  exit 1
fi

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "Compose file not found: $COMPOSE_FILE"
  exit 1
fi

if [ "$DB_PROVIDER" = "postgres" ] && [ ! -f "$POSTGRES_COMPOSE_FILE" ]; then
  echo "Compose override not found: $POSTGRES_COMPOSE_FILE"
  exit 1
fi

if [ ! -f "$SERVER_PROJECT" ]; then
  echo "Server project not found: $SERVER_PROJECT"
  exit 1
fi

if [ ! -d "$CLIENT_APP_DIR" ]; then
  echo "Client app directory not found: $CLIENT_APP_DIR"
  exit 1
fi

if tmux has-session -t "$SESSION_NAME" 2>/dev/null; then
  echo "Attaching to existing tmux session '$SESSION_NAME'; it keeps the database it was started with."
  echo "Kill it first (tmux kill-session -t $SESSION_NAME) to switch to --db $DB_PROVIDER."
  if [ -n "${TMUX:-}" ]; then
    tmux switch-client -t "$SESSION_NAME"
  else
    tmux attach -t "$SESSION_NAME"
  fi
  exit 0
fi

tmux new-session -d -s "$SESSION_NAME" -c "$SCRIPT_DIR"
tmux send-keys -t "$SESSION_NAME:0.0" "docker compose $COMPOSE_ARGS up -d $DB_SERVICE; cid=\$(docker compose $COMPOSE_ARGS ps -q $DB_SERVICE); for i in \$(seq 1 60); do status=\$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \"\$cid\" 2>/dev/null || true); [ \"\$status\" = healthy ] && break; sleep 2; done; docker compose $COMPOSE_ARGS stop ando-server >/dev/null 2>&1 || true; mkdir -p \"$LOCAL_KEYS_PATH\" \"$LOCAL_ARTIFACTS_PATH\" \"$LOCAL_REPOS_PATH\"; ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:17100 Database__Provider=$DATABASE_PROVIDER ConnectionStrings__DefaultConnection=\"$DEV_CONNECTION_STRING\" Encryption__Key=\"$DEV_ENCRYPTION_KEY\" DataProtection__KeysPath=\"$LOCAL_KEYS_PATH\" Storage__ArtifactsPath=\"$LOCAL_ARTIFACTS_PATH\" Build__ReposPath=\"$LOCAL_REPOS_PATH\" dotnet run --project \"$SERVER_PROJECT\"" C-m

# Right top pane: database logs (SQL Server or PostgreSQL)
tmux split-window -h -t "$SESSION_NAME:0" -c "$CLIENT_APP_DIR"
tmux send-keys -t "$SESSION_NAME:0.1" "docker compose $COMPOSE_ARGS up -d $DB_SERVICE && docker compose $COMPOSE_ARGS logs -f --tail=100 $DB_SERVICE" C-m

# Right bottom pane: Vite dev server (port 5173)
tmux split-window -v -t "$SESSION_NAME:0.1" -c "$SCRIPT_DIR"
tmux send-keys -t "$SESSION_NAME:0.2" "cd \"$CLIENT_APP_DIR\" && ([ -d node_modules ] || npm ci) && npm run dev -- --host" C-m

tmux select-layout -t "$SESSION_NAME:0" main-vertical
tmux select-pane -t "$SESSION_NAME:0.0"

if [ -n "${TMUX:-}" ]; then
  tmux switch-client -t "$SESSION_NAME"
else
  tmux attach -t "$SESSION_NAME"
fi

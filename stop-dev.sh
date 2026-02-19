#!/usr/bin/env bash
set -euo pipefail

DELETE_CONTAINERS=false
SESSION_NAME="${TMUX_SESSION_NAME:-ando-dev}"

while getopts ":d" opt; do
  case "$opt" in
    d)
      DELETE_CONTAINERS=true
      ;;
    *)
      echo "Usage: $0 [-d]"
      echo "  -d  remove containers (docker compose down)"
      exit 1
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.yml"

if [ "$DELETE_CONTAINERS" = true ]; then
  docker compose -f "$COMPOSE_FILE" down --remove-orphans
else
  docker compose -f "$COMPOSE_FILE" stop
fi

if command -v tmux >/dev/null 2>&1 && tmux has-session -t "$SESSION_NAME" 2>/dev/null; then
  tmux kill-session -t "$SESSION_NAME"
fi

if [ "$DELETE_CONTAINERS" = true ]; then
  echo "Dev services stopped and containers removed."
else
  echo "Dev services stopped."
fi

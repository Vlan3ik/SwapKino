#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKUP_DIR="${1:-$ROOT_DIR/backups}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP_FILE="$BACKUP_DIR/swapkino-$STAMP.dump"

mkdir -p "$BACKUP_DIR"
docker compose -f "$ROOT_DIR/docker/compose.yml" exec -T postgres \
  pg_dump -U swapkino -d swapkino -Fc > "$BACKUP_FILE"
chmod 600 "$BACKUP_FILE"
printf 'Резервная копия создана: %s\n' "$BACKUP_FILE"

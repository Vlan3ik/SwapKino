#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE=(docker compose -f "$ROOT_DIR/docker/compose.yml")
BASE_URL="${BASE_URL:-http://127.0.0.1:18080}"

"${COMPOSE[@]}" up -d

ready=""
ready_ok=false
for _ in $(seq 1 30); do
  ready=$(curl -fsS "$BASE_URL/ready" 2>/dev/null || true)
  if [[ -n "$ready" ]] && python3 -c 'import json,sys; raise SystemExit(0 if json.load(sys.stdin).get("status") == "ready" else 1)' <<<"$ready" 2>/dev/null; then
    ready_ok=true
    break
  fi
  sleep 1
done
if [[ "$ready_ok" != true ]]; then
  echo "SwapKino readiness check failed: ${ready:-no response}" >&2
  "${COMPOSE[@]}" ps >&2
  exit 1
fi

movie_count=0
for _ in $(seq 1 60); do
  movie_count=$(curl -fsS "$BASE_URL/api/v1/movies?page=1" 2>/dev/null | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["results"]))' 2>/dev/null || echo 0)
  if test "$movie_count" -gt 0; then break; fi
  sleep 1
done
if ! test "$movie_count" -gt 0; then
  echo "Catalog did not become non-empty: movie_count=$movie_count" >&2
  "${COMPOSE[@]}" logs --tail=80 api worker >&2 || true
  exit 1
fi

selenium_health=$("${COMPOSE[@]}" exec -T selenium-service python -c 'from urllib.request import urlopen; print(urlopen("http://127.0.0.1:8081/health").read().decode())')
[[ "$selenium_health" == *'"status":"ok"'* ]]
curl -fsSI "$BASE_URL/novnc/vnc.html" >/dev/null
"${COMPOSE[@]}" exec -T selenium-service sh -c 'kill -0 "$(pgrep -f "websockify.*6080" | head -n1)"'

groups=$("${COMPOSE[@]}" exec -T redis-runtime redis-cli XINFO GROUPS swapkino:events)
[[ "$groups" == *'swapkino-imports'* ]]

errors=$("${COMPOSE[@]}" logs --since=2m api worker selenium-service --no-color | rg 'fail:|ERROR|Traceback|Unhandled|Fatal' || true)
test -z "$errors"

printf 'Docker smoke passed: readiness, catalog (%s movies), Selenium/noVNC, consumer group, logs.\n' "$movie_count"

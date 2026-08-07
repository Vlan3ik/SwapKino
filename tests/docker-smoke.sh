#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE=(docker compose -f "$ROOT_DIR/docker/compose.yml")

"${COMPOSE[@]}" up -d

ready=""
for _ in $(seq 1 30); do
  ready=$(curl -fsS http://127.0.0.1/ready 2>/dev/null || true)
  [[ -n "$ready" ]] && break
  sleep 1
done
[[ "$ready" == *'"status":"ready"'* ]]

movie_count=$(curl -fsS 'http://127.0.0.1/api/v1/movies?page=1' | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["results"]))')
test "$movie_count" -gt 0

selenium_health=$("${COMPOSE[@]}" exec -T selenium-service python -c 'from urllib.request import urlopen; print(urlopen("http://127.0.0.1:8081/health").read().decode())')
[[ "$selenium_health" == *'"status":"ok"'* ]]

groups=$("${COMPOSE[@]}" exec -T redis-runtime redis-cli XINFO GROUPS swapkino:events)
[[ "$groups" == *'swapkino-imports'* ]]

errors=$("${COMPOSE[@]}" logs --since=2m api worker selenium-service --no-color | rg 'fail:|ERROR|Traceback|Unhandled|Fatal' || true)
test -z "$errors"

printf 'Docker smoke passed: readiness, catalog (%s movies), Selenium, consumer group, logs.\n' "$movie_count"

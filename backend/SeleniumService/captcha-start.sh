#!/usr/bin/env bash
set -euo pipefail

if [[ "${SELENIUM_HEADLESS:-false}" != "true" ]]; then
  Xvfb :99 -screen 0 1440x1200x24 -ac +extension GLX +render -noreset >/tmp/xvfb.log 2>&1 &
  x11vnc -display :99 -rfbport 5900 -localhost -forever -shared -nopw >/tmp/x11vnc.log 2>&1 &
  websockify --web=/usr/share/novnc --token-plugin=TokenFile --token-source="${NOVNC_TOKEN_FILE:-/tmp/swapkino-novnc.tokens}" 6080 >/tmp/websockify.log 2>&1 &
fi

exec python run.py

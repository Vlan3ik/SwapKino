#!/usr/bin/env bash
set -euo pipefail

if [[ "${SELENIUM_HEADLESS:-false}" != "true" ]]; then
  # A container restart can leave Xvfb's lock/socket behind even though the
  # old X server is gone. Remove only that stale display state; never touch a
  # live Xvfb instance.
  if [[ ! -S /tmp/.X11-unix/X99 ]] || ! pgrep -f '[X]vfb :99 ' >/dev/null 2>&1; then
    rm -f /tmp/.X99-lock /tmp/.X11-unix/X99
    Xvfb :99 -screen 0 1440x1200x24 -ac +extension GLX +render -noreset >/tmp/xvfb.log 2>&1 &
    xvfb_pid=$!
    for _ in {1..20}; do
      if ! kill -0 "$xvfb_pid" 2>/dev/null; then
        echo "Xvfb failed to start" >&2
        sed -n '1,80p' /tmp/xvfb.log >&2 || true
        exit 1
      fi
      if [[ -S /tmp/.X11-unix/X99 ]]; then
        break
      fi
      sleep 0.1
    done
    if [[ ! -S /tmp/.X11-unix/X99 ]]; then
      echo "Xvfb did not create display :99" >&2
      exit 1
    fi
  fi
  x11vnc -display :99 -rfbport 5900 -localhost -forever -shared -nopw >/tmp/x11vnc.log 2>&1 &
  websockify --web=/usr/share/novnc --token-plugin=TokenFile --token-source="${NOVNC_TOKEN_FILE:-/var/run/swapkino/novnc.tokens}" 6080 >/tmp/websockify.log 2>&1 &
fi

exec python run.py

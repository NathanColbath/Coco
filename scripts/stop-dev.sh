#!/usr/bin/env bash
#
# Stops everything this repo starts.
#
# Why this exists: `dotnet watch` spawns a `dotnet-watch.dll` child that
# survives killing the launcher, and the Angular dev server leaves esbuild
# worker processes behind. Those orphans keep the ports busy and restart the
# API whenever a file changes, which looks like random "address already in use"
# failures later.
#
#   npm run dev:stop

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
killed=0

stop_pid() {
  local pid="$1" reason="$2"
  [ -z "$pid" ] && return
  if kill -0 "$pid" 2>/dev/null; then
    if kill -9 "$pid" 2>/dev/null; then
      echo "  stopped $pid — $reason"
      killed=$((killed + 1))
    fi
  fi
}

# Only touch processes whose working directory is inside this repo, so a stray
# match can never take down something unrelated.
belongs_to_repo() {
  local pid="$1" cwd
  cwd="$(lsof -a -p "$pid" -d cwd -Fn 2>/dev/null | sed -n 's/^n//p' | head -1)"
  [ -n "$cwd" ] && [[ "$cwd" == "$ROOT"* ]]
}

echo "Stopping Comic Studio dev processes…"

# 1. Whatever is holding the dev ports.
for port in 4200 5240; do
  for pid in $(lsof -ti:"$port" 2>/dev/null); do
    if belongs_to_repo "$pid"; then
      stop_pid "$pid" "listening on $port"
    else
      echo "  left $pid alone — listening on $port but not from this repo"
    fi
  done
done

# 2. Watchers and workers, including orphans with no port bound.
for pattern in "dotnet-watch.dll" "ComicStudio.Api" "ng serve" "@angular/build" "esbuild --service"; do
  for pid in $(pgrep -f "$pattern" 2>/dev/null); do
    if belongs_to_repo "$pid"; then
      stop_pid "$pid" "$pattern"
    fi
  done
done

if [ "$killed" -eq 0 ]; then
  echo "  nothing was running."
fi

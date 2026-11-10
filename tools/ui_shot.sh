#!/bin/bash
# 使い方: tools/ui_shot.sh <名前> [引数...]  (-panel shop|missions|settings|free|daily, -demo win|fail, -mobile など)
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; name="$1"; shift
out="$ROOT/Shots/$name"; rm -rf "$out"; mkdir -p "$out"
timeout 230 open -W -n -a "$ROOT/Build/ShibuyaRush.app" --args -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -shotdir "$out" -nopersist -logFile "$out/player.log" "$@"
ls "$out"

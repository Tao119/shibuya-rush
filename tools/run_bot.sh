#!/bin/bash
# 使い方: tools/run_bot.sh <目的地index> <出力名> [追加引数...]
# ビルド済みアプリをBotで走らせ、スクショと result.json を Shots/<出力名>/ に出す。
# 直接実行するとウィンドウが隠れて止まることがあるため、open で前面に出して起動する。
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
idx="$1"; name="$2"; shift 2
out="$ROOT/Shots/$name"; rm -rf "$out"; mkdir -p "$out"
timeout 240 open -W -n -a "$ROOT/Build/ShibuyaRush.app" --args -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
  -autoplay "$idx" -shotdir "$out" -quitafter 200 -logFile "$out/player.log" "$@"
echo "$name: $(cat "$out/result.json" 2>/dev/null || echo FAILED)"

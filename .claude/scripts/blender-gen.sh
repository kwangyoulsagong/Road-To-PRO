#!/bin/bash
# bpy 생성 스크립트를 백그라운드 Blender로 실행한다.
# 사용: .claude/scripts/blender-gen.sh <Art/Blender/scripts/xxx.py> [출력 경로] [추가 인자...]
set -eu
BLENDER="/Applications/Blender.app/Contents/MacOS/Blender"
ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
SCRIPT="$1"; shift
NAME="$(basename "$SCRIPT" .py)"
OUTPATH="${1:-$ROOT/Art/Generated/$NAME.glb}"; [ $# -gt 0 ] && shift
mkdir -p "$(dirname "$OUTPATH")"
"$BLENDER" --background --factory-startup --python-exit-code 1 --python "$SCRIPT" -- --out "$OUTPATH" "$@" 2>&1 | grep -vE '^(Read prefs|Blender quit|$)' | tail -40
[ -f "$OUTPATH" ] && echo "생성됨: $OUTPATH ($(du -h "$OUTPATH" | cut -f1))" || { echo "출력 파일이 없다: $OUTPATH"; exit 1; }

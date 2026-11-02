#!/bin/bash
# Unity EditMode 테스트를 배치모드로 실행하고 결과를 요약한다.
# 사용: .claude/scripts/unity-test.sh [EditMode|PlayMode] [테스트필터] [카테고리]
#   예: .claude/scripts/unity-test.sh EditMode "RoadToPro.Tests.EditMode.Determinism" "!Slow"
# 주의: 같은 프로젝트를 Unity 에디터가 열고 있으면 실패한다 (에디터를 닫거나 에디터 Test Runner 사용).
set -u
UNITY="/Applications/Unity/Hub/Editor/6000.3.27f2/Unity.app/Contents/MacOS/Unity"
ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
PLATFORM="${1:-EditMode}"
FILTER="${2:-}"
CATEGORY="${3:-}"
OUT="$ROOT/Temp/ClaudeTests"
mkdir -p "$OUT"
RESULTS="$OUT/results-$PLATFORM.xml"
LOG="$OUT/log-$PLATFORM.txt"
rm -f "$RESULTS"

if [ -f "$ROOT/Temp/UnityLockfile" ] && lsof "$ROOT/Temp/UnityLockfile" >/dev/null 2>&1; then
  echo "Unity 에디터가 이 프로젝트를 열고 있다. 에디터를 닫고 다시 실행하거나, 에디터의 Test Runner에서 돌려라."
  exit 3
fi

args=(-batchmode -nographics -projectPath "$ROOT" -runTests -testPlatform "$PLATFORM" -testResults "$RESULTS" -logFile "$LOG")
[ -n "$FILTER" ] && args+=(-testFilter "$FILTER")
[ -n "$CATEGORY" ] && args+=(-testCategory "$CATEGORY")

"$UNITY" "${args[@]}"
code=$?

if [ ! -f "$RESULTS" ]; then
  echo "결과 파일이 없다 (exit $code). 컴파일 에러일 가능성이 크다. 로그 마지막 부분:"
  grep -E 'error CS[0-9]+|Scripts have compiler errors|Exception' "$LOG" | head -30
  exit 1
fi

python3 - "$RESULTS" <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
print(f"합계 {r.get('total')}  통과 {r.get('passed')}  실패 {r.get('failed')}  건너뜀 {r.get('skipped')}  ({float(r.get('duration',0)):.1f}s)")
for tc in r.iter('test-case'):
    if tc.get('result') == 'Failed':
        msg = (tc.findtext('failure/message') or '').strip().splitlines()
        st = (tc.findtext('failure/stack-trace') or '').strip().splitlines()
        print(f"\n✗ {tc.get('fullname')}")
        for l in msg[:8]: print("   ", l)
        for l in st[:4]: print("   ", l)
PY
exit $code

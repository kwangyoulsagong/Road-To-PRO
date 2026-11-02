#!/bin/bash
# PostToolUse(Edit|Write): 순수 C# 코어 모듈의 Unity 참조·비결정 API 검사
command -v jq >/dev/null || exit 0
. "$(dirname "$0")/lib.sh"
f=$(jq -r '.tool_input.file_path // empty')
[ -z "$f" ] && exit 0
lrel=$(lower "$(relpath "$f")")
case "$lrel" in
  assets/_project/core/*.cs|assets/_project/matchengine/*.cs|assets/_project/career/*.cs|assets/_project/worldgen/*.cs) ;;
  *) exit 0 ;;
esac
[ -f "$f" ] || exit 0

pat='using[[:space:]]+(static[[:space:]]+)?Unity(Engine|Editor)'
pat+='|(^|[^A-Za-z0-9_])(Mathf|Debug)\.'
pat+='|(^|[^A-Za-z0-9_.])Vector[234](Int)?([^A-Za-z0-9_]|$)'
pat+='|(^|[^A-Za-z0-9_])Time\.(time|deltaTime|frameCount|realtimeSinceStartup)'
pat+='|System\.Random|new[[:space:]]+Random[[:space:]]*\(|UnityEngine\.Random'
pat+='|Guid\.NewGuid|DateTime(Offset)?\.(Now|UtcNow|Today)|Stopwatch|Environment\.TickCount'
pat+='|Parallel\.|Task\.Run|ThreadPool\.'
pat+='|GetHashCode\(\)[[:space:]]*%'

# 주석 줄(//, *, ///)은 제외
hits=$(grep -nE "$pat" "$f" | grep -vE '^[0-9]+:[[:space:]]*(//|\*)')
if [ -n "$hits" ]; then
  {
    echo "결정론/순수 C# 규칙 위반 의심 ($f) — .claude/rules/20-match-engine.md 참고:"
    echo "$hits"
    echo "대체: RoadToPro.Core.Math(Vec2/Vec3) / RoadToPro.Core.Random(시드 주입) / 게임 시간 / 주입된 ILogger. string.GetHashCode는 실행마다 달라진다."
  } >&2
  exit 2
fi
exit 0

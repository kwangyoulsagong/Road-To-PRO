#!/bin/bash
# PostToolUse(Write|Bash): 새 서드파티 바이너리 에셋이 생겼는데 LICENSES.csv가 그대로면 한 번 상기.
# 검증 증거 스크린샷(specs/*/evidence)은 대상이 아니다.
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0
new=$(git -c core.quotepath=false status --porcelain --untracked-files=all -- Art/ThirdParty Assets 2>/dev/null \
  | grep -E '^\?\? ' | cut -c4- | sed -E 's/^"(.*)"$/\1/' \
  | grep -iE '\.(png|jpe?g|tga|exr|hdr|fbx|glb|gltf|wav|ogg|mp3|ttf|otf)$')
[ -z "$new" ] && exit 0

mkdir -p .claude/.cache
sig=$(printf '%s' "$new" | shasum | cut -c1-12)
[ "$(cat .claude/.cache/art-license-sig 2>/dev/null)" = "$sig" ] && exit 0
printf '%s' "$sig" > .claude/.cache/art-license-sig

if [ -z "$(git status --porcelain -- Art/LICENSES.csv)" ]; then
  {
    echo "새 바이너리 에셋이 있는데 Art/LICENSES.csv 가 바뀌지 않았다. 외부 에셋이면 출처를 기록하라 (bpy 산출물이면 무시):"
    printf '%s\n' "$new" | head -20
  } >&2
  exit 2
fi
exit 0

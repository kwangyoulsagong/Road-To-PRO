#!/bin/bash
# PreToolUse(Edit|Write|NotebookEdit): Unity 생성물·IDE 파일·.meta 편집 차단
command -v jq >/dev/null || exit 0
. "$(dirname "$0")/lib.sh"
f=$(jq -r '.tool_input.file_path // .tool_input.notebook_path // empty')
[ -z "$f" ] && exit 0
rel=$(relpath "$f")
lrel=$(lower "$rel")
case "$lrel" in
  library/*|temp/*|logs/*|usersettings/*|obj/*|build/*|builds/*)
    echo "차단: $rel 은 Unity 생성물이다. 원본(Assets/, Packages/, ProjectSettings/)을 고쳐라." >&2; exit 2 ;;
  *.csproj|*.sln|*.slnx)
    echo "차단: $rel 은 Unity가 재생성한다. asmdef를 고쳐라." >&2; exit 2 ;;
  *.meta)
    echo "차단: .meta 는 Unity가 관리한다. 직접 쓰지 말고 짝 파일만 다뤄라. 꼭 필요하면 사용자에게 확인받아라." >&2; exit 2 ;;
esac
exit 0

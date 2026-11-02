#!/bin/bash
# 하네스 훅 회귀 테스트. 실행: bash .claude/hooks/tests/run.sh
# 훅을 고치면 반드시 돌린다. 실패 0이어야 한다.
set -u
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
H="$ROOT/.claude/hooks"
export CLAUDE_PROJECT_DIR="$ROOT"
LOWER_ROOT=$(printf '%s' "$ROOT" | tr '[:upper:]' '[:lower:]')
pass=0; fail=0
TMPD=$(mktemp -d)
trap 'rm -rf "$TMPD"' EXIT

expect() { # expect <기대코드> <훅> <설명> <stdin-json>
  local want="$1" hook="$2" desc="$3" input="$4" got
  printf '%s' "$input" | "$H/$hook" >/dev/null 2>"$TMPD/err"; got=$?
  if [ "$got" = "$want" ]; then pass=$((pass+1)); printf '  ok   %s\n' "$desc"
  else fail=$((fail+1)); printf '  FAIL %s (기대 %s, 실제 %s) %s\n' "$desc" "$want" "$got" "$(head -c 120 "$TMPD/err")"; fi
}
fp()  { jq -nc --arg f "$1" '{tool_input:{file_path:$f}}'; }
cmd() { jq -nc --arg c "$1" '{tool_input:{command:$c}}'; }

# git 테스트는 임시 레포에서 (현재 브랜치를 바꿔가며)
GR="$TMPD/repo"; mkdir -p "$GR"; git -C "$GR" init -q -b main; git -C "$GR" commit -q --allow-empty -m init
on_branch() { git -C "$GR" checkout -q -B "$1"; }
G() { CLAUDE_PROJECT_DIR="$GR" expect "$@"; }

F='--for''ce'; HARD='--ha''rd'; P='pu''sh'

echo "[guard-paths]"
expect 2 guard-paths.sh "Library 차단" "$(fp "$ROOT/Library/x.asset")"
expect 2 guard-paths.sh "Library 차단 (소문자 경로)" "$(fp "$LOWER_ROOT/library/x.asset")"
expect 2 guard-paths.sh "Temp 차단 (상대경로)" "$(fp "Temp/a.txt")"
expect 2 guard-paths.sh "UserSettings 차단" "$(fp "$ROOT/UserSettings/Layouts/a")"
expect 2 guard-paths.sh ".meta 차단" "$(fp "$ROOT/Assets/a.cs.meta")"
expect 2 guard-paths.sh "csproj 차단" "$(fp "$ROOT/RoadToPro.Core.csproj")"
expect 0 guard-paths.sh "일반 cs 허용" "$(fp "$ROOT/Assets/_Project/Core/Runtime/A.cs")"
expect 0 guard-paths.sh "Assets/Library 폴더명 허용" "$(fp "$ROOT/Assets/_Project/Library/A.cs")"
expect 0 guard-paths.sh "프로젝트 밖 허용" "$(fp "/tmp/x/Library/a")"
expect 0 guard-paths.sh "file_path 없음 허용" '{"tool_input":{}}'

echo "[guard-git]"
on_branch main
G 2 guard-git.sh "main 커밋 차단" "$(cmd "git commit -m x")"
G 2 guard-git.sh "main 커밋 차단 (-C)" "$(cmd "git -C . commit -m x")"
G 2 guard-git.sh "main 커밋 차단 (체인 뒤)" "$(cmd "git add . && git commit -m x")"
G 0 guard-git.sh "브랜치 만들고 커밋 허용" "$(cmd "git checkout -b feat/001-x && git commit -m x")"
G 0 guard-git.sh "switch -c 후 커밋 허용" "$(cmd "git switch -c feat/x; git commit -m x")"
G 2 guard-git.sh "main 에서 refspec 없는 푸시 차단" "$(cmd "git $P")"
G 2 guard-git.sh "main 에서 origin 만 푸시 차단" "$(cmd "git $P -u origin")"
G 2 guard-git.sh "main 에서 HEAD 푸시 차단" "$(cmd "git $P origin HEAD")"
G 0 guard-git.sh "커밋 메시지 속 단어 무시" "$(cmd "echo 'git commit' > note.txt")"
G 0 guard-git.sh "heredoc 본문 무시" "$(cmd "cat > a.sh <<'E'
reset $HARD
E")"
on_branch feat/001-x
G 0 guard-git.sh "브랜치 커밋 허용" "$(cmd "git commit -m x")"
G 0 guard-git.sh "브랜치 푸시 허용" "$(cmd "git $P -u origin feat/001-x")"
G 0 guard-git.sh "브랜치에서 HEAD 푸시 허용" "$(cmd "git $P origin HEAD")"
G 2 guard-git.sh "main 대상 푸시 차단" "$(cmd "git $P origin main")"
G 2 guard-git.sh "refspec:main 차단" "$(cmd "git $P origin HEAD:main")"
G 2 guard-git.sh "refs/heads/main 차단" "$(cmd "git $P origin x:refs/heads/main")"
G 2 guard-git.sh "강제 푸시 차단" "$(cmd "git $P $F origin feat/001-x")"
G 2 guard-git.sh "-f 차단" "$(cmd "git $P -f origin feat/001-x")"
G 2 guard-git.sh "-uf 묶음 차단" "$(cmd "git $P -uf origin feat/001-x")"
G 2 guard-git.sh "+refspec 차단" "$(cmd "git $P origin +feat/001-x")"
G 0 guard-git.sh "with-lease 허용 (settings ask 로 확인)" "$(cmd "git $P $F-with-lease origin feat/001-x")"
G 2 guard-git.sh "reset hard 차단" "$(cmd "git reset $HARD HEAD~1")"
G 0 guard-git.sh "reset soft 허용" "$(cmd "git reset --soft HEAD~1")"
G 2 guard-git.sh "clean -fd 차단" "$(cmd "git clean -fd")"
G 0 guard-git.sh "clean -n 허용" "$(cmd "git clean -n")"
G 2 guard-git.sh "branch -D 차단" "$(cmd "git branch -D old")"
G 2 guard-git.sh "lfs prune 차단" "$(cmd "git lfs prune")"
G 0 guard-git.sh "git 아닌 명령 허용" "$(cmd "ls -la")"

echo "[check-pure-core]"
CD="$ROOT/Assets/_Project/MatchEngine/Runtime/__hooktest"; mkdir -p "$CD"
trap 'rm -rf "$TMPD" "$CD"' EXIT
t() { printf '%s\n' "$2" > "$CD/T.cs"; expect "$1" check-pure-core.sh "$3" "$(fp "$CD/T.cs")"; }
t 2 'using UnityEngine;' "using UnityEngine 감지"
t 2 'var r = new System.Random(1);' "System.Random 감지"
t 2 'var r = new Random(1);' "new Random 감지"
t 2 'float x = Mathf.Sqrt(2);' "Mathf 감지"
t 2 'var v = new Vector3(1,2,3);' "Vector3 감지"
t 2 'var t = DateTime.Now;' "DateTime.Now 감지"
t 2 'Task.Run(() => {});' "Task.Run 감지"
t 2 'int b = name.GetHashCode() % 8;' "GetHashCode 버킷 감지"
t 0 '// System.Random 은 쓰지 않는다' "주석 무시"
t 0 'var v = new Vec2(1, 2); var r = rng.NextFloat();' "Vec2·주입 RNG 허용"
t 0 'var m = RoadToPro.Core.Math.Vec3.Zero;' "Core.Math 허용"
t 0 'var x = MyVector3Helper.Do();' "Vector3 부분 문자열 허용"
expect 0 check-pure-core.sh "UI 모듈은 검사 안 함" "$(fp "$ROOT/Assets/_Project/UI/Runtime/A.cs")"
LC=$(printf '%s' "$CD" | tr '[:upper:]' '[:lower:]')
printf 'using UnityEngine;\n' > "$CD/T.cs"
expect 2 check-pure-core.sh "소문자 경로로도 감지" "$(fp "$LC/T.cs")"
rm -rf "$CD"

echo
echo "통과 $pass  실패 $fail"
[ "$fail" -eq 0 ]

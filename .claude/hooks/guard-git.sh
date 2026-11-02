#!/bin/bash
# PreToolUse(Bash): main 직접 커밋/푸시, 강제 푸시, 되돌릴 수 없는 git 명령 차단.
# 명령을 && || ; | 와 줄바꿈 기준으로 나눠 "git ..."으로 시작하는 구간만 본다
# (heredoc 본문·echo 문자열 안의 단어로 오탐하지 않게).
command -v jq >/dev/null || exit 0
cmd=$(jq -r '.tool_input.command // empty')
[ -z "$cmd" ] && exit 0
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null
cur=$(git symbolic-ref --short HEAD 2>/dev/null)

block() { echo "차단: $1" >&2; exit 2; }
is_main() { [ "$1" = main ] || [ "$1" = master ]; }

segs=$(printf '%s\n' "$cmd" | sed -E 's/(&&|\|\||;|\|)/\
/g')

while IFS= read -r seg; do
  seg=$(printf '%s' "$seg" | sed -E 's/^[[:space:]]*(\(|\{|then|do|else)?[[:space:]]*//')
  case "$seg" in git\ *|git) ;; *) continue ;; esac
  # 전역 옵션 제거: -C <경로>, -c <k=v>, --no-pager, --git-dir=... 등
  g=$(printf '%s' "$seg" | sed -E 's/^git(([[:space:]]+(-C|-c)[[:space:]]+[^[:space:]]+)|([[:space:]]+--[a-z-]+(=[^[:space:]]+)?))*[[:space:]]*/git /')
  # shellcheck disable=SC2086
  set -f; set -- $g; set +f
  shift
  sub="${1:-}"; [ $# -gt 0 ] && shift
  case "$sub" in
    checkout|switch)
      nb=
      for a in "$@"; do
        case "$a" in
          -b|-B|-c|-C|--orphan) nb=1 ;;
          -*) ;;
          *) if [ -n "$nb" ]; then cur="$a"; nb=; fi ;;
        esac
      done ;;
    commit)
      is_main "$cur" && block "$cur 에 직접 커밋할 수 없다. 먼저 'git checkout -b <종류>/<주제>'." ;;
    push)
      pos=()
      for a in "$@"; do
        case "$a" in
          --force-with-lease*|--force-if-includes) ;;
          --force|--mirror) block "강제 푸시 금지. 꼭 필요하면 사용자가 직접 실행한다." ;;
          --*) ;;
          -*f*) block "강제 푸시(-f) 금지. 꼭 필요하면 사용자가 직접 실행한다." ;;
          -*) ;;
          +*) block "강제 푸시(+refspec) 금지." ;;
          *) pos+=("$a") ;;
        esac
      done
      if [ ${#pos[@]} -le 1 ]; then
        is_main "$cur" && block "main 브랜치에서 refspec 없이 푸시할 수 없다. 작업 브랜치를 푸시하고 PR을 열어라."
      else
        for r in "${pos[@]:1}"; do
          dst="${r##*:}"; dst="${dst#refs/heads/}"
          if is_main "$dst" || { [ "$dst" = HEAD ] && is_main "$cur"; }; then
            block "main 으로 직접 푸시할 수 없다. 브랜치를 푸시하고 PR을 열어라."
          fi
        done
      fi ;;
    reset)
      for a in "$@"; do [ "$a" = --hard ] && block "git reset --hard 는 되돌릴 수 없다. 사용자에게 먼저 확인받아라."; done ;;
    clean)
      for a in "$@"; do case "$a" in --force|-*f*) block "git clean -f 는 되돌릴 수 없다. 사용자에게 먼저 확인받아라." ;; esac; done ;;
    lfs)
      [ "${1:-}" = prune ] && block "git lfs prune 은 사용자 확인이 필요하다." ;;
    branch)
      for a in "$@"; do [ "$a" = -D ] && block "git branch -D 는 사용자 확인이 필요하다."; done ;;
  esac
done <<< "$segs"
exit 0

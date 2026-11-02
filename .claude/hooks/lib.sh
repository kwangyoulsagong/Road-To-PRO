#!/bin/bash
# 공용 함수. macOS 기본 파일시스템은 대소문자 비구분이라 경로 비교를 소문자로 한다.
lower() { printf '%s' "$1" | tr '[:upper:]' '[:lower:]'; }

# 프로젝트 루트 기준 상대 경로. 프로젝트 밖 절대경로는 그대로.
relpath() {
  local f="$1" root="${CLAUDE_PROJECT_DIR:-$(pwd)}"
  root="${root%/}"
  local lf lr
  lf=$(lower "$f"); lr=$(lower "$root")
  case "$lf" in
    "$lr"/*) printf '%s' "${f:$((${#root}+1))}" ;;
    /*)      printf '%s' "$f" ;;
    *)       printf '%s' "${f#./}" ;;
  esac
}

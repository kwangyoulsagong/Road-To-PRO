---
name: spec
description: 새 기능의 spec.md(요구사항·수용 기준)를 기획서에서 추출해 작성한다. SDD 1단계. "스펙 써줘", "/spec <기능>" 등.
---
# /spec — 스펙 작성

1. `specs/`에서 마지막 번호를 찾아 다음 번호 NNN, 짧은 영어 slug를 정한다.
2. `spec-checker` 에이전트(또는 직접)로 `docs/index.html`·`docs/scenarios.html`에서 해당 기능의 수치·규칙·결정을 출처와 함께 뽑는다.
3. `specs/_templates/spec.md`를 복사해 `specs/NNN-<slug>/spec.md`를 채운다.
   - 구현 방법(클래스·라이브러리)을 쓰지 않는다.
   - 기획서에 없는 수치는 `[미정]`. 지어내지 않는다.
   - 수용 기준은 테스트 가능한 형태(`AC-01: ~이면 ~`).
4. `python3 -I .claude/scripts/spec-lint.py NNN` 오류 0 확인. 그리고 `specs/README.md` 표에 행 추가.
5. 범위가 크면(수용 기준 10개 초과, 모듈 3개 이상) 스펙을 쪼개자고 제안한다.
6. 사용자에게 요약 + `미정 / 질문` 목록을 보여주고 승인을 기다린다. 승인되면 `상태: 승인됨`.
브랜치는 `feat/NNN-<slug>` (main이면 새로 만든다).

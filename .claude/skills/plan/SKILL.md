---
name: plan
description: 승인된 spec.md로부터 plan.md(설계·모듈·데이터·테스트 전략)를 작성한다. SDD 2단계. "/plan NNN".
---
# /plan — 플랜 작성

1. `specs/NNN-*/spec.md`가 `상태: 승인됨`인지 확인. 아니면 멈추고 알린다.
2. 현재 코드 구조를 확인한다 (기존 타입·asmdef 재사용 우선).
3. `specs/_templates/plan.md`를 복사해 채운다.
   - 모듈 배치는 `.claude/rules/10-csharp.md` 의존 방향을 지킨다.
   - 코어 모듈은 `.claude/rules/20-match-engine.md` 결정론 규칙을 지킨다.
   - 모든 AC가 테스트 전략 표에 한 번 이상 나와야 한다.
4. 스펙에 없는 기능을 플랜에 끼워 넣지 않는다. 스펙이 부족하면 스펙 수정을 제안한다.
5. 요약을 보여주고 승인을 기다린다.

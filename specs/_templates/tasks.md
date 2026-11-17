# NNN — <기능 이름> · 태스크

상태: 초안
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면).
중간 태스크의 `검증:`은 좁게(바뀐 테스트 클래스, `smoke --scenario X --profile mac-m1-1600p`, 생성기 1회). `smoke`·`full`·`farm.py art` 전체·`bench`는 마지막 검증 태스크에서만 (`.claude/rules/05-sdd.md` "검증 횟수").

<!-- 검증 준비 체크 (2026-11-17, 082 회고): 시뮬 틱 경로를 바꾸는 스펙이면 첫 태스크에 `Data/perf/NNN-<slug>.json`(IL2CPP 예산)과 `Verification/Runtime/Bench/SimBench.cs` 스펙 등록을 넣는다. 시나리오가 아닌 JSON은 `Data/e2e/` 바로 아래에 두지 않는다(farm이 시나리오로 읽음). 90분 경기를 여러 번 도는 테스트는 같은 시드 경기를 공유하고(082 `LiveContactAudit.Played90`), 경기 여러 개는 병렬로 돈다. -->
- [ ] T01 <작업> — 파일: `...` — 검증: <테스트 이름 또는 확인 방법>
- [ ] T02
- [ ] T03 수용 기준 전체 테스트 통과 (`/unity-test`)
- [ ] T04 `/verify NNN` — 디바이스팜 증거 리포트 생성, 실패 0·성능 예산 통과 확인

# 009 — 팀 형태 · 전술 · 국면 FSM · 태스크

상태: 구현 완료 · AC-18·AC-19 보류 (2026-11-09)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다. Unity 배치 1회가 길어서, 테스트를 먼저 쓰는 태스크는 같은 블록의 구현과 한 배치로 돌린다. 중간 검증은 `.claude/scripts/unity-test.sh EditMode <클래스 필터>`로 좁힌다. `bench`·EditMode 전체·`/verify`는 마지막 태스크에서 각 1회.

## A. 데이터 · 입력
- [x] T01 데이터·지시 테스트 — 파일: `Tests/EditMode/MatchEngine/Tactics/{ShapeTestKit,FormationCatalogTests,TeamInstructionsTests}.cs` — 검증: AC-01·14 (구현 전 컴파일 실패) — 결과: `FormationCatalogTests` 4·`TeamInstructionsTests` 3 통과. `ShapeTestKit` 추가
- [x] T02 포메이션 모델·카탈로그·지시 — 파일: `Tactics/Data/{FormationDefinition,FormationCatalog}.cs`, `Tactics/{TeamInstructions,TeamTactics,TeamPhase}.cs`, `Core/Runtime/Random/RngStreamId.cs`, `Match/Events/SimEventKind.cs` — 검증: `FormationCatalogTests`(형식 부분)·`TeamInstructionsTests` 통과 — 결과: 통과. NaN은 0.5, 열거형 범위 밖은 기본값으로 보정
- [x] T03 기본 데이터 — 파일: `Data/tactics/formations.json`(6종), `Data/tuning/shape.json`, `Tactics/Data/{ShapeTuningIds,ShapeParams}.cs` — 검증: AC-01 `Defaults_AllValid`, 튜닝 로드·범위 검사 — 결과: 6종·튜닝 148개 로드 통과. JSON은 스크래치 생성기로 만든 뒤 레포에는 결과만 둠
- [x] T04 주입 입력·대체 구현 — 파일: `Tactics/ShapeInputs.cs` — 검증: 컴파일, 대체 구현 값 단위 확인 (`ShapeInputsTests`) — 결과: 컴파일·연결 테스트로 확인. 별도 `ShapeInputsTests`는 만들지 않음(대체 구현이 값 반환뿐이고 `PressPlannerTests`·연결 테스트가 사용)

## B. 국면
- [x] T05 국면 테스트 — 파일: `Tests/.../Tactics/PhaseMachineTests.cs` — 검증: AC-02·03, 같은 틱 재탈취, 루즈볼, OnLoss 3종 (구현 전 실패) — 결과: `PhaseMachineTests` 10개(플랜 외 AttTransition 시간 초과·세트피스 복귀 포함)
- [x] T06 점유 추적·FSM — 파일: `Tactics/{PossessionTracker,PhaseMachine}.cs` — 검증: `PhaseMachineTests` 통과 — 결과: 통과

## C. 슬롯 목표 · 라인
- [x] T07 솔버 테스트 — 파일: `Tests/.../Tactics/{ShapeSolverTests,ShapePropertyTests}.cs` — 검증: AC-04·05·08·09 (구현 전 실패) — 결과: `ShapeSolverTests` 12·`ShapePropertyTests` 2 통과
- [x] T08 솔버·앵커 보간 — 파일: `Tactics/{ShapeSolver,AnchorBlend}.cs` — 검증: `ShapeSolverTests`·`ShapePropertyTests` 통과 — 결과: 통과. 팀 단위 계산을 묶은 `Tactics/TeamShapeEngine.cs`를 플랜 표에 없이 추가(단계·테스트 공용 경로)
- [x] T09 수비 라인 테스트 — 파일: `Tests/.../Tactics/DefensiveLineTests.cs` — 검증: AC-06·07, 오프사이드 라인(스위퍼 GK·공·하프라인) (구현 전 실패) — 결과: `DefensiveLineTests` 7개 통과
- [x] T10 수비 라인 — 파일: `Tactics/DefensiveLine.cs` — 검증: `DefensiveLineTests` 통과 — 결과: 통과. FR-44 step-up 확률은 백패스 상승 에지에 1회 추첨

## D. 매칭 · 존 · 압박
- [x] T11 매칭·존·압박 테스트 — 파일: `Tests/.../Tactics/{HungarianTests,ZoneResponderTests,PressPlannerTests}.cs` — 검증: AC-10, 교차·동점·직사각, 존 1순위 (구현 전 실패) — 결과: `HungarianTests` 4·`ZoneResponderTests` 1·`PressPlannerTests` 10개
- [x] T12 슬롯 매칭·존 — 파일: `Tactics/{SlotMatcher,ZoneResponder}.cs` — 검증: `HungarianTests`·`ZoneResponderTests` 통과 — 결과: 통과. 행을 오름차순으로 넣어야 동점이 슬롯 id 순이 됨(테스트로 발견)
- [x] T13 압박 — 파일: `Tactics/{PressPlanner,PressureRules}.cs` — 검증: `PressPlannerTests` 통과 — 결과: 통과

## E. 세트피스
- [x] T14 세트피스 테스트 — 파일: `Tests/.../Tactics/SetPieceLayoutTests.cs` — 검증: AC-13, 코너 근처 프리킥 거리 규칙 (구현 전 실패) — 결과: `SetPieceLayoutTests` 8개
- [x] T15 세트피스 배치 — 파일: `Tactics/SetPieceLayout.cs` — 검증: `SetPieceLayoutTests` 통과 — 결과: 통과. 코너 부근은 원 위 32방향 중 피치 안 최근접 지점

## F. 파이프라인 연결
- [x] T16 연결 테스트 — 파일: `Tests/.../Tactics/{ScenarioBoundaryTests,RosterChangeTests,SetupWiringTests,ShapeTelemetryTests}.cs` — 검증: AC-11·12, AC-19 지표 기록 (구현 전 실패) — 결과: `ConnectionTests.cs` 한 파일에 묶음(`ScenarioBoundaryTests` 3·`RosterChangeTests` 3·`SetupWiringTests` 5·`ShapeTelemetryTests` 1·`TacticsChangeTests` 3)
- [x] T17 보드·단계·설정 — 파일: `Match/Pipeline/TickBoard.cs`, `Match/Stages/{TacticsStage,PhaseStage,ShapeStage}.cs`, `Match/{MatchSim,MatchSetup,MatchParams}.cs`, `Tactics/{ITeamShapeTelemetry,IShapeDiagnostics}.cs` — 검증: T16 통과, 기존 `Match` 테스트 클래스 회귀 통과 — 결과: T16 통과, 기존 `Match` 67개 회귀 통과. `SimpleSteeringStage`에 배역 건너뛰기 1줄, `MatchTuning.Shape`는 선택 인자라 기존 호출 불변
- [x] T18 전술 변경 입력·진단 — 파일: `Match/Input/ExternalInput.cs`, `Match/Stages/{TacticsStage,ShapeStage}.cs` — 검증: FR-71 변경이 다음 결정 틱 반영·이벤트 기록, 재시뮬 재현 테스트 — 결과: `TacticsChangeTests`·`TacticsChange_ReproducedByFork` 통과
- [x] T19 결정론 테스트 — 파일: `Tests/.../Tactics/ShapeDeterminismTests.cs` (`[Category("Determinism")]`) — 검증: AC-15·16·20, 스냅샷 왕복 — 결과: `ShapeDeterminismTests` 9개 통과
- [x] T20 결정론 리뷰 — `determinism-reviewer`로 `MatchEngine/Runtime/Tactics/**`, 변경된 `Match/**` 리뷰, 지적 반영 — 검증: 차단 0 — 결과: 차단 2건 반영 — B1 `_slotTargets` 스냅샷·해시 누락, B2 전술 입력이 선수 입력으로 오인. 권고 반영: NaN 비용 무한 루프 → 예외(R2), 짧은 재매칭 주기(R3), 슬롯보다 많은 선수·`reduced` 검증(R4), 하프 킥오프 프로필(R5), `_hist` 초기화(R6), 세트피스 배역 제외(R8), 맨투맨 검증(R9), 지표를 보드 출력에서 분리(R10), NaN 포메이션 입력(R11), `RoleId` 해시, 수치 3개 튜닝 이동(R13 일부), Fork 텔레메트리 끔(R1). 회귀 `ShapeReviewRegressionTests` 11개 통과(전체 96개). 보류 권고: R7 GK 퇴장 처리(012), R12 진단 읽기 전용 인터페이스, R1 제공자 스냅샷 계약(013 연결 때), R13의 코너 좌표(규칙 기하값)

## G. 성능
- [x] T21 성능 테스트·예산 — 파일: `Tests/Performance/MatchEngine/ShapePerfTests.cs`, `Data/perf/009-team-shape-tactics.json`, `Verification/Runtime/Bench/` — 검증: Mono 측정·틱당 할당 0. 벤치 워크로드 스모크 — 결과: Mono: 틱 평균 10.3 µs·p95 16.8 µs·재매칭 p95 6.2 µs·할당 0회. IL2CPP 판정은 T23. `ShapeBenchWorkloads.cs`로 분리

## H. 마무리
- [x] T22 전체 테스트 — EditMode 전체 실패 0, 콘솔 에러·경고 0, 컴파일 경고 신규 0 (이 스펙의 1회) — 결과: 수정 전 1003개 중 실패 3개(코어 금지 API, 새 이벤트 3종 애니메이션 매핑) 수정. 커밋 58fda98 깨끗한 트리에서 통과 1000·실패 0·건너뜀 3(000 셀프테스트 전용), 컴파일 경고 0
- [x] T23 `/verify 009` — `farm.py logic --spec 009` + `farm.py bench --spec 009`(IL2CPP 판정) 리포트, 성능 기준 채택은 사용자 승인, 리포트 아티팩트 발행. 상태 "구현 완료 · AC-18·AC-19 보류", README 갱신 — 결과: 깨끗한 트리 `logic` 99개 통과, IL2CPP 벤치 4/4(평균 2.43 µs·p95 4.7 µs·재매칭 3.7 µs·경기 0.137 s), 판정 보류 2건(AC-18·19). 성능 기준 채택(사용자 승인 2026-11-09). 리포트 https://claude.ai/artifact/14T18YAaEF1MDF2jGYiLbo

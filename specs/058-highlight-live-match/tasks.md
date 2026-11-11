# 058 — 하이라이트 실경기 연결 · 태스크

상태: 구현 완료 · AC-15 보류 (2026-11-11, 리포트 https://claude.ai/artifact/Ch7RzEDDcRXf5kEakG4Awt). 대체 판단(008·010)·대체 심판(012) 위 — 연결 때 재확인: 평균 득점(AC-15), 파울·선방 이벤트 장면, 오프사이드 골 취소
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. Unity 배치 1회가 길어서 테스트를 먼저 쓰는 태스크는 같은 블록의 구현과 한 배치로 돌린다.
중간 검증은 `.claude/scripts/unity-test.sh EditMode <클래스 필터>`와 `farm.py shot --scenario <id>`(mac-m1-1600p 1프로필)로 좁힌다. EditMode 전체·`bench`·`/verify`는 마지막 태스크에서 각 1회 (`.claude/rules/05-sdd.md` "검증 횟수").

경로 접두: `MatchEngine/Runtime/Match/` = `M/`, `Presentation/Runtime/Highlight/` = `H/`, `Tests/EditMode/` = `T/`.

## A. 기반
- [x] T01 조정 값·이벤트 종류 — 파일: `Data/tuning/match.json`, `Data/tuning/highlight.json`, `M/{MatchParams,MatchTuningIds}.cs`, `M/Events/SimEventKind.cs` — 검증: 컴파일, `TuningLoader` 파일 기본값 = 코드 기본값 테스트 필터 — 결과: 컴파일 통과, `AnimationMapTests` 통과(새 이벤트 5개 `043-anim-map.json`에 none 매핑 추가). match 튜닝에는 코드 기본값이 없어 파일 로드로 확인. 플랜 외 값 1개 `deadball.walkOffMargin` 1.5 m(걸어 나감 완료 지점, [제안])
- [x] T02 순간이동 감사 도구 + 현재 상태 측정 테스트(실패 기대) — 파일: `T/MatchEngine/Flow/TeleportAuditTests.cs`(100 Hz 원시 궤적 싱크, 예외: 경기·하프 시작) — 검증: 현재 코드에서 골·퇴장 순간이동이 검출되어 실패하는지 확인(테스트가 실제로 잡는지) — 결과: 예상대로 실패(참조 경기 틱 289 골 후 22명 2~10 m 이동 검출). 서브스텝 사이(앞 post → pre)도 검사. 20경기 테스트는 `Slow`(≈ 2.5분)

## B. 골 후 데드볼 (코어)
- [x] T03 킥오프 배치 계산 분리 — 파일: `M/Flow/KickoffLayout.cs`, `M/Flow/MatchFlow.cs` — 검증: 기존 006 킥오프 테스트 필터 불변(경기·하프 시작 배치 비트 동일) — 결과: `MatchFlowTests`·`MatchLoopTests`·`MatchDeterminismTests`·`ReconstructionTests` 통과(경기·하프 시작 배치 동일). `KickoffLayout.Violates` 규칙 판정 함께 추가
- [x] T04 골 데드볼 테스트 — 파일: `T/MatchEngine/Flow/DeadBallFlowTests.cs` — 검증: AC-02·03, FR-60(기간 끝 대기)·FR-06(골 취소)·FR-64(승부차기 제외)·FR-62 경계 — 실패 확인 — 결과: 8개 작성(세리머니 길이·재배치 1회·도착 후 준비·실제 길이 정지·세리머니 중 기간 종료 없음·상한+위반 0·골 취소 → 재시작). FR-64(승부차기)는 동기 실행이라 데드볼 진행 경로가 없어 `ShootoutTests` 회귀로 확인
- [x] T05 데드볼 진행기(골) 구현 — 파일: `M/Flow/DeadBallFlow.cs`, `M/Flow/{MatchFlow,StoppageClock}.cs`, `M/Stages/StandIn/SimpleDecisionStage.cs` — 검증: `DeadBallFlowTests` 통과 — 결과: `DeadBallFlowTests` 8/8, 006 회귀 62개 통과. `StoppageTests` 2개 기대값 갱신: 마지막 서브스텝 골은 058 FR-60에 따라 세리머니 뒤 휘슬, 페널티 유예는 골 정지가 실제 길이라 추가시간이 0이 아니어서 기준 경기 종료 틱 −4에 페널티. `ScenarioRuntime` 골 데드볼 중 세트피스 선택 보류(준비 단계에서 선택, 플랜 파일 목록 외 1곳)
- [x] T05a 시나리오 런 배역 칸 초기화 — 파일: `MatchEngine/Runtime/Scenarios/ScenarioRuntime.cs` — 검증: `ScenarioDeterminismTests.Snapshot_RoundTrip_WhileScenarioRunning` — 결과: 통과. 013 잠복 버그: 새 런이 안 쓰는 배역 칸에 이전 런 값을 남겨 스냅샷 복원본과 상태 해시만 달라짐(이벤트 로그는 같음). 058로 경기 흐름이 바뀌며 드러나 이번에 수정 (사유: 발견 버그는 이번에 수정)
- [x] T06 스냅샷·체크포인트 형식 v2 + 결정론 테스트 — 파일: `M/MatchSim.cs`, `M/Replay/StateCheckpoints.cs`, `T/MatchEngine/Flow/DeadBallDeterminismTests.cs` — 검증: AC-10 상태 해시·재구성 클립(골 구간), 기존 결정론 테스트 필터(기준 해시 갱신은 이유 기록) — 결과: `MatchSnapshot.FormatVersion` 2. `DeadBallDeterminismTests` 3개 + 스냅샷·재구성·키 모먼트 회귀 26개 통과. 장면 목록·카메라 시퀀스 해시는 T16·T17에서 같은 클래스 대신 표현 테스트에 둔다. 기존 결정론 테스트에 고정 해시 상수가 없어 갱신할 값 없음

## C. 재시작 · 출입 (코어)
- [x] T07 재시작 이동 테스트 — 파일: `T/MatchEngine/Flow/RestartMoveTests.cs` — 검증: AC-04 6종(프리킥·페널티·드롭볼은 `RefereeScript` 주입), FR-08 공 재배치 1회 — 실패 확인 — 결과: 6종 테스트 작성(프리킥은 골 취소 → 재시작, 페널티·드롭볼은 스크립트 주입)
- [x] T08 재시작 접근·공 재배치 구현 — 파일: `M/Flow/{DeadBallFlow,MatchFlow}.cs`, `M/Stages/StandIn/SimpleDecisionStage.cs` — 검증: `RestartMoveTests` 통과 — 결과: `RestartMoveTests` 6/6, MatchEngine 전체(Slow 제외) 412개 통과. 공 재배치: 멈춤·경기장 밖 3 m·대기 상한 2 s 중 먼저. 준비 시각 전 정지 추가는 `_pendingDelay`로 모아 준비 때 더함. 시나리오 세트피스 선택은 준비 단계(`DeadBallFlow.Pending` 해제)에서
- [x] T09 걸어 나감·교체 테스트 — 파일: `T/MatchEngine/Flow/OffPitchWalkersTests.cs` — 검증: FR-61·65, AC-17 코어 부분(60분 투입·75분 아웃 순간이동 0, 이벤트 `PlayerEntered`·`PlayerLeftPitch`) — 실패 확인 — 결과: 4개 작성(60분 투입·75분 아웃 걸어서, 교대 전 프레임 id 유지, 퇴장 걸어 나감, 걷는 중 스냅샷 복원)
- [x] T10 걸어 나감·교체 구현 — 파일: `M/Flow/{OffPitchWalkers,Substitutions,MatchFlow}.cs`, `M/Stages/PhysicsStage.cs`, `M/Stages/StandIn/SimpleDecisionStage.cs` — 검증: `OffPitchWalkersTests`·`TeleportAuditTests`(AC-01 20경기) 통과 — 결과: `OffPitchWalkersTests` 4/4, MatchEngine 전체(Slow 포함) 417개 중 416 → `ScenarioRuntimeTests.OptionalCastLost_Continues` 기대값 갱신(교체는 터치라인 교대라 즉시가 아님, 걷기 시작하면 배역 해제). **AC-01 20경기 순간이동 0건 통과**. `MatchContext.IsSlotControllable` 추가, `SubstitutionTests.InjuryWithBench_AutoReplaced`는 걸어 나간 뒤 교대로 기대값 갱신
- [x] T11 결정론 리뷰 — `determinism-reviewer`로 B·C 변경 리뷰, 지적 반영 — 검증: 리뷰 결과 줄 기록, `DeadBallDeterminismTests` 재통과 — 결과: 결정론 위반 0. 진행 멈춤 2건(H1 재시작 키커 이탈·H2 골 데드볼 키커 퇴장) → 키커 재선택(`ReselectTakerIfLost`)·키커 없으면 준비 안 함. M1 골 데드볼 중 정지 이중 계산 → 진행 중 골 정지가 있으면 지연만. M2 강제 세트피스 ZeroTick → Pending 중 시작 안 함. M3 종료 때 걷던 교체 → `FlushAtBreak`. M4 걷는 선수 배역·대상 제외. L1 재조향 지점 비교, L2 `walkOffMargin` 상한 2.5 m, L5·L6 반영. L3(이름 붙은 기하 상수)·L4(End 이벤트)는 유지. 회귀 테스트 `DeadBallReviewRegressionTests` 4개 통과

## D. 기록 정합 (코어 · 표현 진단)
- [x] T12 클립 정합 테스트(+ 필요 시 수정) — 파일: `T/MatchEngine/Replay/ClipAlignmentTests.cs` (수정 시 `M/MatchSim.cs`·`H/Logic/SceneClipBuilder.cs`) — 검증: AC-16 — 결과: 통과. 공 재배치는 선수를 옮기지 않으므로 선수 불연속 표시에서 제외(처음엔 재배치 프레임의 판단 원인 가속을 배치로 잘못 분류). 판단 원인 가속 초과 건수는 테스트 출력으로 리포트용 기록
- [x] T13 속도 점프 원인 분류 — 파일: `H/Logic/JumpClassifier.cs`, `Presentation/Runtime/Players/Logic/LocomotionBlend.cs`, `T/Presentation/Highlight/JumpClassifierTests.cs` — 검증: FR-12 배치·기록·판단 분류 단위 테스트 — 결과: `JumpClassifierTests` 4/4. 원인은 `HighlightData.Break`(클립 변환이 표시)로 분류, 경고 끝에 `원인=배치|기록|판단`. 판단 원인의 원시 100 Hz 대조는 T12(AC-16)가 맡음

## E. 장면 선정 1단계 (표현 로직)
- [x] T14 선정기 테스트 — 파일: `T/Presentation/Highlight/Selection/LiveSceneSelectorTests.cs` — 검증: AC-07·08, FR-32·45·47·48(AC-17 장면 부분)·FR-62, 재배치 시각 제외(AC-02) — 실패 확인 — 결과: 13개 작성(실경기 4 + 합성 8 + 기본값=파일)
- [x] T15 점수표·선정기 구현 — 파일: `H/Selection/{SceneScoreTable,LiveSceneSelector}.cs` — 검증: `LiveSceneSelectorTests` 통과 — 결과: `LiveSceneSelectorTests` 13/13. 채움 후보 점수 5(`highlight.score.filler`, 플랜 값 추가 — FR-42 하한용). 골 꼬리는 [킥 −5.2 s, 킥 −0.2 s], 재배치 경계는 반 틱 여유
- [x] T15a VAR 번복 장면 (FR-40 = 018 FR-01 표) — 파일: `H/Selection/{LiveSceneSelector,SceneScoreTable}.cs`, `Data/tuning/highlight.json` — 검증: `LiveSceneSelectorTests.VarOverturnedGoal_IsVarScene_NotGoal` — 결과: 통과(선정·브리프·골 시퀀스 20/20). 사용자 질문("VAR도 들어가지")으로 확인하니 빠져 있었음: VAR로 취소된 골은 골 장면이 아니라 VAR 번복 장면(점수 70, 필수, 골 순간~번복 +3 s, 리플레이 2). 묶음 대표는 필수 후보 우선
- [x] T16 실경기 브리프 — 파일: `H/Selection/LiveSceneBriefs.cs`, `T/Presentation/Highlight/Selection/LiveSceneBriefsTests.cs` — 검증: 골 그룹 역할·데드볼 창·내 선수 미출전 시 방송 모드(FR-24·48 ③) — 결과: `LiveSceneBriefsTests` 3/3. 선수 id → 슬롯 변환은 주입(교체로 슬롯이 바뀜). `SceneBrief.EndWide` 추가

## F. 카메라 · 골 시퀀스 (표현)
- [x] T17 골 시퀀스 메인 샷 유지·연속 골·종료 와이드 + 그물 큐 — 파일: `H/Broadcast/Logic/GoalSequenceDirector.cs`, `H/Logic/ClipConverter.cs`, `T/Presentation/Highlight/Broadcast/LiveGoalSequenceTests.cs` — 검증: AC-05(실경기 골·실점), FR-22·60·62, 042 `GoalSequenceDirectorTests` 회귀 필터 — 결과: `LiveGoalSequenceTests` 4/4(양 팀 골 순서·꼬리·킥오프 와이드 공 위치·같은 시드 카메라 해시), 042 `DirectionTests.GoalSequence_Durations` 기대값 갱신(+7 s·메인 유지). 그물 접촉은 시뮬 이벤트가 없어 클립 공 궤적에서 계산(`NetContact`, 큐 종류 10211). 메인 유지 값 3개 `camera.json`
- [x] T18 카메라 연속성 검사기 — 파일: `H/Broadcast/Logic/CameraContinuity.cs`, `T/Presentation/Highlight/Broadcast/CameraContinuityTests.cs` — 검증: AC-06(합성 튐 검출 + 고정 시드 실경기 전 장면), 넘으면 `FramingSolver`·`CameraTrackBuilder` 조정(결과 줄에 기록) — 결과: `CameraContinuityTests` 3/3 + 실경기 전 장면 위반 0. 처음엔 리플레이 슈터 앵글이 ~100°/s → `CameraTrackBuilder`에 샷 안 회전·화각 제한기(이산 제동 곡선, 리플레이는 0.5x 환산) 추가, FR-33 공 화면 밖 진단 횟수 기록. 상한 값은 `highlight.json`에서 `camera.json`으로 옮김

## G. 동작 선택 (표현)
- [x] T19 태클 동작 거리 조건 — 파일: `Presentation/Runtime/Players/Logic/{TackleGate,AnimPlan}.cs`, `T/Presentation/Players/TackleGateTests.cs` — 검증: AC-09, FR-52 대체·진단 — 결과: `TackleGateTests` 4/4. 대체 동작 진단은 콘솔 경고가 아닌 Info 로그(콘솔 경고 0 기준)

## H. 실경기 재생 연결 · E2E
- [x] T20 실경기 재생을 중계 카메라로 — 파일: `Verification/Runtime/Samples/Highlight/{HighlightScreen,HighlightStandIns,LiveMatchFixture}.cs` — 검증: `StandInCamera`·`CutAtTeleport` 제거, 클립 중 선수 id 변경(교대) 표시 확인, `farm.py shot --scenario 041-highlight-playback` 1장 육안 — 결과: `highlightPlay`가 `LiveSceneSelector` + `BroadcastCamera`로 재생, `StandInCamera`·`StandInSceneSelector`(순간이동 자르기) 삭제. `farm.py shot --scenario 041-highlight-playback` 통과·4장 직접 확인. `freeze.afterTransition`은 리플레이 진입 블렌드(042 FR-14) 첫 프레임이라 0.2 s 뒤로 옮김. 고정 경기는 `LiveMatchFixture`(60분 원정 퇴장 주입, 내 선수 id 10)
- [x] T21 E2E 시나리오 — 파일: `Data/e2e/{058-live-goal-sequence,058-live-variety,058-perf}.json`, `HighlightScreen` 화면 등록(`live.freeze.*`) — 검증: `farm.py shot --scenario 058-live-goal-sequence`, `--scenario 058-live-variety` 각 1회, 스크린샷 Read로 직접 확인 — 결과: `058-live-goal-sequence` 통과·5장 직접 확인(그물 큐 직후 메인 유지·클로즈업·세리머니·리플레이 ①·킥오프 와이드 진영 ok). `058-live-variety` 통과·4장 직접 확인(실점·박스 안 슈팅(골대 대체)·태클 경합·퇴장 장면, HUD 번호 일치). 처음엔 고정 경기가 22골이라 FR-42대로 골만 선정 → 대체 판단 평균 득점이 058 전부터 약 24골(main 24.5, 브랜치 23.3, 30경기)임을 확인, 2026-11-11 사용자 결정 "AC-15 보류 + E2E 경기만 저득점"으로 `LiveMatchFixture`만 `standin.shotProb` 0.05(3:3). 게임 튜닝은 그대로
- [x] T22 로그 무시 항목 정리 — 파일: `Data/e2e/log-ignore.json` — 검증: 순간이동 원인 항목 제거·판단 원인만 유지(사유 갱신), AC-13 집계 — 결과: 속도 점프 무시 패턴을 `원인=판단`만으로 좁힘(사유 갱신). 다양성 재생 로그: 판단 원인 8,505건, 배치·기록 원인 0건(AC-13)

## I. 마무리
- [x] T23 대량 시뮬·성능 테스트 — 파일: `T/MatchEngine/Flow/MassSimDeadBallTests.cs`, `Tests/Performance/DeadBallPerfTests.cs`, IL2CPP 벤치 항목 `Perf_LiveSceneSelector` — 검증: AC-15(200경기, 결과 `evidence/`), 틱 GC 0 B — 결과: `DeadBallPerfTests` 2/2(골 데드볼 1,250틱 GC 0회, 장면 선정 Mono 기준선 통과 — IL2CPP 판정은 T25 벤치). 대량 시뮬 200경기: 평균 23.34골(058 전 24.5 이하), 골 후 데드볼 25.9 s, 재시작 대기 2.6 s → `evidence/mass-sim.json`. **AC-15 보류**(사용자 결정). 200경기 ≈ 23분이라 `Explicit`(수동)으로 전체 실행에서 제외. IL2CPP 벤치 항목 `Perf_LiveSceneSelector`는 T25에서 추가
- [x] T24 수용 기준 전체 테스트 통과 — EditMode 전체 1회(≈ 13분), 실패 0 — 결과: EditMode 전체 1회 1,290개 중 통과 1,285·실패 1·건너뜀 4(기존 3 + 대량 시뮬 Explicit). 실패 `ScenarioSetPieceTests.CornerSequence_LogOrder_NonCastUntouched`는 세트피스 시작이 준비 단계로 바뀐 것에 맞춰 대기 틱 갱신 → 해당 클래스만 재실행 15/15 통과(전체 재실행 안 함)
- [x] T25 `/verify 058` — `bench` 1회, 058·041·042 장면 스크린샷(mac-m1-1600p) 사용자 육안 승인, 리포트 아티팩트 발행 — 결과: 로직 59/59(결정론 6/6), IL2CPP 벤치 2/2(틱 평균 26.02 µs ≤ 26.36, 장면 선정 0.53 ms ≤ 5), E2E 3시나리오 통과·스크린샷 9장 육안 승인(evidence/approval.md), 리포트 https://claude.ai/artifact/Ch7RzEDDcRXf5kEakG4Awt. 벤치 058 항목 추가(`MatchBenchWorkloads.RunLiveHighlight`, `Data/perf/058-*.json`). 결정론 카테고리는 리포트 도구가 메서드 단위로 읽어 메서드에 붙임

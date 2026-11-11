# 058 — 하이라이트 실경기 연결 · 플랜

상태: 구현 완료 · AC-15 보류 (2026-11-11, 리포트 https://claude.ai/artifact/Ch7RzEDDcRXf5kEakG4Awt). 대체 판단(008·010)·대체 심판(012) 위 — 연결 때 재확인: 평균 득점(AC-15), 파울·선방 이벤트 장면, 오프사이드 골 취소
스펙: ./spec.md

## 접근
세 층으로 나눈다. ① **시뮬 코어(MatchEngine)**: 지금 `MatchFlow.OnGoal`이 골 순간 `PlaceKickoff`로 22명을 킥오프 배치에 `Place`(순간이동)하고, `BeginRestart`가 공을 재시작 지점에 즉시 옮기고, `RemoveFromPitch`가 퇴장·부상 선수를 피치 밖으로 옮긴다. 이 셋을 **데드볼 진행기**(`DeadBallFlow`)로 바꾼다. 데드볼 진행기는 단계(세리머니 → 복귀 → 준비 / 재시작 접근 → 준비)와 선수별 목표 지점·속도를 정하고, 판단 단계(대체)는 그 목표를 `Arrive` 명령으로 넘기기만 한다. 그래서 010이 대체 판단을 바꿔도 데드볼 이동 규칙은 코어에 남는다(스펙 Q3). 공 재배치는 한 번만, `BallRelocated` 이벤트로 시각을 남긴다. ② **장면 선정 1단계**(Presentation 순수 로직): 이벤트 로그 → 점수표(FR-40~45) → 병합·구간·골 그룹(메인 + 리플레이 3 + 꼬리)·`SceneBrief`를 만든다. Verification의 `StandInSceneSelector`(골·슈팅만, 순간이동 앞에서 자르기)를 대체한다. ③ **표현 연결**: 실제 경기 재생을 042 `BroadcastCamera`로 돌리고(`StandInCamera` 경로 제거), 카메라 연속성 검사기·속도 점프 원인 분류·태클 동작 거리 조건을 넣는다.
대안: 골 후 흐름을 표현 쪽에서 합성(지금 `BroadcastScenes` 방식)하면 코어는 안 바뀌지만 리플레이·키 모먼트 재시뮬·추가시간과 어긋난다 — 스펙 Q2에서 코어로 정했다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `Match/Flow/DeadBallFlow.cs` (신규) | 데드볼 단계 기계: `GoalPhase {None, Celebration, Return, Ready}`, 재시작 `Approach`, 선수별 목표·속도(`TargetOf(slot)`, `SpeedOf(slot)`), 도착 판정(FR-02·03·09), 공 재배치 시각 결정(FR-04·08), 직렬화·해시 |
| RoadToPro.MatchEngine | `Match/Flow/KickoffLayout.cs` (신규) | `PlaceKickoff`의 배치 계산을 꺼낸 순수 함수 `Compute(ctx, kickTeam, Span<Vec2>)` + 킥오프 규칙 검사(`Violates(slot)`: 센터서클 안·상대 진영). 경기·하프 시작은 계속 `Place`(FR-10), 골 후는 목표로만 쓴다 |
| RoadToPro.MatchEngine | `Match/Flow/OffPitchWalkers.cs` (신규) | 걸어 나가는/들어오는 선수 목록(퇴장·부상 아웃·교체 아웃·교체 투입, FR-61·65). 가까운 터치라인 출구 지점, 도착 시 교대·비활성 |
| RoadToPro.MatchEngine | `Match/Flow/MatchFlow.cs` (수정) | `OnGoal` → `DeadBallFlow.BeginGoal`(Place 제거), `BeginRestart` 공 즉시 이동 제거, `RemoveFromPitch` → 걸어 나감, 킥오프 시 골 정지 시간 = 실제 길이(FR-05), 골 데드볼 중 기간 끝은 진행 중 정지 시간 포함 판정(FR-60), 골 취소 시 재시작 전환(FR-06) |
| RoadToPro.MatchEngine | `Match/Flow/StoppageClock.cs` (수정) | 진행 중 골 정지(시작 틱) — `TotalTicksAt(tick)` |
| RoadToPro.MatchEngine | `Match/Flow/Substitutions.cs` (수정) | 교체를 즉시 교대 대신 `OffPitchWalkers`에 넘김: 아웃 선수가 터치라인 도착 → 그 지점에서 교대 → 투입 선수가 자리로 걸어감. `Substitution` 이벤트는 교대 순간 |
| RoadToPro.MatchEngine | `Match/Stages/StandIn/SimpleDecisionStage.cs` (수정) | 데드볼 분기: `DeadBallFlow` 목표가 있으면 `MoveTo(목표, 속도)`, 세리머니 단계 득점자는 코너 플래그 쪽 조깅·피치 밖 3 m 제한(FR-63), 나머지는 감속(`Stand`). 걸어 나가는 선수는 출구로 |
| RoadToPro.MatchEngine | `Match/Stages/PhysicsStage.cs` (수정) | 걸어 나가는 선수는 공 잡기·분리 대상에서 제외 |
| RoadToPro.MatchEngine | `Match/Events/SimEventKind.cs` (수정) | `BallRelocated`(A = 사유: 골·재시작), `DeadBallPhase`(A = 단계), `RestartHeld`(진단: 킥오프 규칙 위반자, FR-03), `PlayerLeftPitch`·`PlayerEntered`(FR-48·61·65) |
| RoadToPro.MatchEngine | `Match/MatchParams.cs`, `MatchTuningIds.cs`, `Data/tuning/match.json` | 새 조정 값(아래 데이터) |
| RoadToPro.MatchEngine | `Match/MatchSim.cs`, `Replay/StateCheckpoints.cs` | 스냅샷·체크포인트 형식 버전 1 → 2 (DeadBallFlow·OffPitchWalkers 상태) |
| RoadToPro.Presentation | `Highlight/Selection/LiveSceneSelector.cs` (신규, 순수 로직) | 이벤트 로그 → 후보·점수(FR-40·41) → 병합(FR-43) → 8~15개 선택(FR-42·45) → 구간 보정(FR-32·47) → 골 그룹·리플레이 앵글 수(FR-21·44) → `HighlightScene[]` + `SceneBrief` 공급 |
| RoadToPro.Presentation | `Highlight/Selection/SceneScoreTable.cs` (신규) | FR-40 대체 점수표·FR-41 보정. 값은 `Data/tuning/highlight.json` |
| RoadToPro.Presentation | `Highlight/Selection/LiveSceneBriefs.cs` (신규) | 골 그룹 브리프(GoalMain·ReplayAngle·GoalTail, 득점자·슈터·내 선수, 데드볼 창 = `DeadBallPhase`·재시작 이벤트, 흔들림 원천 = `CameraShake.FromEvents`), 출전 상태별 모드 대체(FR-24·48 ③) |
| RoadToPro.Presentation | `Highlight/Broadcast/Logic/CameraContinuity.cs` (신규) | 샷 안 각속도·각가속도·화각 변화 측정, 허용 컷 분류(FR-30·31), 공 화면 밖 0.5 s 진단(FR-33) |
| RoadToPro.Presentation | `Highlight/Broadcast/Logic/GoalSequenceDirector.cs` (수정) | 골 시퀀스 맨 앞 메인 샷 유지(059 FR-30·31: 그물 접촉 +1.0 s, 접촉 없음 0.5 s, 골 기준 상한 1.5 s), 연속 골 꼬리 생략(FR-62), 종료 와이드(FR-60) |
| RoadToPro.Presentation | `Highlight/Logic/ClipConverter.cs` (수정) | 그물 큐 = 골라인 통과 뒤 첫 그물 접촉 시각(FR-22). 공 물리의 그물 접촉 이벤트가 없으면 `GoalBox` 기하로 첫 접촉 프레임 |
| RoadToPro.Presentation | `Players/Logic/AnimPlan.cs`, `Players/Logic/TackleGate.cs` (신규) | FR-50·51: 이벤트 순간 상대 볼 캐리어(또는 직전 소유자) 거리 > 2.0 m면 후보에서 `tackle_*` 제외, 남는 후보 없으면 일반 터치 + 진단(FR-52) |
| RoadToPro.Presentation | `Players/Logic/LocomotionBlend.cs`, `Highlight/Logic/JumpClassifier.cs` (신규) | 속도 점프 경고에 원인 태그: 배치(`BallRelocated`·교대·`Place` 프레임) / 기록(프레임 시각 차 ≠ 1/30) / 판단(원시 100 Hz 궤적 가속 ≤ 005 상한이면 표시 쪽, 아니면 판단) (FR-12) |
| RoadToPro.Verification | `Samples/Highlight/HighlightScreen.cs` (수정) | `highlightPlay`를 `LiveSceneSelector` + `BroadcastCamera`로. `StandInCamera`·`StandInSceneSelector.CutAtTeleport` 제거. 058 화면 등록(`live.freeze.*`) |
| RoadToPro.Verification | `Samples/Highlight/HighlightStandIns.cs` (수정) | `StandInSceneSelector` 삭제(대체됨), HUD·오디오 대체는 유지 |
| RoadToPro.Verification | `Samples/Highlight/LiveMatchFixture.cs` (신규) | E2E 고정 시드 경기: 골 양 팀·골대·태클이 나오는 시드 + `RefereeScript` 주입(경고 1장·60분 투입·75분 아웃 — 012·025·050 전 대체) |
| Data | `Data/e2e/058-live-goal-sequence.json`, `058-live-variety.json`, `058-perf.json` | AC-11·12·13·14 |
| Tests | `Tests/EditMode/MatchEngine/Flow/DeadBallFlowTests.cs`, `TeleportAuditTests.cs`, `RestartMoveTests.cs`, `OffPitchWalkersTests.cs`, `ClipAlignmentTests.cs`, `DeadBallDeterminismTests.cs` | AC-01~04·10·15·16·17 |
| Tests | `Tests/EditMode/Presentation/Highlight/Selection/LiveSceneSelectorTests.cs`, `Broadcast/CameraContinuityTests.cs`, `Broadcast/LiveGoalSequenceTests.cs`, `Players/TackleGateTests.cs`, `Highlight/JumpClassifierTests.cs` | AC-05~09·17 |
| Tests | `Tests/Performance/DeadBallPerfTests.cs` | AC-14 GC·선정 시간 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 — 코어 변경은 MatchEngine 안, 선정기는 Presentation(MatchEngine 이벤트를 읽기만), 고정 경기는 Verification.
선정기를 Presentation에 두는 이유: 018 본 구현 위치가 정해지지 않았고 041 `ISceneListSource` 포트가 Presentation에 있다. 018이 오면 같은 포트 뒤로 교체한다.

## 데이터 · 스키마
`Data/tuning/match.json` 새 항목(태그 `[제안]`, 스펙 출처 표기):

| id | 기본 | 출처 |
|---|---|---|
| `deadball.goalCelebration` | 7 s | FR-02 ① (018 연장 +7 s). 060이 같은 값을 읽는다 |
| `deadball.arriveRadius` | 1.0 m | FR-02 ③ |
| `deadball.returnSpeedMin` / `Max` | 0.40 / 0.60 (최고 속도 비) | FR-03. 선수별 값은 슬롯 순서로 범위 안 고정 분포(RNG 안 씀) |
| `deadball.restartArriveRadius` | 3 m | FR-09 |
| `deadball.restartArriveShare` | 0.8 | FR-09 |
| `deadball.ballOutRelocate` | 3 m | FR-08 |
| `deadball.relocateMaxWait` | [미정] → 대체 2 s | FR-08: 공이 3 m 밖으로 안 나가고 계속 굴러갈 때 상한. **플랜이 정한 대체 값** — 승인 때 확인 |
| `standin.goalCelebration` | 40 s (유지) | FR-03 상한으로 의미 변경(골 순간 ~ 킥오프 상한) |
| `standin.restartWaitMax` | [미정] → 대체 30 s | FR-09 상한(012 전). **플랜이 정한 대체 값** |

`Data/tuning/highlight.json` 새 항목: FR-40 점수 10개, FR-41 보정 3개, `sceneMin 8`·`sceneMax 15`, `before 8`·`after 3`·`goalAfter 7`, `subSceneSeconds 6`(FR-48), `consecutiveGoalSeconds 60`(FR-62), `freeKickZone 25`(FR-40), 카메라 상한 60°/s·240°/s²·10°/s, `ballOffscreenDiag 0.5`, `tackleMaxDistance 2.0`.

시뮬 상태: `DeadBallFlow`(단계, 시작 틱, 골 팀, 재배치 예정·완료 플래그, 위반 진단 플래그)·`OffPitchWalkers`(최대 8명: 슬롯, 종류, 출구 지점, 교대 대상 id)가 스냅샷에 들어간다. `MatchSnapshot.FormatVersion` 1 → 2. 저장은 경기 전·후뿐이라 세이브 마이그레이션 없음(체크포인트는 경기 메모리 안에서만 산다). 옛 버전 스냅샷은 기존처럼 형식 오류로 거부.

새 이벤트(MatchEngine `SimEventKind`, 012 대역 끝 412~416): `BallRelocated`, `DeadBallPhase`, `RestartHeld`, `PlayerLeftPitch`, `PlayerEntered`. 이벤트 로그 해시가 바뀌므로 기존 결정론 기준 해시(006·009·013 테스트 상수)는 갱신 — 변경 이유를 커밋에 적는다.

## 결정론 · 성능
- 데드볼 진행기는 `MatchFlow.EndTick`과 판단 단계 사이에서 슬롯 오름차순으로만 돈다. 새 RNG 사용 없음(복귀 속도 분포도 슬롯 순서 함수). 걸어 나가는 선수 목록은 고정 배열 + 삽입 순서.
- 스냅샷·해시에 새 상태 전부 포함 → AC-10 재구성 클립 비교.
- 틱 경로 할당 0: 목표 배열 `Vec2[22]` 재사용, `KickoffLayout.Compute`는 `Span`.
- 선정기는 경기 끝 1회. 이벤트 로그 1회 순회 + 후보 배열 정렬(안정 정렬: 점수 내림차순 → 틱 → Seq, FR-45). 측정: `Perf_LiveSceneSelector`(IL2CPP 벤치 항목 추가) ≤ 5 ms.
- 헤드리스 시뮬 시간 +5%: 기존 006 벤치 항목 비교(`bench` 마지막 1회). 2026-11-12 011 GK 추가로 006 FR-80 몫 1.5 µs를 더해 27.86 µs(사용자 결정, 011 벤치 실측 27.15 µs·Shape 0.14 → 1.09 µs).
- 카메라 연속성 검사는 검증·진단 전용(재생 경로에서 꺼짐).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `TeleportAuditTests.TwentySeeds_NoTeleport` — 고정 시드 20경기, 100 Hz 원시 궤적 싱크로 매 서브스텝 이동 ≤ 최고 속도 × dt + 1 mm, 경기·하프 시작 배치만 예외 | 로직 | — |
| AC-02 | `DeadBallFlowTests.Goal_BallRelocatedOnce_OutsideLiveScenes` (+ `LiveSceneSelectorTests.Relocation_NotInsideLiveScene`) | 로직 | — |
| AC-03 | `DeadBallFlowTests.Goal_CelebrationLength`, `Goal_KickoffAfterArrival`, `Goal_CapReached_NoViolators`, `Goal_StoppageEqualsActual` | 로직 | — |
| AC-04 | `RestartMoveTests.EachRestartKind_NoTeleport_TakerAtBall_ReadyCondition` (스로인·골킥·코너·프리킥·페널티·드롭볼 — 프리킥·페널티·드롭볼은 `RefereeScript` 주입) | 로직 | — |
| AC-05 | `LiveGoalSequenceTests.GoalAndConceded_CameraOrder` (고정 시드 실경기 → 선정기 → `CameraTrackBuilder` 이벤트 순서) | 로직 | — |
| AC-06 | `CameraContinuityTests.FullMatch_WithinLimits_CutsOnlyAllowed` + 단위(합성 튐 검출) | 로직 | — |
| AC-07 | `LiveSceneSelectorTests.AllGoalsIncluded`, `Count8to15`, `GoalsOver15_GoalsOnly`, `GoallessDraw_AtLeast8_TwoKinds` | 로직 | — |
| AC-08 | `LiveSceneSelectorTests.ShotSaveRebound_Merged`, `ReplayAngleCounts` | 로직 | — |
| AC-09 | `TackleGateTests.FarOpponent_NoTackleClip`, `Tackle_NearOnly`, `NoCandidate_FallbackTouch_Diag` | 로직 | — |
| AC-10 | `DeadBallDeterminismTests.SameSeed_StateHash`, `SceneListAndCameraHash`, `CheckpointRebuild_GoalClipEqual` + `determinism-reviewer` | 결정론 | — |
| AC-11 | `e2e/058-live-goal-sequence` (그물 큐 직후·클로즈업·세리머니 중간·리플레이 ①·킥오프 와이드, 여백 5% 라벨, 킥오프 와이드 진영 검사 라벨) | E2E | mac-m1-1600p |
| AC-12 | `e2e/058-live-variety` (실점·골대·수비·파울(주입 경고) 장면, HUD 장면 번호 = 목록) | E2E | mac-m1-1600p |
| AC-13 | `e2e/058-live-variety` 전체 재생 로그 → 원인 태그별 집계(`[anim] 속도 점프 … 원인=배치|기록` 0건, `판단` 건수 리포트) | E2E | mac-m1-1600p |
| AC-14 | `bench`(헤드리스 시뮬 +5%, `Perf_LiveSceneSelector` ≤ 5 ms), `DeadBallPerfTests.TickGc_Zero`, `e2e/058-perf`(041·042 예산) | 성능 | mac-m1 IL2CPP, mac-m1-1600p |
| AC-15 | `MassSimTests.Batch200_GoalsAndDeadBallStats` (평균 득점 2.5~3.0, 골 후 데드볼·재시작 대기 평균을 `evidence/`에 기록) | 로직 | — |
| AC-16 | `ClipAlignmentTests.AllScenes_FrameStep_RawMatch_AccelCause` | 로직 | — |
| AC-17 | `OffPitchWalkersTests.SubIn60_SubOut75_Scenes_NoTeleport`, `NotPlayed_MyPlayerModeFallsBack` | 로직 | — |

E2E 프로필은 2026-11-11 결정에 따라 `mac-m1-1600p` 1개로 읽는다(스펙의 "전체"·"smoke" 표기 포함). 041·042 기존 시나리오는 회귀로 1프로필 스크린샷만.

## 버그 방지
- **골 데드볼 중 기간 끝**(FR-60): 진행 중 정지 시간을 판정에 넣지 않으면 세리머니 도중 하프가 끝난다 → `Goal_AtNominalEnd_PeriodWaitsForStoppage`, `Goal_DuringAddedTime_EndsAfterCelebration`.
- **킥오프 규칙 위반자 무한 대기**: 걸을 수 없는 선수(부상 아웃 중) → 위반 판정에서 걸어 나가는 선수 제외, 테스트 `Goal_LeavingPlayerIgnored`.
- **연속 데드볼**: 골 데드볼 도중 교체·카드·골 취소(FR-06) → 단계 끊기 테스트. 재시작 접근 중 또 재시작(스크립트) → 재배치 플래그 리셋.
- **공 재배치 1회**: 골당·재시작당 1회. 공이 이미 지점에 있으면 이벤트 없이 0회도 허용(AC-02는 "≤ 1회 + 표시").
- **교대 순간 id 변경**: 041 리그가 클립 중 `PlayerIds` 변경을 처리하는지 확인 — 처리 안 하면 T 태스크에서 장면 경계로 자르거나 리그 교체(스펙 범위 안: FR-61 표현). 테스트 `ClipConverter_IdChangeMidClip`.
- **골키퍼 골킥·페널티 키커** 접근 거리 길 때 상한(30 s 대체) 동작.
- **승부차기**: 데드볼 진행기 비활성(FR-64) → `Shootout_NoDeadBallFlow`.
- **0:0·이벤트 적은 경기**: 후보 < 8이면 점수 낮은 후보·패스 흐름까지 채움 — `GoallessDraw_AtLeast8`.
- **장면 2.5 s 미만**(FR-32): 앞뒤 확장 후에도 짧으면 버림 — 경계값 2.49/2.5 테스트.
- **하프 경계 걸침**: 기존 `SceneListNormalizer` 재사용.
- 결정론 기준 해시 상수 갱신은 같은 커밋에서 이유와 함께.

## 위험 · 미정
- **밸런스(AC-15)**: 데드볼이 40 s 고정 → 실제 길이(≈ 7 s + 복귀 15~25 s)로 바뀌면 실경기 시간이 늘어 득점이 달라질 수 있다. 현재 대체 판단의 200경기 평균이 이미 2.5~3.0 밖이면 이 스펙으로 고칠 수 없다(008·010·017 몫) → 그때 멈추고 보고.
- **교체 표현**: 아웃 선수와 투입 선수가 동시에 보이지 않는다(터치라인에서 교대). 리플레이 프레임 엔티티 수(26)를 늘리지 않기 위한 선택. 둘이 함께 보여야 하면 006 프레임 형식 변경이 필요 — 스펙 수정 사항.
- **파울·선방 이벤트 부재**: 012·011이 없어서 실제 시뮬은 `Foul`·`Save`를 내지 않는다. AC-07 "2종류"는 빗나간 슈팅·수비 액션으로 충족하고, AC-12 파울 장면은 고정 경기의 `RefereeScript` 주입 경고(`Card`)로 대체한다. 점수표 항목은 이벤트가 생기면 그대로 동작.
- **그물 접촉 시각**: 공 물리(004)가 그물 접촉 사건을 내는지 T 태스크에서 확인. 없으면 표현 쪽 기하 계산(FR-22 "059 전에는 큐만").
- 플랜이 정한 대체 값 2개(`relocateMaxWait` 2 s, `restartWaitMax` 30 s)는 012 전 임시 — 승인 때 확인.
- 018 FR-01·FR-15, 042 FR-20 +7 s 반영(스펙 "후속")은 이 스펙에서 코드 상수만 맞추고 두 스펙 문서 갱신은 하지 않는다.

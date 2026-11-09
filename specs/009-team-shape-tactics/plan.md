# 009 — 팀 형태 · 전술 · 국면 FSM · 플랜

상태: 구현 완료 · AC-18·AC-19 보류 (2026-11-09)
스펙: ./spec.md

## 접근
006 파이프라인의 빈 자리 세 개를 채운다: 단계 0 `TacticsStage`(지시 정리·변경 적용), 단계 1 `PhaseStage`(점유 추적·국면 FSM), 단계 2 `ShapeStage`(블록 중심 → 슬롯 목표 → 라인 → 재매칭 → 존·압박 지정). 계산은 전부 **팀 관점 정규화 좌표**에서 하고 마지막에 `FromTeamPerspective`로 미터로 바꾼다. 그래서 하프 반전·양 팀 대칭(FR-01, AC-16)이 관점 변환 한 곳에만 의존한다.

계산 핵심은 `ShapeSolver`·`PhaseMachine`·`DefensiveLine`·`SlotMatcher`·`PressPlanner`로 나눈 정적/할당 없는 부품이다. 단계 클래스는 입력을 모아 부품을 부르고 보드에 쓰기만 한다. 부품 단위로 테스트하므로 경기 전체를 돌리지 않아도 AC 대부분을 검증한다.

아직 없는 스펙의 입력은 주입 인터페이스 5개로 받고 대체 구현을 둔다(FR-02, 2026-11-09 결정). 재시작 종류·위치는 006 `MatchFlow`에 이미 있어 그대로 읽는다.

검토한 대안: (1) 슬롯 목표를 재매칭 주기(0.5 s)마다만 계산 — 3 µs는 쉬워지지만 라인·블록이 공을 0.5 s 늦게 따라가 FR-42 이동식이 무의미해져서 버렸다. (2) 탐욕 매칭 — 빠르지만 비용 동점·교차 상황에서 최적이 아니고 FR-51 근거(Bialkowski 이분 매칭)와 달라서 헝가리안(10×10, 사전 할당)을 쓴다.

## 모듈 · 파일
모두 `RoadToPro.MatchEngine` (네임스페이스 `RoadToPro.MatchEngine.Tactics`). 경로 접두어 `Assets/_Project/MatchEngine/Runtime/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| MatchEngine | Tactics/Data/FormationDefinition.cs | 슬롯(id, 기본 포지션, 프로필 6개의 앵커 오프셋, 존 가중치, 기본 역할 id, 라인 슬롯 여부), 인원 감소 변형 (FR-10) |
| MatchEngine | Tactics/Data/FormationCatalog.cs | JSON 문자열 파싱·검증 (FR-12, 실패 시 `FormationLoadException`). 파일 읽기는 호출자 (TuningLoader와 같은 방식) |
| MatchEngine | Tactics/Data/ShapeTuningIds.cs, ShapeParams.cs | 튜닝 id와 불변 파라미터. 블록·국면 표를 배열로 펼쳐 틱 경로에서 사전 조회 없음 |
| MatchEngine | Tactics/TeamInstructions.cs | 지시 10종 + `MarkingScheme` + 맨투맨 목록, `Sanitize`(FR-70, AC-14) |
| MatchEngine | Tactics/TeamTactics.cs | 포메이션 id + 지시. `TeamSetup.Tactics`로 받음 |
| MatchEngine | Tactics/TeamPhase.cs | enum: BuildUp, Progression, FinalThird, AttTransition, DefTransition, DefHigh, DefMid, DefLow, SetPiece |
| MatchEngine | Tactics/PossessionTracker.cs | 점유 팀·확정 여부·탈취/상실 시각·루즈볼 시간 (FR-22) |
| MatchEngine | Tactics/PhaseMachine.cs | FSM + 히스테리시스 + 최소 체류 (FR-20~24) |
| MatchEngine | Tactics/ShapeSolver.cs | 블록 중심, Lt·Wt, 슬롯 목표, 투영, 대인·피치 클램프 (FR-30~36, 52) |
| MatchEngine | Tactics/AnchorBlend.cs | 국면 전환 앵커 보간 (FR-25, 71) |
| MatchEngine | Tactics/DefensiveLine.cs | 라인 이동, drop/step-up, 일체성 오차, 오프사이드 라인 (FR-40~45) |
| MatchEngine | Tactics/SlotMatcher.cs | 헝가리안 10×10, 고정 동점 규칙 (FR-51, 91) |
| MatchEngine | Tactics/ZoneResponder.cs | 가중 최근접으로 1순위 대응자 (FR-50) |
| MatchEngine | Tactics/PressPlanner.cs, PressureRules.cs | 트리거 점수·압박 지정 (FR-60~61), FIFA 압박 판정 (FR-62) |
| MatchEngine | Tactics/SetPieceLayout.cs | 재시작별 기본 배치 (FR-80~81) |
| MatchEngine | Tactics/ShapeInputs.cs | 주입 인터페이스: `IPlayerShapeTraits`(002), `IRoleShapeSource`(003), `IPitchControlQuery`(007), `IScenarioCastSource`(013), `IPressSignalSource`(008). 대체 구현 포함 |
| MatchEngine | Tactics/IShapeDiagnostics.cs, ITeamShapeTelemetry.cs | FR-95~96. null이면 호출 0 |
| MatchEngine | Match/Stages/TacticsStage.cs, PhaseStage.cs, ShapeStage.cs | 단계 0·1·2. 내부 상태 직렬화·해시 (006 FR-60) |
| MatchEngine | Match/Pipeline/TickBoard.cs (수정) | Tactics·Phase 슬롯 접근자, 팀별 `TeamShapeOutput`(국면·라인·오프사이드·존·압박·지표), 슬롯별 형태 목표 유무 (FR-03, 90) |
| MatchEngine | Match/MatchSim.cs, MatchSetup.cs, MatchParams.cs (수정) | `DefaultStages(setup)` 분기, `TeamSetup.Tactics`, `MatchTuning.Shape`, `MatchSimOptions.ShapeInputs` |
| MatchEngine | Match/Input/ExternalInput.cs (수정) | 전술 변경 입력 종류 추가 (FR-71, 재시뮬·리플레이 재현) |
| MatchEngine | Match/Events/SimEventKind.cs (수정) | 009 범위 추가: `TacticsChanged`=102, `TacticsInputClamped`=103, `ShapeCastOverflow`=104. `PhaseChange`=101은 기존 값 사용. `PossessionChange`는 PhysicsStage가 이미 내므로 중복 발행하지 않음 |
| Core | Core/Runtime/Random/RngStreamId.cs (수정) | `TeamShape = 9` 추가. 기존 번호 불변 |
| 데이터 | Data/tactics/formations.json | 기본 포메이션 6종 |
| 데이터 | Data/tuning/shape.json | 블록 표·국면 값·라인·압박·트리거 가중치 (아래 표) |
| 데이터 | Data/perf/009-team-shape-tactics.json | 성능 예산 사본, deferred: AC-18·AC-19 |
| Verification | Verification/Runtime/Bench/ (수정) | `--sim-bench 009` 워크로드. 시계 API는 코어 밖 |
| Tests.EditMode | Tests/EditMode/MatchEngine/Tactics/*Tests.cs, ShapeTestKit.cs | 로직·결정론 |
| Tests.Performance | Tests/Performance/MatchEngine/ShapePerfTests.cs | Mono 회귀 기준선 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 (`MatchEngine → Core`, `Verification → MatchEngine`).

**기존 경기와의 연결**: `TeamSetup.Tactics`가 두 팀 모두 있으면 009 단계 3개를 쓰고, 없으면 지금의 대체 단계(`HoldShapeStage`)를 그대로 쓴다. 006·054 테스트와 벤치의 동작이 바뀌지 않는다. `RoleAdjust`(단계 3)는 NoOp으로 둔다 — 역할 오프셋은 투영 전에 들어가야 해서(FR-31·33) ShapeStage 안에서 `IRoleShapeSource`로 적용한다. `TeamSetup.Formation`(킥오프 배치용)은 유지한다. `SimpleDecisionStage`는 건드리지 않으므로 압박 지정은 010이 나올 때까지 소비되지 않는다.

## 데이터 · 스키마
`formations.json` 슬롯 예시 (오프셋은 블록 중심 기준 정규화 값, A3 `o_i`):
```json
{"id":"4-3-3","slots":[{"id":"LCB","pos":"CB","line":true,"zoneWeight":1.0,"role":"cb_default",
  "anchors":{"BuildUp":[-0.16,-0.12],"Progression":[-0.17,-0.13],"FinalThird":[-0.18,-0.13],
             "DefHigh":[-0.15,-0.07],"DefMid":[-0.14,-0.07],"DefLow":[-0.13,-0.06]}}],
 "reduced":[{"fieldPlayers":9,"remove":["ST"]}]}
```
세이브 스키마 변경 없음. 이벤트 스키마는 종류만 추가(additive)하므로 `SchemaVersion` 유지. 경기 스냅샷에는 단계 내부 상태가 추가된다(≈ 1.5 KB, 006의 16 KB 안).

### [미정] 초기값 제안 — 승인 대상
전부 `shape.json`의 `[설계]` 태그 값이고 017로 보정한다. 근거는 FR-34 표와 리서치 03 A3 공식의 역산이다.

블록 (라인 높이 표 중앙 + L/2 = 중심, ball.x = 0.5일 때 중심이 맞도록 Cx = 중심 ∓ 0.15):

| 블록 | Lt 목표 (m) | L_min~L_max | W_min~W_max | Cx_min / Cx_max | Floor / Ceil |
|---|---|---|---|---|---|
| Low | 27.5 | 25~30 | 35~40 | 0.238 / 0.538 | 0.340 / 0.436 |
| Mid | 30 | 28~35 | 38~45 | 0.364 / 0.664 | 0.457 / 0.571 |
| High | 31 | 30~40 | 40~48 | 0.493 / 0.793 | 0.586 / 0.700 |

점유 국면 (FR-34 표에 없음, "점유 시 폭·깊이·면적 > 비점유" A3만 근거):

| 국면 | L_min~L_max | W_min~W_max | Cx_min / Cx_max | Floor / Ceil |
|---|---|---|---|---|
| BuildUp | 34~46 | 52~64 | 0.30 / 0.60 | 0.30 / 0.46 |
| Progression | 36~48 | 54~64 | 0.30 / 0.75 | 0.44 / 0.62 |
| FinalThird | 32~44 | 54~64 | 0.40 / 0.85 | 0.58 / 0.76 |

| 항목 | 값 | 근거 |
|---|---|---|
| L_template / W_template | 33 m / 42 m | Mid 표 중앙 근처. k_len·k_wid = 위 Lt ÷ 템플릿 |
| lineHeightBias | 블록 구간 안에서 LineHeight 편차 × 최대 ±0.04 (4.2 m) | 표의 라인 폭 ±5~6 m 안 |
| 블록 선택 | LineHeight < 1/3 Low, < 2/3 Mid, 그 외 High. 공 x < 0.25이면 한 단계 낮춤 | A2 "지시값과 공 위치" |
| lateralShift | 비점유: Compactness로 0.45~0.8, 점유: 0.45 | A3 범위 |
| λ_pc | 0.02 (≈ 2 m) | 미세 이동 (A3). 대체 입력에서는 효과 0 |
| 라인 k / v_line_max | 틱당 0.25 / 5.0 m/s | 약 1 s에 95% 수렴, 조깅~달리기 속도 |
| drop / step-up 폭 | −4 m / +2 m | step-up은 A3 "약 2 m 전진"[검증]. drop은 설계 |
| θ(PressLine, x) | θ0 + 6 × max(0, x − xb) (x = 압박 팀 관점 공 위치). Low θ0 0.8·xb 0.40, Mid 0.7·0.66, High 0.6·1.0 | 단일 강한 트리거(1.0) + 강도 1이면 High에서 개시 (AC-10) |
| 압박 인원 | PressIntensity < 1/3 → 1, < 2/3 → 2, 그 외 3. CounterPress 창 +1 (상한 3) | A6 "1→3" |
| 압박 유지 | 1.5 s + 2 s × PressIntensity | A6 "압박 지속 시간" |
| 맨투맨 이탈 상한 | 12 m | 라인 간 거리 표 상한 근처 |
| 포지션 익숙함 배수 | 1 + 0.5 × (1 − Familiarity) | B4 방향만 있음 (수치는 설계) |
| AttTransition | t < 6 s, 또는 t < 8 s이면서 상대 미정렬 | A2 "6–8 s", 조건·시간 초과를 한 식으로 |
| Hybrid 코너 맨마커 | Zonal 0 · Hybrid 2 · Man 4 | A9 "0–4명" |

### 스펙 해석 — 승인 대상 (본문이 서로 걸리는 곳)
1. **라인 슬롯의 x 오차는 σ_line(FR-43)만 쓰고 FR-37은 y에만 적용한다.** FR-37을 x에도 넣으면 Positioning 0.5에서 σ 2.25 m라 FR-40 ±1.5 m(AC-07)을 지킬 수 없다.
2. **FR-40 ±1.5 m는 잘라내지 않는다.** s=0·피로 1이면 σ_line이 1.0 m라서, 잘라내면 AC-07의 표준편차 ±10%와 OFF-06(처진 수비수)이 깨진다. s=1·피로 0이면 오차가 0이라 ±1.5 m는 저절로 지켜진다.
3. **AC-08의 "자기 수비 라인"은 라인 슬롯 실제 목표의 최솟값이다.** 라인 슬롯이 오차로 x_line 밑에 있어도 AC-08이 깨지지 않고, "처진 한 명이 라인을 만든다"와 일치한다.
4. **FR-32 공격수 상한은 점유 팀 필드 선수 전원에 적용한다** (포지션 분기 없음, AC-08의 상위 집합).
5. **루즈볼 1.5 s 초과 시**: 직전 점유 팀 = DefTransition, 상대 = AttTransition. OnLoss = Mixed: 공이 상대 진영(압박 팀 관점 x ≥ 0.5)이면 CounterPress, 아니면 Regroup.
6. **AttTransition·DefTransition의 앵커**: AttTransition은 공 위치의 점유 프로필, DefTransition은 CounterPress면 DefHigh, Regroup이면 지시 블록.
7. **대칭·라인 묶음 검증(AC-04·05·16)은 오차를 끈 솔버 출력**으로 한다. 오차는 시드 RNG라 점대칭이 성립하지 않는다.
8. **AC-11 시작 조건**: 배역 종료 시 슬롯까지 거리 ≤ 20 m (4 s 안에 3 m는 이 거리에서 물리적으로 가능, 스펙에 시작 거리가 없음). 3 m는 오차 반영 목표 기준이다.
9. **NaN 지시값**은 0.5(중립)로 바꾸고 경고한다 (잘라낼 경계가 없음).

## 결정론 · 성능
- RNG: `TeamShape` 스트림 하나를 ShapeStage가 소유. 오차는 재매칭 주기에만, 활성 슬롯 id 오름차순으로 뽑는다. 소비 횟수는 진단 on/off와 무관(AC-20). 라인 일체성 오차도 같은 주기·같은 스트림.
- 순회: 슬롯 id 오름차순 배열만, 사전 순회 없음. 동점은 `<` 비교 + 슬롯 id 순(FR-51, 경계 항목).
- 재매칭: 팀 0은 `tick % 5 == 0`, 팀 1은 `tick % 5 == 2`로 엇갈려 틱 스파이크를 줄인다.
- 슬롯 비는 경우(배역 중·FR-91): 직사각 매칭, 라인 슬롯에 높은 공석 비용 → 앞선 슬롯이 먼저 빈다.
- 할당 0: 배열은 생성자에서 잡고, 틱 경로에 LINQ·람다·boxing 없음. 파라미터는 `ShapeParams` 평면 배열.
- 진단: `IShapeDiagnostics == null`이면 호출·할당 0, 켜도 읽기만 한다.
- 스냅샷: 3개 단계의 `WriteState/ReadState/AddTo`. 왕복 테스트로 누락을 잡는다.
- 측정: EditMode는 회귀 기준선, 판정은 `farm.py bench --spec 009`(IL2CPP, D17). 평균 3 µs를 넘으면 멈추고 예산 수정을 제안한다(스펙 명시).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `FormationCatalogTests.Defaults_AllValid`, `MissingSlot_Rejected`, `DuplicateId_Rejected`, `AnchorOutOfRange_Rejected` | 로직 | — |
| AC-02 | `PhaseMachineTests.ThirdsByBallX`, `BoundaryOscillation_NoFlicker` | 로직 | — |
| AC-03 | `PhaseMachineTests.DefTransition_Window`, `Regroup_ImmediateBlockBlend` | 로직 | — |
| AC-04 | `ShapeSolverTests.FormationShapes_MatchA4Table` (6종, 오차 끔) | 로직 | — |
| AC-05 | `ShapeSolverTests.BlockPresets_SteadyStateInTable` | 로직 | — |
| AC-06 | `DefensiveLineTests.Drop_StepUp`, `SpeedLimit` | 로직 | — |
| AC-07 | `DefensiveLineTests.Cohesion_Sigma`, `SlotError_SigmaAndHold` | 로직 | — |
| AC-08 | `ShapePropertyTests.OffsideAndDefLineCaps_10000States` | 로직 | — |
| AC-09 | `ShapePropertyTests.TargetsInsidePitch_10000States` | 로직 | — |
| AC-10 | `PressPlannerTests.Trigger_Thresholds`, `Count_ByIntensity`, `CastExcluded` | 로직 | — |
| AC-11 | `ScenarioBoundaryTests.CastSkipped_ThenReturnsToSlot` (005 포함) | 로직 | — |
| AC-12 | `RosterChangeTests.RedCard_TenManShape`, `NoDuplicateOrEmptyAssignment` | 로직 | — |
| AC-13 | `SetPieceLayoutTests.WallCounts`, `DistanceRule`, `CornerSchemes` | 로직 | — |
| AC-14 | `TeamInstructionsTests.InvalidValues_ClampedAndWarned` | 로직 | — |
| AC-15 | `ShapeDeterminismTests.FullMatch_SameHashes`, `TieFixture_Stable`, `Snapshot_RoundTrip` | 결정론 | — |
| AC-16 | `ShapeDeterminismTests.PointSymmetry`, `HalfTimeFlip_Symmetric` | 결정론 | — |
| AC-17 | `ShapePerfTests.Tick_us`, `Rematch_us`, `Tick_NoAlloc` + `farm.py bench --spec 009` | 성능 | mac-m1-1600p |
| AC-18 | `e2e/009-shape-snapshots` — **보류**(040) | E2E | smoke |
| AC-19 | 1,000경기 집계 — **보류**(017). 지표 기록 자체는 `ShapeTelemetryTests.WritesEveryDecisionTick`로 확인 | 로직 | — |
| AC-20 | `ShapeDeterminismTests.DiagnosticsOnOff_SameHashes`, `ShapePerfTests.Tick_NoAlloc_GcAllocCount`(진단을 끈 실행) | 결정론 | — |

추가: `HungarianTests`(교차·동점·직사각), `SetupWiringTests`(Tactics 없으면 기존 단계, 있으면 009 단계), 006 회귀는 기존 `Match` 테스트 클래스.

## 버그 방지
- 경계: 공 x = 0.33·0.66, 코너 플래그, 골라인, 피치 밖 1틱 → 속성 테스트(AC-08·09) 표본에 강제 포함.
- 전환 타이머: 같은 틱 탈취→재탈취 시 재시작 → `PhaseMachineTests.DoubleChange_RestartsTimer`.
- 하프 전환: 보간 상태 초기화 + 라인을 새 관점으로 → `HalfTimeFlip_Symmetric`.
- 인원: 슬롯 > 선수, 교체 직후 역할 불일치, 7명 미만(006·012 책임 — 여기서는 예외 없음만) → `RosterChangeTests`.
- 배역 과다: 형태 인원 < 6 → 경고 1건 + 최소 형태 → `ScenarioBoundaryTests.TooManyCast_Warns`.
- 스위퍼 GK: 오프사이드 라인에 GK 포함 → `DefensiveLineTests.OffsideLine_SweeperKeeper`. 공이 더 뒤·하프라인 상한도 같은 클래스.
- 압박 지정 + 1순위 대응자 겹침 → 압박 우선 테스트.
- 재시작: 벽 인원 충돌(선수 부족), 9.15 m가 피치 밖을 요구하는 코너 근처 프리킥 → 잘라낼 때 거리 규칙 유지 테스트.

## 위험 · 미정
- 평균 3 µs(IL2CPP)는 빠듯하다. `IPitchControlQuery`를 틱마다 20회 부르면 넘을 수 있어 λ_pc 이득은 재매칭 주기마다 계산해 보관한다.
- High 블록 + 3라인 형태(4-5-1 등)는 FR-34(L ≥ 30)와 FR-35(간격 ≤ 15)가 L = 30 부근에서만 동시에 맞는다. 제안값 31 m면 간격 15.5 m다. 리서치도 "하이프레스는 간격이 커진다"고 하므로 AC-19(017)에서 다시 본다.
- 점유 국면의 L·W 범위는 근거가 정성적이라(A3) 017 보정 영향이 가장 크다.
- 대체 입력(능력치 상수, 역할 오프셋 0, 피치 컨트롤 0, 압박 신호 근사)을 쓰는 동안은 AC가 구조만 검증한다. 002·003·007·008·013 연결 때 재확인이 필요하다.
- FR-11의 "인버티드 FB", "8번 전진" 같은 변형은 프로필 앵커로만 표현한다. 역할이 만드는 동적 변형은 003 몫이다.
- 스펙 결정 사항: AC-18(040)·AC-19(017)·OFF-06 빈도 확인은 보류.

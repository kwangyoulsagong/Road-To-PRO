# 011 — 골키퍼: 위치 · 선방 · 3D 접점 · 플랜

상태: 승인됨 (2026-11-11, 태스크와 함께 승인 · 바로 진행)
스펙: ./spec.md

## 접근
지금 GK는 모델이 없다. 물리 단계의 느슨한 공 잡기(`PhysicsStage.TryClaim`)가 GK에게 반경 1.8배·속도 2배를 줄 뿐이고, 팀 형태(`TeamShapeEngine` "011 전 대체")와 `HoldShapeStage`가 공 위치 선형식으로 GK 목표를 찍는다. 그래서 반경 밖 슛은 그대로 들어가고(평균 득점 ≈ 24골, 058 AC-15 보류의 원인 중 하나), 목표 위치가 clamp 경계에서 튄다.

이 플랜은 순수 C# 모듈 `MatchEngine/Runtime/Goalkeeper/`를 새로 만들고, 기존 파이프라인의 세 자리에 끼운다.
1. **위치(단계 2 형태)** — `KeeperPositioner`가 GK 목표점을 낸다(FR-01~07). 009 형태 엔진과 `HoldShapeStage`의 GK 대체식을 대체하고(FR-93), GK는 계속 `HoldShape → Formation` 조향으로 005 운동학만 탄다(FR-08).
2. **선방(단계 7 물리, 서브스텝)** — `ShotWatcher`가 매 서브스텝 "골문을 향하는 공"(킥·굴절 직후 004 예측으로 골라인 통과 예정)을 잡아(FR-15) `SaveModel`로 도달 확률·처리 결과를 정하고, 반응 시각에 다이빙을 시작해(FR-17) 접촉 서브스텝에 결과(잡기·쳐내기·펀칭·실패)를 004 공에 적용한다(FR-10~12).
3. **결정(단계 5)** — GK 행동 후보(위치 유지·박스 안 느슨한 공 경합·시나리오 의도 이동)를 `KeeperChooser`가 010 FR-20 공통 선택 규칙(최댓값 정규화 + softmax, 의도 보너스 FR-92·θ_opp FR-94)으로 고른다(FR-16·94). 010이 아직 없어 이 선택 규칙은 011 모듈 안에 두고, 010 구현 때 010 공용 선택기로 옮긴다.

도달 판정은 골라인 한 점이 아니라 **공 경로 전체**(골라인 통과까지 서브스텝 샘플)에서 `margin(τ) = R(τ − t_react_eff) − d_req(τ)`의 최댓값 하나로 계산한다. 이렇게 해야 너무 나온 GK가 칩슛에 당하고(AC-03) 각을 좁힌 GK가 니어포스트를 막는다. 슛 평가 함수(FR-14)와 실제 판정(FR-10)은 같은 `SaveModel.Evaluate`를 쓰고, 실제 판정만 그 뒤에 RNG를 굴린다(AC-19).
대안: 슛 순간 한 번에 결과를 굴려 즉시 적용 → 반응·다이빙 시각이 이벤트와 어긋나고 가림·굴절 리셋(FR-11)을 표현할 수 없어 버렸다.

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `GK/` = `ME/Goalkeeper/`, `T/` = `Assets/_Project/Tests/EditMode/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `GK/KeeperAttributes.cs` | GK 능력치 입력(002 이름: Reflexes·Diving·Handling·GKPositioning·OneOnOne·CommandArea·Sweeping·Distribution + Jumping·Composure·Decisions, 키). 002 전 대체 입력 — `KinematicAttributes`와 같은 방식(호출자가 채움, 범위 검사) |
| 〃 | `GK/KeeperMapping.cs` | FR-70 매핑: n → t_react·v_dive·r_max 보정·a·spill 평균·위치 오차 σ·후보 수·σ_t·결단 지연·m·σ_angle·온도(073·074 공용). 순수 함수 |
| 〃 | `GK/GoalkeeperParams.cs`, `GK/GoalkeeperTuningIds.cs` | `Data/tuning/goalkeeper.json` 읽기 |
| 〃 | `GK/GoalGrid.cs` | 골문 7×3 셀 중심·코스 선호 사전분포 w_t (FR-02·14, 071 FR-13과 같은 격자) |
| 〃 | `GK/KeeperPositioner.cs` | FR-01~07: 이등분선·니어포스트 우선·위험 최소화 탐색(후보 ≈ 15 × 목표 21)·칩슛 항·대기 깊이(역할·라인 높이)·경계 3 m 섞기·틱당 1.5 m 제한·위치 오차 재추첨 시점·0.3 s 저주기 |
| 〃 | `GK/SaveModel.cs` | FR-03·09·10·12·14: 도달거리 R(τ), 경로 최대 마진, p_reach, 난이도 h, p_catch·p_parry_safe·펀칭 조건, 리바운드 속도·방향(spill 평균 6 − 3n, 골라인 자르기). `Evaluate`(RNG 없음) / `Resolve`(주입 RNG) |
| 〃 | `GK/ShotWatcher.cs` | FR-11·15·17: 골문을 향하는 공 감지(킥·굴절·리바운드 후 004 예측), 가림 시간 계산, 굴절 시 반응 타이머 리셋, 다이빙 시작·접촉 서브스텝 예약, 결과 적용, PSxG(FR-80) |
| 〃 | `GK/DiveMotion.cs` | FR-17: 다이빙 상태(시작 시각·방향·높이 구간 3·수평 변위 = R(τ)), 착지 후 일어나기(행동 불가) |
| 〃 | `GK/KeeperChooser.cs` | FR-16·94: GK 행동 후보·기대효용·010 FR-20 선택 규칙 대체 구현·의도 보너스·θ_opp |
| 〃 | `GK/GoalkeeperSystem.cs` | 위 부품을 묶는 경기당 1개 객체(두 GK 상태, 상태 쓰기·읽기·해시), `MatchContext.Keepers` |
| 〃 | `GK/IKeeperDiagnostics.cs` | FR-82 진단 싱크(목표점·위험도 후보 점·이등분선). null이면 호출 0 |
| 〃 | `ME/Kinematics/KinematicsSystem.cs` | 외부 구동 슬롯(다이빙) API: 그 서브스텝 위치·속도를 외부가 정하고 조향 적분·`MotionLimitMonitor` 검사에서 뺀다(FR-17 "005 FR-52 검사 제외"). 겹침 위치 투영은 유지(T09에서 변경 — 빼면 해제 순간 튐). 구간 표시는 이벤트 |
| 〃 | `ME/Match/{MatchContext,MatchSetup,MatchParams→MatchTuning,MatchSim}.cs` | `Keepers` 생성, `MatchPlayer.Keeper` 능력치(선택), `MatchTuning.Goalkeeper` 묶음(없으면 정의 기본값), 스냅샷 섹션·해시 |
| 〃 | `ME/Match/Squad.cs`, `ME/Match/Flow/Substitutions.cs` | GK 퇴장·부상 후 교체 불가 시 필드 선수가 GK 슬롯으로 이동(경계 상황, AC-15). GK 슬롯 선수가 지정 GK가 아니면 GK 능력치 대체값(표시 2 ≈ 1~3) |
| 〃 | `ME/Match/Stages/PhysicsStage.cs` | 서브스텝마다 `Keepers.OnSubstep` 호출(킥 적용·공 적분 뒤, 잡기 전). `TryClaim`의 GK 특례를 "자기 페널티 박스 안 손 사용"으로 축소하고, 추적 중인 슛은 잡기 대상에서 뺀다(선방 모델만 판정) |
| 〃 | `ME/Match/Stages/StandIn/{SimpleDecisionStage,HoldShapeStage}.cs`, `ME/Match/Stages/ScenarioStage.cs` | GK 결정은 `KeeperChooser`로(다이빙·일어나기 중이면 결정 없음), 느슨한 공 추격 GK 조건을 "공 예상 지점이 자기 박스 안"으로(FR-16), `HoldShapeStage` GK 목표를 포지셔너로 |
| 〃 | `ME/Tactics/TeamShapeEngine.cs`, `ME/Tactics/ShapeInputs.cs` | GK 목표 = 포지셔너 결과(FR-93). 포지셔너에 라인 높이(009 수비 라인)를 넘긴다. 오프사이드 라인(FR-45)은 지금처럼 실제 GK 위치 |
| 〃 | `ME/Match/Events/SimEventKind.cs` | 304 DiveStart, 305 Parry, 306 Smother(073 예약), 307 ClaimFail(073 예약), 308 Distribution(074 예약), 309 KeeperRecovered |
| Core | `Core/Runtime/Random/RngStreamId.cs` | `Goalkeeper = 10` 스트림 추가(선방·위치 오차 전용 — 다른 스트림 소비 순서를 흔들지 않게) |
| Presentation | `Presentation/Players/Data/043-anim-map.json` | Save → none(집계 이벤트), Catch·Parry·Punch·DiveStart 매핑(기존 클립 `gk_dive_l/r`·`gk_catch_chest`·`gk_punch`), 새 이벤트 none 매핑. 클립 제작 없음(경계: 043/054) |
| Presentation | `Presentation/Runtime/Players/Logic/AnimPlan.cs` | 실점 장면 다이빙: DiveStart의 "예정 접촉 시각"(F0)을 접촉 키로 써서 실패 다이빙도 손이 공 경로 근처를 지나게(FR-91, AC-25 실점 장면). 최소 수정 |
| Data | `Data/tuning/goalkeeper.json` | FR-70 계수·[미정] 대체값·[제안] 값·격자·사전분포 |
| Data | `Data/e2e/011-gk-highlight.json` | AC-25 장면 3개(다이빙 선방·펀칭·실점), `LiveMatchFixture` 경기 |
| Verification | `Verification/Runtime/Samples/Highlight/LiveMatchFixture.cs` | 필요하면 GK 능력치·장면 시각만 추가(경기 설정 자체는 058 그대로) |
| Tests | `T/MatchEngine/Goalkeeper/*Tests.cs`, `Tests/Performance/MatchEngine/GoalkeeperPerfTests.cs`, `T/Presentation/...GkContactTests.cs` | 아래 테스트 전략 |

의존 방향 확인: Goalkeeper는 MatchEngine 안의 Ball·Kinematics·Match 타입만 쓰고 UnityEngine 비참조. Presentation은 SimEvent만 읽는다. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- `Data/tuning/goalkeeper.json` (`version: 1`, 기존 튜닝 형식). 태그 규칙: 스펙 값은 `[설계]`·`[제안]`, 스펙 `[미정]`은 플랜 시작값 `[대체]`(017 보정 대상, 디자인 값 아님).

| id | 값 | 태그 | 출처 |
|---|---|---|---|
| gk.map.react.base / slope | 0.32 / 0.14 s | 설계 | FR-70, 리서치 04 § A7 |
| gk.map.dive.base / slope | 3.0 / 1.6 m/s | 설계 | 〃 |
| gk.map.rmax.slope | 0.3 m | 설계 | 〃 |
| gk.map.handling.base / slope | 2 / 4 | 설계 | 〃 |
| gk.map.spill.base / slope | 6 / 3 m | 설계 | FR-12 (방향 [미정] — 아래 위험) |
| gk.map.posNoise.base / slope | 0.6 / 0.5 m | 설계 | FR-06 |
| gk.reach.r0 / bodyHalf | 0.8 / 0.25 m | 설계 | FR-03 |
| gk.reach.rmaxBase | 2.6 m (키 1.88 m 기준, 키 비례) | 대체 | FR-03 [미정] |
| gk.reach.highZ / highFade | 1.8 m / 0.6 | 대체 | FR-03 높은 구석 감쇄 [미정 형태] |
| gk.reach.sReach | 0.15 m | 대체 | FR-10 [미정] |
| gk.save.alpha·beta·gamma·delta | 0.08 / 1.5 / 1.0 / 0.8 | 대체 | FR-10 [미정] |
| gk.save.b·c | 3 / 1.5 | 대체 | FR-10 [미정] |
| gk.save.punchSpeed / crowdCount | 26 m/s / 2명(2 m 안) | 대체 | FR-10 펀칭 조건 |
| gk.occlusion.min / max | 0.1 / 0.2 s | 설계 | FR-11 |
| gk.pos.candidates / depthMin / depthMax / lateral | 15 / 0.5 / 6 / ±0.5 m | 설계 | FR-02 |
| gk.pos.riskScale(s) / lambda / mu / hSafe | 0.15 m / 0.05 / 0.4 / 4 m(공 거리 비례) | 대체 | FR-02 [미정] |
| gk.pos.shootRange | 35 m | 대체 | FR-04 "슈팅 가능 거리" (값 없음, 30 m 중거리 AC-23이 안에 들게) |
| gk.pos.lowRate | 0.3 s | 제안 | FR-04 |
| gk.pos.blendBand / maxStep | 3 / 1.5 m | 제안 | FR-07 |
| gk.wait.ratio / min / max / traditional | 0.3 / 4 / 20 m / 0.6 | 설계·제안 | FR-05 |
| gk.wait.sweeperOffset | 18 m | 설계 | FR-05, 리서치 03 § A8 |
| gk.still.speed / movingPenalty | 1 m/s / 0.3 m | 제안 / 대체 | FR-09, 073 FR-20 [미정: 크기] — 073이 같은 id를 쓴다 |
| gk.dive.recover | 1.0 s | 대체 | FR-17 [미정] |
| gk.dive.lowZ / highZ | 0.6 / 1.6 m | 대체 | FR-17 높이 구간 경계 |
| gk.field.display | 2 | 제안 | 경계 "GK 능력치 1~3" |
| gk.choice.tauMax / tauMin | 0.35 / 0.05 | 설계 | 010 FR-20 (확정 2026-11-02) |
| gk.choice.wIntent / rIntent / thetaOpp | 0.3 / 5 m / 0.2 | 대체 | 010 FR-92·94 [미정] |
| gk.grid.prior[21] | 낮은 구석 1.4 · 중간 1.0 · 높은 1.2 · 중앙열 0.6 (정규화) | 대체 | FR-02 "낮은 구석일수록 높음" |

- 이벤트(006 FR-30 형식, 종류별 필드 의미는 011이 정함):
  - **DiveStart(304)**: Player=GK, Substep=반응 시작, X/Y/Z=예정 접촉 지점, A=방향(−1 왼/0 정면/+1 오른) × 10 + 높이 구간(0 낮음/1 중간/2 높음), F0=예정 접촉까지 시간(s), F1=t_react_eff, RelatedSeq=슛.
  - **Catch(301) / Parry(305) / Punch(302)**: 접촉 이벤트. Part=Hand, Substep=접촉 서브스텝, A=동작 종류 코드(FR-90 목록 인덱스), B=높이 구간, F0=p_reach, F1=난이도 h, Parry는 `B`의 상위 비트에 안전/위험 플래그.
  - **Save(300)**: 유효슈팅 선방 집계 1건(015 입력). F0=PSxG(리그 평균 GK 실점 확률), F1=실제 GK p_save, RelatedSeq=슛, Substep=접촉과 같음. 3D 매핑 none.
  - **Goal(기존)**: GK가 다이빙했고 margin이 −bodyHalf 안이면 `B |= FingertipFlag`(054 FR-44 "손끝 닿음").
  - **KeeperRecovered(309)**: 일어나기 끝.
- FR-90 GK 동작 목록은 `GK/KeeperMotionKind.cs` enum(073·074 동작 포함)으로 두고 이벤트 A가 이 값을 쓴다. 043 매핑은 지금 있는 클립으로만 잇고, 없는 동작(높음/낮음 별도 다이빙, 크로스바 넘기기 등)은 매핑표 `_doc`에 "043/054 후속 클립"으로 남긴다.
- 스냅샷: `GoalkeeperSystem` 섹션 추가 → `MatchSnapshot.FormatVersion` 3. 두 GK의 목표점·직전 목표·위치 오차·재추첨 키·추적 중인 슛(예약 서브스텝·결과)·다이빙 상태.
- `MatchSetup` 변경: `MatchPlayer`에 선택 필드 `KeeperAttributes Keeper`(미지정 = 리그 평균 표시 10~11 내부값). 세이브 스키마(022) 변경 없음.

## 결정론 · 성능
- RNG: 새 스트림 `RngStreamId.Goalkeeper` 하나. 소비 순서 고정: (1) 위치 오차 재추첨(GK 0 → 1, 소유 변경·슈팅 거리 진입 틱에만), (2) 선방 `Resolve`(접촉 서브스텝, 처리 1회당 고정 3회: 처리 판정·리바운드 방향·리바운드 거리), (3) `KeeperChooser` softmax(결정 틱, GK 0 → 1). 도달 판정 자체는 확률값만 쓰고 결과 추첨에서 `p_reach`와 처리 확률을 합친 하나의 균등 난수를 구간으로 나눈다(호출 수 고정).
- `SaveModel.Evaluate`·`KeeperPositioner`는 RNG를 받지 않는다(AC-19 "호출 전후 RNG 상태 같음"은 타입으로 보장 + 테스트).
- 공 경로 예측: 004 `BallPredictor` 대신 같은 `BallStepper`를 상태 복사본으로 서브스텝 단위로 돌리는 내부 루프(골라인 통과·정지·컨트롤에서 끝, 상한 3 s). 결과는 미리 잡은 배열(300칸)에 저장 → 할당 0. 004 예측과 비트 동일(같은 스테퍼).
- 순회 순서: GK 0 → 1, 후보는 깊이 오름차순 → 좌우 오름차순, 셀은 행 → 열. 동점은 낮은 인덱스.
- 진단(FR-82): `IKeeperDiagnostics` null이면 분기 하나뿐, 켜도 RNG·상태를 바꾸지 않음(AC-20).
- 비용: 위치 탐색 15 × 21 = 315 시그모이드 → 슈팅 거리 밖에서는 0.3 s 주기, 안에서는 매 틱. 선방 판정은 슛 1회당 경로 ≤ 300 스텝 예측 1회 + 마진 최대 1회(시그모이드 1회). 성능 테스트: `GoalkeeperPerfTests`(Unity Performance, 위치 탐색 1회·선방 판정 1회·결정 틱당 GK 2명 p95, GC 0 B) + 058 경기 벤치 회귀(+10%)는 마지막 IL2CPP 벤치에서.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `KeeperPositionerTests.PenaltySpotFront_OnBisector_DepthInRange` | 로직 | — |
| AC-02 | `KeeperPositionerTests.SideOfGoalLine_NearPost_NoNaN_1000Samples` | 로직 | — |
| AC-03 | `SaveModelTests.OptimalPosition_BeatsLineCenter_Binomial`, `SaveModelTests.ChipShot_DeepKeeperConcedesMore` | 로직 | — |
| AC-04 | `SaveModelTests.CenterShot_SpeedIndependent`, `SaveModelTests.HighCorner_Below15_LowAboveHigh` (+ 셀별 선방률 표를 리포트로) | 로직 | — |
| AC-05 | `ShotWatcherTests.Occlusion_AddsReactDelay`, `ShotWatcherTests.Deflection_ResetsTimer_Twice` | 로직 | — |
| AC-06 | `SaveModelTests.Handling_RaisesCatch`, `SaveModelTests.SpillMean_MatchesFormula`, `SaveModelTests.Rebound_NeverBehindGoalLine` | 로직 | — |
| AC-13 | (보류 — 071·072·010 연결 뒤 017 대량 시뮬) `PsxgTelemetryTests` 는 PSxG 기록 형식만 지금 검사 | 로직 | — |
| AC-15 | `KeeperReplacementTests.FieldPlayerInGoal_WorksAndSavesLess` | 로직 | — |
| AC-16 | `GoalkeeperDeterminismTests.ShotFixtures100_TwoRuns_SameHash`, `.Match90_TwoRuns_SameKeeperEventHash`, `.Snapshot_RoundTrip_MidDive` | 결정론 | — |
| AC-17 | `GoalkeeperPerfTests.Position_Once`, `.Save_Once`, `.PerTick_TwoKeepers`, `.Gc_Zero` + 마지막 IL2CPP 벤치 | 성능 | mac-m1-1600p |
| AC-18 | `e2e/011-gk-positioning` — **보류**(040 디버그 뷰 없음). 진단 출력은 AC-20 테스트로만 | E2E | mac-m1-1600p |
| AC-19 | `SaveModelTests.Evaluate_MatchesResolve_100k_NoRngChange` | 로직 | — |
| AC-20 | `GoalkeeperDeterminismTests.Diagnostics_OnOff_SameHashes_ZeroAllocWhenOff` | 결정론 | — |
| AC-21 | `KeeperMotionAuditTests.Matches_NoLimitBreach_NoTargetJump` (`Explicit`, 100경기 — 2026-11-11 사용자 결정) | 로직 | — |
| AC-22 | `KeeperPositionerTests.BoundarySweep_MaxChange02`, `.LateralSweep_Monotonic` | 로직 | — |
| AC-23 | `ShotWatcherTests.LongShots1000_AlwaysJudged_ReactTimeExact` | 로직 | — |
| AC-24 | `GkContactTests.AllContacts_HavePartAndSubstep`(엔진) + `GkContactMeterTests.HandBall_Within10cm_33ms_NoContradiction`(054 `MotionMeter`, Presentation EditMode) | 로직 | — |
| AC-25 | `e2e/011-gk-highlight` — 다이빙 선방·혼잡 속 펀칭·실점 3장면 스크린샷 + 육안 승인 (기준 이미지 비교는 2026-11-11 폐지 → 손–공 화면 거리 수치는 캡처 메타로 기록) | E2E | mac-m1-1600p |
| AC-26 | `SaveModelTests.MovingKeeper_ReachPenalty` | 로직 | — |
| AC-27 | `KeeperChooserTests.NegativeUtility_NotChosenOutsideThetaOpp`, `.ZeroBonus_EqualsPlainChoice` | 로직 | — |

회귀: 058 `DeadBallFlowTests`·`LiveSceneSelector` 장면 테스트·`TeleportAuditTests`, 009 형태 테스트(GK 목표 바뀜 → 기대값 갱신은 이유 기록), 006 결정론·스냅샷 테스트, 043 `AnimationMapTests`.

## 버그 방지
- 기하 퇴화: 공이 골라인 위·골문 뒤·코너 플래그 → 이등분선 각 0. 각이 ε 미만이면 니어포스트 커버 위치로 바로 가고, 모든 정규화는 길이 ε 검사 후 기본 방향. `AC-02` 1,000 무작위 표본 + 골문 안 좌표 금지 검사.
- 초근거리: t_ball < t_react → τ − t_react 음수를 max(0, ·)로, R = r0. 테스트 `SaveModelTests.UltraClose_UsesR0`.
- 연속 굴절 2회: 타이머 2회 리셋, 이벤트 순서 (Deflection → DiveStart 취소 없음 → 재예약) 고정. `AC-05`.
- 리바운드가 골라인 너머로: 리바운드 착지 예정점이 골라인 뒤면 방향을 앞으로 접어 자른다. `AC-06`.
- 다이빙 중 두 번째 슛: 일어나기 전이면 R = r0·현재 자세, 새 다이빙 없음. `ShotWatcherTests.SecondShotDuringDive_NoReaction`.
- 외부 구동 슬롯과 Separation·운동 한계: 다이빙 구간을 빼먹으면 005 진단이 터지고(AC-21), 빼고 끝을 안 돌리면 GK가 영원히 고정 → `DiveMotion` 끝에서 반드시 외부 구동 해제 + 속도 0 상태로 005에 돌려줌(`Snapshot_RoundTrip_MidDive`로 중간 복원 검사).
- 골 판정과 선방의 순서: 같은 서브스텝에 골라인 통과와 접촉이 겹치면 접촉이 먼저(접촉 서브스텝은 골라인 통과 서브스텝보다 앞으로만 예약). 공이 이미 골라인을 넘었으면 판정 취소.
- 박스 경계 손 사용: 공 일부라도 박스 위면 허용(스펙 [미정] → 규칙대로 "공 중심이 박스 선에서 반지름 안"까지 허용으로 구현, 012에서 재확인).
- GK 슬롯 빈 상태(퇴장·교체 불가 직후 한 틱): 포지셔너·감시자는 빈 슬롯을 건너뛴다. 필드 선수 이동은 같은 틱 시작 처리.
- 058 E2E 고정 경기의 골 수가 줄어 장면이 바뀔 수 있음 → 058 E2E 장면 2개(`058-live-goal-sequence`·`058-live-variety`)를 회귀로 다시 찍는다(고친 장면만).

## 위험 · 미정
- **결정 (2026-11-11 사용자) AC-21 경기 수**: 이 스펙은 100경기(≈ 12분, `Explicit`, 마지막 1회). 1,000경기는 017 대량 시뮬 때 같은 감사기로 돌린다.
- **결정 (2026-11-11 사용자) spill 방향 = 식 그대로 6 − 3n**: 6 − 3n이면 Handling이 높을수록 위험한 쳐내기가 골문 가까이(3 m) 떨어져 더 위험하다. 리서치 04 § A7 원문도 "spill 거리 분포 평균 = 6 − 3n m"뿐이다. 이 플랜은 스펙 식 그대로 구현하고(AC-06도 그 식), 해석은 "잘하는 GK는 공을 멀리 쳐내기보다 몸 앞에 떨어뜨려 다시 잡는다"로 본다.
- [미정] 계수(α·β·γ·δ, a·b·c, s_reach, λ, μ, h_safe, r_max 기준, 일어나는 시간, 이동 reach 패널티 크기)는 위 표의 `[대체]` 시작값. AC-04(중앙·높은 구석·낮은 구석)와 고정 픽스처 선방률로 1차 맞추고, 리그 선방률 70%·PSxG 분포(AC-13)는 017에서 보정한다.
- 평균 득점이 크게 내려간다(24골 → 한 자리 예상). 058 AC-15(2.5~3.0)는 여전히 대체 판단(008·010) 의존이라 보류 유지, 이 스펙 리포트에 측정값만 기록한다.
- 3D: 기존 GK 클립은 `gk_ready·gk_dive_l/r·gk_catch_chest·gk_punch·gk_kick·gk_goalkick_r·gk_punt_r`뿐이라 높이 3구간 다이빙·크로스바 넘기기는 같은 다이빙 클립으로 재생된다. AC-24·25의 손–공 거리는 054 접촉 보정(루트 이동·공 당김)에 기대고, 3구간 클립은 043/054 후속으로 남긴다. 측정이 실패하면 멈추고 보고한다.
- 010 선택 규칙 대체 구현(`KeeperChooser`)은 010 구현 때 010 공용 선택기로 옮긴다(재확인 항목).
- 073 FR-20 reach 패널티 크기는 073 소유 [미정] — 같은 튜닝 id를 쓰게 해 두고 값은 대체 0.3 m.

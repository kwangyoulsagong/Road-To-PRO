# 008 — 액션 모델: 패스 · 수신 · 공통 실행 인터페이스 · 플랜

상태: 승인됨
스펙: ./spec.md

## 접근
지금은 "무엇을"(010·013·대체 온볼)과 "어떻게 되나"가 섞여 있다. `StandInOnBall.Pass`·`StepExecutor.TryKick`이 오차 없는 속도 벡터를 바로 `BallAction`에 넣고, `PhysicsStage`가 **서브스텝 0에 고정**으로 킥을 적용하며(FR-65 위반), 잡기는 `TryClaim`이 "반경 대비 가장 가까운 선수"로 정한다(능력치·압박 무관, 인터셉트 판정 없음).

이 플랜은 순수 C# 모듈 `MatchEngine/Runtime/Actions/`를 새로 만들어 세 자리를 바꾼다.
1. **평가(단계 5, RNG 없음)** — `PassModel.Evaluate`(로지스틱 xPass, FR-11)와 `PressureModel`(FR-02)을 대체 온볼·시나리오 킥이 후보 점수에 쓴다. 속도 후보·스루볼 리드 후보는 `argmax xPass × ΔxT`(FR-12·14). 선택(누구에게)은 그대로 호출자(010·013·072 자리) 소유.
2. **의도 → 예약 킥(단계 5 → 7)** — `BallAction`은 이제 "의도"(목표점·속도·킥 종류·대상)만 담는다. 단계 7에서 `ActionSystem`이 그 의도를 **예약 킥**으로 받아, 킥 준비 시간(킥 기술·몸 방향 차이로 정해짐, FR-66)이 지난 서브스텝에서 공이 발 도달 거리 안이면 그 순간 실행 오차(FR-13)를 시드 RNG로 샘플링해 004에 킥을 적용하고 그 서브스텝 시각으로 이벤트를 낸다(FR-65). 준비 시간이 틱 경계를 넘으면 다음 틱으로 이어진다(상태 → 해시·스냅샷).
3. **수신·인터셉트(단계 7, 서브스텝)** — `TryClaim`을 `ReceptionResolver`로 바꾼다. 공이 선수 컨트롤 반경 안을 지나는 서브스텝마다 선수 id 순으로 컨트롤 확률(공 속도·높이·λ_recv·퍼스트터치·압박)을 굴리고 첫 성공자가 잡는다(FR-45, 경계 "동시 인터셉트"). 잡으면 `FirstTouchModel`이 이격 거리·방향을 샘플링해(FR-67) 가까우면 바로 컨트롤, 멀면 공을 그만큼 굴려 보내 느슨한 공으로 둔다(불안한 터치 플래그).

물리 xPass(FR-10)는 004 `BallStepper` 복사 루프(011 `ShotWatcher`와 같은 방식) + 005 TTI 컨트롤 근사로 구현하되 틱 기본 경로에서는 쓰지 않는다(키 모먼트·계수 피팅·디버그용). 같은 컨트롤 함수를 `ReceptionResolver`가 서브스텝 판정에 쓰므로 "평가와 실행이 같은 계수"(FR-61)가 성립한다.

대안: 결정 틱에 패스 결과(성공/인터셉트)를 한 번에 굴리고 공을 그 결과로 보냄 → 접촉 시각·물리 궤적과 결과가 어긋나고(054·058 증상) FR-45 "별도 굴림 없이"에 어긋나 버렸다.

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `A/` = `ME/Actions/` (네임스페이스 `RoadToPro.MatchEngine.Actions`), `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/Actions/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `A/TechnicalAttributes.cs` | 002 전 대체 기술 능력치(내부 0~100): Passing·FirstTouch·Technique + 주발(Left/Right)·약발(1~5). `KinematicAttributes`·`DecisionAttributes`와 같은 방식(호출자가 채움, 범위 검사, 미지정 = 평균 50·오른발·약발 3). Vision·Composure는 `DecisionAttributes` 값을 쓴다(이중 정의 금지) |
| 〃 | `A/ActionParams.cs`, `A/ActionTuningIds.cs` | `Data/tuning/action.json` 읽기 (FR-04) |
| 〃 | `A/ActionMath.cs` | FR-01 정규화 a = 내부/100, logit 가산, 안정 시그모이드(최댓값 빼기), 확률 0.001~0.999 자르기, 정규 표본(고정 2 균등 난수 Box–Muller) |
| 〃 | `A/PressureModel.cs` | FR-02·03: Bekkers T_ij·p_ij·팀 P_j, 2 m/s 미만 0, 터치라인 3 m 보정, 볼 소유자 max(선수, 공). 팀 전체 압박 배열(11×11) 한 번에 |
| 〃 | `A/PassModel.cs` | FR-11·12·13·14: 로지스틱 xPass `Evaluate`(RNG 없음), 속도 후보(땅볼 4 + 뜬공 2) 선택, λ_recv 감쇠, 스루볼 리드 2차방정식 해(lead {0,2,4,6}), 실행 오차 σ_angle·σ_speed 계산과 `Execute`(주입 RNG, 고정 소비) |
| 〃 | `A/BallControl.cs` | FR-10·45 공용: 공 상태·선수 TTI로 서브스텝 컨트롤 확률 λ (높이 > 2.5 m 필드 선수 0, 속도·λ_recv·FirstTouch·압박 반영). 물리 xPass와 `ReceptionResolver`가 같은 함수를 쓴다 |
| 〃 | `A/PhysicsXPass.cs` | FR-10: `BallStepper` 상태 복사 루프(≤ 3 s, 미리 잡은 배열)로 궤적을 적분하며 샘플마다 각 선수 컨트롤 누적 → P(의도 수신자/다른 동료/상대/아웃). RNG 없음 |
| 〃 | `A/FirstTouchModel.cs` | FR-67: 이격 σ = 공식(아래 데이터 표, 리서치 03 B2 대안을 [대체] 시작값으로) × λ_recv 감쇠 × 압박, 방향 각 샘플, 불안한 터치 플래그(> 1.5 m). 고정 RNG 소비 3 |
| 〃 | `A/KickTechnique.cs` | FR-66: 킥 기술 enum(Inside·Instep·Outside·Chip — [대체] 목록), 사용 발 선택(주발·약발, 목표 방향이 몸 어느 쪽인지 + 약발 등급), 몸–킥 방향 차이, 각도 임계 초과 σ 배수, 준비 시간(windup). 성향 → 기술 선택 매핑은 [미정]이라 규칙 기반 대체(아래) |
| 〃 | `A/PendingKick.cs` | 예약 킥 상태(의도·킥커·대상·예약 서브스텝·발·기술·만료). 값 타입, 해시·직렬화 |
| 〃 | `A/ReceptionResolver.cs` | FR-45·67·70: `TryClaim` 대체. GK 특례(자기 박스·추적 중 슛 제외)·킥 직후 재접촉 쿨다운은 기존 규칙 유지. 결과 종류 분류(PassReceived·Interception·BallRecovered) |
| 〃 | `A/ActionOutcome.cs` | FR-70 결과 종류 enum + 분류 함수(태클은 상대 캐리어/직전 소유자가 070 도달 거리 안일 때만, 아니면 인터셉트·공 회수), FR-60 터치 종류(의도적 플레이/굴절/블록) |
| 〃 | `A/ActionDetailLog.cs` | FR-72·15: 상세 기록 고정 배열(오프사이드 스냅샷: 러너 22명 x·패스 순간 수비 2nd-last x·공 x). 이벤트의 `B`에 상세 번호, 상세 기록에 이벤트 `Seq`(발행 후 채움). 해시·스냅샷 포함 |
| 〃 | `A/IActionDiagnostics.cs` | FR-63: 후보별 평가값·선택·접촉 요약 싱크. null이면 호출 0 |
| 〃 | `A/ActionSystem.cs` | 경기당 1개: Action RNG, 예약 킥, 상세 기록, 압박 캐시(틱당 1회), `OnSubstep`(예약 킥 실행)·`Resolve`(수신), 해시·쓰기·읽기. `MatchContext.Actions` |
| 〃 | `ME/Match/Pipeline/TickBoard.cs` | `BallAction`에 의도 필드 추가: `AimPoint`·`Speed`·`Loft`·`Technique`·`IsOneTouch`. 기존 `Velocity`는 슛·클리어·태클(070·071 전 대체)이 그대로 쓴다 |
| 〃 | `ME/Match/Stages/PhysicsStage.cs` | 서브스텝 0 고정 `ApplyAction` 제거 → `Actions.Schedule`(서브스텝 0 전) + 매 서브스텝 `Actions.OnSubstep`(예약 킥 접촉 판정·실행). `TryClaim` → `Actions.Resolve`. 킥 이벤트 Substep = 실제 접촉 서브스텝(틱 넘김 포함), `Part`·발·기술 기록 |
| 〃 | `ME/Match/Stages/StandIn/StandInOnBall.cs` | `Pass`: 후보 점수에 `PassModel.Evaluate × ΔxT`(ΔxT = `StandInSpace.Threat` 차, 007 전 대체) + 기존 잡음(선택은 대체 경로 몫, 072 자리 유지). `BallAction`은 의도만. `TryTackle`·압박 분기: 기록은 FR-70 분류로(070 전 동작 유지) |
| 〃 | `ME/Scenarios/StepExecutor.cs` | `TryKick`: 같은 의도 경로(시나리오가 정한 대상·영역, 속도는 `PassModel` 후보 선택). 공중 패스는 뜬공 후보 |
| 〃 | `ME/Kinematics/KinematicsSystem.cs` | FR-64: 킥·터치 직후 감속을 005 명령 상한(최대 속도 캡, 지속 시간)으로 — 위치·속도 직접 변경 없음. 기존 `ApplyContact`는 접촉(태클)만 |
| 〃 | `ME/Match/{MatchContext,MatchSetup,MatchParams,MatchSim}.cs` | `Actions` 생성, `MatchPlayer.Technical`(선택), `MatchTuning.Action`, 스냅샷 섹션(`SnapshotTagActions`)·해시, `FormatVersion` 4, 발행 후 상세 기록 `Seq` 채우기 |
| 〃 | `ME/Match/Events/SimEventKind.cs` | 새 번호 없음(200~213 사용). 종류별 필드 의미 문서화(아래) |
| Core | `Core/Runtime/Random/RngStreamId.cs` | `Action = 11` 추가(킥 오차·수신·첫 터치 — 결정 스트림 소비 순서를 흔들지 않게) |
| Presentation | `Presentation/Players/Data/043-anim-map.json` | 매핑 변경 없음 예상(Pass·PassReceived·Interception 이미 있음). 필드 의미가 바뀌면 058 FR-50~52 읽기 확인만 |
| Data | `Data/tuning/action.json` | 계수 (아래 표) |
| Verification | `Verification/Runtime/Samples/Highlight/LiveMatchFixture.cs` | 필요하면 기술 능력치만 추가 |
| Tests | `T/*Tests.cs`, `Tests/Performance/MatchEngine/ActionPerfTests.cs`, `Tools/devicefarm/ref/bekkers_pressure.py`(AC-01 독립 참조 구현) | 아래 테스트 전략 |

의존 방향 확인: Actions는 MatchEngine 안의 Ball·Kinematics·Match·UtilityAI(`DecisionAttributes`·`Habit`·`StandInSpace`) 타입만 쓰고 UnityEngine 비참조. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- `Data/tuning/action.json` (`version: 1`). 태그: 스펙 값 `[설계]`·`[제안]`, 스펙 `[미정]`은 플랜 시작값 `[대체]`(017 보정 대상).

| id | 값 | 태그 | 출처 |
|---|---|---|---|
| act.press.sigma / T | 0.45 / 1.5 s | 설계 | FR-02 |
| act.press.minSpeed / touchline / touchBand | 2 m/s / 0.15 / 3 m | 설계·제안 | FR-02 |
| act.press.reaction / vmax | 0.7 s / 7 m/s | 대체 | FR-02 τ_r·v_max (리서치 02 §4.1 기본, 005 연결 시 선수값) |
| act.xpass.b0~b7 | 3.2·0.45·1.0·2.0·1.5·0.6·1.2·0.5 | 설계 | FR-11 |
| act.pass.speeds.ground | 10·14·18·22 m/s | 설계 | FR-12 |
| act.pass.speeds.lofted | 2종 (발사각 25°·40°, 착지 = 대상) | 대체 | FR-12 "뜬공 2~3종" |
| act.recv.decayMin / speedLo / speedSpan | 0.6 / 12 / 12 m/s | 설계 | FR-12 |
| act.recv.touchRelief | 0.5 | 대체 | FR-12 "퍼스트터치가 높을수록 감쇠 감소" 크기 [미정] |
| act.err.angleMax / angleMin | 7° / 1.5° | 설계 | FR-13 |
| act.err.pressK / composureK | 0.8 / 0.5 | 설계 | FR-13 |
| act.err.speedMax / speedMin | 15% / 4% | 설계 | FR-13 |
| act.lead.candidates | 0·2·4·6 m | 설계 | FR-14 |
| act.ctrl.radius / maxZ | 1.0 m / 2.5 m | 대체 / 설계 | FR-45 컨트롤 반경 [미정], AC-03 높이 |
| act.ctrl.rate / speedK / fieldTouchK | 6 /s · 0.08 · 1.0 | 대체 | FR-45 λ 형태 [미정] (서브스텝 확률 = 1 − e^(−λ·dt)) |
| act.touch.sigmaBase / sigmaSlope | 1.8 / 1.4 m | 대체 | FR-67 [미정] → 리서치 03 B2 대안 |
| act.touch.pressK / controlDist / heavy | 0.5 / 0.5 m / 1.5 m | 대체·대체·설계 | FR-67 |
| act.kick.reach | 0.9 m | 대체 | FR-65 [미정] |
| act.kick.windup.{inside,instep,outside,chip} | 0.15·0.25·0.12·0.22 s | 대체 | FR-66 준비 시간 (기술별) |
| act.kick.windup.turnK | 0.2 s/rad | 대체 | 몸–킥 방향 차이가 준비 시간을 늘림 |
| act.kick.angleThresh / sigmaMul | 60° / 1.5 | 대체 | FR-66 [미정] |
| act.kick.weakFootK | 0.25 | 대체 | 약발 등급 1~5 → σ ×(1 + k·(5 − 등급)/4) [002 연결 때 재확인] |
| act.kick.timeout | 1.0 s | 대체 | 예약 킥이 도달 거리 밖으로 계속 남으면 취소 |
| act.after.kickSlow / duration | 0.6 × MaxSpeed / 0.3 s | 대체 | FR-64 킥·터치 직후 감속 |
| act.habit.oneTouch | 0.05 | 설계 | FR-71 ② 원터치 마스터 정확도 +5% → σ×0.95 |
| act.prob.min / max | 0.001 / 0.999 | 제안 | 경계 |

- **킥 기술 선택(FR-66·71 ③ 대체)**: 뜬공 → Chip(짧은 뜬공) 또는 Instep(긴 뜬공), 땅볼 ≥ 18 m/s → Instep, 몸–킥 차이 > 임계 → Outside, 나머지 → Inside. 성향 가중치 매핑은 [미정]이라 규칙만, 003 연결 때 재확인.
- **발 선택**: 목표 방향이 몸 기준 주발 쪽이거나 약발 등급 ≤ 2면 주발, 아니면 반대 발. 주발 아니면 약발 σ 배수.
- **버릇(FR-71 ②)**: 원터치 마스터(레벨 ≥ 1)만 이 스펙 실행에 해당(원터치 패스 σ×(1 − 0.05)). 컷인 장인·슬라이딩 장인은 071·070·012가 읽는다. 버릇 레벨은 010 `PlayStyle.HabitLevels`를 읽는다(이중 정의 금지).
- **이벤트 필드 (006 FR-30, 종류별 의미는 008이 정함)**:
  - Pass(200): Substep = 실제 접촉 서브스텝, Part = 사용 발, OtherId = 대상, F0 = 실행 초속, F1 = 평가 xPass, A = 기술(0~3) | 원터치 비트(4) | 약발 비트(8) | 페널티 비트(16, 기존 A=1 대체 — 소비자 확인), B = 상세 기록 번호(+1, 0 = 없음).
  - PassReceived(201) / Interception(208) / BallRecovered(213): Substep = 컨트롤 서브스텝, Part = `PartFor`, F0 = 공 속도, F1 = 첫 터치 이격(m), B = 불안한 터치 비트(1) | 터치 종류(2비트 << 1).
  - Tackle(207)은 FR-70 분류를 통과한 경우만(070 전 대체 걷어내기 중 거리 밖이면 Clearance).
- **상세 기록**: `ActionDetail { int Index; long EventSeq; long Tick; byte Kind; float BallX; float DefLineX[2]; float RunnerX[22]; }` 고정 배열(용량 4,096, 넘치면 오래된 것부터 덮어쓰지 않고 경기당 카운터 유지 → 테스트로 상한 확인 [대체]). 스냅샷 섹션 + 해시.
- 스냅샷: `SnapshotTagActions` 섹션(예약 킥·상세 기록·Action RNG·직후 감속 타이머) → `MatchSnapshot.FormatVersion` 4. 세이브 스키마(022) 변경 없음.
- `MatchSetup`: `MatchPlayer.Technical`(선택, 미지정 = 평균).

## 결정론 · 성능
- RNG: 새 스트림 `RngStreamId.Action = 11`. 액션당 고정 소비(FR-61):
  - 패스 실행 1회 = 4 균등 난수(방향 정규 2 + 속도 정규 2), 분기(원터치·약발·뜬공) 무관.
  - 컨트롤 판정 1회(선수 1명 × 서브스텝 1개) = 1 균등 난수 — 반경 안 후보만, 선수 id 순.
  - 첫 터치 1회 = 3 균등 난수(이격 정규 2 + 방향 1). 이격이 컨트롤 거리 이하여도 소비.
- 평가 함수(`PassModel.Evaluate`·`PressureModel`·`PhysicsXPass`·`BallControl`)는 RNG를 인자로 받지 않는다(타입 보장 + AC-11 테스트).
- 순회 순서: 선수 슬롯 오름차순, 속도 후보 오름차순, 리드 오름차순. 동점은 낮은 인덱스.
- 할당 0: 압박 배열·궤적 배열·후보 버퍼를 `ActionSystem` 생성 시 확보. 진단은 null 분기 하나(AC-17).
- 기존 해시 변화: 킥 시각·오차·수신 규칙이 바뀌므로 006·011·013·058 결정론 "2회 같음" 테스트는 계속 통과해야 하고, 고정 해시 기대값을 박은 테스트가 있으면 갱신 목록을 만들어 이유를 기록한다.
- 성능: `ActionPerfTests`(로지스틱 1회 ≤ 0.2 µs, 물리 xPass 1회 ≤ 80 µs, 압박 11×11 ≤ 5 µs, 볼 소유자 평가 합계, GC 0 B). 틱 경로를 바꾸므로 마지막에 IL2CPP 벤치(D17) 1회 — 058 경기 벤치 회귀 +10% 판정과 결정 틱 볼 소유자 평가 평균·p95.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `PressureModelTests.MatchesReference_20Fixtures_1e4` (참조값은 `Tools/devicefarm/ref/bekkers_pressure.py`로 만든 고정 JSON), `.SlowDefender_Zero`, `.Touchline_Bonus` | 로직 | — |
| AC-02 | `PassModelTests.Logistic_Monotonic_1000Cases` | 로직 | — |
| AC-03 | `PhysicsXPassTests.OpenSpace15m_Above09`, `.DefenderInLane_Below05`, `.HighBall_NoFieldIntercept` | 로직 | — |
| AC-04 | `PassModelTests.ThroughBall_ZeroError_RunnerArrives`, `.ThroughBall_NoRoot_Excluded` | 로직 | — |
| AC-05 | `ExecutionErrorTests.PassSigma_100k_Within3pct`, `FirstTouchTests.SeparationSigma_100k_Within3pct` | 로직 | — |
| AC-10 | `ActionEdgeCaseTests.*` (수신 후보 0, 초근접, 판별식 < 0, 큰 logit, 극단 능력치, 수비 0명, 동시 인터셉트) | 로직 | — |
| AC-11 | `ActionEdgeCaseTests.Evaluate_DoesNotTouchRng` | 로직 | — |
| AC-12 | `ActionDeterminismTests.Actions100k_TwoRuns_SameHash_FixedRngCount` | 결정론 | — |
| AC-13 | **보류**(017·010·072·011·012 뒤). 지금은 `PassRateHarnessTests`가 고정 상황별 성공률만 리포트에 기록 | 로직 | — |
| AC-14 | `ActionPerfTests.*` + 마지막 IL2CPP 벤치 | 성능 | mac-m1-1600p |
| AC-15 | `e2e/008-action-situations` — **보류**(040 디버그 뷰 없음). 진단 출력 자체는 AC-17 테스트로 | E2E | mac-m1-1600p |
| AC-17 | `ActionDeterminismTests.Diagnostics_OnOff_SameHashes_ZeroAllocWhenOff` | 결정론 | — |
| AC-18 | `ActionMotionAuditTests.Fixtures_And1000Seeds_NoLimitBreach` (005 `MotionLimitMonitor`, 속도 변화율 ≤ 상한 × 접촉 배율, 2회 해시) | 결정론 | — |
| AC-19 | `KickContactTests.ContactWithinReach_SubstepsSpread_FieldsFilled` (1,000건, 서브스텝 0~9 분포·부위·발·FR-82 필드) | 로직 | — |
| AC-20 | `FirstTouchTests.Sigma10k_And_HeavyFlagOnlyAbove15` | 로직 | — |
| AC-21 | 054 `MotionMeter` 지표 + 058 E2E 장면 재실행: `farm.py shot --scenario 058-live-goal-sequence`·`058-live-variety` + 054 AC-04 접촉 지표 테스트 실경기 픽스처로, 058 AC-13 판단·액션 원인 속도 점프 수 리포트 기록. 070·071 판정이 아직 대체라 "008 부분 재확인"으로 표시 | E2E | mac-m1-1600p |
| AC-22 | `ActionAttributeTests.Passing18vs8_FTest`, `.FirstTouch18vs8_FTest`, `.OneTouchMaster_SigmaRatio` | 로직 | — |
| AC-23 | `ActionOutcomeTests.Fixtures_KindPerSituation_NoFarTackle_1000Seeds` | 로직 | — |
| AC-24 | **보류**(002·007 구현 뒤 재실행) | 로직 | — |
| AC-25 | `ActionDetailLogTests.Offside1000_LinkedOnce_HashChangesOnEdit`, `.Snapshot_RoundTrip` | 로직 | — |

회귀: 006 `Match*` 결정론·스냅샷, 011 `Goalkeeper*`(TryClaim GK 특례 이전), 013 `Scenario*`(TryKick), 010 `UtilityAI*`·단계 테스트, 058 `DeadBallFlowTests`·`TeleportAuditTests`·하이라이트 실경기 픽스처, 043 `AnimationMapTests`. `--scope auto`가 MatchEngine 변경 + Core(`RngStreamId`) 변경으로 **전체**가 될 것 — 마지막 1회.

## 버그 방지
- 예약 킥 중 공을 잃음(인터셉트·태클·라인 아웃·데드볼 전환): 접촉 시점에 소유자 재확인, 아니면 취소(이벤트 없음). `KickContactTests.LostBallBeforeContact_Cancelled`.
- 틱 경계를 넘는 예약: 스냅샷 왕복 후 같은 서브스텝에 같은 킥. `ActionDetailLogTests.Snapshot_RoundTrip`에 예약 킥 포함.
- 재시작(킥오프·프리킥·페널티) 킥: 058 데드볼 흐름의 `OnRestartTaken` 시점이 접촉 서브스텝으로 옮겨짐 → 058 `DeadBallFlowTests` 회귀, 재시도 10틱 규칙과 예약 충돌(예약 중이면 재시도 안 함).
- 같은 틱 같은 선수에게 결정 2번(시나리오·대체 동시): 예약이 있으면 새 의도 무시.
- 첫 터치 이격으로 공이 라인 밖: 004가 라인 통과를 내면 정상 처리(마지막 터치 = 수신자). 이격 방향이 수신자 몸 뒤로만 가지 않게 각도 범위 제한 [대체 ±90° + 진행 방향 편향].
- 킥 직후 자기 재접촉: 기존 25 서브스텝 쿨다운 유지, 첫 터치 이격 후 수신자는 짧은 쿨다운(5 서브스텝 [대체]).
- 수치: logit 오버플로 → 안정 시그모이드, 정규화 길이 ε 검사, 2차방정식 판별식 음수·음수 근 제외(AC-10).
- GK: 자기 박스 손 사용·추적 중 슛 제외 규칙은 `ReceptionResolver`로 그대로 옮긴다(011 회귀). GK 컨트롤 확률은 011 특례(반경 1.8배)를 유지하고 기술 모델은 필드 선수만.
- 평균 득점·패스 성공률이 크게 바뀔 수 있음 → 058 AC-15·이 스펙 AC-13은 보류 유지, 측정값만 리포트.

## 위험 · 미정
- [미정] 계수(킥 도달 거리, 기술 목록·준비 시간·각도 임계·σ 배수, 첫 터치 계수, 컨트롤 λ, 성향 → 기술 매핑)는 위 `[대체]` 시작값. AC-03·05·20·22 픽스처로 1차 확인, 017에서 보정.
- 예약 킥 준비 시간이 판단 → 킥 지연을 늘려 압박 상황에서 공을 더 자주 잃을 수 있다(현실적이지만 058 하이라이트 흐름이 바뀜). AC-21 장면 재촬영으로 확인하고 이상하면 멈추고 보고.
- AC-21은 054 AC-04·08, 058 AC-13을 "008·070·071 실제 판정"으로 재실행하라고 하는데 070·071은 아직 대체다 → 이 스펙에선 패스·수신·인터셉트 접촉만 재확인하고 070·071 완료 때 다시 돌린다(보류 부분 표시).
- 상세 기록 용량(경기당 4,096 [대체])을 넘는 경기는 없다고 본다(경기당 패스 ≈ 1,000). 넘으면 이후 기록 생략 + 카운터.
- 물리 xPass로 로지스틱 계수를 오프라인 피팅(FR-11)하는 도구는 이 스펙 범위 밖으로 보고 기본 [설계] 계수를 쓴다 — 피팅은 017 보정 루프에서. 필요하면 스펙 수정 제안.

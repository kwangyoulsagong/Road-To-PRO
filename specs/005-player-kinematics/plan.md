# 005 — 선수 운동학 · Steering · 플랜

상태: 구현 완료 · AC-14 보류 (승인 2026-11-03, [제안] 7개 포함)
스펙: ./spec.md

## 접근
선수 운동은 `RoadToPro.MatchEngine`의 `Kinematics` 네임스페이스에 **값 상태(`PlayerMotionState`) + 선수별 불변 프로필(`PlayerMotionProfile`) + 서브스텝 시스템(`KinematicsSystem.Step`)** 으로 둔다. 004 공과 같은 구조다. 능력치 → 운동 파라미터 매핑(FR-01~06)은 `PlayerMotionProfile.Create`에서 한 번만 계산하고, 서브스텝에서는 미리 계산된 값만 읽는다. 결정 틱(10 Hz)은 선수별 `SteeringCommand`(행동 종류 + 목표)를 넣기만 하고, 서브스텝(100 Hz)마다 ① 인지·운동 지연이 지난 명령 활성화 ② 행동 → 목표 속도 ③ 우선순위 합성 ④ 운동 한계 풀이(F-V·마찰원·감속 상한·피벗) ⑤ semi-implicit Euler 적분 ⑥ 겹침 해소 ⑦ 피치 경계 ⑧ 피로·기록을 선수 id 순으로 처리한다. TTI는 같은 프로필로 계산하는 순수 함수 하나(`TimeToIntercept.Estimate`)이고, 공 인터셉트 탐색은 004 `BallPrediction` 샘플을 같은 시각 그리드로 훑는다. 능력치 입력은 002 전까지 `KinematicAttributes`(내부 0~100, 7개)를 직접 받는다 (2026-11-03 결정). 대안으로 "조향 = 힘 합산(Reynolds 원형)"을 검토했지만, 한계(마찰원·F-V)를 정확히 지키고 AC-04·AC-16을 구조로 보장하려면 "목표 속도 → 한계 풀이" 방식이 낫다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/KinematicAttributes.cs | 입력 값 타입: Pace, Acceleration, Agility, Balance, Stamina, Anticipation, Concentration (내부 0~100 정수). 범위 밖 → `ArgumentOutOfRangeException` (로딩 단계 거부). `Normalized(x) = x/100` (002 FR-14) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/KinematicsTuningIds.cs | 튜닝 id 상수 (값 없음) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/KinematicsParams.cs | `KinematicsParams.Create(TuningBundle, PitchSpec)` → 불변 sealed class. 매핑 계수, 조향·피로·기록 임계, 합성 가중치, 경계 여유. 로딩 검증 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/PlayerMotionProfile.cs | `Create(KinematicAttributes, KinematicsParams)` → 선수별 Vmax·a0·τ·a_lat,max·감속 상한·피벗 회전속도·접촉 손실·한계 초과 배율·k_f 배율·회복 배율·인지지연·운동 개시 지연 (FR-01~07). 불변 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/PlayerMotionState.cs | 선수 상태 값 타입: Id, Position·Velocity(Vec2), Heading(rad), Pivoting(bool), W, C, InContact(이번 서브스텝), 활성 명령, `AddTo(ref StableHasher)` |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/MotionStats.cs | 경기 누적 기록 (FR-50): 이동거리·고강도 거리(double), 스프린트·가속·감속 횟수, 최고속도, 지속 판정용 내부 카운터 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/SteeringCommand.cs | 결정 틱 입력: `SteeringKind {Idle, Seek, Arrive, PursuePlayer, InterceptBall, Evade, OffsetPursuit, OffsetArrive, Formation}` + 목표점·대상 id·오프셋·약한 발 쪽 부호. 생성 팩토리만 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/CommandQueue.cs | 선수별 고정 용량(8) 지연 큐 — 명령은 `발행 시각 + 인지지연 + 운동 개시 지연`에 활성화. 시스템이 소유하는 평면 배열 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/Steering.cs | 행동별 목표 속도 (FR-20~26): Seek, Arrive(물리 기반 감속), PursuePlayer(T 상한 1.5 s), Evade, OffsetPursuit(마킹), OffsetArrive(저킹: 백페달 4 m/s·공격수 속도 ×0.9 상한·측면 0.3 m), Formation(대형 오프셋 추종 = 느린 Arrive) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/SteeringCombiner.cs | FR-27: 우선순위 합성 — 충돌 회피(Separation) > 행동 > 대형 오프셋. 상위 항이 속도 변화 예산을 먼저 쓰고 남은 만큼 하위 항 가중합 (가중치는 튜닝 데이터) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/MotionSolver.cs | 한 선수 서브스텝: 목표 속도 → Δv를 진행 방향 종·횡 성분으로 나눔 → 횡 ≤ a_lat,max, 종 가속 ≤ F-V `a0_eff(1−v/Vmax_eff)`, 감속 ≤ 감속 상한, 마찰원 `a_t²+a_lat² ≤ a_cap²` → 적분. 피벗(히스테리시스 2.0/2.3 m/s) 상태에서는 회전속도 상한으로 방향 전환 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/Separation.cs | FR-24: 반경 1.75 m 이내 쌍의 밀어내기 목표 속도(거리 제곱 빠른 배제) + 적분 후 위치 투영으로 중심 거리 ≥ 0.8 m (id 순 고정 반복 2회). 같은 위치면 id 순서로 정한 결정론적 방향 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/Fatigue.cs | FR-30~36: W′·C 갱신, `Vmax_eff`·`a0_eff`, `FatigueCurve(W,C)`(002 FR-62용), `SprintCostWeight(C) = 1 + 2C`(010용), 반응지연 가산(기본 0) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/TimeToIntercept.cs | FR-40·41: `Estimate(profile, state, W, C, target)` — 반응 지연 동안 현재 속도로 진행 → 목표 방향 속도 성분 투영 → 모노-지수 도달 시간(정지·먼 거리에서 `t_r + d/Vmax + τ`에 수렴). FR-22: `FindBallIntercept(…, BallPrediction)` — 샘플 시각마다 TTI ≤ t_ball인 첫 지점, 상한 2.0 s |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/KinematicsSystem.cs | 22명(가변 N ≤ 32) 상태·프로필·명령 큐 소유. `SetCommand(id, cmd, issueTime)`(결정 틱), `Step()`(서브스텝, 처리 순서 고정), `ApplyContact(id)`(008이 부름: 속도 ×(1−손실), 그 서브스텝 a_cap ×초과 배율 — FR-04), `AddTo(ref StableHasher)`. 입력 배열은 생성 시 id 순 정렬 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/IKinematicsDiagnostics.cs | FR-51 진단 출력 (목표·실제 속도, 종·횡 가속, 마찰원 사용률, 피벗, W·C, 지연 상태). null이면 호출 0 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Kinematics/MotionLimitMonitor.cs | FR-52: 서브스텝 전·후 속도 차로 실제 종·횡 가속·감속·회전율을 재고 프로필 한계(+접촉 허용, 수치 여유 1e-3)를 넘으면 `MotionLimitViolation`(서브스텝·선수·항목·값·한계) 기록. 검증 모드에서만 켠다 (진단과 같은 null 규칙) |
| 데이터 | Data/tuning/kinematics.json | 튜닝 정의 (아래 표). 출처 태그 그대로 |
| 데이터 | Data/perf/005-player-kinematics.json | 성능 예산 기계용 사본 + 테스트 네임스페이스 + `deferred: {AC-14}` |
| RoadToPro.Tests.EditMode | Tests/EditMode/MatchEngine/Kinematics/*Tests.cs, KinematicsTestKit.cs | 로직·결정론. 테스트 킷이 튜닝 파일을 읽고 표준 활동 프로필·스프린트 반복 입력을 만든다 |
| RoadToPro.Tests.Performance | Tests/Performance/MatchEngine/KinematicsPerfTests.cs | 성능 예산 측정 |

- 테스트 asmdef는 이미 `RoadToPro.MatchEngine`을 참조한다 (004). 의존 방향 `MatchEngine → Core`만 — `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
**`Data/tuning/kinematics.json`** (001 튜닝 정의 형식):

| id | 기본값 | 출처 |
|---|---|---|
| kin.vmax.base / range / exp | 7.2 / 2.5 / 0.9 | [D] B-7 · FR-01 |
| kin.accel.base / range | 5.8 / 3.4 | [D] B-7 · FR-02 |
| kin.lat.base / range, kin.decel.base / range | 4.5 / 2.5, 4.5 / 2.5 | [D] B-7 · FR-03 |
| kin.pivot.base / range (°/s) | 450 / 300 | [D] B-7 · FR-03 |
| kin.balance.lossWorst / lossBest, capWorst / capBest | 0.40 / 0.10, 1.0 / 1.1 | [D] B-7 · FR-04 |
| kin.stamina.kfWorst / kfBest, recWorst / recBest | 1.4 / 0.7, 0.7 / 1.3 | [D] B-7 · FR-05 |
| kin.perception.base / range | 0.35 / 0.2 | [D] B-7 · FR-06 |
| kin.motorDelay | 0.075 s (범위 0.05~0.1) | **[제안]** B-5 범위의 가운데, 능력치 연결 없음 |
| kin.arrive.decelFactor / tolerance / stopSpeed | 0.8 / 0.3 / 0.3 | [D] B-9 · FR-20 |
| kin.pursue.maxT, kin.intercept.maxT | 1.5 / 2.0 s | [D] B-9 · FR-22·23 |
| kin.sep.radius / bodyRadius | 1.75 / 0.4 | [D] B-9 · FR-24 |
| kin.mark.offset (1~2) | 1.5 m | **[제안]** FR-25 범위 가운데 |
| kin.jockey.distance (1.5~2.5) / speedRatio / backpedalMax / sideOffset | 2.0 / 0.9 / 4.0 / 0.3 | FR-26 (거리는 **[제안]** 범위 가운데) |
| kin.combine.wSeparation / wBehavior / wFormation | 1.0 / 1.0 / 0.5 | **[제안]** FR-27 "데이터로 조정 가능" |
| kin.pivot.enter / exit | 2.0 / 2.3 m/s | spec 승인 [제안] |
| kin.fatigue.critRatio / recoverRate | 0.55 / 1/60 | [D] B-8 · FR-30 |
| kin.fatigue.acuteDrain (k_a) | 구현 중 보정 | **[제안]** AC-09(연속 스프린트 4회 → W 0.25~0.35, 지구력 10 기준)에 맞춘 값 |
| kin.fatigue.chronicDistRate / chronicHirRate | 구현 중 보정 | **[제안]** FR-31 모델 `dC/dt = k_f·(c_d·v + c_h·max(0, v − v_hi))`, AC-10 표준 프로필에서 지구력 10 → C ≈ 0.75 |
| kin.fatigue.vmaxAcute / vmaxChronic / accAcute / accChronic | 0.06 / 0.05 / 0.04 / 0.02 | [D] B-8 · FR-32 |
| kin.fatigue.curveAcute / curveChronic / curveMin | 0.10 / 0.10 / 0.7 | spec 승인 [제안] FR-36 |
| kin.fatigue.reactionAdd | 0 s (최대 0.08) | spec 승인 [제안] FR-34 |
| kin.stats.hirSpeed / sprintSpeed | 5.5 / 7.0 m/s | spec 승인 [제안] FR-50 |
| kin.stats.accThreshold / accMinDuration | 3.0 m/s² / 0.5 s | [V] B-2 |
| kin.bounds.margin | 3 m | spec 승인 [제안] |

**플랜에서 새로 정하는 해석 (승인 필요, `[제안]`)**
1. **a_cap (FR-11)**: 기획서에 값이 없다. `a_cap = max(a0_eff, 감속 상한)` — 직선 가속(AC-02)을 깎지 않으면서, 횡가속을 쓰면 종가속 예산이 줄어든다. 접촉 중 서브스텝만 `× 초과 배율`(FR-04).
2. **인지지연의 예측력·집중력 결합 (FR-06)**: `s_cog = (예측력 + 집중력) / 2`.
3. **지연 적용 방식**: 결정 틱에서 받은 명령은 `인지지연 + 운동 개시 지연` 뒤에 활성화된다 (큐). 그 전까지는 이전 명령을 계속 따른다. AC-02 스프린트는 Haugen 기준(첫 동작 보정 포함)이라 **움직이기 시작한 시각부터** 잰다 — 모델 해석값 10/20/30/40 m = 2.02/3.24/4.37/5.49 s (능력치 15, 지연 없음).
4. **지구력 배율 k_f**: 급성 W′ 소모(k_a)와 만성 C 누적 둘 다에 곱한다. 회복 배율은 W′ 회복률 r에 곱한다.
5. **AC-16 "1,000경기 검증 실행"**: 006 경기 루프가 아직 없다. 이번 범위에서는 **합성 경기 1,000개 × 5분**(22명, 고정 시드 무작위 명령: Arrive·Pursue·Intercept·저킹·마킹·대형 혼합, 무작위 접촉)으로 한다. 실제 90분 1,000경기는 017 대량 시뮬에서 같은 모니터를 켠다.
6. **AC-10 표준 활동 프로필(고정 입력)**: 60 s 주기 반복 — 걷기 1.1 m/s 38 s · 조깅 2.6 m/s 15 s · 러닝 4.5 m/s 4.5 s · 고강도 6.0 m/s 1.8 s · 스프린트(최고속 목표) 0.7 s. 90분 총거리 약 10.5 km · 고강도 약 1.4 km (B-3 범위). 실제 거리는 가감속 때문에 조금 달라지므로 테스트가 총거리 10~11.5 km 안인지도 함께 확인한다. 테스트 킷에 둔다.
7. **겹침 해소 위치 투영은 운동 한계 검사(FR-52)에서 뺀다** — 속도가 아니라 위치만 고치므로 가속으로 세지 않는다. 대신 투영 크기를 진단에 남긴다.

- **PlayerMotionState 직렬화**: Id(int) + Position·Velocity(float×4) + Heading(float) + Pivoting(byte) + W·C(float) + 활성 명령(kind byte + float×4 + int) + 큐(개수 + 항목) + 기록. 해시는 이 순서. 세이브 배치는 022 (경기 중 저장 없음이라 해시·재현 용도).

## 결정론 · 성능
- 난수 없음. `DMath`만 사용. 선수 처리 순서는 `KinematicsSystem` 생성 시 id 오름차순 정렬(입력 순서 무관, AC-12).
- 서브스텝 안 순서 고정: ① 명령 큐 활성화 ② 행동 목표 속도(모든 선수, 이전 서브스텝 위치 스냅숏 기준 — 앞 선수 이동 결과를 뒤 선수가 보지 않게) ③ Separation 목표 속도 ④ 합성 ⑤ 한계 풀이·적분 ⑥ 겹침 위치 투영(쌍 i<j 순, 2회) ⑦ 피치 경계 자르기 ⑧ 피로 ⑨ 기록·진단·모니터 ⑩ `FloatGuard`.
- 할당 0: 상태 배열·스냅숏·큐는 생성 시 1회. 서브스텝·TTI·인터셉트 탐색은 값 타입만.
- 누적 기록은 double (120분에서도 정밀도 유지, 경계 상황).
- 성능 측정 (EditMode, `Stopwatch`는 테스트 코드에서만): 22명 혼합 명령 서브스텝 평균·p95, TTI 1회, 인터셉트 탐색 2 s / 0.04 s, GC 할당 횟수. 예산 1.1 µs/서브스텝(22명)은 선수당 약 50 ns라 빡빡하다 — Separation은 거리 제곱 빠른 배제, 매핑 값은 미리 계산, 벡터 연산은 스칼라로 푼다. 넘으면 측정값과 함께 보고하고 예산 수정을 제안한다.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `KinematicsMappingTests.Ability15_MatchesB7`, `…Ability1And20_TopSpeedKmh` | 로직 | — |
| AC-02 | `SprintTests.StandingStart_SplitsMatchHaugen` (능력치 15·16, 10/20/30/40 m ±0.1 s) | 로직 | — |
| AC-03 | `SprintTests.TimeTo95Percent_Is3Tau` (±10%) | 로직 | — |
| AC-04 | `CurveTests.Circle5m_SpeedBelowSqrtALatR_NoFrictionCircleViolation` | 로직 | — |
| AC-05 | `ArriveTests.From8ms_DecelStartDistance_StopsWithinTolerance_OvershootBelow03` | 로직 | — |
| AC-06 | `InterceptTests.ChosenPoint_ReachedOnTime_98Percent` (고정 시드 1,000 케이스, 004 공 예측 사용) | 로직 | — |
| AC-07 | `JockeyTests.Defender_KeepsDistance15to25_BackpedalBelow4` | 로직 | — |
| AC-08 | `SeparationTests.TwentyTwoInRadius2_Resolve3s_NoDivergence` | 로직 | — |
| AC-09 | `FatigueTests.FourSprints_WBetween025And035_RecoversAbove06After60s` | 로직 | — |
| AC-10 | `FatigueTests.StandardProfile90min_C06to09_Stamina20LowerThan5` | 로직 | — |
| AC-11 | `KinematicsEdgeCaseTests.ZeroDistance_`, `…TargetOutsidePitch_Clamped`, `…SamePosition_DeterministicSplit`, `…PivotBoundary_NoChatter`, `…Match120min_NoNaN_FatigueBounded`, `…InvalidAttributes_Rejected` | 로직 | — |
| AC-12 | `KinematicsDeterminismTests.NinetyMin22Players_TwoRuns_SameHash`, `…ShuffledInputOrder_SameHash` `[Category("Determinism")]` | 결정론 | — |
| AC-13 | `KinematicsPerfTests.Substep22_Avg_us`, `…Substep22_P95_us`, `…Tti_ns`, `…BallIntercept_us`, `…ZeroAlloc_StepTtiIntercept` → `Data/perf/005-player-kinematics.json` | 성능 | mac-m1-1600p |
| AC-14 | `e2e/005-kinematics-debug` — **040 디버그 뷰 완료 후** (004 AC-15와 같은 보류, `deferred`). 이번 범위에서 만들지 않는다 | E2E | smoke |
| AC-15 | `KinematicsDiagnosticsTests.DiagOnOff_SameHash` `[Category("Determinism")]` + `KinematicsPerfTests.ZeroAlloc_DiagOff` | 결정론 | — |
| AC-16 | `MotionLimitTests.SyntheticMatches1000x5min_NoViolation`, `…ForcedAccelOverCap_ReportedExactly_Fails` | 로직 | — |

검증: `python3 Tools/devicefarm/farm.py logic --spec 005`.

## 버그 방지
- 0으로 나누기: d < 1e-4, v < 1e-4에서 방향 계산 안 함 (`Vec2.Normalized` 0 반환 + 명시 분기). Pursue 분모 `v_self + v_target·cosθ ≤ 0.1` → T 상한.
- 피벗 떨림: 히스테리시스 2.0/2.3, 테스트가 2 m/s 근처 왕복 입력에서 상태 전환 횟수를 센다.
- Arrive 오버슈트: 서브스텝 끝 위치가 목표를 넘으면(이동 방향과 목표 방향 내적 < 0) 목표에 맞추지 않고 감속 상한으로만 제동 — 테스트가 오버슈트 ≤ 0.3 m 확인.
- 겹침 해소 발산: 투영량 상한(서브스텝당 0.2 m), 반복 2회 고정. 22명 혼잡 테스트가 속도 > Vmax 0건 확인.
- 명령 큐 넘침: 용량 8(최대 지연 0.43 s + 여유, 10 Hz면 5개) 넘으면 가장 오래된 미활성 명령을 버리고 진단 경고 — 조용히 무한 지연하지 않음.
- W·C는 매 스텝 [0,1] 클램프, 피로 효과 계수 합이 1을 넘어 Vmax_eff ≤ 0이 되지 않도록 로딩 검증.
- NaN: 스텝 끝 `FloatGuard.Check` (테스트에서 Strict).
- 튜닝 정의 파일을 테스트가 직접 로드 → 누락·범위 오류를 바로 잡는다.

## 위험 · 미정
- 성능 예산(22명 1.1 µs)이 Mono EditMode에서 빡빡할 수 있다 (004 공 1개 Air 226 ns). 측정 후 넘으면 보고.
- AC-06(98%)은 TTI 근사(모노-지수 + 투영)와 실제 조향(마찰원·지연)의 차이에 달렸다. 못 맞추면 TTI가 아니라 "탐색이 쓰는 TTI = 실제 조향" 쪽을 맞춘다(같은 함수 원칙, FR-41).
- AC-09·AC-10의 k_a·c_d·c_h는 테스트 조건에 맞춰 정한 값이라 017 대량 시뮬에서 다시 본다 (튜닝 데이터에 `[제안]` 태그).
- 크로스플랫폼 부동소수점 결정론은 결정 대기 — "같은 빌드·같은 플랫폼" 기준.
- 002가 나중에 `KinematicAttributes`를 채울 때 이름(002 FR-20 등록부)과 맞춘다.
- (구현 중 결정, 2026-11-03) 저킹 "공격수 속도 × 0.9"는 **접근할 때만** 적용(하한 1.5 m/s), 후퇴는 백페달 4 m/s 상한. 마킹·저킹은 대상 스냅숏을 서브스텝마다 읽는다(인지 지연은 명령 전환에만). 인터셉트 행동은 "전력 Seek → 컨트롤 반경 안에서 Arrive". 겹침 해소는 목표 간격 0.84 m·반복 4회·선수당 0.35 m. 초과 속도 감속을 마찰원에서 횡가속보다 먼저 확보. 상세는 tasks.md 결과 줄.
- (T16 리뷰, 참고 — 반영 안 함) 진단 콜백 안에서 `SetCommand`·`ApplyContact`를 부르면 스텝 중간에 상태가 바뀐다(인터페이스 주석으로만 금지). 같은 서브스텝에 `ApplyContact` 2회면 손실이 두 번 곱해진다 — 008이 1회만 부르도록 정한다. 해시에 프로필(능력치)은 없다 — 006 체크포인트에 라인업·튜닝 해시를 함께 넣는다. `InjectVelocityFault`는 검증 전용 공개 API. 피치 경계 클램프가 투영 뒤라 경계에서 다음 스텝까지 겹칠 수 있다.

# 004 — 공 물리 · 공 상태 머신 · 플랜

상태: 승인됨
스펙: ./spec.md

## 접근
공은 `RoadToPro.MatchEngine`의 `Ball` 네임스페이스에 **순수 값 상태(`BallState`) + 정적 적분 함수(`BallStepper.Step`)** 로 둔다. 실제 시뮬·궤적 예측기·역산이 모두 이 한 함수를 부르므로 "예측 = 실제"(FR-30, AC-09)가 구조로 보장된다. 파라미터는 `Data/tuning/ball.json`(튜닝 정의 JSON, 001 로더)에만 있고, 코드에는 id 문자열만 있다. 로드 시 공 종류·표면 조건을 골라 미리 계산된 읽기 전용 `BallParams`(β, 스핀 감쇠 배율 `exp(−k·dt)` 등)를 만든다 — 서브스텝마다 튜닝 묶음을 조회하지 않는다. 적분은 semi-implicit Euler, dt 0.01 고정(001 `SimClock`). 이벤트(전이·바운스·골대 접촉·경계 통과·입력 클램프)는 호출자가 재사용하는 고정 용량 버퍼에 쓰고 할당하지 않는다. 땅볼 역산은 `BallParams` 생성 시 실제 구름 모델로 한도 속도에서 한 번 굴린 궤적 표를 만들고(1D 구름 ODE는 자율계라 "v0에서 vr까지 거리 = S(v0) − S(vr)"), 표 보간만으로 초속을 구한다(오차 약 0.0004 m, AC-10이 0.2 m 보장 — 2026-11-02 결정으로 런타임 재검증 시뮬 제거). RK4 예측기(A-8 대안)는 예측·실제가 어긋나므로 쓰지 않는다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallPhase.cs | `BallPhase {Controlled, Air, Rolling, Rest}` (Bounce는 순간 상태라 저장하지 않고 이벤트로만 남김, FR-12), `BallTransitionReason` |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallState.cs | 상태 값 타입: Phase, Position(Vec3), Velocity(Vec3), Spin(Vec3, rad/s), OwnerId(int, Controlled일 때), `AddTo(ref StableHasher)` (세이브·해시용) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallType.cs, SurfaceCondition.cs | `BallType {Panel32, Modern}`, `SurfaceCondition {Dry, Wet, Artificial, Flooded, LongGrass}` |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallTuningIds.cs | 튜닝 id 상수 (값 없음) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallParams.cs | `BallParams.Create(TuningBundle, BallType, SurfaceCondition)` → 불변 sealed class. 미리 계산: β, 스핀 감쇠 배율, 표면별 e·a·b, 정지거리 표. 로딩 검증(a<0, e>1 → `TuningLoadException`, 정의 범위로 1차 차단 + 조합 후 2차 확인) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallAero.cs | `Cd(v)`(FR-02), `ClOf(Sp)`(FR-03), 저속 페이드, 공기 가속 계산. v < 1e-4 → 0 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/GoalFrame.cs | 피치·골대 기하 (105×68, 골 폭 7.32·높이 2.44, 포스트·크로스바 원기둥 r 0.06, 골망 상자). `PitchSpec` 재사용 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallEvent.cs, BallEventBuffer.cs | 이벤트 값 타입(종류·서브스텝 내 시각 비율·위치·입사/반발 속도·전이 전후 상태·경계 종류·골문 안 여부) + 고정 용량(16) 재사용 버퍼 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallStepper.cs | `Step(ref BallState, BallParams, BallEventBuffer, out BallSegment)` — 서브스텝 1회. Air 적분 → 골대 swept 접촉 → 지면 접촉·바운스(FR-23) → Rolling 감속 → Rest → 경계 통과 → 골망 → `FloatGuard` 확인. 이동 선분 `BallSegment(start, end)` 출력 (FR-22) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallCommands.cs | 외부 입력: `Kick(ref state, vel, spin, lofted)` (속도 40 m/s·스핀 180 rad/s 클램프 + 경고 이벤트), `TakeControl(ref state, ownerId, pos)`, `SetControlledPosition` (FR-10, FR-13) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallPredictor.cs, BallPrediction.cs | 상태 복사본으로 `BallStepper.Step`을 반복 (같은 모델·같은 dt). 샘플 간격 0.04~0.1 s(서브스텝 4~10의 정수배), 상한 10 s. 출력 버퍼(호출자 재사용): 샘플 배열 + 최초 착지·정지·경계 통과 시각/지점 (FR-30~32) |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallInverse.cs, BallRollTable.cs | `GroundSpeedFor(distance, residualSpeed)` (FR-33, 표 보간만 — 2026-11-02 결정), `LoftedSpeedFor(distance, launchAngle, spin, maxApex, scratch)` (FR-34, 호출자 버퍼). 결과 struct: Ok/Unreachable/ApexExceeded, 초속, 시간, 정점 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/BallPath.cs, BallSegment.cs | 골대에 맞아 꺾인 서브스텝 경로(최대 4점, 점마다 시간축 비율) — 골대·지면·경계 이벤트가 같은 시간축을 쓴다 (리뷰 R-3). 골라인 근처에서만 만든다 |
| RoadToPro.MatchEngine | MatchEngine/Runtime/Ball/IBallDiagnostics.cs | 진단 출력 인터페이스 (상태·100Hz 궤적 점·예측 착지점·스핀 축/크기·바운스 지점, FR-43). null이면 호출 0 |
| 데이터 | Data/tuning/ball.json | 튜닝 정의(아래 표). 출처 태그는 기획서 그대로 |
| 데이터 | Data/perf/004-ball-physics.json | 성능 예산 기계용 사본 + 테스트 네임스페이스 |
| RoadToPro.Tests.EditMode | Tests/EditMode/MatchEngine/Ball/*Tests.cs, BallTestKit.cs | 로직·결정론 테스트. `BallTestKit`이 `Data/tuning/ball.json`을 읽어 `BallParams`를 만든다 |
| RoadToPro.Tests.Performance | Tests/Performance/MatchEngine/BallPerfTests.cs | 성능 예산 측정 |

- 테스트 asmdef 두 개에 `RoadToPro.MatchEngine` 참조가 이미 있는지 확인하고, 없으면 추가한다.
- 의존 방향 확인: `MatchEngine → Core`만 사용. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
**`Data/tuning/ball.json`** (001 튜닝 정의 형식 `{"version":1,"definitions":[…]}`):

| id | 기본값 | 범위 | 출처 |
|---|---|---|---|
| ball.mass / ball.radius / ball.airDensity / ball.gravity | 0.43 / 0.109 / 1.2 / 9.81 | 물리 범위 | [V] A-1 |
| ball.drag.panel32.cdLow / cdHigh / vCrit / w | 0.47 / 0.20 / 11 / 1.5 | | [D] A-2 |
| ball.drag.modern.cdLow / cdHigh / vCrit / w | 0.45 / 0.15 / 16 / 1.5 | vCrit 15~18 | [D] A-2·A-8 (임시 기본, 2026-11-02 사용자 결정) |
| ball.defaultType | 1 (Modern) | 0~1 | 사용자 결정 2026-11-02 |
| ball.magnus.clMax / spScale / minSpeed | 0.35 / 0.15 / 8 | | [D] A-3 |
| ball.magnus.fadeWidth | 2 m/s | 0~5 | **[제안]** 8 m/s부터 8+폭까지 C_L 선형 페이드 (FR-03 "페이드아웃"의 폭은 기획서에 없음) |
| ball.spin.decay | 0.07 | 0.05~0.1 | [D] A-4 |
| ball.bounce.e.dry / wet / artificial | 0.62 / 0.74 / 0.65 | 0~1 | [D] A-5 |
| ball.bounce.mu / gripKeep / gripSpin | 0.5 / 0.6 / 0.4 | | [D/U] A-5 |
| ball.bounce.toRollVz | 0.5 | | [D] A-5·아키텍처 5 |
| ball.roll.a / b / toRestSpeed | 0.40 / 0.10 / 0.05 | a ≥ 0 | [D] A-6 |
| ball.roll.wetShortA / floodedA / floodedB / longGrassA (배율) | 0.8 / 2.5 / 2 / 1.3 | | [D] A-6 |
| ball.limit.speed / spin | 40 / 180 | | spec 경계 상황 (A-4 런처 최대) |
| ball.goal.postRadius / postRestitution | 0.06 / 0.6 | | 2026-11-02 승인 |
| ball.net.depth / damping | 2.0 m / 0.85 (서브스텝당 속도 배율) | | **[제안]** FR-42 골망 감속 영역의 크기·세기 (승인된 건 "영역이 있다"까지) |

**표면 조건 → 파라미터 매핑** (FR-05·06이 표면 5개와 e 3개를 따로 적어 연결이 필요):

| SurfaceCondition | 반발 e | 구름 보정 |
|---|---|---|
| Dry | dry 0.62 | 없음 |
| Wet (젖은 짧은 잔디) | wet 0.74 | a×0.8 |
| Artificial | artificial 0.65 | 없음 |
| Flooded | wet 0.74 **[제안]** | a×2.5, b×2 |
| LongGrass | dry 0.62 **[제안]** | a×1.3 |

- **BallState 직렬화**: Phase(byte) + Position·Velocity·Spin(float×9) + OwnerId(int). 세이브 배치는 022. 해시는 이 순서로 `StableHasher`에.
- **경계 통과 판정 기준**: 라인 바깥 가장자리 = 피치 경계(x=0, x=105, y=0, y=68). "공 전체가 넘음" = 공 중심이 경계에서 반지름 이상 바깥 (FR-41). 이전 서브스텝 안쪽 → 이번 바깥이면 선형 보간으로 시각 비율·지점을 내고, 골라인이면 골문 안(포스트 사이·크로스바 아래) 여부를 같이 낸다. 다시 들어왔다 나가면 또 낸다.
- **골대 좌표**: 포스트 중심 y = 34 ± (3.66 + 0.06), 골라인 위(x = 0 / 105). 크로스바 중심 z = 2.44 + 0.06.

## 결정론 · 성능
- 난수 없음. 같은 입력 → 같은 비트 (`DMath`만 사용, `MathF` 직접 호출 금지 — 훅이 검사).
- 한 서브스텝 안 처리 순서 고정: ① Air 적분 ② 골대 접촉(시각 비율 오름차순, 동시면 왼쪽 포스트 → 오른쪽 포스트 → 크로스바, 서브스텝당 최대 2회) ③ 지면 접촉·바운스 ④ Rolling 감속·Rest ⑤ 경계 통과 ⑥ 골망 ⑦ `FloatGuard`. 동시 발생 이벤트는 시각 비율 순으로 버퍼에 들어간다.
- 지면 접촉: z가 반지름 아래로 내려간 스텝에서 z 선형 보간으로 접촉 지점을 구하고 그 지점에 두고 반발. 남은 시간은 적분하지 않는다 (지면 아래 상태가 다음 스텝에 남지 않음).
- 할당 0: 상태·이벤트·선분은 값 타입, 버퍼는 호출자 재사용. 정지거리 표는 `BallParams` 생성 시 1회만 할당.
- 성능 측정 (EditMode, `Stopwatch`는 테스트 코드에서만):
  - 상태별 서브스텝 ns: Air(스핀 있음, 최악), Rolling, Controlled·Rest. 가중 평균은 **[제안] 가중치 Controlled 50% · Rolling 25% · Air 15% · Rest 10%** (스펙의 "경기 중 상태 가중 평균"의 가중치가 기획서에 없음. 006 이후 실제 경기 분포로 교체).
  - 예측 10 s / 0.04 s, 땅볼 역산 1회(5~45 m 무작위 고정 시드), GC 할당 횟수(`Recorder("GC.Alloc")`).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `BallRollingTests.StopDistanceAndTime_MatchA6Table` (Panel32, Dry, 초속 7개, 거리 ±3%·시간 ±5%) | 로직 | — |
| AC-02 | `BallRollingTests.Roll32_WithinUefaFifaRange` | 로직 | — |
| AC-03 | `BallFlightTests.NoSpin_RangeAndFlightTime_MatchA7` (Panel32, 3케이스) | 로직 | — |
| AC-04 | `BallFlightTests.ZeroDrag_MatchesVacuumRange` (±1%) | 로직 | — |
| AC-05 | `BallMagnusTests.Curl_DirectionFollowsRightHandRule`, `…FreeKick36ms_Lateral2to6m` | 로직 | — |
| AC-06 | `BallMagnusTests.Below8ms_MagnusZero` | 로직 | — |
| AC-07 | `BallBounceTests.Drop2m_Dry_FirstRebound077`, `…Wet_HigherThanDry` | 로직 | — |
| AC-08 | `BallBounceTests.RepeatedBounce_RollsWithin20_ThenRest`, `BallStateMachineTests.AllTransitions_InAllowedGraph` (무작위 킥 1,000개의 전이 로그 검사) | 로직 | — |
| AC-09 | `BallPredictorTests.Prediction_EqualsActual_10s_Exact` (Air·바운스·Rolling·골대 포함 여러 킥) + `…_DoesNotMutateState` | 로직 | — |
| AC-10 | `BallInverseTests.Ground_5to45m_ErrorWithin02`, `…ResidualSpeed`, `…Unreachable`, `…Lofted_LandsOnTarget_OrUnreachable` | 로직 | — |
| AC-11 | `BallBoundaryTests.CrossbarDown_OnLine_NoSignal`, `…FullyOver_SignalOnce_InterpolatedTime`, `…Touchline` | 로직 | — |
| AC-12 | `BallEdgeCaseTests.ZeroVelocity_`, `…ExtremeInput_ClampedWithWarning`, `…BelowGround_CorrectedSameStep`, `…PostAndCrossbar_SameSubstep_Ordered`, `…InvalidSurfaceParams_RejectedAtLoad`, `…SpinOnly_NoMagnus` | 로직 | — |
| AC-13 | `BallDeterminismTests.TenThousandInitialStates_TwoRuns_SameHash` `[Category("Determinism")]` | 결정론 | — |
| AC-14 | `BallPerfTests.Substep_Air_ns`, `…Substep_Rolling_ns`, `…Substep_Idle_ns`, `…Substep_WeightedAvg_ns`, `…Predict10s_us`, `…GroundInverse_us`, `…ZeroAlloc_StepPredictInverse` → `Data/perf/004-ball-physics.json` 예산 | 성능 | mac-m1-1600p |
| AC-15 | `e2e/004-ball-trajectories` — **040 디버그 뷰 완료 후** (2026-11-02 결정). 이번 범위에서는 만들지 않는다 | E2E | smoke |
| AC-16 | `BallDiagnosticsTests.DiagOnOff_SameEventAndCheckpointHash` `[Category("Determinism")]` + `BallPerfTests.ZeroAlloc_DiagOff` | 결정론 | — |

검증: `python3 Tools/devicefarm/farm.py logic --spec 004` (EditMode 로직·결정론·성능 → `evidence/`).

## 버그 방지
- 0으로 나누기: v̂·Sp·정규화는 v < 1e-4에서 0 처리 (`Vec3.Normalized`는 이미 0 반환). 스핀만 있는 공 → 마그누스 0.
- 바운스 무한 반복: 반발 수직속도 < 0.5 → Rolling 강제. 테스트가 20회 상한 확인.
- Rolling 감속이 한 스텝에 속도를 음수로 만들면 그 스텝에서 정지(방향 뒤집힘 금지). 속도 < 0.05 → Rest.
- 지면 아래 입력 위치: Air·Kick 입력 모두 같은 스텝에 보정.
- 골대 모서리: 포스트·크로스바 동시 접촉을 고정 순서로, 서브스텝당 최대 2회 (무한 루프 방지).
- 경계 통과: "선 위 걸침"은 신호 없음, 다시 들어왔다 나가는 경우 두 번 신호. 보간 시각 비율은 [0,1]로 클램프.
- 예측기 샘플 간격이 서브스텝 정수배가 아니면 `ArgumentException` (실제와 어긋남 방지). 버퍼 용량 부족도 예외 — 조용히 자르지 않는다.
- 역산: 거리 ≤ 0, 잔여속도 ≥ 한도, 40 m/s로도 못 가는 거리 → Unreachable. 할선 반복 상한 8회.
- NaN·무한대: 스텝 끝 `FloatGuard.Check` (디버그 Strict → 테스트 실패).
- 튜닝 정의 파일 자체를 테스트가 로드 → 정의 누락·범위 오류를 바로 잡는다.

## 위험 · 미정
- **[제안] 승인됨 (2026-11-02)**: 마그누스 페이드 폭 2 m/s, 골망 깊이 2.0 m·감속 0.85/서브스텝, 침수=젖음 e·긴 잔디=건조 e, 성능 가중치(50/25/15/10). 튜닝 데이터에는 `[제안]` 태그 그대로 두고 017·006 이후 재검토.
- **AC-15 보류와 완료 판정**: 현재 하네스는 `/verify` 때 모든 AC에 증거를 요구한다(`requireAllAcs`). AC-15가 없으면 리포트 판정이 `fail`이 된다. **결정 (2026-11-02)**: 하네스를 고치지 않는다. 004 상태를 "구현 완료 · AC-15 보류"로 두고 040 완료 후 `완료`로 바꾼다.
- 성능 (EditMode Mono 실측, 2026-11-02): 가중 평균 ≈ 95 ns(예산 100, 여유 적음), Air ≈ 226 ns, Rolling ≈ 190 ns, Idle ≈ 10 ns, 예측 10 s ≈ 218 µs(예산을 250 µs로 수정 — 1,000 서브스텝이라 20 µs는 모순), 땅볼 역산 ≈ 0.05 µs. 벡터 연산을 스칼라로 풀고 경계·골대·골망 판정 앞에 빠른 배제를 둬서 얻은 값.
- (T16 리뷰, 참고 — 반영 안 함)
  - N-2: 역산은 허용 오차 기반이라 실제 경로와 비트 동일은 아니다 (뜬공은 (5,5)에서 +x 프레임, 땅볼은 스칼라 표).
  - N-3: 공 중심이 정확히 골라인 위·vx 정확히 0으로 크로스바 윗면에 놓이면 미세 반발이 이어질 수 있다 (확률 극히 낮음).
  - N-5: `default(BallState)`는 "선수 0이 소유한 Controlled"다. 초기화 누락이 유효 상태로 통과한다 — 006에서 생성 경로를 팩토리로 한정.
  - N-6: 체크포인트에는 튜닝 묶음 해시(`TuningBundle.Hash`)·공 종류·표면을 함께 넣어야 재현된다 (006).
  - N-7: Mono(에디터)와 IL2CPP(ARM64 FMA 축약)는 비트가 다를 수 있다 — 결정론 비교는 같은 런타임 안에서만.
  - `AllFinite`의 `sum − sum == 0` 빠른 경로는 fast-math(부동소수점 재결합) 컴파일 옵션을 켜면 NaN을 놓칠 수 있다. IL2CPP에 그런 옵션을 켜지 않는다.
  - 경계 이벤트의 phase는 스텝 끝 상태다 (같은 서브스텝에서 바운스 전 통과도 바운스 후 상태로 기록).
  - 이벤트 버퍼 계약은 "서브스텝마다 Clear" (Step 최대 7개 + 수치 보정 시 1건으로 교체). 006은 결정 틱 로그로 옮겨 담는다.
- AC-01·AC-03 기준표는 리서치가 같은 모델로 계산한 값이라, 적분 방식(semi-implicit Euler, dt 0.01)의 차이로 ±3%를 넘을 수 있다. 넘으면 모델이 아니라 기준표 계산 조건을 먼저 확인하고 보고한다.
- 크로스플랫폼 부동소수점 결정론은 결정 대기 — "같은 빌드·같은 플랫폼" 기준.

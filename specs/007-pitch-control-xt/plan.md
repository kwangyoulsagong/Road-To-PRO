# 007 — 피치 컨트롤 · 패스 판단 연결 (D19) · 플랜

상태: 완료 (2026-11-18 T21 검증): AC-01~07·09~12 통과, AC-08 보류(D19 패스 수·성공률 — FR-26, README D19 결정 대기). 리포트 https://claude.ai/artifact/5w1Rvkbyj4FVjkVuxZxTTM (이전: 승인됨 (2026-11-18 사용자 "ㄱㄱ"))
스펙: ./spec.md

## 접근
`MatchEngine/Runtime/PitchControl/`(지금 빈 폴더)에 Spearman 피치 컨트롤을 순수 C#으로 만든다. 입력은 008 `ActionWorld`가 매 틱 이미 만드는 005 TTI 컨텍스트(`Tti[]`)·`Active[]`·능력치를 그대로 쓰고, 결과는 상태 없는 질의(지점·궤적·그리드)로 낸다. 연결은 두 군데뿐이다: ① `PassModel.Best`의 `receiverPC` 입력을 `ActionWorld.Control`(대체 로지스틱)에서 지점 질의로 바꾸고, ② `OnBallDecider`의 ④ 점수 단계 뒤·⑤ 노이즈 전에 패스 후보 중 효용 상위 K개만 궤적 질의로 성공 확률 `P`를 다시 정하고 그 후보만 다시 점수 매긴다. 대체 패스 선택 경로(`StandInOnBall`, 데드볼·GK 배급)도 같은 재평가 함수를 거친다.

대안: (a) 모든 패스 후보(최대 ~30개)에 궤적 질의 — 결정 1회 ≈ 30 × 8 µs로 예산(30 µs) 초과라 버림. (b) 기존 `PhysicsXPass`(100 Hz `BallStepper` 300스텝 + 첫 도달 순간 수신 확률로 질량 이동)를 그대로 쓰기 — 질의 1회 비용이 크고(벤치 기준 수십 µs) 스펙 FR-02·03 적분식이 아니라서 버리고, 새 궤적 질의로 교체한다(FR-12).

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `Runtime/PitchControl/PitchControlParams.cs` | FR-01~07 파라미터(σ, λ, κ_def, λ_gk 배수, dt, 최대 시간, 허용치, 3σ 가지치기, 뜬공 높이, GK 손 높이, 원 논문 모드 반응·v_max, 능력치 연결 범위, K, 동료 수신 가치, 그리드 크기). `Create(TuningSet)` + 코드 기본값 |
| RoadToPro.MatchEngine | `Runtime/PitchControl/PitchControlTuningIds.cs` | `Data/tuning/action.json`의 `pcm.*` id (기존 `act.pc.scale`은 대체 근사용으로 그대로 둠) |
| RoadToPro.MatchEngine | `Runtime/PitchControl/ControlPlayers.cs` | 질의 입력 뷰(슬롯 22개): TTI 컨텍스트 참조, Active, 팀, λ_j(능력치 연결 결과), GK 여부, 원 논문 모드용 위치·속도. `ActionWorld.Refresh`가 채운다 (질의는 ActionWorld를 모른다) |
| RoadToPro.MatchEngine | `Runtime/PitchControl/PitchControlQuery.cs` (클래스 `PitchControlQuery` — 네임스페이스 `PitchControl`과 이름이 겹쳐 T06에서 바꿈) | 질의 본체(인스턴스 버퍼 재사용, 경기당 1개): `Point(players, target, ballArrival, out PointControl)`, `Trajectory(players, in TrajectorySamples, passer, receiver, out TrajectoryControl)`, `Grid(players, ballPos, buffer)`. 순수 함수형(상태 = 스크래치 버퍼만) |
| RoadToPro.MatchEngine | `Runtime/PitchControl/TrajectorySamples.cs` | 공 명목 궤적을 0.04 s 간격 (x, y, z, t) 샘플로: 땅볼 = 004 구름 표(`RollTable`) 샘플(스텝 시뮬 없음), 뜬공 = `BallStepper` 상태 복사본 4스텝마다 샘플. 라인 밖 넘는 순간·멈춤·상한(3 s) 표시 |
| RoadToPro.MatchEngine | `Runtime/PitchControl/PitchControlResults.cs` | `PointControl`(팀 0·1·미통제, 선두 선수), `TrajectoryControl`(수신자·동료·상대·아웃) 값 타입 |
| RoadToPro.MatchEngine | `Runtime/PitchControl/IPitchControlDiagnostics.cs` | FR-27 진단 콜백(널이면 호출 0) |
| RoadToPro.MatchEngine | `Runtime/Actions/ActionWorld.cs` (수정) | `ControlPlayers` 소유·`Refresh`에서 채움. `Control(...)`은 모드 스위치: 기본 = 지점 질의 팀 컨트롤, `pcm.legacy = 1`이면 기존 로지스틱(FR-20 비교용) |
| RoadToPro.MatchEngine | `Runtime/Actions/PassModel.cs` (수정) | `receiverPC` 출처만 바뀜(식 그대로). `Refine(...)`: 후보 하나에 궤적 질의 → 판단 P = 수신자 + 동료 × 가치 |
| RoadToPro.MatchEngine | `Runtime/Actions/PhysicsXPass.cs` (수정) | 내부를 `TrajectorySamples` + `PitchControlQuery.Trajectory`로 교체, 공개 API(`Evaluate` → `XPassResult`) 유지 — 키 모먼트·벤치 호출부 그대로 |
| RoadToPro.MatchEngine | `Runtime/UtilityAI/OnBallDecider.cs` (수정) | ④ 뒤 `RefinePasses`: 패스 항목(Short·Prog·Switch·Through, 크로스 제외 FR-23) 중 `Utility` 상위 K개(동점 = 생성 순서) → `P` 갱신 → `_scorer.Score` 재실행. 재평가 뒤 패스 최고 효용이 아직 미질의 후보면 그것도 질의(최대 K + 2회) — 낙관적 미질의 후보가 선택되는 구멍 방지 |
| RoadToPro.MatchEngine | `Runtime/Match/Stages/StandIn/StandInOnBall.cs` (수정) | 대체 패스 선택: 점수 상위 K개를 같은 `Refine`로 재평가 후 최댓값 |
| RoadToPro.MatchEngine | `Runtime/Actions/ActionSystem.cs` (수정) | `IntentAction`의 `XPass` = 판단 P(재평가된 값) → 이벤트 F1로 기록되어 보정도(FR-24) 측정 |
| — | `Data/tuning/action.json` (수정) | `pcm.*` 키 추가 (코드 기본값과 일치 테스트) |
| RoadToPro.Tests.EditMode | `Tests/EditMode/MatchEngine/PitchControl/*.cs` | AC-01~06·10·11 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/MatchEngine/PitchControl/PassRefineTests.cs`, `MatchFeelReportTests.cs`(측정 항목 추가) | AC-07~09 |
| RoadToPro.Tests.Performance | `Tests/Performance/MatchEngine/PitchControlPerfTests.cs` | AC-12 Mono 회귀 기준선 |
| RoadToPro.Verification | `Runtime/Bench/PitchControlBenchWorkloads.cs`, `Data/perf/007-*.json` | AC-12 IL2CPP 판정 |
| — | `Tools/pitchcontrol/reference.py` | AC-01 독립 참조 구현(같은 식, numpy 없이 표준 라이브러리) → `Data/tests/pitchcontrol-reference.json` 고정값 |

의존 방향 확인: 전부 `RoadToPro.MatchEngine` 안. `PitchControl` 네임스페이스는 `Kinematics`·`Ball`·`Core.Math`만 참조하고 `Actions`·`UtilityAI`를 모른다(`Actions`가 `PitchControl`을 쓴다). `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- `Data/tuning/action.json`에 `pcm.*` 추가: `sigma 0.45`, `lambda 4.3`, `kappaDef 1.0`, `gkMul 3`, `dt 0.04`, `maxT 10`, `tol 0.01`, `prune 3`(σ 배수), `airZ 2.5`, `gkHandZ 3.0`, `paperReaction 0.7`, `paperVmax 5`, `attr 1`(능력치 연결 on), `lambdaDefMin 3.0`·`lambdaDefMax 5.5`(FR-06 태클), `lambdaAttMin`·`lambdaAttMax`([설계 미정] → 기본 = λ 4.3 고정, 범위는 `[미정]`이라 연결 계수 0으로 시작), `refineK 3`, `mateValue 0.5`, `refineExtra 2`, `gridW 32`·`gridH 20`, `horizon 3`, `legacy 0`.
  - FR-06 λ_att ← 퍼스트터치, λ_gk ← 핸들링의 범위 수치는 기획서에 없다 → 기본은 연결 끔(계수 0, 원 값). 태클 → λ_def만 리서치 02 §0 범위 3.0~5.5로 켠다.
- 세이브 스키마 변경 없음 (질의는 상태 없음). 체크포인트 형식 그대로.
- 엔진 모드 TTI 반응은 005 프로필 값(현재 대체 능력치에서 나옴) — FR-06 반응 연결은 원 논문 모드에서만 적용.

## 결정론 · 성능
- RNG 없음. 선수 합산은 슬롯 0→21 고정. 가지치기 순서도 슬롯 순으로 판정(하한 계산은 순서 무관) → 입력 순서를 섞는 테스트는 `ControlPlayers`를 섞어 채운 뒤 슬롯 재배치가 결과에 영향 없음을 확인.
- 적분: `PC_j += (1 − ΣPC)·P_j(T)·λ_j·dt` 를 스텝마다 전 선수 계산 뒤 합산(순서 고정), ΣPC > 1이면 비례 정규화.
- 가지치기(FR-04): 하한 `τ_lb = t_r + ‖Δ‖·InvVm`(ActionWorld.Control과 같은 하한)이 `T + prune·σ`보다 크면 그 스텝에서 P_j 계산 생략(P < 0.5 %). 생략한 선수는 이후 스텝에서 다시 판정 — 결과 차이 ≤ 1e-3을 AC-12 테스트로 확인.
- 궤적 질의 샘플: 3 s / 0.04 = 최대 75샘플 × 가지치기 뒤 평균 2~4명 TTI(`Estimate(in Context, x, y)`) → 목표 ≤ 15 µs. 땅볼 샘플은 구름 표 조회라 스텝 시뮬보다 싸다.
- 할당 0: 모든 버퍼는 `PitchControl` 인스턴스 필드(크기 22·76·640 고정). `ActionSystem`이 1개 소유.
- 측정: EditMode `PitchControlPerfTests`(회귀 기준선) + IL2CPP `farm.py bench --spec 007` 워크로드: 지점 질의 단건·궤적 질의 단건·그리드 1회·공 소유자 결정(실경기)·010 결정 틱·실경기 틱·틱당 GC.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `PitchControlReferenceTests.PaperMode_FiveLayouts_MatchReference` (`Data/tests/pitchcontrol-reference.json` ← `Tools/pitchcontrol/reference.py`) | 로직 | — |
| AC-02 | `PitchControlModelTests.SymmetricLayout_EqualControl` | 로직 | — |
| AC-03 | `PitchControlModelTests.Point_SumsToOne`, `Trajectory_SumsToOne` (무작위 고정 시드 1,000) | 로직 | — |
| AC-04 | `PitchControlModelTests.MovingCloser_NeverDecreases` (1,000 케이스) | 로직 | — |
| AC-05 | `TrajectoryControlTests.BlockedGroundPass_OpponentOver50`, `OpenSpace_ReceiverOver90`, `AboveAirZ_NoFieldGain` | 로직 | — |
| AC-06 | `PitchControlEdgeTests.*` (0명 팀·비수렴·거리 0·피치 밖·라인 밖·멈춤·패서 제외·퇴장 직후) | 로직 | — |
| AC-07 | `PassRefineTests.BlockedLane_LowersP`, `OpenLane_DropsAtMost005` + 기존 `PassModelTests`·`OnBall*Tests` 회귀 (scope match) | 로직 | — |
| AC-08 | `MatchFeelReportTests` 100경기 (시드 7400~) → `evidence/measure-after.json`: passes·passSuccess·`passXpMean − passSuccess` | 로직 | — |
| AC-09 | 같은 측정의 stealPerTouch·gkSaveRate·conversion·xgPerShot·takeOnSuccess 범위 판정 | 로직 | — |
| AC-10 | `PitchControlDeterminismTests.Queries_1000States_SameHash_Shuffled` + 기존 `Determinism` 카테고리 경기 2회 해시 | 결정론 | — |
| AC-11 | `PitchControlDeterminismTests.DiagnosticsOnOff_SameHashes`, `DiagnosticsOff_ZeroAlloc` | 결정론 | — |
| AC-12 | `PitchControlPerfTests.*`(Mono 기준선, 가지치기 ≤ 1e-3 비교, 그리드 호출 0) + `farm.py bench --spec 007` (`Data/perf/007-pitch-control.json`) | 성능 | mac-m1-1600p (IL2CPP) |

## 버그 방지
- **판단·실행 어긋남**: 판단은 명목 궤적, 실행은 오차가 붙는다 — 보정도(FR-24)로 감시. 판단 P가 이벤트에 기록되는지 `PassRefineTests.IntentXPass_IsRefinedP`로 확인.
- **미질의 후보 낙관 편향**: 상위 K만 재평가하면 K+1위(로지스틱 낙관값)가 이길 수 있다 → `refineExtra` 루프 + 테스트 `UnrefinedBest_GetsRefined`.
- **경계**: 구름 표 범위 밖(멈춤) 샘플, 피치 경계 위 좌표, 속도 0 선수(투영 분모), λ·σ 0 입력 → AC-06 + 파라미터 하한 클램프.
- **수치**: exp 오버플로(로지스틱 인자 ±20 클램프, 기존 StandInSpace와 같은 방식), ΣPC 정규화.
- **GK**: 뜬공 높이 2.5~3.0 m 구간에서 GK만 컨트롤 — 슬롯 0·11 판정은 `ActionWorld.IsKeeper`와 같은 규칙.
- **크로스·스루볼 경로**: 크로스는 재평가 대상에서 빼고(FR-23), 스루볼이 상위 K면 재평가 — `PassRefineTests.Cross_NotRefined`.
- **다른 스펙 회귀**: 패스 선택이 바뀌어 083·085 E2E 시드 장면이 사라질 수 있다(087에서 실제로 재선택함) → 그 장면 테스트가 실패하면 087처럼 시드 재선택 태스크로 처리하고 장면 내용은 바꾸지 않는다.
- **성능 폭주**: 패스 후보가 많은 틱(최대 24) — 재평가 수 상한 K + extra = 5로 고정.

## 위험 · 미정
- 판단 P가 내려가면 패스 대신 드리블·보유가 늘어 D20(탈취율)·테이크온 지표가 움직일 수 있다 → FR-25 회귀 감시, [대체] 계수(K, mateValue, 008 b0 등)로만 되돌림. [설계] 개정이 필요하면 멈추고 제안.
- 성공률이 판단 개선만으로 60 %에 못 미칠 수 있다(실행 오차·첫 터치 루즈볼 11 %는 판단으로 안 줄어듦) → FR-26 보류 경로. 측정은 1차(연결만) → 2차([대체] 보정) 2회까지.
- λ_att·λ_gk 능력치 연결 범위는 기획서에 없음 → 연결 계수 0(원 값)으로 시작, 수치는 `[미정]`으로 남김.
- 원 초안의 그리드 캐시는 시뮬 루프에서 그리드를 안 쓰므로 만들지 않는다(필요 시 040·019에서).

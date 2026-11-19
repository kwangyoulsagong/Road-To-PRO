# 074 — 골키퍼: 페널티킥 · 배급 · 손 보유 시간 · 플랜

상태: 승인됨 (2026-11-19 사용자 "ㄱㄱ" — Q1~Q4 추천안)
스펙: ./spec.md

## 접근
지금 엔진에서 세 기능은 이렇게 되어 있다.
- **경기 중 PK**: 키커 쪽은 071 FR-17(`ShotSystem.PenaltyCell`, 자연 측 p = 0.6)로 실제 공을 차고, GK는 일반 슛과 똑같이 011 `ShotWatcher`가 슛 서브스텝 + t_react에 반응한다. GK가 미리 움직이는 일이 없어서 "먼저 날았는데 반대로 찼다" 장면이 나올 수 없다.
- **승부차기**: `PenaltyShootout.Run`이 `StandInPenaltyJudge`(고정 확률 `standin.penaltyScoreProb` 0.76)로 결과만 굴린다. GK·키커 능력치가 아무 영향이 없다.
- **배급**: GK가 공을 가지면 대체 경로(`StandInOnBall.Decide` → `HoldTicks` 0.6 s 뒤 대체 `Pass`)를 탄다. 던지기·굴리기·펀트가 없고, 골킥도 같은 대체 `Pass` 또는 `ClearForward`다. 손 보유 시간은 012 `RefereeStage.KeeperHold`가 **틱(0.1 s) 단위**로 자체 측정한다.

이 플랜은 011 모듈(`MatchEngine/Runtime/Goalkeeper/`)에 두 모델을 더하고 기존 흐름에 끼운다.
1. **PK GK 전략 (FR-01~04)** — `PenaltyKeeperModel`이 GK의 "기울기" 값 l = μ + N(0, σ_dir·압박 배수)를 한 번 뽑는다. μ는 키커 자연 측 쪽 치우침 + 승부차기 이전 방향 기억, |l| < 중앙 폭이면 서서 반응(011 FR-10 그대로), 아니면 그 방향으로 **킥 t_e 전에 다이빙을 시작**한다(t_e ∈ 50~350 ms 분포에서 1회). 선방 판정은 011 `SaveModel.Evaluate`에 "미리 출발" 입력을 넘겨 계산한다: 같은 방향 공은 반응 지연 대신 t_e만큼 일찍 출발한 도달거리, 반대 방향 공은 GK가 멀어지며 남는 도달거리 max(0, r0 − v_dive·(τ + t_e))로 본다. 그래서 t_e가 클수록 같은 쪽은 유리, 반대쪽은 불리하다(FR-01 문장 그대로). 반응형(t_e ≤ 0)은 011과 비트 동일.
   - **경기 중 PK**: 키커의 예약 킥(008 `PendingKick.Due`)이 잡히는 결정 틱에 전략을 뽑아 팀별 `PenaltyPlan`에 두고, `Due − t_e` 서브스텝에 다이빙(011 `DiveMotion`·`DiveStart` 이벤트)을 시작한다. 실제 판정은 지금처럼 `ShotWatcher`가 하되 계획의 방향·t_e를 `SaveContext`로 넘긴다.
   - **승부차기**: `StandInPenaltyJudge` 대신 `SimPenaltyJudge`가 같은 함수를 한 틱 안에서 부른다 — 키커 셀·실행 오차(071 `ShotSystem`, 승부차기 σ 배수 = 071 `extraSigmaMul` 자리), GK 전략, 004 경로 예측 + `SaveModel.Evaluate/Resolve`. 공을 실제로 굴리지 않을 뿐 계수·판정은 경기 중 PK와 같다. 006 대체 파이프라인(`Duels.Enabled = false`)은 해시를 지키려고 기존 judge 그대로.
2. **배급 (FR-10~14)** — `DistributionModel`이 후보 = 같은 팀 필드 선수(≤ 10) × 방식(≤ 6)을 만들고, 방식별 사거리 밖은 뺀 뒤 EU = ΔxT × 성공 확률(008 패스 모델 + 007 피치 컨트롤)을 계산한다. 상황 경향(빠른 스로·숏 빌드업·롱 배급, FR-12)·Directness·역할(011 FR-71)·시나리오 의도(011 FR-94)는 **배수/보너스**로만 얹고, 선택은 010 공통 정규화 + softmax(온도 = Decisions)로 1회. 후보 0명이면 롱 배급 강제.
   - 어떤 방식을 쓸 수 있는지는 공 상태가 정한다: 손에 든 공(캐치 뒤) = 굴리기·던지기·펀트·드롭킥 + 공 내려놓고 짧은 패스, 골킥 = 짧은 골킥·롱 골킥, 발로 받은 공(백패스) = 짧은 패스·롱킥.
   - 실행은 기존 `BallAction`(Pass, 손 방식은 `BodyPart.Hand`) → 008 예약 킥 → 004 비행. 롱 배급·펀트의 낙하점 경합은 071 `AerialSystem`이 그대로 처리한다. 각도 오차는 σ_angle = 6° − 4.5°·n(011 `KeeperMapping.DistributionSigmaDeg`, 이미 있음).
3. **손 보유 시간 (FR-14)** — `GoalkeeperSystem`이 캐치 순간의 절대 서브스텝(100 Hz)을 팀별로 들고, 놓을 때 `Distribution` 이벤트에 보유 서브스텝 수와 초를 쓴다. 캐치 때 배급 마감 D = 6 s [제안] + |N(0, σ_hold·(1 − n_Decisions))|를 한 번 뽑고, 그 전이라도 좋은 후보가 있으면(효용 ≥ 기준) 놓는다. 마감이 되면 그 틱의 최선 후보로 강제 배급.

**007·071·012 대체 입력 대신 실물 사용**: 스펙은 의존 스펙이 구현 전이라 대체 입력으로 짓고 AC-07로 재확인하게 되어 있지만, 지금 007(2026-11-18)·071(2026-11-15)·012(2026-11-19)는 완료됐다. 073과 같은 이유로 처음부터 실물(007 피치 컨트롤·ΔxT, 071 키커 방향·공중볼, 012 재차기·8초 판정)을 쓰고, AC-07은 017(PK 성공률 목표) 부분만 보류로 남긴다(아래 Q2).
대안: 배급을 072 온볼 후보에 섞기 → 072는 GK 소유를 타지 않고(073에서 확인), 방식·사거리·손 규칙이 GK 전용이라 011 모듈에 두는 게 맞다.

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `GK/` = `ME/Goalkeeper/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/`, `P/` = `Assets/_Project/Tests/Performance/MatchEngine/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `GK/PenaltyKeeperModel.cs` (새) | FR-01·04: 기울기 l 표본(μ = 자연 측 치우침 + 기억, σ_dir × 압박 배수), 중앙 폭, t_e 표본(분포 데이터), 라인 이탈 표본(FR-03), 승부차기 기억·피로 누적 값 타입 `ShootoutKeeperMemory`(방향별 실점 수·다이빙 수) |
| 〃 | `GK/SaveModel.cs` | `SaveContext`에 `CommitDir`(−1·0·+1)·`CommitLead`(s) 추가. 같은 방향 샘플 = `ReachAfterReact(τ + t_e)`(반응 지연 없음), 반대 방향 = max(0, r0 − v_dive·(τ + t_e)), 기본값(0)이면 011과 비트 동일 |
| 〃 | `GK/DistributionModel.cs` (새) | FR-10~13: 방식 enum `DistributionKind`(Roll 0 · Throw 1 · ShortKick 2 · LongKick 3 · Punt 4 · DropKick 5, 번호 고정), 공 상태별 허용 방식, 사거리 필터, EU = ΔxT × p, 경향 배수(빠른 스로·숏·롱)·Directness·역할·의도 보너스, 010 정규화 + softmax, 후보 0 → LongKick 강제. 롱킥·펀트 목표 = 받는 선수 앞 낙하점, 성공 확률 = 007 지점 질의 |
| 〃 | `GK/KeeperPlans.cs` | `PenaltyPlan`(방향·기울기·t_e·다이빙 시작 서브스텝·라인 이탈·결정 틱), `HoldClock`(캐치 절대 서브스텝·마감 서브스텝·손/발 소유). 상태 저장·해시 포함 |
| 〃 | `GK/GoalkeeperSystem.cs` | PK 예약 킥 감지 → `PenaltyPlan` 생성, `Due − t_e` 서브스텝 다이빙 시작, `ShotWatcher`에 계획 전달. 캐치 시 `HoldClock` 시작, `DecideDistribution(ctx, team)` (마감·효용 기준), 놓을 때 `Distribution` 이벤트. `HoldSubsteps(team, abs)` 조회(012가 읽음). GK 교체·데드볼·소유 변경 시 계획 초기화 |
| 〃 | `GK/ShotWatcher.cs` | 추적 슛이 PK이고 계획이 있으면 반응 예약 대신 계획의 방향·t_e로 `SaveContext` 채움(이미 시작한 다이빙을 다시 시작하지 않음) |
| 〃 | `GK/KeeperMapping.cs` | `KeeperProfile`에 `DistributionSigmaDeg`·`DecisionsN` 사용 필드(매핑 함수는 이미 있음, 새 공식 없음) |
| 〃 | `GK/GoalkeeperParams.cs`, `GK/GoalkeeperTuningIds.cs`, `Data/tuning/goalkeeper.json` | 새 계수 (아래 데이터 표) |
| 〃 | `ME/Match/Flow/PenaltyShootout.cs` | `SimPenaltyJudge : IPenaltyJudge` 추가(위 2). 판정마다 `ShootoutKeeperMemory` 누적(팀별, 승부차기 한 번 안에서만 — 기존처럼 한 틱 안에서 끝나므로 스냅샷 불필요, 해시는 이벤트로 검증). `ShootoutKick` 이벤트에 GK 방향·t_e·기울기 추가(F0·F1·OtherId 기존 칸) |
| 〃 | `ME/Match/MatchSim.cs` | 기본 judge: 010 파이프라인이면 `SimPenaltyJudge`, 006 대체면 기존 `StandInPenaltyJudge` |
| 〃 | `ME/Match/Stages/UtilityDecisionStage.cs` | 인플레이 GK 소유(손·발) → `ctx.Keepers.DecideDistribution` (073 `MustClear` 다음, 대체 `Decide` 앞). 대체 경로는 006 파이프라인 전용으로 남김 |
| 〃 | `ME/Match/Stages/StandIn/StandInOnBall.cs` | 데드볼 재시작 키커가 GK이고 골킥이면 `DecideDistribution`(ShortKick/LongKick만). 손에 든 GK 이동 = 박스 선 1 m 안쪽 지점까지 `MoveTo`(지금 `Carry` 골문 방향은 GK에게 안 씀) |
| 〃 | `ME/Match/Stages/RefereeStage.cs` | 012 연결(Q2): `KeeperHold`가 자체 틱 시계 대신 `ctx.Keepers.HoldSubsteps`로 판정(위반 = 보유 > 800 서브스텝, 정수 비교). PK 결과(득점 아님) + `PenaltyPlan.OffLine` → 기존 `RestartRules.PenaltyRetake`로 `PenaltyRetake`(420) + 같은 지점 PK 재시작 |
| 〃 | `ME/Match/Events/SimEventKind.cs` | `Distribution`(308, 예약됨) 데이터 규약: A = 방식, OtherId = 받는 선수, B = 보유 서브스텝(손이 아니면 −1), F0 = 보유 초, F1 = 선택 EU. 새 `PenaltyKeeper = 310`: A = 방향(−1·0·+1), B = 라인 이탈(0/1), F0 = t_e(s), F1 = 기울기 l, Substep = 다이빙 시작 |
| 〃 | `ME/Match/Replay/StateCheckpoints.cs` | `FormatVersion` 6 → 7 (GK 계획 2종 추가). 저장은 경기 전·후만이라 마이그레이션 없음 |
| RoadToPro.Presentation | `Presentation/Players/Data/043-anim-map.json` | `Distribution` 방식 → 클립: Punt·DropKick → `gk_punt_r`, LongKick·ShortKick(골킥) → `gk_goalkick_r`, Throw → `throw_in` [대체, 080 전], Roll → `gk_kick` 대신 `gk_catch_chest` 해제 동작 [대체]. PK 조기 다이빙은 기존 `DiveStart` → `gk_dive_l/r` |
| RoadToPro.Verification | `Data/e2e/074-gk-pk-distribution.json` (새) + 장면 탐색 | AC-06: 실경기(058 경로) 2장면 — PK 조기 다이빙 후 반대편 실점, 캐치 직후 오버암 던지기 역습 |
| Tests | `T/Goalkeeper/PenaltyKeeperModelTests.cs`, `DistributionModelTests.cs`, `KeeperHoldTests.cs`, `PenaltyShootoutSimTests.cs`, `T/Goalkeeper/GoalkeeperDeterminismTests.cs`(AC-04 추가), `T/Referee/KeeperHoldRetakeLinkTests.cs`(AC-07), `P/Goalkeeper/PenaltyDistributionPerfTests.cs` | 아래 테스트 전략 |

의존 방향 확인: 전부 `RoadToPro.MatchEngine` 안(GK → Actions·PitchControl 읽기, Flow → GK 모델 호출, Referee 단계 → `ctx.Keepers` 조회는 이미 있는 방향). `.claude/rules/10-csharp.md` 위반 없음. UnityEngine 참조 없음.

## 데이터 · 스키마
`Data/tuning/goalkeeper.json`에 추가 (`gk.pk.*`, `gk.dist.*`, `gk.hold.*`). Distribution σ 매핑(`gk.map.distribution.*`)·Decisions 온도는 011이 이미 넣었다.

| id | 시작값 | 태그 | 근거 |
|---|---|---|---|
| `gk.pk.leadMin` / `leadMax` | 0.05 / 0.35 s | [설계] | FR-01 킥 50~350 ms 전 (리서치 04 § A1) |
| `gk.pk.leadShape` | 균등 | [미정→대체] | 조기 움직임 시각 분포 [미정] — 균등으로 시작 |
| `gk.pk.naturalBias` | 0.25 | [대체] | μ의 키커 자연 측 치우침 (071 자연 측 p 0.6에 맞춰 GK 자연 측 다이빙 ≈ 0.57) |
| `gk.pk.sigmaDir` | 1.0 | [대체] | 기울기 노이즈 σ (μ와 같은 척도) |
| `gk.pk.centerBand` | 0.15 | [대체] | |l| < 이 값이면 서서 반응 (중앙 잔류 ≈ 10 %) |
| `gk.pk.offLineP` | 0.08 | [미정→대체] | FR-03 라인 이탈 확률 — 재차기 DIS-12 0.005/경기 ÷ (PK 0.25/경기 × 비득점 0.24) 역산 (Q3) |
| `gk.pk.shootoutMul` | 1.5 | [미정→대체] | FR-04 압박 배수 최대값: 배수 = 1 + (shootoutMul − 1)·(1 − BigMatch_n)·(1 − Composure_n). 키커 071 σ와 GK σ_dir에 같이 |
| `gk.pk.memoryWeight` | 0.1 | [대체] | 승부차기 이전 방향 기억: 같은 경기 상대 키커가 찬 방향 (왼 − 오) 1회당 μ 이동 |
| `gk.pk.fatigueDive` | 0.01 | [대체] | 승부차기 다이빙 1회당 v_dive 감소 비 (FR-04 "피로 누적") |
| `gk.dist.range.roll` | 10~25 m | [설계] | FR-10 (리서치 04 § A6, 거리값 미검증) |
| `gk.dist.range.throw` | 25~45 m | [설계] | FR-10 |
| `gk.dist.range.short` | 10~30 m | [설계] | FR-10 |
| `gk.dist.range.long` | 50~70 m | [설계] | FR-10 (롱 골킥·펀트 공용) |
| `gk.dist.range.dropKick` | 35~55 m | [미정→대체] | FR-10 드롭킥 사이드 사거리 [미정] |
| `gk.dist.speed.roll` / `throw` | 9 / 16 m/s | [대체] | 방식별 출발 속도 (사거리 끝에 닿는 값) |
| `gk.dist.sigmaMul.roll` / `throw` / `short` | 0.5 / 0.8 / 008 패스 σ | [대체] | 리서치 04 § A6 "σ 낮음 / 중간 / 패스 모델 공용" — 롱킥·펀트·드롭킥은 σ_angle 그대로(×1) |
| `gk.dist.sigmaDist` | 0.12 | [대체] | 롱 배급 거리 오차 비 × (1 − 0.75·n) (σ_dist ∝ 1/Distribution) |
| `gk.dist.quickThrowBonus` / `shortBonus` / `longBonus` | 1.5 / 1.3 / 1.4 | [대체] | FR-12 경향 배수 (G-DS-01·02·03) |
| `gk.dist.unsetLine` | 4명 | [설계] | AC-02 "상대 라인 뒤 인원 < 4" = 상대 미정렬 |
| `gk.dist.pressHigh` | 0.6 | [대체] | 압박 높음 = 짧은 대상 주변 상대 압박(008 `PressureOn`) 평균 ≥ 이 값 |
| `gk.dist.directnessK` | 1.0 | [대체] | Directness 0~1 → 롱 계열 배수 1 + k·(D − 0.5) |
| `gk.dist.roleSweeperShort` / `roleTraditionalLong` | 1.2 / 1.2 | [대체] | 011 FR-71 역할 차이 |
| `gk.hold.target` | 6.0 s | [제안] | FR-14 (스펙 [제안] 채택분) |
| `gk.hold.sigma` | 0.8 s | [대체] | 마감 지연 σ × (1 − n_Decisions) |
| `gk.hold.settle` | 1.0 s | [대체] | 캐치 뒤 최소 대기 (빠른 스로 조건이면 0) |
| `gk.hold.goUtility` | 0.6 | [대체] | 마감 전이라도 정규화 효용 최댓값이 이 값 이상이면 배급 |
| `gk.hold.boxInset` | 1.0 m | [대체] | 손에 든 GK가 다가가는 박스 선 안쪽 거리 |

`standin.penaltyScoreProb`는 006 대체 파이프라인 전용으로 남긴다(설명 문구만 갱신).

세이브 스키마: 경기 중 체크포인트(006 `StateCheckpoints`)에 `PenaltyPlan`·`HoldClock` 2종 추가 → `FormatVersion` 7. 커리어 세이브(경기 전·후)는 바뀌지 않는다.

## 결정론 · 성능
- RNG: GK 쪽 새 난수는 전부 `RngStreamId.Goalkeeper`. 호출 순서 고정 —
  - 경기 중 PK: 예약 킥이 잡힌 결정 틱에 팀(수비) 하나만 l 1회 → t_e 1회 → 라인 이탈 1회. 키커 쪽 071 RNG(Actions 스트림)와 스트림이 달라 서로 순서에 영향이 없다.
  - 승부차기: 킥마다 키커(071, Actions 스트림) → GK l → t_e → 라인 이탈 → `SaveModel.Resolve`. 기억·피로는 킥 결과 뒤에 갱신. 기존 `FlowRng` 호출(선축 동전·교체)은 순서 그대로.
  - 배급: 캐치 때 마감 지연 1회, 배급 결정 때 softmax 1회, 실행 때 각도·거리 오차(008 실행 경로, Actions 스트림).
- 순회: 후보 = 슬롯 오름차순 × 방식 번호 오름차순. Dictionary 없음. 007 질의는 기존 `ActionWorld` 뷰 재사용.
- 할당 0: 후보 버퍼 고정 배열(10 × 6 = 60), 승부차기 기억은 값 타입, 경로는 기존 `SaveModel` 버퍼.
- 시계: 보유 시간은 절대 서브스텝 정수(100 Hz)로 들고 초는 표시용으로만 계산 — 8.0 s = 800, 7.99 s = 799 서브스텝으로 정확(부동소수점 비교 없음).
- 성능: 배급 결정은 캐치·골킥·백패스 순간과 손 보유 중 결정 틱(최대 ~80틱)만. 후보 60개 × 008 평가 + 롱 계열 007 지점 질의(≤ 10회). 예산 ≤ 10 µs가 빠듯하면 롱 계열 질의를 상위 K개로 줄인다(007 FR-21 방식, 결과 해시 고정). PK 전략은 표본 3개 + 산술(≤ 1 µs). 측정: `P/Goalkeeper/PenaltyDistributionPerfTests`(GC 0) + 시뮬 틱 경로를 바꾸므로 IL2CPP 벤치 1회(`farm.py bench --spec 074`).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `PenaltyKeeperModelTests.UnsaveableArea_28pct`: 골문면 100 × 100 균등 격자(1만 점)로 PK(점 → 20.8 m/s 직선 조준, 리그 평균 GK 정중앙·정지·반응형)를 판정해 도달 불가(max margin < 0) 비율 28 % ± 3 %p (Q1) | 로직 | — |
| AC-02 | `DistributionModelTests.Catch_Unset_FreeFullback_ThrowTop`(상대 라인 뒤 3명 + 빈 풀백 → Throw가 EU 1위), `HighPress_ShortLowP_LongChosen`(1,000회 선택 중 롱 계열 최빈), `AllDistributions_WithinModeRange`(픽스처 10종 × 1,000회 실행 거리 ⊂ 방식 사거리), `NoCandidates_ForcedLong` | 로직 | — |
| AC-03 | `KeeperHoldTests.Hold_Recorded_InDistributionEvent`, `Hold_10000Catches_Over8s_LE_0_5pct`(무작위 배치 픽스처 1만 개, 결정 틱 루프만 — 물리 없음, Q4), `Boundary_800_799_Substeps_Exact` | 로직 | — |
| AC-04 | `GoalkeeperDeterminismTests.Shootout10Rounds_TwiceSameHash`(GK 방향·t_e·결과·기억 해시), `Distribution_Fixture1000_TwiceSameHash`, `StateRoundTrip_WithPenaltyAndHold` | 결정론 | — |
| AC-05 | `PenaltyDistributionPerfTests.Distribution_10x6`(≤ 10 µs), `PenaltyKeeper_Strategy`(≤ 1 µs), GC 0 B + `farm.py bench --spec 074` | 성능 | mac-m1-1600p |
| AC-06 | `e2e/074-gk-pk-distribution` (2장면: PK 조기 다이빙 후 반대편 실점, 캐치 직후 오버암 던지기 역습) — 1프로필 스크린샷 육안 승인(기준 이미지 비교·smoke 매트릭스는 2026-11-11 폐지) | E2E | mac-m1-1600p |
| AC-07 | `KeeperHoldRetakeLinkTests.Hold801_Violation_800_Not`(012 판정이 074 시계를 읽음), `OffLine_SavedPk_Retake_ScoredPk_NoRetake`(012 `PenaltyRetake` 연결), AC-01~03이 007·071·012 실물 경로로 돈다는 단언. PK 성공률 조건(017 FR-20)만 보류 | 로직 | — |
| AC-08 | `DistributionModelTests.LongAngleSigma_Dist1_6deg_Dist20_1_5deg`(각 10만 표본 ±3 %), `LongSuccess_Dist20_Higher`(같은 픽스처 2,000회씩, 이항 p < 0.01) | 로직 | — |
| AC-09 | `PenaltyKeeperModelTests.ShootoutLowBigMatchComposure_DirNoiseGreater`(기울기 l 표본 각 5,000개, F 검정 p < 0.01) | 로직 | — |

기존 회귀: `farm.py logic --spec 074 --scope auto` → MatchEngine 변경이라 `match` 범위. 경기 흐름(배급·PK)이 바뀌므로 E2E 시드 장면 테스트(082·083·085·086·073)가 깨질 수 있다(아래 위험).

## 버그 방지
- **PK 예약 킥이 늦게 잡힘**: `Due − t_e`가 이미 지났으면 지금 서브스텝에 시작하고 실제 t_e를 기록(음수 없음). `Lead_LargerThanPrep_StartsNow_RecordsActual`.
- **PK가 취소·재차기**: 예약 킥 만료·데드볼·재차기면 `PenaltyPlan` 초기화, 다이빙 중이면 011 일어나기 흐름. `Retake_ClearsPlan_NewDraw`.
- **반응형(t_e ≤ 0)·중앙 잔류**: `SaveContext` 기본값 → 011 결과 비트 동일. 기존 `SaveModelTests` 통과 + `Commit0_BitIdentical`.
- **반대 방향 도달거리 음수**: max(0, …). `Opposite_LateLead_ReachNotNegative`.
- **승부차기 GK 교체**(기존 `ReplaceKeeper`): 기억은 팀 기준이라 유지, 피로는 새 GK 0부터. `ShootoutKeeperReplaced_FatigueResets_MemoryKept`.
- **필드 선수가 GK 슬롯**(011 경계): 같은 규칙, 능력치 1~3 [제안]은 011 `EnsureKeeper` 매핑 그대로. `OutfieldKeeper_SameRules`.
- **배급 후보 0명 / 사거리 밖**: 롱 강제, 방식별 필터. AC-02 테스트에 포함.
- **손 방식이 손 아닌 공에**: 골킥·백패스에 Roll·Throw·Punt·DropKick이 후보로 안 나온다. `GoalKick_FootModesOnly`, `BackPass_NoHandModes`.
- **손으로 던진 공에 핸드볼·백패스 판정**: 012 `HandballJudge`가 GK 자기 박스 안 `BodyPart.Hand` 배급을 위반으로 보지 않는지. `ThrowRelease_NoHandball`.
- **손에 든 채 박스 밖으로**: 이동 목표를 박스 선 − `boxInset`으로 자름. `HoldingKeeper_StaysInBox`.
- **보유 중 상대가 막음 / 소유 잃음**: 시계는 계속(판정은 012), 공을 놓치면(ClaimFail·스필) 시계 끝 + 이벤트 B = −1 아닌 실제 값. `Hold_Blocked_ClockRuns`, `Hold_LostBall_EndsClock`.
- **8초 위반 뒤 코너**: 위반 판정 틱에 시계 초기화(이중 판정 방지). 기존 012 테스트 + `Hold_Violation_ClearsClock`.
- **상태 저장 왕복**: 계획 2종 포함 Write→Read→해시 동일. AC-04 테스트.

## 위험 · 미정
- **Q1 AC-01 "PK 방어 불가 면적" 측정 방식** — 스펙은 "페널티 1만 회"이지만 "면적"은 리서치의 diving envelope(반응만으로 닿는 골문 비율)다. **추천**: 골문면 1만 점 균등 격자에 PK 속도 20.8 m/s로 조준, 리그 평균 GK가 정중앙에서 반응형으로 닿지 못하는(max margin < 0) 비율로 잰다(RNG 없음, 재현 확실). 지금 011 계수로 28 ± 3 %p를 벗어나면 FR-02대로 `gk.map.dive.*`·`gk.reach.rmaxBase`를 011 범위 안에서 고치되, 일반 선방률(087 측정값)도 같이 움직이므로 100경기 선방률을 다시 재서 087 범위를 벗어나면 멈추고 묻는다. (대안: 실제 키커 분포로 1만 번 차서 "GK가 맞게 날아도 못 막은 비율"로 잼)
- **Q2 012 연결을 이번에 함** — 012가 완료라 AC-07의 "연결 때 재확인"을 지금 할 수 있다. **추천**: ① 012 8초 판정이 자기 틱 시계 대신 074 서브스텝 시계를 읽게 바꾸고(같은 시각을 두 곳에서 재지 않게), ② 이미 있는 `RestartRules.PenaltyRetake`를 PK 흐름에 연결한다(라인 이탈 + 비득점 → 재차기). AC-07은 017 PK 성공률만 보류로 남긴다. (대안: 기록만 하고 012 연결은 012 후속으로 미룸 — 그러면 AC-07 전부 보류)
- **Q3 [미정] 시작값** — 라인 이탈 0.08(DIS-12 0.005/경기 역산), 승부차기 배수 최대 1.5, 드롭킥 35~55 m, 조기 시각 균등 분포, 기억 0.1·피로 0.01. **추천**: 위 표의 [대체] 값으로 시작, 017에서 보정.
- **Q4 AC-03 측정 방식** — 실경기로 캐치 1만 회는 약 1,000경기(수 시간)라 테스트 예산과 안 맞는다. **추천**: 무작위 배치 픽스처 1만 개에서 배급 결정 루프만 돌려(물리 없음) 판정하고, 마지막 100경기 측정(`MatchFeelMetrics`)에서 실경기 보유 시간 분포·8초 위반 수를 리포트에 기록만 한다.
- **BigMatch** — 엔진에는 BigMatch 능력치가 없고 버릇(`Habit.BigMatch`, 레벨 0~2)만 있다. BigMatch_n = 버릇 레벨 / 2로 쓴다(003 능력치 정의 전 대체, 003 연결 때 재확인).
- **경기 흐름 변화로 E2E 시드 깨짐** — GK 배급이 대체 `Pass`에서 바뀌면 거의 모든 경기의 진행이 달라진다. 073·082·083·085·086 장면 테스트가 깨지면 087처럼 시드 재선택 태스크(`TNNa`)로 처리.
- **평균 득점·패스 지표 영향** — 롱 배급·던지기가 생기면 점유·패스 수·득점이 움직일 수 있다(D19 패스 지표 보류 중). 017 목표는 이 스펙 AC가 아니지만, 마지막 100경기 측정값(득점·선방률·PK 성공률·GK 배급 방식 비율·평균 보유 시간)을 리포트에 기록한다.
- **3D 클립** — GK 오버암 던지기·굴리기 전용 클립은 080 범위. 이번엔 `throw_in` 등 기존 클립 대체 매핑이라 AC-06은 "장면이 읽히는가"만 본다.
- **(2026-11-19 결정) 배급 선택 예산 10 → 20 µs**: 최적화 2단계 뒤 IL2CPP 12.42 µs — 사용자 "베스트 케이스"로 개정. 원래 메모: 성능 예산 10 µs는 후보 60개 × 008 평가에서 빠듯할 수 있다 — 넘으면 롱 계열 007 질의를 상위 K개로 줄이는 태스크를 추가한다(결과는 해시로 고정).

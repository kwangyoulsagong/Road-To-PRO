# 073 — 골키퍼: 스위퍼 돌진 · 크로스 처리 · 1v1 · 플랜

상태: 완료 (2026-11-19) · AC-04 분류 기준 보류(D28) — 승인됨 (2026-11-19 사용자 "ㄱㄱ" — Q1~Q3 추천안)
스펙: ./spec.md

## 접근
지금 GK가 골문을 떠나는 행동은 하나뿐이다. `GoalkeeperSystem.Decide`가 박스 안 느슨한 공에 `Contest` 후보(공 + 속도 × 0.3 s 지점, 직선 도달 시간 비교 로지스틱)를 내고, 공중볼은 071 `AerialSystem`이 GK가 이기면 무조건 011 잡기 경로로 넘긴다(`HeaderOutcome.Keeper`, 주석 "클레임·펀칭은 073"). 그래서 ① 라인 뒤 스루패스에 GK가 박스 밖으로 나오지 않고 ② 크로스는 혼잡도·CommandArea와 상관없이 잡히며 ③ 1v1은 일반 위치 선정 + 011 FR-09 정지 패널티로만 처리된다.

이 플랜은 011 모듈(`MatchEngine/Runtime/Goalkeeper/`)에 상황 판정기 세 개를 더하고, 기존 결정 흐름(단계 5 `StandInOnBall.DecideKeeper` → `GoalkeeperSystem.Decide` → `KeeperChooser`)에 **후보 종류만 추가**한다. 선택 수식은 010 공용 선택기 그대로라 FR-31(의도는 보너스만, 음수 EU 제외)이 자동으로 지켜진다.
1. **스위퍼 (FR-01~04)** — 공이 킥으로 날아가는 중이고 자기 수비 라인 뒤로 향하면 `SweeperModel`이 004 예측 경로를 0.1 s 간격으로 훑어 첫 접촉점 x*(상대 공격수 TTI ≤ 공 도착 시간인 첫 샘플)를 찾고, Δ = TTI_att − TTI_gk(005 `TimeToIntercept`, GK 반응은 011 FR-70)가 마진 m + κ·[박스 밖]보다 크면 **그때만** 007 지점 질의로 p_win을 구해 EU를 계산한다(비싼 질의는 게이트 통과 때 1회).
2. **크로스 (FR-10~13)** — 공중 공의 착지점이 자기 박스 안이고 높이가 손 도달 범위 안이면 `CrossClaimModel`이 잡기 / 펀칭 / 포기 세 후보의 EU를 내고, 고른 모드와 결단 지연(FR-12)을 팀별 `CrossPlan`에 둔다. 071 `AerialSystem`은 경합자 등록 때 GK의 모드를 읽어 **포기면 GK를 빼고**, GK가 이기면 모드대로 잡기(성공 p_claim, 실패면 ClaimFail) 또는 펀칭(Punch)으로 끝낸다.
3. **1v1 (FR-20~22)** — 상대 볼 캐리어가 자기 박스 근처이고 캐리어와 골문 사이에 같은 팀 필드 선수가 없으면 `OneOnOneModel`이 공(터치 뒤 공 위치)에 대한 TTI 비교로 Smother / Set 후보를 낸다. Set은 이등분선 위 전진 목표 + 정지 시각 오차(σ_t, FR-22)를 표본으로 뽑아 저장하고, 근거리(스프레드 거리 안)에서는 011 `SaveModel`에 스프레드 보정(r0↑, v_dive↓, FR-21)을 넘긴다. 정지 실패 reach 패널티는 011 FR-09 `gk.still.movingPenalty`를 그대로 쓴다(스펙 FR-20 "011 FR-09로 일반 슛에도 적용").

**007·071 대체 입력 대신 실물 사용**: 스펙은 007·071이 구현 전이라 대체 TTI 근사로 먼저 짓고 AC-08로 재확인하게 되어 있지만, 지금은 둘 다 구현 완료다(007 2026-11-18, 071 2026-11-15). 그래서 처음부터 007 `PitchControlQuery.Point`·071 `AerialSystem`/`AerialModel`을 쓰고, AC-08은 "대체 경로 없음 — AC-01~03이 실물 경로로 통과"로 이번 스펙 안에서 닫는다. 대체 근사를 만들었다 지우는 일을 피하려는 선택이다.
대안: 돌진·클레임을 슛 감시처럼 물리 서브스텝에서 판정 → 010 FR-90("GK 결정은 같은 판단 틱 안에서 010 선택 규칙으로")과 어긋나 버렸다. 실행(손/발 접촉)만 물리 단계에 둔다.

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `GK/` = `ME/Goalkeeper/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/`, `P/` = `Assets/_Project/Tests/Performance/MatchEngine/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `GK/SweeperModel.cs` (새) | FR-01~03: 침투 판정(라인 뒤로 향하는 킥), 경로 샘플 → 첫 접촉점 x*, Δ, 마진 m(Sweeping) + κ, 게이트 통과 시 007 지점 질의 p_win, EU = p_win·V_clear − (1 − p_win)·ΔxG_fail. 피치·골라인 밖 x* → 후보 없음, TTI 무한 → 후보 없음 |
| 〃 | `GK/CrossClaimModel.cs` (새) | FR-10~12: 착지점 L·높이(004 예측), 점프 도달 높이(기준 + Jumping 보정, 011 `StandReachZ`·`JumpBonus`), TTI 비교, 혼잡도(반경 2 m 인원), p_claim = σ(k1·Δt + k2·CA − k3·혼잡도), 잡기·펀칭·포기 EU, 결단 지연 0.2·(1 − n) + 노이즈. 착지점이 골문 안이면 후보 없음(011 선방으로) |
| 〃 | `GK/OneOnOneModel.cs` (새) | FR-20~22: 1v1 감지, 터치 뒤 공–캐리어 거리·TTI 비교 → Smother/Set, 이등분선 전진점, 정지 시각 오차 표본 N(0, σ_t), 스프레드 거리·보정값 |
| 〃 | `GK/KeeperPlans.cs` (새) | 팀별 진행 중 계획 값 타입: `CrossPlan`(모드·결정 틱·지연 끝 틱·착지점), `RushPlan`(x*·시작 틱·이벤트 냈는지), `OneOnOnePlan`(모드·정지 예정 틱·스프레드). 상태 저장·해시 포함 |
| 〃 | `GK/KeeperChooser.cs` | `KeeperActionKind`에 `Rush = 3`, `Claim = 4`, `Punch = 5`, `Smother = 6`, `Set = 7` 추가(번호 고정, 세이브 들어감). `Abandon`은 후보로 두지 않고 "위치 선정 선택 + 크로스 진행 중"으로 표현. `MaxCandidates` 8 그대로(최대 후보 수 = 위치·경합·의도·돌진·잡기·펀칭·스마더·세트 중 동시 최대 5) |
| 〃 | `GK/GoalkeeperSystem.cs` | `Decide`에 세 모델 후보 추가(호출 순서: 위치 → 경합 → 돌진 → 크로스(잡기·펀칭) → 1v1(스마더·세트) → 의도 이동 → 직전 선택 보너스), 계획 상태 보관·저장·해시, `CrossMode(team)`·`IsRushing(team)`·`SpreadFor(team)` 조회, KeeperRush·Smother·ClaimFail 이벤트 발행 |
| 〃 | `GK/KeeperMapping.cs` | `KeeperProfile`에 `SweepMargin`·`CommandDelay`·`CommandN`·`JumpBonus`·`OneOnOneSigma`·`OneOnOneN` 필드 추가(이미 있는 매핑 함수 사용, 새 공식 없음) |
| 〃 | `GK/SaveModel.cs` | FR-21: 스프레드 중이면 r0 + Δr0, v_dive × (1 − k_spread)로 `Evaluate` 입력 보정(호출자가 넘김, 기본 0이면 011과 비트 동일) |
| 〃 | `GK/GoalkeeperParams.cs`, `GK/GoalkeeperTuningIds.cs`, `Data/tuning/goalkeeper.json` | 새 계수 (아래 데이터 표) |
| 〃 | `ME/Actions/AerialSystem.cs` | 경합자 등록: GK 슬롯이면 `ctx.Keepers.CrossMode(team)` 확인 — 포기·결단 지연 중이면 등록 안 함. GK 승리 시: 잡기 모드 → p_claim 판정(Goalkeeper RNG) 성공은 기존 `HeaderOutcome.Keeper` 경로, 실패는 `ClaimFail` + 공 그대로 진행(손에 맞고 떨어짐 = 속도 감쇄), 펀칭 모드 → 011 `SaveModel` 펀칭 리바운드(`gk.save.punchDist`)로 차고 `Punch`(302) |
| 〃 | `ME/Match/Stages/StandIn/StandInOnBall.cs` | `DecideKeeper` 매핑: Rush·Smother → `Chase`(목표 x*/공), Claim·Punch → `MoveTo`(착지점), Set → `MoveTo`(전진점, 정지 예정 틱 뒤엔 `Stand`) |
| 〃 | `ME/Match/Stages/PhysicsStage.cs` | FR-04: 박스 밖 GK 접촉은 발 잡기만(지금도 `held`는 박스 안 기준) — 돌진 중 박스 밖에서 공을 얻으면 같은 틱 단계 5에서 072 Clear 후보를 강제(`RushPlan` 플래그, 손 접촉 이벤트 없음). 박스 밖에서 GK 손 처리 경로가 생기지 않는지 단언만 추가 |
| 〃 | `ME/UtilityAI/OnBallCandidates.cs` | 돌진 클리어: GK가 `RushPlan`으로 박스 밖 소유면 Clear만 후보(072 기존 `AddClear` 재사용, 새 공식 없음) |
| RoadToPro.Presentation | `Presentation/Players/Data/043-anim-map.json` | `Smother` → `gk_dive_l/r`(낮은 다이빙, 080 전 대체), `ClaimFail` → `gk_catch_chest`, 클레임 성공은 기존 `Catch`(높이 구간 높음). 손–공 거리는 080 보류(011 AC-24·25와 같은 처리) |
| RoadToPro.Verification | `Data/e2e/073-gk-sweeper-cross.json` (새) + 장면 탐색 | AC-07: 실경기(058 경로)에서 KeeperRush+박스 밖 Clearance, 크로스 클레임(Catch, 크로스 직후), Smother 3장면 고정 시드 |
| Tests | `T/Goalkeeper/SweeperModelTests.cs`, `CrossClaimModelTests.cs`, `OneOnOneModelTests.cs`, `KeeperLeaveGoalTests.cs`(경기 픽스처·AC-04·09), `T/Goalkeeper/GoalkeeperDeterminismTests.cs`(AC-05 추가), `P/Goalkeeper/KeeperLeavePerfTests.cs` | 아래 테스트 전략 |

의존 방향 확인: 모두 `RoadToPro.MatchEngine` 안(GK → PitchControl·Actions 읽기, Actions → GK 조회는 이미 `ctx.Keepers`로 있음). `.claude/rules/10-csharp.md` 위반 없음. UnityEngine 참조 없음.

## 데이터 · 스키마
`Data/tuning/goalkeeper.json`에 추가 (`gk.sweep.*`, `gk.cross.*`, `gk.oneOnOne.*`). 매핑 계수(m·지연·σ_t·점프)는 011이 이미 넣었다(`gk.map.*`).

| id | 시작값 | 태그 | 근거 |
|---|---|---|---|
| `gk.sweep.kappa` | 0.15 s | [대체] | FR-01 κ [미정] — 박스 밖 추가 마진 |
| `gk.sweep.sampleDt` | 0.1 s | [제안] | 경로 샘플 간격 (010 결정 주기와 같음) |
| `gk.sweep.horizon` | 3.0 s | [제안] | 경로 훑는 최대 시간 |
| `gk.sweep.vClear` | 1.0 | [대체] | FR-02 V_clear (011 `contestWin`과 같은 척도) |
| `gk.sweep.failScale` | 1.0 | [대체] | FR-02 ΔxG_fail 배율 |
| `gk.sweep.lambdaGk` | 12.9 1/s | [설계] | FR-13 λ_gk (리서치 02 § 2.3) — GK가 손을 쓸 수 있는 박스 안 질의에서 GK의 컨트롤 비율. 박스 밖 x*는 필드 선수와 같은 λ |
| `gk.cross.k1` | 4.0 1/s | [대체] | FR-11 k1 [미정] |
| `gk.cross.k2` | 2.0 | [대체] | FR-11 k2 [미정] (CommandArea n) |
| `gk.cross.k3` | 0.6 | [대체] | FR-11 k3 [미정] (혼잡도 1명당) |
| `gk.cross.crowdRadius` | 2.0 m | [설계] | FR-11 반경 2 m |
| `gk.cross.jumpReach` | 2.9 m | [미검증] | FR-10 점프 도달 2.9~3.2 m 하한 (기준 키) |
| `gk.cross.claimWin` / `punchWin` / `claimFail` / `punchFail` | 1.0 / 0.6 / 1.5 / 0.4 | [대체] | FR-11 "실패한 claim은 비용 가중치 크게" |
| `gk.cross.delayNoise` | 0.03 s | [대체] | FR-12 판단 노이즈 σ (CommandArea 낮을수록 ×(1 − n)) |
| `gk.cross.failSpeed` | 0.4 | [대체] | ClaimFail 공 속도 감쇄 비 |
| `gk.oneOnOne.range` | 20 m | [대체] | 1v1 감지: 캐리어–골 거리 |
| `gk.oneOnOne.smotherMargin` | 0.1 s | [대체] | FR-20 "GK 쪽 TTI 비교에서 유리" 기준 |
| `gk.oneOnOne.spreadDist` | 6 m | [대체] | FR-21 스프레드 거리 |
| `gk.oneOnOne.spreadR0` | 0.3 m | [미정→대체] | FR-21 r0 증가량 |
| `gk.oneOnOne.spreadDive` | 0.3 | [미정→대체] | FR-21 v_dive 감소 비 |
| `gk.oneOnOne.advance` | 0.5 | [대체] | Set 전진 비율(골–캐리어 이등분선 위, 011 깊이 상한 안) |

세이브 스키마: `GoalkeeperSystem.Write/Read`에 계획 3종 추가 → 경기 중 상태 체크포인트(006 `StateCheckpoints`) 형식이 바뀐다. 저장은 경기 전·후만이라 마이그레이션 없음, 체크포인트 버전 상수만 올린다.

## 결정론 · 성능
- RNG: 새 난수는 모두 `RngStreamId.Goalkeeper` 하나. 호출 순서 고정 — 결정 틱마다 팀 0 → 1, 팀 안에서 후보 계산(RNG 없음) → `KeeperChooser` softmax 1회 → (선택이 Claim/Punch일 때) 결단 지연 노이즈 1회 → (Set 선택 첫 틱) 정지 오차 1회. 물리 단계의 p_claim 판정 1회는 071 경합 승자 표본 뒤.
- 순회: 슬롯 0..21 고정 순서. Dictionary 없음. 007 질의는 기존 `ActionWorld.ControlPlayersView` 재사용.
- 할당 0: 경로 샘플은 고정 배열(30개), 후보는 기존 `_cands`.
- 성능: 침투·크로스·1v1 감지 게이트는 O(22) 거리 비교. 007 지점 질의는 스위퍼 게이트 통과 때만 1회(평균 2.5 µs) — 실경기에서 드물다. 측정: `P/Goalkeeper/KeeperLeavePerfTests`(판단 1회 × 픽스처 3종, GC 0) + 시뮬 틱 경로를 바꾸므로 IL2CPP 벤치 1회(`farm.py bench --spec 073`)로 011 "결정 틱당 GK 2명 전체" 예산 확인.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `SweeperModelTests.Margin_Plus02_Rushes_Minus02_Waits`, `Sweeping20_RushesMoreThan_Sweeping1`(같은 픽스처 1,000회 선택 빈도), `KeeperLeaveGoalTests.RushOutsideBox_NoHandContact`(픽스처 경기: 돌진 클리어 뒤 GK Catch/손 이벤트·012 핸드볼 0) | 로직 | — |
| AC-02 | `CrossClaimModelTests.Crowd0_Command20_ClaimHighest`, `Crowd5_Command1_PunchOrStay_Delay02`(지연 평균 0.2 s ± 노이즈 3σ) | 로직 | — |
| AC-03 | `OneOnOneModelTests.BigTouch_Smother_SmallTouch_Set`, `SetNotStopped_ReachPenalty`(011 `SaveModel` 마진 차 = 패널티), `StopTimingSigma_OneOnOne1_vs_20`(10만 표본 σ 0.15/0.05 ±3 %) | 로직 | — |
| AC-04 | `KeeperLeaveGoalTests.SweeperRole_MeetsClassification_Traditional_DoesNot` (역할만 바꾼 같은 시드 경기 묶음, 박스 밖 수비 행동/90·평균 수비 행동 거리) — 경기 수는 아래 "미정" Q1 | 로직 | — |
| AC-05 | `GoalkeeperDeterminismTests.LeaveGoal_Fixtures100x3_TwiceSameHash`(결정·이벤트·결과 해시) + 기존 경기 결정론 테스트 | 결정론 | — |
| AC-06 | `KeeperLeavePerfTests.Sweeper/Cross/OneOnOne_Decision`, GC 0 B + `farm.py bench --spec 073` | 성능 | mac-m1-1600p |
| AC-07 | `e2e/073-gk-sweeper-cross` (3장면: 돌진 클리어·크로스 클레임·스마더, 박스 밖 손 동작 없음) — 1프로필 스크린샷 육안 승인(기준 이미지 비교는 2026-11-11 폐지) | E2E | mac-m1-1600p |
| AC-08 | 위 AC-01~03 테스트가 대체 경로 없이 007 `PitchControlQuery`·071 `AerialSystem` 실물로 돈다는 단언(`SweeperModelTests.UsesPitchControlPoint`, `KeeperLeaveGoalTests.Cross_GoesThroughAerialSystem`) | 로직 | — |
| AC-09 | `KeeperLeaveGoalTests.NegativeEuRush_WithIntent_NeverChosen_1000`(G-SW 의도 + EU < 0 픽스처 1,000회, θ_opp 규칙 밖 선택 0) | 로직 | — |

기존 회귀: `farm.py logic --spec 073 --scope auto` → MatchEngine 변경이라 `match` 범위(Core + MatchEngine + 하이라이트 실경기 픽스처). 경기 흐름이 바뀌므로 082·083·085·086 E2E 시드 장면 테스트가 깨질 수 있다(아래 위험).

## 버그 방지
- **TTI 무한·NaN**: 도달 불가(속도 0, 퇴장 슬롯) → Δ 계산 전 `float.MaxValue` 검사, 후보 없음. `Margin_BothInfinite_NoCandidate_NoNaN`.
- **x* 피치·골라인 밖**: 후보 없음. `ThroughBall_FirstContactOutside_NoRush`.
- **착지점이 골문 안**: 크로스 후보 없음, 011 선방 경로. `Cross_LandingInGoal_SaveModelHandles`.
- **혼잡도 0**: k3 항 0. `Crowd0_NoPenaltyTerm`.
- **같은 틱 클레임 vs 상대 헤더**: 071 경합 승자 1명만 → 결과 1개. `Cross_SameTickHeader_SingleOutcome`.
- **박스 경계에서 잡기**: 손 사용 여부는 011·086 기준(착지점 박스 안)과 같은 함수 `PhysicsStage.InOwnBox`. `Claim_OnBoxLine_UsesOwnBoxRule`.
- **1v1 중 패스 전환**: Smother는 취소 불가 구간(공까지 0.3 s [대체] 안)이면 진행, 아니면 다음 결정 틱 위치 선정으로. `OneOnOne_PassSwitch_ReturnsToPosition`.
- **돌진 중 소유 변경·데드볼**: 계획 초기화(계획 잔존으로 엉뚱한 Chase 방지). `Rush_PossessionChange_ClearsPlan`.
- **GK 교체·필드 선수 GK**(011 `EnsureKeeper`): 계획 초기화. 기존 `KeeperReplacementTests`에 한 줄.
- **상태 저장 왕복**: 계획 포함 Write→Read→해시 동일. `GoalkeeperDeterminismTests.StateRoundTrip_WithPlans`.
- **011 비트 동일성**: 스프레드 0·계획 없음일 때 `SaveModel` 결과 011과 동일 — 기존 `SaveModelTests` 통과로 확인.

## 위험 · 미정
- **Q1 AC-04 경기 수 — 결정(2026-11-19): 역할별 100경기 판정, 1,000경기는 017 보류** — 스펙은 "1,000경기 평균". 에디터 EditMode에서 1,000경기 × 2역할은 수 시간이라 테스트 예산(메모리: Unity 실행은 적게·좁게)과 맞지 않고, 1,000경기 헤드리스 러너는 017 범위다. **추천**: 이번엔 역할별 100경기(기존 `MatchFeelMetrics` 100경기 측정과 같은 방식, 백그라운드)로 판정하고 1,000경기 확인은 017로 보류. (대안: 그대로 1,000경기 / AC-04 전체 보류)
- **Q2 성능 예산 — 결정(2026-11-19): 스펙 개정(게이트 ≤ 1 µs + 통과 시 지점 질의 1회)** — 스펙 "스위퍼 판단 1회 ≤ 1 µs [제안]"은 007 지점 질의(평균 2.5 µs, 2026-11-18 확정 예산) 1회만 붙어도 넘는다. **추천**: "게이트만 ≤ 1 µs, 게이트 통과 시 + 지점 질의 1회(≤ 2.5 µs)"로 스펙 개정. 011 "결정 틱당 GK 2명 전체 평균 ≤ 1.5 µs"는 게이트 통과가 드물어 유지 가능할 것으로 보고 벤치로 확인.
- **Q3 FR-13 λ_gk 12.9 적용 위치 — 결정(2026-11-19): 추천안** — 071 경합은 softmax 점수식이라 λ가 들어갈 자리가 없다. **추천**: reach + 0.6 m는 071 `act.aerial.gkBonus`(이미 있음)를 그대로 쓰고, λ_gk 12.9는 073의 007 질의(박스 안 손 사용 가능 지점)에서 GK 컨트롤 비율로 쓴다(`gk.sweep.lambdaGk`). 007의 `pcm.lambdaGkK`(핸들링 배율, 0)는 건드리지 않는다.
- **AC-08을 이번에 닫음** — 스펙 문구("연결 때 재확인")와 다르게 처음부터 실물 경로라 대체 → 실물 전환 단계가 없다. 플랜 승인 = 이 해석 승인으로 보고, 완료 때 spec AC-08에 한 줄 메모.
- **경기 흐름 변화로 E2E 시드 깨짐** — GK가 박스 밖으로 나오고 크로스 펀칭이 생기면 같은 시드의 경기 진행이 바뀐다. 082·083·085·086 장면 테스트(고정 시드 장면 번호)가 깨지면 087처럼 시드 재선택 태스크(`TNNa`)로 처리한다.
- **평균 득점 영향** — 크로스 포기·클레임 실패, 돌진 실패(빈 골문)로 득점이 오를 수 있다. 017 목표(2.5~3.0)는 이 스펙 AC가 아니지만, 마지막에 100경기 측정값(득점·선방률·크로스 성공률)을 리포트에 기록만 한다.
- 3D: 클레임 점프·스마더 전용 클립은 080 범위. 이번엔 기존 클립 대체 매핑이라 AC-07은 "장면이 읽히는가"만 본다(손–공 거리 080 보류).
- [미정] 값(κ, k1~k3, 패널티, 스프레드)은 위 표의 [대체] 시작값으로 두고 017에서 보정.

## 구현 중 바뀐 점 (2026-11-19, tasks.md 결과 줄에 근거)
- **λ_gk (Q3)**: 007이 이미 GK에 λ_gk = `pcm.gkMul` 3 × λ_def(4.3 → 12.9)를 쓰고 있어 `gk.sweep.lambdaGk`를 만들지 않았다. "박스 안만"은 박스 밖 첫 접촉점 질의 동안 GK 표시를 끄는 방식(`SweeperModel.PWin`). 대신 FR-03 역할 보정 `gk.sweep.roleSweeper`·`roleTraditional` [대체]를 추가.
- **크로스 손 도달 높이 비교 기준** = `act.ctrlMaxZ` 2.5 m (071 `aerialContactZ` 3.2 m는 착지 예측 기준점이라 점프 도달 2.9 m보다 높다).
- **ClaimFail**: 공 속도 감쇄(`gk.cross.failSpeed`) 없이 GK가 공을 놓치고 그 비행에서 손 잡기 제외.
- **펀칭 실행**: 071 헤더와 같은 접촉 흐름(`HeaderOutcome.Punch`)으로 접촉 순간 쳐내고 Punch(302) 이벤트(헤더 이벤트 없음).
- **세트(FR-20·22)**: 세트 지점 = 011 위치 선정 목표(이등분선 위·깊이 상한 안). 정지는 위치 고정이 아니라 선방 판정에서 정지 예정 틱(예상 슈팅 시각 + N(0, σ_t))으로 정한다(`SaveVelocity` → 011 FR-09 패널티). 위치 고정·속도 상한 구현은 각을 잃어 선방률 61.9 → 50.6 % (T17 측정).
- **스프레드**: 슛 감시 시작 때 한 번 정해 판정·다이빙 동작에 같이 쓴다(`ShotWatcher._spread`, 스냅샷 포함).
- **돌진 클리어(FR-04)**: 072 후보 대신 결정 단계에서 바로 걷어내기(`StandInOnBall.ClearForward`) — GK 소유는 072 경로를 타지 않는다.
- **081 뜬 공 추격**: GK는 크로스 결정을 확정한 비행에서만 빠진다.
- **박힌 상수 → 데이터**: `gk.sweep.range`, `gk.oneOnOne.carrierGap`·`minSpeed`·`coverWidth`·`shootDist`·`commitTime`·`smotherFail`·`setBonus`, `gk.cross.punchK0`, `gk.sweep.emptyXgBase/Slope`.
- **AC-04**: 역할 차이는 성립, 분류 기준 미달 → 보류(README D28).

# 071 — 크로스 · 공중볼 · 헤더 · 슈팅 · xG · 플랜

상태: 승인됨 (2026-11-14)
스펙: ./spec.md

## 접근
지금은 전부 대체다. ① 슈팅: 072가 `StandInOnBallEval.ShotXg`(자체 로지스틱)로 평가하고, 실행은 `StandInOnBall.ShootWithXg`가 **결정 RNG 3회**로 "골문 안 여부(xG × k) → 가로 위치 → 발사 높이"를 굴린다. 속도는 `match.shotSpeed` 고정, 능력치·조준 셀·블록이 없다. 페널티는 같은 `Shoot(penalty)`의 균등 가로. ② 크로스: 072 `OnBallItem.Cross` = 008 `PassModel.Best(LoftedOnly)` 뜬 패스. 타입 구분·착지 오차가 없다. ③ 공중볼: `ActionSystem.Resolve`가 `CtrlMaxZ`(2.5 m) 위 공은 판정 기회를 건너뛰고, 내려온 공을 처음 반경에 들어온 선수가 발로 잡는다 — 헤더가 아예 없다.

이 플랜은 008 `Actions/` 모듈에 **슈팅·크로스·공중볼 3부품**을 더한다. 판단(072가 "슛·크로스할지")과 실행(071이 "어떻게 날아가나·누가 머리에 맞히나")을 가르는 070과 같은 구조다.
1. **xG 평가(FR-10·11·16)** — `ShotModel.Xg`(순수 함수, RNG 없음)가 FR-10 로지스틱을 그대로 계산한다. 입력 묶음 `ShotContext`(거리·각도·헤더·압박·콘 안 블로커 수·GK 위치 이탈·어시스트 종류·원터치 바운드)를 `ShotContextBuilder`가 월드에서 채운다. 페널티는 0.78 고정. 072 `StandInOnBallEval.ShotXg`는 이 함수를 부르게 바꾼다(FR-16 단일 소유). 슈팅 이벤트 F1에 슈팅 순간 xG를 싣는다(015가 읽을 자리, AC-03).
2. **조준(FR-13)** — 011 `GoalGrid` 7×3을 그대로 쓴다. 셀마다 `P_onTarget`(FR-14 σ_aim로 셀 중심 기준 골문 안에 들어갈 확률, 가로·세로 정규 CDF 곱 — 해석식)과 `P_beatGK`를 곱해 최대 셀을 고른다. `P_beatGK`는 **대체 근사**(스펙 "011 FR-14 자리에는 거리·각도 기반 근사"): GK가 셀 중심까지 횡이동·다이빙으로 닿는 시간 vs 공 도달 시간(거리/속도)의 로지스틱. 011 `SaveModel.BeatProbability`는 004 경로를 셀마다 적분해 21셀 × 수백 스텝이 예산(3 µs)을 넘으므로 지금 연결하지 않고 인터페이스(`IShotKeeperModel`)로 끼울 자리만 둔다 → AC-13 연결 때 교체·재측정.
3. **슈팅 실행(FR-14·15)** — 072가 Shoot을 고르면 결정 단계는 `ShotIntent`(조준 셀·파워)를 공 동작으로 예약(008 예약 킥 그대로, 킥 준비 시간 포함)하고, 접촉 서브스텝에 `ShotExecution`이 `v_shot`·`σ_aim`으로 각도 오차를 넣는다(**액션 RNG 고정 4**, 패스와 같은 정규 2쌍). 대체 `ShootWithXg`의 결정 RNG 3회는 사라진다. 블록(FR-15)은 슛이 날아가는 동안 서브스텝마다 공 선분(직전→지금)과 수비 팀 필드 선수 몸 원기둥(005 몸 반지름, 높이 [대체] 1.9 m, 팔 영역 0.8~1.8 m)의 교차로 판정 → 맞으면 `Block` 이벤트 + 굴절(속도 × [대체] 0.4, 반사 + 무작위 각 — 액션 RNG 고정 2), 팔 영역이면 070 `ContactKind.Arm` 접촉에 핸드볼 입력. 블록 판정은 이번 슛의 비행 동안만(슛 종류 킥 직후 ~ 수신·라인·GK 접촉까지).
4. **크로스(FR-01·02·20)** — 072가 Cross를 고르면 071 `CrossModel.ChooseType`이 상황(수신자 위치: 컷백 구역 = 골라인 근처 + 박스 안 뒤쪽 동료 → Cutback, 수비 라인과 GK 사이 공간 → Driven/Early, 그 외 Lofted)과 성향 대체 가중으로 타입을 고른다(**결정 RNG 1**, 결정 단계). 타입 파라미터(FR-01)로 속도·최고점을 정해 의도 공 동작을 만들고, 땅볼(Cutback)·Driven은 008 패스 실행 그대로(FR-02 "008 패스 모델로"), 뜬 공(Early·Lofted)은 접촉 서브스텝에 `σ_landing`(FR-02)을 착지점 2D 정규 오차로 넣어 역산 초속을 구한다(액션 RNG 고정 4). 크로스 이벤트는 기존 `SimEventKind.Cross`(202)를 처음으로 실제로 낸다(A = 타입, F1 = 목표 completion).
5. **공중볼 경합(FR-03~06)** — 뜬 공(최고점 > `CtrlMaxZ`)이 차일 때 `AerialSystem.OnLoftedKick`이 004 예측으로 착지 시각 `t_land`·착지점을 구해 경합을 **예약**한다. 경합 서브스텝 = 공이 내려오며 접촉 높이 상한([대체] 3.2 m — 키 190 cm reach + 점프 0.7 m 근처) 아래로 처음 들어온 서브스텝. 그 순간 착지점 반경 2 m 안 + `TTI ≤ t_land + 0.3 s`인 선수(최대 6명, 거리순)로 FR-03 점수 `S`를 내고 softmax(최댓값 빼기)로 승자를 **액션 RNG 1**로 뽑는다(참여 0명이어도 1 소비). 승자 결과(FR-05)는 **액션 RNG 고정 4**: 공격수 → 헤딩슛(박스 안이고 헤더 xG가 [대체] 문턱 이상) 또는 헤딩 패스(가장 가까운 앞쪽 동료, 008 패스 오차), 수비수 → 클리어(골문 반대 방향, 8~14 m/s). 헤딩슛은 FR-14 σ × 1.6, 조준 셀 선택 그대로. 참여자 사이 접촉은 070 `EmitContact(ContactKind.Aerial)`(승자–최근접 상대 1건). 동작 정보(FR-06)는 참여자마다 도약·정점 시각·정점 높이를 `ActionDetailLog` 새 종류로 기록. GK가 자기 박스에서 손을 쓸 수 있으면 reach + 0.6 m로 경합에만 참여하고, GK가 이기면 기존 011 잡기 경로(`Resolve` hands)로 넘긴다(클레임·펀칭은 073). 경합에 진 선수·참여 안 한 공은 기존 `Resolve`로 흐른다.
6. **페널티(FR-11·17)** — 경기 중 페널티(`StandInOnBall.Shoot(penalty: true)`)를 071 실행으로 바꾼다: 키커 자연 측(주발 반대쪽) 선호 0.6을 **액션 RNG 1**로 고르고 그쪽 낮은 구석 셀(열 0/6 중 하나, 행 0~1 중 `P_onTarget × P_beatGK` 큰 쪽)로 FR-14 실행. xG는 0.78 고정. 승부차기 `PenaltyShootout`(`StandInPenaltyJudge`, 판정 한 틱)는 074·006 몫이라 그대로 둔다(승부차기 압박 배수 074 FR-04 자리는 `ShotExecution`의 σ 배수 인자로만 열어 둔다).

대안: ① 공중볼을 수신(`Resolve`)의 확률 판정 하나에 넣는 안 → 키·점프·타이밍이 컨트롤 확률 곡선에 섞여 FR-04(키 단독 아님) 검정이 어렵고 FR-06 동작 정보를 낼 지점이 없다. 별도 예약 경합으로 간다. ② 조준에 011 `BeatProbability`를 그대로 쓰는 안 → 정확하지만 셀당 경로 적분이라 예산 20배 이상. 근사 + 인터페이스로 가고 연결 때(AC-13) 측정해 정한다.

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `A/` = `ME/Actions/`, `D/` = `ME/UtilityAI/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/Actions/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `A/TechnicalAttributes.cs` | 002 전 대체 입력에 `Heading`·`Jumping`(기본 50)·`HeightCm`(기본 180) 추가. Composure는 `DecisionAttributes` 값, Strength·Positioning은 070에서 이미 추가된 것을 쓴다(이중 정의 금지). `WithAerial(...)` 사본 |
| 〃 | `A/ActionParams.cs`, `A/ActionTuningIds.cs`, `Data/tuning/action.json` | `act.shot.*`·`act.xg.*`·`act.aim.*`·`act.block.*`·`act.cross.*`·`act.aerial.*`·`act.header.*`·`act.pen.*` (아래 표) |
| 〃 | `A/ActionWorld.cs` | Finishing·LongShots·Crossing·Heading·Jumping·HeightCm·Stamina(005)·Composure 슬롯 배열. `Bind`에서 채움 |
| 〃 | `A/ShotContext.cs` | FR-10 입력 값 타입(거리·각도·header·pressure·blockers·gkOut·assistType·firstTimeBouncing·penalty) |
| 〃 | `A/ShotModel.cs` | FR-10 `Xg(in ShotContext)`, `Angle(x, y)`(분모 0·음수 +π 처리, 골라인 밖 → xG 0), FR-11 페널티 0.78, 0.001~0.999 자르기. 순수 함수 |
| 〃 | `A/ShotContextBuilder.cs` | 월드에서 `ShotContext` 채우기: 콘(슈터–두 골포스트 삼각형) 안 수비 필드 선수 수, GK 이탈 = GK와 "슈터–골문 중앙 선 위 이상 위치" 거리를 [대체] 2 m로 나눠 0~1, assistType(컷백 수신 1 / 스루볼 0.5 / 그 외 0 — [대체]), firstTimeBouncing(원터치 + 공 z > 0.3 m) |
| 〃 | `A/ShotAim.cs` | FR-13: 21셀 `P_onTarget × P_beatGK` 최대 셀(같으면 낮은 인덱스), 전부 0이면 먼 하단 기본 셀. `P_onTarget` = 셀 중심 기준 가로·세로 정규 CDF 곱(σ = 거리 × tan σ_aim). 할당 0 |
| 〃 | `A/IShotKeeperModel.cs`, `A/StandInKeeperBeat.cs` | `P_beatGK(origin, cell, speed, gkPos, gkReach)` 인터페이스 + 대체 근사(`sigmoid(k·(t_gk − t_ball))`, t_gk = 셀 중심까지 거리 / [대체] 다이빙 속도 + 반응 [대체] 0.25 s). 011 연결은 AC-13 |
| 〃 | `A/ShotExecution.cs` | FR-14: `SpeedFor(power)`, `SigmaAimDeg(finishing, v, pressure, composure, weakFootMul, header, habitMul, extraMul)`, `Apply(dir, σ, rng)`(RNG 정확히 4: 가로·세로 각 정규 1쌍). 약발 배수는 008 `KickTechnique` 약발 σ 배수(002 FR-04 자리 대체) 재사용 |
| 〃 | `A/ShotBlock.cs` | FR-15: 선분–원기둥 교차(005 몸 반지름), 팔 영역 판정, 굴절 속도(RNG 정확히 2) |
| 〃 | `A/CrossModel.cs` | FR-01 타입 파라미터·`ChooseType`(상황 점수 + 성향 대체 가중 → 결정 RNG 1), FR-02 `SigmaLanding(crossing, pressure)`, 뜬 공 착지 오차 적용(RNG 정확히 4: 정규 2쌍) |
| 〃 | `A/AerialModel.cs` | FR-03: `Reach`, `JumpHeight`(runUpBonus = 접근 속도 [대체] 0~6 m/s → 1.0~1.15, staminaFactor = 0.85 + 0.15·stamina [대체]), `Timing`, `Score`, `SoftmaxPick`(최댓값 빼기, RNG 1). 순수 함수 |
| 〃 | `A/AerialSystem.cs` | 경기당 1개(`ActionSystem.Aerials`): 예약 경합 1건(착지 시각·착지점·킥 종류·크로스 타입), 경합 서브스텝 판정, 참여자 버퍼(6), 승자 결과(헤딩슛·헤딩 패스·클리어, RNG 4), 동작 정보 기록, 접촉 발행, 해시·쓰기·읽기 |
| 〃 | `A/AerialDetail.cs` | FR-06 값 타입: Slot·JumpSubstep·ApexSubstep·ApexHeight·Won. `ActionDetailLog` 종류 `KindAerial = 3`(070 접촉과 같은 고정 배열 방식, 용량 [대체] 2,048) |
| 〃 | `A/ActionSystem.cs` | `Aerials`·슛 비행 상태(블록 대상) 보유·해시·스냅샷. `Execute`: 슛이면 `ShotExecution`, 크로스면 `CrossModel` 오차 + Cross 이벤트, 뜬 공이면 `Aerials.OnLoftedKick`. `Resolve`: 경합 예약 중에는 착지점 반경 안 선수의 머리 위 판정 생략 규칙 유지, 경합이 끝난 뒤 진 선수는 기존 경로 |
| 〃 | `A/BallAction`(기존 `PendingKick.cs`·`BallAction` 정의 파일) | `ShotCell`·`ShotPower`·`CrossType`·`IsHeader` 필드 추가(스냅샷 읽기·쓰기) |
| 〃 | `A/ActionOutcome.cs` | `Header` 결과 매핑, 블록·크로스 이벤트 매핑 |
| 〃 | `ME/Match/Stages/PhysicsStage.cs` | 서브스텝 순서에 `Actions.Aerials.OnSubstep`(공 적분 뒤, GK 앞)·`Actions.OnShotFlight`(블록, 공 적분 뒤) 추가 — 아래 결정론 절 |
| 〃 | `ME/Match/Stages/UtilityDecisionStage.cs` | Shoot → `ShotAim`으로 셀·파워를 정해 예약 슛 공 동작(대체 `ShootWithXg` 제거). Cross → `CrossModel.ChooseType` 뒤 의도 공 동작 |
| 〃 | `ME/Match/Stages/StandIn/StandInOnBall.cs` | 페널티(`Shoot(penalty:true)`) → 071 페널티 실행. `ShootWithXg` 삭제. `SimpleDecisionStage`(006 대체 파이프라인)의 일반 슛 경로는 그대로 두어 006 해시 불변 |
| 〃 | `D/StandInOnBallEval.cs` | `ShotXg` → `ShotModel.Xg`(같은 `ShotContextBuilder`, 결정 월드 입력으로). 072 전용 계수 `OnBallShot*`는 사용처가 없어지면 남겨 두되 읽지 않음(튜닝 파일 정리는 017) |
| 〃 | `ME/Scenarios/StepExecutor.cs` | 시나리오 Shot/Cross 단계도 같은 071 경로(조준 셀·크로스 타입) |
| 〃 | `ME/Match/Events/SimEventKind.cs` | `Header = 215`(A = 결과 종류 0 슛·1 패스·2 클리어, OtherId = 경합 최근접 상대, F0 = 승리 확률, F1 = 헤더 xG). Shot(205) F1 = xG, A 비트 = 페널티·헤더·블록 대상. Cross(202) A = 타입. Block(206) A = 팔 영역 비트 |
| 〃 | `ME/Match/{MatchContext,MatchSim}.cs`, `ME/Match/Replay/*` | 스냅샷 섹션(액션 섹션 안 경합·슛 비행 상태, 섹션 길이로 구분), `MotionHint.Header`(없으면 추가, 043 맵은 기존 머리 접촉 클립이 있으면 연결) |
| Presentation | `Presentation/Players/Data/043-anim-map.json`, `LiveSceneSelector` | `Header` 이벤트 → 헤더 클립(있으면, 없으면 Kick과 같은 처리), 하이라이트 장면 점수에서 헤더 슛을 슛과 같게. 054 FR-43 도약 동작 연결은 054 몫(FR-06 기록만 이 스펙) |
| Verification | `Data/e2e/071-cross-shot.json`, 필요하면 `LiveMatchFixture` 시드 탐색 | AC-12 장면 |
| Tests | `T/Shot*Tests.cs`, `T/Cross*Tests.cs`, `T/Aerial*Tests.cs`, `T/Penalty071Tests.cs`, `Tests/Performance/MatchEngine/ShotAerialPerfTests.cs`, Verification 벤치 `ShotAerialBenchWorkloads`(`--spec 071`) | 아래 테스트 전략 |

의존 방향: Actions는 MatchEngine 안 Ball·Kinematics·Goalkeeper(GoalGrid만)·Match 타입만 쓰고 UnityEngine 비참조. Presentation은 SimEvent만 읽는다. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
`Data/tuning/action.json` 추가 항목 (`[설계]` = 스펙 수식 값, `[제안]`, 스펙 `[미정]`은 `[대체]` 시작값 — 017 보정 대상).

| id | 값 | 태그 | 출처 |
|---|---|---|---|
| act.xg.c0 / kDist / kAngle / kHeader / kPress / kBlock / kGkOut / kAssist / kBounce | −1.1 · 0.10 · 1.6 · 0.75 · 1.2 · 0.9 · 0.8 · 0.6 · 0.4 | 설계 | FR-10 |
| act.xg.penalty | 0.78 | 제안(채택) | FR-11 |
| act.xg.gkOutDist | 2.0 m | 대체 | FR-10 gkOutOfPosition 정규화 [미정] |
| act.xg.assist.cutback / through | 1.0 / 0.5 | 대체 | FR-10 assistType 값 [미정] |
| act.shot.vLo / vHi | 18 / 32 m/s | 설계 | FR-14 |
| act.shot.sigmaMax / sigmaMin / speedK / pressK / headerMul | 6.0° · 1.5° · 0.5 · 0.8 · 1.6 | 설계 | FR-14 |
| act.shot.powerBase / powerDistK | 0.55 / 0.012 | 대체 | 파워 선택(거리가 멀수록 세게) — 스펙에 선택 규칙 없음 |
| act.shot.cutInMul | 0.9 | 설계 | FR-14 "컷인 장인" (008 FR-71 ②) |
| act.aim.beatK / gkDiveSpeed / gkReact | 6 /s · 5 m/s · 0.25 s | 대체 | FR-13 P_beatGK 근사 (011 FR-14 연결 전) |
| act.block.height / armLo / armHi / deflectMul | 1.9 · 0.8 · 1.8 m · 0.4 | 대체 | FR-15 몸 높이·팔 영역(070 `act.hand.armZone*`와 같은 값 재사용)·굴절 감속 |
| act.cross.early.{vLo,vHi,apexLo,apexHi,target} | 20 · 24 · 1.5 · 3 · 0.20 | 설계 | FR-01 |
| act.cross.driven.* | 22 · 28 · 0.5 · 1.5 · 0.22 | 설계 | FR-01 |
| act.cross.lofted.* | 15 · 19 · 4 · 8 · 0.18 | 설계 | FR-01 |
| act.cross.cutback.* | 12 · 16 · 0 · 0 · 0.40 | 설계 | FR-01 (35~45 %의 중앙) |
| act.cross.sigmaHi / sigmaLo / pressK | 4.5 / 1.2 m · 0.6 | 설계 | FR-02 |
| act.cross.cutbackLine / styleK | 6 m · 0.3 | 대체 | ChooseType: 골라인 6 m 안이면 컷백 후보, 성향 가중 [미정] FR-20 |
| act.aerial.radius / ttiSlack | 2.0 m / 0.3 s | 설계 | FR-02·03 |
| act.aerial.heightK / jumpLo / jumpHi / runUpMax | 1.33 · 0.35 · 0.70 m · 1.15 | 설계 | FR-03 |
| act.aerial.timingWin | 0.5 s | 설계 | FR-03 |
| act.aerial.wReach / wTiming / wStr / wHead / headScale / wPos | 4.0 · 1.5 · 0.8 · 0.3 · 0.3 · 0.6 | 설계 | FR-03 (원문 그대로, 실효 헤딩 0.09 — 스펙 [미정]) |
| act.aerial.gkBonus | 0.6 m | 설계 | FR-03 |
| act.aerial.contactZ / maxParticipants | 3.2 m / 6 | 대체 | 경합 서브스텝 높이 상한·참여 상한(성능 예산 "≤ 6명") |
| act.aerial.runUpSpeed / staminaLo | 6 m/s / 0.85 | 대체 | runUpBonus·staminaFactor 매핑 [미정] |
| act.header.shotMinXg / clearLo / clearHi / passMax | 0.04 · 8 · 14 m/s · 15 m | 대체 | FR-05 헤딩슛 vs 헤딩 패스 문턱·클리어 속도·헤딩 패스 거리 |
| act.pen.naturalP | 0.6 | 설계 | FR-17 |

- `BallAction`·`PendingKick`: `ShotCell`(sbyte, −1 없음)·`ShotPower`·`CrossType`(byte)·`IsHeader` — 스냅샷 쓰기·읽기 추가.
- `AerialDetail`(상세 기록 종류 3, 고정 배열): Index · EventSeq · Tick · Substep · Slot · Team · JumpSubstep · ApexSubstep · ApexHeight · Reach · Timing · Won.
- 스냅샷: 액션 섹션에 경합 예약·슛 비행 상태 추가(섹션 길이로 구분, 070과 같은 방식). `MatchSnapshot.FormatVersion`은 기존 스냅샷 읽기 호환이 깨지면 올리고 아니면 유지(테스트로 확인). 세이브 스키마(022) 변경 없음.
- 이벤트 소비자: `LiveSceneSelector`(Shot·Goal 그대로, Header 추가), 015 xG 기록은 015 구현 때(Shot F1을 읽는다).

## 결정론 · 성능
- RNG: 실행은 액션 스트림(`RngStreamId.Action`)만, 액션당 고정 소비(AC-09):
  - 슛 실행 1회 = **4**(조준 오차 가로·세로). 블록 굴절 1회 = **2**.
  - 크로스(뜬 공) 실행 = **4**(착지 오차). Cutback·Driven은 008 패스 실행 4 그대로.
  - 공중볼 경합 1회 = **1**(승자) + 결과 **4**(헤딩슛 조준 4 / 헤딩 패스 008 오차 4 / 클리어 방향·속도 2 + 빈 2). 참여 0명이어도 승자 1 소비, 결과 4 소비(분기와 무관).
  - 페널티 = 방향 **1** + 슛 실행 **4**.
- 결정 스트림: 크로스 타입 선택 결정 RNG **1**(Cross를 고른 뒤 고정 1회). 대체 `ShootWithXg`의 결정 RNG 3회는 사라진다 → 결정 스트림 순서가 바뀌어 010·058·070·072 경기 해시 기대값이 바뀐다. "2회 같음" 테스트는 계속 통과해야 하고, 고정 해시를 박은 테스트는 갱신 목록·사유를 tasks 결과 줄에 남긴다.
- 평가 함수(`ShotModel.Xg`, `ShotAim.Choose`, `StandInKeeperBeat`, `AerialModel.Score`, `CrossModel.SigmaLanding`)는 RNG를 받지 않는다.
- 서브스텝 순서(`PhysicsStage`): 예약 킥(`Actions.OnSubstep` — 슛·크로스 실행) → 예약 태클 → `k.Step()` → 드리블 → 컨트롤 공 위치 → 공 적분 → **슛 블록(`Actions.OnShotFlight`, 수비 슬롯 오름차순, 첫 교차 1건)** → 라인 → **공중볼 경합(`Aerials.OnSubstep`)** → GK → 수신 → 몸싸움. 블록·경합이 공 상태를 바꾸면 뒤 단계는 바뀐 상태를 본다(규칙 고정). 경합 결과 헤딩 킥은 그 서브스텝에 즉시 실행(준비 시간 없음 — 머리 접촉).
- 할당 0: 참여자 버퍼·상세 배열·셀 점수는 생성 시 확보. 진단 null 분기.
- 성능: `ShotAerialPerfTests`(Mono, 예산 4항목 + GC 0 B). 시뮬 틱 경로를 바꾸므로 마지막에 IL2CPP 벤치 1회(`ShotAerialBenchWorkloads` 3항목 + 058 경기 벤치 회귀 +10%).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `AerialModelTests.TallerLate04s_LosesAtLeast30pct_10k`, `.SameTiming_TallerJumper_WinsMore_Binomial` | 로직 | — |
| AC-02 | `ShotModelTests.Order_Penalty_BoxEdge_30m`, `.Header_0p5to0p6_OfFoot`, `.Blockers_Decrease`, `.AllInUnit_NoNaN_GoalLine_BehindGoal` | 로직 | — |
| AC-03 | `Penalty071Tests.PenaltyXg_Fixed078_EqualsShotEventF1` (015 기록 자리 = Shot 이벤트 F1) | 로직 | — |
| AC-04 | `CrossModelTests.LandingSigma_100k_Within3pct`, `ShotExecutionTests.AimSigma_100k_Within3pct` | 로직 | — |
| AC-05 | `ShotAttributeTests.Finishing18vs8_Crossing18vs8_FTest`, `.CutInHabit_Sigma0p9_Within3pct` | 로직 | — |
| AC-06 | `ShotAerialEdgeCaseTests.*` (경합 0명 → 루즈볼·큰 softmax·골라인 위·골대 뒤·조준 셀 전부 0 → 먼 하단·GK 헤더·극단 능력치 0.001~0.999) | 로직 | — |
| AC-07 | `ShotAimTests.GkLeft1m_RightCellsMore_Binomial_1000`, `.ChosenCell_IsArgmax_BeforeError` | 로직 | — |
| AC-08 | `AerialSystemTests.Participants_HaveMotionInfo_ApexWithinLand05`, `.AerialContact_AllContactFieldsFilled` | 로직 | — |
| AC-09 | `ShotAerialDeterminismTests.Cross_Aerial_Shot_Penalty100k_TwoRuns_SameHash_FixedRngCount` | 결정론 | — |
| AC-10 | `ShotAerialPerfTests.*` + 마지막 IL2CPP 벤치 | 성능 | mac-m1-1600p |
| AC-11 | **보류**(017). 지금은 `ShotRateHarnessTests`(고정 상황 크로스 성공률·헤딩 전환율·슛 전환율)와 100경기 측정값(슈팅·슈팅당 xG·크로스·헤더·블록·득점)을 리포트에 기록 | 로직 | — |
| AC-12 | `e2e/071-cross-shot` (058 실경기 경로, 고정 시드 3장면 — 컷백 슛 골·크로스 공중볼 헤더·30 m 중거리 슛) 1프로필 육안 승인. 기준 이미지 비교는 폐지 규칙대로 하지 않음(스펙 문구 "기준 이미지와 비교·smoke"는 `mac-m1-1600p` 1프로필로 읽음). 머리–공 거리는 054 접촉 동기화 측정 로그로 | E2E | mac-m1-1600p |
| AC-13 | **보류**(002·007·011 FR-14 연결 뒤) | 로직 | — |

회귀: 008 `Actions*`(예약 킥·수신·패스), 070 `Duel*`, 072 `OnBall*`(슈팅·크로스 평가 경로), 010 `UtilityAI*`, 011 `Goalkeeper*`(슛 감시·선방이 새 슛 속도를 받음), 013 `Scenario*`(Shot·Cross 단계), 006 `Match*` 결정론·스냅샷, 058 `DeadBallFlowTests`·하이라이트 실경기 픽스처, 043 `AnimationMapTests`. `--scope auto`는 MatchEngine 변경이라 `match` 범위 — 마지막 1회.

## 버그 방지
- 슛 속도 상승(고정 → 18~32 m/s)으로 011 선방률이 바뀜: 100경기 측정에서 득점·선방 비율을 보고. 득점 > 16/경기 또는 슈팅 증가면 멈추고 보고(072·070과 같은 기준).
- 경합 예약 중 공이 다른 경로로 바뀜(GK 선방·블록·라인·다른 선수가 먼저 낮은 높이에서 터치): 공 상태 변경(킥·잡기·골대) 시 예약 취소. `AerialSystemTests.Reservation_CancelledOnTouch`.
- 경합 승자가 다운·이동 불가·교체 중: 참여 제외(070 `IsImmobile`, `IsSlotControllable`).
- 헤딩 직후 같은 선수가 다시 잡음: 기존 킥 쿨다운(25 서브스텝) 그대로.
- 블록 판정에서 슈터 본인·같은 팀·GK(011 선방 몫) 제외. 슛 직후 3 서브스텝은 슈터 주변 오탐 방지로 [대체] 제외.
- 조준 셀이 골포스트 위: `P_onTarget`이 가로 한계에서 자르므로 셀 중심은 항상 골문 안, 오차만 밖으로.
- 골라인 위·골대 뒤 xG: `Angle` 분모 ε, x ≤ 0 → 0 (AC-06).
- softmax 오버플로: 최댓값 빼기, 합 0 방어(AC-06).
- 058 실경기 픽스처 시드가 바뀌어 기존 장면(058·070 E2E)이 다른 경기가 됨: 장면 시드 재탐색은 이 스펙 E2E 태스크에서 071 장면만, 기존 장면은 `--scope auto` 로직 회귀로만 확인하고 깨지면 보고.

## 위험 · 미정
- [미정 → 대체] P_beatGK 근사, 블록 몸 높이·굴절, 헤딩슛 문턱, 경합 접촉 높이, runUp·stamina 매핑, 크로스 타입 선택(성향 FR-20), 파워 선택은 위 `[대체]` 시작값. 고정 상황 하네스로 1차 확인, 017 보정.
- [미정] 감아차기 스핀량(FR-14): 이번에는 스핀 0으로 두고 "컷인 장인" σ×0.9만 적용. 004 마그누스 입력 연결은 스핀 매핑 결정 뒤.
- 헤더 동작: FR-06 기록까지만. 054 FR-43 도약 클립이 없으면 AC-12 헤더 장면이 "서서 머리" 또는 킥 동작으로 보일 수 있다 → 장면에서 확인, 동작은 080·후속 모션 스펙 몫으로 보고.
- 072 슈팅 선택 빈도가 xG 모델 교체로 바뀜(대체 xG 상한 `OnBallXgMax` 없어짐): 100경기 측정 보고, 081(경기 양상 보정) 범위와 겹치면 081로 넘긴다.
- 승부차기는 대체 판정 그대로(074). 경기 중 페널티만 071 실행.

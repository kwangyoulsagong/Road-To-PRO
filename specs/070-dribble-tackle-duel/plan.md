# 070 — 드리블 · 테이크온 · 태클 · 몸싸움 · 접촉 · 플랜

상태: 구현 완료 (2026-11-14) — 플랜과 다른 점은 tasks.md 결과 줄
스펙: ./spec.md

## 접근
지금 결과 판정은 전부 대체다. ① 드리블: 공이 소유자 앞 0.45 m에 붙어 다니고(`PhysicsStage`), 속도는 `standin.carrySpeedRatio` 0.7 고정. 터치·이격이 없어 "긴 터치를 빼앗기는" 일이 없다. ② 테이크온: 072 `DecideOnBall`의 Dribble 항목이 결정 RNG 1회로 성패를 굴리고, 실패면 바로 소유 이전 + Tackle 이벤트. ③ 태클: 010 Press 결정 + 거리 1.2 m 안이면 `TryTackle`이 확률 0.12 × 다혈질로 "걷어내기"(Clearance 6 m/s)를 예약 킥에 넣는다. 능력치·각도·커버가 없고, 다운·늦은 태클·접촉 기록이 없다.

이 플랜은 008 `Actions/` 모듈에 **결투(duel) 하위 시스템**을 더해 판단(010·072가 "시도할지")과 결과(070이 "어떻게 되나")를 가른다.
1. **드리블 터치 사이클(FR-01~03)** — 공은 계속 `Controlled`(009 점유·013·054 그대로). 소유자가 걷는 속도 이상으로 움직이면 터치마다 `DribbleModel`이 이격 거리·방향을 샘플링하고(액션 RNG 고정 4), 공 위치는 그 터치의 004 굴림 예측을 따른다(`SetControlledPosition`으로 매 서브스텝 예측 위치). 소유자가 다시 발 도달 거리에 들어오면 이격이 닫히고 다음 터치. 이격이 열린 동안에만 근처 상대(≤ 4명)의 공 정지 지점 TTI를 소유자와 비교해 탈취를 판정한다(터치당 RNG 1, 상대가 없어도 소비). 이동 상한은 `vmax_ball`(FR-01).
2. **테이크온(FR-04~07)** — 072가 Dribble(상대 지정)을 고르고 FR-04 조건(수비수가 공–골문 사이 · < 3 m)을 만족하면 `TakeOnModel`이 P_beat(FR-05)와 결과 분기(FR-06)를 액션 RNG 2개로 정한다. 조건 밖이면 일반 캐리 터치(1번)로만 진행. 성공은 공격수 1~2 m 분리 + 수비수 회복 지연 0.4 s(이동 불가), 실패는 탈취·아웃·파울 획득(접촉 이벤트, 판정은 012)·루즈볼.
3. **태클(FR-10·11·31)** — 010 Press 결정이 태클 시도 여부를 낸 뒤, 070 `TackleModel`이 스탠딩/슬라이딩(성향 대체 매핑), 도달 거리, 저킹 조건(FR-07)으로 시도 가능성을 거르고 **선수별 예약 태클**로 둔다(008 예약 킥과 같은 방식, 접촉 서브스텝에서 판정 — 같은 서브스텝 두 태클은 슬롯 순, 첫 성공 뒤 무효). 접촉 서브스텝에 P_win(FR-10)을 액션 RNG 2개로 굴린다. 공이 이미 떠났으면 늦은 태클(접촉 이벤트만, 공 획득 없음). 슬라이딩 실패는 1.2 s 다운.
4. **몸싸움(FR-20)** — 이격 0(느린 볼 보호) 소유자에게 압박 중인 상대가 접촉 거리 안이면 주기마다 `ShieldModel`이 근력·밸런스로 볼 지키기를 판정(RNG 1).
5. **접촉 이벤트(FR-13·21)** — 새 이벤트 `Contact`(SimEvent 1건) + `ActionDetailLog`에 접촉 상세 종류 추가(008 FR-72 틀 그대로 일련번호 연결, 해시·스냅샷 창). 태클·테이크온 실패·몸싸움·충돌이 낸다. 핸드볼 입력은 팔 높이 접촉에 붙는 필드 묶음.
6. **이동 불가 시간(005 수정)** — `KinematicsSystem`에 `SetImmobile(id, until)`: 그동안 이동 명령을 받지 않고(무시·카운트) 목표 속도 0, 005 감속 한계로만 멈춘다. 결정 단계는 그 슬롯을 결정하지 않는다. 해시·스냅샷 포함. 005 spec에 FR·AC를 추가한다(스펙 문구 "070 구현 때 추가").

대안: 테이크온·태클을 결정 틱에 한 번에 굴리고 결과를 바로 적용(지금 대체) → 접촉 시각·거리와 결과가 어긋나 "주변에 아무도 없는데 태클" 증상이 계속된다. 예약 → 접촉 서브스텝 판정을 쓴다. 드리블 이격을 "느슨한 공(Rolling)"으로 두는 안은 스펙 FR-03(정리됨: 컨트롤 유지)과 009·013·054 점유 판정이 흔들려 버렸다.

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `A/` = `ME/Actions/`, `D/` = `ME/UtilityAI/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/Actions/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `A/TechnicalAttributes.cs` | 002 전 대체 입력에 `Positioning`·`Strength` 추가(기본 50). Agility·Pace·Balance는 005 `KinematicAttributes` 값을 쓴다(이중 정의 금지) |
| 〃 | `A/ActionParams.cs`, `A/ActionTuningIds.cs`, `Data/tuning/action.json` | `act.drib.*`·`act.takeon.*`·`act.tackle.*`·`act.shield.*`·`act.contact.*`·`act.hand.*` (아래 표, FR·NFR 튜닝 가능성) |
| 〃 | `A/ActionWorld.cs` | 결투 입력 추가: Dribbling·Tackling·Positioning·Strength(기술), Agility·Pace·Balance(005 프로필에서). `Bind`에서 채움 |
| 〃 | `A/DribbleModel.cs` | FR-01·02: `VmaxBall`, `TouchDist`(평균), 방향 σ, `SampleTouch`(RNG 정확히 4: 방향 정규 2 + 거리 정규 2, 거리 σ [대체] 10 %, 상한 5.0 m 자르기). 순수 함수 |
| 〃 | `A/DribbleState.cs` | 소유자 1명의 터치 사이클 상태(값 타입): 이격 열림·터치 시작 서브스텝·터치 원점·초속·방향·정지 지점·탈취 판정 완료. 해시·직렬화 |
| 〃 | `A/TakeOnModel.cs` | FR-04 발동 조건(< 3 m, 수비수가 공격수–골문 사이 = 골문 방향 내적 > 0 · 측면 거리 ≤ [대체] 1.0 m), FR-05 logit(c0 −0.55, 계수 [설계]), `defSetFactor`, `angleAdv`, `cover`(대체: 뒤 두 번째 수비수 005 TTI 근사 → 0~1), FR-06 분기 표본(RNG 정확히 2: 성공 1 + 분기 1, 성공이어도 소비) |
| 〃 | `A/TackleModel.cs` | FR-10·11: 도달 거리(스탠딩 [대체] 1.2 m, 슬라이딩 +1.0), `ballExposure`([대체] 식 아래), `angleFromBehind`, P_win(스탠딩·슬라이딩 −0.3), 늦은 정도, 저킹 조건(FR-07), 스타일 선택(FR-31 대체 매핑), 결과 표본(RNG 정확히 2: 성공 1 + 결과 1) |
| 〃 | `A/ShieldModel.cs` | FR-20: 볼 지키기 확률 `sigmoid(k0 + k1·(0.6·str_att + 0.4·bal_att − 0.6·str_def − 0.4·bal_def))` [대체], 접촉 거리·판정 주기, RNG 1 |
| 〃 | `A/ContactDetail.cs` | FR-13 필드 값 타입(공 먼저 0~1, 강도 0~1, 뒤에서 0~1, 늦은 정도 0~1, 슬라이딩, 발바닥 노출 0~1, 접촉 높이 m, 위치, 박스 안, 득점 기회 스냅샷(골까지 거리·각도·공과 골 사이 수비수 수·공 컨트롤 가능성·비움 플래그), 버릇 비트, 핸드볼 입력(팔 실루엣 0~1·공 거리·반응 시간·자기 몸 굴절)) + `ContactKind`(Tackle·TakeOn·Aerial(071)·Shield·Collision·Arm) |
| 〃 | `A/ContactBuilder.cs` | 접촉 필드 계산(순수 함수): 강도 = 상대 속도 × 유효 질량 정규화, 각도·늦은 정도, 득점 기회 스냅샷, 핸드볼 입력(팔 실루엣 [대체] 식). NaN 방지(피치 밖 → 스냅샷 비움) |
| 〃 | `A/ActionDetailLog.cs` | 상세 종류 `KindContact = 2` 추가. 접촉 상세는 별도 고정 배열(용량 4,096 [대체])로 두고 같은 누적 해시·최근 창(16) 규칙 |
| 〃 | `A/PressTriggers.cs` | FR-14 플래그 비트(등진 수신·불안한 첫 터치 > 1.5 m·백패스·터치라인 3 m 안·상대 진영 볼)를 볼 소유자에게 틱마다 계산. 수신·패스 시 이벤트 플래그로도 남김(009 FR-60·010이 읽는 자리) |
| 〃 | `A/DuelSystem.cs` | 경기당 1개(`ActionSystem.Duels`): 드리블 상태, 선수별 예약 태클(22), 회복·다운 기록, 몸싸움 타이머, 접촉 발행, 해시·쓰기·읽기. RNG는 `ActionSystem.Rng`(액션 스트림) 공유, 소비 고정 |
| 〃 | `A/ActionSystem.cs` | `Duels` 보유·해시·스냅샷 연결. 대체 태클(Clearance + Victim) 경로 제거 → `Duels.ScheduleTackle`. `TransferPossession`에 접촉 이벤트 연결 옵션 |
| 〃 | `A/ActionOutcome.cs` | 슬라이딩 → `SlidingTackle`, `Contact` 이벤트 매핑. `IsTackle` 도달 거리를 `TackleModel.Reach` 기준으로 |
| 〃 | `ME/Kinematics/KinematicsSystem.cs` | 005 수정: `SetImmobile(id, untilSubstep)`·`IsImmobile`·무시한 명령 카운터. 이동 불가 동안 `SetCommand`·`SetCommandImmediate` 무시, 목표 속도 0(가속·감속 한계 그대로). 해시(안 쓰면 해시 불변)·`WriteState`·`ReadState`·`MoveSlot`·`ResetSlot` |
| 〃 | `ME/Match/Stages/PhysicsStage.cs` | 컨트롤 공 위치: 드리블 이격이 열려 있으면 `Duels.BallPosition`(004 굴림 예측), 아니면 기존 0.45 m. 서브스텝마다 `Duels.OnSubstep`(터치 개시·이격 닫힘·탈취 판정·예약 태클·몸싸움) — 기존 `Actions.OnSubstep` 다음, `k.Step()` 다음 순서는 아래 결정론 절 |
| 〃 | `ME/Match/Stages/UtilityDecisionStage.cs` | 소유자 캐리 속도 = `VmaxBall`(FR-01). Dribble(상대 지정) → `Duels.TakeOn`(결정 RNG 대신 액션 RNG). Press 태클 → `Duels.TryTackleAttempt`(시도 여부 확률은 010 대체 그대로, 실행은 070). 이동 불가 슬롯은 결정 생략(Stand, RNG 미소비). `_beatenUntil` 대체 → 이동 불가로 대체 |
| 〃 | `ME/Match/Stages/StandIn/StandInOnBall.cs` | `TryTackle`이 걷어내기 대신 070 예약 태클을 쓰게(UtilityDecisionStage 경로). `SimpleDecisionStage`(006 대체 파이프라인) 경로는 그대로 두어 006 해시 불변 |
| 〃 | `ME/Scenarios/StepExecutor.cs` | Engage 태클도 070 예약 태클로 |
| 〃 | `ME/Match/Events/SimEventKind.cs` | `Contact = 214` (A = ContactKind, B = 상세 번호 + 1, OtherId = 상대, F0 = 강도, F1 = 늦은 정도). Tackle(207) A = 슬라이딩 비트·성공 비트, Dribble(203) B 성공 비트 의미 유지(테이크온) |
| 〃 | `ME/Match/{MatchContext,MatchSim}.cs`, `ME/Match/Replay/*` | 스냅샷 섹션(액션 섹션 뒤 결투 섹션, 섹션 길이로 구분), `MotionHint.Tackle`·다운 표시(기존 `MarkAction`) |
| Presentation | `Presentation/Players/Data/043-anim-map.json`, `LiveSceneSelector` | `Contact`는 동작을 고르지 않음(읽기만 무시). 슬라이딩 비트 → 슬라이딩 태클 클립(있으면). 058 `TackleGate` 기준은 그대로 2.0 m |
| Verification | `Data/e2e/070-duel-situations.json`, 필요하면 `LiveMatchFixture` | AC-11 장면(아래) |
| Spec | `specs/005-*/spec.md` | "이동 불가 시간" FR·AC 추가 (스펙에 이미 승인된 수정 제안 반영) |
| Tests | `T/Duel*Tests.cs`, `Tests/Performance/MatchEngine/DuelPerfTests.cs`, Verification 벤치 `DuelBenchWorkloads`(`--spec 070`) | 아래 테스트 전략 |

의존 방향 확인: Actions는 MatchEngine 안의 Ball·Kinematics·Match·UtilityAI 타입만 쓰고 UnityEngine 비참조. Presentation은 SimEvent만 읽는다. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- `Data/tuning/action.json` 추가 항목 (`[설계]` = 스펙 수식 값, `[제안]`, 스펙 `[미정]`은 `[대체]` 시작값 — 017 보정 대상).

| id | 값 | 태그 | 출처 |
|---|---|---|---|
| act.drib.vmaxLo / vmaxHi | 0.78 / 0.92 | 설계 | FR-01 |
| act.drib.touchLo / touchHi / touchMax | 0.8 / 4.0 / 5.0 m | 설계 | FR-02 |
| act.drib.skillLo / skillHi / wDrib / wTouch | 1.25 / 0.8 / 0.6 / 0.4 | 설계 | FR-02 |
| act.drib.sigmaMax / sigmaMin | 12° / 3° | 설계 | FR-02 |
| act.drib.distSigma | 0.10 (평균 대비) | 대체 | AC-03 "터치 거리 표본" — 거리 산포 크기 [미정] |
| act.drib.minSpeed | 2.0 m/s | 대체 | 이 아래는 이격 없이 붙여 다님(볼 보호, 경계 "이격 0") |
| act.drib.speedNormMax | 9 m/s | 대체 | `speedNorm = 속도 / 이 값` (FR-02 정규화 [미정]) |
| act.drib.stealK / stealMargin | 6 /s · 0.1 s | 대체 | FR-03 탈취: `P = sigmoid(k·(TTI_owner − TTI_opp − margin))` 터치당 1회 |
| act.takeon.range / laneHalf | 3.0 m / 1.0 m | 설계 / 대체 | FR-04 / "공격수와 골문 사이" 측면 허용 |
| act.takeon.c0 / kSkill / kPace / kAngle / kCover / kSet | −0.55 · 2.2 · 1.0 · 0.8 · 0.7 · 0.5 | 설계 | FR-05 |
| act.takeon.setSpeed | 1.5 m/s | 설계 | FR-05 defSetFactor |
| act.takeon.sepLo / sepHi / recover | 1 / 2 m · 0.4 s | 설계 | FR-06 |
| act.takeon.branch.{steal,out,foul,loose} | 0.60 · 0.15 · 0.10 · 0.15 | 설계 | FR-06 |
| act.tackle.reach / slideExtra | 1.2 m / 1.0 m | 대체 / 설계 | FR-10 도달 [미정](지금 대체 1.2 m 유지) / FR-11 |
| act.tackle.b0 / kSkill / kExpose / kBehind / slideB | −0.1 · 2.0 · 1.2 · 0.8 · −0.3 | 설계 | FR-10·11 |
| act.tackle.exposeTouch / exposeDist | 1.5 m / 1.0 m | 대체 | `ballExposure = clamp01(0.5·이격/exposeTouch + 0.5·(1 − |공−수비수| / (reach + exposeDist)))` [미정 → 대체] |
| act.tackle.slideDown / slideFoulMul | 1.2 s / 2.5 | 설계 | FR-11 (파울 배수는 접촉 필드로만) |
| act.tackle.jockeyTouch | 1.2 m | 설계 | FR-07 |
| act.tackle.lateWindow | 0.3 s | 대체 | 공이 떠난 뒤 이 시간 안 접촉 = 늦은 태클(lateness = 경과/창) |
| act.tackle.slideBase / slideTemperK / slideHabitK | 0.20 · 0.15 · 1.0 | 대체 | FR-31 성향 매핑 [미정] → 다혈질(010 Mind)·슬라이딩 장인(010 `HabitLevels`) 대체 |
| act.tackle.winKeep | 0.5 | 대체 | 스탠딩 성공 시 공을 바로 잡을 확률(나머지 찔러내기 6 m/s) — 슬라이딩 성공은 항상 찔러내기 |
| act.shield.dist / period | 0.9 m / 0.5 s | 대체 | FR-20 접촉 거리·판정 주기 [미정] |
| act.shield.k0 / k1 | 1.0 / 3.0 | 대체 | FR-20 계수 [미정] (평균 대결 볼 지키기 ≈ 73 %) |
| act.contact.massLo / massHi / relSpeedMax | 60 / 100 kg · 8 m/s | 대체 | FR-13 강도 정규화 (research[3] 03 B2 유효 질량 60 + 40s) |
| act.hand.armZoneLo / armZoneHi / silBase / silSpeedK | 0.8 / 1.8 m · 0.3 · 0.1 | 대체 | FR-21 팔 실루엣 [미정] |
| act.habit.slideFoul | 1.4 | 설계 | FR-31 버릇 부가 효과(필드로만, 012가 적용) |

- `ContactDetail` (상세 기록 접촉 종류, 고정 배열 4,096): Index · EventSeq · Tick · Substep · Kind · Attacker · Defender · BallFirst · Intensity · FromBehind · Lateness · Sliding · StudsUp · Height · X · Y · InBox · Dogso{Valid, GoalDist, GoalAngle, DefendersBetween, ControlLikely} · HabitBits · Hand{Silhouette, BallDist, ReactTime, SelfDeflect} · HasHand. 발바닥 노출은 [대체] 슬라이딩 + 뒤에서 정도 곱.
- 스냅샷: 액션 섹션에 결투 상태(드리블 상태 1개·예약 태클 22·몸싸움 타이머·접촉 창) 추가, 005 이동 불가는 운동 섹션. 섹션 길이로 구분(072와 같은 방식). `MatchSnapshot.FormatVersion`은 기존 스냅샷 읽기 호환이 깨지면 5 → 6, 깨지지 않으면 유지(테스트로 확인). 세이브 스키마(022) 변경 없음.
- 이벤트 소비자: `LiveSceneSelector`·`AnimPlan`은 Tackle·Dribble를 그대로 읽는다. `Contact`는 012·027 전까지 소비자 없음(로그·하이라이트 무시).

## 결정론 · 성능
- RNG: 액션 스트림(`RngStreamId.Action`)만. 액션당 고정 소비(FR-61, AC-08):
  - 드리블 터치 1회 = 4(방향 정규 2 + 거리 정규 2) + 탈취 판정 1 = **5**. 근처 상대가 없어도 탈취 1 소비.
  - 테이크온 1회 = **2**(성공 1 + 분기 1, 성공이어도 분기 소비).
  - 태클 판정 1회 = **2**(성공 1 + 결과 1). 늦은 태클·무효 태클도 2 소비(분기와 무관).
  - 몸싸움 판정 1회 = **1**.
  - 스타일(스탠딩/슬라이딩) 선택은 결정 단계(010)에서 결정 RNG 1 — 시도 여부 굴림 직후 고정 1회.
- 기존 대체 테이크온의 결정 RNG 1회 소비는 사라진다 → 결정 스트림 순서가 바뀌므로 010·072 경기 해시 기대값이 바뀐다. "2회 같음" 테스트는 계속 통과해야 하고, 고정 해시를 박은 테스트는 갱신 목록·사유를 tasks 결과에 남긴다.
- 서브스텝 순서(`PhysicsStage`): 예약 킥(`Actions.OnSubstep`) → 예약 태클(`Duels.OnTackles`, 슬롯 오름차순) → `k.Step()` → 드리블(`Duels.OnDribble`: 이격 닫힘/터치 개시/탈취) → 컨트롤 공 위치 → 공 적분 → 라인 → GK → 수신 → 몸싸움(`Duels.OnShield`, 소유자 기준 상대 슬롯 오름차순). 같은 서브스텝에 공이 소유자를 떠나면 이후 단계는 바뀐 상태를 본다(규칙 고정).
- 평가 함수(`TakeOnModel.Probability`, `TackleModel.WinProbability`, `DribbleModel.TouchDist/Sigma`, `ShieldModel.Probability`, `ContactBuilder`)는 RNG를 인자로 받지 않는다.
- 할당 0: 예약 태클 배열·근처 상대 버퍼(4)·접촉 배열은 생성 시 확보. 진단 null 분기.
- 성능: `DuelPerfTests`(Mono, 예산 표 5항목 + GC 0 B). 시뮬 틱 경로를 바꾸므로 마지막에 IL2CPP 벤치 1회(`DuelBenchWorkloads` 4항목 + 058 경기 벤치 회귀 +10%).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `TakeOnTests.Average_37pct_100k`, `.Branches_100k_Within1pp` | 로직 | — |
| AC-02 | `ContactEventTests.Tackle_TakeOn_Aerial_Shield_OneEachAllFields` (공중볼은 `ContactBuilder` 직접 호출 — 071이 낼 형식), `.FromBehind_GreaterThanFrontal`, `.LateTackle_LatenessPositive` | 로직 | — |
| AC-03 | `DribbleModelTests.TouchDistAndSigma_100k_Within3pct_MaxBelow5` | 로직 | — |
| AC-04 | `DuelAttributeTests.Dribbling18vs8_FTest_And_BinomialTakeOn` | 로직 | — |
| AC-05 | `DuelEdgeCaseTests.*` (동시 태클·늦은 태클·도달 밖 획득 → 인터셉트/회수·3 m 경계·이격 0 → 몸싸움만·다운 중 시도 불가/대상 가능·극단 능력치 0.001~0.999·피치 밖 스냅샷 비움) | 로직 | — |
| AC-06 | `DribbleCycleTests.BallControlledEveryTick_StealOnlyWhenGapOpen`, `.Gap4m_StealsMore_Than1m_1000` | 로직 | — |
| AC-07 | `ImmobileTests.SlideFail_NoCommands12s_NoLimitBreach_HashChanges` (005 `MotionLimitMonitor`, 무시한 명령 카운터, 체크포인트 해시) + 005 쪽 `KinematicsImmobileTests` | 로직 | — |
| AC-08 | `DuelDeterminismTests.Duels100k_TwoRuns_SameHash_FixedRngCount` | 결정론 | — |
| AC-09 | `DuelPerfTests.*` + 마지막 IL2CPP 벤치 | 성능 | mac-m1-1600p |
| AC-10 | **보류**(017). 지금은 `DuelRateHarnessTests`(고정 상황별 테이크온·태클 성공률)와 100경기 측정값(테이크온·태클·접촉·PPDA 근사·슈팅·득점)을 리포트에 기록 | 로직 | — |
| AC-11 | `e2e/070-duel-situations` (058 실경기 경로, 고정 시드 4장면 — 테이크온 성공·테이크온 실패 탈취·스탠딩 태클·늦은 슬라이딩 태클) 1프로필 육안 승인. 기준 이미지 비교는 폐지 규칙대로 하지 않음. "주변 상대 없는 태클 0건"은 `DuelMatchTests.Tackles_CarrierWithin2m_100Matches`(로직)와 장면 로그 `TackleGate` 대체 0건으로 | E2E | mac-m1-1600p |
| AC-12 | **보류**(002·003·007·012·027 구현 뒤) | 로직 | — |

회귀: 008 `Actions*`(예약 킥·수신), 010 `UtilityAI*`·단계, 072 `OnBall*`(Dribble 경로), 013 `Scenario*`(Engage 태클), 005 `Kinematics*`(이동 불가 추가 — 안 쓰면 해시 불변), 006 `Match*` 결정론·스냅샷, 058 `DeadBallFlowTests`·하이라이트 실경기 픽스처, 043 `AnimationMapTests`. `--scope auto`는 MatchEngine 변경이라 `match` 범위 — 마지막 1회.

## 버그 방지
- 이격 중 공이 라인을 넘음: 굴림 예측 위치로 라인을 넘으면 기존 "몰고 나간 공" 경로(`OutsidePitch`)가 처리 — 마지막 터치 = 소유자. `DribbleCycleTests.TouchOverLine_BoundaryCall`.
- 이격 중 소유자가 패스·슛 예약: 예약 킥 도달 거리 판정이 실제 공 위치를 보므로 이격이 닫혀야 찬다(지연은 현실적). 도달 거리 밖이 길면 기존 만료 취소 — `DribbleCycleTests.KickWhileGapOpen_ExecutesWhenReached`.
- 이격 중 데드볼 전환·소유 이전·교체: 드리블 상태를 소유자 변경 시 초기화. `DuelEdgeCaseTests.OwnerChange_ResetsDribble`.
- 소유자가 멈추거나 방향을 크게 틀 때 공이 앞에서 계속 굴러감: 터치 초속은 소유자 속도 기준 정지 지점이 touchDist가 되게 정하고, 소유자 진행 방향이 [대체] 90° 넘게 바뀌면 이격을 그 자리에서 닫지 않고 기다린다(소유자가 공으로 간다 — 072 캐리 목표가 공 쪽으로 보정). 1,000시드 `DribbleCycleTests.NoBallTeleport_MaxStep`(공 위치 서브스텝 이동 ≤ 004 최대 속도 × dt).
- 예약 태클 중 대상이 공을 잃음/공이 떠남: 늦은 태클 창 안이면 접촉만, 밖이면 취소(접촉 없음). 대상 아닌 다른 소유자는 판정 안 함.
- 태클 성공 직후 재태클 핑퐁: 성공자·대상 터치 쿨다운(기존 25 서브스텝 규칙 재사용).
- 다운 중 공이 와도 잡지 않음: `ReceptionResolver`에서 이동 불가 슬롯 제외 [대체] — 다운은 컨트롤 반경 판정 제외. `DuelEdgeCaseTests.DownedPlayer_CannotClaim`.
- GK: 테이크온·태클 대상이 될 수 있으나 시도자(필드 선수 태클 모델)는 아님. GK 바쁜 슬롯(다이빙)은 제외(011 회귀).
- 수치: logit 안정 시그모이드(008 `ActionMath`), 길이 ε 검사, 확률 0.001~0.999, 피치 밖 득점 기회 스냅샷 비움.
- 경기 지표가 크게 바뀔 수 있음(072 뒤 슈팅 ~150/경기): 100경기 측정을 T 마지막에 리포트. 득점 > 16 또는 슈팅이 늘면 멈추고 보고(072 T11a와 같은 기준).
- 058 `058-live-variety` 수비 장면 보류(`Data/e2e/log-ignore.json`, 072 메모): 이 스펙 마지막에 항목을 지우고 재촬영해 확인.

## 위험 · 미정
- [미정 → 대체] 스탠딩 도달 거리(1.2 m), `ballExposure` 식, 몸싸움 계수, 팔 실루엣 식, 성향 → 스타일 매핑, 터치 거리 산포, 탈취 TTI 비교식은 위 `[대체]` 시작값. 고정 상황 하네스로 1차 확인, 017 보정.
- 드리블 이격이 화면에서 공이 선수 앞 최대 5 m까지 굴러가 보이게 한다(스펙 의도). 054·058 드리블 동작(공 달고 뛰기)이 이격을 따라가지 못하면 어색할 수 있다 → AC-11 장면에서 확인, 동작 쪽 수정은 "실경기 모션 다듬기" 후속 스펙 몫으로 보고.
- 핸드볼 입력은 이 스펙에서 "팔 높이(0.8~1.8 m)로 필드 선수 컨트롤 반경을 지나는 공" 접촉에 필드를 붙이는 데까지만 한다. 공 굴절 물리(블록)는 071 FR-15. 소비자 012 FR-50 전이라 AC-02 필드 채움으로만 확인.
- FR-14 압박 트리거 플래그의 소비자(009 FR-60~61)가 아직 없다 → 플래그 계산·테스트만, 연결은 009 쪽.
- 기존 대체 태클 확률 0.12(결정마다)는 "시도 여부"로 남는다(010 몫). 실제 태클 수가 이 값과 FR-10 성공률의 곱으로 바뀌어 경기 흐름이 달라질 수 있음 — 100경기 측정 보고.
- 005 "이동 불가 시간"은 005 스펙 본문 수정이다(스펙에 이미 승인된 수정 제안). FR·AC 문구는 T01에서 넣고 사용자 승인 시 함께 확인받는다.

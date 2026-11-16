# 081 — 경기 양상 1차 보정 · 플랜

상태: 승인됨
스펙: ./spec.md

## 접근
지금 측정은 070 `DuelMatchTests`·071 `ShotMatchTests`의 `Report_100Matches_*`(Explicit, 시드 7400 + i, `Parallel.For`)가 따로 지표를 낸다. 이 플랜은 둘을 **한 측정 도구**(`MatchFeelMetrics` 집계 + `MatchFeelReportTests` 실행)로 합쳐 FR-01 지표를 한 번에 내고 JSON으로 저장한다. 그 뒤 순서는 **① 측정 도구 → ② 보정 전 기준 측정(규칙·계수 변경 전) → ③ 규칙 2개(크로스 후보·착지점 추격) → ④ 계수 보정 루프(최대 3회) → ⑤ 하이라이트 규칙 2개(수비 장면·결투 줌) → ⑥ 장면 재촬영**이다.

규칙 ①(FR-23)은 `OnBallCandidates`의 크로스 분기를 바꾼다: 지금은 "측면 구역 + 그 동료가 박스 안"일 때만 그 동료에게 뜬 패스를 낸다. 여기에 **박스 안 고정 지점 3곳**(니어·파 포스트·페널티 스폿, `[대체]`) 후보를 더하고, 각 지점에 TTI가 가장 짧은 동료를 받는 선수로 지정한다. 실행은 지금 크로스 경로(`IntentAction` → `ApplyCrossType`) 그대로다.
규칙 ②(FR-24·25)는 `UtilityDecisionStage`의 071 T08a 승자 추격(`Aerials.TryChase`) 바로 뒤에 둔다. 경합이 예약(armed)되고 아직 판정 전인 동안, 매 틱 팀별로 착지점 TTI 최소 1~2명을 다시 골라(RNG 없음) `MoveTo(착지점)`을 준다. 상태를 저장하지 않으므로 스냅샷 형식은 바뀌지 않는다.

계수 보정은 `Data/tuning/*.json`의 `[대체]` 값만 바꾼다. 보정 루프 1회 = 계수 변경 → 100경기 측정 → 표 기록이다. 3회 안에 FR-10을 동시에 못 맞추거나 `[설계]` 계수가 막고 있으면 멈추고 보고한다(FR-11, 경계 규칙).
하이라이트는 `LiveSceneSelector` 선택 단계 끝에 "수비 장면 최소 1개" 교체 규칙을, `FramingSolver`에 "결투 초점" 입력을 더한다.

대안: 계수만 조이고 규칙은 나중에 → 크로스 0.4·빈 경합은 계수로 못 고친다(071 T08a 결론). 그래서 스펙대로 규칙 2개를 먼저 넣고 같은 측정 루프에서 맞춘다.

### 플랜이 스펙 해석으로 정하는 것 (승인 질문에 포함)
1. **"수비 형태 이탈 인원 상한"(FR-24)**: 009/010에는 팀 단위 이탈 인원 상한 값이 없다(FR-85는 선수 단위 역할 자유도 × 규율). 그래서 착지점 추격 인원 상한(`dec.aerial.chasers` 팀당 2 [제안])을 팀 상한으로 쓰고, 수비 팀은 최후방 수비수(오프사이드 라인을 정하는 선수)를 추격 대상에서 뺀다 `[대체]`.
2. **"먼저 닿을 수 있는 동료"(FR-23)**: 받는 선수는 그 지점에 TTI가 가장 짧은 **동료**다(상대보다 빠른지는 따지지 않는다). 상대와의 경합은 071 공중볼 판정이 정한다. 단, TTI가 공 비행 시간 + `dec.onball.cross.ttiSlack`(1.0 s [대체])보다 길면 후보를 내지 않는다(경계: 닿을 동료 없음).
3. **[미정] 값에 `[대체]` 시작값**: 박스 지점 좌표(니어 = 골라인에서 5.5 m·니어 포스트 Y, 파 = 6 m·파 포스트 Y, 스폿 = 11 m·중앙), 결투 줌 FOV = 기본 FOV × 0.45(2026-11-16 육안 승인, 시작값 0.7)·유지 = 사건 전 1.0 s ~ 후 1.5 s, 결투 해제 거리 8 m. 육안 승인 때 조정.
4. **보정 전 기준(FR-02)**: main 60f53c7 코드에 **측정 전용 카운터만 더한 상태**(양 팀 참여 경합 수 등)에서 잰다. 카운터는 시뮬 상태가 아니라 해시에 들어가지 않는다.

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `PR/` = `Assets/_Project/Presentation/Runtime/Highlight/`, `T/` = `Assets/_Project/Tests/EditMode/`, `P/` = `Assets/_Project/Tests/Performance/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `ME/UtilityAI/OnBallCandidates.cs` (수정) | FR-23: 측면 구역이면 박스 지점 3곳 × TTI 최소 동료로 크로스 후보 추가(지점 순서 고정 니어 → 파 → 스폿). 기존 "박스 안 동료" 크로스는 유지하고, 같은 받는 선수면 성공 확률이 높은 쪽 1개만 남긴다. 성공 확률 = `_eval.CrossSuccess`(072 자리 = 071 평가). 할당 0 |
| 〃 | `ME/UtilityAI/CrossTargets.cs` (새) | 박스 지점 3곳 좌표(공격 방향·크로서 쪽 측면으로 니어/파 결정)와 지점별 TTI 최소 동료 고르기. 순수 함수, 동점 = 낮은 슬롯 |
| 〃 | `ME/UtilityAI/AerialChase.cs` (새) | FR-24·25: 착지점(`AerialSystem` 예측 착지점)·팀별 TTI 순 상위 `chasers`명 선택. 제외: 경기장 밖 착지점, GK(자기 박스 안 착지점이 아니면), 받는 선수(008 T14a)·경합 승자(071 T08a), 이동 불가(다운). 고정 배열, RNG 없음 |
| 〃 | `ME/Actions/AerialSystem.cs` (수정) | 예약 상태의 예측 착지점·착지까지 남은 시간 공개(`TryLanding`). 측정 카운터 `BothTeamContests`(진단 누계, 해시·스냅샷 밖) |
| 〃 | `ME/Match/Stages/UtilityDecisionStage.cs` (수정) | `TryChase` 다음에 `AerialChase` 결과면 `MoveTo(착지점, 스프린트)`. 결정 주기와 무관하게 매 틱(071 T08a와 같은 위치) |
| 〃 | `ME/UtilityAI/DecisionParams.cs`, `DecisionTuningIds.cs` (수정) | 새 튜닝: `dec.aerial.chasers`, `dec.aerial.maxDist`, `dec.onball.cross.near/far/spot*`, `dec.onball.cross.ttiSlack` |
| RoadToPro.Presentation | `PR/Selection/LiveSceneSelector.cs` (수정) | FR-30: 선택 끝에서 수비 장면이 없고 수비 후보 묶음이 있으면, 점수가 가장 높은 수비 묶음을 넣고 비필수(Must 아님·골 아님) 중 점수가 가장 낮은 묶음을 뺀다. 겹치면 그다음 수비 후보. 장면 수가 상한 미만이면 빼지 않고 더한다 |
| 〃 | `PR/Broadcast/Logic/SceneBrief.cs`, `FramingTargets.cs`, `FramingSolver.cs`, `CameraTuning.cs` (수정) | FR-31(2026-11-16 개정): 장면 안 테이크온·태클 순간(대표 사건 포함) ± 유지 시간 동안 두 선수를 대상에 넣고 FOV 상한을 결투 FOV로 낮춘다(기존 감쇠 `ZoomDamp` 그대로 → 흔들림 없음). 두 선수 거리 > 해제 거리면 기본 프레이밍 |
| — | `Data/tuning/decision.json`, `action.json`, `goalkeeper.json` | 보정 대상 `[대체]` 값 + 새 규칙 계수 (아래 "데이터") |
| — | `Data/tuning/camera.json`, `highlight.json` | `camera.duel.fovScale`·`camera.duel.leadSeconds`·`camera.duel.tailSeconds`·`camera.duel.releaseDist` |
| — | `Data/e2e/081-match-feel-scenes.json` | AC-07 장면 (시드는 071 T12처럼 탐색해 고정) |
| — | `Data/perf/081-match-feel-calibration.json` | AC-09 벤치 예산 (072 값 그대로) |
| RoadToPro.Tests.EditMode | `T/MatchEngine/MatchFeelMetrics.cs`, `T/MatchEngine/MatchFeelReportTests.cs` (새) | FR-01·02 측정 도구. 070·071 측정 테스트는 이 도구를 부르게 바꾸지 않고 그대로 둔다(기존 증거 재현용) |
| 〃 | `T/MatchEngine/UtilityAI/CrossTargetsTests.cs`, `AerialChaseTests.cs`, `T/MatchEngine/TuningProvenanceTests.cs`, `T/Presentation/Highlight/DefensiveSceneRuleTests.cs`, `T/Presentation/Highlight/Broadcast/DuelFramingTests.cs` (새) | 테스트 전략 표 |
| RoadToPro.Tests.Performance | `P/MatchEngine/MatchFeelPerfTests.cs` (새) | 착지점 추격·크로스 후보 포함 결정 틱·GC 0 B (Mono 참고값) |
| RoadToPro.Verification | `Runtime/Bench/SimBench.cs` (수정) | `--spec 081` 등록 (072 워크로드 재사용 + 뜬 공 비행 구간) |

의존 방향 확인: 엔진 변경은 전부 `MatchEngine` 안(→ Core만), UnityEngine 비참조. 하이라이트 변경은 `Presentation` 안. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- **측정 결과**: `evidence/measure-before.json`·`measure-after.json`(+ 루프마다 `measure-iterN.json`) — 지표 이름·경기당 값·N·시드 범위·코드 커밋. `/verify` 리포트가 전·후 표로 보여준다(FR-02). 측정 수 N은 `.farm/feel-n.txt`로 바꿀 수 있다(기본 100).
- **지표 정의**(FR-01, 070·071 집계와 같게): 패스 성공률 = `PassReceived`/`Pass`, 테이크온 = `TakeOn` 이벤트(성공 비트), 태클 = `Tackle`(성공), 이격 탈취율 = `Duels.Steals`/`Duels.Touches`, 시퀀스 평균 지속 = 연속 `PossessionChange` 사이 틱 평균(데드볼 포함), PPDA 근사 = 패스 ÷ (태클 + 인터셉트), 유효 슈팅 = 선방 + 득점 ÷ 슈팅, 전환율 = 득점 ÷ 슈팅, GK 선방률 = 선방 ÷ (선방 + 득점), 크로스 성공 = 크로스 뒤 같은 팀 첫 터치(수신·헤더), 양 팀 경합 = `BothTeamContests`, 빈 경합 = `EmptyContests`, 헤딩 전환율 = 헤딩슛 득점 ÷ 헤딩슛.
- **보정 손잡이 (`[대체]`만, FR-11)** — 루프 1회차 전에 패스 실패 원인(인터셉트·라인 밖·첫 터치·이격)을 분해해 어느 손잡이부터 움직일지 정한다:
  - 드리블 탈취·소유 변경: `act.touch.sigmaBase/sigmaSlope/pressK/controlDist/cooldown`, `dec.onball.dribble.step/engage`.
  - 패스: `act.touch.*`(첫 터치), `dec.onball.*` 패스 선택 가중(Risk 손실 가중 등), 008 패스 실행 `[대체]`.
  - 테이크온 빈도(FR-20): `dec.onball.dribble.b0/b1/b2/engage/cone`, `act.takeon.armTime`·`laneHalf`. 성공률은 `act.takeon.*` `[설계]`라 바꾸지 않는다.
  - 슈팅(FR-21): `act.shot.powerBase/powerDistK`, `act.aim.beatK/gkReact/gkDiveSpeed/gkReach`, `dec.onball.shot.range/b0/bDist/bAngle/bPress/onTargetK/missMax`, `gk.save.alpha/beta/gamma/delta/b/c`. 071 FR-14 σ_aim·`act.cross.*` `[설계]`는 그대로(AC-12에서 값 불변 확인).
  - 크로스(FR-23): `dec.onball.cross.wide/third/base/slope` + 새 지점 계수.
- **새 계수** (전부 튜닝 파일, 코드 상수 없음): `dec.aerial.chasers` 2 `[제안]`, `dec.aerial.maxDist` 25 m `[대체]`(이보다 먼 선수는 추격 안 함), `dec.onball.cross.nearX` 5.5·`farX` 6·`spotX` 11 m `[대체]`, `dec.onball.cross.ttiSlack` 1.0 s `[대체]`, `camera.duel.*` 4개 `[대체]`.
- **출처 칸(FR-12)**: 바꾼 계수마다 `source`에 `081: <이전> → <새>, <측정 근거>`. `TuningProvenanceTests`가 "main 대비 값이 바뀐 id는 태그 `[대체]`이고 source에 081 기록이 있다"를 확인(AC-03). 기존 "파일 = 코드 기본값" 테스트도 통과해야 한다.
- 세이브·스냅샷 형식 변경 없음(착지점 추격은 매 틱 재계산, 카운터는 상태 밖).

## 결정론 · 성능
- RNG: 새 규칙 2개는 RNG를 쓰지 않는다. 크로스 후보가 늘면 072 노이즈 표본 수(후보당 1)가 늘어 같은 시드의 이후 굴림이 달라진다 → 고정 해시 기대값이 바뀌는 테스트는 목록·사유로 보고하고 갱신(NFR).
- 순회 순서: 착지점 추격 = 팀 0 → 1, 팀 안은 TTI 오름차순, 동점 = 낮은 슬롯. 크로스 지점 = 니어 → 파 → 스폿, 동료 = 슬롯 순.
- 할당 0: 고정 배열(팀당 추격 2, 지점 3). LINQ·Dictionary 없음. TTI는 005 기존 계산(`ActionWorld` TTI) 재사용.
- 성능: 착지점 추격은 뜬 공 비행 중에만 틱당 최대 20명 TTI. 크로스 후보는 측면 구역 소유자만 + 3지점 × 시야 동료. 판정은 IL2CPP 벤치 `--spec 081`(결정 틱 평균 ≤ 12 µs, 틱 GC 0 B). 072 평균이 이미 예산에 가까우니, 넘으면 원인 분해 표와 함께 사용자에게 묻는다(예산을 임의로 바꾸지 않음).
- 장면 선정(≤ 5 ms)은 058 Performance 테스트를 다시 돌려 확인.
- 코어 변경이라 `determinism-reviewer` 리뷰.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `MatchFeel_SameSeeds_SameMetrics`(4경기 × 2회, 값 동일), `MatchFeel_AllFr01MetricsPresent` | 로직 | — |
| AC-02 | `MatchFeelReportTests.Report_100Matches`(Explicit, 측정 JSON) + `/verify` 전·후 표·FR-10 범위 판정 | 로직 | — |
| AC-03 | `TuningProvenance_ChangedIdsAreStandIn_WithSource`, 기존 `Tuning_FileMatchesCodeDefaults` | 로직 | — |
| AC-04 | 072 `Twin_DribbleAddict_Fixture1000` 재실행 (보정 뒤 계수로) | 로직 | — |
| AC-05 | `DefensiveRule_CandidatePresent_AtLeastOne`, `DefensiveRule_NoCandidate_Unchanged`, `DefensiveRule_AlreadyPresent_Unchanged` (고정 이벤트 로그 3종) | 로직 | — |
| AC-06 | `DuelFraming_TakeOn_BothInView_FovNarrower`, `DuelFraming_Tackle_BothInView`, `DuelFraming_FarApart_ReleasesToDefault` | 로직 | — |
| AC-07 | `e2e/081-match-feel-scenes` (+ `070-duel-situations`·`058-live-variety`·`071-cross-shot` 재실행), 헤더 접촉 프레임 머리–공 거리는 054 `ContactSync` 측정값 기록 | E2E | mac-m1-1600p |
| AC-08 | `Determinism_90min_FeelHashSame`, `Determinism_DiagnosticsOnOff_SameHash` (착지점 추격·크로스 후보 포함 경기) | 결정론 | — |
| AC-09 | `Perf_DecisionTick_WithAerialChase`, `Perf_Tick_ZeroGc` + IL2CPP 벤치 `--spec 081`, 058 `Perf_SceneSelection` | 성능 | mac-m1-1600p |
| AC-10 | `CrossTargets_WideNoMateInBox_CandidateWithTtiReceiver`, `CrossTargets_NoReachableMate_NoCandidate`, `Twin_CrossMachine_MoreCrosses`(1,000회, 이항 p < 0.01) | 로직 | — |
| AC-11 | `AerialChase_EachTeamMinTti_MovesToLanding`, `AerialChase_BothTeamsContest_HeaderOccurs`, `AerialChase_LandingOutOfPitch_None`, `AerialChase_ReceiverOrWinner_NoDuplicate`, `AerialChase_KeeperOnlyInOwnBox` | 로직 | — |
| AC-12 | AC-02 측정의 슈팅 4지표 범위 판정 + `ShotAimDesignCoefficients_Unchanged`(071 FR-14 `[설계]` 값 = main) | 로직 | — |

회귀: `--scope auto`(MatchEngine·Presentation 변경 → match + presentation 범위). 070·071·072·058 기존 테스트 중 기대값이 바뀌는 것은 목록 보고 후 갱신.

## 버그 방지
- 착지점이 경기장 밖·골대 뒤: 추격 없음(`AerialChase_LandingOutOfPitch_None`).
- 경합이 판정되거나 취소됨(컨트롤·다른 터치·라인): 다음 틱 `TryLanding`이 false → 추격 즉시 끝(`AerialChase_EndsOnDecideOrCancel`).
- 받는 선수·승자 중복: 추격 집합에서 빼고 인원을 다음 선수로 채우지 않는다(`AerialChase_ReceiverOrWinner_NoDuplicate`).
- 퇴장·교체로 팀 인원 부족: 후보가 2명 미만이어도 정상.
- 다운(070 이동 불가) 선수: 제외(`AerialChase_ImmobileExcluded`).
- 크로스 지점이 크로서 자신과 가까움(박스 모서리 소유자): 지점까지 거리 < 8 m [대체]면 그 지점 후보를 내지 않는다(`CrossTargets_TooClose_Skipped`).
- 같은 받는 선수에 크로스 후보 2개: 1개만 남김 → 후보 상한 24 보호(`CrossTargets_DedupSameReceiver`).
- 수비 장면 교체로 골 장면이 빠짐: 골·Must는 교체 대상이 아니다(`DefensiveRule_NeverRemovesGoalOrMust`).
- 결투 줌에서 공이 화면 밖: 기존 `KeepBallInCone`·`ExpandForBall`이 마지막에 돌아 공이 항상 화면 안(`DuelFraming_BallStaysInView`).
- 보정 루프 발산: 3회 제한 + 회차마다 측정 JSON을 남겨 되돌릴 수 있게.

## 위험 · 미정
- **`[설계]` 계수가 막을 수 있음**: 테이크온 성공률(`act.takeon.*`), 크로스 착지 오차(`act.cross.sigma*`), 공중볼 판정(`act.aerial.*`), 071 FR-14 σ_aim이 `[설계]`다. `[대체]`만으로 범위에 못 들어가면 FR-11대로 멈추고 해당 스펙 개정을 제안한다(특히 유효 슈팅 ≤ 50 %가 가장 위험).
- **패스 성공률 36 → 60 %**: 원인이 한 곳(예: 첫 터치 실패)이면 쉽지만, 오프볼 움직임(010)·피치 컨트롤(007 대체)이 원인이면 계수로는 부족할 수 있다. 1회차 전에 원인 분해로 확인하고, 부족하면 보고한다.
- **측정 시간**: 100경기 × (기준 1 + 루프 최대 3 + 최종 1) = 최대 500경기. 1회 소요를 첫 측정에서 재서 알리고 백그라운드로 돌린다.
- **E2E 장면 탐색**: 071 AC-12는 64시드에서 헤더 0개였다. 규칙 ②가 들어가도 장면이 안 나오면 탐색 시드를 늘리고, 그래도 없으면 보고한다(대체 장면 금지).
- `[미정]` 3개(테이크온 시도 목표, 탈취율 리그 기준, 결투 줌 배율·유지)는 `[제안]`·`[대체]` 시작값으로 진행하고 리포트에 남긴다.
- 스펙 "검증 · 증거"의 E2E 4시나리오는 2026-11-11 규칙대로 `mac-m1-1600p` 1프로필 육안이다.

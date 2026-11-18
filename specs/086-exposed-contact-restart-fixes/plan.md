# 086 — 084 계수 개정으로 드러난 숨은 실패 3건 · 플랜

상태: 승인됨 (2026-11-18 — 정지 속도 0.5 m/s [제안] 채택, 스펙 Q2 헤더 = ⓐ·슬라이딩 = ⓓ)
스펙: ./spec.md

## 접근
플랜 단계 진단(2026-11-18): `LiveContactAuditTests.Report_FixedSeeds`를 082 10경기 + 083 5경기 + 20261349(임시 추가, 진단 뒤 되돌림)로 1회 실행(33 s)해 `.farm/083-audit.json`을 얻었다. 위반은 스펙에 적힌 2건뿐이었다(헤더 5건 중 1건, 이긴 슬라이딩 10행 중 같은 사건 3행 — 장면 28~30). 원인은 셋 다 서로 다르다.

1. **재시작 키커 잡기 (FR-01, Q1 ①)** — 원인: `StandInOnBall.Decide`(재시작)·`DecideGoalDeadBall`(킥오프)는 공이 재배치된 뒤 키커가 지점 `standin.claimRadius`(1.0 m) 안에 들어오면 **속도와 상관없이** 바로 `TakeControl`한다. 그다음 서브스텝에 `PhysicsStage`가 컨트롤 공을 `소유자 위치 + 정면 × BallCarryOffset(0.45 m)`으로 옮긴다. 그래서 지나가던 키커가 잡으면 공이 키커 진행 방향 발 앞으로 튄다(084 T15b ①: 1.52 m).
   - ⓐ 잡기 조건: 지점까지 거리 ≤ `claimRadius`이고 **키커 속도 ≤ `standin.restartTakeMaxSpeed`** 0.5 m/s [제안]일 때만 잡는다. 이 값은 058 FR-08의 "공 멈춤" 기준(`DeadBallFlow.TryRelocateRestartBall`: 속도² < 0.25)과 같다. 스펙의 `[미정]` 정지 속도는 이 값으로 정하자는 제안이다. 반경 안이어도 빠르면 지금처럼 `MoveTo(지점)`을 낸다(도착 감속은 판단 단계 기존 경로).
   - ⓑ 잡은 뒤 공 위치: 데드볼 중 키커(`flow.RestartTaker`)가 가진 공은 `PhysicsStage`가 캐리 위치로 옮기지 않고 **재시작 지점에 둔다**. ⓐ만 있으면 멈춘 키커라도 공이 지점에서 키커 발 앞으로 최대 반경 + 0.45 m 옮겨진다(순간이동, FR-01 위반). 킥은 지금처럼 공 위치(= 지점)에서 나간다. GK가 자기 박스에서 손으로 든 공 규칙(081 T12a)보다 먼저 본다(골킥 공도 지점에 있음).
   - 킥오프(골 데드볼)에도 같은 규칙을 적용한다(058 FR-02 ③, 같은 코드 모양).
2. **실경기 헤더 24 cm (FR-02)** — 원인은 **시뮬**(Q2 ⓐ)이다. 위반 사건은 엔티티 11(원정 GK)의 `header_stand` 걷어내기다. 이때 공 높이가 1.29 m인데, 이 선수 키(193.7 cm)로 서 있는 이마 높이는 1.80 m다. 그래서 리프트 하한(−0.20 m)에 걸린 뒤에도 24 cm가 남았다. `AerialSystem`은 참여자 점수를 매길 때 **착지점**이 자기 박스 안인 GK를 손 처리(`hands`)로 본다. 그러면 머리 최저가 0이 되고 도달 높이는 손 기준이 된다. 그런데 결과를 GK 잡기(`HeaderOutcome.Keeper`)로 보내는 판정은 **접촉 순간 공 위치**가 박스 안인지로 한다(`AerialSystem.cs` 222행). 착지점은 박스 안이고 접촉 지점은 박스 밖이면, GK가 손 기준 높이 범위로 **헤더**를 실행한다. 결정론 리뷰(082 M-1)가 "같은 기준"을 요구했는데 두 곳이 서로 다른 점을 본다.
   - 수정: 참여자마다 필드 기준 도달·최저(`ContactReach(…, false)`·`HeadLow`)도 함께 계산해 둔다. 헤더 접촉 게이트(163행)와 `Execute`는 **222행과 같은 판정**(승자가 GK이고 접촉 순간 공이 자기 박스 안)일 때만 손 기준을 쓰고, 아니면 필드 기준을 쓴다. 손 기준을 쓰는 경우는 222행에서 GK 잡기로 끝나므로, 실제로 실행되는 헤더는 항상 필드 기준이 된다. 경합 점수(071 FR-04 확률)는 그대로 둔다.
   - **T04 뒤 개정 (2026-11-18, 구현 중 확인)**: 위반 GK의 접촉 지점은 실제로 박스 **안**이었다. 222행 GK 잡기 판정은 "접촉 순간"이 아니라 **결정 순간 공중의 공 위치**를 본다(위 분석 정정). 그래서 수정은 더 단순하다 — 222행을 손 처리와 같은 **착지점** 기준으로 바꾼다. 그러면 손 기준 승자 GK는 언제나 잡기로 끝나고, 헤더를 실행하는 GK는 언제나 필드 기준이다. 필드 기준 배열·해시·스냅샷 필드 추가는 필요 없어 뺐다.
   - 동작 쪽(082 리프트 범위·`headerRootMax`)은 바꾸지 않는다. 시뮬이 고쳐지면 공이 이마 높이 −0.2 m 안에 들어온다.
3. **이긴 슬라이딩 60 cm (FR-03)** — 원인은 **화면 타임라인**이다. 스펙 Q2 ⓐ·ⓑ 어느 쪽도 아니라서 ⓓ로 추가를 제안한다(아래 위험). 시뮬은 정상이다. 판정 순간 태클러–공 거리는 0.54 m로 슬라이딩 도달 거리 안이다. 루트 보정 1.15 m도 상한(≈ 3.1 m) 안이고, `planVsEval` 0, 정면 각 차 0°다. 문제는 포즈 쪽 0.60 m(`poseGap`)다. 같은 선수의 `shoulder_duel`(시작 = 접촉 = −0.14 s, 끝 +0.66 s)과 `tackle_slide_r`(시작 −0.14 s, 접촉 0) 두 동작이 접촉 순간 함께 있다. `AnimPlan.BuildActions`는 새 동작 시작을 앞 접촉 시각 뒤로 미룬다(`LastContactBefore`). 그래서 슬라이딩 시작이 어깨 경합 시작과 **정확히 같은 시각**이 된다. 그런데 `ActionTimeline.EndBefore`는 `e.A.Start >= newStart`인 항목을 건너뛰므로 어깨 경합을 자르지 않는다. 평가 때 목록 앞의 어깨 경합이 가중치 1을 먼저 가져가서 슬라이딩 가중치는 0이 된다(`w = min(w, 1 − actW)`).
   - 수정: `EndBefore`가 건너뛰는 조건을 "새 동작보다 **늦게** 시작한 항목"(`e.A.Start > newStart`)으로 바꾼다. 시작이 같아도 접촉이 새 시작 이하인 앞 항목은 054 FR-82대로 새 접촉 전에 빠지게 자른다. 계산하면 끝 = max(앞 접촉 + 1 ms, min(새 시작 + 0.18, 새 접촉)) = 새 접촉이다. 그러면 접촉 순간 앞 동작 가중치가 0이다.
   - 같은 결함이 다른 연속 접촉(트래핑 → 패스 등 시작 = 앞 접촉)에도 있을 수 있다. 진단 표와 054 측정 테스트로 확인한다(버그 방지).

대안: 헤더를 동작으로만 메우는 안(리프트 하한을 −0.5 m로 낮춤)은 082가 버린 안이다(자세가 깨짐). 게다가 원인(GK가 손 기준 높이로 머리를 씀)이 남는다. 슬라이딩에 루트 상한을 더 주는 안(Q2 ⓑ)은 포즈 차이라 효과가 없다. 재시작 Q1 ②(키커를 공에 맞춤)는 사용자 결정으로 버렸다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `Match/Stages/StandIn/StandInOnBall.cs` | 재시작·킥오프 키커 잡기: 속도 ≤ `RestartTakeMaxSpeed`일 때만 `TakeControl` (FR-01 ⓐ) |
| RoadToPro.MatchEngine | `Match/Stages/PhysicsStage.cs` | 데드볼 중 키커가 가진 공은 `RestartSpot`에 둔다 — 캐리·GK 손 규칙보다 먼저 (FR-01 ⓑ) |
| RoadToPro.MatchEngine | `Match/MatchParams.cs`, `MatchTuningIds.cs` | `RestartTakeMaxSpeed` (`standin.restartTakeMaxSpeed`) |
| — (데이터) | `Data/tuning/match.json` | `standin.restartTakeMaxSpeed` 0.5 m/s [제안] (= 058 FR-08 공 멈춤 기준) |
| RoadToPro.MatchEngine | `Actions/AerialSystem.cs` | GK 잡기 판정(`Decide`)을 손 처리와 같은 착지점 기준으로 (FR-02, T04 뒤 개정 — 새 상태 필드 없음) |
| RoadToPro.Presentation | `Players/Logic/ActionClipSelector.cs` (`ActionTimeline.EndBefore`) | 건너뛰기 조건 `Start >= newStart` → `Start > newStart` (FR-03) |
| — (데이터) | `Data/e2e/seeds/083-live-seeds.json` | 20261349 추가 (FR-04, Q3). 시뮬 변경으로 이긴 슬라이딩 시드가 0건이 되면 084 T15a처럼 `SearchSlideWinSeeds`로 재선택 |
| — (데이터) | `Data/perf/086-exposed-contact-restart-fixes.json`, `Verification/Runtime/Bench/SimBench.cs` | IL2CPP 예산(081 사본) · 086 → 081 워크로드 등록 |
| RoadToPro.Verification | `Samples/Highlight/FeelSceneCoverage.cs`·`HighlightScreen.cs` | `e2e/086-contact-reshoot` 장면: 082 점프 헤딩슛·083 이긴 슬라이딩 정지 화면 재사용, 플레이어 빌드 안 시드 재탐색 |
| — (데이터) | `Data/e2e/086-contact-reshoot.json` | E2E 장면 (AC-06) |
| Tests | `EditMode/MatchEngine/Flow/RestartMoveTests.cs`, `EditMode/MatchEngine/Actions/AerialKeeperHandsTests.cs`(신규), `EditMode/Presentation/Players/ActionTimelineTests.cs`(신규 또는 기존 파일에 추가), `EditMode/Presentation/Highlight/LiveContactAuditTests.cs` | 아래 테스트 전략 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 (MatchEngine 안 수정은 UnityEngine 비참조 유지, Presentation 수정은 순수 로직).

## 데이터 · 스키마
- 튜닝 1개 추가: `standin.restartTakeMaxSpeed` (unit m/s, min 0.1, max 3, [제안]).
- `AerialSystem` 상태 필드 변경 없음 (T04 뒤 개정으로 필드 추가 불필요).
- 시드 파일 1개 갱신. 새 스키마 없음.

## 결정론 · 성능
- RNG 소비 순서·횟수 변경 없음: 잡기 조건은 비교만, 공 고정은 위치 대입만, 헤더 게이트는 비교 기준만 바뀐다.
- **경기 결과는 바뀐다**: 재시작 잡기 시각(키커가 멈출 때까지)·헤더 실행 여부가 달라지므로 같은 시드의 이벤트 로그가 달라진다. 고정 해시·고정 수치 기대값이 바뀐 테스트는 목록(이전/새 값·사유)을 남긴다. 084 측정 지표는 `Report_100Matches` 1회로 다시 확인하고 084 AC-01(선방률·전환율)·AC-05(기준 범위 8개)가 유지되는지 본다(스펙 경계: 시뮬 조건 수정 시).
- 할당 없음: 참여자 배열은 기존 `MaxParticipants` 고정 배열에 둘 추가.
- 시뮬 틱 경로를 바꾸므로 IL2CPP 벤치 1회 (AC-07). 비교 기준은 081 예산(결정 틱 ≤ 12 µs, 틱 GC 0 B).
- `determinism-reviewer` 리뷰: StandInOnBall·PhysicsStage·AerialSystem 변경.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `RestartMoveTests` 기존 6종(`ThrowIn_ApproachReadyKick` 포함) + `Restart_KickerPassingBy_TakesOnlyAfterStop_BallStaysAtSpot`(고정 상황: 재배치 직후 키커가 지점 0.8 m 옆을 6 m/s로 지나감 → 잡는 틱의 키커 속도 ≤ 0.5, 잡는 순간·준비 순간 공–지점 ≤ claimRadius + 0.05, 재배치 이후 공 위치 프레임 간 변화 0) | 로직 | — |
| AC-02 | `AerialKeeperHandsTests.KeeperLandingInBox_ContactOutside_HeaderNeedsFieldHeadLow`(착지 박스 안 · 접촉 박스 밖 GK: 공이 필드 머리 최저보다 낮으면 헤더 실행 0) + `LiveContactAuditTests.Headers_WithinDistanceAndTime_JumpOrStand`(20261349 포함 묶음) | 로직 | — |
| AC-03 | `ActionTimelineTests.EndBefore_SameStartAsPrevContact_PrevWeightZeroAtNewContact` + `LiveContactAuditTests.SlideWins_FootWithin5cm_TimeWithinFrame`(이긴 슬라이딩 ≥ 5건) | 로직 | — |
| AC-04 | `farm.py logic --spec 084 --scope auto` (= 086 범위와 같은 `match`) 실패 0 — 086 logic 1회로 함께 판정 | 로직 | — |
| AC-05 | `Determinism_90min_FeelHashSame`·`Determinism_DiagnosticsOnOff_SameHash` (logic 범위 안) + `determinism-reviewer` | 결정론 | — |
| AC-06 | `e2e/086-contact-reshoot` (헤딩 접촉 정지·이긴 슬라이딩 접촉 정지) | E2E | mac-m1-1600p |
| AC-07 | `farm.py bench --spec 086` (결정 틱 ≤ 12 µs, 틱 GC 0 B) | 성능 | mac-m1-1600p |

## 버그 방지
- 키커가 영영 멈추지 않는 경우: 반경 안에서 `MoveTo(지점)`은 도착 감속이 있어 멈춘다. 그래도 대기 상한(058 FR-09)이면 지금처럼 키커가 공을 가진 상태여야 준비된다. 그래서 상한 뒤에도 못 잡으면 `ReselectTakerIfLost` 경로를 그대로 탄다. 테스트: 6종 재시작 모두 준비·킥까지 가는지 확인(기존 `RestartMoveTests`).
- 골킥(GK가 박스 안에서 키커): 공 고정이 GK 손 규칙보다 먼저라 공이 지점에 남는다. 기존 6종 중 골킥 케이스로 확인한다.
- 키커 교체·퇴장: 소유자가 `RestartTaker`가 아니면 고정하지 않는다(기존 캐리 규칙).
- 헤더 게이트: 박스 안 GK는 결과가 잡기라 손 기준 경로가 그대로이고, 박스 밖 GK는 필드 선수와 같아진다. 경계(공이 박스 선 위)는 222행과 같은 `InOwnBox` 하나만 쓰므로 두 판정이 어긋날 수 없다. 단위 테스트에서 박스 안 접촉도 함께 확인한다(잡기 경로 불변).
- `EndBefore` 수정이 다른 연속 동작을 바꿀 수 있음: 054 `MotionMeter` 기반 모션 테스트(Presentation 범위)와 실경기 진단 묶음 전체(헤더·슬라이딩·놓친 경합)로 회귀를 확인한다. 같은 시각에 시작하는 히트(파울·부상)는 `Add`의 히트 경로가 따로 자르므로 영향이 없다.
- 시뮬 변경으로 고정 시드 장면이 사라짐: 083 시드·E2E 장면은 084 T15a·081 방식(탐색)으로 다시 고르고 사유를 남긴다.

## 위험 · 미정
- **스펙 Q2 보충 제안**: 이긴 슬라이딩의 원인이 ⓐ 시뮬 조건도 ⓑ 동작 맞춤 상한도 아닌 **ⓓ 동작 타임라인(연속 접촉 겹침 정리, 054 FR-82)**이다. 플랜 승인 때 스펙 Q2를 "헤더 = ⓐ, 슬라이딩 = ⓓ"로 고친다.
- [제안] `standin.restartTakeMaxSpeed` 0.5 m/s: 스펙 `[미정]` 정지 속도를 이 값으로 정할지 플랜 승인 때 결정.
- 헤더 원인은 정적 분석과 진단 행(엔티티 11 = GK, 공 1.29 m < 필드 머리 최저 1.60 m)으로 확정했다. T01 단위 테스트가 먼저 실패하는지로 한 번 더 확인한다. 실패하지 않으면 멈추고 보고한다.
- 시뮬이 바뀌면 084 지표가 흔들릴 수 있다. 084 AC-01을 벗어나면 멈추고 보고한다(084 계수는 이 스펙 제외 항목).

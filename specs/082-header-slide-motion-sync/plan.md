# 082 — 헤더 점프 · 슬라이딩 태클 동작 연결 · 플랜

상태: 완료 (2026-11-17)
스펙: ./spec.md

## 접근
조사로 확인한 원인 두 가지를 고친다.
1. **헤더 높이 (FR-01~03)**: 071 `AerialSystem`은 내려오는 공이 `min(reach, contactZ)` 아래로 들어오는 첫 서브스텝에 헤더를 실행한다. `reach = 키×1.33 + 점프`는 **팔 든 높이**(키 1.80 m·점프 0.5 m면 2.89 m)인데, `header_jump` 클립의 머리 접촉점은 2.11 m(키 비율 보정 전)라 접촉 순간 공이 머리 위 약 0.8 m에 있다. 또 화면 공 보정(`BallVisualCorrection.Offset`)은 접촉 시각 **전**까지만 당기고 접촉 프레임에서 0으로 돌아가므로, 054 측정(접촉 1 ms 전 화면 공 기준)은 통과하는데 실제 화면(접촉 프레임 정지)에서는 어긋난다.
   - 시뮬: 헤더 접촉 게이트만 **머리 도달 높이**(`키×headK + 점프`, headK 0.93 [제안])로 바꾼다. 경합 점수의 reach(071 FR-04 확률)는 그대로 둔다 — 스펙 제외 항목(승패 확률 변경 없음).
   - 동작: 필요한 점프(접촉 높이 − 서 있을 때 이마 높이) ≥ 0.10 m면 `header_jump`, 아니면 `header_stand`. 남는 높이 차는 헤더 전용 **수직 리프트**(점프 높이 보정, 접촉 시각에 정점인 엔벨로프)로 메운다. 054 루트 오차 상한(10 cm)은 수평에만 적용하던 그대로 두고, 리프트는 별도 범위 [−0.20, +0.35] m [제안].
   - 화면 공 보정은 접촉 프레임까지 포함(접촉 시각에서 연속)하도록 고쳐 측정과 화면을 같게 한다(FR-05·AC-04).
2. **슬라이딩 (FR-04)**: 실경기 경로는 `TackleSliding` 비트로 슬라이딩 클립을 고르지만(070), 081 화면에서 안 보였다. 코드만으로 원인이 하나로 좁혀지지 않아 **진단을 먼저 만들고 고정 시드로 재현**한 뒤 고친다. 후보: ① `TackleGate` 거리(2 m)가 늦은 태클에서 하이라이트 위치 기준으로 넘음 ② 같은 엔티티의 다음 이벤트가 `EndBefore`로 클립을 자름 ③ 화면 정지 시각과 클립 구간 불일치. 다운 유지는 별도 결함: `tackle_slide_r`는 36프레임(1.2 s, 접촉 0.4 s)이라 접촉 뒤 0.8 s에 끝나지만 070 다운은 접촉 뒤 1.2 s(`act.tackle.slideDown`) — 마지막 다운 포즈를 다운 끝까지 유지한다.

**T03 진단 뒤 추가 (2026-11-17 사용자 결정 "추천")**: 헤더 머리–공 91~154 cm의 주원인은 수평 거리(071 접촉 게이트가 수평 2 m 안이면 실행, 동작 루트 맞춤은 10 cm). 시뮬 헤더 실행 수평 거리 ≤ `act.aerial.headRadius` 1.0 m [제안], 동작은 헤더만 루트 맞춤 상한 `highlight.headerRootMax` 1.0 m [제안] (T10: 0.6 m에서 헤더 21.4 → 7.5/경기 → 1.0 m로, 사용자 결정). 슬라이딩 원인은 ① `TackleGate` 대체 ④ 이긴 슬라이딩의 결과별 후보(`tackle_win_r`)가 슬라이딩 비트보다 먼저 — 실경기 슬라이딩은 게이트 제외, 슬라이딩 비트면 결과와 상관없이 `tackle_slide_r`.

대안: 시뮬은 그대로 두고 동작만 늘리는 안(Q1 ②)은 0.8 m 리프트가 필요해 자세가 깨진다 — 스펙 Q1 ③ 결정대로 둘 다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `Actions/AerialModel.cs` | `HeadReach(p, heightCm, jumpH)` 추가 (`키×AerialHeadK/100 + jumpH`) |
| RoadToPro.MatchEngine | `Actions/AerialSystem.cs` | 참여자별 `_headReachOf[]`, 승자 `_headReach`; 접촉 게이트·`ContactPoint` 높이를 GK 손 처리 외에는 `_headReach` 기준으로. 헤더 Detail에 접촉 높이 기록. 해시·스냅샷에 `_headReach` 추가 |
| RoadToPro.MatchEngine | `Actions/ActionParams.cs`, `ActionTuningIds.cs` | `AerialHeadK` (`act.aerial.headK`) |
| — (데이터) | `Data/tuning/action.json` | `act.aerial.headK` 0.93 [제안] (082 FR-03, 2026-11-17 사용자 결정) |
| — (데이터) | `Data/tuning/highlight.json` | `highlight.headerJumpMin` 0.10 m, `highlight.headerLiftMin` −0.20, `highlight.headerLiftMax` 0.35 [제안] |
| RoadToPro.Presentation | `Players/Logic/HeaderMotion.cs` (신규, 순수 로직) | 헤더 클립 선택(점프/서서), 필요 점프·리프트 계산, 리프트 엔벨로프 |
| RoadToPro.Presentation | `Players/Logic/AnimPlan.cs` | 헤더 이벤트(부위 Head인 Shot·Pass·Clearance)에서 `HeaderMotion`으로 후보를 좁히고 리프트를 타임라인에 기록; 슬라이딩 다운 유지; 진단 결과에 따른 슬라이딩 수정 |
| RoadToPro.Presentation | `Players/Logic/Motion/*` (`ActionChoice`·평가) | 수직 리프트 값을 루트 Y에 더함 (기존 RootOffset과 별개 필드) |
| RoadToPro.Presentation | `Players/Logic/AnimPlan.cs` 내 `BallVisualCorrection` | 접촉 시각 포함 연속 보정(t == contactAt에서 최대, 뒤로 감쇠) |
| RoadToPro.Presentation | `Highlight/Logic/ClipConverter.cs` | 슬라이딩 다운 시간(`act.tackle.slideDown`)을 HighlightData로 전달 (튜닝값, 시뮬 이벤트 변경 없음) |
| RoadToPro.Verification | `Samples/Highlight/LiveContactAudit.cs` (신규) | 실경기 시드 → 하이라이트 장면 → AnimPlan → `MotionMeter.MeasureContacts`로 헤더·슬라이딩 사건별 부위–공 거리·시각 차·클립·제외 사유 표 (FR-05). JSON 출력 → report.html 표 |
| RoadToPro.Verification | `Samples/Highlight/HighlightScreen.cs`, `FeelSceneCoverage.cs` | `livePlay082` 화면, 헤딩슛(점프)·늦은 슬라이딩 시드 탐색 재사용 |
| — (데이터) | `Data/e2e/082-header-slide.json`, `Data/e2e/082-live-seeds.json` | E2E 장면 / 진단 고정 시드 10경기 |
| Tests | `EditMode/MatchEngine/Actions/AerialHeadReachTests.cs`, `EditMode/Presentation/Players/HeaderMotionTests.cs`, `SlideDownTests.cs`, `EditMode/Presentation/Highlight/LiveContactAuditTests.cs` | 아래 테스트 전략 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 (Presentation → MatchEngine 참조는 기존과 같음, MatchEngine은 UnityEngine 비참조 유지).

## 데이터 · 스키마
- 튜닝 3+1개 추가(위 표). 세이브 스키마 변경 없음.
- `AerialSystem` 스냅샷에 `_headReach` 1필드 추가 — 경기 중 저장이 없으므로(경기 전·후만) 세이브 호환 영향 없음, 스냅샷 왕복 테스트만 갱신.
- 진단 출력 `evidence/live-contact-audit.json`: `[{seed, scene, seq, kind, entity, clip, simPart, distance, distanceRaw, timeError, lift, excluded, reason}]`.

## 결정론 · 성능
- 시뮬 변경은 비교 기준 하나(헤더 게이트 높이)뿐, RNG 소비 순서·횟수는 같다(헤더 실행 서브스텝이 늦어질 뿐). `_headReachOf` 배열은 기존 `_reachOf`와 같이 생성 시 할당, 틱 경로 할당 0.
- 헤더 접촉 시각·위치가 바뀌므로 고정 해시 기대값이 바뀐다 → 바뀐 테스트 목록과 사유를 tasks 결과 줄·report에 남긴다.
- AC-06 불변식: 모든 헤더에서 `접촉 높이 ≤ headReach + 1 서브스텝 낙하량` (진단 카운터로 검사).
- 성능: 010 결정 틱 IL2CPP 벤치(≤ 12 µs, 081 예산) — 틱 경로를 바꿨으므로 스펙 끝 1회. 하이라이트 쪽은 클립 선택·엔벨로프 상수 시간이라 058 Performance 테스트 범위 안에서 확인.
- `determinism-reviewer` 리뷰 (MatchEngine 변경).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `LiveContactAuditTests.Headers_WithinDistanceAndTime_JumpOrStand` (고정 시드 10경기 하이라이트 헤더 전부 ≤ 5 cm·±1프레임·점프 기준) + `HeaderMotionTests.*` (경계 0.10 m 양쪽, 리프트 범위) | 로직 | — |
| AC-02 | `LiveContactAuditTests.SlidingTackles_SlideClip_ContactTime_StaysDown` + `SlideDownTests.HoldsDownPose_UntilSlideDownEnds` | 로직 | — |
| AC-03 | `HeaderMotionTests.EdgeCases` (헤더 없이 끝난 경합 → 헤딩 동작 없음, 극단 키 150·205 cm) · `SlideDownTests.DeadBallAfter_DownPlaysToEnd` · `LiveContactAuditTests.SceneBoundary_ExcludedWithReason` · GK 손 처리 → `AerialHeadReachTests.KeeperHands_UsesArmReach` · NaN·예외 0 | 로직 | — |
| AC-04 | `LiveContactAuditTests.MatchesMotionMeter_Within1cm` (합성 054 헤더 장면을 진단 경로로 재서 `ContactSyncTests` 값과 비교) + `BallVisualCorrectionTests.ContinuousAtContact` | 로직 | — |
| AC-05 | `e2e/082-header-slide` (헤딩슛 점프 접촉 정지·늦은 슬라이딩 정지) | E2E | mac-m1-1600p |
| AC-06 | `AerialHeadReachTests.ContactHeight_NeverAboveHeadReach` (90분 다시드 불변식) · 기존 `Determinism` 카테고리(같은 시드 2회·진단 켬/끔) | 결정론 | — |
| AC-07 | `farm.py bench --spec 082` (010 결정 틱) · 058 하이라이트 Performance 테스트 | 성능 | mac-m1-1600p |

## 버그 방지
- 헤더 게이트를 낮추면 승자가 공 밑에 못 오는 경우 `FallbackZ`로 떨어지는 Missed가 늘 수 있다 → 081 측정 도구로 경기당 헤더 수·Missed 비율 전후 비교(급감 시 멈추고 보고). 경기당 득점(2.5~3.0)도 같이 기록.
- 공이 서 있는 이마 높이보다 0.20 m 이상 낮게 들어오는 헤더(다이빙 헤딩 영역) → 리프트 하한을 넘음. 진단에서 위반으로 잡히면 멈추고 071·043 개정 제안 (새 클립 제작은 범위 밖).
- 화면 공 보정을 접촉 프레임까지 늘리면 접촉 직후 공이 튀는 불연속이 생길 수 있다 → `BallVisualGuardTests`(기존) + 새 연속성 테스트.
- 다운 유지 중 다음 이벤트(재태클 등)가 같은 엔티티에 오면 → 기존 `EndBefore` 규칙 우선, 테스트로 고정.
- 키 비율: 머리 높이는 `ScaleOf(e)`로 비례 — 극단 키 테스트.

## 위험 · 미정
- 슬라이딩 미표시 원인이 위 후보 ①~③ 밖이면(예: 하이라이트 장면 선택이 사건을 자름) 진단 결과를 보고 멈추고 태스크 추가를 제안한다.
- headK 0.93·리프트 범위·점프 경계 0.10 m는 [제안] — 진단 위반 0건이 나오는 범위에서 튜닝 파일로 조정.
- 헤더 실행이 늦어지면서 헤딩슛 xG·득점 분포가 바뀔 수 있다 — 081 범위(평균 득점) 밖으로 나가면 보고.
- 엔진이 바뀌므로 081 E2E 시드(20261247·20261220)는 다시 고른다(`FeelSceneCoverage`).

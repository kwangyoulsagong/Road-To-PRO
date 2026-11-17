# 083 — 놓친 공중볼 점프 · 이긴 슬라이딩 접촉 · 플랜

상태: 승인됨 (2026-11-17)
스펙: ./spec.md

## 접근
조사로 확인한 현재 상태와 고칠 곳.

1. **놓친 경합 점프 (FR-01·02)**: 071은 결정된 경합이 헤더 없이 끝나면 `Header` 이벤트(A = `Missed`, B = 경합 기록 첫 번호 + 1, 주체 = 승자, 상대 = 가장 가까운 상대 참여자)를 **경합이 끝난 시각**에 남긴다. 하이라이트는 이 이벤트를 받지만 043 맵에서 `Header`는 기록뿐이라 동작이 없다. 도약·정점 시각·점프 높이는 `AerialLog`(071 FR-06)에만 있고 `ReplayClip`(프레임 + 이벤트)에는 없다.
   - 시뮬: `MatchSim.ExtractClip`이 구간 안 `Header` 이벤트가 가리키는 경합 기록 묶음을 **읽기만 해서** `ReplayClip`에 붙인다(본 실행의 `AerialLog`에서 복사, `IsAvailable` 아니면 생략). 시뮬 상태·이벤트·해시는 그대로(AC-06).
   - 변환: `ClipConverter`가 `Missed` 결과 `Header` 이벤트마다 승자·가장 가까운 상대 참여자의 기록을 찾아 `HighlightData.AerialJumps`(엔티티, 도약·정점·착지 재생 시각, 점프 높이)로 옮긴다. GK이고 자기 박스 안이면 넣지 않는다(경계 · 예외).
   - 동작: `AnimPlan`이 이벤트 동작을 다 만든 **뒤** 점프를 넣는다 — `header_jump`를 접촉 시각 = 정점에 맞추고(시각 맞춤은 `AddHeaderLoser`와 같은 방식), 루트 보정 0(공에 맞추지 않음, 054 FR-36), 리프트 = 기록 점프 높이 − 클립 점프 높이를 082 `JumpLift` 범위로 자름(스펙 Q 정리). 같은 선수의 다른 동작이 정점 전에 시작하면(점프 구간과 겹치면) 넣지 않고(FR-02), 정점 뒤 다음 동작이 있으면 그 시작에서 자른다(054 FR-82). 예상 머리 위치가 공과 접촉 허용치 안이면 리프트를 하한까지 낮추고, 그래도 닿으면 진단 위반으로 남긴다(멈추고 보고).
   - 이벤트 처리 중에 넣지 않는 이유: `Missed` 이벤트는 정점보다 늦게 오므로, 그사이 같은 선수의 수신 등 동작이 이미 타임라인에 있어야 FR-02 판정이 된다.
2. **이긴 슬라이딩 접촉 (FR-03)** — **T02 뒤 개정 (2026-11-17 사용자 결정 Q1 ③)**: 기준 측정에서 판정 순간 시뮬 태클러–공 수평 거리가 1.74~8.65 m(이긴 9건). 070 `DuelSystem.OnTackles`는 도달 거리를 태클러–캐리어 거리로만 보고, 드리블 터치로 공이 캐리어 앞 수 m여도(공 Controlled) 이김을 준다. ⓐ 시뮬: 슬라이딩이고 캐리어가 공을 가진 판정에서 태클러–공 수평 거리 > 슬라이딩 도달 거리면 `Resolve` 뒤 결과를 `Miss`로(늦은 태클·무효와 같은 방식 — RNG 소비 순서·횟수 그대로). 서서 하는 태클은 이 스펙 밖. ⓑ 동작은 아래 그대로.
   - (원래 분석) 082에서 실경기 슬라이딩은 `tackle_slide_r`로 나오지만 루트 맞춤 상한이 054 FR-02 10 cm(`MotionDefaults.RootContactMax`)라, 시뮬 슬라이딩 도달 거리(최대 `act.tackle.reach` 1.2 + `slideExtra` 1.0 = 2.2 m)와 클립 접촉점(수평 0.88 m)의 차이가 그대로 남아 발–공 1.7~5.3 m가 된다. 스펙 Q1 ① 결정대로 **동작만**: 이긴 실경기 슬라이딩에 한해 상한 = `highlight.slideRootMax`(2.2 m [제안], 시뮬 도달 거리와 같은 값) + 클립 접촉점 수평 거리 × 키 배율. 합친 루트 보정 상한(`RootCorrection.Clamp`)도 그 액션 동안 같은 값(082 헤더 `RootMax` 경로 재사용). ⓐ 뒤에는 태클러–공 ≤ 2.2 m라 상한 안에 든다.
   - 진단에서 5.3 m가 상한(2.2 + 0.88 ≈ 3.1 m)을 넘는 사건이 남으면 원인(장면 위치 보간·`EndBefore` 등)을 진단 표로 확인하고 멈춰 보고한다 — 상한을 임의로 늘리지 않는다.
3. **진단 (FR-04)**: 082 `LiveContactAudit`에 사건 종류 둘 추가 — `MissedJump`(점프가 있으면 정점 시각 오차, 머리–공 거리 > 허용치, 없으면 사유: FR-02 겹침·GK 박스·기록 없음·장면 경계), `SlideWin`(발–공 ≤ 5 cm, 시각 차 ≤ ±1 기록 프레임). 고정 시드는 082와 같은 `Data/e2e/seeds/082-live-seeds.json` 10경기 + `Data/e2e/seeds/083-live-seeds.json`(이긴 슬라이딩이 하이라이트 장면에 있는 시드, 합계 ≥ 5건 — T01a 탐색, 2026-11-17 사용자 결정).

대안: 놓친 경합 정점을 화면 쪽에서 공 궤적으로 추정하는 안은 071 기록(참여자별 정점·점프 높이)과 어긋날 수 있어 버렸다. `Missed` 이벤트에 정점 정보를 더 싣는 안은 이벤트 해시가 바뀌어 AC-06(082 대비 시뮬 해시 불변)을 깬다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `Actions/DuelSystem.cs` | `OnTackles`: 슬라이딩 이김은 태클러–공 수평 ≤ `TackleModel.Reach(P, true)`일 때만(밖이면 `Miss`), 진단 누계 `SlideFarMiss`(해시 밖) |
| — (데이터) | `Data/perf/083-aerial-slide-motion-followup.json`, `Verification/Runtime/Bench/SimBench.cs` | IL2CPP 예산(081 사본) · 083 → 081 워크로드 |
| RoadToPro.MatchEngine | `Match/Replay/StateCheckpoints.cs` | `ReplayClip.Aerials`(`AerialDetail[]`, 기본 빈 배열) + 선택 생성자 인자. 기존 호출부(BroadcastScenes 등)는 그대로 |
| RoadToPro.MatchEngine | `Match/MatchSim.cs` | `ExtractClip`: 구간 이벤트 중 `Header`(B > 0)의 묶음 기록을 본 실행 `AerialLog`에서 복사해 `ReplayClip`에 붙임. 읽기 전용 |
| RoadToPro.Presentation | `Players/Logic/HighlightData.cs` | `AerialJump` 값 타입 + `AerialJumps` 목록 |
| RoadToPro.Presentation | `Highlight/Logic/ClipConverter.cs` | `Missed` `Header` 이벤트 → 승자·가장 가까운 상대 기록을 재생 시각으로 바꿔 `AerialJumps`에. GK 자기 박스 제외 |
| RoadToPro.Presentation | `Players/Logic/MissedJumpMotion.cs` (신규, 순수 로직) | 점프 액션 계산: 정점 시각 맞춤, 리프트(082 `JumpLift` 범위), FR-02 겹침 판정, 머리–공 여유 검사 |
| RoadToPro.Presentation | `Players/Logic/SlideMotion.cs` (신규, 순수 로직) | `RootMax` 2.2 m 상수 + `RootCap(clipContact, scale)` |
| RoadToPro.Presentation | `Players/Logic/AnimPlan.cs` | 이긴 실경기 슬라이딩의 루트 상한을 `SlideMotion.RootCap`으로(`WithHeader(0, cap)`과 같은 `RootMax` 경로 — 이름은 `WithRootCap`으로 일반화). `BuildActions` 끝에 놓친 경합 점프 단계 추가 |
| RoadToPro.Presentation | `Players/Logic/ActionClipSelector.cs` | `ActionChoice.WithRootCap(lift, cap)` (기존 `WithHeader` 대체 이름, 동작 같음) |
| — (데이터) | `Data/tuning/highlight.json` | `highlight.slideRootMax` 2.2 m [제안] (= `act.tackle.reach` + `act.tackle.slideExtra`, 테스트가 같음을 확인) |
| Tests | `EditMode/Presentation/Highlight/LiveContactAudit.cs` | `Kind.MissedJump`·`Kind.SlideWin` 측정 추가 |
| RoadToPro.Verification | `Samples/Highlight/HighlightScreen.cs`, `FeelSceneCoverage.cs` | `livePlay083` 화면, `live.freeze083.missed`·`live.freeze083.slide` 정지(놓친 경합 정점·이긴 슬라이딩 접촉), 플레이어 빌드 안 시드 탐색 |
| — (데이터) | `Data/e2e/083-aerial-slide.json` | E2E 장면 (FR-05) |
| Tests | `EditMode/MatchEngine/Match/ReplayAerialTests.cs`, `EditMode/Presentation/Players/MissedJumpMotionTests.cs`, `SlideRootCapTests.cs`, `EditMode/Presentation/Highlight/LiveContactAuditTests.cs` | 아래 테스트 전략 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 (Presentation → MatchEngine 기존 참조, MatchEngine은 UnityEngine 비참조 유지, `AerialDetail`은 MatchEngine 타입이라 ReplayClip에 둬도 방향 그대로).

## 데이터 · 스키마
- `AerialJump { int Entity; float JumpAt, ApexAt, LandAt; float JumpHeight; long Seq; }` — 재생 시각은 기록 서브스텝 × 0.01 s(시뮬 초, `HighlightData` 시각 축과 같음).
- 튜닝 1개 추가(`highlight.slideRootMax`). 세이브 스키마 변경 없음 — `ReplayClip`은 저장하지 않는다(경기 전·후 저장, 하이라이트는 재구성).
- 진단 출력 `evidence/live-contact-audit.json`에 `kind = MissedJump | SlideWin` 행 추가(필드는 082와 같고 `apexError` 추가).

## 결정론 · 성능
- **시뮬 틱 경로 변경 (FR-03 ⓐ, Q1 ③)**: `OnTackles` 비교 1개 추가(공 위치·태클러 위치는 이미 읽음, 할당 0). RNG 소비 그대로, 결과만 바뀌어 경기 해시·고정 해시 기대값이 바뀐다 → 바뀐 테스트 목록·사유를 결과 줄에. 010 결정 틱 IL2CPP 벤치 1회(≤ 12 µs). 081 측정 도구 100경기로 태클·슬라이딩 이김률·득점 전후 비교, 081 범위 밖이면 멈추고 보고.
- 하이라이트 쪽: `ExtractClip`은 재구성 뒤 본 실행 기록을 읽어 복사만 한다. 해시·RNG 소비 그대로 → 추출 전후 해시 같음(AC-06).
- `determinism-reviewer` 리뷰(`DuelSystem` RNG 소비 순서 대칭, `ExtractClip` 읽기 전용: `IsAvailable` 밖 접근, 복사 순서).
- 하이라이트: 점프 단계는 장면당 놓친 경합 수 × 2명, 상수 시간. `AnimPlan` 생성(로드 시) 경로라 매 프레임 할당 없음. 058 Performance 테스트로 장면 1개 동작 계획 예산 확인, 프레임은 개발 맥 기록.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `MissedJumpMotionTests.ApexAligned_WithinOneFrame_HeadClearOfBall` (합성: 정점 시각 ±1 기록 프레임, 머리–공 > 허용치, 리프트 범위) + `LiveContactAuditTests.MissedAerials_WinnerAndNearestOppJump_NoTouch` (고정 시드 10경기) + `ReplayAerialTests.ExtractClip_CarriesGroupRecords` | 로직 | — |
| AC-02 | `MissedJumpMotionTests.OtherActionBeforeApex_NoJump` · `ActionAfterApex_JumpCutAtStart` | 로직 | — |
| AC-03 | `SlideTackleBallReachTests.WonSlide_BallWithinReach`(다시드 90분 불변식: 이긴 슬라이딩 태클러–공 ≤ 2.2 m) · `FarBall_Miss_RngSame` + `LiveContactAuditTests.SlideWins_FootWithin5cm_TimeWithinFrame` (고정 시드 10경기, WinPoke·WinKeep 모두) + `SlideRootCapTests.Cap_EqualsReachPlusClipContact` · `TuningMatchesAction` | 로직 | — |
| AC-04 | `MissedJumpMotionTests.EdgeCases` (GK 자기 박스 → 없음, 기록 없음·`IsAvailable` 밖 → 없음, 정점이 장면 밖 → 진단 제외 사유, 극단 키 150·205 cm) · `SlideRootCapTests.NextActionAfterWin_RampsFromContact` (082 T07b 규칙) · NaN·예외 0 | 로직 | — |
| AC-05 | `e2e/083-aerial-slide` (놓친 경합 정점 정지 · 이긴 슬라이딩 접촉 정지) | E2E | mac-m1-1600p |
| AC-06 | `ReplayAerialTests.ExtractClip_DoesNotChangeHash` (추출 전후 해시 같음) · 기존 `Determinism` 카테고리(같은 시드 2회·진단 켬/끔). 고정 해시 기대값 변경은 목록·사유 | 결정론 | — |
| AC-07 | `farm.py bench --spec 083`(010 결정 틱 ≤ 12 µs) · 058 하이라이트 Performance 테스트(동작 계획) + 하이라이트 프레임 개발 맥 기록 | 성능 | mac-m1-1600p |

## 버그 방지
- **루트 보정 속도**: 이긴 슬라이딩 보정이 최대 ≈ 3 m라 접근 구간에 몰리면 화면 속도가 튄다(054 FR-03 프레임당 속도 변화, 058 FR-12 속도 점프 경고). 기존 `RootCorrection.Ramp`(접근 창 + `MinRamp`)를 그대로 쓰고, 진단에 슬라이딩 접근 구간 최대 화면 속도 변화를 같이 기록한다. 기준을 넘으면 멈추고 보고(램프 연장은 스펙 밖 결정).
- `B`가 가리키는 기록이 넘침(`AerialLog.Overflow`)·창 밖이면 → 점프 없음, 진단 사유 "기록 없음".
- 놓친 경합 상대가 퇴장·교체로 빠짐(071 `_nearestOpp = -1`) → 승자만.
- 승자 = 다른 동작 중(예: 경합 뒤 바로 수신) → FR-02로 빠짐, 진단 사유로 확인.
- 이긴 슬라이딩 직후 같은 선수 다음 동작 → 082 T07b(`Ramp` from = 앞 접촉) 그대로, 테스트로 고정.
- 점프 착지 전 장면 끝 → 클립이 장면 끝에서 잘릴 뿐, 정점이 장면 안이면 측정.

## 위험 · 미정
- `highlight.slideRootMax` 2.2 m는 [제안] — 시뮬 도달 거리와 같은 값. 진단 위반이 남으면 원인을 보고 멈춘다(스펙 Q1 ② 시뮬 개정은 스펙 변경 사항).
- 놓친 경합에서 승자가 공 바로 밑에 있었던 경우(공이 리치 위로 지나감) 머리–공이 허용치 안에 들 수 있다 — 리프트 하한으로 낮춰도 닿으면 진단 위반으로 보고하고 처리(점프 생략 vs 다른 규칙)를 묻는다.
- 놓친 경합이 실경기 하이라이트 장면에 드물면 E2E 시드 탐색이 길어질 수 있다 — `FeelSceneCoverage` 탐색 범위를 082처럼 플레이어 빌드 안에서 넓힌다.

# 054 — 3D 선수 로코모션 · 공 접촉 동기화 · 플랜

상태: 승인됨 (2026-11-06, 추가 [제안] 수치 포함)
스펙: ./spec.md

## 접근
043의 원칙 **포즈 = f(재생 시각)** 을 그대로 지킨다. 재생 중에 상태를 쌓지 않고, 장면을 불러올 때 `AnimPlan.Build`가 궤적·이벤트를 읽어 전환 동작·발 디딤·기울기·겹침 보정을 미리 계산해 둔다. 그래서 배속·렌더 fps가 달라도 같은 시뮬 시각이면 포즈가 같고(AC-13·15), 재생 중 할당이 없다.

1. **전환 동작**: 30 Hz 궤적에서 출발·감속·컷·턴을 찾아 클립을 끼운다. 공 접촉 액션이 항상 우선한다.
2. **발 고정**: 접지 구간마다 발 디딤 위치를 계획해 두고, 2본 IK로 발을 그 자리에 붙인다(055 플랜이 054로 넘긴 일). 기존 `FootGrounding`의 높이 보정 뒤에 돈다.
3. **자세 오버레이**: 횡가속 기울기(FR-22)와 가감속 숙임·젖힘(FR-20·21)을 프레임별로 구워 골반·척추 회전에 더한다.
4. **접촉 키**: 공 보간에 접촉 시각을 키로 넣어 30 Hz 보간이 접촉을 지나치지 않게 한다(FR-52).
5. **겹침 보정**: 선수 쌍의 몸통 거리와 경합 결과를 보고 화면 루트 오프셋을 나눈다. 시뮬 값은 읽기만 한다.
6. **측정**: 실제 본 위치로 지표를 재는 측정기를 EditMode(수정 중 빠른 확인)와 E2E(최종 판정)가 함께 쓴다.

**고려한 대안**: 재생 중 상태를 쌓는 발 고정은 구현이 쉽지만 배속·fps에 따라 포즈가 달라져 AC-13·15를 어긴다. Unity Humanoid Foot IK는 그래프 평가 비용이 크고 043에서 이미 껐다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| Art/Blender | `Art/Blender/scripts/player/clips/football.py`, `bake.py`, `checks.py` | 컷 디딤 클립 `cut_l45`·`cut_r45`·`cut_l90`·`cut_r90` 추가. 로코모션 클립 태그 `footPath`(접지 시작·끝 발목 로컬 XZ), 액션 클립 태그 `stance`·`plant` 추가 |
| RoadToPro.Presentation | `Runtime/Players/Logic/ClipLibrary.cs` (수정) | 새 태그 읽기 |
| | `Runtime/Players/Logic/Motion/MotionStandards.cs` | 지표 기준값 읽기 (FR-72) |
| | `Runtime/Players/Logic/Motion/MotionTrack.cs` | 엔티티별 30 Hz 속도·가속·횡가속·방향·진행 방향 파생값, 시뮬 한계 초과 분류 (FR-80) |
| | `Runtime/Players/Logic/Motion/TransitionPlanner.cs` | 출발·감속·컷·턴·피벗 탐지와 클립 선택 (FR-13·20~24) |
| | `Runtime/Players/Logic/Motion/YawTrack.cs` | 골반 yaw 각속도 상한 1,200°/s (FR-05·25) |
| | `Runtime/Players/Logic/Motion/PostureTrack.cs` | 기울기·숙임 프레임 값 (FR-20~22) |
| | `Runtime/Players/Logic/Motion/FootPlantPlan.cs` | 접지 구간·디딤 위치, 이진 탐색 조회 (FR-10·11·23·33) |
| | `Runtime/Players/Logic/Motion/ContactKeys.cs` | 공 보간 접촉 키 (FR-35·52) |
| | `Runtime/Players/Logic/Motion/InteractionPlanner.cs` | 몸통 겹침·경합 패자·태클 순서·골대 관통 보정 (FR-40~45·83·85·86) |
| | `Runtime/Players/Logic/AnimPlan.cs`, `HighlightData.cs`, `ActionClipSelector.cs` (수정) | 위 계획 연결, 공 소유자 기록(드리블 런 FR-37), 연속 접촉(FR-82), 급한 원터치 표시(FR-81), 루트 보정 변화 상한(FR-03) |
| | `Runtime/Players/FootLock.cs`, `PostureApplier.cs`, `PlayerAnimationSystem.cs` (수정) | 본에 IK·자세 적용, 마커 `Players.Motion` |
| | `Runtime/Players/Motion/BodyCapsules.cs`, `MotionMeter.cs`, `IMotionDiagnostics.cs` | 부위 캡슐 8개와 지표 측정 (FR-70·71). 진단이 꺼지면 호출 0 |
| RoadToPro.Presentation (데이터) | `Players/Data/054-motion-standards.json` | 기준값과 이전 기준 (FR-72) |
| RoadToPro.Verification | `Runtime/Samples/Motion/MotionScenes.cs` | 증거 장면 8종·경계 장면 (005·004로 생성, 100 Hz 원본 보존) |
| | `Runtime/Samples/Motion/MotionScreen.cs`, `Steps/MotionSteps.cs` | E2E 화면과 측정 단계(`motionMeasure`, 위반 프레임 자동 캡처) |
| | `Runtime/Bench/AnimBench.cs`, `SimBench.cs` (수정) | 054 IL2CPP 벤치 |
| Tools/devicefarm | `farm/motion.py`, `farm/report.py` (수정), `tests/test_motion.py` | 지표 판정, 시계열 SVG, 리포트 부착 (AC-16) |
| 데이터 | `Data/e2e/054-motion-metrics.json`, `054-motion-set.json`, `054-speed-compare.json`, `054-motion-perf.json`, `Data/perf/054-locomotion-ball-sync.json` | E2E·성능 정의 |
| RoadToPro.Tests.EditMode | `Tests/EditMode/Presentation/Players/Motion/*Tests.cs` | 로직·측정 테스트 |

의존 방향 확인: Presentation → MatchEngine·Core 읽기 전용, Verification → Presentation. `.claude/rules/10-csharp.md` 위반 없음. 코어 모듈은 고치지 않는다.

## 데이터 · 스키마
- `player.clips.json` (schemaVersion 1 유지, 필드 추가만)
  - `footPath`: `{"Left":[x0,z0,x1,z1],"Right":[...]}` — 로코모션 접지 시작·끝의 발목 로컬 위치
  - 액션 클립 `stance`: 접지 구간(정규화). `plant`: 디딤발 이름과 접촉 시점 공 기준 옆·뒤 거리(m)
- `HighlightData.BallOwner`: `short[Frames]`, 기본 −1. `Hash()`에 포함.
- `PoseSample`에 `Lean`·`Pitch`(라디안) 추가. 기존 필드와 슬롯 7개는 그대로.
- `054-motion-standards.json`: `{version, thresholds:{id:{max|min,unit,fr}}, previous:{...}}`
- 세이브 변경 없음(재생 전용).
- 부위 캡슐([미정] 해소 제안): 몸통 1·머리 1·팔 2·허벅지 2·정강이 2 = **8개**. 팔은 어깨~손목을 하나로 묶는다.

## 탐지 규칙 (전부 스펙 수치, 기준값은 데이터)
| 동작 | 조건 | 클립 |
|---|---|---|
| 출발 | 속도 ≤ 0.3 m/s → 가속 ≥ 2.78 m/s² | `start_f` (`speedCurve`로 시간 맞춤) |
| 감속 | 감속 ≥ 3 m/s² | 정지까지 가면 `stop_f`·`decel_stop`, 감속이 더 크면 `quick_stop` |
| 컷 | 0.35 s [제안] 안에 진행 방향 ≥ 45°, 속도 ≥ 2 m/s | `cut_l45`·`cut_r45`·`cut_l90`·`cut_r90` |
| 180° 턴 | 진행 방향 ≥ 135° [제안] | `turn_180` |
| 저속 피벗·스텝 턴 | 속도 < 2 m/s, yaw 변화 ≥ 45° | `turn_l90`·`turn_r90` |
| 백페달·사이드스텝 | 몸 방향과 이동 방향의 차이 | 기존 8방향 블렌드 (`*_b`, `*_l`, `*_r`) |
| 드리블 런 | 공 소유 중 속도 2.5~7 m/s [제안] | `dribble_run_30·50·65` |

## 결정론 · 성능
- RNG를 쓰지 않는다. 엔티티 순서 0~24 고정, 후보 동점은 클립 순서로 정한다.
- `Build`만 할당한다(장면 로드 때). `Evaluate`·`FootLock`은 할당 0. 렌더 fps·해상도는 계획 입력에 들어가지 않는다.
- 측정 런타임은 D17·043과 같다: CPU·GC 판정은 IL2CPP 벤치, E2E 개발 빌드는 기록만. 프레임 시간은 `low-igpu-1080p` E2E.
- 예산: 마커 `Players.Motion` p95 ≤ 0.8 ms, 최대 ≤ 1.5 ms. 043 `Players.Anim` ≤ 2 ms 안에 포함한다.

## 테스트 전략
측정 지표(AC-01~11·18)는 수정 중에는 EditMode로 장면 하나씩 빠르게 재고, 최종 판정은 스펙대로 재생 실행(`e2e/054-motion-metrics`)에서 한다.

| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `MotionFidelityTests.RootError_WithinLimits`, `e2e/054-motion-metrics` | 로직 | mac-m1-1600p |
| AC-02 | `FootPlantTests.Slide_WithinLimits`, `.Height_WithinLimits`, 같은 E2E | 로직 | mac-m1-1600p |
| AC-03 | `YawTrackTests.FrameDelta_Under20`, 같은 E2E | 로직 | mac-m1-1600p |
| AC-04 | `ContactSyncTests.TimeDistancePart`, 같은 E2E | 로직 | mac-m1-1600p |
| AC-05 | `ContactSyncTests.PlantFoot_95Percent`, 같은 E2E | 로직 | mac-m1-1600p |
| AC-06 | `ContactKeysTests.BallAfterContact_2cm`, `.PullCap_15cm`, 같은 E2E | 로직 | mac-m1-1600p |
| AC-07 | `InteractionPlannerTests.NoPenetration`, 같은 E2E | 로직 | mac-m1-1600p |
| AC-08 | `InteractionPlannerTests.OutcomeOrder_MatchesSim` (합성 판정) | 로직 | — |
| AC-09 | `PostureTrackTests.Lean_Within5`, `TransitionPlannerTests.PostureUsage_90Percent` | 로직 | — |
| AC-10 | `MotionSpeedTests.AllRates_SameMetrics`, `.Cadence_InTable` | 로직 | — |
| AC-11 | `ContactKeysTests.Interp30Hz_Vs100Hz_3cm` | 로직 | — |
| AC-12 | 보류 (002·003) | — | — |
| AC-13 | `MotionDeterminismTests.*`, `e2e/054-motion-metrics` 프로필 비교 | 결정론 | low-igpu-1080p, desktop-1440p |
| AC-14 | `e2e/054-motion-set` | E2E | smoke |
| AC-15 | `e2e/054-speed-compare` | E2E | low-igpu-1080p, mac-m1-1600p, phone-landscape |
| AC-16 | `test_motion.py`, 리포트 확인 | E2E | mac-m1-1600p |
| AC-17 | `farm.py bench --spec 054`, `e2e/054-motion-perf` | 성능 | mac-m1(IL2CPP), low-igpu-1080p |
| AC-18 | `MotionSpeedTests`·`MotionFidelityTests`·`InteractionPlannerTests` 경계 항목, `e2e/054-motion-metrics` 경계 세트 | 로직 | mac-m1-1600p |
| AC-19 | `e2e/054-motion-film` 1.5x 연속 프레임 육안 승인 → `evidence/approval.md` | E2E | mac-m1-1600p |

## 버그 방지
- **발 고정**: 다리가 닿지 않는 디딤 위치는 무릎이 튄다 → IK 전에 도달 거리를 자르고 디딤 위치 자체를 재계획한다. 전환·액션 경계에서 접지가 끊기는 구간은 `FootPlantTests`로 확인한다.
- **0°/360° 경계**: yaw·진행 방향 탐지는 모두 `IEEERemainder`로 잰다(056 T14a와 같은 버그 방지).
- **전환과 액션 겹침**: 공 접촉이 항상 이기고, 접촉 시각은 밀리지 않는다 → `TransitionPlannerTests.ActionWins`.
- **30 fps 키 보간**: 접촉 순간 발이 멈추는 043 함정. 새 컷 클립은 kicks.py 해석 곡선 방식을 쓰고 Python에서 Unity 샘플링을 재현해 확인한다(055 교훈).
- **Present 경계**: 교체 프레임의 속도 폭주·겹침 → `InteractionPlannerTests.Substitution_*`.
- **기존 회귀**: 043·055 슬라이드·접촉·결정론 테스트와 기준 이미지가 그대로 통과해야 한다. 이번 변경으로 포즈가 바뀌면 기준 이미지 갱신을 승인받는다.
- **진단 꺼짐 비용**: 진단 null 분기만 남기고 A/B 측정한다(≤ 0.02 ms).

## 위험 · 미정
- **태클·슬라이딩 관통(AC-07)**: 부위 캡슐 판정은 합성 장면 위치에 크게 좌우된다. 기존 클립으로 못 지키면 멈추고 클립 추가나 기준 조정을 제안한다.
- **발 고정 비용**: 다리 50개(선수 22 + 심판 3) IK가 0.8 ms를 넘을 수 있다. 넘으면 IK를 2회 반복하지 않고 1회로 줄이고, 그래도 넘으면 보고한다.
- **생성기 재실행**: 태그·클립 추가로 `player.glb`가 바뀐다. Unity 재임포트와 기존 기준 이미지 영향을 T04에서 확인한다.
- **[제안] 추가 수치** (플랜 승인 대상): 컷 탐지 창 0.35 s, 180° 턴 135°, 드리블 런 속도 2.5~7 m/s, 부위 캡슐 8개.
- 범위 밖 유지: 041 재생기, 실제 판정 연결, 성향 표현(스펙 "이번 구현 범위").

## 구현 중 바뀐 점 (2026-11-06)
- 클립 태그: `footPath`·`stance`·`plant` 대신 모든 클립 공통 `stancePaths` 하나(발끝 경로, 비루프는 6프레임 조각). 서 있는 클립도 접지 구간을 갖는다.
- 발 고정 기준은 발목이 아니라 발끝이다. 다리가 다 펴지면 발을 세워 맞추고, 놓을 때는 먼저 들어 올린 뒤 옮긴다(땅에 끌리지 않는 스텝).
- 파일 통합: `PostureApplier` → `FootLock`, `ContactKeys`·`FaultFollower` → `InteractionPlanner.cs`, `PostureTrack` → `TransitionPlanner.cs`, `BodyCapsules`·`SlideTracker`·`MotionStats`는 순수 로직이라 `Logic/Motion/BodyCapsules.cs`.
- 추가 [제안] 수치: 도달 거리 0.65 m, 놓는 시간 0.15 s, 숙임 상한 0.2 rad, 헤딩 패자 지연 0.07 s, 접지 판정 발 속도 1 m/s, 회피 거리 0.4~0.9 m.
- 컷 탐지: 005가 2 m/s 아래에서 피벗하므로 들어오거나 나갈 때 2 m/s 이상이면 컷. 135° 이상은 속도와 무관하게 턴. 감속 탐지 창은 2 s.
- 키 배율: `AnimPlan.Build`가 엔티티별 배율을 받는다. 시뮬 공 높이는 선수 키를 모르므로(002·008 전) 높이 차는 화면 공 보정 15 cm 안에서 흡수한다 — 실제 판정 연결 때 재확인.
- 043 버그 수정: 앞 접촉이 끝나기 전에 다음 액션이 시작돼 앞 액션 가중치가 접촉 순간 0이 되던 문제. 043 ContactAccuracyTests·RootCorrectionTests는 054 기준(화면 공·접촉 키)으로 비교하도록 수정.
- 리뷰(2026-11-06) 반영: 실패한 트래핑·빗맞은 슈팅은 공에 닿는다(`MissesBall`은 태클·선방·경합만), 예상 밖 시뮬 오류는 시뮬 실패로 보고, 접촉 키에 태클·GK 포함, 같은 클립 합침, 접촉 창 프레임 번호 경계, 장면 3개 추가(회피·몸싸움·기둥 스침), 필름 시나리오 `054-motion-film`, 리포트에 기준값 버전·이전 값·접촉 그래프·시뮬 실패.
- 추가 [제안] 수치: 시뮬 오류 판정 11 m/s·25 m/s², 회피 어깨 25°·펴는 창 0.3 s, 숙임 비율 0.6·기울기 상한 0.6 rad, 전환 임계 4·3 m/s, 전환 재생 속도 0.65~1.6, 컷 클립 경계 67.5°, 보정 최소 시간 0.25 s, 위반 캡처 8장.
- 구현 중 추가한 [제안] 수치 전부 채택 (2026-11-06 사용자 승인).

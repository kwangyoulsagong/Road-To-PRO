# 042 — 중계 카메라 · 리플레이 · 플랜

상태: 승인됨 (2026-11-10)
스펙: ./spec.md

## 접근
카메라 경로를 **로딩 단계에서 장면별로 미리 계산**한다(`CameraTrack`). 041이 클립을 미리 만드는 것과 같은 구조다. 감쇠·컷·흔들림은 30Hz 시뮬 프레임 기준 고정 스텝으로 한 번만 계산하고, 재생 중에는 재생 시각으로 트랙을 보간해 읽기만 한다. 그래서 프레임률·배속이 달라도 경로가 같고(AC-09·10), 재생 중 계산과 GC가 사실상 없다(AC-14).

연출 규칙(프레이밍·컷·골 시퀀스·앵글 배치·흔들림)은 Unity를 모르는 순수 C#으로 만들어 EditMode에서 검증하고, 얇은 MonoBehaviour가 결과를 `Camera`에 적용한다.

대안으로 기획 리서치의 Cinemachine 3을 검토했으나 쓰지 않는다. 감쇠가 렌더 프레임에 묶여 AC-10과 충돌하고, `Packages/manifest.json` 변경이 필요하며, 트랙 방식이면 필요한 기능이 적다.

화각은 트랙에 수직·수평 반각 tan 값으로 저장하고 재생 때 화면비로 FOV를 구한다. 창 크기가 바뀌어도 트랙을 다시 만들지 않는다.

## 모듈 · 파일
경로 접두: `Assets/_Project/`

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Broadcast/Logic/CameraTypes.cs | `CameraPose`(위치, 시선, 반각 tan, 흔들림 오프셋), `ShotKind`, `CameraMode`, 리버스 플래그 |
| RoadToPro.Presentation | …/Broadcast/Logic/SceneBrief.cs | 장면별 연출 입력(FR-23): 그룹·시퀀스 역할, 사건 종류·시각·위치, 득점자, 앵글 번호·수, 라이브 컷 요청, 내 선수 관여, 흔들림 원천. 018이 채울 형식 |
| RoadToPro.Presentation | …/Broadcast/Logic/CameraRigData.cs | 경기장 입력(FR-06): 거치 범위, 금지 상자, 골대 기둥·크로스바·그물. `StadiumLayout`에서 변환, 없으면 기본값 + 진단 1건 |
| RoadToPro.Presentation | …/Broadcast/Logic/CameraTuning.cs | `Data/tuning/camera.json` 값 묶음 |
| RoadToPro.Presentation | …/Broadcast/Logic/FramingTargets.cs | 프레임에서 공·볼 캐리어·최근접 수비 2명·수신 후보 선택과 가중치 (FR-03, FR-52). 수신 후보는 008 전까지 다음 `PassReceived` 이벤트, 없으면 전방 최근접 동료 |
| RoadToPro.Presentation | …/Broadcast/Logic/FramingSolver.cs | 시선·반각 계산, 고정 스텝 감쇠, 박스 근처 줌, 팬 ±75°·틸트 한계, 화면비 (FR-02~04, 50, 51, 55) |
| RoadToPro.Presentation | …/Broadcast/Logic/LiveCutRules.cs | 인플레이 금지, 데드볼 컷, 최소 2.5 s, 180° 규칙, 동시 요청 우선순위 (FR-10~14, 56) |
| RoadToPro.Presentation | …/Broadcast/Logic/GoalSequenceDirector.cs | 골 시퀀스 샷 순서·구간 (FR-20, 33). 골 뒤 프레임이 짧으면 구간을 줄이되 순서 유지 |
| RoadToPro.Presentation | …/Broadcast/Logic/ReplayAnglePlacer.cs | 앵글 3종 후보, 사건 위치 기준 좌우 대칭 배치, 가림 시 다음 후보, 금지 영역 제외, 대체 하이앵글 (FR-21~25, 54) |
| RoadToPro.Presentation | …/Broadcast/Logic/CameraOcclusion.cs | 선분 대 원기둥·상자 교차 (FR-24). 물리 레이캐스트 없음 |
| RoadToPro.Presentation | …/Broadcast/Logic/CameraShake.cs | 원천별 감쇠 노이즈, 시드는 장면 id (FR-40~42). 메인 카메라 제외 |
| RoadToPro.Presentation | …/Broadcast/Logic/CameraModeLogic.cs | 탑뷰·내 선수 추적 포즈 (FR-30~33), 세로 화면 탑뷰 (FR-55) |
| RoadToPro.Presentation | …/Broadcast/Logic/CameraTrackBuilder.cs, CameraTrack.cs | 장면 → 30Hz 포즈 배열 생성, 0.3 s 리플레이 진입 블렌드 (FR-14), `Sample(시각, 화면비)` |
| RoadToPro.Presentation | …/Broadcast/Logic/CameraSettings.cs | 모드·흔들림 끄기 값 (저장은 인터페이스 주입, 022·038 전에는 메모리 값, 041 `PlaybackSettings`와 같음) |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Broadcast/BroadcastCamera.cs | `IHighlightCamera` 구현. 트랙 샘플을 `Camera`에 적용, 마커 `Highlight.CameraUpdate` |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/{HighlightScene,HighlightSequencer,HighlightPorts,SceneClipBuilder}.cs, Highlight/HighlightPlayer.cs | **041 최소 변경**: 장면 `GroupId`(기본 0 = 그룹 없음), 같은 그룹 사이 타임라인 없음, 건너뛰기는 그룹 끝으로 (FR-53), 카메라 포트에 샘플 호출 추가, 클립 빌더에서 트랙 생성 슬라이스 |
| RoadToPro.Verification | Verification/Runtime/Samples/Highlight/BroadcastScenes.cs | 합성 골·가장자리 장면과 `SceneBrief` 대체 입력 (018·012 대체, 054 합성 장면 선례) |
| RoadToPro.Verification | Verification/Runtime/Samples/Highlight/HighlightScreen.cs | 화면 id `broadcast.*` 추가. 기존 041 화면은 `StandInCamera` 유지 |
| RoadToPro.Verification | Verification/Runtime/Bench/BroadcastCameraBench.cs | IL2CPP 벤치: 샘플 p95, 가림 검사 시간 |
| RoadToPro.Tests | Tests/EditMode/Presentation/Highlight/Broadcast/*.cs | 로직·결정론 테스트 |
| 데이터 | Data/tuning/camera.json, Data/e2e/042-*.json, Data/perf/042-*.json | 조정 값, E2E, 성능 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음. Presentation → Core·MatchEngine 읽기만, 시뮬 상태 변경 없음. 대체물은 Verification에만 둔다.

## 데이터 · 스키마
- `SceneBrief`: `SceneId, GroupId, Role(Live/GoalMain/ReplayAngle/GoalTail), EventKind, EventTime, EventPos, ScorerEntity, AngleIndex, AngleCount, LongPath, LiveCuts[], MyPlayerEntity, MyPlayerInvolved, ShakeSources[]`.
- 골 시퀀스의 장면 구성: `GoalMain`(… ~ 골 +6 s: 라이브 → 득점자 클로즈업 2.5 s → 세리머니 3.5 s), `ReplayAngle` × 3(각 시뮬 3 s, 0.5x), `GoalTail`(골 +6 ~ +11 s: 반응 2.5 s → 킥오프 와이드 2.5 s). 전부 같은 `GroupId`.
- `CameraTrack`: 30Hz `CameraPose[]`, 샷 경계 인덱스(샷 경계는 보간 안 함), 리버스 플래그 구간.
- `Data/tuning/camera.json`: FOV 범위, 팬 한계, 감쇠, 프레이밍 여백, 샷 길이, 앵글 후보 오프셋, 강슛 임계. 기존 `TuningLoader` 사용. 세이브 스키마 변경 없음.
- `FR-25` 슬로모 지점은 트랙의 표시 값으로 내보내고 적용은 041 범위로 둔다.

## 결정론 · 성능
- 트랙 생성은 장면 데이터·`SceneBrief`·설정·경기장만 입력으로 받는 순수 함수다. 흔들림 RNG는 001 시드 RNG에 장면 id를 넣어 만든다. 시스템 시각·프레임 수·`Time.*` 미사용.
- 감쇠는 30Hz 프레임 기준 고정 스텝. 재생 시 보간은 위치·시선·반각을 선형으로, 샷 경계에서는 끊는다.
- 재생 경로 할당 0: 배열은 로딩 때 만든다. `Sample`은 인덱스 계산과 보간뿐이다.
- 키 모먼트 재시뮬 때는 바뀐 장면의 트랙만 다시 만든다.
- 측정: 카메라 CPU·가림 검사는 IL2CPP 벤치, GC는 `mac-m1-1600p` E2E, 전환 프레임은 `low-igpu-1080p` E2E.
- 041 진입 로딩 예산(≤ 4 s)에 트랙 생성 시간을 더해 `042-perf`에서 함께 기록한다.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `Framing_LiveTenSeconds_PositionFixed_FovInRange` | 로직 | — |
| AC-02 | `LiveCut_RejectsInPlay_AcceptsFirstDeadBall_RespectsMinShot` | 로직 | — |
| AC-03 | `LiveCut_NeverFarSide`, `Replay_FarSide_SetsReverseFlag` | 로직 | — |
| AC-04 | `GoalSequence_ShotOrder` (합성 골) | 로직 | — |
| AC-05 | `ReplayAngles_MirroredGoal_Symmetric` | 로직 | — |
| AC-06 | `ReplayAngles_PostOccludes_MovesToNextCandidate` | 로직 | — |
| AC-07 | `Framing_BallFarOutside_PanClamped`, `Framing_NearTouchline_BallInFrame` | 로직 | — |
| AC-08 | `MyPlayerMode_CentralThird_BallVisible` | 로직 | — |
| AC-09 | `Track_TwoBuilds_SameHash` (흔들림 포함) | 결정론 | — |
| AC-10 | `Track_30vs60fps_SameAtSameTime` (시퀀서 경유) | 결정론 | — |
| AC-11 | `e2e/042-goal-sequence` (라벨은 041 대체 HUD) | E2E | 전체 |
| AC-12 | `e2e/042-camera-modes` | E2E | smoke + ultrawide-1080, phone-landscape |
| AC-13 | `e2e/042-edge-ball` | E2E | smoke |
| AC-14 | `BroadcastCameraBench`, `e2e/042-perf` | 성능 | IL2CPP mac-m1, mac-m1-1600p |
| AC-17 | `e2e/042-perf` 기록만 (보류, 048 판정) | 성능 | low-igpu-1080p |
| AC-15 | `Shake_Disabled_AllOffsetsZero` | 로직 | — |
| AC-16 | `RigData_ThreeStadiums_MountAndNoGo`, `RigData_Missing_DefaultAndOneDiagnostic` (044 최소·기본·최대 layout을 테스트 픽스처로) | 로직 | — |

## 버그 방지
- 그룹 없는 041 장면의 동작 불변: 기존 시퀀서 테스트 + `Sequencer_GroupedScenes_NoTimeline_SkipEndsGroup`.
- `Normalizer` 겹침 처리: `GoalTail`이 `ReplayAngle` 뒤에 와도 잘리지 않는지 테스트.
- 골 뒤 프레임이 짧은 장면(현재 실제 시뮬 클립): 순서 유지, 예외 없음 테스트.
- 프레이밍 대상 0명·프레임 결손·`Present=false`: 직전 프레이밍 유지 (FR-52).
- 팬이 ±180° 경계를 넘을 때 감쇠가 반대로 돌지 않게 각도 대신 시선 벡터로 감쇠.
- 샷 경계를 보간하지 않는지(컷이 번지지 않음), 일시정지·탐색 때 샘플이 그대로인지 테스트.
- 건너뛰기: 시퀀스 중 어느 샷에서든 다음 그룹 첫 카메라로 컷 (FR-53).

## 위험 · 미정
- 메인 카메라 위치는 044 거치 범위가 우선이고, 그 안에서 FR-01에 가장 가까운 점을 고른다 (스펙 FR-01·FR-06 수정, 2026-11-10 사용자 결정).
- 세로 화면의 탑뷰 고정은 라이브 구간에만 적용하고, 골 시퀀스·리플레이는 FR-33대로 둔다.
- AC-17(리플레이 진입 전환 프레임)은 측정·기록만 하고 041 AC-15와 함께 048 릴리스 빌드에서 판정한다 (2026-11-10 사용자 결정).
- 최소 경기장은 카메라가 터치라인에 가까워 FOV 상한 30°로 무리 전체를 못 담을 수 있다. 이때는 공 > 캐리어 > 나머지 순으로 담는다.
- 041 화면·기준 이미지는 `StandInCamera`를 유지해 재승인이 필요 없다. 실제 카메라로의 교체는 018 연결 때 한다.
- 관중·실제 HUD 없이 측정한다(045·039 연결 때 재측정). 와이프 표시는 039 전까지 대체 HUD 플래그로만 확인한다.

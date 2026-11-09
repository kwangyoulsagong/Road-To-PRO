# 041 — 하이라이트 3D 플레이어 · 재생 속도 · 플랜

상태: 구현 완료 · AC-15 보류 (2026-11-09, 리포트 https://claude.ai/artifact/6ine73zr4qFE9toyAvu4c5). 대체 입력 범위 — 연결 때 재확인 (spec "재확인")
스펙: ./spec.md

## 접근
재생 규칙(시계·배속·장면 순서·건너뛰기·키 모먼트·큐 발화)은 Unity를 모르는 **순수 C# 상태 기계**로 만들어 EditMode에서 프레임 단위로 검증한다. 그 위에 얇은 MonoBehaviour(`HighlightPlayer`)가 3D 씬, 25명 리그, 공, 입력을 연결한다.

재생 입력은 006 `MatchSim.ExtractClip`으로 **로딩 단계에서 장면별 클립을 전부 미리 만든다** (FR-02). 전체 약 3 MB로 작고 재생 중 시뮬을 돌리지 않으므로 전환 스파이크와 GC를 구조적으로 없앤다. 키 모먼트 재시뮬은 선택 화면에서 재생이 멈춘 동안 바뀐 장면만 다시 만든다.

아직 없는 스펙(018·019·039·042·045·046·048)은 **인터페이스 뒤의 대체물**로 둔다. 인터페이스는 Presentation에, 대체물은 Verification에 둬서 출시 코드에 섞이지 않게 한다. 대안으로 재생 중 롤링 생성(워커 스레드)을 검토했으나, 미리 만드는 쪽이 동시성 없이 로딩 예산(≤ 4 s) 안에 들어갈 것으로 보여 택하지 않았다. T12의 로딩 측정에서 초과하면 멈추고 재논의한다.

## 모듈 · 파일
경로 접두: `Assets/_Project/`

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/HighlightScene.cs | 장면 항목(FR-01): id, 시작·끝 시각, 리플레이·키 모먼트 여부, 정지 시각, 우선순위 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/SceneListNormalizer.cs | FR-51 경기·하프 경계 자르기, FR-52 겹침 당기기(리플레이 예외), 빈 구간 제거 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/PlaybackClock.cs | `IPlaybackClock` 구현. FR-10~13: double 누적, 배속 규칙(리플레이 0.5x), 장면 시작 재설정. 실제 시간은 호출자가 넘겨 UI·시뮬 시계와 분리 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/PlaybackSettings.cs | FR-10 사용자 속도 값 하나 (저장은 인터페이스로 주입, 022·038 연결 전에는 메모리 값) |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/CueDispatcher.cs | FR-55: (시각, 일련번호) 순 큐 커서. 배열 미리 정렬, 프레임당 구간을 빠짐없이 1회 발화, 할당 0 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/HighlightSequencer.cs | 재생 상태 기계(로딩·재생·전환·타임라인·키 모먼트 대기·결과·취소). FR-20~22·32·50·54·56·57·58·59·5A. 프레임 입력을 한 곳에서 순서대로 처리(건너뛰기 → 속도 → 일시정지) |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/HighlightPorts.cs | 대체 지점 인터페이스: `ISceneListSource`(018), `IKeyMomentHandler`(019·FR-57), `IHighlightHud`(039·FR-40·41), `IHighlightCamera`(042), `IFramePolicy`(048·FR-32·33·34), `IHighlightAudio`(046·FR-15), `IHighlightDiagnostics`(FR-53·70) |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/ClipConverter.cs | `ReplayClip` → `HighlightData`(FR-02·03): 슬롯·심판 인덱스, `PitchCoordinates` 공식의 좌표 변환, Absent → `Present=false`, `SimEvent` → `AnimEvent`·큐 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/BallVisualGuard.cs | FR-04: 지면 아래 금지, 보간 구간이 골대·골망 면을 넘으면 넘기 전 프레임 값 유지. 샘플 프레임은 시뮬 값 그대로 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/Logic/SceneClipBuilder.cs | 장면 목록 + 클립 소스 → 장면별 `HighlightData`·`AnimPlan`. 진행률, 프레임 시간 분할, 취소. 키 모먼트 뒤 재구축. 클립 불가면 FR-53 진단 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/HighlightAssets.cs | FR-30·33·58: 씬 비동기 로드·언로드, 핸들 목록(남은 핸들 수 공개), 셰이더 웜업, `Resources.UnloadUnusedAssets` |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/HighlightStage.cs, Highlight/Logic/LoadHandleTracker.cs | 3D 씬의 무대 추상 클래스(리그·공·클립 준비, 선수 외형은 002·020 전까지 씬 구현이 정함), 로드 핸들 목록(AC-20) |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/HighlightPlayer.cs | MonoBehaviour. 매 프레임 `unscaledDeltaTime`을 시퀀서에 전달, 포즈 적용, 포트 호출. FR-54 앱 포커스 처리 |
| RoadToPro.Presentation | Presentation/Runtime/Players/PlayerAnimationSystem.cs (수정) | `SwapPlan(plan)` 추가: 그래프·애니메이터 재사용, 플랜과 발 고정 상태만 교체 (기존 `Init`은 그래프를 다시 만들어 FR-31·AC-15·16을 어김) |
| RoadToPro.Presentation | Presentation/Runtime/Players/FootLock.cs, PlayerAnimator.cs (필요 시 수정) | 할당 없는 상태 초기화 |
| RoadToPro.Verification | Verification/Runtime/Samples/Highlight/StandIns.cs | 대체물: 이벤트 기반 고정 장면 목록(018), 스크립트 키 모먼트 응답(019), 검증 HUD(039, 라벨 이름 고정), 공과 주변 필드 선수를 화면비에 맞춰 담는 방송 시점(042), 프레임 정책(048), 큐 기록 오디오(046) |
| RoadToPro.Verification | Verification/Runtime/Samples/Highlight/HighlightScreen.cs | E2E 화면 등록. 고정 시드 경기 실행, 프레임 고정 캡처, 위치 시퀀스 해시, 투영 위치 단언 |
| RoadToPro.Verification | Verification/Runtime/Samples/Highlight/{HighlightCastStage,HighlightBench}.cs | 043 가상 선수 무대, IL2CPP 벤치 워크로드. 새 E2E 스텝 없이 goto 화면 + 기존 단언으로 구동 (`IFarmPlayback`·`IDeterminismProbe` 사용) |
| RoadToPro.Verification.Editor | Verification/Editor/HighlightSceneSetup.cs, FarmBuild.cs·PlayerSceneSetup.cs (수정) | 호스트 `Highlight041`(카메라·HUD, 캡처가 시작 때 카메라를 붙이므로 호스트 소유) + 무대 `Highlight3D` 씬 생성, 빌드 목록 추가 |
| RoadToPro.Tests.EditMode | Tests/EditMode/Presentation/Highlight/*.cs | 아래 테스트 전략 |
| 데이터 | Data/tuning/highlight.json | 타임라인 구간 길이 등 튜닝 값 |
| 데이터 | Data/e2e/041-*.json, Data/perf/041-*.json | E2E 시나리오·성능 기준 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음. Presentation → MatchEngine·Core, Verification → Presentation. `MatchEngine` 변경 없음.

## 데이터 · 스키마
- `HighlightScene`: `Id:int, Start:double, End:double, IsReplay:bool, IsKeyMoment:bool, KeyMomentTime:double, Priority:int` (시각은 시뮬 초). 세이브 변경 없음(비기능 세이브 호환).
- `Data/tuning/highlight.json`: `timelineGapSeconds` = 1.0 (2026-11-09 사용자 결정, 039 연결 때 조정 가능).
- 큐 항목: `(float time, long seq, kind, entity)` 구조체 배열. 장면 구축 때 정렬한다.

## 결정론 · 성능
- **시뮬 불변 (AC-02)**: `ExtractClip`은 별도 `MatchSim`을 포크한다. 재생 코드는 원본을 읽기만 한다. 재생 전후 체크포인트 해시와 이벤트 로그 해시를 비교한다.
- **재생 시계**: 시각은 `double` 누적이고 입력 dt만으로 정해진다. 해상도·프레임률이 재생 순서에 영향을 주지 않는다. AC-19는 렌더 프레임이 아니라 고정 시뮬 시각(k/60 s)에서 위치를 샘플링해 프로필 간 비교한다.
- **GC 0 (AC-16)**: 프레임 경로에서 큐·장면 배열은 사전 할당하고, `SwapPlan`만 쓴다. 문자열은 HUD 값이 바뀔 때만 대체 HUD에서 만들며, 측정 프로필에서는 HUD를 고정 문자열 표로 처리한다.
- **판정 런타임**: 프레임 시간·GPU는 E2E, CPU·GC는 IL2CPP 벤치(`farm.py bench --spec 041`, D17·043 선례). Mono 값은 기록만 한다.
- **셰이더 웜업 측정 (AC-15)**: 로딩 끝에 모든 머티리얼을 화면 밖에서 한 번 렌더하고, 재생 중 `ProfilerRecorder`로 `Shader.CreateGPUProgram` 카운터를 읽어 0건을 확인한다. 동기 로드는 재생 경로에서 `HighlightAssets`가 로드 API를 부른 횟수 카운터로 판정한다.
- 장면 사이 렌더 중지(FR-32, 048 FR-17 [제안] 그대로): 대체 `IFramePolicy`가 `OnDemandRendering.renderFrameInterval`로 렌더 빈도를 낮추고 다음 장면 1프레임 전에 복귀한다.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `HighlightSequencerTests.Tick_ThreeScenes_PlaysInOrderWithinOneSnapshot` | 로직 | — |
| AC-02 | `HighlightDeterminismTests.Playback_FullRun_LeavesSimHashUnchanged` | 결정론 | — |
| AC-03 | `PlaybackClockTests.Advance_EachRate_ProgressesByRate` (1·1.5·2·3x) | 로직 | — |
| AC-04 | `PlaybackClockTests.BeginScene_Replay_StaysHalfSpeedThenRestores` | 로직 | — |
| AC-05 | `PlaybackClockTests.SetUserRate_MidScene_NoPositionJump` | 로직 | — |
| AC-06 | `CueDispatcherTests.FireUpTo_FiveCuesInOneFrame_FireOnceInSimOrder` | 로직 | — |
| AC-07 | `HighlightSequencerTests.Start_NoScenes_NeverRequestsLoad` | 로직 | — |
| AC-08 | `SceneListNormalizerTests.Normalize_BeyondMatchAndHalf_ClipsToBoundaries` | 로직 | — |
| AC-09 | `HighlightSequencerTests.Tick_KeyMoment_StopsOnceAndContinuesWithNewList` | 로직 | — |
| AC-10 | `e2e/041-highlight-playback` | E2E | 전체 |
| AC-11 | `e2e/041-speed-switch` | E2E | smoke |
| AC-12 | `e2e/041-keymoment-pause` | E2E | smoke |
| AC-13 | `e2e/041-unload` | E2E | smoke |
| AC-14 | `e2e/041-perf` | 성능 | low-igpu-1080p (CPU는 IL2CPP 벤치) |
| AC-15 | `e2e/041-perf` 전환 구간 8회 이상 + 카운터 | 성능 | low-igpu-1080p, mac-m1-1600p |
| AC-16 | `bench --spec 041` `HighlightPlayback.Gc` | 성능 | mac-m1-1600p (IL2CPP) |
| AC-17 | `e2e/041-enter-exit` 10회 메모리 | 성능 | mac-m1-1600p |
| AC-18 | `e2e/041-battery` (`batterySaver` 재정의) | 성능 | low-igpu-1080p |
| AC-19 | `e2e/041-highlight-playback` 위치 시퀀스 해시 비교 | 결정론 | low-igpu-1080p ↔ mac-m1-1600p |
| AC-20 | `HighlightAssetsLogicTests.Cancel_WhileLoading_ReleasesAllHandles` | 로직 | — |
| AC-21 | `HighlightSequencerTests.RequestRate_OnTransition_AppliesNextScene`, `Tick_SkipAndRateSameFrame_SkipsFirst`, `Tick_InputWhileLoading_Ignored` | 로직 | — |

## 버그 방지
- 시각 경계: 장면 끝을 정확히 넘는 프레임, 3x 대형 dt 한 프레임에 장면 전체를 지나가는 경우 → 큐 누락·중복 테스트.
- 구간: 시작 ≥ 끝이 된 장면, 0분·종료·하프 경계, 리플레이 겹침 → 정규화 테스트.
- 건너뛰기가 키 모먼트를 넘지 않음(FR-21, 039 FR-23), 모두 건너뛰기 중 남은 키 모먼트 처리.
- 일시정지 중 속도 변경, 리플레이 중 속도 변경, 전환 중 일시정지.
- 클립: 교체·퇴장 Absent 프레임 보간, `HighlightData` 프레임 2개 미만 구간 → 진단 처리.
- 키 모먼트 재구축이 실패하거나 목록이 비는 경우 → 결과 화면으로.
- 취소가 로드 도중·웜업 도중 각각 올 때 핸들 0.
- `SwapPlan` 뒤 발 고정 상태가 앞 장면을 끌고 오지 않음.
- 동시성 없음: 모든 구축은 메인 스레드 분할 실행.

## 위험 · 미정
- 성능 AC는 관중·실제 HUD·방송 카메라 연출 없이 재므로 045·039·042 연결 때 다시 판정한다.
- 대체 장면 목록은 013 대체 구현의 평균 득점 7.8 경기로 만든다. 장면 수는 충분하지만 실제 선정 품질은 018에서 본다.
- 대체 심판은 재시작 때 선수를 한 틱에 재시작 위치로 옮긴다(2026-11-09 실측: 골 직후 88건). 대체 장면 선정기가 그 직전에서 장면을 자른다. 재생기에는 이 처리가 없고 012·018 연결 때 사라진다.
- 054의 "재생 시계·배속 전환은 041 연결 때 재확인" 항목은 T17에서 함께 돌린다. 054 FR-86의 골대 충돌 연결은 BallVisualGuard의 공 쪽만 해당하고, 선수와 골대 충돌은 범위 밖이다.
- `low-igpu-1080p`는 렌더 스케일 프로필이라 실제 내장 GPU 측정과 다르다 (D7 기존 위험 그대로). 60 fps 제한기 흔들림이 절대값에 보이면 054 선례대로 빈 화면 대비 판정을 사용자에게 제안한다.

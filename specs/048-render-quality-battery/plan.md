# 048 — 렌더 품질 프리셋 · 배터리 · 성능 예산 · 플랜

상태: 구현 완료 · 실기기 보류 (2026-11-10)
스펙: ./spec.md (P1 범위, 2026-11-10 결정)

## 접근
렌더 품질을 **순수 데이터(프리셋 정의·프레임 예산 표)**와 **얇은 적용기**로 나눈다. 프리셋 정의는 Unity를 모르는 C#으로 EditMode에서 FR-01 Low 열과 대조하고(AC-01), 적용기가 그 값을 URP 에셋·품질 레벨·조명·블롭 섀도에 반영한다. Low는 **새 `Low_RPAsset` + `Low_Renderer` + 품질 레벨 `Low`**로 만들고 기존 Mobile/PC는 손대지 않는다 — Medium/High 프리셋은 후속 스펙까지 Mobile/PC가 대역이다.

041이 만든 `IFramePolicy` 포트의 실제 구현(`HighlightFramePolicy`)을 Presentation에 두고 Verification의 `StandInFramePolicy`를 대체한다. 하이라이트 60 / 배터리 30 상한, 재생 중 배터리 토글 즉시 적용, 타임라인 구간에서는 **방송 카메라를 끄고 마지막 프레임을 렌더 텍스처로 한 번 떠서 UI 뒤에 고정**한다(FR-17). 041 시퀀서가 이미 다음 장면 1프레임 전에 `ExitTimeline`을 부르므로 그때 카메라를 켠다. 재생 시각은 041 `PlaybackClock`이 실시간으로 진행하므로 fps·프리셋이 시뮬 시각을 바꾸지 않는다(AC-04).

스파이크(AC-12·18)는 로딩 단계에서 막는다: 기존 LOD 워밍업에 **셰이더 변형 모음 워밍업**을 더하고, 041 AC-15·042 AC-17은 디바이스팜에 **릴리스(비개발) 빌드 E2E 경로**를 추가해 판정한다. 개발 빌드의 Mono·프로파일러 오버헤드가 70 ms 튐의 원인인지 여기서 가린다.

하위 시스템 몫(AC-16)은 **절제(ablation) 측정**으로 나눈다: 고정 프레임(freeze)에서 시스템 그룹을 하나씩 끄고 GPU·드로우콜·삼각형 차이를 잰다. 프레임 안 GPU 패스별 타이머보다 플랫폼 의존이 적고 결정론적이다. CPU 몫(FR-22)은 기존 프로파일러 마커(`Players.Anim`, `Highlight.CameraUpdate` 등)를 묶어 읽는다.

대안: (1) Mobile 에셋을 Low로 고치는 안 — 기존 기준 이미지 296장 재승인이 생겨 버림(2026-11-10 결정). (2) 타임라인 구간을 `OnDemandRendering.renderFrameInterval`로 낮추는 안(041 대역 방식) — UI까지 같이 느려지고 3D 0프레임을 보장 못 해 버림. (3) URP 패스별 GPU 타이머 — Metal에서 패스 단위 GPU 시간이 불안정해 버림.

## 모듈 · 파일
경로 접두: `Assets/_Project/`

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.Presentation | Presentation/Runtime/Rendering/Logic/RenderPreset.cs | 프리셋 정의 값(FR-01 열): 렌더 스케일, 업스케일러, 그림자 방식(블롭/방향광/실시간), 잔디 방식, 관중 방식, 선수 LOD 정책, SSAO·볼류메트릭·SSR, 후처리 묶음, 텍스처 최대 크기, Unity 품질 레벨 이름, 대역 여부 |
| RoadToPro.Presentation | …/Rendering/Logic/RenderPresetTable.cs | `Low`(실제), `Medium`→Mobile 대역, `High`→PC 대역. 이름으로 조회, 알 수 없는 이름은 Low + 진단 |
| RoadToPro.Presentation | …/Rendering/Logic/FrameBudget.cs | FR-20 전체 예산 + FR-21 GPU·드로우콜·삼각형 몫 + FR-22 CPU 몫 표(원본). 측정값을 받아 초과 시스템 목록을 돌려준다(AC-16) |
| RoadToPro.Presentation | …/Rendering/Logic/FrameCap.cs | 상태(하이라이트/타임라인/메뉴) × 배터리 절약 → 목표 fps·3D 렌더 여부 (FR-12·13·14·17) |
| RoadToPro.Presentation | Presentation/Runtime/Rendering/RenderQualityApplier.cs | 프리셋 적용: 품질 레벨 전환, URP 렌더 스케일·STP, 후처리 볼륨 프로필 교체, 메인 라이트 그림자 끄기, 블롭 섀도 켜기. 하이라이트 진입 때 1회(P1은 재생 중 프리셋 변경 없음) |
| RoadToPro.Presentation | Presentation/Runtime/Rendering/HighlightFramePolicy.cs | `IFramePolicy` 구현. `Application.targetFrameRate`, vSync 0, `SetBatterySaver(bool)` 즉시 적용, 타임라인 진입 시 프레임 고정·카메라 끔, 종료 시 카메라 켬 |
| RoadToPro.Presentation | Presentation/Runtime/Rendering/FrozenFrame.cs | 타임라인 진입 프레임에 방송 카메라를 렌더 텍스처로 1회 렌더(URP `SubmitRenderRequest`), 전체 화면 UI 이미지로 표시. 창 크기 변경 시 다시 만들지 않고 늘려 표시, 재개 때 새 설정 적용 |
| RoadToPro.Presentation | Presentation/Runtime/Stadium/PlayerBlobShadows.cs | 선수 25명 발밑 블롭: 낮 1장(태양 반대 방향으로 약간 늘림), 나이트 4장(`BlobShadow4.Compute` 재사용). 인스턴싱 1~2 드로우콜, 할당 없음 |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/HighlightAssets.cs | **041 최소 변경**: `WarmUp`에 셰이더 변형 모음 점진 워밍업 추가(로딩 화면 뒤, 프레임당 시간 제한) |
| RoadToPro.Presentation | Presentation/Runtime/Highlight/HighlightStage.cs | **최소 변경**: 블롭 섀도 대상 목록·조명 프리셋 선택(낮/나이트) 훅 |
| RoadToPro.Presentation.Editor | Presentation/Editor/Rendering/LowPresetAssets.cs | Low URP 에셋·렌더러·볼륨 프로필(톤맵 + 약한 블룸) 생성, 2K 텍스처를 밉맵 제한 그룹 `Max1K`에 등록, 셰이더 변형 모음 저장 도구 |
| 설정 에셋 | Assets/Settings/Low_RPAsset.asset, Low_Renderer.asset, LowVolumeProfile.asset, Assets/_Project/Presentation/Rendering/HighlightVariants.shadervariants | 생성물 (`.meta` 짝 커밋) |
| ProjectSettings | ProjectSettings/QualitySettings.asset | 품질 레벨 `Low` 추가(맨 앞, Low_RPAsset, 밉맵 제한 그룹 `Max1K` = 1), 기존 Mobile/PC 값 불변 (2026-11-10 승인된 변경) |
| RoadToPro.Verification | Verification/Runtime/Samples/Highlight/HighlightScreen.cs, HighlightCastStage.cs | `StandInFramePolicy` 제거 → `HighlightFramePolicy`. 화면 `highlightNight`, 프리셋 적용 연결, 블롭 대상 등록, 카운터 `render3DFrames`·`frozen`·`maxTextureDim` |
| RoadToPro.Verification | Verification/Runtime/Steps/RenderSteps.cs | 단계 `renderSet`(preset·renderScale·batterySaver), `perfBreakdown`(그룹별 끄고 재기) |
| RoadToPro.Verification | Verification/Runtime/PerfSampler.cs | CPU 마커 묶음 기록(FR-22), 릴리스 빌드에서 렌더 카운터가 없으면 null로 기록 |
| RoadToPro.Verification.Editor | Verification/Editor/FarmBuild.cs | `BuildMacRelease`: E2E 씬 포함, `BuildOptions.None`, IL2CPP(판정 런타임 D17과 같음, 빌드 동안만 전환·복구 — 벤치 빌드 선례) |
| 디바이스팜 | Tools/devicefarm/farm/{cli,build,commands,perf,compare}.py | `--build release` 옵션(별도 빌드 경로·캐시), 캡처 쌍 최소 차이 판정 `captureDiff`, 몫 분해 리포트(초과 시스템 표시) |
| 데이터 | Data/e2e/profiles.json | `qualityMap` Low → `Low` (Medium/High 그대로) |
| 데이터 | Data/e2e/048-*.json, Data/perf/048-render-quality-battery.json | E2E·성능 시나리오, 예산 기계용 사본 |
| RoadToPro.Tests | Tests/EditMode/Presentation/Rendering/*.cs | 로직·결정론 테스트 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음. Rendering/Logic은 UnityEngine 비참조(Presentation 안 순수 C# 폴더, 042 Broadcast/Logic 선례). Presentation → Core·MatchEngine 읽기만. 대체물·측정 단계는 Verification에만.

## 데이터 · 스키마
- `RenderPreset`: `Id(Low/Medium/High), QualityLevel, RenderScale, Upscaler(None/STP), Shadows(Blob/DirectionalPlusBlob/Realtime), Grass(Single/Shell), Crowd(Billboard/BillboardLod/VertexAnim), PlayerLod(Lod1Default/…), Ssao, Volumetric, Ssr, Post(ToneBloomWeak/…), MaxTextureSize, IsStandIn`. Low 값: 0.67, STP, Blob, Single, Billboard, Lod1Default, off×3, ToneBloomWeak, 1024, false.
- 관중은 045 전이라 값만 두고 적용하지 않는다(진단 없음 — 관중 객체가 없음).
- `FrameBudget`: GPU 10 ms · CPU 8 ms · 드로우콜 300 · 삼각형 50만 · 텍스처 1 GB, 몫 표 FR-21·22 원문. 측정값 입력 `(system, gpuMs, drawCalls, tris)` → 초과 목록.
- `Data/perf/048-render-quality-battery.json`: 예산 사본 + `deferred`(최소 사양 실기기 보류).
- `profiles.json` 스키마 불변, `qualityMap` 값만 변경. 세이브·설정 파일 스키마 변경 없음(설정 화면은 후속).
- 배터리 절약 진입점: `HighlightFramePolicy.SetBatterySaver(bool)` + 팜 인자 `-farmBatterySaver`(기존)·단계 `renderSet`.

## 결정론 · 성능
- 렌더 코드는 시뮬 상태를 읽기만 한다. 재생 시각은 `PlaybackClock`(실시간 dt) — fps가 바뀌어도 장면 길이 동일. AC-04는 시퀀서를 30/60 Hz dt로 돌려 장면별 시뮬 구간·클립 해시를 비교하고, 클립 빌더 출력 해시가 프리셋과 무관함을 확인한다.
- 할당 0(FR-23): 블롭 행렬 배열·렌더 텍스처는 로딩 때 생성. 프레임 고정 렌더 텍스처는 타임라인마다 재사용, 해상도가 바뀐 경우에만 재생성(재생 경로 밖 — 타임라인 구간).
- STP는 고정 렌더 스케일만(FR-31). 동적 해상도 끔.
- 텍스처 1K: 밉맵 제한 그룹 `Max1K`(2K 텍스처 3장: 클로즈업 피부·노멀, 이름 글리프)에 Low 레벨 제한 1. 런타임 생성 키트 아틀라스는 크기 상수 확인(1K 이하면 변경 없음). 측정 카운터 `maxTextureDim`.
- 측정: 프레임·GPU·드로우콜·삼각형·텍스처 메모리는 개발 빌드 E2E(`048-perf-highlight`), 전환 최대 프레임은 **릴리스 빌드** E2E(`048-transition-release`, 렌더 카운터 없음 — 프레임 시간만), CPU 몫·GC는 IL2CPP 벤치 선례 유지 + E2E 마커.
- 흉내 낸 프로필 표시(000 FR-44): 모든 성능 결과에 `isReferenceDevice=false`면 "잠정".

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `RenderPreset_Low_MatchesFr01Column`, `RenderPresetTable_UnknownName_LowWithDiagnostic`, E2E 카운터 `maxTextureDim ≤ 1024`·렌더 스케일 0.67·STP 로그 (`048-presets`) | 로직 | — / low-igpu-1080p |
| AC-04 | `FramePolicy_30vs60Hz_SameSceneSimSpans`, `ClipBuilder_HashIndependentOfPreset` | 결정론 | — |
| AC-05 | `e2e/048-presets` (각 프로필 자기 프리셋 기준 이미지 + mac-m1에서 3프리셋 캡처 쌍 차이 ≥ 2%) | E2E | low-igpu-1080p, mac-m1-1600p, desktop-1440p |
| AC-06 | `e2e/048-upscale` (렌더 스케일 1.0/0.75/0.67 고정 슈팅 프레임, UI 영역 차이 0) | E2E | low-igpu-1080p, uhd-4k-200 |
| AC-08 | `e2e/048-perf-highlight` (골·코너킥·역습 + 나이트) | 성능 | 성능 예산 표 프로필 전체 |
| AC-09 | `e2e/048-transition-release`(릴리스) 최대 프레임 ≤ max(33 ms, 빈 화면 구간 최대), 개발 빌드 `048-perf-highlight`는 기록 | 성능 | low-igpu-1080p |
| AC-11 | `e2e/048-battery` (30 ± 1 fps, 해제 후 1초 안 60), `FrameCap_BatteryToggle_Immediate` | 성능 | low-igpu-1080p |
| AC-12 | `e2e/048-transition-release`(릴리스) 진입 로딩 첫 ≤ 4 s·재진입 ≤ 2 s·첫 장면 최대 프레임, 셰이더 컴파일 0 | 성능 | low-igpu-1080p |
| AC-15 | `perf-baselines.json`에 048 기준선 등록, 회귀 +10% 판정(000 FR-43 기존 로직) | 성능 | 성능 예산 표 프로필 |
| AC-16 | `FrameBudget_OverShare_Reported` + `e2e/048-perf-highlight`의 `perfBreakdown` 결과 리포트 | 성능 | low-igpu-1080p |
| AC-17 | `e2e/048-timeline-gap` (구간 `render3DFrames` = 0, 재개 지연 ≤ 1프레임, 정지 중 창 크기 변경 후 재개 첫 프레임 새 크기), `FrameCap_Timeline_No3D` | E2E | smoke |
| AC-18 | `e2e/048-transition-release`(041 전환 ≥ 8회)·`e2e/048-replay-release`(042 리플레이 진입), 릴리스 빌드, ±1초 최대 ≤ max(25 ms, 전환 밖 최대), 동기 로드·셰이더 컴파일 0 | 성능 | low-igpu-1080p (+ mac-m1-1600p 기록) |
| AC-02·03·07·10·13·14 | 후속 스펙 | — | — |

## 버그 방지
- 품질 레벨 이름으로 찾기: 레벨 순서가 바뀌어도 이름 조회. 기존 프로필(Medium/High)이 이전과 같은 에셋으로 그려지는지 041·042 기준 이미지가 그대로 통과하는 것으로 확인(full).
- 품질 레벨 전환이 URP 에셋을 바꾸면 렌더 스케일을 다시 설정해야 한다 — 적용 순서(레벨 → 렌더 스케일 → STP) 고정, 로그로 실제 값 기록(`FarmBootstrap` 기존 로그).
- STP + MSAA 충돌: Low 에셋 MSAA 끔. STP 첫 프레임 히스토리 없음 → 장면 컷마다 히스토리 리셋(잔상 방지, AC-06에서 확인).
- 타임라인 구간: 일시정지·건너뛰기·키 모먼트로 구간이 끊길 때 카메라가 꺼진 채 남지 않게, 상태 진입마다 카메라 켜짐을 `FrameCap` 결과로 재설정. 구간 길이 0이면 고정 생략. 결과 화면·취소로 끝나면 렌더 텍스처 해제.
- 배터리 토글을 타임라인 구간 중에 해도 재개 후 새 상한 적용.
- 블롭: 공·심판 포함 25명 인덱스 범위, 화면 밖 선수도 그려져도 무방(인스턴싱 1회). 나이트 광원 4개 미만이면 낮 방식으로 + 진단 1회.
- 릴리스 빌드: `DEVELOPMENT_BUILD` 전용 코드(진단)가 빠져도 드라이버가 동작하는지 — 릴리스 셀프테스트(`000-selftest-*` 1개)로 먼저 확인.
- 프로젝트 설정 복구: IL2CPP 전환은 빌드 동안만, `farm.py`가 빌드 전 바이트로 되돌림(벤치 선례).

## 구현 중 바뀐 점 (2026-11-10)
- Low 품질 레벨은 Mobile과 PC 사이(인덱스 1)에 둔다 — Standalone 기본 품질 인덱스는 제외 레벨을 뺀 목록 기준이라 맨 뒤에 두면 기본이 PC → Low로 바뀐다. 대역 경로는 PC 레벨을 깐 뒤 에셋만 덮고, 레벨 전환 때 레벨 원래 에셋을 되돌린다.
- 셰이더 변형 모음은 만들지 않음 — 기존 LOD 워밍업 프레임이 Low 후처리·STP·블롭까지 덮어 재생 중 컴파일 0.
- 팜 단계 타입 대신 `goto` 화면(`preset.*`·`renderScale.*`·`battery.*`·`highlightExit`·`hide.*`·`show.all`·`hide.scene`), 몫 분해는 기존 `derived`. 이미지 검사 `differentImage`·`sameImage rect`, 예산 형식 `{max, orMetric, orSection}`(기능 유발분 판정), 구간 지표 `outsideTransitionFrameMs`, 시나리오 `"build": "release"`.
- STP 캡처 안정화: 지터 위상 고정 + 히스토리 리셋 + 블루 노이즈 32주기 간격 비교 (`TemporalCaptureHold`, 검증 전용 리플렉션).
- 블롭: 044 블롭 텍스처는 중계 거리에서 안 보여 하이라이트용 텍스처를 런타임 생성.
- 전환 카운터는 엔진 프레임 간격(`unscaledDeltaTime`)으로 — `Update` 안 실시간 차이는 스크립트 실행 시점 흔들림이 섞였다.

## 위험 · 미정
- 릴리스 빌드에서도 전환 프레임이 25 ms를 넘으면 원인 조사(동기 로드·GC·셰이더) 후 수정이 필요하다 — 수정 범위가 041·042 코드면 멈추고 알린다.
- STP는 Metal에서 지원되지만 렌더 스케일 0.67의 1080p는 내부 723p라 얇은 라인(SDF)이 흐려질 수 있다 — AC-06 캡처로 확인, 문제면 보고.
- Low 프로필(low-igpu-1080p·compact-1366·handheld-800p·tablet-4x3·phone-portrait·phone-landscape)의 041·042·043·044 기준 이미지가 바뀐다 → full 후 일괄 재승인 요청(사용자 승인 필요 지점).
- 관중(045)·실제 HUD(039) 없이 측정. 몫 표의 관중·HUD 칸은 "미연결"로 기록.
- 맥 흉내 프로필이므로 최소 사양 60fps는 잠정 — 상태 '구현 완료 · 실기기 보류'.
- Low 렌더러는 Forward(방향광 1개라 Forward+ 이점 없음, research 06 § 1.2의 Forward+ 권장은 추가 광원 많을 때). 후속 Medium/High에서 재검토.

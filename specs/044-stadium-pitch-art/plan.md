# 044 — 경기장 · 피치 (bpy 생성) · 플랜

상태: 승인됨 (2026-11-03, FR-03 스펙 수정 승인 포함)
스펙: ./spec.md

## 접근
경기장은 **Blender 스크립트 패키지 하나**(`Art/Blender/scripts/stadium/`)가 원본이다. 진입점 `gen_stadium.py`가 인자를 검증하고, **피치 마킹 정의(Law 1 수치 표)**를 먼저 만든 뒤, 그 정의로 골대·골망·관중석·지붕·조명·광고판·외곽 바닥을 순서대로 짓는다. 결과는 세 가지다. ① `stadium.glb`: 시각 메시. 라이트맵 UV2 포함, 텍스처 없음 ② `stadium.layout.json`: 피치 라인 정의, 골대 충돌 형상, 좌석 줄, 카메라 거치 위치·금지 영역, 조명탑 위치, 생성 요약 ③ 미리보기 PNG 3장. 피치 라인은 메시로 만들지 않는다. Unity에서는 **피치 셰이더 하나**가 `layout.json`의 라인 정의를 받아 SDF로 그린다(FR-32). Blender 미리보기에서만 같은 정의로 얇은 라인 메시를 만들고, 이 메시는 내보내지 않는다. 그래서 라인 수치의 원본은 한 곳이고, AC-01은 그 정의와 메시 경계를 함께 검사한다.

Unity 쪽은 glTFast로 glb를 가져온다(2026-11-03 결정). 런타임 컴포넌트 몇 개가 `layout.json`을 읽어 피치 머티리얼 파라미터, 낮·나이트 조명, 나이트 블롭 섀도 4장을 건다. E2E 씬은 에디터 메서드로 생성한다(000 `FarmSampleSetup`과 같은 방식). 디바이스팜은 지금 UI 패널만 캡처하므로 **3D 카메라 캡처, 캡처 영역 주석, GPU 프레임 시간, Python 이미지 검사**를 하네스에 추가한다. 이 기능은 이후 040·041·043도 쓴다.

검토한 대안:
1. 라인을 데칼이나 텍스처로 그리는 방식은 FR-32(어떤 화각에서도 계단 현상 없음)를 못 지켜서 버렸다.
2. FBX 내보내기는 생성 시각이 들어가 AC-03 해시가 깨져서 버렸다(사용자 결정).
3. 좌석을 개별 좌표 6만 개로 내보내는 방식 대신 **좌석 줄**(시작점·방향·간격·개수)로 내보낸다. 파일이 작고, 개수·위치 검사(AC-09)는 펼쳐서 한다.

## 스펙 수정 (2026-11-03 승인, spec.md 반영됨)
- **FR-03 골대 위치 ↔ 004 `GoalFrame` 충돌**: 스펙은 "포스트 앞면이 골라인 뒤쪽 면과 일치"인데, 004 시뮬은 포스트 중심을 골라인 x = 0(피치 경계) 위에 둔다. 시뮬이 원본이므로 **포스트 중심 = 골라인 중심 = 피치 경계(x = 0, 105)**로 바꾸자고 제안한다. Law 1의 "골포스트와 골라인은 폭이 같고 같은 자리"와도 맞다. 이렇게 하면 골라인·터치라인(경계선)은 경계를 중심으로 ±0.06 m에 그린다. 내부 라인(페널티 에어리어 등)은 "라인 폭은 구역 안에 포함" 규칙을 그대로 따른다. AC-01의 "피치 경계 105.00 × 68.00"은 **경계선 중심선 기준**으로 잰다. AC-16도 004 `BallParams`(골 폭 7.32, 높이 2.44, `PostRadius`, `NetDepth`)와 대조한다.

## 모듈 · 파일
| 모듈 | 파일 | 역할 |
|---|---|---|
| Blender 스크립트 | Art/Blender/scripts/gen_stadium.py | 진입점. `sys.argv` `--` 뒤를 argparse로 파싱: `--out`(glb 경로, 같은 폴더에 `*.layout.json`·`preview_*.png`), `--capacity {8000,20000,40000,60000}`, `--tiers 1~3`, `--roof none|partial|full`, `--seat-colors #RRGGBB,#RRGGBB`, `--lights towers|roofline`, `--boards on|off`, `--seed int`, `--no-preview`. 검증 실패 → stderr 메시지 + 종료 코드 2, 파일 없음(FR-12·66). 산출물은 임시 폴더에 만든 뒤 전부 성공하면 옮긴다(부분 파일 금지) |
| Blender 스크립트 | Art/Blender/scripts/stadium/params.py | 인자 → 불변 `StadiumParams`. 등급별 좌석 수, 층별 줄 수, 전체 지붕 + 타워 → 지붕 라인 자동 전환(FR-61, 요약에 기록) |
| Blender 스크립트 | stadium/markings.py | Law 1 수치(FR-02·03) → 라인 정의(선분·원호 목록, 피치 중심 원점, 미터). bpy를 쓰지 않아 순수 Python 테스트 가능 |
| Blender 스크립트 | stadium/geometry.py | 메시 생성 도우미(bmesh): 상자·계단·원기둥·사각 판. 이름·계층 규칙(FR-24) 적용 |
| Blender 스크립트 | stadium/pitch.py, goals.py, stands.py, roof.py, lights.py, boards.py, ground.py | 부위별 생성. `stands.py`는 4면 × 층 × 줄로 계단을 짓고 좌석 줄 데이터를 같이 만든다(FR-25). `goals.py`는 포스트·크로스바·골망(깊이 ≥ 1.5 m, FR-64)과 충돌 형상(캡슐·상자, FR-27)을 만든다 |
| Blender 스크립트 | stadium/camera_data.py | 메인 카메라 거치 범위(메인 스탠드 하프라인 상단)·리플레이 금지 영역(구조물 경계 상자) (FR-26) |
| Blender 스크립트 | stadium/checks.py | 규격(AC-01), 런오프 침범(FR-63), 가시성(`scene.ray_cast`로 거치점 → 피치 네 모서리, FR-62), UV2 겹침(라이트맵 UV 래스터 검사, AC-08), 삼각형·머티리얼 수. 실패하면 종료 코드 3 |
| Blender 스크립트 | stadium/export.py | 축 변환 + Apply Transform + glTF 내보내기(고정 옵션, `export_yup`, 텍스처 없음, 추가 속성 없음). JSON은 키 정렬·소수 6자리 고정 형식. Blender 버전 ≠ 5.1.1이면 경고(FR-65) |
| Blender 스크립트 | stadium/preview.py | Workbench 엔진, 1280×720, 고정 카메라 3장(중계 위치, 골대 뒤 하이앵글, 탑뷰), 미리보기 전용 라인 메시. 같은 인자 → 같은 픽셀 |
| Blender 스크립트 | Art/Blender/scripts/gen_axis_test.py | 축·스케일 검증용 에셋(FR-21): 1 m 큐브 + +X·+Z 표식 + 골대 1개 |
| Blender 테스트 | Art/Blender/tests/test_markings.py | `markings.py` 순수 Python 단위 테스트(Blender 불필요) |
| Blender 테스트 | Art/Blender/tests/test_stadium.py | Blender를 백그라운드로 실행하는 통합 테스트(unittest). 인자 세트 최소·기본·최대 + 오류 4종 + 시드 변경 + 2회 해시 비교 + 실행 시간 |
| 도구 | Tools/devicefarm/farm/art.py, cli.py | `farm.py art --spec 044`: Blender 테스트 실행 → JUnit XML → 로직 리포트와 같은 HTML(`logic.render_html` 재사용) + 인자 세트별 미리보기 격자 → 스크린샷을 `evidence/`에 저장. 미리보기 PNG는 `evidence/blender/<세트>/`에 복사 |
| 산출물 (LFS) | Assets/_Project/Presentation/Stadium/Generated/default/stadium.glb, stadium.layout.json | 기본 인자 산출물. 손으로 고치지 않는다. 다시 만드는 명령은 `Generated/README` 대신 `layout.json` 요약의 `command` 필드에 둔다 |
| 산출물 (LFS) | Assets/_Project/Presentation/Stadium/Generated/axis-test.glb | FR-21 검증 에셋 |
| 서드파티 | Assets/_Project/Presentation/Stadium/Textures/grass_*.png | ambientCG CC0 잔디 1K(albedo·normal). `Art/LICENSES.csv`에 기록. 받을 수 없으면 스크립트로 만든 잔디 노이즈 텍스처로 대신하고 그 사실을 기록 |
| RoadToPro.Presentation | Presentation/Runtime/Stadium/StadiumLayout.cs | `layout.json` DTO + 파서(Core `JsonReader`). 라인·충돌 형상·좌석 줄·카메라·조명탑·요약 |
| RoadToPro.Presentation | Presentation/Runtime/Stadium/PitchCoordinates.cs | 001 정규화 좌표 ↔ Unity 월드 변환(X = 길이, Z = 폭, Y = 위, 센터마크 원점). 041·043이 같은 변환을 쓴다 |
| RoadToPro.Presentation | Presentation/Runtime/Stadium/PitchMaterialBinder.cs | 라인 정의 → 셰이더 파라미터(선분·원호 배열). 줄무늬 패턴·폭, High 확장 자리(셸·마모·젖음 0 고정, FR-33) |
| RoadToPro.Presentation | Presentation/Runtime/Stadium/StadiumLighting.cs | 낮·나이트 프리셋(방향광 1개 + 환경색 + 발광 머티리얼 세기, FR-40·42). 프리셋 값은 ScriptableObject 2개 |
| RoadToPro.Presentation | Presentation/Runtime/Stadium/BlobShadow4.cs | 나이트: 대상 발밑에 조명탑 4기(또는 지붕 라인 네 모서리) 반대 방향으로 늘린 블롭 4장(공유 머티리얼·인스턴싱). 낮: 비활성 (FR-41) |
| RoadToPro.Presentation | Presentation/Shaders/Pitch.shader, PitchLines.hlsl | URP 셰이더(Forward + ShadowCaster + DepthOnly + Meta). 잔디 albedo × 2스케일 디테일, 깎기 줄무늬(띠별 잎 방향 · 시선 방향 → 명암, FR-31), SDF 라인(`fwidth` 기반 안티에일리어싱, 최소 1 px 폭, FR-32) |
| RoadToPro.Presentation | Presentation/Stadium/Materials/*.mat, *.asset | 피치 머티리얼, 블롭 머티리얼·텍스처(스크립트 생성), 조명 프리셋 2개 |
| RoadToPro.Presentation.Editor (새 asmdef) | Presentation/Editor/Stadium/StadiumImportSettings.cs | glTFast 임포트 후처리: 피치 메시 머티리얼 → Pitch.mat로 교체, 정적 플래그·라이트맵 UV(UV2 사용, 자동 생성 끔) |
| RoadToPro.Verification | Verification/Runtime/OffscreenCapture.cs (수정), FarmContext.cs (수정), ScenarioRunner.cs (수정) | `FarmContext.Cameras` 등록 시 카메라를 캡처 RT에 그린다(UI 문서가 없어도 됨, 둘 다 있으면 카메라 → UI 순). `FarmContext.CaptureRegions`(현재 시점의 이름 붙은 픽셀 영역·선분을 돌려주는 함수) → capture 결과에 `regions` 기록 |
| RoadToPro.Verification | Verification/Runtime/PerfSampler.cs (수정) | `FrameTimingManager`로 GPU 프레임 시간(`gpuMs[]`) 수집. 개발 빌드에서만 유효, 없으면 null (기존 카운터와 같은 규칙) |
| RoadToPro.Verification | Verification/Runtime/Samples/StadiumViewsScreen.cs | 044 E2E 씬 제어: `goto` 화면 = 카메라 시점(main, behindGoal, top, pitchLevel, mowA, mowB, lineFov8, lineFov30, night, perfEmpty, perfPitch, perfStructure, perfFull). 시점마다 캡처 영역(줄무늬 띠 2개, 페널티 박스 라인 200 px 구간, 블롭 4장 예상 위치)을 계산해 등록. 성능 시점은 레이어로 피치·구조물만 켜고 끈다 |
| RoadToPro.Verification.Editor | Verification/Editor/StadiumSceneSetup.cs, FarmBuild.cs (수정) | `Stadium044.unity` 생성(glb 인스턴스 + 바인더 + 조명 + 카메라 + 대체 선수 캡슐 1개). 빌드 씬 목록에 추가 |
| 도구 | Tools/devicefarm/farm/imagechecks.py, evaluate.py (수정), perf.py (수정), scenario.py (수정) | 시나리오 `imageChecks`: `bandFlip`(AC-11), `lineContinuity`(AC-12: 끊긴 칸 0, 폭 표준편차 ≤ 1 px), `blobX`(AC-13: 4개 어두운 덩어리, 대각 2쌍), `notBlank`(AC-10). 실패하면 해당 조합 fail. perf: `gpuMs` 집계·예산 키(`gpuMs.p95`, `drawCalls.max`, `triangles.max`, `textureMemoryBytes`) |
| 데이터 | Data/e2e/044-stadium-views.json, 044-mow-flip.json, 044-line-sharpness.json, 044-night.json, 044-stadium-perf.json | E2E 시나리오(AC-10~14). 프로필: views = `full`(전체), mow·night = smoke, line = smoke + `uhd-4k-200`(시나리오 `profiles`에 목록 지정 — 지원 안 하면 scenario.py 확장), perf = `low-igpu-1080p` |
| 데이터 | Data/perf/044-stadium-pitch-art.json | 성능 예산 사본(AC-14·15, 산출물 크기 20 MB) |
| Packages | Packages/manifest.json | `com.unity.cloud.gltfast` 추가 (2026-11-03 승인). 버전은 Unity 6000.3에서 확인한 최신 안정판 |
| RoadToPro.Tests.EditMode | Tests/EditMode/Presentation/Stadium/*Tests.cs | 임포트 축·스케일(AC-02), Unity 쪽 규격·UV2·충돌 형상(AC-01·08·16 보조), layout 파서, 좌표 변환, 바인더 파라미터, 블롭 방향 |

- 의존: Presentation → Core·MatchEngine(이미 있음, `BallParams` 상수 대조용), Verification → Presentation(**새 참조**, E2E 씬 제어용 — Verification은 원래 게임 모듈을 참조하는 어댑터 층이라 규칙 위반 아님). Presentation.Editor → Presentation + glTFast. 코어 모듈 변경 없음 → 결정론 리뷰 대상 아님.

## 데이터 · 스키마
`stadium.layout.json` (schemaVersion 1, 미터, Unity 좌표 = X 길이·Y 위·Z 폭, 센터마크 원점):
```
{ "schemaVersion": 1, "generator": {"blender": "5.1.1", "script": "gen_stadium.py", "args": {...}, "command": "..."},
  "pitch": {"length": 105, "width": 68, "lineWidth": 0.12, "runoff": 5.0,
            "lines": [{"kind":"segment","a":[x,z],"b":[x,z]} | {"kind":"arc","c":[x,z],"r":9.15,"from":deg,"to":deg}],
            "spots": [{"c":[x,z],"r":0.11}], "stripes": {"pattern":"straight","width":5.25}},
  "goals": [{"side":-1, "posts":[{"a":[x,y,z],"b":[x,y,z],"r":0.06}x2], "crossbar":{...}, "net":{"min":[...],"max":[...]}} x2],
  "seats": {"count": N, "rows": [{"section":"N|S|E|W","tier":1,"home":true,"start":[x,y,z],"dir":[x,y,z],"spacing":0.5,"count":k}]},
  "cameras": {"mainMount": {"min":[...],"max":[...]}, "noGo": [{"min":[...],"max":[...]}]},
  "lights": {"mode":"towers|roofline","emitters":[[x,y,z] x4], "switchedFromTowers": false},
  "summary": {"triangles": {"total":n, "<part>":n}, "materials":n, "objects":n, "bbox":[...], "checks": {...}} }
```
오브젝트 이름 규칙(FR-24): `Pitch`, `Goal_L`/`Goal_R`(자식 `Post_*`, `Crossbar`, `Net`), `Stand_N|S|E|W_T1..3`, `Roof_*`, `LightTower_NE..`, `Boards_*`, `Bench_*`, `Tunnel`, `Ground`. 머티리얼(≤ 12): Pitch, Line(미리보기 전용, 내보내지 않음), Concrete, Seat_A, Seat_B, Metal, Roof, Net, Board, LightEmit, Ground, Bench.

## 결정론 · 성능
- Blender: `read_factory_settings(use_empty=True)`, `random.Random(seed)` 하나만, 생성 순서 고정(면 → 층 → 줄), 이름 고정, dict는 생성 순서 고정 + JSON `sort_keys`, 실수 `round(v, 6)`. glTF 내보내기 옵션을 전부 명시해 기본값 변화에 흔들리지 않게 한다. 미리보기는 Workbench(무작위 샘플링 없음). 테스트는 같은 인자 2회 → glb·json·png 바이트 비교.
- Unity: 셰이더 1패스 + 텍스처 2장(albedo·normal 1K). 구조물은 머티리얼별로 메시를 합쳐 드로우콜을 줄인다(Blender에서 머티리얼별 join, 면별 오브젝트는 섹션 단위까지만). 정적 배칭·라이트맵. 블롭 4장은 GPU 인스턴싱.
- 성능 측정: E2E `044-stadium-perf`에서 구간 3개. `perfPitch`(피치만) → 피치 GPU ≤ 1.0 ms, `perfStructure`(구조물만) → 구조 GPU ≤ 1.5 ms, `perfFull` → 드로우콜 ≤ 60, 삼각형 ≤ 10만, 텍스처 메모리 ≤ 128 MB. 구간 값에는 화면 지우기 같은 기본 비용이 포함되므로 실제보다 보수적이다. 생성 시간(AC-15)은 `test_stadium.py`가 `--no-preview` 실행으로 잰다.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `test_markings.MarkingDimensions`, `test_stadium.SpecDimensions`(메시 경계·골대 안쪽 치수), `StadiumImportTests.PitchAndGoalBounds` | 로직 | — |
| AC-02 | `StadiumImportTests.AxisTestCube_IsUnitAndYUp`, `AxisTest_LengthAlongX` | 로직 | — |
| AC-03 | `test_stadium.SameArgs_SameBytes`(glb·json·png 3장) | 결정론 | — |
| AC-04 | `test_stadium.SeedChange_ChangesHash_KeepsSpec` | 결정론 | — |
| AC-05 | `test_stadium.Budgets_Min_Default_Max` | 로직 | — |
| AC-06 | `test_stadium.InvalidArgs_*`(4종: 종료 코드 ≠ 0, 메시지, 출력 없음) | 로직 | — |
| AC-07 | `test_stadium.Visibility_And_Runoff_AllSets` | 로직 | — |
| AC-08 | `test_stadium.LightmapUv_NoOverlap`, `StadiumImportTests.StaticMeshes_HaveUv2` | 로직 | — |
| AC-09 | `test_stadium.Seats_CountAndOnMesh`, `StadiumLayoutTests.SeatRows_Expand` | 로직 | — |
| AC-10 | `e2e/044-stadium-views` + `notBlank` | E2E | 전체 |
| AC-11 | `e2e/044-mow-flip` + `bandFlip` | E2E | smoke |
| AC-12 | `e2e/044-line-sharpness` + `lineContinuity` | E2E | smoke + uhd-4k-200 |
| AC-13 | `e2e/044-night` + `blobX`, `BlobShadow4Tests.FourShadows_PointAwayFromEmitters` | E2E | smoke |
| AC-14 | `e2e/044-stadium-perf` 예산 | 성능 | low-igpu-1080p |
| AC-15 | `test_stadium.GenerationTime_Under60s` | 성능 | mac-m1-1600p |
| AC-16 | `test_stadium.GoalColliders_MatchVisual_AndDeterministic`, `StadiumImportTests.GoalColliders_Match004BallParams` | 로직 | — |

하네스 확장 자체의 테스트: `Tools/devicefarm/tests/test_imagechecks.py`(합성 이미지로 각 검사의 통과·실패), `test_perf.py` gpuMs 추가, `test_scenario.py` imageChecks·프로필 목록 검증, EditMode `OffscreenCaptureTests.Camera_RendersToTarget`. 기존 000 셀프테스트(`farm.py self-test`)가 계속 통과해야 한다.

## 버그 방지
- 축 뒤집힘: **T03 실측 Blender (x, y, z) → Unity (−x, z, −y)**. 생성 스크립트는 경기장 좌표(x 길이·y 폭·z 위)를 받아 Blender에 (−x, −y, z)로 넣는다(`stadium/geometry.py`) → Unity (X 길이, Y 위, Z 폭). `StadiumImportTests`가 표식 3개로 이 규칙을 지킨다.
- 원호 각도·좌우 대칭: 페널티 아크는 페널티 에어리어 밖 부분만 그린다. 좌우 골대 쪽 각도 부호 실수를 잡기 위해 좌우 미러 대칭 테스트를 둔다.
- 라인 폭 포함 규칙: 내부 라인은 구역 안쪽, 경계선은 경계 중심(스펙 수정 제안). 테스트에 바깥 가장자리 좌표를 직접 적는다.
- 부동소수 형식: JSON 숫자는 `-0.0` → `0.0`으로 정규화한다(해시 흔들림 방지).
- 인자 경계: 층수 0·4, 수용 인원 등급 외 값, 음수, `#GGGGGG`·`red`·3자리 색은 거부한다. 최소·최대 조합 모두 예산 테스트.
- 부분 파일: 검사 실패나 예외가 나면 임시 폴더를 지우고 `--out` 경로에 아무것도 남기지 않는다(테스트로 확인).
- 캡처 안정성: 3D 캡처도 "연속 2프레임 동일" 규칙을 쓴다. 셰이더에 시간 의존 항(바람 흔들림 등)을 넣지 않는다.
- 성능 카운터 null: 릴리스 빌드면 GPU 시간·드로우콜이 null이다. 예산 항목이 null이면 "측정 불가 = 실패"로 처리한다(통과로 넘어가지 않게).
- UI 없는 씬: 기존 "UI 문서 없음 → 오류 2" 규칙은 "UI 문서도 카메라도 없음"으로 바꾼다. 셀프테스트로 회귀를 확인한다.

## 구현 중 바뀐 점 (2026-11-03)
- 성능 측정(AC-14): 절대 GPU 값 대신 **빈 화면(카메라만) 대비 차이**. 구간마다 fps 상한을 풀어(`perfBegin.uncapped`) GPU가 병목인 상태의 평균 프레임 간격을 쓴다. 이유: 60fps 상한의 가벼운 부하는 Apple GPU 동적 클럭이 낮아 ms가 부풀려지고, Metal `FrameTimingManager.gpuFrameTime`은 처리 중인 여러 프레임이 겹쳐 잡힌다(0.81 ms 간격에 1.9 ms). gpuMs는 리포트에 기록만 한다.
- 피치 셰이더: 라인이 두 축 대칭이면 |x|, |z|로 접고 1사분면 라인만 계산(27 → 12개), 원호 각도는 atan2 대신 내적.
- 구조물 머티리얼: glTFast 기본 셰이더 그래프 → 같은 색 URP Lit(FR-22 원문대로). 머티리얼 교체는 임포터 리맵이 아니라 씬 생성 단계(`StadiumSceneSetup`).
- `Data/perf/044-*.json`은 만들지 않았다 — EditMode 성능 테스트가 없고, 예산은 E2E 시나리오(`044-stadium-perf`)와 Blender 테스트(AC-15)에 있다.
- UV2는 Blender lightmap pack 대신 면별 선반 배치를 직접 구현(겹침 0 보장).

## 위험 · 미정
- glTFast의 Unity 6000.3 호환 버전과 UV2 임포트 동작은 첫 태스크에서 확인한다. 안 되면 멈추고 보고한다.
- `FrameTimingManager` GPU 시간은 개발 빌드에서 켜진다고 알려져 있다. 켜지지 않으면 `ProjectSettings` `enableFrameTimingStats` 변경이 필요하므로 그때 멈추고 확인받는다.
- 이 기기에는 내장 GPU가 없어서 `low-igpu-1080p` 프로필은 M1에서 해상도·품질만 흉내 낸다(000과 같은 한계). 리포트에 명시한다.
- 잔디 텍스처 다운로드(ambientCG)가 막히면 스크립트로 만든 텍스처로 대신한다. 시각 품질은 낮아지지만 AC에는 영향이 없다.
- 미리보기 PNG의 픽셀 결정론(Workbench + Metal)이 깨지면 미리보기 렌더를 CPU 경로로 바꾸고, 그래도 안 되면 멈추고 보고한다.
- [미정] 셸 잔디·마모·젖음·날씨·골망 물리(048), 수용 인원 등급과 클럽 명성의 연결(020)은 이 플랜에서 다루지 않는다.

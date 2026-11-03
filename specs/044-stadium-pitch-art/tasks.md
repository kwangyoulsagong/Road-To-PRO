# 044 — 경기장 · 피치 (bpy 생성) · 태스크

상태: 승인됨 (2026-11-03, 플랜 승인과 함께 바로 진행)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다. Blender 순수 Python 테스트는 `python3 -m unittest`, Blender 통합 테스트는 `farm.py art --spec 044`, C# 테스트는 `/unity-test`. 코어(Core·MatchEngine) 변경 없음 → 결정론 리뷰 대상 아님.

## A. 축 · 임포트 기반
- [x] T01 glTFast 패키지 추가 — 파일: `Packages/manifest.json` — 검증: Unity 배치 컴파일 에러 0, 패키지 해석 성공 (승인 2026-11-03) — 결과: `com.unity.cloud.gltfast` 6.20.0(최신 안정판, Unity 6000.0+). 컴파일 에러 0
- [x] T02 축 검증 에셋 스크립트 — 파일: `Art/Blender/scripts/gen_axis_test.py`, 산출물 `Assets/_Project/Presentation/Stadium/Generated/axis-test.glb` — 검증: 2회 실행 해시 동일 — 결과: 해시 동일. 공용 `stadium/{geometry,export}.py`를 이 태스크에서 먼저 만듦(bmesh 직접 생성, glTF 옵션 전부 명시)
- [x] T03 임포트 축·스케일 테스트 + Presentation.Editor asmdef — 파일: `Presentation/Editor/RoadToPro.Presentation.Editor.asmdef`, `Presentation/Editor/Stadium/StadiumImportSettings.cs`(초안), `Tests/EditMode/Presentation/Stadium/StadiumImportTests.cs` — 검증: AC-02 통과. 실제 축 변환 결과를 plan "버그 방지"에 기록하고 경기장 생성 좌표 규칙을 확정 — 결과: 실측 Blender (x,y,z) → Unity (−x, z, −y). geometry가 경기장 좌표(x 길이·y 폭·z 위)를 받아 (−x, −y, z)로 넣으면 Unity (X 길이, Y 위, Z 폭). `StadiumImportTests` 3개 통과(큐브 1×1×1·+Y 위, 길이 +X·폭 +Z, 골 안쪽 7.32×2.44). 임포트 후처리 본체는 T12

## B. 피치 마킹 · 생성 스크립트
- [x] T04 마킹 정의 테스트 — 파일: `Art/Blender/tests/test_markings.py` — 검증: AC-01 치수(경계 중심선 105×68, 내부 라인 구역 안 포함, 원호·마크), 좌우 대칭 (구현 전 실패) — 결과: 10개(경계 중심선, 내부 라인 바깥 가장자리 치수, 원호·코너 방향, 좌우 대칭). 구현 전 import 실패 확인
- [x] T05 마킹 정의·인자 — 파일: `Art/Blender/scripts/stadium/{__init__,markings,params}.py` — 검증: T04 통과, 인자 검증 단위 테스트(`test_params.py`) — 결과: `test_markings` 10 + `test_params` 5 통과. 지붕 없음 + 지붕 라인 조명 조합도 인자 오류로 거부(스펙 FR-61의 반대 경우 — 지붕 없이 지붕 라인은 만들 수 없음)
- [x] T06 Blender 통합 테스트 골격 + `farm.py art` — 파일: `Art/Blender/tests/test_stadium.py`, `Tools/devicefarm/farm/{art,cli}.py`, `Tools/devicefarm/tests/test_art.py` — 검증: AC-01·03~09·15·16 테스트 존재(생성기 없으므로 실패), `farm.py art`가 JUnit·HTML 리포트를 만든다, 기존 파이썬 테스트 통과 — 결과: `test_stadium` 20개(AC-01·03~09·15·16 + 이름·좌석·카메라·조명 데이터), `farm.py art`(unittest 수집 → 로직 리포트 형식 HTML + 최소·기본·최대 미리보기 격자 → `evidence/art.{html,png,json}`, `evidence/blender/<세트>/`). `test_art.py` 3개, 파이썬 전체 136개 통과
- [x] T07 생성기: 피치·골대·골망·충돌 형상·내보내기 — 파일: `Art/Blender/scripts/gen_stadium.py`, `stadium/{geometry,pitch,goals,ground,export}.py` — 검증: AC-01·03·06·16 통과 — 결과: 통과. 경기장 좌표 → Blender (−x,−y,z) 변환은 `MeshBuilder`가 하고 면마다 바깥 방향을 받아 감기 순서를 맞춤. 골대 반지름·골망 깊이는 `Data/tuning/ball.json`(004)을 직접 읽음 → 골망 깊이 2.0 m(≥ 1.5). 파일 분리: `layout.py`(공용 치수), `materials.py`(12개). 외곽 바닥·코너 플래그는 `pitch.py`에
- [x] T08 생성기: 관중석·좌석 줄·지붕·조명·광고판·벤치·터널 — 파일: `stadium/{stands,roof,lights,boards}.py` — 검증: AC-04·05·09 통과, 전체 지붕 + 타워 → 지붕 라인 전환 기록 — 결과: 통과. 좌석 줄 수 = ⌈수용/(층 × 링 812석)⌉, 1층부터 채움. 코너는 좌석 없는 블록. 좌석 색은 시드로 고른 2색 패턴(띠·열·체크). 전체 지붕은 S·N이 모서리까지, W·E는 짧게(같은 높이 면 겹침 방지)
- [x] T09 검사·카메라 데이터·UV2·미리보기 — 파일: `stadium/{checks,camera_data,preview}.py` — 검증: AC-07·08·15 통과, 미리보기 PNG 3장 픽셀 동일(AC-03) — 결과: 통과. UV2는 면별 평면 투영 사각형을 선반 배치(직접 구현 — 겹침 0 보장, 래스터 검사로 확인). 런오프 검사는 면 단위(고리 모양 광고판의 경계 상자 오탐 수정). 미리보기는 Workbench + 메타데이터 도장 끔 → 2회 픽셀 동일. 기본 인자 생성 2.4 s
- [x] T10 기본 인자 산출물 생성 — 산출물 `Presentation/Stadium/Generated/default/{stadium.glb,stadium.layout.json}` (LFS) — 검증: `farm.py art --spec 044` 전부 통과, 미리보기를 Read로 직접 확인 — 결과: `farm.py art --spec 044` 35/35 통과. 기본 세트 6,060 tris·머티리얼 12·좌석 40,000·glb가 테스트 산출물과 바이트 동일. 최소 1,848 / 최대 8,536 tris(최대는 지붕 라인 조명 자동 전환). 미리보기 9장 직접 확인 — 골대 뒤 카메라가 W 관중석 안에 있던 것을 높여서 고침

## C. Unity 프레젠테이션
- [x] T11 레이아웃·좌표·임포트 테스트 — 파일: `Tests/EditMode/Presentation/Stadium/{StadiumLayoutTests,PitchCoordinatesTests,PitchMaterialBinderTests,BlobShadow4Tests}.cs`, `StadiumImportTests.cs` 확장 — 검증: AC-01(Unity 쪽 경계)·08(UV2)·09(좌석 줄 펼치기)·16(004 `BallParams` 대조) (구현 전 실패) — 결과: `StadiumLayoutTests` 5·`PitchCoordinatesTests` 3·`PitchMaterialBinderTests` 3·`BlobShadow4Tests` 3·`StadiumDefaultImportTests` 3(피치 면 115×78, 포스트 중심 ±52.5, UV2 전 메시, 004 `BallParams` 골 폭·높이·포스트 반지름·골망 깊이 대조) — 구현 전 컴파일 실패 확인
- [x] T12 `StadiumLayout`·`PitchCoordinates`·임포트 후처리 — 파일: `Presentation/Runtime/Stadium/{StadiumLayout,PitchCoordinates}.cs`, `Presentation/Editor/Stadium/StadiumImportSettings.cs` — 검증: T11 레이아웃·좌표·임포트 테스트 통과 — 결과: 통과. 시뮬 미터 → Unity (x − L/2, z, y − W/2). layout 파서는 Core `JsonReader`. 임포트 후처리는 머티리얼 교체를 임포터 리맵 대신 씬 생성 단계(T19)와 `PitchMaterialBinder`(프로퍼티 블록)로 처리 — glTFast 리맵 의존을 피함
- [x] T13 잔디 텍스처 (ambientCG CC0) — 파일: `Presentation/Stadium/Textures/*`, `Art/LICENSES.csv` — 검증: 1K, LICENSES 한 줄. 다운로드 불가면 생성 텍스처 + 기록 — 결과: ambientCG `Grass004` 1K(CC0) 색·노멀(GL) — 원본 `Art/ThirdParty/ambientCG/Grass004/`, 사용본 `Presentation/Stadium/Textures/`, LICENSES.csv 1줄
- [x] T14 피치 셰이더·바인더 — 파일: `Presentation/Shaders/{Pitch.shader,PitchLines.hlsl}`, `Presentation/Runtime/Stadium/PitchMaterialBinder.cs`, `Presentation/Stadium/Materials/Pitch.mat` — 검증: 바인더 테스트 통과, 셰이더 컴파일 에러·경고 0 — 결과: `RoadToPro/Pitch`(ForwardLit + DepthOnly). 잔디 2스케일, 띠별 잎 방향 · 시선 방향 → 명암(카메라가 반대면 뒤집힘), SDF 라인(직사각 선분·원호·마크, `fwidth` AA, 1 px보다 가늘면 넓히고 밝기 비율로 줄임). 셰이더 컴파일 에러·메시지 0 테스트 추가. 머티리얼·조명 프리셋·블롭 텍스처는 `Presentation/Editor/Stadium/StadiumAssets.Generate`로 생성(파일 추가)
- [x] T15 조명 프리셋·블롭 섀도 — 파일: `Presentation/Runtime/Stadium/{StadiumLighting,BlobShadow4}.cs`, 프리셋 에셋 2개, 블롭 머티리얼 — 검증: `BlobShadow4Tests` 통과 — 결과: `BlobShadow4Tests` 3 통과. `StadiumLighting`(+`StadiumLightingPreset` 낮·나이트 에셋 2개): 방향광·삼색 환경광·발광 세기·배경·블롭 켜기

## D. 디바이스팜 확장
- [x] T16 하네스 확장 테스트 — 파일: `Tools/devicefarm/tests/{test_imagechecks,test_perf,test_scenario}.py`, `Tests/EditMode/Verification/OffscreenCaptureTests.cs` — 검증: 이미지 검사 4종 통과·실패 케이스, gpuMs 집계·null 예산 실패, imageChecks·프로필 목록 형식 (구현 전 실패) — 결과: `test_imagechecks` 12·`test_perf` GpuMs 3·`test_scenario` ImageChecks 3, `OffscreenCaptureTests` 2 — 구현 전 실패 확인
- [x] T17 3D 카메라 캡처·영역 주석·GPU 시간 — 파일: `Verification/Runtime/{OffscreenCapture,FarmContext,ScenarioRunner,PerfSampler}.cs`, `Steps/SystemSteps.cs` — 검증: `OffscreenCaptureTests` 통과, `farm.py self-test` 통과 — 결과: `FarmContext.Cameras/RegisterCamera/CaptureRegions`, 캡처 RT 깊이 32, 카메라 있으면 UI는 RT를 지우지 않음, "UI 문서도 카메라도 없음"일 때만 오류, capture 결과에 `regions`, `PerfSampler` `gpuMs`(FrameTimingManager). 배치(-nographics)는 RT를 못 만들어 테스트는 연결만 확인 — 픽셀은 E2E. 셀프테스트는 T21에서 빌드 후
- [x] T18 이미지 검사·perf·시나리오 확장 — 파일: `Tools/devicefarm/farm/{imagechecks,evaluate,perf,scenario,report}.py` — 검증: T16 파이썬 테스트 통과, 전체 파이썬 테스트 통과 — 결과: `imagechecks.py`(notBlank·bandFlip·lineContinuity·blobX), evaluate가 조합마다 검사 → 실패면 조합 fail, 리포트 "이미지 검사" 표·성능 표 GPU 열, perf `gpuMs` 집계·회귀 항목. 파이썬 전체 157개 통과

- [x] T18a 증거 합치기 (태스크 추가) — 파일: `Tools/devicefarm/farm/{art,commands,report}.py`, `tests/test_art.py` — 이유: logic·matrix가 `evidence/report.html`을 서로 덮어써서 Blender·EditMode·E2E 증거가 한 리포트에 모이지 않음 — 결과: Blender 테스트 클래스 docstring의 AC → `art.json` AC 증거, `logic --spec`이 art.json을 합치고(EditMode에 결정론 테스트가 없어 생긴 '결정론 테스트 없음'은 Blender 결정론 증거로 대신), `smoke/full --spec`이 같은 스펙의 앞선 logic run.json을 합침. 리포트에 Blender 섹션. 테스트 3개 추가

- [x] T18b 시나리오별 실행 폴더 (태스크 추가) — 파일: `Tools/devicefarm/farm/commands.py` — 이유: `full --spec`으로 시나리오 여럿을 돌리면 조합 폴더 이름이 같아 뒤 시나리오가 앞 캡처를 덮어씀(지금까지 스펙은 시나리오 1개) — 결과: 시나리오가 2개 이상이면 `run_dir/<시나리오 id>/<조합>`
- [x] T18c 파생 성능 구간·상한 해제 구간 (태스크 추가) — 파일: `farm/{evaluate,perf,scenario}.py`, `Verification/Runtime/Steps/SystemSteps.cs`, `tests/test_perf.py` — 이유: 절대 GPU 값에 화면 지우기·창 출력이 섞이고, 60fps 상한의 가벼운 부하는 GPU 동적 클럭이 낮아 ms가 부풀려짐(피치 5.3 ms로 측정). 또 Metal `gpuFrameTime`은 처리 중인 프레임이 겹쳐 잡힘(0.81 ms 간격에 1.9 ms) — 결과: 시나리오 `derived`(두 구간 지표 차), `perfBegin.uncapped`(구간 동안 fps 상한 해제). 044는 빈 화면 대비 평균 프레임 간격 차로 GPU 비용 판정, gpuMs는 기록만

## E. E2E 씬 · 시나리오
- [x] T19 E2E 씬 생성기·시점 제어 — 파일: `Verification/Editor/StadiumSceneSetup.cs`, `Verification/Runtime/Samples/StadiumViewsScreen.cs`, `Verification/Editor/FarmBuild.cs`, Verification asmdef(Presentation 참조) — 검증: 씬 생성 배치 성공, 빌드 씬 목록 포함 — 결과: `Stadium044.unity`(glb 인스턴스 + Pitch.mat + 바인더, 해·카메라, 대체 선수 캡슐, 블롭, 조명, 시점 13개). glb 머티리얼은 같은 색 URP Lit로 매핑(FR-22 — glTFast 셰이더 그래프보다 가벼움, 구조 GPU 1.86 → 1.46 ms), 작은 소품·반투명은 그림자 끔. 빌드 씬은 BuildMac만(벤치 빌드 제외). 고친 버그: 조명 프리셋 클래스가 다른 파일 이름에 있어 에셋이 null, 블롭 광원 목록이 직렬화 안 됨
- [x] T20 E2E 시나리오 5개·성능 예산 — 파일: `Data/e2e/044-{stadium-views,mow-flip,line-sharpness,night,stadium-perf}.json`, `Data/perf/044-stadium-pitch-art.json` — 검증: `farm.py smoke --scenario` 각 시나리오 실행, 캡처를 Read로 직접 확인, AC-10~14 판정. 기준 이미지 채택은 사용자 승인 — 결과: 5개 시나리오 전부 통과(full). 라인 끊김 0·폭 표준편차 ≤ 0.71 px, 줄무늬 ±12 반전, 블롭 X자, 성능(m1 판정) 피치 0.45 ms·구조 0.20 ms·드로우콜 54. 기준 이미지 채택(사용자 "베스트케이스로"). High SSAO를 Interleaved Gradient로(Blue Noise는 프레임마다 바뀜), 피치 DepthNormals 패스 추가. 3D 장면은 테마·언어 무관 → 시나리오 `themes`/`langs`로 light·ko 한 벌, 중복 기준 이미지 171장 삭제(1.1 GB → 294 MB). **남음: 한 벌 설정으로 full 재실행 확인 → T21·T22**

## F. 마무리
- [ ] T21 수용 기준 전체 테스트 통과 — `farm.py art --spec 044`, `farm.py logic --spec 044`, 파이썬 테스트 전체 — 검증: 실패 0, 콘솔 에러·경고 0, 컴파일 경고 신규 0
- [ ] T22 `/verify 044` — 디바이스팜 증거 리포트 생성·아티팩트 발행, 실패 0·성능 예산 통과 확인, spec·plan·tasks 상태 갱신

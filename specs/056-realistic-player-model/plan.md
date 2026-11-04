# 056 — 사람다운 선수 모델 · 플랜

상태: 승인됨 (2026-11-04, [제안] 채택: AC-04 기준 25%, 수염 균등, 클립당 4프레임)
스펙: ./spec.md

## 접근
043의 파이프라인 모양(스크립트 패키지 하나 → `player.glb` + JSON + 텍스처, 같은 인자 → 같은 바이트)은 그대로 두고, **메시를 만드는 부분만** MPFB2 CC0 에셋 기반으로 바꾼다. 생성기는 MPFB2 확장의 파이썬 API로 베이스 메시(MakeHuman `base.obj`)에 체형 타깃을 적용해 "기준 선수"(키 180 cm, 근육 0.6, BMI 23 근처)를 만들고, 이 고해상도 기준 표면에 **우리가 정한 저폴리 메시(LOD0·1·2)를 무게중심 좌표로 묶는다**(MakeHuman 프록시 `.mhclo`와 같은 원리). 묶음이 있으므로 MPFB의 체형·얼굴 타깃은 모두 저폴리 메시의 셰이프키로 그대로 옮겨진다 — Decimate가 셰이프키·UV를 깨는 043의 문제(043 플랜 대안 3)를 피한다.

뼈대는 043과 **이름·계층·축이 같고**(FR-62) 관절 위치만 MPFB 기준 표면의 관절 정점 그룹 중심에 맞춘다. 클립 82개는 043의 절차 생성 코드가 새 관절 위치로 다시 굽는다(코드 수정 없음, 산출물만 갱신). 키는 043처럼 **루트 균등 스케일**(키/180)로 낸다: 키 타깃을 선수마다 쓰면 뼈 길이가 선수마다 달라져 공유 클립·IK가 깨진다. 그래서 FR-10의 "키 → MPFB 타깃" 중 키는 스케일로, 체중·근육·비율은 셰이프키로 옮긴다(키 오차 0, 머리 비율은 기준 선수 하나로 고정 — FR-12를 한 번만 맞추면 된다).

Unity 쪽은 런타임 블렌드셰이프를 늘리지 않는다. 얼굴 타깃 20개 이상 + 체형 3 + 수염을 25명이 매 프레임 GPU 스키닝에서 섞으면 비용이 크다. 대신 **하이라이트 로딩 때 선수별 메시를 한 번 굽는다**(`PlayerMeshBaker`: 정점 = 기준 + Σ 가중치 × 델타, 선택한 머리카락 조각을 Body 서브메시에 합침). 재생 중에는 블렌드셰이프가 클로즈업용 `HideHead` 하나뿐이다. 서브메시는 043과 같은 2개(Body·Kit)를 유지해 드로우콜 증가를 0에 가깝게 둔다(FR-63 +25% 이내).

검토한 대안:
1. **MPFB2 프록시 vs 베이스 메시 감량** (스펙 [미정]): 둘 다 "저폴리 메시를 기준 표면에 묶는다"로 같은 처리 경로가 된다. 저폴리 메시의 출처만 다르다 — (a) MPFB 시스템 에셋의 CC0 프록시 (b) 베이스 메시를 한 번 Decimate한 토폴로지(셰이프키 없이 토폴로지만 쓰고 바로 묶음). T03에서 두 후보의 삼각형 수·머리 삼각형 비율·관절 변형 품질을 측정해 고르고 결과를 이 플랜에 적는다. 기준: LOD0 6~10k 중 머리 ≥ 2.0k, 무릎·팔꿈치·어깨 굽힘 링 유지.
2. **머리카락 카드 vs 셸** (스펙 [미정]): 셸만 쓰면 헬멧처럼 보이고, 카드만 쓰면 알파 클립 면적이 커 내장 GPU에 비싸다. **볼륨 셸(불투명, 가닥 방향 노멀 + 이방성 하이라이트) + 가장자리·앞머리 카드(알파 클립, LOD0만)** 를 기본안으로 하고 T09에서 GPU 비용을 측정한다. 셸만으로 FR-40을 못 맞추거나 카드 비용이 예산을 넘으면 멈추고 보고한다.
3. 런타임 블렌드셰이프(043 방식 유지): 구현은 쉽지만 셰이프 수가 4배 이상이라 애니메이션 갱신 예산(p95 ≤ 2 ms)을 위협한다. 로딩 시 굽기로 간다.
4. MPFB "GameEngine" 리그 그대로 쓰기: 뼈 이름·수가 043과 달라 FR-62(클립·IK·Freeze 수정 없음)를 깬다. 리그는 043 것을 쓰고 MPFB 정점 그룹은 관절 위치 계산에만 쓴다.

## 모듈 · 파일
| 모듈 | 파일 | 역할 |
|---|---|---|
| 환경 | MPFB2 Blender 확장 (버전 고정) + MakeHuman 시스템 에셋 팩(CC0) | T01에서 설치·버전 기록. 설치 위치는 Blender 사용자 확장 폴더. 레포에는 **넣지 않는다**(GPL 코드) — 대신 쓰는 CC0 에셋만 `Art/ThirdParty/MPFB2/`에 복사(LFS) + `LICENSES.csv` |
| Blender 스크립트 | Art/Blender/scripts/player/mpfb_src.py (새) | MPFB2 로더: 확장·버전 확인(없으면 설치 안내와 함께 종료 코드 4, FR-03), 에셋 목록(`MPFB_ASSETS` 고정 표, 경로 + sha256)과 대조, 베이스 메시 + 타깃 적용 → 기준 표면. `Art/ThirdParty/MPFB2/` 복사본이 있으면 그걸 우선(재현성) |
| Blender 스크립트 | player/bind.py (새) | 저폴리 → 기준 표면 무게중심 묶음(최근접 삼각형 + 법선 오프셋), 타깃 델타 → 셰이프키 전달, 묶음 결과를 LOD 공통 규칙으로 |
| Blender 스크립트 | player/body.py (대체) | 043 튜브 메시 생성 → 저폴리 메시 준비(T03 결정안) + 부위 태그(`vpart`: head·torso·arm·leg·hand·foot …, 043 검사·가중치 코드가 쓰는 이름 유지). 유니폼에 덮이는 피부 면 제거(관통 원천 차단, FR-50) |
| Blender 스크립트 | player/kit.py (새) | 유니폼 셸: 몸 표면을 부위별로 오프셋(상의·반바지·양말·축구화), 소매·칼라·밑단·양말 윗단에 두께 링(FR-50). UV는 043 `uv.py`의 같은 사각 영역으로 투영 → `player.layout.json` 키트 영역·`PlayerLook` 상수 불변(FR-51) |
| Blender 스크립트 | player/face.py (새) | 얼굴 타깃 표(`FACE_TARGETS`, MPFB 얼굴 타깃 ≥ 20개 + 범위), 수염 4종 셸, 눈(안구·각막 별도 형상, 눈꺼풀이 덮는지 검사), 랜드마크 표(정점 id ≈ 30개: 눈썹뼈·안와·광대·콧대·콧방울·인중·입술 경계·턱끝·귀) → `player.face.json` |
| Blender 스크립트 | player/hair.py (새) | 헤어 10종(043 순서 그대로: 버즈 기본 + 9) 볼륨 셸 + 카드, 두피 위 간격 유지. 스타일별 조각을 별도 메시 데이터로 glb에 넣는다(`Hair_<style>_LOD0..2`, 같은 뼈대 가중치) |
| Blender 스크립트 | player/headlod.py (새) | 클로즈업 머리(≤ 6k tris, 눈·수염·머리카락 포함, FR-61): 기준 표면 머리 영역을 높은 해상도로 묶은 별도 스킨 메시 `Head_Closeup` |
| Blender 스크립트 | player/skeleton.py (수정) | 뼈 이름·계층·축 고정, 관절 위치는 기준 표면에서 계산한 표(생성 시 `player.layout.json`에 기록). 순수 파이썬 기본값은 043 값(테스트·MPFB 없는 단위 테스트용) |
| Blender 스크립트 | player/rig.py, uv.py, shapes 부분 (수정) | 가중치: 043 뼈 선분 거리 규칙을 기준 표면에서 계산해 묶음으로 전달(LOD 간 포즈 일치 유지). 셰이프키 이름 `Thin`·`Muscular`·`Heavy` 유지(값은 MPFB weight·muscle 타깃 델타) + 얼굴 `Face_<target>` + `Beard_<n>` + `HideHead` |
| Blender 스크립트 | player/textures.py (수정) | 피부: MPFB CC0 스킨(또는 절차) 기반 알베도 디테일(밝기 + 붉은 기운 채널: 볼·코·귀·수염 자리), 노멀(근육 베이크 + 모공 노이즈), 마스크(R 거칠기, G 머리카락, B 눈·입술 영역, A 카드 알파). 몸 1024², 얼굴은 같은 아틀라스 1024² 안, 클로즈업 머리 2048²(FR-32). 키트 텍스처는 043 그대로 |
| Blender 스크립트 | player/checks.py (수정) | AC-02 예산·비율, AC-03 관통(피부×키트, 머리카락×몸, 눈×눈꺼풀 바깥)과 뒤집힌 삼각형 — 클립 82 × 극단 4 × 클립당 [제안] 4프레임(접촉·최대 굽힘 포함), AC-01 에셋 라이선스 대조 |
| Blender 스크립트 | gen_player.py (수정) | `--out --lods --clips --seed --no-preview` 유지 + 종료 코드 4(MPFB 없음). 산출물에 `player.face.json`, `Textures/skin_*.png`, `head_closeup.*` 추가 |
| Blender 테스트 | Art/Blender/tests/test_player.py (수정), test_face.py (새) | 2회 바이트 동일, 예산, 비율 200명 샘플, 극단 체형, 라이선스 대조, 랜드마크 JSON 형식, MPFB 없음 → 종료 코드 4 |
| 도구 | Tools/devicefarm/farm/art.py (수정) | `farm.py art --spec 056`: 미리보기 격자(얼굴 8명 정면·3/4·옆, 체형 극단 전신, LOD 3단, 클로즈업 머리) |
| 서드파티 (LFS) | Art/ThirdParty/MPFB2/** | 쓰는 CC0 에셋 복사본(베이스 메시·타깃·스킨·눈 등) + 원본 라이선스 문서 |
| 산출물 (LFS) | Assets/_Project/Presentation/Players/Generated/** | `player.glb`(LOD0~2·헤어 조각·클로즈업 머리·클립), `*.json`, `Textures/*.png` 갱신 |
| RoadToPro.Presentation (Logic, Unity 비참조) | Players/Logic/FaceGenerator.cs (새) | 선수 id·`Face` → 얼굴 타깃 가중치 벡터 + 수염(FR-22·23). 시드 = StableHasher(id, 태그) → SplitMix64, 043 `AppearanceGenerator`와 같은 방식. `PlayerAppearance`는 바꾸지 않는다(043 테스트 회귀 방지, AC-05) |
| 〃 | Players/Logic/FaceLandmarks.cs (새) | `player.face.json` 읽기, 랜드마크 = 기준 + Σ w·Δ(선형), 거리 벡터·쌍 거리(AC-04) |
| 〃 | Players/Logic/BodyShapeMap.cs (새) | 043 외형(Thin·Muscular·Heavy) → 셰이프키 가중치(FR-10·11: Muscular 0.25~1 → MPFB muscle 0.6~0.9 구간) |
| RoadToPro.Presentation | Players/PlayerMeshBaker.cs (새) | 로딩 시 선수별 메시 굽기: 셰이프 합성 + 헤어 조각 합치기(Body 서브메시), 법선·접선 재계산, `HideHead`만 남김. 할당은 로딩 때만, 재생 중 호출 시 진단 로그(043 KitCompositor 규칙) |
| 〃 | Players/PlayerRig.cs (수정) | `ApplyAppearance`가 블렌드셰이프 가중치 대신 구운 메시를 꽂는다. LOD·그림자 프록시·히스테리시스 그대로 |
| 〃 | Players/CloseupHeads.cs (새) | 클로즈업 머리 전환(FR-61): 얼굴 화면 높이 ≥ 25%인 선수 중 큰 순서 최대 2명, 진입·이탈 히스테리시스(043 LOD와 같은 ±15%), 바꾸는 프레임 스파이크 측정 마커 |
| 〃 | Shaders/PlayerBody.shader, PlayerCommon.hlsl (수정) | 피부: 랩 라이팅 + 그림자 경계 붉은 산란 근사(사전 적분 대신 1D 램프 상수), 마스크 거칠기, 노멀맵. 눈: 높은 매끄러움 + 각막 하이라이트. 머리카락: 이방성 하이라이트 근사, 카드 알파 클립(마스크 A < 0.5만 버림). 키트 셰이더는 그대로 |
| RoadToPro.Verification | Runtime/Samples/FaceLineupScene.cs, BodyLineupScene.cs (새) | E2E 장면(043 Freeze·LOD 고정 규칙 재사용) |
| 데이터 | Data/e2e/056-face-lineup.json, 056-body-lineup.json (새), perf-baselines.json (056 항목) | 시나리오·기준. 기준 이미지·성능 기준 채택은 사용자 승인 후 |
| 테스트 | Tests/EditMode/Presentation/Players/FaceGeneratorTests.cs, FaceLandmarkTests.cs, PlayerMeshBakerTests.cs, CloseupHeadsTests.cs (새) | 아래 테스트 전략 |

의존 방향 확인: 새 C#은 전부 `RoadToPro.Presentation`(Logic은 Core 타입만) · `Verification` · 테스트. `.claude/rules/10-csharp.md` 위반 없음. 코어 모듈(Core·MatchEngine) 변경 없음 → determinism-reviewer 대상 아님(외형 결정론은 AC-11 테스트로).

## 데이터 · 스키마
- `player.layout.json` schemaVersion 2: `generator.mpfb`(확장 버전·에셋 sha256 목록), `skeleton.bones`(관절 위치가 MPFB 기준 표면 값으로 바뀜), `shapes`(`Thin`·`Muscular`·`Heavy`·`Face_*`·`Beard_*`·`HideHead`), `hair`(스타일 → 메시 이름), `headCloseup`. `uv` 키트 영역 값은 043과 같아야 한다(T08 테스트가 비교).
- `player.face.json` (새): `{ schemaVersion, targets: [{id, min, max}], beards: [...], landmarks: [{name, base:[x,y,z], deltas:{targetId:[dx,dy,dz]}}] }` 키 정렬·6자리.
- `player.clips.json`: 형식 그대로, 값만 새 관절 위치로 다시 굽힘.
- 세이브 변경 없음: 외형은 선수 id에서 다시 만든다(비기능 세이브 호환). 메시를 세이브에 넣지 않는다.
- FR-23 분포: 피부색·눈 색·머리색은 043 `AppearanceGenerator` 분포(균등) 그대로, 수염 4종(없음·까칠·짧은 수염·턱수염)은 **[미정 — 승인 필요]** 기본안 균등. 리그·국적별 분포는 스펙대로 미정으로 남긴다.

## 결정론 · 성능
- 생성기: MPFB 버전·에셋 sha256 고정, 셰이프·묶음 계산은 정점 id 순서, 집합 순회는 정렬, 부동소수 출력 6자리 → 2회 바이트 동일(AC-01·11). MPFB API가 비결정 출력을 내면(예: 해시 순서) 결과를 우리 쪽에서 정렬해 쓴다.
- C#: `FaceGenerator`는 id → 가중치 순수 함수(SplitMix64). `PlayerMeshBaker`는 정해진 순서로 합산하므로 같은 입력 → 같은 정점 배열(AC-11 해시 비교).
- 재생 중 경로: 새 할당 없음. 블렌드셰이프는 `HideHead` 하나(평소 가중치 0). `CloseupHeads`는 25명 화면 높이 비교 루프, 할당 없음.
- 측정: 043 `043-anim-perf`와 같은 장면에 새 모델을 얹어 프레임 p95·드로우콜·GPU 시간(`FrameTimingManager`; 하네스에 GPU 시간 카운터가 없으면 T19에서 추가)을 low-igpu-1080p·compact-1366에서, 애니메이션 p95는 IL2CPP AnimBench(043 T27 경로). 043 측정값은 `perf-baselines.json`의 043 항목을 기준으로 +20%(GPU)·+25%(드로우콜) 판정.
- 굽기 시간(로딩)은 스펙 예산이 없다 — 기록만 하고, 선수당 수십 ms를 넘으면 보고한다.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `test_player.py::test_two_runs_byte_identical`, `test_player.py::test_assets_all_cc0_in_licenses`, `test_player.py::test_missing_mpfb_exit4` | 로직 | — |
| AC-02 | `test_face.py::test_budget_tris_lods_head`, `test_player.py::test_proportions_200_samples`(키 오차·머리 비율 1/7.5~1/8), `PlayerMeshBakerTests.Height_Within1cm` | 로직 | — |
| AC-03 | `checks.penetration`(클립 82 × 극단 4 × 4프레임: 피부×키트, 머리카락×몸, 뒤집힌 삼각형) ← `test_player.py::test_no_penetration_extremes` | 로직 | — |
| AC-04 | `FaceLandmarkTests.Team22_MinPairDistance`, `FaceGeneratorTests.SameId_SameWeights` | 로직 | — |
| AC-05 | 043 EditMode 전체(`Presentation/Players/*Tests`) 회귀 + `PlayerImportTests` 새 glb 구조 | 로직 | — |
| AC-06 | `e2e/056-face-lineup` (8명 × 정면·3/4·옆, 클로즈업 머리 사용) | E2E | smoke |
| AC-07 | `e2e/056-body-lineup` (극단 포함 8명 × 정면·옆·뒤 + 방송 원거리) | E2E | smoke |
| AC-08 | `e2e/043-kit-lineup`, `e2e/043-contact-closeup`, `e2e/043-speed-motion` 재실행(기준 이미지는 사용자 승인 후 갱신) | E2E | 043과 같은 매트릭스 |
| AC-09 | 리포트에 face/body lineup 캡처 → 사용자 육안 승인 기록 `evidence/approval.md` | E2E | — |
| AC-10 | `e2e/056-model-perf`(043-anim-perf 장면 + 새 모델: 프레임 p95·GPU·드로우콜·클로즈업 전환 스파이크), `farm.py bench --spec 056`(IL2CPP AnimBench) | 성능 | low-igpu-1080p, compact-1366, IL2CPP |
| AC-11 | `test_player.py::test_two_runs_byte_identical`(생성), `PlayerMeshBakerTests.SameSeed_SameMeshHash`, E2E 같은 장면 2회 캡처 동일(하네스 연속 캡처 규칙) | 결정론 | smoke |

AC-04 기준값 **[제안]**: 랜드마크 거리 벡터(약 30점 쌍거리, 키 정규화)의 팀 내 최소 쌍 거리 ≥ 무작위 1,000명 평균 쌍 거리의 **25%**. 절대값(mm)은 T05에서 측정 후 적는다.

## 버그 방지
- 축: 044·043 실측 규칙 Blender (x,y,z) → Unity (−x,z,−y). MPFB 베이스 메시는 데시미터 단위·Z-up일 수 있어 로더에서 m·선수 좌표로 바꾸고, `test_face.py::test_reference_height_180`이 기준 선수 키 1.80 ± 0.005 m를 확인한다.
- 묶음: 최근접 삼각형이 반대쪽 면(손가락 사이·겨드랑이·다리 안쪽)에 붙는 오류 → 부위 태그가 같은 삼각형만 후보로, 법선 내적 > 0 조건. 극단 체형에서 뒤집힌 삼각형 검사(AC-03).
- 유니폼 아래 피부 면 제거는 기준 포즈에서만 판단하면 굽힘 포즈에서 구멍이 보인다 → 경계에 겹침 띠(오프셋 링)를 두고 AC-03 클립 샘플로 확인.
- 눈: 눈꺼풀이 안구를 덮는지 기준 포즈 + 얼굴 타깃 극값에서 검사(눈 타깃이 안구를 노출시키는 조합 금지 → 타깃 범위 축소).
- 헤어: 스타일별 조각 × 극단 체형 × 고개 숙임·젖힘 클립에서 머리카락×목·어깨 관통 0(FR-41).
- 굽기: 셰이프 가중치 0~1 밖 입력은 자른다 + 진단 로그. 법선 재계산이 UV 경계에서 갈라지지 않게 정점 분할 그대로 유지(Unity `RecalculateNormals` 대신 생성기가 낸 델타 법선 합성).
- 043 회귀: `PlayerAppearance`·`ComboKey`·`PlayerLook` 키트 상수를 바꾸지 않는다. 헤어 인덱스 0~9 의미 유지.
- 라이선스: 에셋 목록에 없는 MPFB 파일을 읽으면 생성기가 실패(종료 코드 3)한다 — 서드파티 비CC0 에셋이 몰래 섞이지 않게(FR-02).

## 구현 중 바뀐 점 (2026-11-04)
- T01: MPFB 2.0.18 설치(`--online-mode --command extension install mpfb`). 번들 data(베이스 메시·타깃·game_engine 리그·가중치·메시 메타)는 LICENSE.md/LICENSE.ASSETS.md에 CC0 1.0 명시(코드만 GPL). 시스템 에셋 팩(프록시·스킨·눈)은 다운로드 실패(네트워크, MakeHuman 커뮤니티 서비스 종료 공지) → **쓰지 않는다**. 쓴 파일만 `Art/ThirdParty/MPFB2/data/`에 복사 + `assets.json`(sha256).
- T03 LOD 출처 결정: **베이스 메시 감량 + 기준 표면 묶음**(프록시 팩을 받을 수 없어 비교 불가). Blender COLLAPSE의 정점 그룹 계수는 사실상 켜기/끄기라 머리·몸 두 단계로 나눠 감량, LOD1·2는 이전 LOD에서 단계 감량(한 번에 3 %로 줄이면 손발이 사라짐).
- 레스트 재정렬: MPFB 기본 포즈(다리 벌림·팔꿈치 굽힘)를 MPFB game_engine 리그·가중치로 043 레스트(다리 수직·팔 45° 일직선)에 맞춘다(numpy LBS). 손 뼈 꼬리 = 가운데 손가락 끝(MPFB hand 뼈는 손목 토막이라 접촉점이 낮아졌다).
- 가중치: 043 뼈 선분 거리 대신 **MPFB game_engine 가중치(CC0)를 22뼈로 합산** + 몸 표면 라플라시안 평활 12회(관절 주름 접힘 감소).
- 키 매크로 0.75: 0.5면 머리 비율 1/7.3(FR-12 밖) → 0.75에서 1/7.7.
- 좌표 반사(Blender→선수, 행렬식 −1) 때문에 MPFB 면 감김 기준 법선이 안쪽 → 법선 부호 반전 + 코끝 법선 검사.
- 유니폼: "피부 지우고 셸 씌우기" 대신 **덮이는 면을 유니폼 재질로 바꿔 띄운다**(같은 표면에서 나뉘므로 피부가 유니폼을 뚫을 수 없음). 셔츠·소매는 같은 묶음(틈 없음), 경계마다 턱 띠, LOD2는 띄우지 않음(턱 생략).
- 헤어: **볼륨 셸만**(LOD0 알파 카드 생략 — 드로우콜·알파 클립 비용). 가닥 방향은 parts 텍스처 + Kajiya-Kay. 품질은 AC-09 육안 승인으로.
- 피부 텍스처: MakeHuman UV에 3D 위치 기반 절차 페인팅(외부 이미지 없음). Body 머티리얼 하나에 피부(u<1)·parts(u≥1: 눈·머리카락) → 서브메시 2개 유지.
- E2E 장면: 별도 FaceLineupScene/BodyLineupScene 대신 기존 `PlayerScreen`(Player043 씬)에 화면 `face{n}Front/Three/Side`, `body*`, `perfCloseup` 추가.
- AC-03 판정 규칙: 043 CHECK_PAIRS와 같은 뜻으로 **관절로 이어진 2단계 이내 부위의 접촉은 관통에서 제외**, 뒤집힘은 **한 뼈 가중치 ≥ 0.8인 면**만 실패(관절 주름의 선형 스키닝 접힘은 `creaseFolds`로 기록).

## 위험 · 미정
- **MPFB2 설치·에셋 구성**: 확장 본체에 베이스 메시·타깃이 있고 스킨·눈·프록시는 별도 시스템 에셋 팩일 수 있다. T01에서 확인하고, CC0가 아닌 파일이 섞여 있으면 그 파일은 쓰지 않는다. 설치(다운로드)는 T01에서 하며 버전을 고정한다.
- **절차 클립 재굽기**: 관절 위치가 바뀌면 043 접촉 키 포즈 격자 탐색 결과가 달라진다. 043 클립 검사(접지·미끄러짐)와 AC-08 `043-contact-closeup`으로 회귀를 잡는다. 055가 이 뼈 길이 위에서 보법을 다시 만든다(055는 056 다음).
- FR-10 해석: 키는 MPFB 키 타깃이 아니라 루트 균등 스케일로 낸다(위 접근). 스펙 문구와 다르면 스펙 FR-10에 "키는 스케일" 한 줄 추가를 제안한다.
- 헤어 카드 알파 클립이 내장 GPU에서 비싸면 LOD0에서도 셸만 쓰는 대체안 → FR-40 품질은 AC-09 육안 승인으로 판단.
- 내장 GPU 실기기가 없어 `low-igpu-1080p`는 해상도·품질 흉내(043·044와 같은 한계).
- [미정] 수염 분포(FR-23) — 기본안 균등, 승인 필요.
- [제안] AC-04 기준값 25%, AC-03 클립당 샘플 4프레임.

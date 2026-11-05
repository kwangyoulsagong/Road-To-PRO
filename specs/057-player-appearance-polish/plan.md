# 057 — 선수 외형 다듬기 · 플랜

상태: 승인됨
스펙: ./spec.md

## 접근
056 생성기(`Art/Blender/scripts/player/*`)와 셰이더·외형 생성기를 고친다. 새 모듈은 없다.

1. **칼라 경계**: 지그재그 원인 가설은 "부위 판정을 면의 **주 뼈**로 먼저 거른다"는 점이다. `classify`는 주 뼈가 `Head`인 면을 칼라 평면 아래여도 피부로 두고, `_bisect`도 주 뼈가 대상 집합인 면만 자른다. 그래서 칼라 근처 경계가 평면이 아니라 가중치가 Head↔Neck으로 바뀌는 삼각형 변을 따라 계단이 된다. T01에서 현재 산출물의 경계 루프 꺾임 각·평면 이탈을 재서 원인을 확인한 뒤, **경계 띠(평면 ± 몇 cm) 안에서는 주 뼈와 상관없이 평면으로만 자르고 판정**한다. 자른 뒤 경계 루프 정점을 평면 위로 투영하고 루프 방향으로 고르게 다시 놓는다(턱 띠는 그 루프에서 만든다). 소매·밑단·반바지·양말도 같은 규칙.
2. **가닥 카드**: 헤어라인 고리(앞머리·옆머리·구레나룻·뒷목선)를 따라 바깥·아래로 기운 띠 카드를 셸 가장자리에 붙인다. 카드 텍스처는 parts 아틀라스에 새 칸(가닥 알파·결)으로 절차 생성. **카드는 Body 서브메시 안에 합친다**(드로우콜 0 증가). 셰이더는 parts 카드 칸 픽셀만 알파를 내고 `AlphaToMask`(MSAA 켜짐) 또는 분기 안 `clip`(MSAA 꺼짐)으로 가장자리를 끊는다. T10에서 GPU 몫을 재서 +15%를 넘으면 멈추고 "LOD0 전용 카드 서브메시"로 바꿀지 보고한다(그러면 드로우콜이 늘어난다).
3. **피부**: `skin.py`의 모공·잡티 진폭을 줄이고 부위 가중치(코·볼 1.0 / 이마·눈가·입술 0.35 [제안])를 곱한다. 원거리 깜빡임은 피부 노멀맵 임포트에 밉맵 페이드(먼 밉일수록 평탄)를 켜서 막는다. 측정은 텍스처 단계 지표(로직)와 E2E 연속 프레임 깜빡임(FR-22) 두 가지.
4. **헤어 30종**: `hair.py`를 스타일 정의 표(헤어라인 곡선·두께 함수·부속 형상·카드 배치·계열)로 바꾸고 20종을 더한다. 부속 형상(번·톱 노트·포니테일·하이톱·브레이드)은 머리 뼈에 고정한 간단한 회전체·관(tube)로 만든다. 외형 생성기는 균등 대신 **가중치 표**로 뽑고, 장면 묶음(`GenerateGroup`)에서 같은 스타일이 3명을 넘으면 다음 스타일로 민다(FR-32).

대안: 카드를 별도 서브메시로 두는 안은 품질이 쉽지만 056 드로우콜(043 59.8 × 1.25 상한 기준) 대비 +10%를 LOD0 선수 수만큼 쉽게 넘는다 → 합치기 먼저, 측정 후 판단.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| Blender 스크립트 | player/body.py (수정) | `cut_planes` 경계 띠 정의, `_bisect`·`classify`가 띠 안에서는 평면만 보고 자르고 판정, 경계 루프 투영·재배치(FR-01~03). 턱 생성 그대로 |
| 〃 | player/hair.py (수정) | 스타일 정의 표 30종(`STYLES` 순서: 056의 10종 그대로 0~9 + 새 20종 10~29), 부속 형상, 가닥 카드 생성(LOD0·클로즈업만, FR-10·11), 계열 태그(short·medium·long) |
| 〃 | player/headlod.py (수정) | 클로즈업 머리에 카드 포함, ≤ 6k 확인 |
| 〃 | player/skin.py (수정) | 피부 모공·잡티 진폭·부위 가중치(FR-20·21), 카드 칸 텍스처(가닥 알파·결, FR-10·14) |
| 〃 | player/checks.py (수정) | AC-01 경계 지표(`collar_metrics`), AC-02 관통(헤어 30 × 카드 포함), AC-03 스타일별 예산·카드 유무, AC-05 텍스처 지표(`skin_hf_std`) |
| 〃 | gen_player.py (수정) | 헤어 30종 조각 출력, `player.layout.json` `hair` 목록 30·계열 |
| Blender 테스트 | Art/Blender/tests/test_player.py (수정) | 아래 테스트 전략 |
| 도구 | Tools/devicefarm/farm/art.py (수정) | `farm.py art --spec 057`: 칼라 클로즈업·헤어 30종 일람 미리보기 |
| RoadToPro.Presentation (Logic, Unity 비참조) | Players/Logic/Appearance.cs (수정) | `HairStyles = 30`, 스타일 가중치 표(정수, 합 1000)와 계열, `Generate` 가중 선택, `GenerateGroup` 같은 스타일 상한 3(선수 수 > 90이면 ⌈n/30⌉) |
| RoadToPro.Presentation | Players/PlayerLook.cs (수정) | `HairMeshes` 30개(삭발 null) |
| 〃 | Shaders/PlayerBody.shader, PlayerCommon.hlsl (수정) | 카드 칸 알파: `AlphaToMask On` + MSAA 꺼짐용 분기 `clip`, 카드 가닥 이방성 하이라이트 재사용 |
| 〃 | Editor/Players/PlayerImportSettings.cs (수정) | 피부 노멀맵(`skin*_normal`) 밉맵 페이드 켜기(FR-22) |
| RoadToPro.Verification | Runtime/Samples/CollarHairCloseupScene.cs, HairCatalogScene.cs (새) | E2E 장면(056 FaceLineup·Freeze·LOD 고정 재사용). 깜빡임 측정 단계(카메라 천천히 이동, 연속 프레임) |
| 데이터 | Data/e2e/057-collar-hair-closeup.json, 057-hair-catalog.json, 057-model-perf.json (새), Data/perf/057-player-appearance-polish.json (새) | 시나리오·성능 예산 기계용 사본. 기준 이미지·성능 기준 채택은 사용자 승인 후 |
| 산출물 (LFS) | Assets/_Project/Presentation/Players/Generated/** | `player.glb`·`*.json`·`Textures/*.png` 갱신 |
| 테스트 | Tests/EditMode/Presentation/Players/AppearanceTests.cs, PlayerImportTests.cs, PlayerMeshBakerTests.cs (수정) | 아래 테스트 전략 |

의존 방향 확인: C# 변경은 `RoadToPro.Presentation`(Logic은 Core 타입만)·`Verification`·테스트뿐. `.claude/rules/10-csharp.md` 위반 없음. 코어 모듈(Core·MatchEngine) 변경 없음 → determinism-reviewer 대상 아님(외형 결정론은 AC-10 테스트로).

## 데이터 · 스키마
- 헤어 30종과 계열·가중치 [제안] (합 1000, 짧은 계열 62.3% · 중간 26.0% · 긴·묶음 11.7% → FR-31 ≥ 55% / ≤ 15% 충족):

| # | 스타일 (메시 이름) | 계열 | 가중치 |
|---|---|---|---|
| 0 | 버즈 Hair_Buzz | 짧은 | 42 |
| 1 | 짧은 Hair_Short | 짧은 | 42 |
| 2 | 긴(턱선) Hair_Long | 긴·묶음 | 17 |
| 3 | 곱슬 Hair_Curly | 중간 | 33 |
| 4 | 번 Hair_Bun | 긴·묶음 | 17 |
| 5 | 페이드 Hair_Fade | 짧은 | 42 |
| 6 | 아프로 Hair_Afro | 중간 | 32 |
| 7 | 모히칸 Hair_Mohawk | 중간 | 32 |
| 8 | 드레드 Hair_Dreads | 긴·묶음 | 17 |
| 9 | 삭발 (메시 없음) | 짧은 | 42 |
| 10 | 크루컷 Hair_Crew | 짧은 | 42 |
| 11 | 사이드 파트 Hair_SidePart | 짧은 | 42 |
| 12 | 언더컷 Hair_Undercut | 짧은 | 42 |
| 13 | 슬릭백 Hair_SlickBack | 중간 | 33 |
| 14 | 쿠이프 Hair_Quiff | 짧은 | 41 |
| 15 | 가운데 가르마 Hair_Curtains | 중간 | 33 |
| 16 | 텍스처드 크롭 Hair_TexturedCrop | 짧은 | 41 |
| 17 | 버즈 + 라인 Hair_BuzzLine | 짧은 | 41 |
| 18 | 하이톱 Hair_HighTop | 중간 | 32 |
| 19 | 콘로 Hair_Cornrows | 짧은 | 41 |
| 20 | 트위스트 Hair_Twists | 중간 | 33 |
| 21 | 톱 노트 Hair_TopKnot | 긴·묶음 | 17 |
| 22 | 짧은 포니테일 Hair_Ponytail | 긴·묶음 | 17 |
| 23 | 미디엄 웨이브 Hair_MediumWave | 중간 | 32 |
| 24 | 프렌치 크롭 Hair_FrenchCrop | 짧은 | 41 |
| 25 | 아이비리그 Hair_IvyLeague | 짧은 | 41 |
| 26 | 스파이크 Hair_Spiky | 짧은 | 41 |
| 27 | 짧은 곱슬 톱 Hair_CurlyTop | 짧은 | 42 |
| 28 | 박스 브레이드 Hair_BoxBraids | 긴·묶음 | 16 |
| 29 | 하프 업 Hair_HalfUp | 긴·묶음 | 16 |

- 인덱스 0~9는 056과 같은 스타일(기존 테스트·기준 이미지 해석 유지). 기존 시드의 조합은 바뀐다(스펙 FR-34 허용).
- `player.layout.json`: `hair` 목록 30(삭발 null), 스타일별 `family`·`cards`(LOD0 카드 삼각형 수). schemaVersion 그대로(필드 추가만, 읽는 쪽은 없는 필드 무시).
- `PlayerAppearance` 구조·세이브 변경 없음. `Combos` = 8 × 8 × 30 × 6 = 11,520 (`GenerateGroup` stackalloc 11.5 KB — 로딩 때만, 허용).
- 텍스처 크기 그대로(skin 1024², closeup 2048², parts 1024²). parts 아틀라스에 카드 칸 추가(머리카락 칸 일부를 나눔, 눈 칸 불변).
- 외부 에셋 없음(전부 절차 생성) → `LICENSES.csv` 변경 없음.

## 결정론 · 성능
- 생성기: 스타일 표는 튜플 순서 고정, 카드 배치는 헤어라인 고리 정점 id 순서 + 스타일 이름 해시 시드(정수 노이즈 `_noise`), 출력 6자리 → 2회 바이트 동일(AC-10).
- `Generate`: 가중치 선택은 `SplitMix64` 한 번 + 누적표(정수) — 부동소수 없음. 호출 순서는 기존과 같아 다른 필드(키·체중·피부·얼굴) 값은 그대로이고 헤어 값만 바뀐다.
- `GenerateGroup`: 입력 순서대로, 스타일 카운트 배열(stackalloc 30). 상한 초과면 `(hs + 7) % 30`으로 밀고 조합 충돌 규칙(+389)과 함께 반복. 종료 보장: 상한 × 30 ≥ n.
- 폴리곤: 056 LOD0 9,658 = 몸 약 8.56k + 가장 무거운 헤어 1.1k. 10k 안에 들려면 **헤어(셸 + 카드) LOD0 ≤ 1,400 tris**: 셸 상한 1,000 · 카드 상한 400 [제안]. 클로즈업 머리 카드 ≤ 600, 합계 ≤ 6k. LOD1·LOD2 셸 상한은 056 그대로(230·70).
- GPU: 카드는 Body 서브메시 → 드로우콜 변화 0 목표. `AlphaToMask`/분기 `clip`이 피부 픽셀 early-Z에 주는 영향을 `057-model-perf`의 GPU 몫으로 측정(예산 +15%).
- 굽기: 헤어 조각 정점 수 증가분만큼 `PlayerMeshBaker` 시간 증가 — 056 대비 +20% 예산, EditMode 측정(`PlayerMeshBakerTests` 타이밍 기록).
- 생성 시간: 헤어 조각 20개 추가로 056 약 70 s → 3분 이내 목표(FR-42). 넘으면 스타일 조각 생성을 캐시.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `test_player.py::KitBoundary_Smooth` — LOD0 키트 메시 경계 고리 10개: 칼라 꺾임 ≤ 20°, 나머지 계단 0·꺾임 ≤ 40°, 전부 이탈 ≤ 3 mm. LOD1은 닫힘·이탈만 | 로직 | — |
| AC-02 | `test_player.py::test_no_penetration_extremes` (헤어 30종 × 카드, 클립 82 × 극단 4 × 4프레임) | 로직 | — |
| AC-03 | `test_player.py::test_budget_tris_lods_head` 확장 (스타일별 LOD0 ≤ 10k, LOD1 2~3k, 클로즈업 ≤ 6k, 카드 유무), `PlayerImportTests.Budgets` 갱신 | 로직 | — |
| AC-04 | `AppearanceTests.HairStyles_Thirty_WeightsSum_FamilyShares`, `AppearanceTests.GenerateGroup_HairCapThree_ThousandTeams`(1,000팀 × 22명, 같은 스타일 ≥ 4명 팀 0 ≤ 5%), `AppearanceTests.Generate_SameId_Same` | 로직 | — |
| AC-05 | `test_player.py::test_skin_detail` — 클로즈업 피부 텍스처 얼굴 영역 고주파 표준편차(밝기 R + 노멀 기울기) ≤ 056 값 × 0.5 이고 > 0, 코·볼 > 이마·눈가, 거칠기(정반사) 채널 하한 056 그대로. 056 값은 T01에서 측정해 테스트 상수로 고정 | 로직 | — |
| AC-06 | 043·056 EditMode 회귀(`RoadToPro.Tests.EditMode.Presentation.Players` 전체) + `test_player.py` 키트 UV 영역·칼라 번짐(`test_kit_rim_uv_region`) | 로직 | — |
| AC-07 | `e2e/057-collar-hair-closeup` (8명 × 정면·3/4·뒤) | E2E | smoke |
| AC-08 | `e2e/057-hair-catalog` (30종 × 정면·옆·뒤 × 원거리·근거리, 깜빡임 측정 `custom.faceFlicker`) + 회귀 `056-face-lineup`·`056-body-lineup`·`043-kit-lineup`·`043-contact-closeup`·`043-speed-motion` | E2E | smoke |
| AC-09 | 리포트 육안 승인 → `evidence/approval.md` (Data/perf `manual`) | E2E | — |
| AC-10 | `e2e/057-model-perf`(056-model-perf 장면 재사용: 프레임 p95·드로우콜 056 대비 +10%·GPU 몫 +15%) + `farm.py bench --spec 057`(IL2CPP 애니메이션 p95 ≤ 2 ms·GC 0) + `PlayerMeshBakerTests` 굽기 시간·해시 동일 + `test_player.py::test_deterministic_twice` | 성능·결정론 | low-igpu-1080p, compact-1366, il2cpp |

## 버그 방지
- 경계 띠에서 평면만 보고 자르면 턱(Head) 아래 귀 뒤·뒷목처럼 평면이 피부를 두 번 지나는 곳에서 엉뚱한 섬이 생길 수 있다 → 띠를 목 둘레 반경 안으로 제한하고, 경계 루프가 부위마다 **닫힌 고리 1개**인지 테스트(`test_kit_boundary_smooth`에서 루프 수 검사).
- 루프 정점 재배치가 스킨 가중치·셰이프 델타와 어긋나면 극단 체형에서 칼라가 뜬다 → 재배치 정점은 기준 표면에서 다시 묶음(`_bind_point`) 후 가중치·델타 재계산, AC-02 극단 체형으로 확인.
- 카드가 고개 숙임·젖힘에서 목·어깨·칼라를 뚫는다 → 카드 끝은 헤어라인 아래 [제안] 2 cm 이내로 자르고, 뒷목 카드는 칼라 높이보다 위에서 끝낸다. AC-02 클립 샘플(고개 최대 굽힘 포함).
- 포니테일·박스 브레이드·하프 업은 머리 뒤로 늘어져 위 가슴·등과 가깝다 → 길이 상한을 목 뒤 칼라 위로 두고 관통 검사 대상에 등(UpperChest) 포함.
- 알파 카드가 MSAA 꺼짐 프로필에서 계단·구멍이 된다 → 두 경로(`AlphaToMask`/분기 `clip`) 모두 E2E 프로필에서 캡처(smoke 프로필에 MSAA 꺼짐 1개 포함 확인).
- 헤어 인덱스 0~9 순서가 바뀌면 056 기준 이미지 해석이 깨진다 → 테스트로 0~9 이름 고정.
- `GenerateGroup` 무한 루프: 상한 × 30 < n 경계(91명 이상) 테스트.
- 피부 노이즈를 너무 줄여 플라스틱이 된다 → AC-05 하한(> 0)과 정반사 상한(056 FR-31)을 같은 테스트에서 확인.

## 위험 · 미정
- T01 측정 결과 지그재그 원인이 주 뼈 판정이 아니면(예: 키트 텍스처 칼라 띠, 감량 단계 경계 붕괴) 멈추고 원인과 수정안을 보고한다.
- 카드 합치기의 GPU 비용이 +15%를 넘으면 멈추고 보고(LOD0 전용 카드 서브메시 → 드로우콜 예산 재검토 필요).
- 몸 + 헤어 LOD0 10k 상한이 빠듯하다(헤어 ≤ 1,400). 아프로·하이톱·박스 브레이드가 셸 1,000 안에 안 들면 몸 감량 목표를 조정해야 하는데, 이는 056 수치 변경이라 멈추고 보고.
- [미정] FR-22 깜빡임 기준값 — T01에서 056 현재값을 측정해 "056 대비 ≤ 70%" [제안]으로 확정 승인받는다(기준 채택 지점).
- 부위 가중치(코·볼 1.0 / 이마·눈가·입술 0.35)와 카드·셸 삼각형 상한은 [제안].

## 구현 중 바뀐 점
- **T01 기준 측정 (056 산출물, 2026-11-05)**
  - 키트 경계 (`checks.kit_boundary`, 평면 투영 꺾임 각 최대 / 평면 이탈): LOD0 칼라 **168° / 17.1 mm**(55정점, 평면 위 ±2.5 mm 안 정점 10개뿐), 밑단 80° / 5.2 mm, 양말 53~67°, 축구화 71°, 소매 26~29° / 2.1 mm, 반바지 28° / 1.3 mm. LOD1 칼라 118° / 10 mm, 소매·축구화 13~14정점.
  - 지그재그 원인 두 가지 확인: ① 주 뼈 판정 — 칼라 고리에 위팔(UpperArm)·Head 주 뼈 정점이 끼어 고리가 평면을 벗어남(가설 맞음) ② **감량 메시 위에서 자른 절단점을 기준 표면에 다시 묶을 때 최근접점으로 밀려** 평면에서 −14~+12 mm 벗어남. 플랜의 "띠 안 평면 판정 + 루프 투영·재배치·재묶음"이 둘 다 다룬다 → 진행.
  - AC-01 판정 LOD: **LOD0만 20°·3 mm로 판정**, LOD1은 닫힌 고리·이탈만 기록. 이유: 고리 정점 N개의 평균 꺾임은 360°/N이라 LOD1 소매·축구화(13~14정점)는 고르게 놓아도 26°를 넘는다. 스펙 FR-01은 클로즈업 기준이고 클로즈업·근거리는 LOD0다.
  - 피부 (`skin.detail_metrics`, 클로즈업 2048², 부위별 고주파 RMS): 노멀 **0.0704**(코·볼·이마·눈가·입술 모두 같음 — 부위 차이 없음), 밝기 0.0025, 거칠기 평균 코 0.59·볼 0.68·이마 0.57·눈가 0.68·입술 0.52. AC-05 기준: 얼굴 노멀 고주파 ≤ 0.0352(× 0.5)·> 0, 코·볼 > 이마·눈가, 거칠기 평균 056 ± 0.02(정반사 상한 유지).
  - 삼각형: LOD0 몸 8,638 + 헤어 최대 1,020 = 9,658, LOD1 2,468 + 395, LOD2 762 + 137, 클로즈업 5,480. → 헤어 상한 정정: **LOD0 헤어(셸 + 카드) ≤ 1,350**(셸 ≤ 950 · 카드 ≤ 400), LOD1 헤어 ≤ 500, LOD2 ≤ 230, 클로즈업 카드 ≤ 500(합계 ≤ 6k). 경계 수정으로 몸 삼각형이 늘면 그만큼 헤어 상한을 줄인다(몸 + 헤어 ≤ 10k가 판정).
- **T04 경계 (2026-11-05)**: 투영·재배치·재묶음 + 평면 안 Taubin 평활(표면에서 최대 4 mm 오프셋, 위치에만)로 이탈은 전부 ≤ 1.2 mm가 됐지만, 꺾임은 칼라 27°·축구화 36°·소매 21°로 남았다(계단이 아니라 목-승모근·발목 단면의 실제 곡률 × 정점 간격 15~20 mm). 사용자 결정: **칼라만 20°**(칼라 고리 정점 1.5배 — 짝수 번째 변에 중점 삽입, 약 +110 tris, 몸 감량 목표 LOD0 7,000 → 6,900으로 상쇄), 나머지 고리는 **계단 0·꺾임 ≤ 40°·이탈 ≤ 3 mm**. 스펙 FR-02·AC-01 수정. 계단 = 이웃 꺾임의 부호가 번갈아 바뀌고 둘 다 10°를 넘는 쌍.
- **수염 추가 (2026-11-05, 사용자 지적 "여전히 수염 있는 건 이상하다" → 스펙 FR-24 추가, 057에 포함 결정)**: smoke 캡처에서 ① 모낭 점 노이즈(0.35 + 0.65 × 점)가 소금·후추처럼 보이고 ② 수염이 목 아래 칼라까지 내려오며 ③ 입 둘레가 큰 타원으로 비어 가면 같고 ④ 턱수염 경계가 칼로 자른 듯함. 수정: `face.beard_mask` 재작성(볼 선 0.35·목 선 0.30 폭 흐림, 목 선 가운데 −2.55 d에서 턱선 따라 올라감, 입술만 비움, 턱·콧수염 짙고 볼 옅게), `skin` 수염 채널 = 밀도 × (0.88 + 0.12 × 아래로 늘인 결 노이즈), 셰이더 = 밀도의 부드러운 문턱(smoothstep, 폭 0.3) × 종류별 덮임(짧을수록 옅은 색조). 지표 `skin.beard_metrics`(056: 점 노이즈 0.164 · 입꼬리 0) → 0.024 · 0.77 · 울대 아래 0. 셰이프키 `Beard_*` 두께도 새 마스크를 따른다. 같은 캡처에서 이마 카드가 디더 점 박힌 사각형으로 보여 카드 알파를 거의 0/1로(경계 0.02), 앞머리 카드를 짧게 함(FR-10·12 범위).
- **smoke 캡처 확인 후 칼라 수정 (2026-11-05)**: ① 칼라 띠의 톱니는 UV가 아니라 **음영**이었다 — 굽기(`PlayerMeshBaker.WeldedNormals`)가 같은 위치 정점 법선을 각도 상관없이 평균해 수직 턱 면이 셸 경계 법선을 끌어당겼다. 수정: 생성기 턱 면이 셸 바깥 정점의 복제본을 쓰고(삼각형 수 그대로), 굽기에 스무딩 각 60°(자기 면 방향과 60° 넘게 꺾인 같은 위치 면은 빼고 평균) + `WeldedNormals_KeepsCreaseAbove60_SmoothsSeams` 테스트. ② 칼라 띠 매핑을 평면 거리 5 cm 안에서 **아핀**(v 높이 = Y1 + d)으로 — 비선형(min·lerp)이면 큰 삼각형 안 선형 보간 때문에 띠 경계가 꺾인다(메시 측정: 띠 경계 d = −19~−23 mm). ③ 칼라 평면 기울기가 056부터 **반대**(앞이 높고 뒤가 낮아 뒤가 등까지 파임)였다 — `COLLAR_TILT` +0.10(뒤가 조금 높고 앞이 낮음). 0.25 이상은 앞이 흉골 위 패임까지 내려가 고리가 패인다(측정 109~157°). ④ 칼라 고리만 정점 2배·Taubin 30회(오프셋 ≤ 8 mm)·남은 모서리(> 15°) 다듬기 → 칼라 17.9°. 소매·축구화는 056 설정(정점 적은 고리는 모서리 다듬기가 간격을 틀어 더 나빠짐). 몸 LOD0 8,674 + 헤어 ≤ 1,320 = 9,994.
- **E2E 장면 위치 (T14)**: 플랜의 `CollarHairCloseupScene.cs`·`HairCatalogScene.cs` 대신 056처럼 `PlayerScreen.cs`에 화면(`collar*`·`hair*`·`flicker*`)을 더했다(같은 리그·로딩 공유).
- **깜빡임 측정 (FR-22)**: 머리 둘레 12×12 px 프레임 간 밝기 차 평균. 노멀맵 켬(flickerNormal)/끔(flickerFlat) 비율로 본다 — 056 산출물 측정은 하지 않았다(빌드 두 벌 필요). 1차 측정: 세 프로필 모두 비율 1.00(밉맵 페이드로 원거리 노멀이 평탄).

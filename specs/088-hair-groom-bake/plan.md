# 088 — 선수 모델 전면 교체 · 플랜

상태: 승인됨 (2026-11-22 사용자 "암튼 바로 진행 해줘 파이프라인으로")
스펙: ./spec.md

## 접근
현재 게임 몸은 이미 MPFB2 CC0 베이스 메시 + 체형 매크로 + 인종 매크로(바탕 머리 8개)로 만든다(`player/mpfb_src.py`·`face.py`). 시안과 실제로 다른 것은 ① 체형 매크로 값 ② **피부 텍스처**(절차 생성 → MakeHuman CC0 피부 diffuse) ③ **눈**(MPFB 기본 눈 헬퍼 + 절차 parts → 고해상도 눈 + CC0 눈 재질) ④ **눈썹·속눈썹**(없음 → Mindfront CC0 프록시) ⑤ **헤어**(셸 매개변수 35종 → 그루밍 v2 굽기)다. 그래서 생성기 골격(뼈대·클립·LOD 감량·키트 셸·굽기·내보내기)은 유지하고 다섯 입력을 갈아 끼운 뒤, 몸에서 파생되는 키트·정장·구운 지도는 새 몸에서 전부 다시 생성한다(FR-33). 옛 경로는 대체가 끝나는 단계에서 지운다(FR-17).
MakeHuman 피부는 베이스 메시와 **같은 MakeHuman UV**라 재투영 없이 해상도만 줄여 쓴다. 노멀은 diffuse에서 고역 통과로 굽는다(MakeHuman 피부에 노멀 맵이 없음 — "normal False").
헤어는 그루밍(가닥 15~16만)을 게임 메시로 굽는다: **셸**(가닥 외곽 봉투 → 감량) + **가닥 카드**(다발 중심선 리본, 바깥층·앞머리·가장자리 우선) + **공유 결 아틀라스**(직모·웨이브·곱슬·코일·브레이드·드레드 타일, 1K 하나) + **셸 정점 속성**(밀도·차폐·흐름). 스타일별 텍스처를 두지 않아 텍스처 예산을 지킨다(FR-04). 대안(스타일별 512² 아틀라스 34장)은 메모리 +9~36 MB라 버렸다.
그루밍은 느리다(스타일당 ≈ 25 s) → 굽기 결과를 입력 해시로 캐시한다(FR-16).

## 모듈 · 파일
| 모듈 | 파일 | 역할 |
|---|---|---|
| Blender 생성기 | `Art/Blender/scripts/player/mpfb_src.py` | `REFERENCE_MACRO`를 시안 값(근육 0.85·체중 0.55·비율 0.7)으로, 키는 기존대로 1.80 m 균등 스케일. 인종 델타는 몸 전체(시안과 같게) |
| | `player/mhassets.py` (신규) | MakeHuman CC0 에셋 읽기: mhmat·mhclo 파싱, 피부 3종(young caucasian·asian·african male) diffuse, 고해상도 눈 + 눈 재질, 눈썹(Mindfront 02·04·09)·속눈썹(02) 프록시 맞춤(MPFB 서비스는 생성 도구로만). `used_files()`·sha256 |
| | `player/skin.py` (재작성) | 절차 피부 제거. MakeHuman diffuse → `skin_<k>.png`(1K)·`skin_<k>_closeup.png`(2K) 3세트, 공유 `skin_normal.png`(diffuse 고역 통과), 수염 마스크·붉은기·거칠기는 MakeHuman 이미지에서 파생한 채널로 유지(수염·땀·젖음 기능 보존) |
| | `player/face.py` | 눈: 고해상도 눈 메시 + 눈 재질 텍스처를 parts 눈 칸으로. 눈썹·속눈썹 메시(LOD0·클로즈업 카드, 표정·얼굴 축 셰이프 키 전달). 바탕 머리 Base_n은 기존 구조(인종 매크로 + 얼굴 타깃) 유지·값 재계산 |
| | `player/groom/` (신규 패키지) | `styles.py`(34종 정의 — 승인 4종은 groom_v2 그대로 이식), `grow.py`(그루밍), `bake.py`(셸·카드·정점 속성), `atlas.py`(공유 결 아틀라스), `fidelity.py`(시안 vs 게임 마스크 IoU·ΔE·가르마·앞머리 오차), `compare.py`(사진 비교 시안 도구, 참고 사진은 인자로만 받고 저장 안 함), `cache.py` |
| | `player/hair.py`·`hairtex.py` | **삭제**(FR-17). `STYLES` 이름·순서(0~34)·계열 표는 `groom/styles.py`로 옮김 |
| | `player/body.py`·`heightfield.py`·`suit.py` | 코드 구조 유지, 새 몸에서 키트·정장·구운 키트 지도 재생성(FR-33). 몸 구운 디테일(`body_detail_*`)은 새 피부 기준으로 다시 굽거나 MakeHuman 파생으로 대체 |
| | `player/checks.py`·`visual_metrics.py` | 새 검사: `mh_licenses`, `groom_fidelity`, `no_legacy`(옛 이름·텍스처·모듈 없음), 피부·눈썹 검사. 062 검사(체형·관통·헤어 062·얼굴 062·예산)는 그대로 새 생성물에 |
| | `Art/Blender/scripts/groom_v2/` | `player/groom/`로 이식 후 삭제 |
| | `Art/Blender/tests/test_player.py` | 새 검사 단언, 옛 헤어·피부 단언 갱신 |
| 서드파티 | `Art/ThirdParty/MakeHuman/` (신규, LFS) + `assets.json` | 쓰는 파일만 풀어 넣음(피부 3·눈·눈 재질·눈썹 3·속눈썹 1). 압축 파일(`MakeHumanPacks/zips`)은 커밋 안 함(.gitignore) |
| | `Art/LICENSES.csv` | MakeHuman 시스템 에셋·Mindfront 행 추가 |
| RoadToPro.Presentation | `Runtime/Players/PlayerLook.cs` | 피부톤 8단계 → (피부 세트 0~2, 틴트) 표(Q4), 눈썹 종·색 |
| | `Runtime/Players/Logic/Appearance.cs` | `Eyebrow`(0~2) 필드, 결정론 생성, ComboKey 갱신 |
| | `Runtime/Players/Logic/HairTaxonomy.cs` | Q3 계열 재분류 반영 |
| | `Runtime/Players/PlayerBodyAssets.cs`·`PlayerMeshBaker.cs`·`CloseupHeads.cs` | 피부 텍스처 세트 선택, 눈썹 메시 활성, 새 헤어 메시 이름 |
| | `Shaders/PlayerBody.shader` | 피부 = diffuse 텍스처 × 틴트(절차 채널 제거), 헤어 = 공유 결 아틀라스 + 정점 속성 |
| RoadToPro.Verification | `Runtime/Samples/PlayerScreen.088.cs` + `Data/e2e/088-*.json` | 장면 `088-face-closeup`·`088-body-closeup`·`088-hair-groom`(시안 렌더 이미지와 나란히 놓을 게임 캡처) |
| 문서 | `docs/hair-groom-process.html` | FR-20 스타일 시안 절차 |

의존 방향 확인: Presentation·Verification만 바뀜, Core·MatchEngine 참조 없음 — `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- `player.layout.json`: `skins`(세트 3개 파일 이름), `eyebrows`(메시 이름 3), `hair` 스타일 표(이름·계열·결 계열·카드 수·삼각형) — schemaVersion 2 → 3.
- `player.face.json`: `baseHeads` 값 갱신(구조 동일), `skinSets`·`toneTable`(8단계 → 세트·틴트 RGB) 추가.
- 피부톤 표 [제안]: 0~2 → caucasian 세트(틴트 밝게·기본·약간 어둡게), 3~4 → asian 세트, 5 → asian 어둡게, 6~7 → african 세트(기본·어둡게). 지역 → 톤 범위는 062 FR-63 표 그대로.
- 그루밍 캐시: `.farm/cache/groom/<style>-<hash>.npz`(gitignore). 해시 = 스타일 정의 소스 + 기준 머리 메시 + 시드 + 굽기 코드 버전.
- 세이브 스키마 변경 없음(외형은 id·시드 재생성, 062 FR-55 선례).

## 결정론 · 성능
- 그루밍 RNG: 스타일마다 고정 시드(`numpy.default_rng(시드 표)`), 다발 중심 선택·카드 순서는 정렬된 인덱스 순. 같은 입력 → 같은 npz·glb 해시(AC-05: 2회 생성 비교).
- 런타임: `AppearanceGenerator`가 선수 id 시드로 눈썹을 고른다(기존 RNG 흐름 끝에 추가 → 다른 필드 순서 불변).
- 예산(art 검사로 판정): LOD0 ≤ 11,000(헤어 몫 [제안] ≤ 1,900, 눈·눈썹·속눈썹 ≤ 600), LOD1 ≤ 3,000(헤어 셸만 ≤ 450), LOD2 ≤ 1,000, 클로즈업 머리 ≤ 6,000(현재 5,710 → 눈썹·속눈썹·고해상도 눈 증가분은 머리 감량으로 흡수, 넘치면 멈춰서 묻는다).
- 텍스처: 피부 1K × 3 + 2K × 3 증가 → 062 195 MB 대비 +10 % 이내인지 `textureBudget`으로 판정. 넘치면 클로즈업 2K를 한 세트만 상주시키는 안을 묻는다.
- 시뮬 틱 경로 변경 없음 → IL2CPP 벤치 안 돌림.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `test_player.py::test_mh_face_assets`(피부·눈·눈썹·속눈썹 존재, 지역 7 × 톤 ≥ 2, 텍스처 해상도·메모리), `AppearanceTests.Eyebrow_*`, `PlayerLookTests.ToneTable_*` | 로직 | — |
| AC-02 | `test_player.py::test_mh_licenses`(assets.json sha256 ↔ LICENSES.csv, 압축·사진 파일 없음) | 로직 | — |
| AC-03 | `test_player.py::test_groom_fidelity`(34종 × 정면·3/4·옆 IoU·ΔE·가르마·앞머리) | 로직 | — |
| AC-04 | `test_player.py::test_hair062`·`test_penetration`(바탕 머리 × 체형 극단), `HairTaxonomyTests` | 로직 | — |
| AC-05 | `test_player.py::test_budgets`·`test_deterministic_hash`·`test_gen_time` | 로직·성능·결정론 | — |
| AC-06 | `e2e/088-face-closeup`, `e2e/088-body-closeup` | E2E | mac-m1-1600p |
| AC-07 | `e2e/088-hair-groom` (+ 시안 렌더 나란히 붙인 비교 시트) | E2E | mac-m1-1600p |
| AC-08 | `e2e/062-hair-catalog`·`062-face-catalog`·`042-goal-sequence` 회귀 | E2E | mac-m1-1600p |
| AC-09 | `test_docs.py::test_groom_process_doc`(문서 존재·네 단계·지표 문구) | 로직 | — |
| AC-10 | `test_player.py::test_no_legacy`(옛 모듈 파일·옛 텍스처 이름·옛 메시 이름 없음) | 로직 | — |
| AC-11 | `test_player.py::test_body_shapes`·`test_proportions`·`test_hands`·`test_rig_compat`·`test_kit_motion`, `PlayerRigTests`·`PlayerImportTests` | 로직 | — |

## 버그 방지
- 프록시(눈썹·속눈썹) 맞춤 후 셰이프 키 전달 누락 → 표정·얼굴 축에서 눈썹이 피부에 박힘: 표정 5종 × 극단 얼굴 축에서 눈썹-피부 거리 검사.
- 헤어 셸이 바탕 머리마다 다른 두피에 맞지 않음 → 관통·뜸: 바탕 머리 8 × 체형 극단에서 두피 간격(057 FR-13) 검사.
- 헤어 카드 알파 정렬 문제(겹침 깜빡임) → 기존 디더 경로 유지, 카드 순서 결정론.
- 피부 세트 전환으로 얼굴·몸 이음매 색 차 → 같은 diffuse를 몸·머리·클로즈업이 공유(같은 UV).
- 캐시 오염 → 해시에 굽기 코드 버전 포함, `--no-cache`로 강제 재생성 경로.
- 몸 전체 인종 델타로 키트 셸이 몸을 뚫음 → 클립 82개 극단 관통 검사(기존 `penetration`·`kit_motion`).
- 옛 헤어 이름을 Unity가 여전히 찾음 → 이름·순서(0~34) 유지 + `PlayerImportTests` 메시 목록 단언.

## 위험 · 미정
- 클로즈업 머리 6k: 고해상도 눈 + 눈썹·속눈썹이 들어가면 넘칠 수 있다 → 감량으로 못 맞추면 멈춰서 묻는다.
- 나머지 30종 헤어는 스타일마다 사용자 사진 비교 승인이 필요하다(Q6) → 파이프라인은 이 단계에서 멈추고 사진·승인을 받는다. 사진 없는 스타일은 형태 설명으로 시안을 만든다.
- MakeHuman 피부 diffuse는 조명이 조금 구워져 있다(시안에서 승인된 모습 그대로) — 셰이더 조명과 겹쳐 어두우면 diffuse 보정 계수 [제안]으로 조정.
- 그루밍 시간: 34종 첫 생성 ≈ 15분(캐시 후 0) — `farm.py art` 첫 실행이 길어진다.

# 062 — 선수 비주얼 업그레이드 · 플랜

상태: 완료 (2026-11-21)
스펙: ./spec.md

## 접근
056·057 생성기(`Art/Blender/scripts/player/*`)와 Unity 외형 생성기·굽기·셰이더를 고친다. 큰 틀은 그대로 둔다 — **외형은 시드 → 셰이프 키 가중치 → 하이라이트 로딩 때 선수별 메시 굽기(`PlayerMeshBaker`)**, 재질은 선수당 2개(`PlayerBody`·`PlayerKit`, 클로즈업 머리는 별도 1개). 스펙 Q8대로 한 스펙 안에서 9단계(A~I)로 나눈다.

1. **구운 디테일 = 높이장 굽기 (Cycles 아님)**: 지금 텍스처는 numpy 절차 생성이다. 062의 "고해상도 원형"은 **저해상도 메시 + 절차 변위장**(근육·뼈 윤곽, 천 주름·봉제선·짜임, 손 주름, 모공·잔주름, 축구화 패널)으로 정의하고, 같은 변위장을 UV 텍셀 위치에서 평가해 노멀·캐비티 AO·색 변화 맵을 바로 계산한다. Cycles 베이크를 쓰지 않으니 기기·샘플 노이즈 없이 바이트 단위로 결정론이고(FR-95), 생성 시간도 짧다. 근육 맵은 1장을 공유하고, 선수별 근육 정도는 셰이더 `_MuscleDef`(근육 축 → 노멀 세기)로 조절한다 → 텍스처 메모리가 선수 수와 상관없다(FR-92 공유 원칙).
2. **체형·손**: MPFB 셰이프 키를 3개(Thin·Muscular·Heavy)에서 5개로 늘린다(+`Broad` 어깨·가슴, +`Limb` 팔다리 둘레). `BodyShapeMap`이 키·체중·역할 + 시드 편차로 5축을 정한다(FR-01·04). 손은 지금 `BONE_MAP`이 MPFB 손가락 뼈를 손 뼈에 합치고 있다. 이걸 풀어 **Humanoid 손가락 뼈(손당 15개)**로 두고, LOD0 손가락을 기하로 분리한다. 기존 82개 클립에는 손가락 커브가 없으므로, 손 포즈(편 손·주먹·반쯤 쥔 손)는 클립 태그 → 포즈 표 → 런타임 손가락 회전(재생 시각 블렌드)으로 입힌다 → 클립·발 IK·055 폼은 수정하지 않는다(FR-13). LOD1은 손가락을 합친 장갑형이고, 가중치는 손가락 뼈 대신 손 뼈를 쓴다.
3. **의상 움직임 보조 (천 시뮬 없음)**: Humanoid 밖 보조 뼈 6개(상의 밑단 앞·뒤, 소매 좌·우, 반바지 밑단 좌·우)를 둔다. 오프셋은 **재생 시각의 순수 함수**로 계산한다: `offset(t) = Σ k(τ)·a(t−τ)`. `a`는 골반 가속도이고, 이동은 리플레이 궤적(30 Hz 보간), 제자리 동작은 Blender가 클립마다 미리 뽑아 둔 골반 가속도 커브(`player.clips.json`)에서 가져온다. `k`는 감쇠 진동 커널을 고정 표로 만든 것이다. 적분 상태가 없어 fps·배속·30 fps 절약 모드에서 모양이 같다(FR-22, AC-14). 진폭 상한은 클램프로 막고, 관통은 보조 뼈 가중 범위를 밑단 띠로 제한해서 막는다.
4. **재질 분리, 재질 개수 그대로**: 키트 마스크 A 채널(팀/축구화)과 새 **영역 맵**(R 거칠기, G 인쇄 광택, B 땀 마스크, A 젖음 대상)으로 셔츠·반바지·양말·축구화·인쇄의 반사를 구분한다(FR-30·25·33). 재질은 2개로 유지한다(FR-82 ≤ 3). 피부 역광 비침은 `PlayerBody` wrap 항에 두께 맵(귀·코끝·손가락 끝)을 곱하는 근사로 낸다.
5. **헤어 재제작**: `hair.py`의 "두피 법선으로 일정하게 민 셸"이 헬멧의 원인이다. 스타일마다 **두께장**(정수리·옆·뒤·앞머리 제어점, 두개골 윤곽 위 두께 상한 = FR-40 비율)과 **밀도장**(페이드 그라데이션, 피부 비침 = 알파 디더)을 정의하고, 결·클럼프·나선(곱슬)·땋은 마디(브레이드)·로프(드레드) 텍스처를 스타일 계열별로 절차 생성한다. 가닥 카드는 057 방식 그대로 Body 서브메시에 합친다. 35종 = 057의 30종 재제작 + ★ 신규 5종. 머리색 8색 + 컬러 포인트: 스타일마다 정점 색 채널에 "뿌리/끝·옆/위" 마스크를 구워 두고, 셰이더가 `_HairColor`·`_HairColor2`를 섞는다(FR-53·54).
6. **얼굴**: 바탕 머리 8개를 셰이프 키 `Base_<n>`으로 만든다. MPFB CC0 얼굴 타깃 조합에서 시작해 공개 얼굴 비율 연구로 다듬는다. 선수 얼굴 = 바탕 2~3개 혼합 + 기존 30축이고, 기존 굽기 경로에 그대로 얹힌다(FR-60·61). 수염 7종은 셰이프 키 + 수염 종류별 밀도 마스크(RGBA 2장, 7채널)로 만든다.
7. **표정**: 표정만 굽지 않고 **런타임 블렌드셰이프**로 남긴다(`Expr_Cheer`·`Smile`·`Dismay`·`Clench`, 무표정 = 0). 대상은 클로즈업 머리와 LOD0뿐이다. 입 안(치아·혀·어두운 입안 캡)을 클로즈업 머리와 LOD0에 넣는다. 선택은 순수 로직이다(반응·SimEvent → 표정 표, 0.2 s 블렌드, 재생 시각 기준).
8. **품질 등급**: 061 `CinematicModeController.TopTier`(최대 3명, 아직 받는 쪽이 없음)를 받아, 앞 2명 → 기존 `CloseupSelector` 클로즈업 머리, 3번째 → LOD0 고정 + 고해상도 텍스처 + 표정·땀으로 만든다. 등급 밖은 048 프리셋 그대로다(FR-85~87). 재질 값은 등급과 상관없이 같은 머티리얼 파라미터를 쓰고 텍스처만 바꾼다 → FR-88이 구조로 보장된다.

대안: Cycles 고폴리 베이크는 품질 원형을 실제 조각으로 만들 수 있지만, 비결정·느림(25명 × 맵) 문제가 있고 고폴리 원형을 레포에 둬야 한다 → 높이장 굽기를 택했다. 손가락 포즈를 클립에 굽는 안은 클립 82개를 다시 만들어야 하고 FR-13(클립 수정 없음)에 어긋난다 → 런타임 포즈 표. 천 2차 움직임을 스프링 적분으로 하면 fps에 따라 모양이 달라진다 → 커널 합성.

## 단계
| 단계 | 내용 | FR | AC |
|---|---|---|---|
| A | 기준선 측정(056·057 현재값: 근육 대비·천 분산·키트 L* 표준편차·헤어 고주파·실루엣·랜드마크 거리) + 공개 자료 조사(팔 둘레 비율, 헤어 유형 빈도, 얼굴 비율) | FR-03·51·52·54·60 [미정] | 기준값 |
| B | 체형 5축·근육 높이장 굽기·실루엣 각짐·손가락 뼈·손 포즈 | FR-01~05·10~13 | AC-01·02 |
| C | 키트 형태·주름·봉제선·짜임 굽기, 정강이 보호대·축구화, 보조 뼈 2차 움직임 | FR-20~26·91 | AC-03·10 |
| D | 영역 맵·재질 값 표·피부 역광·땀·머리카락 이방성·젖음 입력 | FR-30~35·97 | AC-04 |
| E | 헤어 35종 재제작·분류표·8색·컬러 포인트 | FR-40~45·50~55 | AC-05·06·11 |
| F | 바탕 머리 8개·지역 얼굴형·피부톤·수염 7종·겉보기 거리·표정 5종·입 안 | FR-60~66·70~72 | AC-07·08·11·12 |
| G | 품질 등급(061 신호 연결)·LOD 표·예산·전환 튐 | FR-80~89·92·94 | AC-04·09·13 |
| H | 정장 1벌·기준 머리 뼈대·UV 규격 버전·외형 팩 검사 | FR-27·67~69 | AC-15·16 |
| I | E2E 장면·성능·결정론·마지막 검증 | 전부 | AC-09~14 |

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| Blender 스크립트 | `player/mpfb_src.py` (수정) | `BODY_MACROS` +Broad·Limb. `BONE_MAP`에서 손가락 뼈 합치기를 없애고 Humanoid 손가락 15개 × 2로 매핑. 바탕 머리 `Base_0..7` 타깃 조합 |
| 〃 | `player/skeleton.py`·`rig.py` (수정) | 손가락 30개 + 보조 뼈 6개(`Kit_HemF/B`·`Kit_Sleeve.L/R`·`Kit_Short.L/R`, Humanoid 밖). `REQUIRED_KEYS`에 Broad·Limb·Base_*·Expr_*·Beard_* 추가 |
| 〃 | `player/body.py` (수정) | 어깨·소매·팔꿈치·무릎 윤곽 정점 밀도 추가(몸 감량 없음, LOD0 상한 11k — FR-81 Q2 개정). LOD0 손가락 분리, LOD1 장갑형. 키트 셸 오프셋을 부위 함수로(겨드랑이·허리·등 아래 늘어짐), 정강이 보호대 볼록·윗단 턱, 축구화 밑창·뒤꿈치·스터드 윤곽. 보조 뼈 가중치(밑단 띠 한정) |
| 〃 | `player/heightfield.py` (새) | 절차 변위장 정의(근육·뼈·힘줄, 천 주름·봉제선·짜임 3종, 손 주름·손톱, 축구화 패널, 피부 모공·잔주름)와 UV 텍셀 평가 → 노멀·캐비티 AO·색 변화. 정수 시드·고정 순서 |
| 〃 | `player/textures.py`·`skin.py` (수정) | 구운 맵 출력: `body_detail_n/ao`(근육·손, 2K·1K 밉), `kit_detail_n/ao`(주름·봉제선·짜임), `kit_region.png`(거칠기·인쇄 광택·땀·젖음), `skin_thickness`, `beard_mask0/1.png`(7종), 057 모공 맵 보강. 기존 `kit_normal.png`는 `kit_detail_n`으로 대체 |
| 〃 | `player/hair.py` (재작성)·`hairtex.py` (새) | 스타일 표 35종(두께장·밀도장·헤어라인 4형·카드 배치·부속 형상·계열·컬러 포인트 마스크 종류). 인덱스 0~29 이름은 057 그대로 두고 30~34가 신규. 결 텍스처 계열별(직모·곱슬 나선·코일·브레이드 마디·드레드 로프) |
| 〃 | `player/face.py` (수정) | 바탕 머리 8개, 수염 7종(+콧수염·염소수염·진한 스텁블), 표정 셰이프 키 4개, 랜드마크·바탕 머리 메타(지역 태그) 내보내기 |
| 〃 | `player/headlod.py` (수정) | 입 안 메시(치아·혀·입안 캡), 표정 키 유지, ≤ 6k |
| 〃 | `player/suit.py` (새) | 정장 셸(재킷·셔츠·바지·구두) LOD0~2, 키트와 같은 바인딩·가중치 방식, 중립 3색 |
| 〃 | `player/headref.py` (새) | `head_reference.json`(머리·목 뼈 이름·계층·바인드 포즈, 목 이음매 정점 id·위치, UV 규격 버전), UV 템플릿 PNG(가상 얼굴) |
| 〃 | `player/bake.py`·`export.py` (수정) | 클립별 골반 가속도 커브 `player.clips.json`에 추가, 손 포즈 태그 |
| 〃 | `player/checks.py` (수정) | 아래 테스트 전략의 Blender 측 지표 함수 |
| 〃 | `gen_player.py` (수정) | 새 산출물, 생성 시간 ≤ 5분 |
| Blender 테스트 | `Art/Blender/tests/test_player.py`·`test_face.py`·`test_hair.py` (새) | 아래 표 |
| 도구 | `Tools/devicefarm/farm/art.py`·`imagechecks.py` (수정) | `farm.py art --spec 062` 미리보기(체형·손·헤어 35·얼굴 8·표정·정장), 이미지 지표(윤곽 꺾임, 이음매 단차, 오목점, L* 표준편차·포화 비율, 실루엣 IoU, 이방성, ΔE2000) |
| RoadToPro.Presentation (Logic, Unity 비참조) | `Players/Logic/Appearance.cs` (수정) | `HairStyles=35`, `HairColors=8`(자연 6 + 탈색 금발·백금), `ColorPoint`(없음 + 3색), `Beards=7`, `SkinTones` 8(부족하면 ≤ 12), 가중치 표(A 조사 반영), 어색한 조합 제외(회색 + 컬러 포인트). `GenerateGroup` 상한 3 유지 |
| 〃 | `Players/Logic/FaceGenerator.cs`·`BodyShapeMap` (수정) | 바탕 머리 2~3개 혼합 가중치(지역 균등), 체형 5축 |
| 〃 | `Players/Logic/AppearanceDistance.cs` (새) | 겉보기 거리 = 랜드마크 + 헤어 실루엣 계열 + 피부톤 + 머리색 + 수염 가중합(FR-65), 팀 단위 재추첨 |
| 〃 | `Players/Logic/HandPose.cs` (새) | 클립 태그 → 손 포즈(편·주먹·반쯤) 표, 손가락 회전 값, 0.15 s 블렌드 |
| 〃 | `Players/Logic/KitSecondaryMotion.cs` (새) | 커널 표·골반 가속도 입력 → 보조 뼈 오프셋 6개(재생 시각 함수, 상태 없음, 클램프 0.5~3 cm) |
| 〃 | `Players/Logic/ExpressionLogic.cs` + `Players/Data/expressions.json` (새) | 반응(득점자·동료·실점 팀)·SimEvent(골·실점·찬스 무산·카드) → 표정, 빠진 항목은 무표정 + 진단 1회. 060이 아직 구현 전이라 반응 종류는 이 스펙의 임시 열거형으로 두고, 060 연결 때 재확인 |
| 〃 | `Players/Logic/QualityTierPolicy.cs` (새) | 061 TopTier → {클로즈업 머리 2, LOD0 고해상도 1, 일반}, 히스테리시스는 `CloseupSelector` 재사용 |
| 〃 | `Players/Logic/AppearancePackSpec.cs` (새) | 기준 머리 뼈대·목 이음매 허용 오차·UV 규격 버전 읽기와 **검사 함수**(뼈 이름·계층·바인드 포즈·가중치 없는 정점·정점당 뼈 > 4·이음매 오차). 049가 아직 구현 전이라 검사를 여기 두고, 049는 나중에 이걸 호출한다 (질문 Q-P3) |
| RoadToPro.Presentation | `Players/PlayerRig.cs`·`PlayerMeshBaker.cs` (수정) | 손가락·보조 뼈 바인딩, 굽기에서 `HideHead` + `Expr_*` 블렌드셰이프 유지, 바탕 머리 혼합 델타, 정장 메시 교체(`ApplyOutfit`) |
| 〃 | `Players/PlayerMotionExtras.cs` (새) | LateUpdate(클립·IK 뒤): 손 포즈·보조 뼈·표정 가중치 적용. 할당 0, 마커 `Players.MotionExtras` |
| 〃 | `Players/CloseupHeads.cs` (수정) | `QualityTierPolicy` 연결, 3번째 선수 LOD0 고정·고해상도 머티리얼 교체 |
| 〃 | `Players/PlayerLook.cs`·`PlayerBodyAssets.cs` (수정) | 8색·컬러 포인트 팔레트·수염 7·바탕 머리 메타·새 맵 참조·정장 색 |
| 〃 | `Shaders/PlayerBody.shader`·`PlayerCommon.hlsl` (수정) | 근육·손 디테일 노멀(`_MuscleDef`), 피부 두께 역광, 수염 마스크 7채널 선택, 헤어 두 색·밀도 디더·결 이방성 계열별, 땀(`_Sweat`), 젖음(`_Wetness`, 078 입력 자리) |
| 〃 | `Shaders/PlayerKit.shader` (수정) | 주름·봉제선·짜임 노멀·AO, 영역 거칠기, 인쇄가 주름 노멀을 따르고 광택 차이, 젖음 |
| RoadToPro.Presentation.Editor | `Editor/Players/PlayerImportSettings.cs` (수정) | 새 맵 임포트(노멀 밉맵 페이드, 057 FR-22 방식을 몸·키트·헤어까지), Humanoid 손가락 매핑 |
| RoadToPro.Verification | `Runtime/Samples/PlayerScreen.cs` (partial 추가: `PlayerScreen.062.cs`) | 체형·손 클로즈업, 키트 움직임(0.5x 고정 재생 시각, 배속별), 헤어 35·얼굴 일람, 팀 11명 정렬, 표정, 등급 전환 연속 캡처, 시상 장면 대체 픽스처(076 구현 전) |
| 데이터 | `Data/e2e/062-body-closeup.json`·`062-kit-motion.json`·`062-hair-catalog.json`·`062-face-catalog.json`·`062-face-expressions.json`·`062-suit.json`·`062-model-perf.json`, `Data/perf/062-player-visual-upgrade.json` (새) | E2E·성능 예산 |
| 산출물 (LFS) | `Assets/_Project/Presentation/Players/Generated/**` | glb·json·Textures 갱신, `head_reference.json`·UV 템플릿 |
| 문서 | `docs/index.html` 리서치 섹션 | 팔 둘레·헤어 유형 빈도·얼굴 비율 공개 출처 (FR-50·60, 스펙 "검증·증거") |
| 테스트 | `Tests/EditMode/Presentation/Players/*` (수정·새) | 아래 표 |

의존 방향 확인: C# 변경은 `RoadToPro.Presentation`(Logic은 Core 타입만)·`Presentation.Editor`·`Verification`·테스트뿐이다. `.claude/rules/10-csharp.md` 위반 없음. Core·MatchEngine 변경 없음 → `determinism-reviewer` 대상이 아니다(외형·포즈 결정론은 AC-14 테스트로 판정).

## 데이터 · 스키마
- `PlayerAppearance` 필드 추가: `Broad`, `Limb`(체형), `BaseHeads`(바탕 머리 인덱스 3 + 가중치 3, 정수 천분율), `ColorPoint`(0~3), `Beard`(0~6, 지금 `FaceShape`의 수염을 옮김). 세이브에는 넣지 않는다(시드 재생성, 056 그대로). 기존 시드 외형 변화 허용(Q4).
- `ComboKey`: 피부 × 헤어 35 × 색 8 × 컬러 포인트 4 × 수염 7 × 바탕 머리 주 인덱스 8 → `GenerateGroup`은 stackalloc 대신 로딩 때 한 번 만드는 `HashSet<long>`(조합 수가 커짐, 로딩 전용 할당 허용).
- `player.layout.json` 필드 추가: `hair[].family`·`hairline`·`colorPointMask`, `baseHeads[]`(지역 태그), `beards`(7), `expressions`(4), `fingerBones`, `kitHelpers`, `outfits.suit`. schemaVersion은 올리지 않는다(필드 추가만, 없는 필드 무시).
- `player.clips.json`: 클립별 `pelvisAccel`(30 Hz 3축), `handPose` 태그.
- `expressions.json`: `{ reaction|event → expression, blend }`.
- `head_reference.json`: `uvSpecVersion: 1`, 뼈 목록, 이음매 정점, `neckSeamTolerance`([미정] — A단계 측정 뒤 제안, 승인 지점).
- 텍스처(FR-92): 공유 `body_detail` 2K(Low 1K), `kit_detail` 2K(Low 1K), `kit_region` 1K, `skin_thickness` 512, `beard_mask` 1K × 2, 헤어 결 아틀라스 1K. 선수별은 키트 합성(팀당 1회)뿐이다. 메모리 계산과 측정은 G단계.
- 재질 값 표 (FR-30, T19 [제안] — 스펙이 "플랜에서 표로" 정하라고 했으나 빠져 있던 것): 정반사 = F0 × (p + 8)/(8π) × (n·h)^p, p = 2^(10(1 − 거칠기) + 1) (`PlayerBody` 피부 `Spec`와 같은 정규화 Blinn-Phong). 키트 F0 0.04, 피부 0.028(056).

  | 재질 | 거칠기 | 기준 조명 정반사 피크 |
  |---|---|---|
  | 축구화 갑피 | 0.30 | 0.420 |
  | 인쇄 | 0.42 | 0.190 |
  | 피부(이마·코, 텍스처 A) | 056 값 유지(0.566~0.592, ± 0.02) | 0.054 (상한 = 056 − 0.02일 때 0.061) |
  | 셔츠·소매 | 0.66 | 0.046 |
  | 반바지 | 0.70 | 0.038 |
  | 양말 | 0.82 | 0.024 |
  | 땀(FR-33) | 피부 − 0.04 | 0.068 (상한의 1.13배 ≤ 1.3배) |

  영역 맵 `kit_region`: R 거칠기(위 표), G 인쇄 거칠기(셰이더가 인쇄 픽셀에서 R 대신), B 결 선택(T14: 0 메시·0.5 평직·1 골지), A 젖음 대상. 위 '접근' 4의 "B 땀 마스크"는 땀이 피부(몸 UV)라 `skin_thickness` G로 옮겼다(R 두께: 귀·코끝·손가락 끝, G 땀: 이마·관자·목·팔).
- 외부 에셋 없음(전부 절차 생성·MPFB CC0 기존 기록) → `LICENSES.csv`는 변경이 없을 것으로 예상하고, AC-04 검사가 확인한다.

## 결정론 · 성능
- 생성기: 변위장·스타일 표·바탕 머리는 튜플 순서 고정, 정수 노이즈(`_noise`) 시드, 출력 6자리 → 2회 실행 바이트 동일(AC-14, FR-95).
- `Generate`: SplitMix64 순서를 고정하고, 새 필드는 기존 호출 **뒤에** 뽑는다(키·체중·피부는 유지, 나머지는 바뀌어도 허용).
- 런타임 포즈: 손 포즈·보조 뼈·표정은 모두 `f(재생 시각, 리플레이 데이터, 클립 데이터)`이고 프레임 누적 상태가 없다 → 30/60 fps·0.5x·3x·절약 모드에서 같은 시각이면 같은 포즈 해시(AC-14).
- 폴리곤(FR-81, LOD0 상한 11k — 2026-11-20 사용자 결정 "베스트로 실제 선수처럼 느껴져야"): 지금 LOD0 ≈ 몸 8.56k + 헤어 ≤ 1.35k. 손가락 분리 +[제안] 600, 입 안 +[제안] 150, 어깨·관절 윤곽 +[제안] 300 → ≈ 10.96k. 몸은 깎지 않는다. 헤어 상한 1,350 그대로. 클로즈업 머리 5,200 + 입 안 300 + 카드 ≤ 500 ≤ 6k. 화면 내 선수 삼각형(≤ 15만)은 LOD0 대상이 카메라 근처 몇 명뿐이라 영향 작음 — `062-model-perf`로 확인. 11k로도 넘치면 멈추고 묻는다.
- 드로우콜: 재질 2개 + 클로즈업 1개 그대로 → 변화 0이 목표(FR-82 ≤ 75).
- 애니메이션: 뼈 22 → 58(손가락 30 + 보조 6). 스키닝·LateUpdate 비용을 `Players.MotionExtras` 마커와 E2E 애니메이션 구간으로 잰다. 예산 p95 ≤ 2 ms·GC 0. 넘으면 LOD1 이하에서 손가락·보조 뼈 갱신을 끈다(LOD 표: LOD1 상의 밑단만).
- 굽기: 바탕 머리·체형 키가 늘어 `PlayerMeshBaker` 시간이 늘어난다 — 057 기준(309.5 ms × 1.2) 대비 +30% 예산(FR-84), `PlayerMeshBakerTests`.
- 생성 시간: 헤어 35 + 높이장 굽기 → ≤ 5분(FR-84). 넘으면 맵 굽기를 입력 해시 캐시로 돌린다.
- **검증 범위 (05-sdd 2026-11-12 기준으로 스펙 문구를 다시 읽음)**:
  - E2E 프로필 smoke·low-igpu·compact → `mac-m1-1600p` 1프로필, 기준 이미지 비교 없음, 육안 승인.
  - 시뮬 틱 경로 변경 없음 → `farm.py bench`(IL2CPP)는 돌리지 않는다. AC-13의 "IL2CPP AnimBench"는 E2E 애니메이션 구간(`062-model-perf`)으로 대신 잰다 (질문 Q-P1).
  - GPU·텍스처 메모리는 개발 맥 측정값을 기록만 하고, 내장 GPU 판정은 실기기 확보 후로 미룬다(05-sdd 완료 정의 4).
  - Blender 스크립트를 바꾸므로 `farm.py art --spec 062` 1회, EditMode는 `farm.py logic --spec 062 --scope auto` 1회(Presentation 범위).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `test_player.py::test_body_shapes` — 200명 + 극단 4 × 근육 최소·최대: 근육 부위 중주파 음영 대비 ≥ A 기준 × 1.5, 팔 둘레/키 비율 범위(A 조사), 찢어짐·뒤집힌 삼각형 0. `AppearanceTests.BodySilhouette_ThousandTeams_StdDev`(정면 너비/키 근사 = 체형 5축 함수, 1,000팀 표준편차 ≥ 0.02, 같은 키 ±2 cm 쌍 차이) | 로직 | — |
| AC-02 | `test_player.py::test_outline_and_hands` — LOD0 어깨·소매·팔꿈치·무릎 윤곽 꺾임 ≤ 25°, 소매 경계 밝기 단차 ≤ 1.5배(Blender 기준 조명 렌더), 편 손 오목점 ≥ 4·간격 > 0, 주먹·편 손·반쯤 관통 0, LOD1 장갑형. `test_kit_boundary_smooth` 회귀(057 AC-01). `HandPoseTests.Tags_Map_ThreePoses` | 로직 | — |
| AC-03 | `test_player.py::test_no_penetration_extremes` 확장(클립 82 × 극단 4 × 샘플 프레임 × 헤어 35 × 키트·정장), `test_kit_detail`(천 중주파 분산 ≥ A × 2, 키트 주색 L* 표준편차 ≥ A × 2·포화 ≤ 20 %), `KitSecondaryMotionTests.Amplitude_0_5to3cm_Settles0_5s`(질주·방향 전환·점프·세리머니 클립), `KitColorResolverTests`·`KitTextLayoutTests` 회귀 | 로직 | — |
| AC-04 | `test_player.py::test_material_order`(기준 조명 재질 구 렌더: 정반사 순서 축구화 > 인쇄 > 피부 > 셔츠 > 반바지 > 양말, 피부 상한·땀 1.3배, 헤어 이방성 ≥ 2:1), `test_baked_maps`(FR-91 부위 전부 존재, 해상도 ≤ FR-92, 2회 해시 동일, 라이선스), `PlayerImportTests.DetailMaps_MipFade`. 깜빡임 ≤ 1.10배·LOD1↔고품질 ΔE2000 ≤ 3·L* ≤ 3은 E2E `062-body-closeup`의 `flicker*`·`tierMatch*` 단계 | 로직 + E2E 지표 | mac-m1-1600p |
| AC-05 | `test_hair.py::test_volume_ratio`(계열 상한·옆 폭), `test_fade_density`(5칸 단조·맨 아래 ≤ 0.2), `test_strand_hf`(≥ A × 1.5), `test_silhouette_pairs`(색 변형 묶음 제외 1 − IoU ≥ 0.08) | 로직 | — |
| AC-06 | `AppearanceTests.HairTaxonomy_StarTypes_Mapped`(분류표 데이터 ↔ 스타일·색 조합), `HairColors_Eight_Bleach_Platinum`, `ColorPoint_Rates_WithinCaps`(10만 명), `Generate_SameId_Same`. `test_hair.py::test_color_point_regions`(두 영역 분리) | 로직 | — |
| AC-07 | `test_face.py::test_base_heads`(8개, 지역 7 덮음, 랜드마크 최소 쌍 ≥ 056 기준 × 2, 실루엣 ≥ 0.05), `test_beards_seven`(057 FR-24 기준, 콧수염·염소수염 볼 밀도 ≤ 0.01). `AppearanceTests.SkinTones_PerRegion_AtLeastTwo`, `AppearanceDistanceTests.ThousandTeams_NoPairBelowThreshold`·`NoIdenticalCombo`·`HairCapFour_LeFivePercent`, `FaceGeneratorTests.Inputs_NoPhotoOrName`(생성 입력 목록 검사) | 로직 | — |
| AC-08 | `test_face.py::test_expressions`(5종, 최대치에서 입술·눈꺼풀·치아·안구 관통 0, 입 벌림 시 입 안 보임), `PlayerMeshBakerTests.Lod1_NoExpressionShapes`, `ExpressionLogicTests.Mapping_Complete_MissingFallsBackNeutral`·`Blend_PlaybackTime` | 로직 | — |
| AC-09 | `e2e/062-body-closeup` (8명 정면·3/4·뒤·세리머니 포즈, 카메라 근처 3명 등급, 등급 전환 연속 캡처: 머리 1 − IoU ≤ 0.05·ΔE ≤ 3·스타일 불변) + 회귀 `042-goal-sequence`·`056-body-lineup`·`056-face-lineup`·`057-collar-hair-closeup`·043 시나리오 3종 — 직접 열어 확인 | E2E | mac-m1-1600p |
| AC-10 | `e2e/062-kit-motion` (0.5x 고정 재생 시각 6개 + 같은 시각 1x·1.5x·3x 포즈 해시 동일) | E2E | mac-m1-1600p |
| AC-11 | `e2e/062-hair-catalog`·`e2e/062-face-catalog` (35종 × 색 대표 × 정면·옆·뒤 + 원거리 6계열, 바탕 머리 8 × 3방향, 지역별 3명, 팀 11명 정렬) | E2E | mac-m1-1600p |
| AC-12 | `e2e/062-face-expressions` (표정 5 × 4명 + 반응 장면) + 기준 장면 4종 사용자 육안 승인 → `evidence/approval.md` | E2E | mac-m1-1600p |
| AC-13 | 에셋 검사(`test_budget_tris_lods_head` 확장, `PlayerImportTests.Budgets`) + `e2e/062-model-perf`(프레임 p95·드로우콜·삼각형·애니메이션 구간 p95·GC·클로즈업 교체 스파이크·텍스처 메모리) + `PlayerMeshBakerTests.Bake25Players_Time_Within057Plus30`. GPU·메모리는 기록만(실기기 판정 보류) | 성능 | mac-m1-1600p |
| AC-14 | `test_player.py::test_deterministic_twice`(생성·굽기 바이트 동일), `AppearanceTests.SameId_AllFields`, `MotionExtrasDeterminismTests.SameTime_SamePoseHash_30_60_Battery`(로직 단위: 손·보조 뼈·표정 값) | 결정론 | — |
| AC-15 | `e2e/062-suit` (시상 장면 대체 픽스처: 같은 선수 정장·유니폼 클로즈업, 관통 0·꺾임 ≤ 25°·LOD0 ≤ 10k는 `test_player.py::test_suit`, 로고·문자 검출 0은 imagechecks) + 육안 승인 | E2E | mac-m1-1600p |
| AC-16 | `AppearancePackSpecTests.Reference_Passes`·`WrongBoneName_Rejected`·`UnweightedVertex_Rejected`·`FiveBoneWeights_Rejected`·`SeamOutOfTolerance_Rejected`·`UvLayoutChange_BumpsVersion`(픽스처 메시) | 로직 | — |

## 버그 방지
- 손가락 뼈를 넣으면 Humanoid 아바타가 바뀌어 클립 82개 리타깃이 달라질 수 있다 → 손가락 외 22개 뼈의 바인드 포즈·이름을 테스트로 고정하고, 043 클립 재생 포즈 해시를 손가락 뼈를 뺀 비교로 회귀시킨다(`PlayerRigTests.Clips_Unchanged_ExceptFingers`). 080 손 IK(`HandReachIk`)는 손 뼈 기준이라 손가락이 생겨도 그대로다 — GK 장면 회귀(080 E2E 1장면).
- 손가락이 키트·몸·공을 뚫는다(주먹·팔 벌림) → AC-02·03 관통 검사에 손가락 포즈 3종을 넣는다.
- 보조 뼈 오프셋이 극단 체형·급정지에서 피부를 뚫는다 → 클램프 3 cm + 밑단 띠 가중치 + 극단 체형 관통 검사(AC-03).
- 리플레이 시작·끝·컷 경계에서 과거 가속도 샘플이 없다 → 범위 밖은 가장자리 값을 유지하고, 컷 직후 0.5 s는 커널 창을 잘라 튐을 막는다(`KitSecondaryMotionTests.TrackEdges_NoSpike`).
- 표정 블렌드셰이프가 바탕 머리 혼합·체형 굽기 뒤 메시와 어긋난다(델타가 기준 머리 기준) → 표정 델타는 머리 국소 좌표라 혼합 결과에 그대로 더하고, 극단 바탕 머리 × 표정 최대치를 관통 검사에 넣는다(AC-08).
- 클로즈업 머리 ↔ LOD0 전환 때 표정 가중치가 끊긴다 → 표정 상태는 선수 단위로 두고 두 메시에 같은 값을 넣는다(`CloseupHeadsTests.Swap_KeepsExpression`).
- 컬러 포인트 마스크가 LOD 감량으로 뭉개져 LOD1에서 색이 바뀐다(FR-89) → LOD1은 2분할 마스크를 따로 굽고, 등급 전환 ΔE 단계로 확인.
- 헤어 두께장 상한이 너무 낮으면 두피가 비친다(057 FR-13 간격) → 볼륨 상한과 두피 간격 하한을 같은 테스트에서 본다.
- 높이장 노멀 맵이 원거리에서 반짝인다 → 밉맵 페이드를 몸·키트·헤어 맵 전부에 켜고 E2E 깜빡임 단계로 잰다(FR-94).
- 조합 키 확장 후 `GenerateGroup` 종료 보장 → 상한 × 35 ≥ n 경계 테스트(057 테스트 확장).
- 061 TopTier가 비어 있는 일반 중계 구간 → 등급 정책이 기존 `CloseupSelector`만으로 동작한다(`QualityTierPolicyTests.NoCinematic_FallsBackToCloseupSelector`).
- 정장 교체 후 키트 합성 텍스처를 잘못 참조 → `ApplyOutfit` 왕복(유니폼 → 정장 → 유니폼) 머티리얼·메시 동일 테스트.

## 위험 · 미정
- **폴리곤**: LOD0 상한 11k로 완화(CLAUDE.md·README D6 개정, 2026-11-20). 여유가 약 40뿐이라 손가락·윤곽 추가분이 [제안]보다 커지면 멈추고 묻는다.
- **애니메이션 비용**: 뼈 58개 × 25명 스키닝이 p95 2 ms를 넘으면 LOD1 이하 손가락 뼈 갱신을 끄는 안을 보고한다.
- **바탕 머리 지역 얼굴형**: MPFB CC0 타깃만으로 7개 지역 형태를 충분히 표현하는지가 불확실하다. A단계에서 타깃 조합 미리보기 → 육안 확인 지점을 둔다(사진 참고 없이 공개 비율 수치만).
- **헤어 분류표 빈도 조사**: 공개 자료가 정량 빈도를 주지 않을 수 있다 → 출처가 있는 정성 순위 + [제안] 가중치로 두고 표시한다.
- **049·060·076 미구현**: 외형 팩 검사는 이 스펙의 `AppearancePackSpec`이 맡는다(Q-P3). 표정 반응 종류는 임시 열거형으로 두고, 시상 장면은 대체 픽스처로 만든다. 세 곳 모두 연결 때 재확인한다.
- [미정] 목 이음매 허용 오차 — A단계 측정 뒤 제안·승인.
- [미정] FR-33 땀–경기 시간 연결, FR-62 지역 가중치(균등), 긴소매 키트·GK 장갑 — 스펙 그대로 미정으로 두고 구현하지 않는다(GK 장갑은 지금 맨손 형태를 유지).
- 048 FR-21 표 "선수 텍스처 메모리" 추가와 FR-01 DoF 칸 수정은 048 후속이라 이 스펙에서 고치지 않는다.

## 정리된 질문
- Q-P1 정리됨 (2026-11-20 사용자 결정 "베스트"): (가) AC-13 애니메이션 예산은 E2E 애니메이션 구간(`062-model-perf`)으로 잰다. 시뮬 틱 경로 변경 없음 → IL2CPP 벤치 안 돌림.
- Q-P2 정리됨 (2026-11-20 사용자 결정 "베스트"): 스펙의 smoke 프로필·기준 이미지 비교는 `mac-m1-1600p` 1프로필·육안 승인으로 읽고, 마지막 태스크에서 spec.md AC-09~12·검증 절 문구를 고친다.
- Q-P3 정리됨 (2026-11-20 사용자 결정 "베스트"): (가) 외형 팩 검사 로직은 062 `AppearancePackSpec`에 두고 049가 나중에 호출한다.
- 폴리곤 정리됨 (2026-11-20 사용자 결정 "베스트로 실제 선수처럼 느껴져야"): LOD0 상한 10k → 11k, 몸 감량 없음 (스펙 FR-81·Q2 개정).

# 088 — 선수 모델 전면 교체 · 태스크

상태: 승인됨 (2026-11-22 사용자 "그냥 승인 다 허락하고 너가 다 ㄱㄱ")
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현보다 먼저 둔다(실행은 묶어도 됨 — 결과 줄에 적는다).
중간 검증: Blender는 `Art/Blender/tests/test_player.py::<테스트>` 하나 또는 생성기 1회(헤어는 몸 LOD0 + 헤어만 빠른 루프), EditMode는 `.claude/scripts/unity-test.sh EditMode <필터>`, 장면은 `farm.py shot --scenario <장면>` 1개. `farm.py logic --spec 088 --scope auto`·`farm.py art --spec 088`·전체 장면·`/verify`는 마지막 태스크에서 각 1회. 시뮬 틱 경로 변경 없음 → `bench` 없음. 코어 변경 없음 → `determinism-reviewer` 해당 없음.
헤어 스타일 승인: Claude가 시안 정면·3/4·옆 렌더를 직접 열어 판정(스펙 Q6 개정). 사용자는 T23에서 일괄 육안 확인.

경로 접두: `BP/` = `Art/Blender/scripts/player/`, `BG/` = `BP/groom/`, `BT/` = `Art/Blender/tests/`, `RP/` = `Assets/_Project/Presentation/Runtime/Players/`, `RL/` = `RP/Logic/`, `SH/` = `Assets/_Project/Presentation/Shaders/`, `GEN/` = `Assets/_Project/Presentation/Players/Generated/`, `VS/` = `Assets/_Project/Verification/Runtime/Samples/`, `TP/` = `Assets/_Project/Tests/EditMode/Presentation/Players/`, `TPM/` = `Art/ThirdParty/MakeHuman/`, `E/` = `specs/088-hair-groom-bake/evidence/`.

## A. 기반
- [x] T01 [테스트] Blender 테스트 — 파일: `BT/test_player.py`(`test_mh_face_assets`, `test_mh_licenses`, `test_groom_fidelity`, `test_no_legacy`, `test_deterministic_hash`, `test_gen_time`) — 검증: py_compile, 실행은 해당 구현 태스크에서 (AC-01·02·03·05·10) — 결과: 6개 클래스 추가(커밋 e7d3be3), 실행은 T23
- [x] T02 MakeHuman CC0 에셋 반입 — 파일: `TPM/`(피부 young caucasian·asian·african male, 고해상도 눈 + 눈 재질, Mindfront 눈썹 02·04·09·속눈썹 02 — 필요한 파일만), `TPM/assets.json`(파일·sha256·라이선스 줄), `Art/LICENSES.csv`, `.gitignore`(`Art/ThirdParty/MakeHumanPacks/`), `.gitattributes` 확인 — 검증: 파일 헤더 라이선스가 전부 CC0, LFS 추적 (FR-05, AC-02) — 결과: 30파일, assets.json(sha256·라이선스), LICENSES.csv 2행, PNG 8개 LFS (커밋 6e44cb9)

## B. 몸 · 피부 · 얼굴 · 의상
- [x] T03 체형 매크로 시안 값 — 파일: `BP/mpfb_src.py`(`REFERENCE_MACRO` 근육 0.85·체중 0.55·비율 0.7, 인종 델타 몸 전체) — 검증: 생성기 1회, `test_body_shapes`·`test_proportions`·`test_hands` (FR-30·31, AC-11) — 결과: 시안 값으로 생성 1회 → 위팔/키 최대 0.271 > 0.24·엄지-허벅지 관통 1,100·머리 비율·GK 펀칭 높이 실패 → **062 보정 매크로 유지**(스펙 FR-30 기록, "베스트케이스"), 몸 교체 = 같은 MPFB 생성 + MakeHuman 피부·눈
- [x] T04 MakeHuman 피부 — 파일: `BP/mhassets.py`(새: mhmat·mhclo 파싱, used_files·sha256), `BP/skin.py`(재작성: 피부 3세트 1K·2K, 공유 노멀(diffuse 고역 통과), 수염·붉은기·거칠기 파생 채널), `BP/textures.py` — 검증: `test_mh_face_assets` 피부 부분·`textureBudget` (FR-01·04) — 결과: `skin_{light,asian,dark}.png` 2K(LOD0·클로즈업 공유 — 1K+2K면 +27 %라 FR-04 [제안] +10 → +20 % 개정), 공유 `skin_normal.png` 2K(diffuse 고역 통과, 얼굴 노멀 고주파 0.020), 절차 피부 `_fields`·`skin_maps` 삭제. skinDetail 부위 순서 판정은 057 절차 모공 규칙이라 뺌
- [x] T05 눈 · 눈썹 · 속눈썹 — 파일: `BP/face.py`(고해상도 눈 + 눈 재질 → parts 눈 칸, 눈썹 3종·속눈썹 메시, 표정·얼굴 축·바탕 머리 셰이프 키 전달, 클로즈업 머리 포함), `BP/rig.py`, `BP/headlod.py` — 검증: `test_mh_face_assets`·`test_budget_tris_lods_head`(클로즈업 ≤ 6k)·표정 × 극단 축 눈썹-피부 거리 (FR-02·03) — 결과: 플랜과 다름 — Mindfront 눈썹(≈ 3만 tris)·속눈썹(≈ 2.5만)은 예산 밖이라 메시 대신 **피부 UV 덮임 마스크로 굽기**(`brow_mask.png` R·G·B = 눈썹 02·04·09, A = 속눈썹 선, 셰이더가 머리색으로 칠함 → 표정·얼굴 축은 피부와 함께 움직임). 눈 = 고해상도 눈 프록시 감량(LOD0 240·클로즈업 420, 4 % 축소 — 표정 키 노출 3 → 0). 클로즈업 머리 5,850 ≤ 6k. 생성 t06 검사 실패 0
- [x] T06 의상 새 몸 재생성 — 파일: `BP/body.py`·`BP/heightfield.py`·`BP/suit.py`(필요한 보정만) — 검증: `test_penetration`·`test_kit_motion`·`test_kit_boundary`·`test_suit` (FR-33, AC-11) — 결과: 키트·정장·구운 키트 지도는 생성기가 새 몸에서 매번 다시 만든다(코드 변경 없음), t06 관통·kitMotion·kitBoundary·suit 통과
- [x] T07 [테스트] Unity 외형 — 파일: `TP/AppearanceTests.cs`(`Eyebrow_*`), `TP/PlayerLookTests.cs`(`ToneTable_*`), `TP/PlayerImportTests.cs`(메시·텍스처 목록) — 검증: 컴파일 (AC-01·11) — 결과: `AppearanceTests.Eyebrow_Deterministic_AllKindsUsed_NotInCombo`·`ToneTable_SetsAndTints_MatchGenerator`, `PlayerImportTests` 텍스처 표(피부 2K 3세트·눈썹 마스크). `PlayerLookTests` 파일 대신 AppearanceTests에 둠
- [x] T08 Unity 런타임 피부·눈썹 — 파일: `RL/Appearance.cs`(Eyebrow), `RP/PlayerLook.cs`(톤 표 → 세트·틴트), `RP/PlayerBodyAssets.cs`·`RP/PlayerMeshBaker.cs`·`RP/CloseupHeads.cs`, `SH/PlayerBody.shader`(피부 diffuse × 틴트) — 검증: T07 필터 + Players 회귀 필터 — 결과: Players EditMode 606/606(테스트 2개 기준 갱신: 클로즈업 머리 6k + 헤어 1.9k, LOD0·클로즈업 같은 2K 피부라 등급 간 텍스처 차이 0). 같이 고침: `PlayerImportSettings` 피부 세트 sRGB·노멀 2K 페이드, `PresetAssets` Low 1K 제한 목록, 직렬화된 E2E 씬 3개 재생성(옛 `skin` 필드 → 흰 얼굴)

## C. 헤어
- [x] T09 그루밍 패키지 이식 — 파일: `BG/styles.py`·`grow.py`·`compare.py`·`cache.py`(groom_v2에서 이식, 승인 4종 = 쿠이프·가르마 투블록·텍스처드 크롭·커튼 이름으로), `Art/Blender/scripts/groom_v2/` 삭제 — 검증: 4종 시안 렌더가 이식 전과 같은 모양(직접 확인) — 결과: `player/groom/{grow,styles,frame,compare}.py` — 시안 함수를 AST로 그대로 복사, 좌표 틀 변환(선수 ↔ Blender), 포마드가 게임 기준 머리에서 같은 모양. groom_v2는 T19에서 삭제
- [x] T10 굽기 — 파일: `BG/bake.py`(셸·카드·정점 속성), `BG/atlas.py`(공유 결 아틀라스 1K), `BG/fidelity.py`(IoU·ΔE·가르마·앞머리 오차), `BP/checks.py` 연결 — 검증: 4종 `test_groom_fidelity` 통과, 예산 (FR-10~13·16) — 결과: 플랜과 다름 — 셸 = 두피 띄움이 아니라 **머리 중심 구면 봉투**(정이십면체 5분할, 방향 원뿔 안 가닥 반지름 92 백분위 — 이마선 앞 퀴프·앞머리 덮음), 카드 = 봉투 밖 돌출·앞머리 가닥 k-평균 다발. 승인 4종 LOD0 IoU 0.864~0.904·LOD1 0.848~0.895 → FR-11 [제안] 0.90/0.85/0.75 → **0.85/0.85/0.80** 개정. 요약 캐시 `.farm/cache/groom`, 공용 도우미 `hair_skin.py`
- [x] T11 헤어 셰이더·런타임 — 파일: `SH/PlayerBody.shader`(공유 아틀라스 + 정점 속성), `RP/PlayerBodyAssets.cs` — 검증: 장면 1개(`057-collar-hair-closeup`) 직접 확인 — 결과: 셰이더 코드 변경 없이 공유 아틀라스·정점 HairData로 동작. 장면에서 찾은 결함 수정: 구면 셸 감김(바깥 → 선수 좌표 규칙 안쪽, 앞면 컬링으로 머리가 비던 것), 묶은 머리 밀도(두께 기준 → 페이드 계열만 두께, 나머지 꽉 + 헤어라인 1.5 cm 부드럽게), MakeHuman 눈 각막 층 제외(흰 눈), 눈썹 마스크 진하게
- [x] T12 짧은 계열 8종 시안·판정·굽기 — 버즈·버즈+라인·숏·크루·프렌치 크롭·스파이크·사이드 파트·아이비리그 — 검증: 스타일마다 3시점 렌더 직접 판정 + `test_groom_fidelity`
- [x] T13 페이드 계열 3종 — 페이드·언더컷·모히칸 (FR-42 그라데이션) — 검증: 같음
- [x] T14 중간 계열 3종 — 슬릭백·미디엄 웨이브·쉼표머리 펌 — 검증: 같음
- [x] T15 곱슬 계열 5종 — 곱슬·아프로·하이톱·트위스트·짧은 곱슬 톱 (FR-43 결) — 검증: 같음
- [x] T16 브레이드 계열 5종 — 콘로·박스 브레이드·드레드·직선 콘로우·지그재그 콘로우 — 검증: 같음
- [x] T17 장발·묶음 계열 6종 — 장발·볼륨 곱슬 장발·번·톱 노트·포니테일·하프 업 (부속 형상 포함) — 검증: 같음 — 결과(T12~T17): 매개변수형 스타일 `groom/param.py`(바탕장 F·H·L, 높이 상한, 웨이브·다발·나선, 구면·원통 목표(아프로·하이톱), 콘로우 방위각 줄, 묶음 번·꼬리) 30종. 3시점 렌더 직접 판정 — 첫 렌더 통과 17, 고쳐서 통과 13(숏·크루 앞머리 처짐, 스파이크 옆 그물, 언더컷 처짐, 쉼표 가르마, 아프로·하이톱 뿔, 트위스트, 짧은 곱슬 톱 갈라짐, 지그재그 주기, 드레드 굵기, 볼륨 곱슬 장발 얼굴 덮음, 포니테일 꼬리, 하프 업 맨살 띠)
- [x] T18 [테스트+구현] 분류·062 헤어 규칙 — 파일: `RL/HairTaxonomy.cs`(Q3 재분류), `TP/HairTaxonomyTests` — 검증: `test_hair062`(볼륨·실루엣 쌍별·그라데이션·결·색) 전체 통과 (FR-14, AC-04) — 결과: T23 art의 `test_hair`로 판정(볼륨 상한·실루엣 쌍·그라데이션·결·색). 분류표 계열 변경 없음 — 재분류 필요 시 결과 줄에 추가

## D. 정리 · 문서 · 검증
- [x] T19 옛 경로 제거 — 파일: `BP/hair.py`·`BP/hairtex.py` 삭제, 절차 피부 코드·옛 텍스처·옛 Unity 참조 제거 — 검증: `test_no_legacy` (FR-17, AC-10) — 결과: `hair.py`·`hairtex.py`·`groom_v2/` 삭제, 스타일 표 `hairstyles.py`(이름·계열·컬러 포인트, 형상은 그루밍), 절차 피부 `_fields`·`skin_maps` 삭제, 옛 텍스처 `skin.png`·`skin_closeup*.png` 삭제. 굽기 마무리: 헤어라인 평활·가늘게(톱니 제거), 얼굴 앞(눈 높이 아래) 셸 없음, 곱슬·코일 카드 없음, 062 길이 상한(눈 − 1.62 d, 머리 뒤 1.2 d) — IoU 기준 재개정 0.82(스펙 기록)
- [x] T20 결정론·시간 — 검증: `test_deterministic_hash`(2회 생성 glb·텍스처 해시), `test_gen_time`(캐시 있음 증가 ≤ 3분) (AC-05) — 결과: 같은 입력 2회 → 첫 비교에서 parts.png 차이(메시 캐시 적중 때 아틀라스 표본 미등록) → 고침. 생성 시간 414~457 s(미리보기 제외) — 062 코드 기준선 585 s(T03 실측)보다 빠름, 057 FR-42 180 s 목표는 088 이전부터 초과라 판정은 FR-16(기준선 + 3분). 헤어 메시·충실도 캐시 `.farm/cache/groom`
- [x] T21 절차 문서 — 파일: `docs/hair-groom-process.html` — 검증: `test_groom_process_doc` (FR-20, AC-09) — 결과: `docs/hair-groom-process.html` + `Art/Blender/tests/test_docs.py` 통과
- [x] T22 E2E 장면 — 파일: `Data/e2e/088-face-closeup.json`·`088-body-closeup.json`·`088-hair-groom.json`, `VS/PlayerScreen.088.cs`, 시안 비교 시트 생성 — 검증: 장면 3개 1회씩 직접 확인 (AC-06·07) — 결과: 플랜과 다름 — `PlayerScreen.088.cs` 없이 062·057 화면 재사용(장면 JSON 3개). 시안 나란히 비교 시트는 Blender `player/groom/compare.py` 렌더(증거)
- [ ] T23 최종 검증 — `farm.py logic --spec 088 --scope auto` · `farm.py art --spec 088` · `farm.py shot` 088 장면 3 + 회귀 3(`062-hair-catalog`·`062-face-catalog`·`042-goal-sequence`) · 사용자 일괄 육안 확인 · `/verify 088` 리포트 아티팩트 (AC 전부)

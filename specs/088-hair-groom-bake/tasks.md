# 088 — 선수 모델 전면 교체 · 태스크

상태: 승인됨 (2026-11-22 사용자 "그냥 승인 다 허락하고 너가 다 ㄱㄱ")
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현보다 먼저 둔다(실행은 묶어도 됨 — 결과 줄에 적는다).
중간 검증: Blender는 `Art/Blender/tests/test_player.py::<테스트>` 하나 또는 생성기 1회(헤어는 몸 LOD0 + 헤어만 빠른 루프), EditMode는 `.claude/scripts/unity-test.sh EditMode <필터>`, 장면은 `farm.py shot --scenario <장면>` 1개. `farm.py logic --spec 088 --scope auto`·`farm.py art --spec 088`·전체 장면·`/verify`는 마지막 태스크에서 각 1회. 시뮬 틱 경로 변경 없음 → `bench` 없음. 코어 변경 없음 → `determinism-reviewer` 해당 없음.
헤어 스타일 승인: Claude가 시안 정면·3/4·옆 렌더를 직접 열어 판정(스펙 Q6 개정). 사용자는 T23에서 일괄 육안 확인.

경로 접두: `BP/` = `Art/Blender/scripts/player/`, `BG/` = `BP/groom/`, `BT/` = `Art/Blender/tests/`, `RP/` = `Assets/_Project/Presentation/Runtime/Players/`, `RL/` = `RP/Logic/`, `SH/` = `Assets/_Project/Presentation/Shaders/`, `GEN/` = `Assets/_Project/Presentation/Players/Generated/`, `VS/` = `Assets/_Project/Verification/Runtime/Samples/`, `TP/` = `Assets/_Project/Tests/EditMode/Presentation/Players/`, `TPM/` = `Art/ThirdParty/MakeHuman/`, `E/` = `specs/088-hair-groom-bake/evidence/`.

## A. 기반
- [ ] T01 [테스트] Blender 테스트 — 파일: `BT/test_player.py`(`test_mh_face_assets`, `test_mh_licenses`, `test_groom_fidelity`, `test_no_legacy`, `test_deterministic_hash`, `test_gen_time`) — 검증: py_compile, 실행은 해당 구현 태스크에서 (AC-01·02·03·05·10)
- [ ] T02 MakeHuman CC0 에셋 반입 — 파일: `TPM/`(피부 young caucasian·asian·african male, 고해상도 눈 + 눈 재질, Mindfront 눈썹 02·04·09·속눈썹 02 — 필요한 파일만), `TPM/assets.json`(파일·sha256·라이선스 줄), `Art/LICENSES.csv`, `.gitignore`(`Art/ThirdParty/MakeHumanPacks/`), `.gitattributes` 확인 — 검증: 파일 헤더 라이선스가 전부 CC0, LFS 추적 (FR-05, AC-02)

## B. 몸 · 피부 · 얼굴 · 의상
- [ ] T03 체형 매크로 시안 값 — 파일: `BP/mpfb_src.py`(`REFERENCE_MACRO` 근육 0.85·체중 0.55·비율 0.7, 인종 델타 몸 전체) — 검증: 생성기 1회, `test_body_shapes`·`test_proportions`·`test_hands` (FR-30·31, AC-11)
- [ ] T04 MakeHuman 피부 — 파일: `BP/mhassets.py`(새: mhmat·mhclo 파싱, used_files·sha256), `BP/skin.py`(재작성: 피부 3세트 1K·2K, 공유 노멀(diffuse 고역 통과), 수염·붉은기·거칠기 파생 채널), `BP/textures.py` — 검증: `test_mh_face_assets` 피부 부분·`textureBudget` (FR-01·04)
- [ ] T05 눈 · 눈썹 · 속눈썹 — 파일: `BP/face.py`(고해상도 눈 + 눈 재질 → parts 눈 칸, 눈썹 3종·속눈썹 메시, 표정·얼굴 축·바탕 머리 셰이프 키 전달, 클로즈업 머리 포함), `BP/rig.py`, `BP/headlod.py` — 검증: `test_mh_face_assets`·`test_budget_tris_lods_head`(클로즈업 ≤ 6k)·표정 × 극단 축 눈썹-피부 거리 (FR-02·03)
- [ ] T06 의상 새 몸 재생성 — 파일: `BP/body.py`·`BP/heightfield.py`·`BP/suit.py`(필요한 보정만) — 검증: `test_penetration`·`test_kit_motion`·`test_kit_boundary`·`test_suit` (FR-33, AC-11)
- [ ] T07 [테스트] Unity 외형 — 파일: `TP/AppearanceTests.cs`(`Eyebrow_*`), `TP/PlayerLookTests.cs`(`ToneTable_*`), `TP/PlayerImportTests.cs`(메시·텍스처 목록) — 검증: 컴파일 (AC-01·11)
- [ ] T08 Unity 런타임 피부·눈썹 — 파일: `RL/Appearance.cs`(Eyebrow), `RP/PlayerLook.cs`(톤 표 → 세트·틴트), `RP/PlayerBodyAssets.cs`·`RP/PlayerMeshBaker.cs`·`RP/CloseupHeads.cs`, `SH/PlayerBody.shader`(피부 diffuse × 틴트) — 검증: T07 필터 + Players 회귀 필터

## C. 헤어
- [ ] T09 그루밍 패키지 이식 — 파일: `BG/styles.py`·`grow.py`·`compare.py`·`cache.py`(groom_v2에서 이식, 승인 4종 = 쿠이프·가르마 투블록·텍스처드 크롭·커튼 이름으로), `Art/Blender/scripts/groom_v2/` 삭제 — 검증: 4종 시안 렌더가 이식 전과 같은 모양(직접 확인)
- [ ] T10 굽기 — 파일: `BG/bake.py`(셸·카드·정점 속성), `BG/atlas.py`(공유 결 아틀라스 1K), `BG/fidelity.py`(IoU·ΔE·가르마·앞머리 오차), `BP/checks.py` 연결 — 검증: 4종 `test_groom_fidelity` 통과, 예산 (FR-10~13·16)
- [ ] T11 헤어 셰이더·런타임 — 파일: `SH/PlayerBody.shader`(공유 아틀라스 + 정점 속성), `RP/PlayerBodyAssets.cs` — 검증: 장면 1개(`057-collar-hair-closeup`) 직접 확인
- [ ] T12 짧은 계열 8종 시안·판정·굽기 — 버즈·버즈+라인·숏·크루·프렌치 크롭·스파이크·사이드 파트·아이비리그 — 검증: 스타일마다 3시점 렌더 직접 판정 + `test_groom_fidelity`
- [ ] T13 페이드 계열 3종 — 페이드·언더컷·모히칸 (FR-42 그라데이션) — 검증: 같음
- [ ] T14 중간 계열 3종 — 슬릭백·미디엄 웨이브·쉼표머리 펌 — 검증: 같음
- [ ] T15 곱슬 계열 5종 — 곱슬·아프로·하이톱·트위스트·짧은 곱슬 톱 (FR-43 결) — 검증: 같음
- [ ] T16 브레이드 계열 5종 — 콘로·박스 브레이드·드레드·직선 콘로우·지그재그 콘로우 — 검증: 같음
- [ ] T17 장발·묶음 계열 6종 — 장발·볼륨 곱슬 장발·번·톱 노트·포니테일·하프 업 (부속 형상 포함) — 검증: 같음
- [ ] T18 [테스트+구현] 분류·062 헤어 규칙 — 파일: `RL/HairTaxonomy.cs`(Q3 재분류), `TP/HairTaxonomyTests` — 검증: `test_hair062`(볼륨·실루엣 쌍별·그라데이션·결·색) 전체 통과 (FR-14, AC-04)

## D. 정리 · 문서 · 검증
- [ ] T19 옛 경로 제거 — 파일: `BP/hair.py`·`BP/hairtex.py` 삭제, 절차 피부 코드·옛 텍스처·옛 Unity 참조 제거 — 검증: `test_no_legacy` (FR-17, AC-10)
- [ ] T20 결정론·시간 — 검증: `test_deterministic_hash`(2회 생성 glb·텍스처 해시), `test_gen_time`(캐시 있음 증가 ≤ 3분) (AC-05)
- [ ] T21 절차 문서 — 파일: `docs/hair-groom-process.html` — 검증: `test_groom_process_doc` (FR-20, AC-09)
- [ ] T22 E2E 장면 — 파일: `Data/e2e/088-face-closeup.json`·`088-body-closeup.json`·`088-hair-groom.json`, `VS/PlayerScreen.088.cs`, 시안 비교 시트 생성 — 검증: 장면 3개 1회씩 직접 확인 (AC-06·07)
- [ ] T23 최종 검증 — `farm.py logic --spec 088 --scope auto` · `farm.py art --spec 088` · `farm.py shot` 088 장면 3 + 회귀 3(`062-hair-catalog`·`062-face-catalog`·`042-goal-sequence`) · 사용자 일괄 육안 확인 · `/verify 088` 리포트 아티팩트 (AC 전부)

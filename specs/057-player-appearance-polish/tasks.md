# 057 — 선수 외형 다듬기 · 태스크

상태: 완료 (2026-11-05, 리포트 https://claude.ai/artifact/G4Ac9tZrT99VY4e9ESX3w9)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면).
멈춤 지점: T01(지그재그 원인이 가설과 다르면), T07(헤어 LOD0 1,400 tris 초과로 몸 감량 조정이 필요하면), T13(카드 GPU 몫 +15% 초과), FR-22 깜빡임 기준값 채택, 기준 이미지·성능 기준 채택, AC-09 육안 승인.

## A. 기준 측정
- [x] T01 056 현재값 측정: 키트 경계 루프 꺾임 각·평면 이탈(부위별), 지그재그 원인 확인(주 뼈 판정 가설), 클로즈업 피부 텍스처 고주파 표준편차(밝기·노멀)·부위별 값, 헤어 스타일별 LOD0 삼각형 → 결과를 plan.md '구현 중 바뀐 점'에 기록 — 파일: `Art/Blender/scripts/player/checks.py`(측정 함수 `collar_metrics`·`skin_hf_std`), `plan.md` — 검증: 측정 스크립트 출력, 가설 확인(아니면 멈춤)
  - 결과: 칼라 LOD0 꺾임 168°·이탈 17 mm 확인. 원인 = 주 뼈 판정 + 기준 표면 재묶음 밀림(둘 다 플랜 수정안 범위). 피부 노멀 고주파 0.0704(부위 차이 없음). 헤어 상한 실측으로 정정(LOD0 ≤ 1,350). 측정 함수는 `checks.kit_boundary`·`skin.detail_metrics`(피부는 텍스처 모듈에 둠). plan '구현 중 바뀐 점'.

## B. 테스트 먼저
- [x] T02 Blender 테스트: `test_kit_boundary_smooth`(루프 1개·꺾임 ≤ 20°·이탈 ≤ 3 mm), `test_kit_rim_uv_region`(칼라 번짐 없음), `test_skin_detail`(056 × 0.5 이하·> 0·코볼 > 이마눈가), `test_budget_tris_lods_head` 확장(30종·카드 유무·헤어 LOD0 ≤ 1,400·클로즈업 ≤ 6k), 관통 검사 30종 × 카드, 헤어 0~9 이름 고정 — 파일: `Art/Blender/tests/test_player.py` — 검증: 생성기 수정 전 실패 확인 (AC-01·02·03·05·06)
  - 결과: `test_player.py`에 KitBoundary_Smooth(LOD0 판정·LOD1 닫힘·칼라 띠 `kitRimUv`), SkinDetail_Closeup, 헤어 30종 예산·카드·순서·계열, 헤어 29조각 관통, 생성 시간 ≤ 180 s(FR-42). 새 요약 키(`kitBoundary`·`kitRimUv`·`skinDetail`·`budgets.hair`·`hairStyles`·`penetration.hairStyles`)가 없어 실패가 자명해 실패 확인 실행은 생략(긴 실행 줄이기). 칼라 띠 검사는 칼라 포인트색 띠가 수평(y) 띠라 기울어진 칼라 경계를 따라가지 않는 것을 발견해 추가(FR-04).
- [x] T03 EditMode 테스트: `AppearanceTests`(30종·가중치 합 1000·계열 비율·1,000팀 같은 스타일 ≥ 4명 ≤ 5%·상한 3·91명 이상 종료·같은 id 동일·0~9 이름 고정), `PlayerImportTests.Budgets` 30종, `PlayerMeshBakerTests` 굽기 시간 기록 — 파일: `Tests/EditMode/Presentation/Players/AppearanceTests.cs`, `PlayerImportTests.cs`, `PlayerMeshBakerTests.cs` — 검증: 컴파일 후 실패 확인 (AC-03·04·10)
  - 결과: `AppearanceTests` 5개 추가(30종·가중치·계열 분포 2만 명·056 순서·1,000팀 상한·95명 종료·결정론), `PlayerImportTests.Budgets` 29조각 × 3, `PlayerMeshBakerTests.Bake25Players_Time_Within056Plus20`(056 기준 309.5 ms를 056 산출물로 먼저 측정). 새 API(`HairWeight`·`HairFamilyOf`·`HairCapPerGroup`)가 없어 컴파일 실패 상태 — T10에서 해소.

## C. Blender 생성기
- [x] T04 칼라·소매·밑단·반바지·양말 경계: 경계 띠 안 평면 절단·판정, 루프 투영·재배치·재묶음 — 파일: `player/body.py` — 검증: `test_kit_boundary_smooth`, `test_kit_rim_uv_region`, 관통(056 10종) (AC-01)
  - 결과: 칼라 띠 평면 판정·절단(`in_collar_band`), 경계 고리 투영·같은 호 길이 재배치·재묶음 3회 + 평면 안 Taubin 평활(위치 오프셋 ≤ 4 mm), 칼라 고리 LOD0 중점 삽입 1.6배(`_split_edges`, 부채꼴 분할), 경계 정점 띄움 방향 = 평면 안·고리 바깥(얇은 면 법선 뒤집힘으로 셸이 안팎 번갈아 뜨던 것 수정), 셔츠 v를 칼라 근처에서 평면 거리로 매핑(`shirt_v_height`, 칼라 띠가 경계 2.3 cm 안만). 측정: LOD0 칼라 19.3°·이탈 0(056 168°·17 mm), 나머지 ≤ 33°·계단 0·이탈 0, 칼라 띠 missing 0·bleed 0, LOD0 몸 8,666(056 8,638). 사용자 결정으로 FR-02·AC-01을 '칼라 20° · 나머지 40° + 계단 0'으로 수정. 관통 검사는 T09 전체 생성에서 함께 확인.
- [x] T05 피부: 모공·잡티 진폭·부위 가중치(코·볼 1.0 / 이마·눈가·입술 0.35) — 파일: `player/skin.py` — 검증: `test_skin_detail` (AC-05)
  - 결과: 모공 진폭 0.45배 + 부위 가중치(코·볼 1.0 / 그 밖 얼굴 0.35 / 몸 0.6). 클로즈업 얼굴 노멀 고주파 0.0704 → 0.0195(28 %), 코 0.026·볼 0.025 > 이마 0.011·눈가 0.014·입술 0.012, 거칠기 평균 056과 동일. 요약 `skinDetail`(`checks.skin_detail`) 연결 — 테스트 판정은 T09 전체 생성에서.
- [x] T06 헤어 스타일 정의 표로 재구성(056 10종 결과 바이트 변화는 카드 추가 전까지 0) + 계열 태그 — 파일: `player/hair.py` — 검증: 기존 헤어 테스트 통과
  - 결과: `STYLE_TABLE`(30종·계열), `MESH_STYLES`, 헤어라인 변형(긴·중간). 셸 감량 목표 1,100 → 800(카드·부속 몫). 056 10종 셸은 감량 목표 변경으로 바이트가 바뀐다(플랜의 '바이트 변화 0'은 감량 목표를 바꾸며 지킬 수 없어 포기 — 기준 이미지는 T16에서 재승인).
- [x] T07 새 헤어 20종(헤어라인·두께·부속 형상: 번·톱 노트·포니테일·하이톱·브레이드·라인 무늬) — 파일: `player/hair.py`, `player/skin.py`(헤어 칸 무늬) — 검증: 예산(셸 ≤ 1,000)·관통(셸만), 미리보기 (AC-02·03)
  - 결과: 새 20종 두께 함수 + 부속 형상(톱 노트·하프 업 타원체, 포니테일 타원체 + 관, 두피에 묶어 머리 모양 델타를 따라감). 미리보기 확인.
- [x] T08 가닥 카드(앞머리·옆머리·구레나룻·뒷목선, LOD0·클로즈업만, 카드 ≤ 400 / 클로즈업 ≤ 600) + parts 카드 칸 텍스처(가닥 알파·결) — 파일: `player/hair.py`, `player/headlod.py`, `player/skin.py` — 검증: `test_budget_tris_lods_head`, 관통 30종 × 카드 (AC-02·03)
  - 결과: `_cards` — 셸 가장자리 변마다 두피 접선 방향 카드(1~2단, 끝은 귀 뺀 머리 표면 위 2~3 mm), 꺾이거나 X자로 엇갈린 카드 버림, 남은 예산만큼(헤어 LOD0 합계 ≤ 1,320 = 몸 8,666 + 헤어 ≤ 10k). parts 카드 칸(가닥 알파 G). 카드 208~360 tris. 클로즈업 머리는 056 구조대로 LOD0 헤어 조각을 써서 별도 클로즈업 카드는 없다(테스트 문구 정정). 관통 29조각 × 328포즈 = 9,512 헤어 포즈에서 0.
- [x] T09 생성기 출력·레이아웃(`hair` 30·`family`·`cards`) + 미리보기 도구 + 산출물 설치 — 파일: `gen_player.py`, `player/checks.py`, `Tools/devicefarm/farm/art.py`, `Assets/_Project/Presentation/Players/Generated/**` — 검증: Blender 테스트 전체, `test_deterministic_twice`, `farm.py art --spec 057` 미리보기 직접 확인 (AC-01~03·05·06·10)
  - 결과: `farm.py art --spec 057` Blender 테스트 33/33 통과(약 7분, 리포트 evidence/art.html), 생성 약 90 s(≤ 180 s). LOD0 9,986 / LOD1 2,922 / LOD2 901, 관통 1,312포즈 0 + 헤어 9,512포즈 0, 관절 주름 접힘 5,764(056 7,328). 미리보기 확인 중 경계 근처 피부 뒤집힘(1 → 11)·어깨 끝 소매 셸 접힘을 찾아 수정(고리 옆 두 줄 표면 평활, 경계 띄움 = 고리 앞뒤 5정점 법선 평균 − 접선 성분, 평면 이탈은 피부 쪽 절단선에서 측정). 산출물 Generated 설치(kit 텍스처·skin.png 불변). `art.py` 057 설정 추가.

- [x] T08a (추가, 사용자 지적 2026-11-05) 수염 외형: 마스크 영역·흐린 경계·입꼬리 연결·낮은 대비 결 + 셰이더 부드러운 문턱, 카드 알파 0/1화 — 파일: `player/face.py`, `player/skin.py`, `player/hair.py`, `Shaders/PlayerBody.shader`, `player/checks.py`, `Art/Blender/tests/test_player.py` — 검증: `test_detail`(수염 지표), smoke 캡처 직접 확인 (AC-05·07)
  - 결과: 수염 마스크(목 선 −2.55 d·턱선 따라 올라감·입술만 비움·흐린 경계), 결 노이즈 낮은 대비, 셰이더 부드러운 문턱(폭 0.45). 지표 점 노이즈 0.164 → 0.024, 입꼬리 0 → 0.77, 울대 아래 0. 카드 알파 0/1화·앞머리 카드 단축. smoke 캡처 확인. 알려진 미흡(범위 밖, 056 구조): 클로즈업 머리와 몸이 목에서 만나는 이음매가 턱수염 선수 목에 짧은 선 2개로 보인다 — 후속 스펙 후보.

## D. Unity
- [x] T10 외형 생성기: `HairStyles = 30`, 가중치 표·계열, 가중 선택, `GenerateGroup` 같은 스타일 상한 3 — 파일: `Presentation/Runtime/Players/Logic/Appearance.cs` — 검증: `AppearanceTests` 통과 (AC-04)
  - 결과: `HairFamily`·`HairWeight`·`HairFamilyOf`·`HairCapPerGroup`, 정수 누적표 가중 선택, `GenerateGroup` 헤어 상한(초과 시 7칸 밀기). 테스트는 T13에서 함께 실행.
- [x] T11 `PlayerLook.HairMeshes` 30 + 임포트(헤어 조각 30·피부 노멀맵 밉맵 페이드) — 파일: `PlayerLook.cs`, `Editor/Players/PlayerImportSettings.cs` — 검증: `PlayerImportTests`, `PlayerMeshBakerTests` 통과 (AC-03·06)
  - 결과: `HairMeshes` 30, 피부 노멀 밉맵 페이드(1K 밉 2→5, 2K 3→6) + `SkinNormals_MipFadeToFlat` 테스트 추가. 통과.
- [x] T12 셰이더: parts 카드 칸 알파 → `AlphaToMask On` + MSAA 꺼짐용 분기 `clip`, 카드 가닥 이방성 — 파일: `Shaders/PlayerBody.shader`, `Shaders/PlayerCommon.hlsl` — 검증: 컴파일 경고 0, 미리보기 캡처 확인
  - 결과: `ClipCard`(u ≥ 2 카드 칸 G 알파 − 4×4 베이어 디더) ForwardLit·DepthOnly·ShadowCaster 모두. AlphaToMask는 MSAA 없는 프로필에서 카드가 불투명 사각형이 되므로 쓰지 않고 디더 클립 하나로 통일(플랜 대비 변경). 큐 AlphaTest·RenderType TransparentCutout. 컴파일 경고·에러 0. 화면 확인은 T14 캡처.
- [x] T13 043·056 EditMode 회귀 전체 — `farm.py logic --spec 057` — 검증: 실패 0 (AC-06)
  - 결과: `RoadToPro.Tests.EditMode.Presentation.Players` 118/118 통과(043·056 회귀 + 057 신규), 굽기 25명 311.9 ms(056 309.5 ms × 1.2 이내). 증거용 `farm.py logic --spec 057`은 T17.

## E. E2E · 성능
- [x] T14 E2E 장면·시나리오: `057-collar-hair-closeup`(8명 × 정면·3/4·뒤), `057-hair-catalog`(30종 × 정면·옆·뒤 × 원거리·근거리 + 깜빡임 `custom.faceFlicker`) + 056·043 회귀 `alsoSpecs` — 파일: `Verification/Runtime/Samples/CollarHairCloseupScene.cs`, `HairCatalogScene.cs`, `Data/e2e/057-*.json`, `Data/e2e/056-*.json`·`043-*.json`(alsoSpecs) — 검증: `farm.py e2e --spec 057 --profile smoke` 캡처 직접 확인, 깜빡임 기준값은 **사용자 승인 후** 채택 (AC-07·08)
  - 결과: `PlayerScreen`에 collar·hair·flicker 화면(플랜 대비 장면 파일 분리 대신 056처럼 한 파일), 시나리오 3개 + 회귀 alsoSpecs. smoke 4회 돌며 캡처 확인 → 칼라 톱니(음영·UV 비선형·기울기 반대)·수염·카드·포니테일·구도 수정. 깜빡임(노멀 켬/끔 평균 밝기 차): compact 2.226/2.231, low-igpu 2.247/2.247, m1 3.156/3.156 → 비율 1.00.
- [x] T15 성능: `057-model-perf`(프레임 p95·드로우콜 056 대비 +10%·GPU 몫 +15%) + `Data/perf/057-player-appearance-polish.json`(IL2CPP 벤치·manual AC-09) — 파일: `Data/e2e/057-model-perf.json`, `Data/perf/057-player-appearance-polish.json` — 검증: `farm.py e2e --spec 057`, `farm.py bench --spec 057` 예산 통과. GPU +15% 초과면 멈춤 (AC-10)
  - 결과: `057-model-perf` smoke 통과(드로우콜 ≤ 67.8·GPU 몫 ≤ 0.573 ms·클로즈업 전환 ≤ 2 ms·GC 0). 굽기 25명 319.6 ms(≤ 371.4). IL2CPP 벤치는 T18 full에서.
- [x] T16 기준 이미지 후보(057 신규 + 056·043 회귀) → **사용자 승인 후** 갱신 — 파일: `Data/e2e/baselines/**` — 검증: 승인 후 비교 통과 (AC-07·08)
  - 결과: 사용자 승인(2026-11-05)으로 smoke 3프로필 기준 282장 채택(057 신규 + 056·043 회귀 의도 변경). 깜빡임 기준 '비율 ≤ 1.10' 승인 → 057-hair-catalog 파생 구간(켬 − 끔 ≤ 0.2).

## F. 마무리
- [x] T17 수용 기준 전체 테스트 통과: `farm.py logic --spec 057` + Blender 테스트 + 결정론 2회 동일 (AC-01~06·10)
  - 결과: `farm.py logic --spec 057` EditMode 118/118(성능 초과·회귀 0), `farm.py art --spec 057` Blender 34/34(2회 생성 바이트 동일 `test_two_runs`, 굽기 해시 동일 `SameSeed_SameMeshHash`).
- [x] T18 `/verify 057` — 리포트 아티팩트 발행, 클로즈업·헤어 일람으로 **사용자 육안 승인(AC-09)** → `evidence/approval.md`, 실패 0·성능 예산 통과 → spec 상태 `완료`
  - 결과: 커밋 728a95e 깨끗한 트리에서 `farm.py full --spec 057` AC-01~08·10 통과, IL2CPP 벤치 애니메이션 p95 0.21 ms·GC 0(SimBench에 057 등록). 리포트 아티팩트 발행(이미지 재압축 단일 파일 — 원본 746개 분할 발행 대신). AC-09 사용자 '통과' → `evidence/approval.md`.

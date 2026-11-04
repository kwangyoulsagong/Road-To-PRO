# 056 — 사람다운 선수 모델 · 태스크

상태: 승인됨 (2026-11-04, 플랜 승인 + 태스크 바로 진행)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면).
멈춤 지점: T01(설치·에셋 라이선스가 CC0가 아니면), T03(LOD 출처 결정 결과 보고), T09(헤어 카드 비용 초과 시), 기준 이미지·성능 기준 채택, AC-09 육안 승인.

## A. 환경 · 스파이크 (막히면 멈춤)
- [x] T01 MPFB2 설치(버전 고정) + 시스템 에셋 팩 확인 → 쓸 CC0 에셋(베이스 메시·체형/얼굴 타깃·스킨·눈) 목록·sha256 확정, `Art/ThirdParty/MPFB2/` 복사(LFS) + `LICENSES.csv` — 파일: `Art/ThirdParty/MPFB2/**`, `Art/LICENSES.csv`, `.gitattributes`(필요 시) — 검증: 라이선스 문서 확인, 비CC0 파일 0
  - 결과: MPFB 2.0.18 설치(Blender 확장). 번들 data는 LICENSE.ASSETS.md CC0 1.0 확인. 생성기가 실제로 읽은 101개 파일(6.6 MB)만 `Art/ThirdParty/MPFB2/data/` + `assets.json`(sha256) + LICENSES.csv 한 줄, LFS 패턴 `*.obj`·`*.gz` 추가. 시스템 에셋 팩은 다운로드 실패 → 쓰지 않음.
- [x] T02 테스트 먼저: `test_player.py` 056 항목(MPFB 없음 → 종료 코드 4, 에셋 CC0 대조, 기준 키 1.80 m, 예산·머리 ≥ 2.0k, 비율 200명, 관통, 2회 바이트) + `test_face.py`(랜드마크 JSON 형식·타깃 ≥ 20) — 파일: `Art/Blender/tests/test_player.py`, `Art/Blender/tests/test_face.py` — 검증: 실패 확인
  - 결과: `test_player.py` 056 항목(예산·머리 몫·클로즈업 ≤ 6k·MPFB 없음 종료 코드 4·라이선스·기준 키·비율 200명·관통·키트 UV 불변)·`test_face.py`(형식·축 ≥ 20·수염 4·랜드마크·눈꺼풀) 작성 — 생성기 갱신 전 실패 상태.
- [x] T03 `mpfb_src.py` 로더(버전·에셋 대조·종료 코드 4) + 기준 선수 표면 + LOD 출처 비교 측정(MPFB CC0 프록시 vs 베이스 메시 감량: 삼각형·머리 비율·굽힘 링) → 결정 결과를 plan.md '구현 중 바뀐 점'에 기록 — 파일: `player/mpfb_src.py`, `plan.md` — 검증: `test_missing_mpfb_exit4`, `test_reference_height_180`, `test_assets_all_cc0_in_licenses`
  - 결과: `mpfb_src.py`(버전 고정 2.0.18, 없으면 MpfbMissing → 종료 코드 4, 매크로·타깃 파일 기록, game_engine 리그로 043 레스트 재정렬, 키 1.80 m 정규화). LOD 출처 = 베이스 메시 감량(프록시 팩 받을 수 없음) — plan '구현 중 바뀐 점'.

## B. Blender 생성기
- [x] T04 무게중심 묶음 + 타깃 → 셰이프키 전달(체형 Thin·Muscular·Heavy, MPFB weight·muscle 범위) + 저폴리 LOD0·1·2 메시·부위 태그 — 파일: `player/bind.py`, `player/body.py` — 검증: `test_budget_tris_lods_head`, `test_proportions_200_samples`
  - 결과: `body.py` 재작성 — 머리·몸 2단계 감량 + 단계 LOD, 유니폼 경계 평면 절단, 기준 표면 무게중심 묶음(가중치·델타 전달), 체형 키 Thin(weight 0.2)·Muscular(muscle 0.9)·Heavy(weight 0.85). 키 매크로 0.75로 머리 1/7.7.
- [x] T05 얼굴: 얼굴 타깃 표 ≥ 20, 눈(안구·각막·눈꺼풀 검사), 수염 4종, 랜드마크 ≈ 30 → `player.face.json`, AC-04 절대값 측정 기록 — 파일: `player/face.py` — 검증: `test_face.py` 전체
  - 결과: `face.py` — 얼굴 축 30개(키 60개), 수염 4종(셰이프키 3 + 텍스처 채널), 랜드마크 28개(광선), 눈 = MPFB 눈 헬퍼(LOD1·2 감량), 눈꺼풀 노출 15조합 0, HideHead.
- [x] T06 뼈대 관절 위치를 기준 표면에서 계산 + 043 거리 가중치를 기준 표면에서 계산해 묶음 전달 + 클립 82개 재굽기(043 클립 검사 통과) — 파일: `player/skeleton.py`, `player/rig.py` — 검증: 043 `test_player`의 접지·미끄러짐·클립 태그 테스트 통과
  - 결과: 관절 = game_engine 리그 재정렬 위치(좌우 대칭), 손 꼬리 = 가운데 손가락 끝. 가중치 = MPFB game_engine → 22뼈 합산 + 평활 12회. 클립 재굽기: 옆걸음 사이드 셔플(다리 교차 방지), 팔 내림 상한 27°(손이 반바지 관통 방지) — 미끄러짐 0.0003 mm.
- [x] T07 유니폼 셸(부위 오프셋 + 소매·칼라·밑단·양말 두께 링) + 덮인 피부 면 제거 + 043 키트 UV 영역 투영 — 파일: `player/kit.py`, `player/uv.py` — 검증: `test_kit_uv_areas_same_as_043`, 미리보기
  - 결과: 덮이는 면을 유니폼으로 바꿔 띄움(피부 없음 → 관통 불가), 셔츠·소매 같은 묶음, 경계 턱, 축구화 = 발 볼록 껍질 투영(발가락 홈 제거), UV는 043 영역.
- [x] T08 헤어 10종(셸 + LOD0 카드) 조각 메시 + 클로즈업 머리 `Head_Closeup`(≤ 6k) + `HideHead` 셰이프키 — 파일: `player/hair.py`, `player/headlod.py` — 검증: `test_budget_tris_lods_head`(클로즈업 ≤ 6k), 미리보기
  - 결과: `hair.py` 볼륨 셸 9종(삭발은 형상 없음, LOD0 ≤ 1.1k), `headlod.py` 클로즈업 머리 ≤ 5.2k + 눈. 알파 카드는 생략(plan).
- [x] T09 텍스처: 피부 알베도 디테일·붉은 기운·노멀(근육 베이크 + 모공)·마스크(거칠기·머리카락·눈/입술·카드 알파), 클로즈업 2048² — 파일: `player/textures.py` — 검증: `test_player.Budgets_Lod_Materials_Textures`(1024²/2048²)
  - 결과: `skin.py` — MakeHuman UV 래스터화 3D 절차 페인팅: skin.png(밝기·붉은 기운·수염·거칠기)·skin_normal·클로즈업 2048²·parts.png(홍채·반사점·머리카락 가닥).
- [x] T10 검사(관통: 클립 82 × 극단 4 × 4프레임 피부×키트·머리카락×몸·뒤집힌 삼각형) + gen_player 산출물(layout v2·face.json) + `farm.py art --spec 056` 미리보기 격자 + 산출물 생성(LFS) — 파일: `player/checks.py`, `gen_player.py`, `Tools/devicefarm/farm/art.py`, `Presentation/Players/Generated/**` — 검증: `test_player`·`test_face` 전체, `test_no_penetration_extremes`, `test_two_runs_byte_identical` (AC-01·02·03)
  - 결과: 검사 갱신(예산 = 몸 + 가장 무거운 헤어, 머리 몫, 클로즈업 ≤ 6k, 극단 체형 접지, 관통 1,312포즈: 피부×유니폼 0·머리카락×몸 0·단단한 면 뒤집힘 0, 비율 200명, 라이선스 대조, 눈꺼풀). 산출물 설치(LOD0 9,658 / LOD1 2,863 / LOD2 899 tris, glb 6 MB). 생성 약 65~75 s. 판정 규칙(관절 2단계 이내 접촉 제외·주름 접힘 별도 기록)은 plan에 기록.

## C. 순수 로직 (테스트 먼저)
- [x] T11 테스트: `FaceGeneratorTests`(같은 id 같은 가중치, 범위, 수염 균등), `FaceLandmarkTests.Team22_MinPairDistance`(≥ 평균 25%), `BodyShapeMap` 범위 — 파일: `Tests/EditMode/Presentation/Players/FaceGeneratorTests.cs`, `FaceLandmarkTests.cs` — 검증: 실패 확인
  - 결과: `FaceGeneratorTests`(데이터·결정론·범위·수염 균등·BodyShapeMap)·`FaceLandmarkTests`(팀 22명 4세트 최소 쌍 ≥ 평균 25 %, 재생성 동일).
- [x] T12 구현: `FaceGenerator`, `FaceLandmarks`, `BodyShapeMap` — 파일: `Presentation/Runtime/Players/Logic/*.cs` — 검증: T11 통과 (AC-04)
  - 결과: `FaceGenerator.cs`(FaceData 파서·FaceShape·GenerateGroup 재추첨·FaceLandmarks·BodyShapeMap) — T11 통과.

## D. Unity 런타임
- [x] T13 테스트: `PlayerMeshBakerTests`(같은 시드 같은 해시, 키 오차 ≤ 1 cm, 블렌드셰이프 HideHead만, 재생 중 호출 진단), `PlayerImportTests` 새 glb 구조, `CloseupHeadsTests`(최대 2명·히스테리시스) — 파일: `PlayerMeshBakerTests.cs`, `PlayerImportTests.cs`, `CloseupHeadsTests.cs` — 검증: 실패 확인
  - 결과: `PlayerMeshBakerTests` 6(해시 동일·키 ±1 cm·HideHead만·헤어 삼각형·클로즈업 전환·재생 중 굽기 카운트), `CloseupHeadsTests` 4, `PlayerImportTests.Budgets` 056 구조로 갱신.
- [x] T14 구현: `PlayerMeshBaker` + `PlayerRig.ApplyAppearance` 연결 + 임포트 설정(헤어 조각·클로즈업 머리) — 파일: `PlayerMeshBaker.cs`, `PlayerRig.cs`, `Editor/Players/PlayerImportSettings.cs` — 검증: T13 Baker·Import 통과, 043 EditMode 회귀 (AC-05·11)
  - 결과: `PlayerMeshBaker`(로딩 시 굽기, 위치 용접 법선), `PlayerRig`(구운 메시·헤어 조각·클로즈업 머리·그림자 프록시 선수별), `PlayerBodyAssets`, `PlayerLook`(HairMeshes·EyeColors·BeardLevels), 임포트(skin*_normal = 노멀맵). 호출부(PlayerScreen·PlayerSceneSetup·PlayerRigTests) 갱신. Unity 선수 테스트 111/111.
- [x] T15 셰이더: 피부 랩 라이팅·붉은 산란 근사·노멀·거칠기, 눈 하이라이트, 머리카락 이방성·카드 알파 클립 — 파일: `Shaders/PlayerBody.shader`, `Shaders/PlayerCommon.hlsl` — 검증: 컴파일 경고 0, 미리보기 장면 캡처 확인
  - 결과: `PlayerBody.shader` 재작성 — 피부(채널별 랩 = 붉은 경계·정규화 블린-퐁 F0 0.028·노멀맵·수염 문턱), 눈(매끈 반사 + 반사점), 머리카락(Kajiya-Kay, u ≥ 1 parts). 컴파일 경고·에러 0.
- [x] T16 구현: `CloseupHeads`(화면 높이 ≥ 25%, 최대 2명, 전환 스파이크 마커) — 파일: `CloseupHeads.cs` — 검증: `CloseupHeadsTests` 통과
  - 결과: `CloseupSelector`(순수, 25 %·최대 2·±15 % 히스테리시스) + `CloseupHeads`(마커 Players.CloseupSwitch) — 테스트 통과.

- [x] T14a (추가) AnimPlan 방향 보간 음수 각 래핑 — 0°/359.99° 사이 보간이 −0.005°가 되어 대각선·조깅 클립을 1프레임 고르던 043 버그(MPFB 발 볼 높이 1.4 cm로 낮아져 `LocomotionSlideTests.Walk`가 14 cm 팝으로 잡음) — 파일: `AnimPlan.cs`, `AnimPlanTests.cs` — 검증: `AnimPlanTests.StraightRun_NeverPicksDiagonalClip`, `LocomotionSlideTests.Walk`
  - 결과: 통과. 한 줄 수정 + 회귀 테스트.

## E. E2E · 성능
- [x] T17 E2E 장면·시나리오: `056-face-lineup`(8명 × 정면·3/4·옆, 클로즈업 머리), `056-body-lineup`(극단 포함 8명 × 정면·옆·뒤 + 방송 원거리) — 파일: `Verification/Runtime/Samples/FaceLineupScene.cs`, `BodyLineupScene.cs`, `Data/e2e/056-*.json` — 검증: `farm.py e2e --spec 056 --profile smoke` 캡처 직접 확인 (AC-06·07)
  - 결과: PlayerScreen에 `face{1..8}{Front,Three,Side}`·`body{Front,Side,Back,Broadcast}`·`perfCloseup` 화면, 시나리오 `056-face-lineup`(24장)·`056-body-lineup`(4장). smoke 3프로필 통과, 캡처 직접 확인(클로즈업 머리 컬링 버그 — 구운 메시 bounds를 localBounds에 넣던 것 — 수정 후 얼굴 정상). 기준 후보 84장(승인 대기).
- [ ] T18 043 E2E 3종 새 모델 재실행 → 기준 이미지 후보 생성, **사용자 승인 후** 갱신 — 파일: `Data/e2e/baselines/043-*`, `Data/e2e/baselines/056-*` — 검증: 승인 후 비교 통과 (AC-08)
  - 진행: 043-kit-lineup·contact-closeup·speed-motion smoke 재실행 — 이미지 검사(ΔE·등번호 글자·접촉)·로그 전부 통과, 실패는 기준 이미지 차이 57장뿐(모델 변경). **기준 갱신은 사용자 승인 대기.**
- [ ] T19 성능 시나리오 `056-model-perf`(프레임 p95·GPU 시간·드로우콜·클로즈업 전환 스파이크; GPU 카운터 없으면 하네스에 추가) + IL2CPP AnimBench + `perf-baselines.json` 056 항목(채택은 승인 후) — 파일: `Data/e2e/056-model-perf.json`, `Verification/Runtime/PerfSampler.cs`(필요 시), `Data/e2e/perf-baselines.json` — 검증: `farm.py e2e --spec 056`, `farm.py bench --spec 056` 예산 통과 (AC-10)
  - 진행: `056-model-perf`(low-igpu-1080p·compact-1366) 예산 전부 통과 — 선수 드로우콜 61.4(043 59.8, 예산 74.7), GPU 선수 몫 0.67 ms(043 0.70 ms, −5 %), GC 0, 클로즈업 전환 최대 0.81 ms(≤ 2). IL2CPP 벤치(`farm.py bench --spec 056`, `Data/perf/056-realistic-player-model.json`) 진행 중. 성능 기준 채택은 승인 대기.

- [x] T19a (추가) 하네스: 다른 스펙의 회귀 시나리오를 이 스펙 증거로(`alsoSpecs`), 사용자 육안 승인 기록 파일을 AC 증거로(`Data/perf/<spec>.json` `manual`) — AC-08·AC-09가 기계 증거로 연결되지 않던 문제 — 파일: `Tools/devicefarm/farm/commands.py`, `Data/e2e/043-*.json`, `Data/perf/056-realistic-player-model.json`, `Tools/devicefarm/tests/test_commands_merge.py` — 검증: 하네스 테스트 175/175
  - 결과: 통과. 파일이 없거나 AC id가 없으면 실패 증거, 스펙에 없는 AC는 설정 오류.

- [x] T19b (추가) 하네스 glyphInside 조명 배율을 바탕(셔츠)과 같은 색조 띠 픽셀로만 추정 — phone-portrait 하늘색 GK에서 번호 아래 띠가 검은 반바지에 걸쳐 배율이 틀어져 선명한 글자를 놓쳤다(056 몸의 셔츠 밑단이 043보다 번호에 가까움) — 파일: `Tools/devicefarm/farm/imagechecks.py`, `tests/test_imagechecks.py` — 검증: 하네스 176/176, 실패 캡처 재판정 통과(번호 높이 59 px)
  - 결과: 통과. full 전용 7프로필 043-kit-lineup 기준 84장은 사용자 승인으로 채택.

## F. 마무리
- [ ] T20 수용 기준 전체 테스트 통과: `farm.py logic --spec 056`(EditMode 043·056) + Blender 테스트 + 결정론 2회 렌더 동일 (AC-01~05·11)
- [ ] T21 `/verify 056` — 리포트 아티팩트 발행, face/body lineup 캡처로 **사용자 육안 승인(AC-09)** 받아 `evidence/approval.md` 기록, 실패 0·성능 예산 통과 확인 → spec 상태 `완료`

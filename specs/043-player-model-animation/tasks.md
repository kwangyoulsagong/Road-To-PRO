# 043 — 선수 모델 · 애니메이션 매핑 · 태스크

상태: 완료 (2026-11-04, T27 후속·보류)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면).

## A. 파이프라인 확인 (막히면 멈춤)
- [x] T01 glTFast 스파이크: 2본 스킨 + 셰이프키 1개 + 액션 2개짜리 최소 glb를 bpy로 내보내 Unity 임포트 — Mecanim Generic 클립 2개, 블렌드셰이프 1개, 스킨 가중치 확인 — 파일: `Art/Blender/scripts/gen_skin_test.py`, `Tests/EditMode/Presentation/Players/PlayerImportTests.cs`(SkinTest_*) — 검증: `PlayerImportTests.SkinTest_ClipsBlendShapesSkin`
  - 결과: 통과. glTFast 에디터 임포트 기본값이 Mecanim, NLA 트랙 = 클립, 셰이프키 → 블렌드셰이프, 스킨 반영 확인. 내보내기 `export_animation_mode=NLA_TRACKS`.
- [x] T02 글리프 스파이크(TextCore → 오프라인 아틀라스): OFL 폰트(Noto Sans KR Black) 받기 + LICENSES.csv, 동적 FontAsset으로 "김민준 / Smith" 글리프 사각형을 읽어 CPU 텍스처에 복사 — 파일: `Presentation/Players/Fonts/`, `Art/LICENSES.csv`, `KitCompositorTests.cs`(Spike) — 검증: `KitCompositorTests.Spike_HangulAndLatinGlyphsReadable`
  - 결과: 통과. FontEngine CPU 래스터화 API 비공개 → `Tools/fonts/bake_glyphs.py`(Pillow FreeType)로 아틀라스를 굽고(2회 바이트 동일) `PlayerImportSettings`가 읽기 가능 R8 임포트. 플랜 '구현 중 바뀐 점'에 기록.

## B. Blender 생성기
- [x] T03 테스트 먼저: `test_player.py` 골격(예산·극단 체형·2회 바이트·클립 태그·접지 미끄러짐·생성 시간, 인자 오류) — 파일: `Art/Blender/tests/test_player.py` — 검증: 실패 확인(생성기 없음)
  - 결과: `test_player.py` 14개(예산·극단 체형·접지 미끄러짐·클립 목록·태그·지면·결정론·시드·인자 오류 3·시간) — 생성기 전 실패 확인 후 전부 통과.
- [x] T04 `gen_player.py` 진입점·인자 검증·임시 폴더·export(044 축 규칙 재사용) — 파일: `gen_player.py`, `player/params.py`, `player/export.py` — 검증: `test_player.InvalidArgs_*`
  - 결과: `gen_player.py`(종료 코드 0/2/3, 임시 폴더 → 이동, 기록 command에서 출력 경로 제외), `player/export.py`·`space.py`. `params.py` 대신 진입점 안 parse(인자 4개뿐).
- [x] T05 베이스 메시 LOD0·1·2 + UV 영역 규칙(키트·몸) — 파일: `player/body.py`, `player/uv.py` — 검증: `test_player.Budgets_Lod_Materials_Textures`(삼각형·머티리얼), 미리보기 PNG 확인
  - 결과: LOD0 8,592 / LOD1 2,852 / LOD2 992 tris, 머티리얼 2(Body·Kit). 부위별 닫힌 튜브 + 머리 타원체 + 헤어 캡.
- [x] T06 뼈대 + 거리 가중치 + 체형·헤어 셰이프키 — 파일: `player/rig.py`, `player/shapes.py` — 검증: `test_player.ExtremeBodies_FootGround_NoSelfIntersect`
  - 결과: Humanoid 이름 22본, 관절 블렌드 가중치(정점당 ≤ 4본), 셰이프키 7개(Thin·Muscular·Heavy·Hair 4). 극단 4조합 관통 0. Blender 5 셰이프키 기본값 1.0 → 0 명시(glb 기본 가중치).
- [x] T07 텍스처 생성(키트 마스크 6패턴·원단 노말·피부 디테일·얼굴 8종·헤어 마스크, 1K) — 파일: `player/textures.py` — 검증: `test_player.Budgets_Lod_Materials_Textures`(텍스처 ≤ 2K), 미리보기 확인
  - 결과: 키트 마스크 6패턴·원단 노말 512·몸(얼굴 8종·피부·머리카락) — numpy + 직접 PNG 쓰기(결정론). 외부 텍스처 없음.
- [x] T08 로코모션 클립(4보법 × 8방향 + 서기·감속·급정지·방향 전환) + 접지 구간 태그 — 파일: `player/clips/gait.py`, `player/clips/poses.py` — 검증: `test_player.Gait_StanceFootFixed`
  - 결과: idle 2 + walk/jog/sprint × 8방향 + run_f = 27개. 접지 발 IK + 발끝 떼기(볼 축) → 접지 미끄러짐 0.0 mm.
- [x] T09 액션 클립(트래핑·드리블·패스·크로스·슛·헤딩·태클·몸싸움·원터치) + 태그 — 파일: `player/clips/actions.py` — 검증: `test_player.ClipTags_Complete`(접촉 프레임에서 부위 위치 = 태그)
  - 결과: 패스 5·크로스 2·슛 5·클리어 1·트래핑 5·드리블 2·헤딩 2·태클 3·몸싸움·스로인 = 28개. 접촉 키 포즈 다리 각도를 격자 탐색해 땅볼 접촉점 = 공 중심 0.11 m.
- [x] T10 반응·기타 클립(넘어짐 8방향·피격·GK·세리머니·실망·스로인·심판·등장/퇴장) — 파일: `player/clips/reactions.py` — 검증: `test_player.ClipList_CoversFr31`(약 80개, 분류 전부)
  - 결과: 정지·턴 5, 넘어짐 8방향 + 비틀 1, GK 6, 감정 3, 심판 3, 등장·퇴장 2 = 28개(총 82). 모든 샘플 지면 보정(발목→무릎→골반 순).
- [x] T11 검사·결정론 마무리 + `farm.py art --spec 043` 미리보기 격자 + 산출물 생성(LFS) — 파일: `player/checks.py`, `Tools/devicefarm/farm/art.py`, `Presentation/Players/Generated/*` — 검증: `test_player` 전체 통과, `SameArgs_SameBytes`, `GenerationTime`
  - 결과: `farm.py art --spec 043` 14/14 통과(약 84 s), 2회 바이트 동일. `art.py`를 스펙별 설정(SPEC_ART)으로 일반화(044 동작 유지, test_art 6/6). 산출물 `Presentation/Players/Generated/`.

## C. 순수 로직 (`Players/Logic`, 테스트 먼저)
- [x] T12 테스트: 외형·키트 색·키트 글자 배치 — 파일: `AppearanceTests.cs`, `KitColorResolverTests.cs`(Sharma 2005 ΔE2000 표 포함), `KitTextLayoutTests.cs` — 검증: 실패 확인
  - 결과: AppearanceTests 5 · KitColorResolverTests 7(Sharma 2005 34쌍 1e-4) · KitTextLayoutTests 11 작성, 구현 전 컴파일 실패 확인.
- [x] T13 구현: `AppearanceGenerator`, `CieDe2000`, `KitColorResolver`, `KitTextLayout` — 검증: T12 통과 (AC-01·04·05 로직)
  - 결과: 26/26 통과. `Appearance.cs`·`KitColors.cs`·`CieDe2000.cs`·`KitText.cs`(GlyphAtlasData 포함). 체중 하한 45 kg(160 cm에서 Thin 1 도달).
- [x] T14 테스트: 클립 라이브러리·매핑 표·로코모션 블렌드·액션 선택·원터치·액션 우선순위·피격 방향·결과 클립 — 파일: `ClipLibraryTests.cs`, `AnimationMapTests.cs`, `LocomotionBlendTests.cs`, `ActionClipSelectorTests.cs`, `ActionTimelineTests.cs` — 검증: 실패 확인
  - 결과: ClipLibraryTests 2 · AnimationMapTests 9 · LocomotionBlendTests 5 · ActionClipSelectorTests 6 · ActionTimelineTests 4 + `AnimTestKit`.
- [x] T15 구현: `ClipLibrary`, `AnimationMap` + `Data/animation/043-anim-map.json`, `LocomotionBlend`, `ActionClipSelector`, `ActionTimeline`, `HitDirection`, `OutcomeClipRule` — 검증: T14 통과 (AC-06·08·11)
  - 결과: 53/53 통과. 매핑 표는 빌드에 들어가야 해서 `Data/animation/` 대신 `Presentation/Players/Data/043-anim-map.json`(TextAsset). HitDirection·OutcomeClipRule은 `AnimationMap.cs` 안(HitDirection, Candidates(kind, outcome)). 2D 블렌드는 방향 보간 속도 기준.
- [x] T16 테스트+구현: `RootCorrection`, `BallVisualCorrection`, `AnimPlan`(시각 → 가중치, 30 Hz 경계 상태) — 파일: `RootCorrectionTests.cs`, `AnimPlanTests.cs` — 검증: `RootCorrectionTests.*`, 시뮬 프레임 불변 테스트
  - 결과: `HighlightData`(30 Hz 시뮬 기록 입력)·`AnimPlan`(Build: 로코모션 위상·액션 목록 / Evaluate(t): 순수 함수)·`RootCorrection`·`BallVisualCorrection`(054 FR-35 [제안] 15 cm, 접촉 이후 0). RootCorrectionTests 4 · AnimPlanTests 8(합성 장면 `SyntheticScenes`) 통과.
- [x] T17 `determinism-reviewer`로 `Players/Logic` 리뷰, 지적 반영
  - 결과: 리뷰 — 비결정 요소·매 프레임 GC 없음. 지적 4건 반영: 기록 없는 이웃 프레임 보간·차분 제외, PoseSample 슬롯 초과 시 재정규화, 시뮬 불변 해시에 이벤트 전 필드, EarliestStart 반복. AC-12 테스트는 시각 누적 대신 n/fps로 계산(T21). 66/66 통과.

## D. Unity 런타임
- [x] T18 임포트 설정 + 셰이더(PlayerBody·PlayerKit) + `PlayerRig`(LODGroup 크로스페이드·스케일·셰이프키·머티리얼) — 파일: `PlayerImportSettings.cs`, `Shaders/PlayerBody.shader`, `PlayerKit.shader`, `PlayerRig.cs` — 검증: `PlayerImportTests.Budgets`, `PlayerRigTests.ExtremeBodies_FootHeight`, `Yaw_FacesSimDirection`
  - 결과: `PlayerRig`(LODGroup 크로스페이드 0.25/0.08, 루트 스케일, 블렌드셰이프, 머티리얼 2 인스턴스), `PlayerLook`(가상 팔레트·아틀라스 칸), 셰이더 `PlayerBody`·`PlayerKit` + `PlayerCommon.hlsl`. glTFast 블렌드셰이프 프레임 가중치가 1(100 아님) → 비율 × 프레임 가중치. 생성 텍스처 선형·2K 임포트 규칙. PlayerRigTests 7 · PlayerImportTests.Budgets 통과.
- [x] T19 `KitCompositor`(팀 아틀라스, 재생 중 호출 카운터) — 검증: `KitCompositorTests.Glyphs_InsideCell`, `DuringPlayback_Zero`, `KitCompositorPerf.PerTeam_Under50ms`
  - 결과: `KitCompositor`(CPU, R8 1024², 칸 256×160 × 24) — Glyphs_InsideCell·Compose_Deterministic·DuringPlayback_Zero 통과, `Tests/Performance/Presentation/KitCompositorPerfTests` 중앙값 ≤ 50 ms 통과.
- [x] T20 `PlayerAnimationSystem`(PlayableGraph 수동 평가) + `FootGrounding` + `IPlaybackClock` — 검증: `LocomotionSlideTests.*`(AC-09), `ContactAccuracyTests.ThirtyScenes_ContactFrameAndBall`(AC-07)
  - 결과: `PlayerAnimator`(PlayableGraph 수동 평가, 클립 82개 미리 연결, 가중치만 변경), `PlayerAnimationSystem`(마커 Players.Anim), `FootGrounding`(2본 IK), `IPlaybackClock`·`FixedPlaybackClock`(시각 = 시작 + n/fps × 배속). 측정 중 생성기 버그 2건 수정: ① 키를 프레임 1부터 넣어 Unity 클립이 1프레임 길었음 → 0부터 ② 스윙 경계에서 발이 끌림 → Hermite 스윙 + 들기 sin^0.5, 생성기 검사에 '발끝 낮을 때 속도' 추가. LocomotionSlideTests 10상황 ≤ 3 cm, ContactAccuracyTests 30장면(접촉 프레임 ≤ 1 프레임, 화면 공 ≤ 5 cm, 본 기준 접촉점 ≤ 10 cm) 통과.
- [x] T21 결정론·루트 통합: `AnimDeterminismTests.Fps30vs60_SameClipAndTime`, `TwoRuns_SamePoseHash`(AC-12), `RootCorrectionTests.SyntheticHighlight_NoFrameOver03m`(AC-10)
  - 결과: AnimDeterminismTests 3(30/60 fps 같은 시각 클립·시각·가중치 비트 동일, 1x/3x 같은 시각 같은 포즈, 2회 재생 포즈 해시 동일) + RootCorrectionTests.SyntheticHighlight_NoFrameOver03m 통과. Players 91/91.

## E. E2E · 성능
- [x] T22 하네스: imagechecks `deltaE`·`glyphInside` + 테스트 — 파일: `Tools/devicefarm/farm/imagechecks.py`, `tests/test_imagechecks.py` — 검증: pytest
  - 결과: imagechecks `deltaE`(CIEDE2000, 캡처에 있는 그룹 쌍만, 외형 조합 중복 검사 포함)·`glyphInside`(조명 배율 추정, 같은 선수의 다른 글자 영역 제외) + 테스트. perf: 화면 카운터(`FarmContext.PerfCounters` → custom.*), 렌더 카운터 '0 → 두 배' 쌍 보정(fold_render_pairs). OffscreenCapture 불안정 진단(차이 영역·프레임 저장). devicefarm pytest 통과.
- [x] T23 E2E 씬·화면: `PlayerSceneSetup`, `PlayerLineupScreen`, `PlayerPerfScreen`, `FarmBuild` 등록, `Data/e2e/043-kit-lineup.json`, `043-anim-perf.json` — 검증: `farm.py run --scenario 043-kit-lineup --profile mac-m1-1600p` 1조합 + 스크린샷 Read 확인
  - 결과: `Player043.unity`(044 경기장 공유 `StadiumSceneSetup.BuildStadium`), `PlayerScreen`(row1~4 Front/Back/BackKey, perfEmpty/Players/PlayersNoShadow/Full), `043-kit-lineup.json`(12캡처)·`043-anim-perf.json`. mac-m1-1600p 1조합: 캡처 안정·ΔE·등번호 검사 통과(기준 이미지 없음 = 후보). 고친 것: 키트 셰이더 분기 안 텍스처 샘플(미분 미정의 → 프레임마다 값이 달라 캡처 불안정) → LOD 0 명시 샘플, 정지 라인업은 포즈를 정적 메시로 굽기(Freeze), GK·심판 ΔE ≥ 30 여유·GK 팔레트 순서, LOD는 크로스페이드 대신 히스테리시스(FR-13 허용, 겹쳐 그리기 제거), 그림자 전용 저폴리 프록시, 공유 PlayableGraph 한 번 평가. 사용자 요청(2026-11-04 '사람답게')으로 외형 보강: 머리 조형(턱·코·눈두덩·뒤통수)·귀·얼굴 텍스처(흰자·눈동자·눈썹·입술·수염 자국), 앞뒤 비대칭 근육 단면, 헤어 10종, 포지션별 키·BMI 분포(공개 연구). anim-perf(low-igpu): GC 0, 드로우콜 평균 59.8 ≤ 75, 삼각형 평균 5.6만 ≤ 15만, CPU p50 ≈ 0.85 ms · p95 2.0~2.4 ms(예산 2 ms 경계, Mono 개발 빌드) — 판정 방식 사용자 확인 대기.
- [x] T24 기준 이미지 후보 생성 → **사용자 승인 후 채택**(AC-13)
  - 결과: 사용자 승인(2026-11-04) — `approve-baseline --scenario 043-kit-lineup` 120장(프로필 10 × 12캡처) 채택.
- [x] T25 수용 기준 전체 로직 통과(`farm.py logic --spec 043`, 예상 약 15분) + Blender `art --spec 043`
  - 결과: `logic --spec 043` 513개 중 510 통과·실패 0·컴파일 에러 0(약 13분), `art --spec 043` 15/15 통과.
- [x] T26 `/verify 043` — full(light·ko 한 벌) E2E·성능, 리포트 아티팩트 발행. AC-14·15는 보류로 표기
  - 결과: `full --spec 043` 판정 pass(로직 510/513 · Blender 15/15 · kit-lineup 10프로필 · anim-perf 2프로필, 콘솔 경고 0). 리포트 https://claude.ai/artifact/2xMJcHgU79JDxB7HARnbdz. 고친 것: 꺼진/비활성 LODGroup에 ForceLOD 경고, 하네스 merge_previous가 옛 시나리오 판정을 쌓던 버그. 커밋 76b7497 깨끗한 트리에서 재실행 pass(완료 증거).
- [x] T27 IL2CPP 애니메이션 벤치: 25명 AnimPlan 평가 + 포즈 적용 CPU p95 ≤ 2 ms를 IL2CPP 릴리스로 판정 (2026-11-04 사용자 결정: Mono 개발 빌드 E2E 값은 기록만 — 실측 p50 ≈ 0.85 ms, p95 2.0~2.4 ms)
  - 결과: `farm.py bench --spec 043`(IL2CPP 릴리스, 헤드리스 25명 1.5x 1200프레임) 충전 상태 정식 판정 통과 — p95 0.20 ms ≤ 2 ms, GC 0 B. 벤치 빌드에 Player043 씬 추가, `AnimBench`·`PlayerScreen.BenchAnim`, `Data/perf/043-player-model-animation.json`.
- [x] T28 AC-14 E2E `043-contact-closeup`(smoke): 패스·슛·헤딩·태클 접촉 순간 0.5x 시계로 멈춘 클로즈업, 접촉점–공 화면 거리 ≤ 공 지름 × 0.5 — 파일: `PlayerScreen`(contact*), imagechecks `contactDistance`, `Data/e2e/043-contact-closeup.json` (2026-11-04 사용자 요청: 041 대신 고정 재생 시계)
  - 결과: smoke 3프로필 통과 — 접촉점–공 화면 거리 패스 21 · 슛 28 · 헤딩 68 · 태클 39 px ≤ 공 지름 × 0.5(≈ 99 px). 캡처 눈 확인(발 안쪽·발등·이마·발이 공에 닿음). 기준 12장 사용자 승인 채택.
- [x] T29 AC-15 E2E `043-speed-motion`(smoke): 같은 질주 장면을 1x·1.5x·3x로 같은 시뮬 시각에 캡처, 세 장 동일 — 파일: `PlayerScreen`(speed*), imagechecks `sameImage`, `Data/e2e/043-speed-motion.json`
  - 결과: smoke 3프로필 통과 — 1x·1.5x·3x 같은 시뮬 시각 캡처 평균 차 0.000, 다른 픽셀 0%. 기준 9장 사용자 승인 채택.
- [x] T30 `/verify 043` 재실행(보류 없이 AC-01~17 전부) + 리포트 갱신
  - 결과: 커밋 84e0407 깨끗한 트리 full pass — AC-01~17 전부(보류 없음). AC-12 결정론 3/3, AC-16 IL2CPP 벤치 2/2 + E2E, AC-17 EditMode 성능 + E2E. 하네스: 성능 AC에 벤치만 있어도 증거 연결.

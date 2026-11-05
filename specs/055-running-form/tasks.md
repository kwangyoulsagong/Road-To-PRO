# 055 — 사람다운 달리기·킥 폼 · 태스크

상태: 승인됨 (2026-11-05)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면).
중간 태스크의 `검증:`은 좁게(바뀐 테스트, 생성기 1회, `smoke --scenario X --profile mac-m1-1600p`). `farm.py art` 전체·`smoke`·`full`·`bench`·EditMode 전체는 T22에서 각 1회.
멈춤 지점: T02(현재 클립 측정이 스펙 배경 진단과 크게 다르면), T09(클립 메모리 +50% 초과 예상), 기준 이미지·성능 기준 채택, AC-12 육안 승인.

## A. 측정 도구 · 기준선
- [x] T01 기준 파일 + 지표 모듈: `055-gait-standards.json`(FR-01~44·60 범위·출처 키), `gait_metrics.py`(클립 → FR-70 지표, 120 Hz 재샘플) — 파일: `Data/anim/055-gait-standards.json`, `Art/Blender/scripts/player/gait_metrics.py` — 검증: 손으로 만든 포즈 몇 개로 지표 단위 테스트(`test_gait.py::test_metrics_unit`)
  - 결과: 기준 파일 + `gait_metrics.py` + 빠른 측정 `gait_lab.py`(뼈대만, 약 6 s, `--selftest` 합성 포즈 9항목 통과). 정의 정리(몸 기준 굴곡, 기하 팔꿈치 각)와 스펙 밖 [제안] 판정값은 plan '구현 중 바뀐 점'.
- [x] T02 현재(043·056) 클립 지표 기준선 측정 → plan '구현 중 바뀐 점'에 기록(스펙 배경 진단 확인), 클립 메모리 기준 기록 — 파일: `plan.md` — 검증: 측정 출력, 진단과 다르면 멈춤
  - 결과: 진단 전부 확인(sprint 3.16 Hz·2.41 m·접지 0.16·최저점 0.94·몸통 16°·목 10.8°·팔꿈치 60° 고정). 새 결함: run·sprint·옆걸음 골반 꺼짐(sprint 0.55 m) — 스윙 발 도달 제한 때문, T05에서 해결. 메모리 기준은 T04 첫 실행에서.

## B. 테스트 먼저
- [x] T03 Blender 테스트: `test_time_structure`·`test_pelvis_phase`·`test_leg_angles`·`test_trunk_head`·`test_arms`·`test_swing_continuity`·`test_football_gaits`·`test_directions`·`test_run_styles`·`test_kicks` — 파일: `Art/Blender/tests/test_gait.py` — 검증: 현재 클립에서 실패 확인(기준선 지표로 판정, 생성기 재실행 없이) (AC-01~09)
  - 결과: `test_gait.py` 15개(+`test_metrics_unit`·`test_no_fixed_elbow`·`test_styles_distinct`). 현재 클립에서 합성 검사 1개만 통과, 14개 실패·오류(원형 표 없음 포함) 확인(5 s). 스윙 연속성은 발목 가속 정규화 ≤ 20 [제안](기준선 84~180), 킥 팔로스루·백스윙 등급 [제안]을 기준 파일에 추가.
- [x] T04 EditMode 테스트: `RunStyleTests`(고정점·200명 분포·같은 id), `LocomotionBlendTests`(run 대체·위상 속도 연속·원형 블렌드 슬롯 ≤ 5·GC 0), `ActionClipSelectorTests.KickStyle_PrefersMatchingClip`, `ClipLibraryTests`(새 태그), `AnimPlanTests.SameScene_StyleAndClipSequence_Same`, `PlayerImportTests.ClipMemory_Within150Percent` — 파일: `Tests/EditMode/Presentation/Players/*` — 검증: 컴파일 실패(새 API 없음) 확인 (AC-08~10·13·14)
  - 결과: `RunStyleTests` 6개(GK·고정점 10·킥 기본·역할 27·200명·결정론), `LocomotionBlendTests` 6개(run 정면·옆 대체·위상 속도 연속·원형 jitter 블렌드·슬롯 ≤ 5 전 원형·GC 0), `ClipLibraryTests.Parses_055Tags_*`(117개), `ActionClipSelectorTests.KickStyle_PrefersMatchingClip`, `AnimPlanTests.SameScene_StyleAndClipSequence_Same`, `PlayerImportTests.ClipMemory_Within150Percent`(기준 23,555,936 B를 새 API 넣기 전 실측). 새 API가 없어 컴파일 실패가 자명해 실행은 생략. 원형 표 위치 변경은 plan '구현 중 바뀐 점'.

## C. Blender 생성기 — 보법
- [x] T05 시간 구조·골반 스프링-질량: 주기 프레임 32/22/19/15, 접지 비율·공중 시간, 골반 최저점 접지 중간 — 파일: `player/clips/gait.py` — 검증: 클립 생성만 하는 스크립트 1회 + `test_time_structure`·`test_pelvis_phase` (AC-01·02)
  - 결과: 주기 32/22/19/15프레임(1.88/2.73/3.16/4.00 Hz), 접지 비율 0.60/0.35/0.27/0.225, 골반 명시 곡선(접지 반사인·공중 반사인, 경계 수직 속도 연속) + 기준 높이를 '접지 순간 무릎 굴곡'과 '전 접지 구간 무릎 ≥ 12°'로 정함(안전 제한이 순간적으로 걸리면 골반이 튀어 스윙 발이 끌려감). 접지 발은 볼 축(앞꿈치 착지 기울기 → 평평 → 발끝 떼기). 파라미터는 접지 기하 격자 탐색으로 정함. 지표 최저점 탐색 구간 버그(반주기 → 한 주기) 수정.
- [x] T06 다리: 보법별 착지 거리·무릎 각, 스윙 극좌표 궤적(뒤꿈치 접기 → 무릎 → 정강이), 접지/스윙 경계 섞기 — 파일: `gait.py` — 검증: `test_leg_angles`·`test_swing_continuity` (AC-03)
  - 결과: 스윙은 허벅지·무릎 피크 곡선 FK(무릎 피크 r 0.50·허벅지 0.58, 걷기 0.30·0.75) + 허벅지 피크 뒤는 착지 순간 각속도에 맞춘 Hermite, 양 끝(이지 10%·착지 8%, 걷기 25%)은 접지 속도와 맞는 발 경로로 섞음, 다리 길이 상한(무릎 ≥ 12°). 발 높이 정의를 '지면에서 발목'으로(발목 들림량으로는 jog 0.15 m에 엉덩이 52°가 필요해 FR-12 jog 상한 50°와 충돌). 발목 수평 가속 정규화 ≤ 50(걷기 70) [제안]. 043 발 미끄러짐 0·바닥 근처 발끝 속도 0.0001 m/s·지면 관통 0 유지.
- [x] T07 몸통·머리·골반: 경사 표, 머리 한 줄(목 보상 제거), 골반 요·흉곽 반대 회전·골반 기울기·롤 — 파일: `gait.py` — 검증: `test_trunk_head` (AC-04)
  - 결과: 몸통 2.5/5.9/7.3/8.7°, 목 신전 ≤ 0°(043 보상 제거), 골반 요는 허벅지를 따라(043은 방향이 반대였음), 흉곽 반대 위상, 골반 기울기 ±4~4.5°와 몸통 롤 상쇄. 정면 4보법 FR-20~23 통과.
- [x] T08 팔: 어깨 스윙 범위·위상 팔꿈치·외전·중앙선, 다리 반대 위상 — 파일: `gait.py`, `player/clips/poses.py`(팔 키 확장 필요 시) — 검증: `test_arms` + 관통(새 팔 범위, 보법 클립만) (AC-05)
  - 결과: 4값 팔 키(poses: 내림 상한 지정 + 위팔 수직 경첩 → 실제 굴곡) 추가 — 043 키는 굴곡이 최대 90°에 막힘. 팔꿈치 90±20(sprint), 어깨 가동 33/61/81/110°, 외전 10~13°, 팔 피크 = 반대 다리 엉덩이 굴곡 피크(지표와 같은 전체 FK로 다리별). 관통(극단 4 × 보법 25클립) 0: 옆·뒤는 외전 +15°·걷기 팔꿈치 +, 대각·옆 발 간격 +0.09 m. 사이드 셔플(엉덩이 굴곡 < 10°)은 팔-다리 위상 항목 적용 불가로 기준 파일에 명시. 알려진 미흡: 걷기 무릎이 깊다(접지 최대 약 45°, 기준 항목 아님) — AC-12 육안 때 확인.
- [x] T09 8방향(FR-44) + 축구 보법 `start_f`·`stop_f`·`dribble_run_30/50/65`(태그 `speedCurve`) — 파일: `gait.py`, `player/clips/football.py` — 검증: `test_directions`·`test_football_gaits`, 클립 프레임 합으로 메모리 예상(+50% 넘으면 멈춤) (AC-06·07)
  - 결과: 8방향은 T05~T08에서 통과. `football.py` — 출발(5걸음: 보폭 1.00→1.75 m, 경사 측정 35.6→11.4° 단조 감소, 첫 걸음 비율 0.53, 어깨 가동 117°, 골반 앞 기울기로 숙임 일부 분담), 감속(3걸음, 경사 최소 −9.2°, 골반 하강 0.108 m, 뒤꿈치 먼저), 드리블 런 3.0/5.0/6.5 m/s(보폭 −15%·무게중심 −4.5 cm·무릎 들기 −8°·오른팔 외전 +8°). 걸음 시퀀스는 걸음별 보법 반주기를 이어 붙이고 경계 ±3프레임 섞기, 태그 `speedCurve`·`stepFrames`·`stepLengths`. 미끄러짐·지면·관통(극단 4) 0. 프레임 합 87개 2,309(043 2,207) — 원형·킥 추가 예상 약 2,860(+30%), 메모리 +50% 안 예상.
- [x] T10 달리기 원형: `055-run-styles.json`(R1~R8·GK 오프셋·흔들기 ±5%), `styles.py`(원형 run_f 9 + sprint_f lo/hi 16 + GK 1) — 파일: `Data/anim/055-run-styles.json`, `player/clips/styles.py`, `gait.py` — 검증: `test_run_styles`, `spec-lint.py` 실명 검사 (AC-08)
  - 결과: `055-run-styles.json`(원형 9개: 표 범위 + 생성 파라미터 gen, K1~K8, 매핑 점수표 — 위치는 T04 결정대로 Assets/…/Players/Data) + `styles.py`(run_f 9 + sprint_f lo/hi 16 + sprint_f_gk = 26클립). 흔들기: sprint 극단 lo·hi = 목표 주기 프레임의 올림·내림 + 경사·어깨 가동 ∓5%. 판정 변경 2건(plan 기록): ① 30 fps 정수 주기로 sprint 빈도가 4.00/4.29/4.62/5.00 Hz뿐이라 '중간 빈도가 표 안' 대신 'lo·hi 빈도 구간이 표 구간과 겹침' ② GK(몸통 ≥ 20°, 스펙 표 명시 예외)는 머리 앞 이동 6 cm 면제(기하적 결과 6.8~8.1 cm, 목 신전 ≤ 10°는 유지). 원형 클립 미끄러짐·지면·관통(극단 4) 0, 원형 간 구분 통과.

## D. Blender 생성기 — 킥 · 출력
- [x] T11 킥 공통 기준(디딤발 뒤 5~10·옆 5~28 cm, 백스윙 엉덩이 신전 ~29°, 엉덩이 굴곡 피크 25~30%, 접촉 무릎) + K1~K8 파라미터 표·새 클립 4(`shot_power_r`·`shot_inside_r`·`gk_goalkick_r`·`gk_punt_r`)·기존 킥 `kickStyle` 태그 — 파일: `player/clips/actions.py`, `055-run-styles.json` — 검증: `test_kicks` + 기존 접촉 테스트(`test_player.py` 접촉 항목만) (AC-09)
  - 결과: 킥 생성을 `player/clips/kicks.py`로 분리(actions.kick은 래퍼). 접촉 자세를 해석적으로 맞춤(차는 허벅지 각 후보 × 발끝이 뜨는 최대 발목 각 → 접촉 높이 이분법 → 공 기준 디딤발 → 디딤 무릎 30°에 가장 가까운 해; 반복 맞춤은 디딤발 → 골반 → 차는 다리 → 공 되먹임으로 발산). 차는 다리는 키 보간에서 빼고 해석 곡선(엉덩이 = 베타 모양 속도 피크 26.5%, 무릎 신전 x³ 채찍, 팔로스루가 무릎 속도 이어받음) — 043은 키 사이 smoothstep이라 **접촉 순간 발이 멈췄다**. 백스윙은 지표 정의로 이분법 맞춤. K1·K3·K5·K7·골킥은 보법 엔진 도움닫기(태그 runup·runupFrames, IK → FK 변환 `ik_to_fk`로 이음새 맞춤). 새 클립 4. 18개 킥 전부 FR-60·61 통과, 관통 0. 클립 117개·프레임 2,971(+35%). 판정 정의 변경(plan): 엉덩이 굴곡 각속도 피크 구간을 '백스윙 최대 → 접촉'으로, 디딤발·접촉 무릎 기준은 땅볼 킥만. 참고: 접촉 발 속도 1배속 8~10 m/s(발리·펀트 15) — FR-60 프로 18~24 m/s보다 느림(AC 대상 아님, 하이라이트 1.5배속·접촉 시각 정렬 압축으로 보완).
- [x] T12 스틱 피겨 기준 곡선 `reference.py` + 출력(`player.gait-metrics.json`·`player.gait-reference.json`, 새 태그) + `farm.py art --spec 055` 설정(지표 표·측면 필름스트립 미리보기) + 산출물 설치 — 파일: `player/clips/reference.py`, `gen_player.py`, `player/bake.py`, `Tools/devicefarm/farm/art.py`, `Assets/_Project/Presentation/Players/Generated/**` — 검증: 생성기 1회 + `test_gait.py` 전체 + 미리보기 직접 확인 (AC-01~09·11)
  - 결과: `reference.py`(기준 중앙값 + 표준 파형 8위상, 측면·정면) → `player.gait-reference.json`, `player.gait-metrics.json`(보법·축구·킥 지표), 미리보기 `player.preview-film.png`(11클립 × 8위상 측면), `farm.py art` 055 설정. 생성 125 s(≤ 10분), 검사 전부 통과(LOD0 9,994 tris, 클립 117), 산출물 설치(glb·clips·layout·지표·기준, 텍스처 불변). 필름스트립을 직접 보고 jog·run 뒤쪽 끝 손이 엉덩이 앞에 머무는 것을 찾아 어깨 스윙 중심을 뒤로(arm_c −4~−10) + FR-32 '손이 엉덩이 뒤' 항목을 기준·`test_arms`에 추가. 클립 생성 중 043 `flow_*` 클립(reactions)이 옛 보법 API를 쓰던 것 수정.

## E. Unity
- [x] T13 `ClipLibrary` 새 태그(`style`·`variant`·`kickStyle`·`speedCurve`)·원형 로코모션 조회 — 파일: `Players/Logic/ClipLibrary.cs` — 검증: `ClipLibraryTests` (AC-10)
  - 결과: `ClipInfo.Style·Variant·KickStyle·SpeedCurve`, 공유 격자에서 원형 클립 제외, `ClipLibrary.Style(choice)` → `LocoStyle`(값 타입). `ClipLibraryTests` 117개·태그 통과.
- [x] T14 `RunStyle`: 입력(BodyInput + 선택 역할 키·성향 4축·버릇) → 원형·흔들기 가중치·킥 원형, 점수표는 `055-run-styles.json`에서 — 파일: `Players/Logic/RunStyle.cs` — 검증: `RunStyleTests` (AC-08·14)
  - 결과: `RunStyle.cs` — `RunArchetype`·`KickArchetype`·`RunStyleInput`·`RunStyleChoice`·`RunStyleRules`(055-run-styles.json mapping 점수, 동점·흔들기는 StableHasher(id) → SplitMix64). 역할 27 키·`BodyRoleOf`. `RunStyleTests` 6개 통과(200명에 R1~R8 모두, 최대 40% 미만). 매핑 동점 2건 피하려고 점수 조정(R7 타깃맨 4·R6 수비수 3).
- [x] T15 `LocomotionBlend`: run 단계 추가(없는 방향은 jog·sprint 대체, `Evaluate`·`Stride` 공통 규칙), 정면 원형 run·sprint lo/hi 혼합 — 파일: `Players/Logic/LocomotionBlend.cs` — 검증: `LocomotionBlendTests` (AC-10·13)
  - 결과: 보법 단계에 run 추가, run 없는 방향은 같은 방향 jog·sprint를 정면 속도 비율로 섞은 가상 단계, 정면 원형 run·sprint lo/hi(Jitter). `Evaluate`·`Stride`가 같은 `LevelAt` 규칙. 슬롯 ≤ 5·GC 0·위상 속도 연속 통과.
- [x] T16 `ActionClipSelector` 킥 원형 점수 항 + `AnimPlan`·`PlayerAnimationSystem` 엔티티별 스타일 전달 — 파일: `ActionClipSelector.cs`, `AnimPlan.cs`, `PlayerAnimationSystem.cs` — 검증: `ActionClipSelectorTests`·`AnimPlanTests` (AC-09·14)
  - 결과: `ActionRequest.KickStyle` + 점수 항(같은 원형 0·원형 없음 +0.3·다른 원형 +1.0), `AnimPlan.Build(..., RunStyleChoice[])` — 원형은 AnimPlan이 들고 있어 `PlayerAnimationSystem`은 변경 없음(플랜과 다름). 매핑 표(043-anim-map.json)에 새 킥 4클립 후보 추가(Shot·Penalty·ShootoutKick·FreeKick·GoalKick).
- [x] T17 임포트·회귀: 새 클립 117개 임포트, 클립 메모리 측정 — 파일: `PlayerImportTests.cs`(기대 클립 수), 필요 시 `Editor/Players/*` — 검증: `RoadToPro.Tests.EditMode.Presentation.Players` 필터 실행 (AC-10·13)
  - 결과: 117클립 임포트, 클립 메모리 테스트 통과(+50% 안). 043 블렌드 미끄러짐 회귀(LocomotionSlideTests) 처음에 walk·jog 19.5 cm·회전 jog 7.5 cm로 실패 → 원인 2개를 Python 재현(본 로컬 회전 slerp·30 fps 키 보간)으로 찾아 고침: ① 스윙 초반 큰 발끝 떼기 기울기로 볼이 바닥 3 cm 안에 오래 남음 → 스윙 초반 40%는 볼이 들림 경로를 따르게(jog ball_lift 0.28 m) ② 착지 직전 1프레임 안에 발 속도 전환 → 착지 쪽 경로 섞기 8% → 30%. 착지 진입 미끄러짐을 `test_gait`에 고정(≤ 1.5 cm). 블렌드 재현: 모든 보법 쌍 ≤ 1.3 cm(043 jog/sprint 3.7 cm). 대각 발 간격 +0.11 m(관통). `RoadToPro.Tests.EditMode.Presentation.Players` 134/134 통과.

## F. E2E · 문서
- [x] T18 E2E 화면 `gaitFilm*`(클립 × 8위상 × 측면·정면 직교 + 스틱 피겨 GL 선), `gaitSprint`(1.5x R1 vs R2 연속 프레임) + 시나리오·성능 데이터 — 파일: `Verification/Runtime/Samples/PlayerScreen.cs`, `Data/e2e/055-gait-filmstrip.json`, `Data/e2e/055-anim-perf.json`, `Data/perf/055-running-form.json` — 검증: `smoke --scenario 055-gait-filmstrip --profile mac-m1-1600p` 캡처를 직접 열어 팔꿈치 굽힘·머리 정렬·무릎 들기 확인 (AC-11·12)
  - 결과: `PlayerScreen` 화면 `gaitFilm{Walk·Jog·Run·Sprint·Start·Stop·Dribble}{Side·Front}`(클립을 8위상에 `SampleAnimation`으로 고정, 위 줄에 연구 기준 스틱 피겨 — `GaitReference.cs`, URP Unlit 선 머티리얼 에셋 `GaitStick.mat`)·`gaitSprint1~4`(1.5x R1 vs R2, 앞쪽 비스듬한 시점 — 측면이면 겹침)·성능 장면 원형 포함(`PerfHighlight.Styles`). 씬 재생성(클립 117). `055-gait-filmstrip`(36단계)·`055-anim-perf`·`Data/perf/055-running-form.json`. smoke mac-m1 1개 통과(기준 후보 18장), 캡처 직접 확인: 팔꿈치 굽힘·머리 한 줄·무릎 들기 보임.
- [x] T19 리서치 부록: 원형 리서치 흔적(출처 URL, 실명은 여기에만)을 `docs/index.html` 리서치 섹션 부록에 — 파일: `docs/index.html` — 검증: `spec-lint.py`(specs·Data에 실명 없음), 부록 렌더 확인
  - 결과: `docs/index.html` 딥다이브 리서치에 '08 달리기·킥 폼(055)' 부록 추가(출처 표·킥 출처·원형 도출 방법). 이전 세션의 실명 리서치 흔적은 이 레포에 없어 옮기지 않았다(실명 없이 출처만). spec-lint 055 통과.
- [x] T20 054 연결 메모: 054 spec의 FR-12 [미정] 속도별 걸음 주기 표를 055 FR-01로 채우자는 제안을 054 spec `미정` 항목에 기록(054는 초안이라 수정 제안만) — 파일: `specs/054-locomotion-ball-sync/spec.md` — 검증: diff 확인
  - 결과: 054 spec '미정'에 FR-12 걸음 주기 표를 055 FR-01로 채우자는 제안과 055 실측값·태그 연결을 기록(054는 초안이라 결정은 054 승인 때).

## G. 마무리
- [x] T21 커밋(사용자 요청 시) 전 정리: plan '구현 중 바뀐 점', spec 수정 필요분 반영 확인 — 파일: `plan.md`, `spec.md` — 검증: `spec-lint.py`
  - 결과: 스펙 해석 6건 사용자 승인(2026-11-05 '다 고치고 ㄱㄱ') → spec.md 반영(FR-13·FR-60·FR-60a·FR-50 판정·FR-44, 미정/질문 '정리됨'). 알려진 미흡도 손봄: 걷기 뒤꿈치 들기 접지 35 → 45 %(접지 최대 무릎 47 → 42°, 블렌드 미끄러짐 043 수준 유지), 킥 백스윙 최대 → 접촉 4 → 3프레임(접촉 발 속도 8~10 → 10~20 m/s). spec-lint 통과, Unity Players 134/134.
- [ ] T22 최종 검증 각 1회: `farm.py art --spec 055`(≈ 7~10분) · EditMode 전체 · `full --spec 055`(055-gait-filmstrip + 043 회귀) · `bench --spec 055`(IL2CPP) → 기준 이미지 채택 승인 · AC-12 육안 승인(`evidence/approval.md`) — 검증: 실패 0·성능 예산 통과 (AC-01~14)
- [ ] T23 `/verify 055` — 리포트 `evidence/report.html` 아티팩트 발행, spec·plan·tasks 상태 `완료`

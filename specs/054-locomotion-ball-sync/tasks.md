# 054 — 3D 선수 로코모션 · 공 접촉 동기화 · 태스크

상태: 구현 완료 · AC-12 보류 (2026-11-06, 리포트 https://claude.ai/artifact/G1Eh8J51Azxt2tbuC9UWw2)
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면).
중간 태스크의 `검증:`은 좁게(바뀐 테스트 클래스, `smoke --scenario X --profile mac-m1-1600p`, 생성기 1회). `smoke`·`full`·`farm.py art` 전체·`bench`는 마지막 검증 태스크에서만 (`.claude/rules/05-sdd.md` "검증 횟수").

## 기반: 측정과 장면
- [x] T01 기준값 데이터와 로더 (FR-72) — 파일: `Players/Data/054-motion-standards.json`, `Logic/Motion/MotionStandards.cs`, `MotionStandardsTests.cs` — 검증: `MotionStandardsTests`
  - 결과: MotionStandardsTests 5 통과
- [x] T02 합성 장면 8종·경계 장면: 005·004로 생성, 100 Hz 원본 보존, 공 소유자 기록 — 파일: `Verification/Runtime/Samples/Motion/MotionScenes.cs`, `HighlightData.cs`, `MotionScenesTests.cs` — 검증: `MotionScenesTests`(005 한계 안, 결정론)
  - 결과: MotionScenesTests 34 통과(16장면 결정론·005 속도 한계). 몸 방향 덮어쓰기는 합성 입력(005는 이동 방향만 기록), 공은 BallStepper. HighlightData.BallOwner 추가
- [x] T03 부위 캡슐·측정기·진단 인터페이스, 지표 계산식 테스트 (FR-70·71) — 파일: `Motion/BodyCapsules.cs`, `MotionMeter.cs`, `IMotionDiagnostics.cs`, `MotionMeterTests.cs` — 검증: `MotionMeterTests`. 현재 코드의 기준선 지표를 결과 줄에 남긴다
  - 결과: MotionMeterTests 8 통과. 파일 분리: BodyCapsules·SlideTracker·MotionStats는 순수 로직이라 Logic/Motion/BodyCapsules.cs. 기준선(1.5x): 미끄러짐 p95 2.9~29 cm·최대 6.9~29 cm(기준 2·5), 디딤발 롱패스 옆 40 cm·발리 뒤 96 cm(기준 옆 5~28·뒤 5~10), GK 실점 장면 접촉 거리 205 cm·루트 30 cm, 기울기·yaw·접촉 시각은 통과

## 클립
- [x] T04 컷 클립 4개와 태그(`footPath`, 액션 `stance`·`plant`), 생성기 검사 — 파일: `clips/football.py`, `bake.py`, `checks.py`, `ClipLibrary.cs`, `ClipLibraryTests.cs` — 검증: 생성기 1회, `ClipLibraryTests`, `PlayerImportTests`
  - 결과: 컷 4개(ClipLibraryTests 통과, 121클립). 태그는 플랜의 `footPath`·`stance`·`plant` 대신 모든 클립 공통 `stancePaths`(발별 [프레임0, 프레임1, 발끝 x0, z0, x1, z1], 비루프는 6프레임 조각)로 통합 — 런타임 조회가 한 경로라 단순. 컷 디딤은 ±20 cm(FR-23)에 맞춰 발 대신 골반을 안쪽으로 옮김

## 궤적·방향·자세
- [x] T05 파생 트랙과 시뮬 한계 초과 분류 (FR-80) — 파일: `Motion/MotionTrack.cs`, `MotionTrackTests.cs` — 검증: `MotionTrackTests`
  - 결과: MotionFidelityTests.MotionTrack_* 통과. 시뮬 오류 추종은 InteractionPlanner.cs의 FaultFollower
- [x] T06 yaw 트랙, 렌더 방향 오차·프레임당 변화 (FR-05·25, AC-03) — 파일: `Motion/YawTrack.cs`, `YawTrackTests.cs`, `AnimPlan.cs` — 검증: `YawTrackTests`
  - 결과: YawFrameDelta·방향 오차 전 장면 통과(YawTrack 1,200°/s). 측정은 재생 시계 60 fps 프레임 환산
- [x] T07 루트 오차·보정 변화 상한 (FR-01~04, AC-01) — 파일: `AnimPlan.cs`(RootCorrection), `MotionFidelityTests.cs` — 검증: `MotionFidelityTests`, `RootCorrectionTests`
  - 결과: MotionFidelityTests.RootError 전 장면 통과. 접촉 루트 ≤ 10 cm, 남는 차이는 화면 공 보정. RootCorrection 최소 0.25 s. 043 ContactAccuracyTests는 접촉점을 화면 공과 비교하도록 수정(054 FR-31이 기준 소유)
- [x] T08 전환 탐지와 클립 삽입, 액션 우선 (FR-13·20·21·23·24) — 파일: `Motion/TransitionPlanner.cs`, `AnimPlan.cs`, `TransitionPlannerTests.cs` — 검증: `TransitionPlannerTests`
  - 결과: TransitionPlannerTests 통과. 파일 합침: TransitionPlanner.cs에 PostureTrack. 005가 2 m/s 아래에서 피벗하므로 컷은 들어오거나 나갈 때 2 m/s 이상이면 인정, 135° 이상은 속도와 무관하게 턴. 감속 탐지 창 2 s
- [x] T09 자세 오버레이 (FR-20~22, AC-09) — 파일: `Motion/PostureTrack.cs`, `PostureApplier.cs`, `PoseSample`, `PostureTrackTests.cs` — 검증: `PostureTrackTests`
  - 결과: Lean_Within5·PostureUsage 통과. 온몸을 지면 위 루트 기준으로 기울이고(FootLock.ApplyPosture) 숙임 상한 0.2 rad [제안]. 파일: PostureApplier 대신 FootLock에 통합

## 발
- [x] T10 발 디딤 계획: 접지 구간·디딤 위치·경계 재계획 (FR-10·23·33) — 파일: `Motion/FootPlantPlan.cs`, `FootPlantPlanTests.cs` — 검증: `FootPlantPlanTests`
  - 결과: FootPlantTests 계획 부분 통과. 접지 판정 = 포즈 지시 클립의 stancePaths 가중 ≥ 50%, 접촉 지나는 접지는 접촉 시각 위치, 공 건드리는 발은 접촉 전 해제, 도달 거리 0.65 m [제안]
- [x] T11 발 고정 IK 적용과 측정, 마커 `Players.Motion` (FR-10·11·13, AC-02) — 파일: `FootLock.cs`, `PlayerAnimationSystem.cs`, `FootPlantTests.cs` — 검증: `FootPlantTests`, `LocomotionSlideTests`
  - 결과: 미끄러짐 전 장면 p95 ≤ 1.3 cm·최대 ≤ 1.7 cm(기준선 최대 29 cm), 발 높이 통과. 발끝 기준 고정 + 다리 다 펴지면 발 세우기 + 놓을 때 먼저 드는 스텝
- [x] T12 걸음 빈도 표 판정, 드리블 런 선택과 터치 1:1 (FR-12·37) — 파일: `AnimPlan.cs`, `LocomotionBlend.cs`, `CadenceTests.cs`, `DribbleSyncTests.cs` — 검증: 두 테스트 클래스
  - 결과: Cadence_InTable·Dribble_* 통과(FootPlantTests에 합침, 별도 CadenceTests·DribbleSyncTests 파일 없음)

## 공 접촉
- [x] T13 공 접촉 키와 화면 공 오차, 드리블 터치 (FR-35·52, AC-06·11) — 파일: `Motion/ContactKeys.cs`, `AnimPlan.cs`, `ContactKeysTests.cs` — 검증: `ContactKeysTests`
  - 결과: ContactSyncTests.BallKeyed_*·BallAfterContact·Interp30Hz 통과. ContactKeys.cs는 InteractionPlanner.cs에 합침. 043 RootCorrectionTests·ContactAccuracyTests는 접촉 키 기준으로 수정
- [x] T14 접촉 시각·거리·부위·디딤발, 급한 원터치·연속 접촉·결과 일치 (FR-30~34·36·81·82, AC-04·05) — 파일: `ActionClipSelector.cs`, `AnimPlan.cs`, `ContactSyncTests.cs` — 검증: `ContactSyncTests`, `ContactAccuracyTests`, `ActionClipSelectorTests`
  - 결과: ContactSyncTests 통과. 앞 접촉이 끝나기 전 다음 액션 시작 금지(LastContactBefore), 연속 접촉 페이드 수정, 접촉 시 앞 액션 가중 1, 디딤발은 055 측정식과 동일 — 측정 중 발견한 043 버그(앞 액션이 접촉 전에 빠짐) 수정

## 선수 간 상호작용
- [x] T15 겹침 보정·회피·골대·교체 (FR-40·45·85·86, AC-07) — 파일: `Motion/InteractionPlanner.cs`, `AnimPlan.cs`, `InteractionPlannerTests.cs` — 검증: `InteractionPlannerTests.NoPenetration`
  - 결과: InteractionPlannerTests.NoPenetration 16+1장면, 단위 5 통과. 회피 어깨 돌림은 PoseSample.Twist
- [x] T16 태클 순서·넘어짐·헤딩 경합·GK·동시 접촉 (FR-41~44·83, AC-08) — 파일: `Motion/InteractionPlanner.cs`, `InteractionPlannerTests.cs` — 검증: `InteractionPlannerTests.OutcomeOrder_MatchesSim`
  - 결과: OutcomeOrder 태클 승·파울·헤딩·GK, SameFrame 통과(합성 판정, 실제 판정은 008·011·012 연결 때 재확인). 헤딩 패자 정점 지연 0.07 s [제안]

## 배속·결정론·경계
- [x] T17 배속별 지표와 배속 전환 포즈 (FR-50·51·53, AC-10) — 파일: `MotionSpeedTests.cs`, `PlayerAnimator.cs`(시계) — 검증: `MotionSpeedTests`
  - 결과: MotionSpeedTests 배속 5종 × 3장면, FixedPlaybackClock.SwitchRate, RateSwitch_PoseJump 통과
- [x] T18 결정론: 클립 시퀀스·지표·시뮬 해시 전후 (AC-13) — 파일: `MotionDeterminismTests.cs` — 검증: `MotionDeterminismTests`, `AnimDeterminismTests`
  - 결과: TwoRuns·Plan_TwoBuilds·Playback_KeepsSimHash·Evaluate_Allocates_Nothing 통과(MotionSpeedTests에 합침)
- [x] T19 경계 장면 전부: 고속 시작·45 fps·극단 체형·속도 포함 (FR-80~88, AC-18) — 파일: `MotionEdgeCaseTests.cs` — 검증: `MotionEdgeCaseTests`
  - 결과: FlyingStart·LowFps_45·FastSlow·ExtremeBody·Substitution·InNet·MotionTrack_Teleport 통과(MotionSpeedTests·FidelityTests·InteractionPlannerTests에 나눔). AnimPlan.Build에 entityScale 추가

## 하네스·E2E
- [x] T20 측정 단계와 화면, 위반 프레임 자동 캡처 (FR-71) — 파일: `Samples/Motion/MotionScreen.cs`, `Steps/MotionSteps.cs`, `Data/e2e/054-motion-metrics.json` — 검증: `smoke --scenario 054-motion-metrics --profile mac-m1-1600p`
  - 결과: `smoke --scenario 054-motion-metrics --profile mac-m1-1600p` 통과, 위반 0(17장면 1.5x + longPass·tackle 4배속). PlayerScreen을 partial로 나눠 Samples/Motion/MotionScreen.cs. 러너에 `motionMeasure` 단계, 위반 프레임은 motion/ 폴더에 자동 캡처. 클립 순서가 바뀌어 Player043.unity 재생성(클립 121). 실제 키 배율 리그에서 드러난 문제 수정: 선택기·접촉 규칙에 키 배율, 공 높이 차는 화면 공 보정(≤ 15 cm)
- [x] T21 지표 판정·시계열 SVG·리포트 부착 (AC-16) — 파일: `Tools/devicefarm/farm/motion.py`, `report.py`, `tests/test_motion.py` — 검증: `pytest Tools/devicefarm/tests/test_motion.py`
  - 결과: test_motion.py 8 통과(`PYTHONPATH=Tools/devicefarm python3 -m unittest discover -s Tools/devicefarm/tests -p test_motion.py`). farm/motion.py, 리포트 카드
- [x] T22 장면 캡처와 배속 비교 시나리오 (AC-14·15·19) — 파일: `Data/e2e/054-motion-set.json`, `054-speed-compare.json`, `MotionScreen.cs` — 검증: `smoke --scenario 054-motion-set --profile mac-m1-1600p`, 접촉 프레임 캡처를 직접 열어 확인
  - 결과: `054-motion-set`·`054-speed-compare` mac-m1 1개 통과(기준 후보 23·4장, 채택 전). 접촉·파울·넘어짐 프레임 직접 확인: 발·머리와 공 사이 틈 없음. sameImage 3건 통과. 카메라 대상을 키별로 지정(파울·넘어짐은 엔티티 2)
- [x] T23 성능 벤치와 예산, 진단 꺼짐 A/B (AC-17) — 파일: `Bench/AnimBench.cs`, `SimBench.cs`, `Data/e2e/054-motion-perf.json`, `Data/perf/054-locomotion-ball-sync.json` — 검증: EditMode 측정 1회(값은 기록만), IL2CPP 판정은 T25
  - 결과: Mono 25명 p95 0.054 ms·최대 0.087 ms(tackle 장면, 움직이는 선수 4명이라 부하 낮음 — 기록만). 벤치는 전원 달리는 perfPlayers로 motion_p95·motion_max·diag_off 측정, 판정은 T25 IL2CPP

## 마무리
- [x] T24 EditMode 전체와 파이썬 테스트, `determinism-reviewer`·`spec-checker` 리뷰, 043 FR-41~52 참조 정리 — 검증: `farm.py logic --spec 054`(약 13분, 마지막 1회)
  - 결과: `farm.py logic --spec 054` 484/484 통과(스펙 설정 필터 Presentation.Players — MatchEngine·Core 변경 없음), 파이썬 188 통과. 남은 AC-14~17·19는 T25 증거. 043 FR-41~52는 이미 054 참조(FR-50 갱신).
  - 리뷰: determinism-reviewer·spec-checker 완료(2026-11-06). 반영: `MissesBall`(실패 트래핑·빗맞은 슈팅은 공에 닿음), 접촉 디딤 선계획(달려와 찰 때 디딤발 1.3 m 튐 수정, 구간 경계 프레임 번호), 같은 클립 합침, 공 범위 자르기, 시뮬 오류 가속 판정·예상 밖 오류 실패 처리, 접촉 키에 태클·GK, 회피·몸싸움·기둥 스침 장면, 움직일 때 간격 0.50 m, 필름 시나리오, 리포트 기준값·접촉 그래프·시뮬 실패, FR-41 10 cm·FR-53 전 배속 쌍 테스트, Determinism 카테고리, 재생 측정에 디딤발·기울기·보간 판정. 스펙 "측정 제외" 절 추가.
- [x] T25 커밋 요청 후 `farm.py art`·`full --spec 054`·`bench --spec 054` 각 1회 — 검증: 실패 0, 영향받은 043·055·056·057 회귀 포함. 기준 이미지 채택과 AC-19 육안 승인은 사용자에게 받는다
  - 결과: art 45/45, bench 6/6(054 처리 p95 0.06 ms·최대 1.03 ms, 애니 전체 0.32 ms, GC 0, 진단 켬 0.07 ms), full 40/40 조합·캡처 454장. 기준 이미지 새 103장·달라진 79장 채택, AC-19 승인(evidence/approval.md). 프레임 예산은 빈 화면 대비 증가로 변경(사용자 결정, 증가 p95 0.11 ms). 오디오 장치 변경 경고로 full 2회 실패 → 해당 문구만 log-ignore(사용자 승인)
- [x] T26 `/verify 054` — 리포트 아티팩트 발행, 상태 `구현 완료 · AC-12 보류`, README·tasks 갱신
  - 결과: 깨끗한 트리 28b48c9 full 판정 보류(AC-12)만. 리포트 https://claude.ai/artifact/G1Eh8J51Azxt2tbuC9UWw2 — assets 995개라 이미지 재압축 단일 HTML로 발행(057과 같은 방식)

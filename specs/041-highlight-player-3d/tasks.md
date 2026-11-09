# 041 — 하이라이트 3D 플레이어 · 재생 속도 · 태스크

상태: 구현 완료 · AC-15 보류 (2026-11-09). 대체 입력 범위 — 연결 때 재확인 (spec "재확인")
플랜: ./plan.md

규칙: 위에서부터 순서대로. Unity 배치 1회가 길어서 테스트를 먼저 쓰는 태스크는 같은 블록의 구현과 한 배치로 돌린다. 중간 검증은 `.claude/scripts/unity-test.sh EditMode <클래스 필터>`와 `farm.py smoke --scenario <id> --profile mac-m1-1600p`로 좁힌다. `smoke` 전체·`full`·`bench`·EditMode 전체·`/verify`는 마지막 태스크에서 각 1회.

## A. 재생 규칙 (순수 로직)
- [x] T01 장면 타입·포트·설정 — 파일: `Highlight/Logic/{HighlightScene,HighlightPorts,PlaybackSettings}.cs` — 검증: 컴파일 — 결과: 컴파일 통과
- [x] T02 구간 정규화 테스트·구현 — 파일: `Tests/EditMode/Presentation/Highlight/SceneListNormalizerTests.cs`, `Highlight/Logic/SceneListNormalizer.cs` — 검증: AC-08, FR-52 — 결과: 통과. 하프 경계 걸침은 키 모먼트 쪽, 없으면 긴 쪽을 남김
- [x] T03 재생 시계 테스트·구현 — 파일: `.../PlaybackClockTests.cs`, `Highlight/Logic/PlaybackClock.cs` — 검증: AC-03·04·05 — 결과: 통과
- [x] T04 큐 발화 테스트·구현 — 파일: `.../CueDispatcherTests.cs`, `Highlight/Logic/CueDispatcher.cs` — 검증: AC-06 — 결과: 통과
- [x] T05 시퀀서 테스트·구현 — 파일: `.../HighlightSequencerTests.cs`, `Highlight/Logic/HighlightSequencer.cs`, `Data/tuning/highlight.json` — 검증: AC-01·07·09·21, FR-21·54·56·57·5A — 결과: 통과(33개 묶음). 리플레이 앞에는 타임라인 없음, `Data/tuning/highlight.json` 1.0 s

## B. 클립 · 애니메이션 연결
- [x] T06 클립 변환·공 보호 테스트·구현 — 파일: `.../ClipConverterTests.cs`, `.../BallVisualGuardTests.cs`, `Highlight/Logic/{ClipConverter,BallVisualGuard}.cs` — 검증: FR-02·03·04, Absent 보간 — 결과: 통과
- [x] T07 장면 구축 테스트·구현 — 파일: `.../SceneClipBuilderTests.cs`, `Highlight/Logic/SceneClipBuilder.cs` — 검증: 진행률·취소·재구축·FR-53 진단 — 결과: 통과
- [x] T08 `SwapPlan` — 파일: `Players/PlayerAnimationSystem.cs`, 필요 시 `FootLock.cs`·`PlayerAnimator.cs`, `Tests/EditMode/Presentation/Players/` — 검증: 그래프 재생성 없음, 발 고정 초기화, 기존 `AnimPlanTests`·`RootCorrectionTests` 회귀 — 결과: 통과. `IBallVisualFilter` 훅 추가, 포즈는 Init 경로와 같음
- [x] T08a 정지 중 자세 누적 수정 (태스크 추가: E2E 불안정 캡처로 발견) — 파일: `Players/PlayerAnimationSystem.cs`, `.../HighlightFrozenPoseTests.cs` — 검증: 실제 골 장면 정지 포즈 동일·직립 — 결과: 같은 시각 재적용을 건너뜀
- [x] T09 결정론 테스트 — 파일: `.../HighlightDeterminismTests.cs` — 검증: AC-02, 고정 시각 위치 시퀀스 2회 동일 — 결과: 통과 (AC-02)

## C. 3D 런타임
- [x] T10 에셋 수명 주기 테스트·구현 — 파일: `.../HighlightAssetsLogicTests.cs`, `Highlight/{HighlightAssets,HighlightRigs}.cs` — 검증: AC-20, 웜업 호출 순서 — 결과: 통과 (AC-20). 리그 생성은 `HighlightStage` 추상 클래스로 분리해 `HighlightRigs.cs` 대신 Verification 구현이 맡음 (002·020 전까지 외형이 검증 전용)
- [x] T11 `HighlightPlayer` + 진단 — 파일: `Highlight/HighlightPlayer.cs` (FR-70 포함) — 검증: 컴파일, T13 화면 — 결과: 컴파일·E2E 동작 확인, FR-70 진입점 포함. 로드 취소 경합 수정(로드 끝난 뒤 언로드)
- [x] T12 대체물·검증 씬 — 파일: `Verification/Runtime/Samples/Highlight/{StandIns,HighlightScreen}.cs`, `Verification/Runtime/Steps/HighlightSteps.cs`, `Verification/Editor/HighlightSceneSetup.cs`, `Verification/Editor/FarmBuild.cs` — 검증: 씬 생성, 로딩 시간 측정(≤ 4 s, 초과하면 멈추고 보고) — 결과: 완료. `HighlightSteps.cs` 대신 goto 화면 + 기존 단언 사용, 무대 `Highlight3D`·호스트 `Highlight041` 분리. 대체 선정기는 대체 심판의 재시작 순간이동 직전에서 장면을 자름
- [x] T13 E2E 재생 시나리오 — 파일: `Data/e2e/041-highlight-playback.json` — 검증: `smoke --scenario 041-highlight-playback --profile mac-m1-1600p`, 스크린샷 직접 확인 — 결과: `041-highlight-playback` mac-m1-1600p 단계 전부 통과, 스크린샷 4장 육안 확인. 기준 이미지는 후보
- [x] T14 E2E 속도·키 모먼트·언로드 — 파일: `Data/e2e/{041-speed-switch,041-keymoment-pause,041-unload}.json` — 검증: 시나리오별 `--profile mac-m1-1600p` — 결과: speed-switch·keymoment-pause·unload mac-m1-1600p 통과 (차이 0.000%)

## D. 성능
- [x] T15 성능 시나리오·벤치 — 파일: `Data/e2e/{041-perf,041-enter-exit,041-battery}.json`, `Data/perf/041-highlight-player.json`, `Verification/Runtime/Bench/` — 검증: `041-perf` mac-m1-1600p 1회, 전환 카운터 0 — 결과: 041-enter-exit +5.3 MB(예산 20), 전환 셰이더 컴파일 0, IL2CPP 벤치 재생 p95 0.30 ms·GC 0 B. 프레임 시간은 60 fps 제한기 흔들림으로 p95 17.45 ms·전환 최대 28.3 ms(mac-m1, Mono 개발 빌드) — 최종 판정 사용자 확인 필요

## E. 마감
- [x] T15a 하니스 수정 (태스크 추가) — 파일: `Tools/devicefarm/farm/commands.py` — 검증: `041-battery` 목표 fps 30 — 결과: 시나리오 `batterySaver`가 플레이어 인자로 전달되지 않던 누락 수정
- [x] T15b 하니스·시나리오 수정 (태스크 추가, full에서 발견) — 파일: `Tools/devicefarm/farm/{logs,report}.py`, `Data/e2e/041-*.json` — 검증: 하니스 테스트 — 결과: 무시 로그를 항목별 첫 메시지 + 횟수로 모아 리포트 30 MB 문제 해결, 첫 goto의 경기 시뮬(약 9초)이 10초 제한에 걸리지 않게 시나리오 제한 30초
- [x] T16 전체 테스트 — EditMode 전체 실패 0, 콘솔 에러·경고 0, 컴파일 경고 신규 0 (이 스펙의 1회) — 결과: EditMode 전체 1144개 중 통과 1141·실패 0·건너뜀 3(000 셀프테스트 전용), 컴파일 경고 0. 콘솔 경고는 E2E에서 속도 점프 1515건이 남아 사용자 결정 대기
- [x] T17 054 재생 시계 재확인 — 041 시계로 `054-speed-compare` 재실행, 결과를 054 재확인 항목에 기록 — 검증: `smoke --scenario 054-speed-compare --profile mac-m1-1600p` — 결과: `054-speed-compare` mac-m1-1600p 통과 (기준 이미지 차이 0.000%)
- [x] T18 `/verify 041` — 커밋 후 `farm.py full --spec 041` + `bench --spec 041` 각 1회, 기준 이미지·성능 기준 채택은 사용자 승인, 리포트 아티팩트 발행, 상태·README 갱신 — 결과: full 전 프로필 통과, 기준 이미지 64장 승인 채택, IL2CPP 재생 p95 0.26 ms·GC 0 B, AC-15는 사용자 결정으로 보류(mac-m1 70 ms·low-igpu 31 ms, Mono 개발 빌드). 하니스 순서는 bench → logic → full

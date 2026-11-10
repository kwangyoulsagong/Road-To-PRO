# 042 — 중계 카메라 · 리플레이 · 태스크

상태: 구현 완료 · AC-17 보류 (2026-11-10, 리포트 https://claude.ai/artifact/EiHkN5vSThSmKACbYWQkUq). 대체 입력 범위 — 018·039·045·012 연결 때 재확인 (spec "재확인")
플랜: ./plan.md

규칙: 위에서부터 순서대로. Unity 배치 1회가 길어서 테스트를 먼저 쓰는 태스크는 같은 블록의 구현과 한 배치로 돌린다. 중간 검증은 `.claude/scripts/unity-test.sh EditMode <클래스 필터>`와 `farm.py smoke --scenario <id> --profile mac-m1-1600p`로 좁힌다. `smoke` 전체·`full`·`bench`·EditMode 전체·`/verify`는 마지막 태스크에서 각 1회.

경로 접두: `Presentation/Runtime/Highlight/Broadcast/Logic/` = `L/`, 테스트 `Tests/EditMode/Presentation/Highlight/Broadcast/` = `T/`.

## A. 기반
- [x] T01 타입·설정·조정 값 — 파일: `L/{CameraTypes,SceneBrief,CameraSettings,CameraTuning}.cs`, `Data/tuning/camera.json` — 검증: 컴파일, `TuningLoader` 로드 — 결과: 컴파일 통과, 파일 기본값 = 코드 기본값 테스트. 네임스페이스는 `Highlight.Broadcast` (`Highlight.Camera`면 `UnityEngine.Camera`를 가려서 플랜 경로 수정)
- [x] T02 경기장 입력 테스트·구현 — 파일: `T/CameraRigDataTests.cs`(044 최소·기본·최대 layout 픽스처), `L/CameraRigData.cs` — 검증: AC-16 (거치 범위 우선, 없으면 기본값 + 진단 1건) — 결과: 통과. 최소·최대 layout은 `Data/tests/`에 복사
- [x] T03 가림 기하 테스트·구현 — 파일: `T/CameraOcclusionTests.cs`, `L/CameraOcclusion.cs` — 검증: 선분 대 원기둥·상자, 경계값 — 결과: 통과. 광고판은 layout에 없지만 런오프 바깥이고 앵글 후보는 전부 런오프 안쪽이라 검사 대상 아님

## B. 메인 카메라 · 컷
- [x] T04 프레이밍 대상 테스트·구현 — 파일: `T/FramingTargetsTests.cs`, `L/FramingTargets.cs` — 검증: FR-03·52, 결손 프레임 — 결과: 통과
- [x] T05 프레이밍 해석기 테스트·구현 — 파일: `T/FramingSolverTests.cs`, `L/FramingSolver.cs` — 검증: AC-01·07, FR-04·55, 시선 벡터 감쇠 — 결과: 통과. T16에서 경기장 밖 공 조준 제한 추가
- [x] T06 라이브 컷 규칙 테스트·구현 — 파일: `T/LiveCutRulesTests.cs`, `L/LiveCutRules.cs` — 검증: AC-02·03, FR-56 — 결과: 통과

## C. 골 시퀀스 · 리플레이 · 모드 · 흔들림
- [x] T07 리플레이 앵글 배치 테스트·구현 — 파일: `T/ReplayAnglePlacerTests.cs`, `L/ReplayAnglePlacer.cs` — 검증: AC-03·05·06, FR-25·54, 금지 영역 (AC-16) — 결과: 통과
- [x] T08 골 시퀀스 테스트·구현 — 파일: `T/GoalSequenceDirectorTests.cs`, `L/GoalSequenceDirector.cs` — 검증: AC-04, 골 뒤 프레임이 짧은 장면 — 결과: 통과
- [x] T09 사용자 모드 테스트·구현 — 파일: `T/CameraModeLogicTests.cs`, `L/CameraModeLogic.cs` — 검증: AC-08, FR-31·33, 세로 화면 라이브 탑뷰 — 결과: 통과. `Highlight.Broadcast` 테스트 파일은 `DirectionTests`로 묶음(T06~T09)
- [x] T10 흔들림 테스트·구현 — 파일: `T/CameraShakeTests.cs`, `L/CameraShake.cs` — 검증: AC-15, FR-05·40·41 — 결과: 통과. 이벤트에서 골·강슛 원천 추출(`FromEvents`) 포함

## D. 트랙 · 041 연결
- [x] T11 트랙 생성·샘플 테스트·구현 — 파일: `T/CameraTrackTests.cs`, `L/{CameraTrackBuilder,CameraTrack}.cs` — 검증: AC-09·10, 샷 경계 비보간, 0.3 s 블렌드, 할당 0 — 결과: 통과
- [x] T12 041 장면 그룹 테스트·구현 — 파일: `Tests/EditMode/Presentation/Highlight/HighlightSequencerTests.cs`, `Highlight/Logic/{HighlightScene,HighlightSequencer,SceneListNormalizer}.cs` — 검증: 그룹 사이 타임라인 없음, 건너뛰기는 그룹 끝(FR-53), 그룹 없는 041 동작 불변, `GoalTail` 안 잘림 — 결과: 통과. 테스트는 `HighlightGroupTests.cs`로 분리, 그룹 없는 041 테스트 121개 묶음 불변
- [x] T13 포트·클립 빌더·플레이어 연결 — 파일: `Highlight/Logic/{HighlightPorts,SceneClipBuilder}.cs`, `Highlight/HighlightPlayer.cs`, `Highlight/Broadcast/BroadcastCamera.cs` — 검증: 041 시퀀서·클립 테스트 필터, 키 모먼트 뒤 트랙 재생성, 마커 `Highlight.CameraUpdate` — 결과: 통과. `BroadcastCamera`는 MonoBehaviour 없이 포트 구현으로 충분해 일반 클래스

## E. 검증 장면 · 시나리오
- [x] T14 합성 장면·대체 입력·화면 id — 파일: `Verification/Runtime/Samples/Highlight/{BroadcastScenes,HighlightScreen,HighlightStandIns}.cs` — 검증: 041 화면 `StandInCamera` 유지, `broadcast.*` 진입 컴파일 — 결과: 통과. 합성 경기 `BroadcastScenes`, 대체 HUD에 리버스 라벨
- [x] T15 메인 카메라 튜닝 (기본 경기장 화면 확인) — 파일: `Data/tuning/camera.json`, `Data/e2e/042-goal-sequence.json` — 검증: `smoke --scenario 042-goal-sequence --profile mac-m1-1600p` 스크린샷 직접 확인 — 결과: mac-m1-1600p 통과, 캡처 5장 직접 확인. `camera.json` 기본값 변경 없음
- [x] T16 E2E 시나리오 — 파일: `Data/e2e/{042-camera-modes,042-edge-ball}.json` — 검증: 각 `smoke --scenario <id> --profile mac-m1-1600p` (AC-12·13), 스크린샷 직접 확인 — 결과: mac-m1-1600p 통과, 캡처 직접 확인. stands 장면이 관중석만 찍혀 조준점을 피치 + 5 m로 제한
- [x] T17 성능 시나리오·벤치 — 파일: `Data/e2e/042-perf.json`, `Data/perf/042-*.json`, `Verification/Runtime/Bench/BroadcastCameraBench.cs` — 검증: `042-perf` mac-m1-1600p 1회, 전환 프레임은 기록만 (AC-14) — 결과: `042-perf` mac-m1-1600p 통과, 벤치는 `AnimBench`로 라우팅

## F. 마무리
- [x] T18 결정론·리뷰 — 시뮬 불변 확인(Presentation → MatchEngine 읽기만), 필요 시 `determinism-reviewer` — 검증: AC-09·10 필터 — 결과: 변경은 Presentation·Verification·Tests·Data뿐이라 코어 결정론 리뷰 대상 아님 (`git diff`로 Core·MatchEngine 변경 0 확인), AC-09·10 통과
- [x] T19 `/verify 042` — 커밋 후 EditMode 전체, `bench --spec 042` → `logic` → `full --spec 042` 각 1회, 기준 이미지 채택은 사용자 승인, 리포트 아티팩트 발행, 상태·`specs/README.md` 갱신 — 결과: bench(IL2CPP) 3/3 통과(카메라 p95 0.0002 ms·GC 0 B·가림 0.02 ms), logic 56/56, full 전 프로필 통과, 기준 이미지 296장 사용자 승인 채택, AC-17 보류. 리포트 https://claude.ai/artifact/EiHkN5vSThSmKACbYWQkUq. 최종 full은 깨끗한 트리(890a3f6)에서 다시 1회 실행해 통과

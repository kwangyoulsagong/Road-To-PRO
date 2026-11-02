# 001 — 코어 기반 · 태스크

상태: 완료
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면). C# 테스트는 `/unity-test` (`.claude/scripts/unity-test.sh EditMode <필터>`), Python은 `python3 -m unittest discover Tools/devicefarm/tests`. 코어 변경 태스크는 블록 끝에서 `determinism-reviewer` 리뷰.

## A. 모듈 기반
- [x] T01 도메인 asmdef 7개 + 마커 — 파일: `Assets/_Project/{Core,MatchEngine,Career,WorldGen,Data,Presentation,UI}/Runtime/RoadToPro.<모듈>.asmdef`, 각 `AssemblyMarker.cs`(Core 제외 — Core는 첫 실제 파일로), Tests EditMode·Performance asmdef 참조 추가 — 검증: 배치 컴파일 `error CS` 0, 플랜 의존 그래프대로 references·`noEngineReferences` — 결과: asmdef 7개(Core·MatchEngine·Career·WorldGen `noEngineReferences`), 마커 5개(Presentation은 T19의 UnityLogger로 대체해 마커 없음), Tests 2개 asmdef에 7개 모듈 참조 추가. 배치 컴파일 `error CS`·`warning CS` 0
- [x] T02 모듈 그래프·금지 API 테스트 — 파일: `Tests/EditMode/Architecture/ModuleGraphTests.cs` — 검증: `AsmdefGraph_MatchesAllowedDirections`, `AsmdefGraph_IsAcyclic`, `Checker_RejectsCorePresentationAndMatchEngineCareer`, `CoreModules_HaveNoUnityEngineReferences`, `CoreSources_HaveNoForbiddenApis`(static 가변 필드 포함) 통과 (AC-01·02, FR-04) — 결과: 6개 통과(+`ForbiddenScanner_DetectsViolations` 스캐너 자체 검사 추가). 금지 API 목록 = 훅 목록 + `MathF/Math` 초월함수 직접 호출(DMath 제외) + static 가변 필드

## B. 결정론 수학 · 좌표
- [x] T03 수학 테스트 — 파일: `Tests/EditMode/Core/{DMathTests,Vec2Tests,Vec3Tests,AngleTests,InterpTests,GeometryTests,FloatGuardTests}.cs` — 검증: 컴파일 후 실패(구현 전) 확인 — 결과: 테스트 8파일 선작성. 구현과 같은 배치 실행에서 확인(Unity 배치 1회 ≈ 25초라 블록 단위로 묶음)
- [x] T04 `DMath`·`FloatGuard` 구현 — 파일: `Core/Runtime/Math/DMath.cs`, `FloatGuard.cs` — 검증: `DMathTests`, `FloatGuardTests` 통과 (AC-19) — 결과: `DMathTests` 3·`FloatGuardTests` 4 통과. 디버그 검사는 `[Conditional("DEBUG")]`(에디터 DEBUG 정의 확인 — Assume 건너뜀 없음). FloatGuard가 ILogger를 쓰므로 `ILogger`·`LogLevel`·`MemoryLogger`를 T18보다 먼저 만듦
- [x] T05 벡터·각도·보간 구현 — 파일: `Core/Runtime/Math/Vec2.cs`, `Vec3.cs`, `Angle.cs`, `Interp.cs` — 검증: `Vec2Tests`, `Vec3Tests`, `AngleTests`, `InterpTests` 통과 (AC-03) — 결과: `Vec2Tests` 6·`Vec3Tests` 3·`AngleTests` 5·`InterpTests` 2 통과. 정규화 임계 제곱길이 1e−12, 큰 값 길이는 최대 성분 스케일링
- [x] T06 swept 판정 구현 — 파일: `Core/Runtime/Math/Geometry.cs` — 검증: `GeometryTests` 통과 (AC-04, 경계: 길이 0·접선·r=0·시작점 내부) — 결과: `GeometryTests` 7 통과
- [x] T07 피치 좌표 테스트·구현 — 파일: `Core/Runtime/Pitch/PitchSpec.cs`, `PitchCoords.cs`, `Tests/EditMode/Core/PitchCoordsTests.cs` — 검증: `Center_Is52_5_34`, `Perspective_RoundTrip_Within1Ulp`, 사용자 피치 크기 변환 (AC-05) — 결과: `PitchCoordsTests` 4 통과. AC-05를 1 ulp 허용으로 스펙 수정(2026-11-02 사용자 승인). 관점 변환 `ToPerspective`/`FromPerspective`(같은 반전), 기간 index 홀수면 진영 교체

## C. 시드 RNG
- [x] T08 RNG 테스트 — 파일: `Tests/EditMode/Core/{SeedRngTests,RngStreamsTests,DistributionTests}.cs` (`[Category("Determinism")]` 표시: AC-06~08) — 검증: 컴파일 후 실패 확인 — 결과: 테스트 3파일 선작성 (`Determinism` 카테고리: AC-06~08)
- [x] T09 코어 RNG 구현 — 파일: `Core/Runtime/Random/SplitMix64.cs`, `SeedRng.cs`(xoshiro256\*\*·균등 실수·편향 없는 정수), `RngState.cs` — 검증: `SeedRngTests` 통과 (AC-06·08) — 결과: `SeedRngTests` 5 통과 (상태 33바이트 v1, 잘못된 버전 FormatException)
- [x] T10 스트림 파생 — 파일: `Core/Runtime/Random/RngStreamId.cs`, `RngStreams.cs` — 검증: `RngStreamsTests` 통과 (AC-07) — 결과: `RngStreamsTests` 3 통과. 스트림 id 고정 번호(경기 1~7, 커리어·월드 101~103)
- [x] T11 분포 — 파일: `SeedRng.cs`(정규·로그정규·베르누이·가중·softmax·셔플) — 검증: `DistributionTests` 통과 (AC-09·10) — 결과: `DistributionTests` 16 통과 (χ²·정규 평균/표준편차·softmax 99%·오류 입력). softmax τ≤1e−6은 argmax(난수 미소비)

## D. 시계 · 날짜
- [x] T12 경기 시계 테스트 — 파일: `Tests/EditMode/Core/MatchTimeTests.cs` — 검증: 컴파일 후 실패 확인 — 결과: 테스트 선작성
- [x] T13 경기 시계 구현 — 파일: `Core/Runtime/Time/SimClock.cs`, `MatchPeriod.cs`, `MatchTimeline.cs`, `MatchTime.cs` — 검증: `MatchTimeTests` 통과 (AC-11·12) — 결과: `MatchTimeTests` 9 통과 (3가지 타임라인 전 틱 왕복 + 표기 파싱 왕복). 표기 파싱은 경계 모호성 때문에 기간을 함께 받음(`MatchTime.Parse(text, period)`)
- [x] T14 게임 날짜 테스트·구현 — 파일: `Core/Runtime/Calendar/GameDate.cs`, `Tests/EditMode/Core/GameDateTests.cs` — 검증: `Feb29_PlusYear_IsMar1`, `Age_Feb29Born`, `Dec31_PlusDay`, ISO 주차 해 경계, 9999년 상한 (AC-13) — 결과: `GameDateTests` 7 통과 (1900~2100 전 일자 .NET 달력 대조 포함)

## E. 해시 · 순회 순서
- [x] T15 해시·체크포인트 테스트 — 파일: `Tests/EditMode/Core/{StableHasherTests,CheckpointTests}.cs` (`Determinism` 카테고리) — 검증: 컴파일 후 실패 확인 — 결과: 테스트 선작성
- [x] T16 해시·체크포인트 구현 — 파일: `Core/Runtime/Hashing/StableHasher.cs`, `Checkpoint.cs`, `CheckpointLog.cs` — 검증: `FixedLog_MatchesGoldenHash`, `OneUlp_Differs`, `NegZero_EqualsPosZero`, `NaNPatterns_Equal`, `FirstDivergence_At7th_Returns60to70s` 통과 (AC-14·15) — 결과: `StableHasherTests` 6·`CheckpointTests` 3 통과. 골든 해시 `a81a70efe79a54bb`는 Python 독립 구현과 일치. 체크포인트 갈라짐 결과는 `Divergence`(index·구간 틱) 타입
- [x] T17 정렬 순회 테스트·구현 — 파일: `Core/Runtime/Ordering/DeterministicOrder.cs`, `Tests/EditMode/Core/DeterministicOrderTests.cs` — 검증: `HashSet_DifferentInsertOrder_SameResultHash`, `StableSort_TiesById` (AC-17) — 결과: `DeterministicOrderTests` 4 통과

## F. 로거 · 튜닝
- [x] T18 로거 — 파일: `Core/Runtime/Logging/{ILogger,LogLevel,LoggerExtensions,NullLogger,MemoryLogger}.cs`, `Tests/EditMode/Core/LoggerTests.cs` — 검증: 레벨 필터, 비활성 레벨에서 포맷 인자 미평가 (FR-70·71) — 결과: `LoggerTests` 3 통과 (비활성 레벨 인자 ToString 호출 0회)
- [x] T19 Unity 로거 어댑터 — 파일: `Presentation/Runtime/Logging/UnityLogger.cs`, `Tests/EditMode/Presentation/UnityLoggerTests.cs` — 검증: `Warning_Error_ReachUnityLog` (FR-72) — 결과: `UnityLoggerTests` 2 통과. Presentation 마커 대신 이 파일이 어셈블리를 만듦
- [x] T20 JSON 리더 — 파일: `Core/Runtime/Serialization/JsonReader.cs`, `Tests/EditMode/Core/JsonReaderTests.cs` — 검증: 기본 타입·중첩·이스케이프·InvariantCulture 숫자·오류 줄·열 — 결과: `JsonReaderTests` 4 통과 (중복 키 보존, de-DE culture에서도 숫자 해석)
- [x] T21 튜닝 테스트 — 파일: `Tests/EditMode/Core/TuningLoaderTests.cs` — 검증: 컴파일 후 실패 확인 — 결과: 테스트 선작성
- [x] T22 튜닝 구현 — 파일: `Core/Runtime/Tuning/{TuningDefinition,SourceTag,TuningRegistry,TuningBundle,TuningLoader,TuningLoadException}.cs` — 검증: `TuningLoaderTests` 통과 (AC-16) — 결과: `TuningLoaderTests` 7 통과. `SourceTag`는 enum 대신 대괄호 형식 검사 + 기획서 표기 문자열 그대로 보관(플랜 "표기 그대로"에 맞춤)
- [x] T23 코어 결정론 리뷰 — `determinism-reviewer`로 `Assets/_Project/Core/**` 리뷰, 지적 사항 반영 — 검증: 리뷰 지적 0 (또는 반영 후 재리뷰 통과) — 결과: 차단 1(`Range(float)`가 float 반올림으로 max 반환) + 권장 수정: Length NaN 은닉, 가중치 합 오버플로, FloatGuard·예외 메시지 InvariantCulture, 큰 각도 정규화 반복, RngState 정확 길이, `AddWithLength`, 체크포인트 간격 0, JSON 깊이 64 제한, 튜닝 객체 중복 키, 스트림 파생 비선형화(XOR→Mix 합). 회귀 테스트 `ReviewRegressionTests` 10개 추가, EditMode 151개 중 실패 0. 미반영 2건은 plan "위험"에 기록(타임라인 확정 시점, Mono↔IL2CPP float 차이)

## G. 성능 · 하네스 보강
- [x] T24 성능 테스트 — 파일: `Tests/Performance/Core/CorePerfTests.cs`, `Data/perf/001-core-foundation.json`(스펙 성능 예산 표 사본) — 검증: 배치 실행으로 `Rng_Uniform_ns`, `Rng_Normal_ns`, `Vec_Swept_ns`, `Hash_30kEvents_ms`, `Tuning_Load1000_ms`, `ZeroAlloc_MathRngClockLog` 결과 JSON 생성, 예산 안 (AC-18) — 결과: 성능 테스트 7개(`Vec_Swept_ns`를 `Vec_Normalize_ns`·`Swept_ns`로 분리 — 예산 표도 두 줄로). Unity Mono는 `GC.GetAllocatedBytesForCurrentThread`가 0이라 할당은 프로파일러 `Recorder("GC.Alloc")` 횟수로 재고 대조군으로 측정 동작 확인. 핫 경로에 `AggressiveInlining` 추가(균등 8.2→5.5 ns). 예산 4개가 Mono에서 구조적으로 초과해 **EditMode 실측 기준으로 재설정**(2026-11-02 사용자 승인: 균등 ≤8·정규 ≤80·정규화 ≤50·swept ≤100 ns), 스펙 성능 예산 표 수정
- [x] T25 farm logic 보강 테스트 — 파일: `Tools/devicefarm/tests/test_logic_spec.py` — 검증: `test_determinism_ac_linked`, `test_perf_results_written_and_judged`, `test_over_budget_fails`, `test_spec_filter_namespaces` 실패 확인 — 결과: `test_logic_spec.py` 10개 + 픽스처 2개(`nunit-categories.xml`, `perf-editmode.json`). 구현 전 실패 확인
- [x] T26 farm logic 보강 구현 — 파일: `Tools/devicefarm/farm/logic.py`, `commands.py`, `perf.py` — 검증: T25 통과 + 기존 Python 테스트 전부 통과 + `farm.py self-test all` 회귀 없음 — 결과: Python 111개 통과(`test_logic_spec` 10개 포함), `farm.py self-test all` 통과(셀프테스트가 다시 쓴 000 evidence는 되돌림). 추가: `farm logic --spec`이 `Data/perf/<NNN>-*.json`의 `tests`를 필터로 쓰고, `[결정론]` AC를 `Determinism` 카테고리 결과로, `[성능]` AC를 EditMode 예산·+10% 회귀로 연결, `evidence/perf.json` 기록, 리포트에 "EditMode 성능" 카드. `approve-perf --spec`이 EditMode 기준(`editmode/<NNN>/<프로필>`) 채택. `unity-test.sh`는 `UNITY_PERF_RESULTS` 환경 변수로 `-perfTestResults` 전달

## H. 마무리
- [x] T26a farm logic 종료 코드를 리포트 판정과 맞춤 — 파일: `farm/commands.py`, `tests/test_logic_spec.py` — 검증: `test_unjudged_perf_makes_spec_verdict_fail` — 결과: 추가 태스크(사유: 배터리 전원이라 성능 판정이 빠져 리포트는 fail인데 CLI가 "통과"를 출력). 이제 AC 증거 누락·실패 시 종료 코드 1, Python 112개 통과
- [x] T27 수용 기준 전체 테스트 통과 — EditMode 전체(`/unity-test`) 실패 0, 콘솔 에러·경고 0, 컴파일 경고 신규 0 — 결과: EditMode 151개 중 통과 148·실패 0·건너뜀 3(000 셀프테스트 전용 픽스처), `warning CS`·`error CS` 0. 콘솔 경고·에러는 `UnityLoggerTests`의 `LogAssert.Expect` 대상뿐
- [x] T24a 튜닝 로드 성능 측정 안정화 — 파일: `Tests/Performance/Core/CorePerfTests.cs` — 검증: `farm logic --spec 001` 3회 연속 회귀 0건 — 결과: 추가 태스크(사유: 기준 채택 직후 재측정에서 튜닝 로드가 7.4~9.5 ms로 흔들려 +31% 회귀 오탐). 측정마다 GC를 비우고 워밍업 5·표본 30으로 늘림(5.8~6.7 ms). 측정 방식이 바뀌어 기준을 다시 채택
- [x] T28 `/verify 001` — `farm.py logic --spec 001` 리포트(로직·결정론·성능), 첫 성능 기준은 사용자 승인 후 `approve-perf`, 리포트 아티팩트 발행, spec·tasks·README 상태 `완료` — 결과: 깨끗한 작업 트리(커밋 69aaeba)에서 `farm logic --spec 001` 판정 pass — AC 19개 전부 증거 연결(로직 13·결정론 5·성능 1), EditMode 126개 통과, 성능 7항목 예산 안·기준 대비 회귀 0(AC 전원, mac-m1-1600p). 성능 기준 `editmode/001/mac-m1-1600p` 채택(사용자 승인). 리포트 https://claude.ai/artifact/AFz3HpKiUkEPtdNFrW3BXS

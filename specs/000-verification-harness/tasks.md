# 000 — 검증 하네스 · 디바이스팜 · 태스크

상태: 완료
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트 태스크를 구현 태스크보다 먼저 둔다(가능하면). Python 테스트는 `python3 -m unittest discover Tools/devicefarm/tests`.

## A. 기반
- [x] T01 레포 기반 정리 — 파일: `.gitignore`(`/.farm/`, `/Assets/FarmSelfTest_Tmp/`), `CLAUDE.md` 레포 구조, `.claude/rules/10-csharp.md` 모듈 목록(`RoadToPro.Verification`(+Editor), `RoadToPro.Tests.Performance`) — 검증: `git check-ignore .farm/x Assets/FarmSelfTest_Tmp/x`, `bash .claude/hooks/tests/run.sh` 통과 — 결과: ignore 2경로 확인, 훅 테스트 통과 52·실패 0
- [x] T02 Performance Testing 패키지 추가 — 파일: `Packages/manifest.json`(`com.unity.test-framework.performance`) — 검증: 배치모드 임포트 후 컴파일 에러 0 (`/unity-test` 빈 실행) — 결과: 3.6.0 직접 의존(기존 간접 3.5.0), 배치 임포트·EditMode 빈 실행 통과, `error CS` 0
- [x] T02a `unity-test.sh` 결과 경로 수정 (Unity가 종료 시 `Temp/`를 지워 결과 XML·로그가 사라짐) — 파일: `.claude/scripts/unity-test.sh` — 검증: 빈 EditMode 실행이 결과를 요약 — 결과: 추가 태스크(2026-11-02 사용자 승인), `.farm/unity-test/`로 변경
- [x] T03 asmdef·폴더 생성 — 파일: `Assets/_Project/Verification/Runtime/RoadToPro.Verification.asmdef`, `Verification/Editor/RoadToPro.Verification.Editor.asmdef`, `Tests/EditMode/RoadToPro.Tests.EditMode.asmdef`(Verification 참조), `Tests/Performance/RoadToPro.Tests.Performance.asmdef` — 검증: 컴파일 에러 0, 도메인 asmdef가 Verification을 참조하지 않음 — 결과: asmdef 4개·`.meta`는 Unity가 생성, `error CS` 0. 스크립트가 없어 "will not be compiled" 안내 4줄(스크립트 추가 시 사라짐). Verification을 참조하는 건 Verification.Editor·Tests 2개뿐
- [x] T04 Python 패키지 골격 + CLI — 파일: `Tools/devicefarm/farm.py`, `farm/__init__.py`, `farm/errors.py`(`RunnerError` vs 검증 실패) — 검증: `test_cli.py::test_subcommands_parse`, `test_cli.py::test_runner_error_exit_code_distinct` — 결과: `test_cli.py` 6개 통과. 종료 코드 0 통과·1 검증 실패·2 사용법·3 실행기 오류. 명령 해석은 `farm/cli.py`로 분리(`farm.py`는 얇은 진입점 — 같은 폴더의 `farm` 패키지와 이름 충돌 방지), `.gitignore`에 `__pycache__/`

## B. 데이터 형식
- [x] T05 프로필 테스트 — 파일: `Tools/devicefarm/tests/test_profiles.py` — 검증: 10개 로드, smoke=3×1 조합, full=10×4 조합, 필드 누락·중복 id 로드 실패 (FR-10~13) — 결과: `test_profiles.py` 11개 통과(전개 선택자·언어 강제 포함)
- [x] T06 프로필 구현 + 데이터 — 파일: `farm/profiles.py`, `Data/e2e/profiles.json`(스펙 FR-11 표 + `qualityMap` Low/Medium→Mobile, High→PC) — 검증: T05 통과 — 결과: `farm/profiles.py`, `farm/paths.py`(경로 한 곳) 추가, 프로필 10개
- [x] T07 시나리오 스키마 (Python 검증) 테스트·구현 — 파일: `farm/scenario.py`, `tests/test_scenario.py` — 검증: `test_capture_inside_perf_rejected`, `test_inject_outside_selftest_rejected`, `test_profiles_all_smoke_list`, `test_3d_capture_default_ratio` — 결과: `test_scenario.py` 10개 통과
- [x] T08 로그 무시 목록 형식 — 파일: `farm/logs.py`(로드부), `Data/e2e/log-ignore.json`(빈 목록), `tests/test_logs.py::test_ignore_without_reason_rejected` — 검증: 해당 테스트 통과 — 결과: 빈 무시 목록, 사유 없음·정규식 오류 로드 실패 테스트 통과

## C. Python 판정 로직 (Unity 없이)
- [x] T09 이미지 비교 테스트 — 파일: `tests/test_compare.py` — 검증: `test_identical_zero_diff`, `test_threshold_ratio`, `test_masks_excluded`, `test_resolution_mismatch_fails`, `test_diff_image_highlights_region`, `test_blank_and_solid_fail_without_baseline`, `test_card_screen_not_solid`, `test_missing_baseline_is_candidate` — 결과: `test_compare.py` 13개(기준 경로·승인 포함)
- [x] T10 이미지 비교 구현 — 파일: `farm/compare.py`, `farm/baseline.py`(경로 규칙·`approve-baseline`) — 검증: T09 통과 (AC-03·05·07·08 로직) — 결과: `farm/compare.py`, `farm/baseline.py`. 단색 판정 = 휘도 표준편차 ≤ 2.0
- [x] T11 성능 판정 테스트 — 파일: `tests/test_perf.py` — 검증: `test_percentiles`, `test_warmup_discarded`, `test_over_budget_fails`, `test_regression_15pct_fails_5pct_passes`, `test_non_reference_profile_not_judged`, `test_battery_power_excluded`, `test_perf_testing_json_read` — 결과: `test_perf.py` 12개
- [x] T12 성능 판정 구현 — 파일: `farm/perf.py`, `Data/e2e/perf-baselines.json`(빈 객체), `approve-perf` 명령 — 검증: T11 통과 (AC-09·10) — 결과: 백분위수 = 최근접 순위, 회귀 = +10% 이상 실패, 기준 0이면 증가 시 실패
- [x] T13 결정론 비교 테스트·구현 — 파일: `farm/determinism.py`, `tests/test_determinism.py` — 검증: `test_equal_lists_pass`, `test_first_divergence_reported`, `test_cross_profile_equal`, `test_length_mismatch_reports_first_missing` (AC-11·16 로직) — 결과: `test_determinism.py` 5개 통과
- [x] T14 로그 판정 테스트·구현 — 파일: `farm/logs.py`, `tests/test_logs.py` — 검증: `test_warning_fails`, `test_exception_and_assert_classified`, `test_ignored_listed_with_reason` (AC-12 로직) — 결과: `test_logs.py` 9개 통과 (T08과 같은 모듈)
- [x] T15 로직 결과 파싱 테스트·구현 — 파일: `farm/logic.py`, `tests/test_logic.py`, `tests/fixtures/nunit-1fail-2pass.xml`, `tests/fixtures/compile-error.log` — 검증: `test_failures_listed_first`, `test_compile_error_extracted`, `test_metadata_commit_branch_unity` (AC-01·02 로직) — 결과: `test_logic.py` 7개 통과. 0개 실행은 통과 아님. HTML 공통은 `farm/htmlkit.py`로 분리
- [x] T16 실행기 전처리 — 파일: `farm/preflight.py`, `tests/test_preflight.py` — 검증: `test_lock_blocks_second_run`, `test_stale_lock_removed`, `test_editor_open_is_runner_error`, `test_low_disk_is_runner_error`, `test_dirty_worktree_flag` — 결과: `test_preflight.py` 7개 통과. Python 누적 80개 통과

## D. Unity 드라이버 (C#)
- [x] T17 실행 인자·시나리오 파서 테스트 — 파일: `Assets/_Project/Tests/EditMode/Verification/FarmArgsTests.cs`, `ScenarioParserTests.cs` — 검증: `Parse_AllProfileArgs_Applied`, `CaptureInsidePerf_Rejected`, `InjectOutsideSelfTest_Rejected` — 결과: `FarmArgsTests` 5·`ScenarioParserTests` 5 통과 (MiniJson 포함)
- [x] T18 인자·컨텍스트·파서 구현 — 파일: `Verification/Runtime/FarmArgs.cs`, `FarmContext.cs`, `ScenarioDefinition.cs` — 검증: T17 통과 — 결과: `FarmArgs`·`FarmContext`·`ScenarioDefinition`·`MiniJson`(패키지 추가 없이 다형 JSON 해석). `assertText`는 언어별 기대값 {ko,en} 허용
- [x] T19 SampleSim·결정론 프로브 테스트 — 파일: `Tests/EditMode/Verification/SampleSimTests.cs` — 검증: `SameSeed_TwoRuns_SameCheckpoints`, `ExtraRngCall_DivergesFromThatTick`, `DifferentSeed_DifferentHash` — 결과: `SampleSimTests` 5개 통과 (FNV-1a 공개 벡터 포함)
- [x] T20 SampleSim 구현 — 파일: `Verification/Runtime/IDeterminismProbe.cs`, `SampleSim.cs`(xorshift·FNV-1a 64·체크포인트 JSON) — 검증: T19 통과 — 결과: t450 RNG 추가 → t500에서 갈라짐 확인
- [x] T21 레이아웃 단언 테스트·구현 — 파일: `Verification/Runtime/LayoutAsserts.cs`, `Tests/EditMode/Verification/LayoutAssertsTests.cs` — 검증: `NoClip_OverflowingLabel_Fails`, `NoClip_FittingLabel_Passes`, `NoOverlap_Siblings_Detected`, `Tolerance_1px` (AC-06 로직) — 결과: `LayoutAssertsTests` 6개 통과. 실제 플레이어 en 실행에서 `card3Caption` 464px > 387px 잘림 검출
- [x] T22 부트스트랩·프로필 적용 — 파일: `Verification/Runtime/FarmBootstrap.cs`, `FarmLog.cs` — 검증: 플레이어 1회 수동 실행으로 `logs.jsonl`·`exit.json` 생성, 품질·렌더 스케일·목표 fps 값이 로그에 찍힘 — 결과: `logs.jsonl`·`device.json`·`exit.json` 생성 확인. **변경**: macOS 빌드엔 품질 레벨 `PC`만 켜져 있어 `Mobile`을 고를 수 없음 → `Resources/FarmRenderAssets`(이름→URP 에셋 표)로 렌더 파이프라인 직접 교체, ProjectSettings 무변경. 비포커스 시 정지 막으려 `runInBackground=true`
- [x] T23 오프스크린 캡처 — 파일: `Verification/Runtime/OffscreenCapture.cs` — 검증: 캡처 PNG 해상도 = 프로필 해상도(레티나에서도), 연속 2프레임 동일 확인 동작. **화면 캡처와 1회 대조** — 다르면 멈추고 보고(플랜 위험 항목) — 결과: 2560×1600 캡처(레티나 무관), 픽셀이 USS 토큰과 정확히 일치(바탕 242,243,245·버튼 49,130,246) — 화면 대조 대신 토큰 대조로 렌더 동일성 확인, 연속 2프레임 동일 확인 동작
- [x] T24 성능 샘플러 — 파일: `Verification/Runtime/PerfSampler.cs` — 검증: `perf.json`에 프레임 시간·메인 스레드·GC·드로우콜·삼각형·텍스처·총 메모리 필드, 워밍업 제외 — 결과: `perf.json`에 150프레임(워밍업 30), 드로우콜·삼각형·텍스처·총 메모리·메인 스레드 기록 (개발 빌드)
- [x] T25 시나리오 실행기·단계 — 파일: `Verification/Runtime/ScenarioRunner.cs`, `Steps/*.cs`(goto·tap·type·waitFor·assert*·capture·perf·checkpoint·freeze·inject) — 검증: 샘플 시나리오 1프로필 실행 시 `steps.jsonl` 전 단계 통과, 타임아웃 단계는 스크린샷+실패 — 결과: 샘플 시나리오 12단계 전부 통과, 프로필당 약 10초
- [x] T26 샘플 화면 — 파일: `Verification/Samples/FarmSampleCards.unity`, `SampleCards.uxml`, `SampleCards.uss`(색은 USS 변수), `SampleCardsScreen.cs`(ko/en·light/dark·en 넘침 라벨·`-farmSampleButtonColor`·`-farmSampleBlack`) — 검증: 4개 조합 캡처를 Read로 눈으로 확인 — 결과: 캡처를 Read로 확인: 카드 3장·버튼·한글 정상 (폰트는 맥 시스템 폰트 동적 에셋). 씬·패널 에셋은 `FarmSampleSetup.Generate`로 생성
- [x] T27 배치 빌드 — 파일: `Verification/Editor/FarmBuild.cs`, `farm/build.py`(소스 해시 캐시 키) — 검증: `farm.py build` 2회째는 캐시 적중으로 건너뜀 — 결과: `farm/build.py`, `test_build.py` 2개. 실제 빌드 15s → 2회째 캐시 적중 0s
- [x] T28 SampleSim Performance 테스트 — 파일: `Tests/Performance/SampleSimPerfTests.cs` — 검증: `-perfTestResults` JSON이 `perf.py`로 읽힘 (FR-41 경로) — 결과: `-perfTestResults` JSON을 `perf.read_perf_testing`이 읽음 (Simulate_1000Ticks 중앙값 1.2 ms). SampleUnit 실측 매핑 수정(2=ms)

## E. 오케스트레이션 · 리포트
- [x] T29 실행기 테스트 — 파일: `tests/test_runner.py`(가짜 플레이어 스크립트 사용) — 검증: `test_crash_isolated`, `test_hang_30s_killed`, `test_step_timeout_recorded`, `test_nonzero_exit_fails` — 결과: `test_runner.py` 7개 통과 (가짜 플레이어로 크래시·행·타임아웃·종료 코드)
- [x] T30 실행기 구현 — 파일: `farm/runner.py` — 검증: T29 통과 (AC-13 로직) — 결과: `farm/runner.py`. 하트비트 30초 무응답 시 프로세스 그룹 종료, 프로필 순차 실행
- [x] T31 리포트 테스트 — 파일: `tests/test_report.py` — 검증: `test_header_has_commit_branch_time`, `test_dirty_worktree_warning`, `test_grid_rows_steps_cols_profiles`, `test_ac_link_table`, `test_size_cap_downscale`, `test_run_command_recorded` — 결과: `test_report.py` 8개 통과
- [x] T32 리포트 구현 — 파일: `farm/report.py`, `farm/templates/report.html`(400px 대응, 기준·현재·차이 3장 비교) — 검증: T31 통과 — 결과: `farm/report.py` + `farm/evaluate.py`(판정 묶음 — 플랜 표에 없던 파일, 리포트 입력 생성). 같은 시나리오 반복 실행 시 썸네일 경로 분리
- [x] T33 Chrome 연동 — 파일: `farm/chrome.py` — 검증: 로직 리포트 → `logic.png` 생성, 400px `--dump-dom` 검사 결과 읽기 (AC-14) — 결과: `farm/chrome.py`. 헤드리스 Chrome 최소 창 폭이 500px라 400px 검사는 iframe으로 감싸서 측정 (넘치는 페이지에서 overflow:708>400 검출 확인)
- [x] T34 CLI 모드 연결 — 파일: `farm.py`(`logic`/`smoke`/`full`/`--profile`/`--spec`), 진행 출력 — 검증: `farm.py logic` 실제 실행, `farm.py smoke` 실제 실행으로 샘플 시나리오 3 프로필 결과 — 결과: `farm/commands.py`. `farm.py smoke --scenario 000-sample-cards` 실제 실행: 3 프로필 통과, 기준 후보 6장 → 판정 candidate. 캡처 3장 Read로 확인
- [x] T35 E2E 시나리오 데이터 — 파일: `Data/e2e/000-sample-cards.json`, `000-report-mobile.json`, `000-selftest-logs.json`, `000-selftest-crash.json` — 검증: `farm.py smoke --scenario 000-sample-cards` 첫 실행 "기준 후보" 3장 — 결과: `000-sample-cards`·`000-selftest-logs`·`000-selftest-crash`·`000-selftest-black`. **변경**: `000-report-mobile`은 플레이어 시나리오가 아니라 Chrome 검사라 셀프테스트 `report` 케이스로 대체, 검은 화면용 `000-selftest-black` 추가
- [x] T36 셀프테스트 — 파일: `farm/selftest.py`(logic-fail·compile-error·stability·diff·black·determinism·logs·crash·all), `tests/test_selftest.py::test_tmp_cleanup_on_failure`, `Tests/EditMode/Verification/SelfTestFixtures.cs`(`[Explicit]` 실패 1·통과 2) — 검증: `farm.py self-test all` 각 항목이 기대대로 실패/통과 — 결과: `farm/selftest.py`, `test_selftest.py` 2개. `self-test all` 18건 전부 통과 (222s), 케이스 `clip`·`perf` 추가

## F. 마무리
- [x] T37 기준 이미지 승인 — 파일: `Data/e2e/baselines/000-sample-cards/**`(LFS) — 검증: **사용자 승인 후** `approve-baseline`, 재실행 통과 (AC-08) — 결과: 2026-11-02 사용자 승인. smoke → 기준 후보 6장 → `approve-baseline` 채택 → 재실행 판정 pass. **변경**: `smoke --scenario`가 스펙 증거를 덮어써서, 증거 폴더 기록은 `--spec` 지정 시로 한정
- [x] T38 verify 스킬 갱신 — 파일: `.claude/skills/verify/SKILL.md` — 검증: 스킬 절차가 `farm.py` 명령·`artifact-url.txt` 재발행과 일치 — 결과: `farm.py` 명령·`artifact-url.txt` 재발행 절차로 갱신. 같이 추가: `/pipeline` 스킬(사용자 요청)
- [x] T39 수용 기준 전체 통과 — Python 테스트 실패 0, `/unity-test` EditMode 실패 0, 콘솔 에러·경고 0, smoke ≤ 5분(AC-15) — 결과: Python 101개·EditMode 22개(+Explicit 3 제외) 실패 0, 플레이어 로그 경고 0, `_Project` 컴파일 경고 0, smoke 25s ≤ 300s, 훅 테스트 52/52
- [x] T40 `/verify 000` — `farm.py self-test all`로 AC-01~16 증거 `evidence/report.html` 생성, 아티팩트 발행·링크 기록, spec.md `완료` — 결과: 커밋 9306cbe(작업 트리 깨끗)에서 `self-test all` 19건 통과·AC-01~16 증거 연결, 판정 pass. 리포트 https://claude.ai/artifact/8QDktqWYZ4B5bqnCDLL9e5

## G. 개정
- [x] T41 배터리 판정 개정(2026-11-12 사용자 결정) — 파일: `farm/perf.py`(`power_state`·`power_judged`·`power_note`), `farm/bench.py`, `farm/evaluate.py`(`pmset_text`), `tests/test_perf.py`·`test_bench.py`·`test_logic_spec.py` — 검증: 파이썬 테스트 전체 — 결과: 배터리 + 저전력 모드만 판정 제외, 그냥 배터리는 판정. 계기: 011 검증에서 배터리 측정이 판정에서 빠져 충전기 연결 후 재측정(058 틱 평균 배터리 27.36 vs 전원 27.15 µs, 006 뼈대 76.9 vs 74.8 ms)

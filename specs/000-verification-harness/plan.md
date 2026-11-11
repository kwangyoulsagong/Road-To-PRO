# 000 — 검증 하네스 · 디바이스팜 · 플랜

상태: 완료
스펙: ./spec.md

## 접근
**맥 쪽 Python 오케스트레이터 + Unity 쪽 C# 드라이버**로 나눈다 (스펙 미정 "실행기 언어" 결정). Python(`Tools/devicefarm/`)이 프로필 매트릭스 전개, 플레이어 프로세스 실행·타임아웃·크래시 감지, 이미지 비교, 성능 판정, 로그 판정, 리포트 생성을 맡는다. Unity 쪽(`RoadToPro.Verification`)은 실행 인자로 받은 프로필·시나리오를 적용하고, 시나리오 단계를 실행해 **오프스크린 렌더 타깃**에서 캡처하고, 성능·결정론·로그를 JSON으로 떨군다. 둘 사이 계약은 파일(JSON) 뿐이라 각자 따로 테스트할 수 있다.
- 대안: 전부 Unity Editor 스크립트(C#)로 — 프로필마다 독립 프로세스·크래시 격리(FR-22)가 어렵고 Python 이미지 처리(Pillow·numpy)를 못 써서 기각. 전부 셸 — 비교·리포트가 너무 복잡해 기각.
- 아티팩트 발행(FR-72)은 스크립트가 할 수 없으므로 `/verify` 스킬에서 Claude가 Artifact 도구로 한다. 스크립트는 발행할 파일 목록과 이전 링크(`evidence/artifact-url.txt`)를 준비한다.

## 모듈 · 파일
| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| (Python) | `Tools/devicefarm/farm.py` | CLI 진입점: `logic` / `smoke` / `full` / `--profile <id>` / `--spec NNN` / `approve-baseline` / `approve-perf` / `self-test`. 단계별 진행 출력 (FR-80) |
| (Python) | `Tools/devicefarm/farm/preflight.py` | Unity·Chrome·Pillow·numpy 확인, 디스크 공간, 잠금 파일, 에디터 열림 감지. 실패는 `RunnerError`(실행기 오류)로 구분 (FR-81) |
| (Python) | `Tools/devicefarm/farm/profiles.py` | `profiles.json` 로드·검증, smoke/full × 테마·언어 조합 전개 (FR-10~13) |
| (Python) | `Tools/devicefarm/farm/logic.py` | `unity-test.sh` 호출, NUnit XML·로그 → 결과 모델, 컴파일 에러(`error CS`) 추출 (FR-01~03) |
| (Python) | `Tools/devicefarm/farm/build.py` | Unity 배치 빌드 호출(`FarmBuild.BuildMac`), 빌드 캐시 키(소스 해시) 비교 |
| (Python) | `Tools/devicefarm/farm/runner.py` | 프로필마다 플레이어 독립 프로세스 실행, 단계 타임아웃·무응답 30초·종료 코드 감지 (FR-22, 24, 61) |
| (Python) | `Tools/devicefarm/farm/compare.py` | 픽셀 차이 판정·마스크·차이 이미지·빈/단색/검은 화면 감지·해상도 불일치 실패 (FR-30~34) |
| (Python) | `Tools/devicefarm/farm/perf.py` | 성능 JSON 집계(p50/p95/p99), 예산 판정, 이전 통과 측정 대비 회귀(+10%) 판정, Performance Testing 결과 읽기 (FR-40~44) |
| (Python) | `Tools/devicefarm/farm/determinism.py` | 체크포인트 해시 목록 비교 → 처음 갈라진 체크포인트, 프로필 간 비교 (FR-50, 51) |
| (Python) | `Tools/devicefarm/farm/logs.py` | 플레이어·에디터 로그 수집, 에러·경고·예외·어서션 분류, 무시 목록 적용 (FR-60) |
| (Python) | `Tools/devicefarm/farm/report.py` + `templates/report.html` | `evidence/report.html`·썸네일·AC 연결표·화면 격자·성능 표, 용량 맞춤 (FR-70~74) |
| (Python) | `Tools/devicefarm/farm/chrome.py` | 헤드리스 Chrome 스크린샷(로직 리포트 PNG), 400px 레이아웃 검사(`--dump-dom`) |
| (Python) | `Tools/devicefarm/tests/test_*.py` | 실행기 자체 회귀 테스트 (`unittest`, Unity 불필요) |
| RoadToPro.Verification | `Assets/_Project/Verification/Runtime/FarmBootstrap.cs` | 실행 인자 파싱(`-farmProfile -farmScenario -farmOut -farmSeed`), 프로필 적용(해상도·품질·렌더 스케일·목표 fps·테마·언어·배터리 절약) |
| RoadToPro.Verification | `.../Runtime/FarmContext.cs` | 현재 프로필·테마·언어·배터리 절약 상태를 앱 코드가 읽는 곳 (UI 037 등이 나중에 구독) |
| RoadToPro.Verification | `.../Runtime/ScenarioRunner.cs`, `Steps/*.cs` | 시나리오 단계 실행기: 이동·누르기·입력·대기·단언·캡처·성능 구간·결정론 체크포인트·(셀프테스트 전용) 주입 |
| RoadToPro.Verification | `.../Runtime/OffscreenCapture.cs` | 카메라 + UI Toolkit 패널을 프로필 해상도 RenderTexture에 그려 PNG 저장. 워밍업 프레임, 연속 2프레임 동일 확인 (FR-23, 경계) |
| RoadToPro.Verification | `.../Runtime/LayoutAsserts.cs` | `겹침 없음`·`잘림 없음`: VisualElement `worldBound` 사각형 검사 (FR-26) |
| RoadToPro.Verification | `.../Runtime/PerfSampler.cs` | `FrameTimingManager` + `ProfilerRecorder`(GC Allocated In Frame, Draw Calls, Triangles, Texture Memory, Total Used Memory), 워밍업 제외 (FR-40) |
| RoadToPro.Verification | `.../Runtime/FarmLog.cs` | `Application.logMessageReceivedThreaded` → `logs.jsonl` |
| RoadToPro.Verification | `.../Runtime/IDeterminismProbe.cs`, `SampleSim.cs` | 결정론 체크포인트 공급 인터페이스. `SampleSim`은 000 검증용 작은 결정론 시뮬(정수 상태, 자체 xorshift, FNV-1a 64) |
| RoadToPro.Verification | `.../Samples/FarmSampleCards.unity`, `SampleCards.uxml/.uss`, `SampleCardsScreen.cs` | 샘플 화면(카드 3장 + 버튼), ko/en 문자열·light/dark 테마, en 넘침 라벨 (AC-03~08) |
| RoadToPro.Verification.Editor | `Assets/_Project/Verification/Editor/FarmBuild.cs` | 배치모드 빌드 메서드: 샘플 씬 포함 macOS 개발 빌드 → `Builds/Farm/` |
| RoadToPro.Tests.EditMode | `Assets/_Project/Tests/EditMode/Verification/*Tests.cs` | 드라이버의 순수 로직(인자 파싱, 단계 파싱, 레이아웃 사각형 판정, SampleSim 결정론) |
| RoadToPro.Tests.EditMode | `.../Verification/SelfTestFixtures.cs` | AC-01용 `[Explicit]` 픽스처(실패 1·통과 2). 이름으로 지정했을 때만 돈다 |
| RoadToPro.Tests.Performance | `Assets/_Project/Tests/Performance/SampleSimPerfTests.cs` | FR-41 경로 확인용 Performance 테스트 (SampleSim) |
| (데이터) | `Data/e2e/profiles.json`, `log-ignore.json`, `000-sample-cards.json`, `000-report-mobile.json` | 프로필, 무시 목록(사유 필수), E2E 시나리오 |
| (데이터, LFS) | `Data/e2e/baselines/<scenario>/<step>/<profile>-<theme>-<lang>.png` | 기준 이미지 (결정: 저장소 LFS) |
| (데이터) | `Data/e2e/perf-baselines.json` | 승인된 이전 통과 성능 측정 |

의존 방향 확인: `RoadToPro.Verification` → (나중에) `Core`·`MatchEngine` 등 도메인을 읽을 수 있는 **최상위 층**(Presentation·UI와 같은 층). 도메인 모듈은 Verification을 참조하지 않는다. `.claude/rules/10-csharp.md` 위반 없음. 10-csharp 모듈 목록에 `RoadToPro.Verification`(+Editor), `RoadToPro.Tests.Performance`를 추가하는 문서 수정이 함께 들어간다.

기타 변경:
- `Packages/manifest.json`: `com.unity.test-framework.performance` 추가 (스펙 결정으로 승인됨).
- `.gitignore`: `/.farm/` (실행 원본·잠금 파일), `/Assets/FarmSelfTest_Tmp/` (AC-02 임시 폴더).
- `CLAUDE.md` 레포 구조: `Tools/devicefarm/`, `Data/e2e/`, `Assets/_Project/Verification/` 추가.
- `.claude/skills/verify/SKILL.md`: `farm.py` 호출 방식으로 갱신.

## 데이터 · 스키마
모든 JSON은 LF·2칸 들여쓰기·`camelCase` (`40-data.md`). 각 파일에 `schemaVersion`.

- `profiles.json`: `{ schemaVersion, qualityMap, profiles: [{ id, description, width, height, uiScale, aspect, quality, renderScale, targetFps, batterySaver, isReferenceDevice, tier }] }` — 스펙 FR-11 표 10개 그대로. 테마·언어는 프로필에 넣지 않고 `tier`로 전개(FR-12).
- `qualityMap` `[제안]`: 현재 프로젝트 품질 레벨은 `Mobile`·`PC` 두 개뿐이다. 048이 프리셋을 정할 때까지 `Low→Mobile`, `Medium→Mobile`, `High→PC`로 매핑하고 렌더 스케일은 런타임에 URP 에셋 `renderScale`로 적용한다. `ProjectSettings/QualitySettings.asset`은 건드리지 않는다.
- E2E 시나리오 `Data/e2e/<id>.json`: `{ schemaVersion, id, spec, scene, fixture: { save, seed }, profiles: "all"|"smoke"|[ids], batterySaver, defaults: { timeoutSec: 10, pixelThreshold: 8, maxDiffRatio: 0.001 }, steps: [{ type, ... }] }`
  - `type`: `goto(screen)`, `tap(element)`, `type(element, text)`, `waitFor(element | simTime | highlightScene)`, `assertVisible`, `assertText`, `assertNumberRange`, `assertNoOverlap(scope)`, `assertNoClip(scope)`, `capture(name, masks[], maxDiffRatio?, pixelThreshold?)`, `perfBegin(name)`, `perfEnd(name)`, `checkpoint(name)`, `freeze(simTime | scene, speed)`, `inject(warning|exception|crash)` (셀프테스트 시나리오만 허용, 일반 시나리오에 쓰면 로드 실패).
  - 마스크: `{ element }` 또는 `{ rect: [x,y,w,h] }`(프로필 해상도 기준 비율 0~1).
  - 3D 장면 캡처는 `capture.kind: "3d"`면 기본 `maxDiffRatio` 0.005 (FR-31).
- 플레이어 출력 `.farm/runs/<runId>/<profile>-<theme>-<lang>/`: `steps.jsonl`(단계별 결과·소요), `captures/<step>.png`, `perf.json`, `checkpoints.json`(`[{ name, tick, hash }]` — **001이 이 형식으로 체크포인트를 낸다**), `logs.jsonl`, `player.log`, `exit.json`.
- `log-ignore.json`: `[{ pattern(정규식), reason(필수), addedBy, added }]`. 사유 없는 항목은 로드 실패.
- `perf-baselines.json`: `{ "<scenario>/<perfSection>/<profile>": { metrics, commit, measuredAt } }`. `approve-perf` 명령으로만 갱신 (FR-43).
- 증거 `specs/NNN-*/evidence/`: `report.html`, `assets/`(JPEG 썸네일·차이 이미지 축소본·`logic.png`), `perf.json`, `determinism.json`, `logs-summary.json`, `run.json`(실행 명령·인자·커밋·브랜치·시각·작업 트리 변경 여부), `artifact-url.txt`. 원본 캡처는 `.farm/runs/`에 로컬 보관.

## 결정론 · 성능
- 이 스펙의 C# 코드는 코어 모듈이 아니다 (Unity 참조 필요). 단 `SampleSim`은 결정론 규칙을 지킨다: 시드 인자 주입, 정수 상태, 자체 RNG, `System.Random`·시계 미사용. AC-11의 "RNG 호출 1개 추가"는 `-farmSampleSimExtraRngAtTick <n>` 인자로 주입.
- 001이 들어오면 `SampleSim`의 RNG·해시를 001 것으로 바꾸지 않는다 — `SampleSim`은 하네스 자체 검증용으로 남고, 실제 경기 시뮬은 `IDeterminismProbe`를 구현해 같은 `checkpoints.json`을 낸다. 체크포인트 비교(처음 갈라진 지점)는 Python 쪽 한 곳에만 있다 (001 FR-61의 C# 비교 함수와 역할이 겹치지 않게: 001은 프로세스 내부 비교, 000은 프로세스 간 파일 비교).
- 화면 결정론(같은 커밋 2회 = 차이 0): 고정 세이브·고정 시드, `Time.captureDeltaTime`으로 고정 프레임 진행, 카운트업·트랜지션 완료 대기 후 캡처, 연속 2프레임 동일 확인. 시계·네트워크 의존 없음 (FR-21, 23).
- 성능 측정: `perfBegin` 뒤 워밍업 30프레임 `[제안]` 버리고 측정. 성능 구간 안에서는 캡처 금지(구간 안에 `capture`가 있으면 시나리오 로드 실패). 측정 중에는 `captureDeltaTime`을 끄고 실제 프레임으로 잰다.
- 성능 판정은 `isReferenceDevice=true` 프로필 측정만 사용, 나머지는 기록·상대 비교만 (FR-44). 지금은 `mac-m1-1600p`만 참. 전원 상태는 `pmset -g batt`(출처) + `pmset -g`(lowpowermode)로 읽어 `ac`·`battery`·`battery-lowpower`. `battery-lowpower`만 판정 제외(2026-11-12 개정).
- 하네스 자체 예산(스펙 성능 예산 표): `run.json`에 단계별 실행 시간 기록 → 리포트에 예산 대비 표시.
- 리포트 크기 ≤ 15 MB: 썸네일 JPEG(긴 변 480px, 품질 70) → 합계 초과 시 360px·품질 55로 한 번 더, 그래도 넘으면 실패가 아니라 "원본 로컬 경로" 안내로 대체.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `farm.py self-test logic-fail` → `SelfTestFixtures` 실행, 리포트 맨 위 실패 1건 / `test_logic_report.py::test_failures_listed_first` | 로직 | — |
| AC-02 | `farm.py self-test compile-error` (임시 `Assets/FarmSelfTest_Tmp/CompileError.cs` 생성 → 실행 → 반드시 삭제) / `test_logic.py::test_compile_error_extracted` (실제 로그 픽스처) | 로직 | — |
| AC-03 | `e2e/000-sample-cards` 캡처 3장 · `test_compare.py::test_resolution_mismatch_fails` | E2E | smoke |
| AC-04 | `e2e/000-sample-cards` 2회 실행 → 차이 0 (`farm.py self-test stability`) | E2E | smoke |
| AC-05 | `farm.py self-test diff` (`-farmSampleButtonColor` 인자로 버튼 색 변경) · `test_compare.py::test_diff_image_highlights_region` | E2E | smoke |
| AC-06 | `e2e/000-sample-cards` 단계 `assertNoClip`(en 넘침 라벨) · `LayoutAssertsTests.NoClip_OverflowingLabel_Fails` | E2E | smoke (en 강제) |
| AC-07 | `farm.py self-test black` (`-farmSampleBlack`) · `test_compare.py::test_blank_and_solid_fail_without_baseline` | E2E | smoke |
| AC-08 | `e2e/000-sample-cards` 첫 실행 "기준 후보" → `approve-baseline` → 재실행 통과 · `test_compare.py::test_missing_baseline_is_candidate` | E2E | smoke |
| AC-09 | `e2e/000-sample-cards` 단계 `perfBegin/End` → `perf.json` 필드 존재 · `test_perf.py::test_over_budget_fails` | 성능 | smoke |
| AC-10 | `test_perf.py::test_regression_15pct_fails_5pct_passes` (고정 수치. 실제 측정 잡음으로 5%/15%를 가르는 E2E는 불안정해 로직으로 검증) | 성능 | — |
| AC-11 | `SampleSimTests.SameSeed_TwoRuns_SameCheckpoints`, `farm.py self-test determinism` (`-farmSampleSimExtraRngAtTick`) · `test_determinism.py::test_first_divergence_reported` | 결정론 | smoke |
| AC-12 | `e2e/000-selftest-logs` (`inject: warning`) → 실패 + 로그 요약 줄 / 무시 목록 추가 후 통과·무시 항목 표시 · `test_logs.py::*` | 로직 | mac-m1-1600p |
| AC-13 | `e2e/000-selftest-crash` (`inject: crash`, 프로필 1개만) → 나머지 정상 · `test_runner.py::test_crash_isolated` | E2E | smoke |
| AC-14 | `e2e/000-report-mobile`: Chrome `--window-size=400,…` `--dump-dom`으로 리포트 내 검사 스크립트가 기록한 `scrollWidth ≤ 400`(격자·표·코드 제외) 확인 + 스크린샷, 발행 링크 출력 · `test_report.py::test_header_has_commit_branch_time` | E2E | — |
| AC-15 | `farm.py smoke` 실행 시간 ≤ 5분 (빌드 캐시 있음) → `run.json` | 성능 | mac-m1-1600p |
| AC-16 | `e2e/000-sample-cards` 단계 `checkpoint` 해시가 smoke 3 프로필에서 동일 · `test_determinism.py::test_cross_profile_equal` | E2E | smoke |

셀프테스트(`farm.py self-test all`)가 AC-01~16 증거를 모아 `specs/000-verification-harness/evidence/report.html`을 만든다. 셀프테스트의 "일부러 실패" 결과는 기대 실패로 표시되고, 기대대로 실패했으면 해당 AC는 통과다.

## 버그 방지
- **임시 파일 잔류**: AC-02 임시 컴파일 에러 파일·`.meta`는 `try/finally`로 삭제, 실행 시작 시 잔류 폴더가 있으면 먼저 정리. 경로는 `.gitignore`. → `test_selftest.py::test_tmp_cleanup_on_failure`
- **레티나 2배 캡처**: 캡처는 화면이 아니라 RenderTexture에서 하므로 OS 배율 영향 없음. 저장 PNG 크기 ≠ 프로필 해상도면 실패 → `test_compare.py::test_resolution_mismatch_fails`
- **기준 이미지 리사이즈 비교 금지**: 크기 다르면 비교 없이 실패.
- **첫 프레임 셰이더·폰트**: 워밍업 프레임 + 2프레임 동일 확인, 10회 안에 안정 안 되면 "불안정" 실패.
- **빈 화면 판정 오탐**: 단색 판정은 픽셀 분산이 임계값 이하일 때. 샘플 화면(회색 바탕 + 흰 카드)이 통과하는지 테스트 → `test_compare.py::test_card_screen_not_solid`
- **프로세스 행·좀비**: 무응답 30초면 프로세스 그룹 종료, 프로필 실패로 기록하고 다음 프로필 진행. 실행 종료 시 잠금 파일 해제(`finally` + 오래된 잠금은 PID 확인 후 제거).
- **에디터 열림**: `Temp/UnityLockfile` 점유 시 실행기 오류로 종료, 에디터 종료 시도 안 함.
- **디스크 부족**: 실행 전 여유 공간(기본 5 GB `[제안]`) 확인.
- **무시 목록 남용**: 사유 빈 항목 로드 실패, 리포트에 무시 항목 수·사유 항상 표시.
- **작업 트리 변경**: `git status --porcelain` 비어 있지 않으면 리포트 경고 + 완료 판정 불가 표시.
- **성능 구간 안 캡처**: 시나리오 로드 시 검증 → `ScenarioParserTests.CaptureInsidePerf_Rejected`
- **`inject` 오남용**: 셀프테스트가 아닌 시나리오의 `inject` 단계는 로드 실패 → `ScenarioParserTests.InjectOutsideSelfTest_Rejected`
- **긴 en 문자열**: `assertNoClip`은 텍스트 측정 후 부모 `contentRect` 비교, 1px 허용 오차 → `LayoutAssertsTests`

## 위험 · 미정
- `[제안]` 품질 매핑 `Low→Mobile, Medium→Mobile, High→PC` (048 전까지 임시). `QualitySettings.asset`은 바꾸지 않음.
- `[제안]` 워밍업 30프레임, 디스크 여유 5 GB, 썸네일 480px/JPEG 70.
- UI Toolkit 패널을 RenderTexture로 그리는 방식(`PanelSettings.targetTexture`)이 실제 화면 렌더와 다를 수 있다 → 샘플 화면에서 화면 캡처와 1회 대조해 확인하고, 다르면 멈추고 보고.
- 맥에서 `low-igpu-1080p` 등은 흉내 프로필이라 성능 수치는 상대 비교용 (스펙 결정: 원격 기기는 P1 이후).
- smoke 5분 예산은 Unity 빌드가 캐시 있을 때 기준. 첫 빌드는 예산 밖이며 리포트에 따로 표시.
- 하네스 자체가 증거 체계라 000의 증거는 셀프테스트로 만든다 (스펙 "검증 · 증거" 절과 일치).

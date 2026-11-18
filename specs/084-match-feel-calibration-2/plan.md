# 084 — 경기 양상 2차 보정 (GK 선방 · 패스 · 드리블 탈취 · 컷백) · 플랜

상태: 승인됨 — 보류 (2026-11-18 T17 검증): AC-01·05·06·07 통과, AC-02·03·04 보류(D19·D20·D22 결정 대기). 보류가 풀리기 전에는 `완료` 아님. 리포트 https://claude.ai/artifact/VzT2AL31mctCDn6MZRyBPa (플랜 승인 2026-11-18)
스펙: ./spec.md

## 접근
새 규칙·새 수식 없이 **튜닝 계수만** 바꾸는 보정 스펙이다(Q2: 수식 구조 유지). 081 측정 도구(`MatchFeelMetrics` + `MatchFeelReportTests.Report_100Matches`, 시드 7400 + i, N = `.farm/feel-n.txt` 100)를 그대로 쓰고, 컷백은 081 T19의 플레이어 256시드 탐색(`FeelSceneCoverage`, 비트 `CutbackGoal`)을 쓴다. 순서는 Q1대로 **① 기준 재측정 → ② D20 드리블 → ③ D19 패스 → ④ D18 GK → ⑤ D22 컷백 → ⑥ 전체 회귀 측정**이고, 항목마다 "원인 분해 → 계수 1~2개 변경 → 100경기 측정 → FR-07 확인"을 최대 3회 돈다(081 루프와 같은 상한). 3회 안에 못 들어가면 그 항목은 Q3대로 **보류**(사유·측정값 기록, README 결정 대기로 되돌림)하고 다음 항목으로 간다.
대안: 네 항목을 한 번에 동시 보정 → 지표끼리 얽혀(패스 성공 ↔ 탈취 ↔ 슈팅 수) 어느 개정이 무엇을 바꿨는지 원 스펙 기록(FR-06)을 못 남긴다. 그래서 항목별 순차.

### 플랜이 스펙 해석으로 정하는 것 (승인 질문)
1. **기준(FR-01)**: 스펙은 082 `measure-after.json`을 기준으로 적었지만, 그 뒤 083이 시뮬(슬라이딩 공 거리 조건)을 바꿨다. 그래서 **현재 main(083 포함)에서 개정 전 100경기를 다시 재서 `evidence/measure-before.json`으로 쓰고**, 082 값과 나란히 보여준다. AC 판정·FR-07 "이미 범위 안" 판정은 이 재측정 기준.
2. **[대체] 손잡이 먼저**: 각 항목에서 아직 [대체]인 계수(예: `act.drib.stealK`, `act.cross.cutbackLine`, `gk.reach.sReach`)가 남아 있으면 먼저 쓰고, 막히면 [설계]로 간다. [설계] 값은 튜닝 min·max 안에서만(경계 규칙: 밖이 필요하면 멈추고 원 스펙 개정 제안).
3. **FR-05 컷백 하한 [제안]**: 256시드 중 **3개 이상**(≈ 1.2 %, 지금 0~1). 표본이 작으니 미달·경계면 512시드로 늘려 재측정하고 늘린 수를 기록(경계 규칙).
4. **D19가 007 없이는 안 될 때**: 이 스펙에서 007을 하지 않는다. 3회 뒤 원인 분해에서 실패 원인 1위가 "인터셉트(레인 판단)"이고 008·010 계수로 못 줄이면 AC-02 보류 + "007 피치 컨트롤 연결 스펙" 제안을 리포트에 남긴다.
5. **보정 실험 위치**: 루프 중 계수 변경은 튜닝 JSON만 고치고, 확정한 값은 코드 기본값(`ActionParams` 등)도 같은 커밋에서 맞춘다 — 081과 같음.
6. **081 출처 테스트 완화**: 081 `TuningProvenanceTests`는 "[설계] 값 = 081 시작 main"을 강제한다. 084는 [설계] 개정 스펙이라 `084:` 기록이 있는 id만 그 비교에서 빼고, 084 전용 출처 테스트를 새로 둔다(아래 모듈 표).

## 항목별 손잡이 (후보, 값은 측정으로 정함)
| 항목 | 먼저 볼 원인 분해 (측정 도구 기존 칸) | [대체] 손잡이 | [설계] 손잡이 (원 스펙 · min~max) |
|---|---|---|---|
| D20 드리블 탈취 16.7 % → ≤ 12 % | `stealPerTouch`, 터치 거리 분포, 탈취 순간 TTI 차 | `act.drib.stealK`(9), `act.drib.distSigma`, `act.drib.speedNormMax` (`stealMargin`은 상한 1.0 도달) | 070 FR-02 `act.drib.touchLo`(0.8, 0.1~5)·`touchHi`(4, 1~8)·`skillLo/Hi` — 터치 거리를 줄여 상대 TTI 우위를 줄임 |
| D19 패스 453·55 % → ≥ 550·≥ 60 % | `passFate`(Received 49 %·Intercepted 18 %·LostLoose 10 %·Out 9 %·Aerial 8 %) | `act.touch.sigmaBase/Slope/pressK`(첫 터치 이격), `act.recv.touchRelief`, `act.pass.loftPenalty`, `dec.support.*`(reachMul·arc·floor) | 008 FR-11 `act.xpass.b0~b7`(평가 쪽 — 위험한 패스를 덜 고름), 008 FR-12 `act.recv.decayMin`·`act.pass.ground0~3`, 010 FR-19 `dec.support.min/max`([제안] 8·15 m) |
| D18 선방 57 %·전환 18.5 % → 60~80 %·≤ 18 % | 슈팅 위치·높이별 선방, 도달 마진 분포 | `gk.reach.sReach`, `gk.save.alpha/beta/gamma`, `gk.reach.highZ/highFade`(높은 구석 기준 지킴) | 011 FR-70 `gk.map.react.base`(0.32, 0.1~0.6)·`dive.base`(3.0, 1~6)·`dive.slope`, 011 FR-03 `gk.reach.r0`(0.8, 0.2~2) |
| D22 컷백 0~1/256 → ≥ 3/256 | 컷백 타입 선택 수·조건 불충족 사유(골라인 거리) | `act.cross.cutbackLine`(6 m, 0~20), `act.cross.styleK` | 071 FR-01 `act.cross.cutback.target`(0.4), 072 크로스 선택 가중(해당 [설계] 키가 있을 때만) |

- `rmaxBase` 2.6 → 3.0은 081에서 011 `HighCorner_Below15`를 깨 되돌렸다 → D18은 반응·다이빙 속도·r0(낮은·중간 높이 도달에 더 크게 듣는 쪽)부터.
- 측정 칸이 없는 원인(D22 "조건 불충족 사유", D18 "높이별 선방")은 `MatchFeelMetrics`에 **진단 카운터만** 더한다(해시·스냅샷 밖, 081 `BothTeamContests`와 같은 방식).

## 모듈 · 파일
경로 접두: `T/` = `Assets/_Project/Tests/EditMode/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| — | `Data/tuning/action.json`, `goalkeeper.json`, `decision.json` (수정) | 개정 값 + `source`에 `084: <이전> → <새>, <측정 근거>` (FR-06) |
| RoadToPro.MatchEngine | 각 튜닝 id의 코드 기본값(`ActionParams`·`GoalkeeperParams`·`DecisionParams` 등, 수정) | 파일 = 코드 기본값 유지. 로직 변경 없음 |
| 〃 (필요할 때만) | `ME/Actions/*` 진단 카운터 (수정) | D18·D22 원인 분해용 누계. 시뮬 상태 아님 |
| RoadToPro.Tests.EditMode | `T/MatchEngine/MatchFeelMetrics.cs` (수정) | 진단 칸 추가(높이별 선방, 컷백 후보 사유), 081 FR-10 범위 판정 표에 084 목표 반영 |
| 〃 | `T/MatchEngine/TuningProvenanceTests.cs` (수정) | 081 테스트 2개(`ChangedIdsAreStandIn_WithSource`·`ShotAimDesignCoefficients_Unchanged`)는 [설계] 변경을 막는다 → **source에 `084:` 기록이 있는 id만 081 비교에서 뺀다**(081 보증은 그 외 계수에 계속 적용). 새 테스트 `Tuning084_ChangedIds_HaveSource`: 기준 `evidence/tuning-before.json`(084 시작 main 합본) 대비 바뀐 id마다 source에 `084:` 기록, 태그 불변, 값이 min·max 안. 071 FR-14 σ_aim 등 후보 표에 없는 [설계] 값은 계속 불변 |
| 〃 | 고정 해시·고정 수치 테스트 (갱신될 수 있음) | NFR: 바뀐 테스트 목록·이전/새 값·사유를 리포트에 |
| RoadToPro.Verification | `Runtime/Bench/SimBench.cs` (수정) | `--spec 084` 등록(081 워크로드 재사용) |
| — | `Data/perf/084-match-feel-calibration-2.json` (새) | 벤치 예산(081 값 그대로) |
| — | `specs/011·008·010·070·071·072/spec.md` (수정) | 바꾼 [설계] 계수의 FR에 "084 개정: 이전 → 새, 근거" (FR-06) |

의존 방향 확인: 엔진 쪽은 튜닝 기본값·진단 카운터만, `MatchEngine` → Core. UnityEngine 비참조. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- 측정 JSON: `evidence/measure-before.json`(①), 항목·회차별 `measure-d20-1.json` … `measure-d22-N.json`, `measure-after.json`(⑥). 형식은 081 그대로(라벨·N·시드·커밋·perMatch·passFate) + 진단 칸.
- 컷백 탐색: `evidence/cutback-coverage.json` — 시드 수, `CutbackGoal` 시드 목록, 빌드 커밋.
- 튜닝 스키마·세이브·스냅샷 변경 없음.

## 결정론 · 성능
- RNG 사용처·순회 순서 변경 없음(계수만). 계수가 바뀌면 같은 시드의 경기 결과가 달라지므로 고정 해시 기대값 테스트는 갱신하고 목록을 남긴다(NFR). 같은 시드 2회 해시 같음은 081 `Determinism_90min_FeelHashSame`로.
- 진단 카운터는 해시 밖 — 081 `Determinism_DiagnosticsOnOff_SameHash` 재실행.
- 성능: 계수 변경이 후보 수·드리블 터치 빈도·결정 분기를 바꿔 틱 비용이 달라질 수 있다 → IL2CPP 벤치 `--spec 084` 1회(결정 틱 ≤ 12 µs, 틱 GC 0 B). 코어 변경이라 `determinism-reviewer` 리뷰.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `MatchFeelReportTests.Report_100Matches`(Explicit, `measure-after.json`) GK 선방률·전환율 판정 + 011 `SaveModelTests.HighCorner_Below15_LowAboveHigh` | 로직 | — |
| AC-02 | `Report_100Matches` 패스 수·성공률 판정 | 로직 | — |
| AC-03 | `Report_100Matches` 터치당 탈취율 판정 + 072 `OnBallDeciderTests.Twin_DribbleAddict_MoreDribble_FewerPasses_AC05` | 로직 | — |
| AC-04 | 플레이어 빌드 `FeelSceneCoverage` 256시드(부족·경계면 512) `CutbackGoal` 개수 ≥ 3 → `cutback-coverage.json` | 로직 | mac-m1-1600p (플레이어 빌드) |
| AC-05 | `Report_100Matches` 081 FR-10 표 전 지표 — `measure-before.json`에서 범위 안이던 지표가 after에서도 안(득점 제외) | 로직 | — |
| AC-06 | 081 `Determinism_90min_FeelHashSame`, `Determinism_DiagnosticsOnOff_SameHash` | 결정론 | — |
| AC-07 | IL2CPP 벤치 `--spec 084`(결정 틱 ≤ 12 µs, 틱 GC 0 B) | 성능 | mac-m1-1600p |
| (FR-06) | `Tuning084_ChangedIds_HaveSource`, 081 `TuningProvenance_*` 2개(084 기록 id 제외), 각 모듈 "파일 = 코드 기본값" 테스트(예: `StyleMultiplierTests.FileDefaults_EqualCodeDefaults`) | 로직 | — |

보류된 AC는 리포트에 "보류 — 사유·측정값"으로 표시하고 완료 조건에서 뺀다(스펙 Q3).

## 버그 방지
- **얽힘**: 한 항목 개정 뒤 매번 전체 지표 표를 뽑아 FR-07 확인(이전 항목에서 맞춘 지표 포함). 밀어내면 되돌리고, 순서 바꿔 재시도 1회(경계 규칙).
- **011 높은 구석 기준**: D18 회차마다 `HighCorner_Below15`를 필터로 같이 돌린다(081에서 이걸로 되돌린 전례).
- **072 AC-05 방향**: D20 회차마다 `Twin_DribbleAddict` 필터 실행 — 탈취를 줄여도 드리블 중독 > 일반 유지.
- **표본 흔들림**: 경계값(목표 ± 1 %p 안)이면 시드 묶음을 7400 + i(100) 외에 8400 + i(100)로 한 번 더 재서 둘 다 범위 안일 때만 통과로 본다.
- **에디터 ↔ 플레이어 차이**(081 T19): 컷백은 반드시 플레이어 빌드 안 탐색으로 판정, 백그라운드 스레드(메인 30 s 막으면 farm 무응답).
- **튜닝 min·max 밖**: 값이 범위 끝에 닿으면 그 회차에서 멈추고 원 스펙 개정 제안.

## 위험 · 미정
- 평균 득점 ≈ 10은 017 몫이라 기록만 — 하지만 D18 개정으로 크게 줄 수 있다(좋은 방향, 판정 안 함).
- D19는 근본 원인이 007(피치 컨트롤 없는 수신·레인 판단)일 가능성이 높다 → 보류 가능성 큼(해석 4).
- 계수 변경이 E2E 고정 시드(081·082·083 장면)를 바꾼다 — 이 스펙은 E2E 없음. 재촬영은 별도(085가 082 장면 재확인을 이미 포함).
- 100경기 측정 1회 ≈ 수 분 — 항목당 최대 3회 + 기준·최종 = 최대 14회. 백그라운드로 돌리고 회차마다 결과만 보고.

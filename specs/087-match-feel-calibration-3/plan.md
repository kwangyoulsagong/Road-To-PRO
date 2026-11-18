# 087 — 경기 양상 3차 보정 (드리블 탈취 판정 개정 · 컷백 선택 가중) · 플랜

상태: 완료 (2026-11-18 T13 검증): AC-01·02·03·05·06·07 통과, AC-04 보류(D22 결정 대기, 2026-11-18 사용자 승인 "베스트케이스") — 스펙 경계 규칙(3회 안 미달 → 보류)대로 완료 조건에서 뺌. 리포트 https://claude.ai/artifact/3eewsDVb3Fycx1mqSBoSnB (플랜 승인 2026-11-18)
스펙: ./spec.md

## 접근
084와 같은 측정 루프(`MatchFeelReportTests.Report_100Matches`, 시드 7400 + i / 확인 8400 + i, N = 100)를 쓰되, 이번에는 **판정 식 1곳 개정(D20) + 상수 1개 튜닝화(D22)**가 먼저다. 순서: ① 기준 재측정(086 완료 main) → ② D20 판정 식 개정(FR-02) → 측정 → 필요하면 계수 보정(FR-03, ≤ 3회) → ③ D22 가중치 튜닝화(FR-04, 비트 동일 확인) → 가중치 보정 + 플레이어 768시드 탐색(FR-05, ≤ 3회) → ④ 최종 두 묶음 측정·회귀.
대안: D22를 조건 거리(`cutbackLine`)로 다시 보정 → 084에서 D18을 밀어냈다(Q2 ⓐ로 가중치 먼저).

### 플랜이 스펙 해석으로 정하는 것
1. **"쫓는 중" 판정(Q1 ⓐ)**: 탈취 판정은 물리 서브스텝의 `DuelSystem.OnDribble`에서 일어나고, 이 층은 010 결정 상태(`PlayerDecisionState`)를 보지 않는다(Actions → UtilityAI 역참조 금지). 그래서 "직전 결정이 드리블 이동"을 **판정에 이미 있는 정보로** 읽는다: 소유자에게 예약된 킥(`ActionSystem.Pending`, 패스·슈팅·크로스로 바뀜)이 없고 소유자 속도 ≥ `act.drib.minSpeed`. 드리블 터치는 이 두 조건에서만 시작되므로(`OnDribble` 새 터치 조건과 같음) 같은 기준이다. 멈춤(Hold)이면 속도가 떨어져 조건 밖, 패스·슈팅이면 예약 킥으로 조건 밖 — 스펙 경계 2줄과 일치.
2. **반응 시간 뺀 도달 시간**: 005 `TimeToIntercept.Context`는 시작점을 `pos + vel·reaction`으로 외삽하고 `reaction`을 더한다. 쫓는 소유자는 **외삽 없이 현재 위치·속도에서 반응 0**으로 같은 식(`RunTime` + 방향 전환 비용)을 쓴다 — 새 순수 함수 `TimeToIntercept.EstimateNoReaction(in Context, x, y)`. 상대 쪽(`w.Tti[ch].Reaction`)은 그대로.
3. **FR-03 계수 순서**: `act.drib.stealMargin`(지금 1.0 = 상한)을 먼저 내리는 방향(판정 식이 소유자에게 유리해져 오히려 여유를 줄일 수 있음), 탈취율이 아직 높으면 `stealK` → 마지막으로 범위 상한 1.0 → 2.0 [제안]. 탈취율이 너무 떨어져 테이크온 성공률(30~45 %)이 넘치면 여유를 줄여 되돌린다.
4. **컷백 가중치 id**: `act.cross.cutbackW`(기본 3, min 0 · max 20, `[대체]` — 071 플랜의 타입 가중이 [대체]). 기본 3에서 해시 동일.
5. **기준 768시드 측정**: FR-05대로 첫 측정에서 기준(6 m·가중 3) 플레이어 768시드 수를 잰다(084 T13과 같은 `farm.py shot --scenario tool-feel-search`).
6. **출처 테스트**: 084 `Tuning084_ChangedIds_HaveSource`와 같은 방식으로 `Tuning087_ChangedIds_HaveSource`(기준 `evidence/tuning-before.json` = 087 시작 main 합본). 081 테스트의 "[설계] 불변" 제외 목록에 `087:` 기록 id도 넣는다(084와 같은 규칙).

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `ME/Kinematics/TimeToIntercept.cs` (수정) | `EstimateNoReaction(in Context, x, y)` 추가 — 외삽 전 위치·반응 0. 기존 `Estimate` 불변 |
| 〃 | `ME/Actions/DuelSystem.cs` (수정) | FR-02: 이격 탈취 판정에서 쫓는 소유자면 `EstimateNoReaction`, 아니면 지금 `Estimate`. 진단 카운터 `StealChasing`(해시 밖) |
| 〃 | `ME/Actions/CrossModel.cs`, `ActionParams.cs`, `ActionTuningIds.cs` (수정) | FR-04: 컷백 가중 상수 3 → `p.CrossCutbackW` |
| — | `Data/tuning/action.json` (수정) | `act.cross.cutbackW` 추가, 보정 값 + source `087: <이전> → <새>, <근거>` |
| RoadToPro.Tests.EditMode | `T/Kinematics/TimeToInterceptTests.cs` (수정) | `EstimateNoReaction` 고정값: 정지 선수 = Estimate − reaction, 달리는 선수는 외삽 없이 |
| 〃 | `T/Actions/DribbleCycleTests.cs` (수정) | AC-01: 쫓는 소유자/예약 킥 소유자/저속 소유자 판정 도달 시간 고정 픽스처, 기존 `Gap4m_StealsMore_Than1m_1000` |
| 〃 | `T/Actions/CrossModelTests.cs` (수정) | AC-03: 기본 가중 3, 값 올리면 컷백 선택 비율 상승 |
| 〃 | `T/TuningProvenanceTests.cs` (수정) | `Tuning087_ChangedIds_HaveSource` + 081 비교에서 `087:` id 제외 |
| 〃 | 고정 해시·고정 수치 테스트 (갱신될 수 있음) | NFR: 목록·이전/새 값·사유를 리포트에 |
| RoadToPro.Verification | `Runtime/Bench/SimBench.cs` (수정) | `--spec 087` 등록(081 워크로드) |
| — | `Data/perf/087-match-feel-calibration-3.json` (새) | 벤치 예산(084 값 그대로) |
| — | `specs/070-*/spec.md` FR-03, `specs/071-*/spec.md`·`plan.md` 타입 선택 (수정) | FR-08 "087 개정" 기록 |

의존 방향: MatchEngine 내부(Actions → Kinematics)만, UnityEngine 비참조. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- 튜닝 id 1개 추가(`act.cross.cutbackW`). 세이브·스냅샷 스키마 변경 없음(튜닝은 세이브 밖).
- 측정 JSON: `evidence/measure-before.json`(①, 086 `measure-after`와 같아야 함), `measure-d20-0.json`(식 개정만), `measure-d20-N.json`, `measure-d22-N.json`, `measure-after.json`·`measure-after-8400.json`. 컷백 탐색: `evidence/cutback-coverage.json`(기준·회차별 256·512·768 수).

## 결정론 · 성능
- RNG 소비 순서 불변: 탈취 판정은 이미 뽑아 둔 `_d.StealU`를 쓰고 확률만 바뀐다. 크로스 타입 선택도 결정 RNG 1회 그대로.
- 식이 바뀌면 같은 시드 경기 결과가 달라지므로 고정 해시 기대값은 갱신·목록화. 2회 해시 같음은 081 `Determinism_90min_FeelHashSame`, 진단 카운터는 `Determinism_DiagnosticsOnOff_SameHash`.
- 성능: 판정 1회당 함수 하나 바꿈(같은 비용). 판정 식 변경 = 틱 경로라 IL2CPP 벤치 `--spec 087` 1회. 코어 변경이라 `determinism-reviewer` 리뷰.

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `TimeToInterceptTests.EstimateNoReaction_*`, `DribbleCycleTests.StealCheck_ChasingOwner_NoReaction`(쫓는/예약 킥/저속 3경우), `DribbleCycleTests.Gap4m_StealsMore_Than1m_1000` | 로직 | — |
| AC-02 | `Report_100Matches`(7400) 터치당 탈취율 ≤ 12 % + `OnBallDeciderTests.Twin_DribbleAddict_MoreDribble_FewerPasses_AC05` | 로직 | — |
| AC-03 | `CrossModelTests.CutbackWeight_Tunable_DefaultSame_HigherPicksMore` + 기본값에서 `Determinism_90min_FeelHashSame` 해시가 T03 전 기록값과 같음 | 로직 | — |
| AC-04 | 플레이어 빌드 `tool-feel-search` 768시드 `CutbackGoal` ≥ 5 → `cutback-coverage.json` | 로직 | mac-m1-1600p (플레이어) |
| AC-05 | `Report_100Matches` 7400·8400 두 묶음: 선방 60~80 %·전환 ≤ 18 %, 084 FR-07 보호 8개 범위 안 | 로직 | — |
| AC-06 | `Determinism_90min_FeelHashSame`, `Determinism_DiagnosticsOnOff_SameHash` | 결정론 | — |
| AC-07 | IL2CPP 벤치 `--spec 087`(결정 틱 ≤ 12 µs, 틱 GC 0 B) | 성능 | mac-m1-1600p |
| (FR-08) | `Tuning087_ChangedIds_HaveSource`, 081 출처 테스트 2개, `FileDefaults_EqualCodeDefaults`류 | 로직 | — |

## 버그 방지
- **식 개정이 너무 셈**: FR-02만으로 탈취가 크게 떨어지면 테이크온 성공률·슈팅 수가 튈 수 있다 → `measure-d20-0`에서 FR-07 표 먼저 보고 계수 방향 정함.
- **070 AC-06 방향**: 짧은 터치는 쫓는 소유자에게 더 유리해져 4 m/1 m 차이가 오히려 커질 것(긴 터치는 공이 멀어 이동 시간이 큼) — 그래도 회차마다 `Gap4m_*` 필터 실행.
- **072 AC-05**: D20 회차마다 `Twin_DribbleAddict` 필터.
- **D18 밀림**: D22 회차마다 7400 측정의 선방·전환 확인, 768시드 통과 후보는 8400도 측정.
- **에디터 ↔ 플레이어 차이**: 컷백 판정은 플레이어 빌드 탐색만.
- **튜닝 범위 끝**: `stealMargin` 하한 −1·상한, `cutbackW` 상한 20에 닿으면 멈추고 보고.

## 위험 · 미정
- 컷백 골은 플레이어 경기당 ≈ 0.8 %라 768시드도 표본이 작다(±2~3개). 경계(4~6개)면 결과를 그대로 기록하고 판정은 스펙 기준대로.
- 판정 식 변경이 081·082·083·086 E2E 고정 시드 장면을 바꿀 수 있다 — 이 스펙은 E2E 없음. logic 범위의 시드 기반 실경기 테스트(`LiveContactAuditTests` 등)가 깨지면 084 T15a처럼 시드 재선택 태스크를 추가한다.
- 측정 1회 ≈ 1~1.5분(100경기), 768시드 탐색 ≈ 10분(플레이어 빌드 포함). 백그라운드.

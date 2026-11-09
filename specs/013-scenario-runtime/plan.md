# 013 — 시나리오 런타임 · 플랜

상태: 구현 완료 · 4만 개 메모리 보류(014) (2026-11-09)
스펙: ./spec.md

## 접근
006 파이프라인의 단계 5(`ScenarioOrUtility`)를 `ScenarioStage`로 채운다. 정의 파일은 로딩 때 **평면 배열로 컴파일**해서(문자열·사전 없음) 틱 경로를 할당 0으로 유지한다. 후보는 `(18존, 국면)` 색인과 트리거 이벤트 색인으로 미리 거르고(FR-11), 소유 변화·트리거 이벤트가 있거나 실행 중 시나리오가 없을 때만 평가한다.

실행 상태는 팀당 1개, 경기당 최대 2개의 `ScenarioRun`이다(공격·수비 동시 실행, FR-45). 단계 동사는 닫힌 enum이고, 실행 클래스 7종(이동 / 캐리 / 돌파 / 패스류 / 슈팅 / 수비 / 대기) 중 하나로 묶여 "이동 목표 + 행동 의도"를 낸다(FR-40). 시나리오는 결과를 정하지 않는다. 성패는 물리 결과를 관찰해서 받는다.

**단계 순서 문제와 해법**: FR-33·AC-08은 배역이 끝난 선수가 *같은 틱에* 형태 목표로 돌아가길 요구한다. 그런데 형태는 단계 2, 시나리오는 단계 5다. 종료 원인(소유 상실·아웃·휘슬·이탈·시간 초과·단계 결과)은 전부 직전 틱 물리·심판 또는 틱 번호로 정해지므로, **수명 주기(종료·분기·체이닝)를 단계 2 직전에 돌린다**. `ScenarioShapeStage`가 기존 `ShapeStage`를 감싸 `runtime.Advance(ctx)`를 먼저 부르고, `ScenarioStage`는 새 선택과 의도 생성만 한다. 이러면 `ShapeStage`를 고치지 않고 해제된 선수가 같은 틱에 슬롯 목표를 받는다.

**폴백 연결**: `SimpleDecisionStage`에 `IDecisionOverride`를 선택 인자로 추가한다. 배역 선수는 오버라이드 값을, 나머지는 기존 판단을 쓴다. 한 루프에서 처리하므로 `MarkDecided` id 오름차순(006 AC-04)이 유지된다. 오버라이드가 없으면 RNG 소비가 지금과 같아 기존 해시가 변하지 않는다.

검토한 대안: (1) 시나리오 단계를 단계 2 앞으로 통째로 옮기기 — 006 FR-01 순서를 깨고 단계 5의 보드 계약도 바뀌어 버렸다. (2) 해제 틱에 1틱 늦게 복귀 허용 — AC-08을 고쳐야 해서 버렸다. (3) YAML 파서 도입 — Core에 없고 `Packages/manifest.json` 변경이 필요하며 053 워크벤치도 JSON으로 쓸 수 있어 JSON으로 간다.

## 모듈 · 파일
모두 `RoadToPro.MatchEngine` (네임스페이스 `RoadToPro.MatchEngine.Scenarios`). 경로 접두어 `Assets/_Project/MatchEngine/Runtime/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| MatchEngine | Scenarios/Data/ScenarioSchema.cs | enum: 분류, 동사(FR-03), 실행 클래스, 공 상태, 블록, 트리거 이벤트, 종료 사유, 분기 결과 |
| MatchEngine | Scenarios/Data/ScenarioDefinition.cs | 컴파일된 평면 정의: 트리거·게이트·배역·단계·분기·태그 |
| MatchEngine | Scenarios/Data/ScenarioParser.cs | JSON → 정의. 두 표기(순서 `do`, 시각 `t`·`action`)를 하나로 (FR-02). 국면 표기 매핑 (AC-18) |
| MatchEngine | Scenarios/Data/ScenarioValidator.cs | 중복·없는 대상·없는 동사·순환·가중치·범위 (FR-04). 오류는 파일·줄·필드로 모아 보고 |
| MatchEngine | Scenarios/Data/ScenarioLibrary.cs | id 정렬 후 색인 생성, FN 공용 표, PP 수식자, `상태: 후보/폐기` 제외와 로딩 리포트 (FR-05·55, AC-15). 파일 읽기는 호출자 |
| MatchEngine | Scenarios/Data/ZoneMap.cs | 18존 키, 이름 있는 좌표 영역, 좌우 반전 |
| MatchEngine | Scenarios/ScenarioInputs.cs | 주입 인터페이스와 대체 구현 (아래 표) |
| MatchEngine | Scenarios/TriggerEvaluator.cs | 트리거 술어 평가, 트리거 이벤트 감지 (FR-10~11) |
| MatchEngine | Scenarios/RarityGate.cs | 경기당 횟수·상한·쿨다운 (FR-12~13, 경기마다 초기화) |
| MatchEngine | Scenarios/ScenarioSelector.cs | 점수 곱, 최근 사용 감쇠, softmax (FR-20~24) |
| MatchEngine | Scenarios/CastAssigner.cs | 역할·위치·거리 배정, 상한, 충돌 조정, 반전 역할 (FR-30~34) |
| MatchEngine | Scenarios/StepExecutor.cs | 동사 → `Decision` + `BallAction`, 목표 해석 (FR-40) |
| MatchEngine | Scenarios/ScenarioRuntime.cs | 실행 상태, `Advance`(종료·분기·체이닝), 시작, `IScenarioCastSource` 구현, 스냅샷·해시 (FR-41~46) |
| MatchEngine | Scenarios/ScenarioAggregator.cs | 시작·완주·폴백·소요 시간·사유 분포·역습 허용률 (FR-51·54) |
| MatchEngine | Scenarios/IScenarioDiagnostics.cs | FR-52. null이면 호출 0 |
| MatchEngine | Match/Stages/ScenarioStage.cs, ScenarioShapeStage.cs | 단계 5 본체, 단계 2 감싸개 |
| MatchEngine | Match/Stages/StandIn/SimpleDecisionStage.cs (수정) | `IDecisionOverride` 선택 인자. 추격자·압박자 계산에서 배역 제외 |
| MatchEngine | Match/MatchSim.cs, MatchParams.cs, Match/Result/MatchResult.cs (수정) | `MatchTuning.Scenario`, `MatchSimOptions`의 입력·강제·진단, 결과의 시나리오 id 표·집계 |
| MatchEngine | Match/Events/SimEventKind.cs (수정) | 601 `ScenarioCast`, 602 `ScenarioStepEnd`, 603 `ScenarioBranch`, 604 `ScenarioChain`, 605 `ScenarioEnd`, 606 `ScenarioForceRejected`, 607 `ScenarioResume`, 608 `ScenarioCastReleased`. 600 `ScenarioStart`는 기존 값 |
| Core | Core/Runtime/Serialization/JsonReader.cs (수정) | `JsonObject`에 멤버별 줄 조회 추가 (FR-04 "줄"). 기존 호출 동작 불변 |
| 데이터 | Data/scenarios/*.json | AC-01 6개 (기획서 예시, FT01·PR04·CT02, D-PR-01·S-CK-01) |
| 데이터 | Data/scenarios/_shared/regions.json, finishing.json, modifiers.json | 좌표 영역, FN 표 형식, PP 수식자 형식. 전체 내용은 014 |
| 데이터 | Data/tuning/scenario.json | 감쇠·상한·softmax·기본 태그·분류 사전확률 |
| 데이터 | Data/perf/013-scenario-runtime.json | 성능 예산 사본 |
| Verification | Verification/Runtime/Bench/ScenarioBenchWorkloads.cs (신규), SimBench.cs (수정) | `--sim-bench 013`: 틱 시간, 로딩, 1,000경기 집계 |
| Tests.EditMode | Tests/EditMode/MatchEngine/Scenarios/*Tests.cs, ScenarioTestKit.cs, Fixtures | 로직·결정론 |
| Tests.Performance | Tests/Performance/MatchEngine/ScenarioPerfTests.cs | Mono 회귀 기준선, 할당 0 |

의존 방향 확인: `.claude/rules/10-csharp.md` 위반 없음 (`MatchEngine → Core`, `Verification → MatchEngine`).

**기존 경기와의 연결**: `MatchTuning.Scenario`가 있고 두 팀에 전술이 있으면 013 단계를 쓴다. 없으면 지금 단계 그대로다. 006·009·054 테스트와 벤치의 해시가 변하지 않는다. 라이브러리가 0개면 전 구간이 폴백이다(AC-10).

### 대체 입력
| 인터페이스 | 자리 | 대체 구현 |
|---|---|---|
| `IScenarioSpaceQuery` | 007 | 기하 계산: 앞 공간(진행 방향 복도 안 최근접 상대), 뒤 공간(오프사이드 라인까지), 압박자 거리 |
| `IScenarioValueQuery` | 007 xT | x·중앙 근접도의 단순 함수 |
| `IScenarioTraitSource` | 003 | 기본 ×1. 테스트용 `TaggedTraitSource`는 슬롯별 태그가 선호 특성과 맞으면 배수 적용 |
| `IScenarioRoleSource` | 003 | 009 포메이션 슬롯의 `pos`(GK·CB·FB·DM·CM·AM·W·ST)와 좌우 위치로 역할 유도 |
| `IScenarioJudgement` | 002 Decisions | 0.5 고정 |
| 단계 행동 | 008·011 | `StepExecutor`가 `BallInverse`로 패스·크로스·슈팅을 만들고 물리 결과를 관찰한다 |
| 폴백 | 010 | `SimpleDecisionStage` |
| 재시작 | 012 | `MatchFlow`의 Restart·ReadyTick |

대체 구현에 없는 트리거 이벤트(나쁜 터치 등)는 발생하지 않는다. 테스트는 이벤트 주입으로 검증한다.

## 데이터 · 스키마
```json
{"id":"FT01","name":{"ko":"오버랩→크로스","en":"Overlap & Cross"},"category":"FT",
 "status":"active",
 "trigger":{"zones":{"regions":["T3"],"lanes5":["W-L","W-R"]},"carrierRole":["W","IW"],
            "space":{"fbFrontSpaceM":">=8"},"oppBlock":["MB","LB"]},
 "gate":{"lambda":1.0,"maxPerMatch":3,"cooldownS":30},
 "cast":[{"role":"W","required":true,"steps":[
           {"do":"carry","dir":"inside"},{"do":"pass_short","to":"FB"}]},
         {"role":"FB","required":true,"steps":[
           {"do":"overlap"},{"do":"receive"},{"do":"cross_driven_low","alt":"cross_lofted"}]}],
 "branches":[{"at":"W.1","success":"next","fail":{"goto":"fallback"},
              "alt":[{"if":"fb_marker_follows_overlap","goto":"FT02"}]},
             {"at":"FB.3","success":{"goto":["FN03","FN04","FN05"],"weights":[0.4,0.35,0.25]},"fail":{"goto":"FT15"}}],
 "kpi":["cross_into_box"],"favoredTraits":{"FB":["crossing","pace"]},
 "variationAxes":["side","cross_type"],"tags":{"successRate":0.3,"xg":0.08,"durationS":8}}
```
- 시각 표기는 단계에 `"t":1.2`, 분기에 `"when":"regain","goto":"T-CA-01"`로 쓴다. 파서가 같은 구조로 합친다.
- `CT_opp`처럼 상대 전환을 뜻하는 카탈로그 대상은 종료 사유 소유 상실로 처리한다(새 시나리오는 상대 팀 선택이 고른다).
- 세이브 스키마 변경 없음. 실행 상태는 006 스냅샷에만 들어간다.
- 이벤트 `ScenarioId`는 경기 안 표의 번호이고, `MatchResult.ScenarioIds`에 id 문자열 표를 둔다. 라이브러리가 바뀌어도 리플레이 표기가 유지된다. `A`는 단계, `B`는 분기 결과·사유, 변주 id는 표에 그대로 들어간다.

### 초기값 제안 — 승인 대상
`Data/tuning/scenario.json`의 `[설계]` 값이고 017로 보정한다.

| 항목 | 값 | 근거 |
|---|---|---|
| τ_max / τ_min | 0.35 / 0.05 | 010 FR-20. 점수는 후보 최댓값으로 나눈 뒤 softmax |
| 분류 사전확률 | 오픈플레이 0.60 · 세트피스 0.25 · 페널티 0.08 · 역습 0.07 | FR-24 |
| 기본 태그 (014 전) | 성공률 0.3, xG 0.05, 소요 8 s | 시나리오 지속 4~10 s (research[1] § 5). 나머지는 설계 |
| 기대 가치 | `max(xG 태그, 도착 지점 xT − 현재 xT)` | 설계 |
| 수식자 배수 범위 | 0.5~1.5 | 설계 |
| 도착 판정 | 목표 1.5 m 이내 | 설계 |
| 패스 성공 판정 | 지정 수신자 또는 같은 팀이 공을 소유 | 설계 |
| HL 판정 | 상대 오프사이드 라인이 상대 관점 x ≥ 0.60 | 009 High 블록 범위 근처 |
| 역습 판정 (FR-54) | 시나리오 종료 후 10 s 안에 상대 슈팅 | 설계 |

상대 블록 매핑: HP = `DefHigh`, MB = `DefMid`, LB = `DefLow`, UNB = `DefTransition`, HL = 위 라인 조건(다른 블록과 겹칠 수 있음).

좌표 영역 (공격 팀 관점 미터, IFAB 치수): 5레인 경계 y = 13.84 / 24.84 / 43.16 / 54.16, T1~T3 = 길이 3등분, Z14 = 골라인에서 16.5~34 m · C 레인, 컷백존 = 골라인에서 5~6 m · 포스트 바깥 · 박스 안. 18존 열은 자기 골대 쪽부터 1~6, 레인은 L/C/R이다.

### 스펙 해석 — 승인 대상
1. **"그 결정 틱에 종료"(AC-08·09)는 원인을 관찰할 수 있는 첫 결정 틱이다.** 소유 상실은 틱 N의 물리에서 일어나므로 틱 N+1 단계 2 직전에 종료된다.
2. **상대 팀에게 주는 배역의 기본값은 선택 배역이다.** `required`를 쓰지 않으면 자기 팀 배역은 필수, 상대 배역은 선택이다. FR-32로 뺏겨도 시나리오가 깨지지 않게 하기 위함이다.
3. **1,000경기 AC(07·10·17·21)는 IL2CPP 벤치에서 경기 단위 스레드 병렬로 마지막에 1회 돈다** (006 FR-71 허용, 약 5분 예상). EditMode는 20경기로 같은 불변식을 확인한다.
4. **4만 개 로딩(성능 예산)은 합성 데이터로 잰다.** 014가 아직 없어서 6개 샘플의 변형을 생성해 측정한다.
5. **라이브러리 내용은 AC-01의 6개와 픽스처뿐이다.** FN 12개·PP 11개와 268개 패턴 작성은 014 몫이고, 여기서는 형식과 참조만 구현한다.
6. **`T3` 등 공격 문서 깊이와 18존 열 번호는 소유 팀 관점이다.** 수비 시나리오의 `col`은 수비 팀 자기 관점(자기 골대 쪽 1열)으로 읽는다.

## 결정론 · 성능
- RNG: 기존 `ScenarioSelect` 스트림(5번) 하나를 런타임이 소유한다. 선택 softmax, 가중 분기 순서로 뽑고 팀 0 → 1 순서로 처리한다. 대체 슈팅·패스의 무작위도 이 스트림을 쓴다. 진단 on/off와 무관(AC-19).
- 순회: 라이브러리는 id 서수 정렬이고 파일 순서를 쓰지 않는다(AC-15). 사전 순회 없음. 동점은 서수 순.
- 평가 빈도: 후보 평가는 소유 변화·트리거 이벤트·해당 팀 실행 없음일 때만. 실행 없음이 계속되면 평가 비용이 매 틱 들어가므로 재평가를 [설계] 5틱(0.5 s) 주기로 제한한다. 소유 변화·트리거 이벤트 틱은 즉시 평가한다.
- 할당 0: 후보·배역·이벤트 버퍼는 사전 할당, 조건식은 enum + 숫자로 컴파일. 틱 경로에 LINQ·람다·boxing·문자열 없음.
- 스냅샷: `ScenarioStage`의 `WriteState/ReadState/AddTo`에 실행 상태·게이트·감쇠·RNG를 넣는다. 왕복 테스트로 누락을 잡는다.
- 측정: EditMode는 회귀 기준선, 판정은 `farm.py bench --spec 013`(IL2CPP, D17).

## 테스트 전략
| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `ScenarioParserTests.SixDocumentSamples_FieldsMatch` | 로직 | — |
| AC-02 | `ScenarioValidatorTests.Errors_FileLineField`, `ValidFilesStillLoad` | 로직 | — |
| AC-03 | `TriggerEvaluatorTests.Matches_AndEachMismatchRejected` | 로직 | — |
| AC-04 | `RarityGateTests.MaxPerMatch`, `Cooldown` | 로직 | — |
| AC-05 | `ScenarioSelectorTests.TraitBias_1000Seeds` (이항 검정 p < 0.01) | 로직 | — |
| AC-06 | `ScenarioSelectorTests.RecentUseDecay_AndRecovery` | 로직 | — |
| AC-07 | `CastAssignerTests.Caps_NoDoubleCast`, `ScenarioMassTests.Caps_20Matches` + `--sim-bench 013` 1,000경기 | 로직 | mac-m1-1600p |
| AC-08 | `ScenarioRuntimeTests.CastReleased_SameTickShapeTarget`, `NoTeleportOnBreak` | 로직 | — |
| AC-09 | `ScenarioRuntimeTests.TackleBreak_EndsAndOpponentActs` | 로직 | — |
| AC-10 | `ScenarioMassTests.EmptyLibrary_20Matches` + `--sim-bench 013` 1,000경기 | 로직 | mac-m1-1600p |
| AC-11 | `ScenarioRuntimeTests.RequiredCastLost_Ends`, `OptionalCastLost_Continues` | 로직 | — |
| AC-12 | `ScenarioRuntimeTests.ChainLimit_Fallback` | 로직 | — |
| AC-13 | `ScenarioMirrorTests.CastTargets_Symmetric` | 로직 | — |
| AC-14 | `ScenarioSetPieceTests.CornerSequence_LogOrder_NonCastUntouched` | 로직 | — |
| AC-15 | `ScenarioDeterminismTests.SameSeed_SameLogHash`, `FileOrderShuffled_SameHash`, `Snapshot_RoundTrip` | 결정론 | — |
| AC-16 | `ScenarioPerfTests.*` + `farm.py bench --spec 013` | 성능 | mac-m1-1600p |
| AC-17 | `ScenarioMassTests.Aggregate_MatchesEventLog_20Matches` + `--sim-bench 013` 1,000경기 | 로직 | mac-m1-1600p |
| AC-18 | `ScenarioParserTests.PhaseNotation_Mapped`, `UnknownPhase_Error` | 로직 | — |
| AC-19 | `ScenarioDeterminismTests.DiagnosticsOnOff_SameHashes`, `ScenarioPerfTests.Tick_NoAlloc_DiagnosticsOff` | 결정론 | — |
| AC-20 | `ScenarioForceTests.Marked_ReleaseRejected_MissingCastReason` | 로직 | — |
| AC-21 | `ScenarioMassTests.FallbackReasons_SumEqual_20Matches` + `--sim-bench 013` 1,000경기 | 로직 | mac-m1-1600p |
| AC-22 | `ScenarioLibraryTests.CandidateStatus_ExcludedAndReported` | 로직 | — |

추가: `SetupWiringTests`(라이브러리 없으면 기존 단계·기존 해시), `FallbackOverrideTests`(오버라이드 없으면 기존과 같은 RNG 소비).

E2E는 스펙대로 없다. 시각 검증은 040·053이 맡는다.

## 버그 방지
- 같은 틱 소유 상실 + 슈팅: 006 이벤트 순서대로 하나만 반영 → `ScenarioRuntimeTests.LossAndShotSameTick_OneOutcome`.
- 하프타임·종료 휘슬 중 실행: 즉시 종료, 다음 반에 이어지지 않음. 하프 전환 뒤 좌표는 관점 변환으로만 계산 → `HalfTimeEnds_NoCarryOver`.
- 배역 틱에 퇴장·교체: 슬롯 비활성 검사를 시작·전진 양쪽에서 → AC-11 테스트.
- 10명 이하: 배역 수요가 인원을 넘으면 후보 제외 → `CastAssignerTests.ShortHanded_Excluded`.
- 분기 대상이 로드 실패: 폴백으로 대체 + 경고 → `ScenarioLibraryTests.MissingBranchTarget_FallsBack`.
- 게이트 상한 0, 점수 합 0, 전부 NaN: 후보 없음 처리 → `RarityGateTests.ZeroCap`, `ScenarioSelectorTests.ZeroScores_Fallback`.
- 공 동작 틱당 1개: `hasAction`을 오버라이드와 폴백이 공유 → `FallbackOverrideTests.OneBallActionPerTick`.
- 이벤트 로그 용량(16 K) 초과: 배역 이벤트가 늘어난다. 1경기 이벤트 수를 재고 넘으면 용량을 조정 → `ScenarioMassTests.EventLogNotOverflowed`.
- 경기 간 누수: 게이트·감쇠는 인스턴스 필드 → `TwoMatches_NoLeak`.

## 위험 · 미정
- 평균 3 µs(IL2CPP)는 빠듯하다. 009도 2.43 µs였다. 넘으면 멈추고 예산 수정을 제안한다.
- 4만 개 메모리 64 MB는 합성 측정 113.4 MB로 초과했다 (로딩 시간 0.9 s는 예산 안). 2026-11-09 사용자 결정으로 014의 "기본 패턴 + 변주 묶음" 저장 구조에서 판정한다.
- 대체 입력을 쓰는 동안 AC-05·09 등은 구조만 검증한다. 시나리오 성공률과 득점 영향은 003·007·008·010 연결 뒤 017에서 본다.
- 스케줄러 교체(010): 배역 선수는 항상 결정 대상에 넣어야 한다. 지금은 매 틱 전원이라 문제없지만 010 연결 때 재확인한다.
- `SimpleDecisionStage`의 압박 지정 미소비(009 잔여)는 이 스펙에서 고치지 않는다.
- 대체 판정 구현(`StepExecutor`·기하 공간 등)의 상수 일부는 코드에 있다. 007·008 연결 때 대체 구현과 함께 사라지므로 튜닝 파일로 옮기지 않았다 (determinism-reviewer R11).

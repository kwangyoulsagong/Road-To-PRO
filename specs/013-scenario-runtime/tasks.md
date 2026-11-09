# 013 — 시나리오 런타임 · 태스크

상태: 승인됨
플랜: ./plan.md

규칙: 위에서부터 순서대로. Unity 배치 1회가 길어서, 테스트를 먼저 쓰는 태스크는 같은 블록의 구현과 한 배치로 돌린다. 중간 검증은 `.claude/scripts/unity-test.sh EditMode <클래스 필터>`로 좁힌다. `bench`·EditMode 전체·`/verify`는 마지막 태스크에서 각 1회.

플랜 보충 (구현 전 발견, 승인 범위 안의 해석):
- 카탈로그 id 목록(`Data/scenarios/_shared/catalog-ids.json`, `docs/scenarios.html`에서 추출): 분기 대상이 목록에 있지만 로드되지 않았으면 경고 + 폴백(스펙 경계), 목록에도 없으면 로딩 오류(AC-02). AC-01 샘플이 아직 없는 카탈로그 id를 참조하기 때문이다.
- 순환 체이닝(FR-04)은 "분기만으로 무한 루프"다: 단계 실행 없는 `when: always` 분기로만 이어진 순환을 로딩 오류로 한다. 단계 결과를 거치는 A→B→A는 런타임 상한(AC-12)이 끊는다.
- 세트피스 재개 표시용 이벤트 607 `ScenarioResume`을 추가한다 (AC-14 순서 검증).

## A. 데이터
- [x] T01 JSON 줄 조회 — 파일: `Core/Runtime/Serialization/JsonReader.cs` — 검증: `ScenarioValidatorTests`의 줄 번호, 기존 튜닝 로드 회귀 — 결과: `JsonObject.Line`·`MemberLines` 추가, 줄 번호 테스트 통과
- [x] T02 스키마·정의·구역 — 파일: `Scenarios/Data/{ScenarioSchema,ScenarioDefinition,ZoneMap}.cs`, `Data/scenarios/_shared/regions.json` — 검증: 컴파일 — 결과: 동사 57개, 18존·세부 구역 21개. `regions.json`은 스크래치 생성 후 결과만 둠
- [x] T03 파서·검증·라이브러리 — 파일: `Scenarios/Data/{ScenarioParser,ScenarioValidator,ScenarioLibrary}.cs`, `Scenarios/Data/ScenarioParams.cs`, `Data/tuning/scenario.json`, `Data/scenarios/_shared/{catalog-ids,finishing,modifiers}.json` — 검증: T05 — 결과: 카탈로그 id 271개 추출. FN은 `finishing.json`(형식 샘플 4개)
- [x] T04 AC-01 샘플 6개 — 파일: `Data/scenarios/*.json` — 검증: T05 — 결과: 6개 파일 오류 0
- [x] T05 데이터 테스트 — 파일: `Tests/EditMode/MatchEngine/Scenarios/{ScenarioTestKit,ScenarioParserTests,ScenarioValidatorTests,ScenarioLibraryTests}.cs` — 검증: AC-01·02·18·22 — 결과: `ScenarioDataTests.cs` 한 파일에 묶음, 통과

## B. 트리거 · 선택 · 배역
- [x] T06 입력·트리거·게이트 — 파일: `Scenarios/{ScenarioInputs,TriggerEvaluator,RarityGate}.cs` — 검증: T09 — 결과: 통과
- [x] T07 선택 — 파일: `Scenarios/ScenarioSelector.cs` — 검증: T09 — 결과: 통과
- [x] T08 배역 — 파일: `Scenarios/CastAssigner.cs` — 검증: T09 — 결과: 통과
- [x] T09 선택 계층 테스트 — 파일: `Tests/.../Scenarios/{TriggerEvaluatorTests,RarityGateTests,ScenarioSelectorTests,CastAssignerTests}.cs` — 검증: AC-03·04·05·06, AC-07 일부 — 결과: `ScenarioSelectionTests.cs`에 묶음, 통과

## C. 실행
- [x] T10 단계 실행·런타임·집계·진단 — 파일: `Scenarios/{StepExecutor,ScenarioRuntime,ScenarioAggregator,IScenarioDiagnostics}.cs` — 검증: T12 — 결과: 통과. 필수 상대 배역을 자기 팀이 뺏으면 상대 시나리오를 끝내도록 수정(테스트로 발견)
- [x] T11 파이프라인 연결 — 파일: `Match/Stages/{ScenarioStage,ScenarioShapeStage}.cs`, `Match/Stages/StandIn/SimpleDecisionStage.cs`, `Match/{MatchSim,MatchParams}.cs`, `Match/Result/MatchResult.cs`, `Match/Events/SimEventKind.cs` — 검증: T12, 기존 `Match`·`Tactics` 테스트 회귀 — 결과: 통과
- [x] T12 실행 테스트 — 파일: `Tests/.../Scenarios/{ScenarioRuntimeTests,ScenarioMirrorTests,ScenarioSetPieceTests,ScenarioForceTests,FallbackOverrideTests,SetupWiringTests,ScenarioMassTests}.cs` — 검증: AC-08~14·17·20·21 (20경기), AC-07·10 — 결과: `ScenarioRuntimeTests.cs`에 묶음. 합계 71개 통과. 교체는 데드볼에만 실행되어 AC-11 선택 배역은 세트피스로 검증
- [x] T13 결정론 테스트 — 파일: `Tests/.../Scenarios/ScenarioDeterminismTests.cs` — 검증: AC-15·19, 스냅샷 왕복 — 결과: 통과 (스냅샷 왕복·파일 순서·진단 on/off). 하네스가 메서드 단위 카테고리만 읽어 `[Category("Determinism")]`를 메서드에 붙임
- [x] T14 결정론 리뷰 — `determinism-reviewer`로 `Scenarios/**`, 변경된 `Match/**` 리뷰, 지적 반영 — 검증: 차단 0 — 결과: 차단 1건 반영(B1: 배역 중 역할 캐시가 스냅샷에 없어 복원 뒤 이벤트 로그가 갈림, 스냅샷 4000에서 재현 후 수정). 권고 반영: 라이브러리 해시에 카탈로그·종류 구분(R1), Fork 진단 끔(R2), 상황 분기 next 로딩 오류(R3), 다른 배역 점프 재발화(R4), 순환 판정 방문 순서 무관(R5), 배역 해제 이벤트 608(R6), 진단 배열 복사(R7), 시각 정수 비교(R8), 입력 순수성 계약 명시(R9), 결과 id 표 복사(R10), `CT_opp`는 `PossessionLost`. R11(대체 구현 상수)은 007·008 연결 때 제거로 플랜에 기록. 합계 77개 통과

## D. 성능
- [x] T15 성능 테스트·벤치·예산 — 파일: `Tests/Performance/MatchEngine/ScenarioPerfTests.cs`, `Verification/Runtime/Bench/{ScenarioBenchWorkloads,SimBench}.cs`, `Data/perf/013-scenario-runtime.json` — 검증: Mono 측정·틱당 할당 0 — 결과: Mono 평균 1.0 µs·p95 2.7 µs·약 270개 로딩 8.4 ms·할당 0회. `IScenarioTimer` 추가(측정 전용). IL2CPP 판정은 T17

## E. 마무리
- [x] T16 전체 테스트 — EditMode 전체 실패 0, 콘솔 에러·경고 0, 컴파일 경고 신규 0 (이 스펙의 1회) — 결과: 커밋 전 트리에서 1083개 중 통과 1078·실패 2·건너뜀 3(000 셀프테스트 전용). 실패 2건은 새 이벤트의 애니메이션 매핑 누락이라 `043-anim-map.json`에 8종 추가 후 `AnimationMapTests` 10개 통과. 컴파일 경고 0
- [ ] T17 `/verify 013` — `farm.py logic --spec 013` + `farm.py bench --spec 013`(IL2CPP 판정, 1,000경기 AC 포함) 리포트, 성능 기준 채택은 사용자 승인, 리포트 아티팩트 발행, 상태·README 갱신 — 진행: IL2CPP 벤치 1회 실행(판정 제외: 배터리 전원). 14개 중 13개 예산 안, **4만 개 메모리 113.4 MB > 64 MB 초과**로 사용자 결정 대기

# 072 — 온볼 판단: 후보 · 점수 · 선택 · 지시 · 다이빙 · 태스크

상태: 승인됨
플랜: ./plan.md

규칙: 위에서부터 순서대로. 한 태스크 = 한 커밋 크기. 테스트를 먼저 쓰는 태스크는 같은 블록의 구현과 한 Unity 배치로 돌린다(실패 확인 → 구현 → 통과).
중간 검증은 `.claude/scripts/unity-test.sh EditMode <클래스 필터>`로 좁힌다. `farm.py logic --scope auto`·`bench`·`/verify`는 마지막 태스크에서 각 1회 (`.claude/rules/05-sdd.md` "검증 범위").

경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `D/` = `ME/UtilityAI/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/UtilityAI/`.

## A. 기반
- [x] T01 튜닝·이벤트·항목 — 파일: `Data/tuning/decision.json`(`dec.onball.*`), `D/{DecisionParams,DecisionTuningIds,OnBallItem}.cs`, `ME/Match/Events/SimEventKind.cs`(901·902, Dribble 성공 비트), `Presentation/Players/Data/043-anim-map.json` — 검증: 컴파일, 튜닝 로드·파일=코드 기본값 테스트, `AnimationMapTests` — 결과: `dec.onball.*` 61개([제안] 11·[대체] 50, 정의 206개), 이벤트 `InstructionIgnored = 901`·`DiveAttempt = 902`, `OnBallItem`(9종 + Clear·Dive)·`ConsiderationSet`. 파일=코드 기본값·`AnimationMapTests` 통과
- [x] T02 대체 입력 — 파일: `ME/Actions/TechnicalAttributes.cs`(Dribbling·Finishing·Crossing·LongShots), `D/{IChemistrySource,StandInChemistry,IOnBallEvaluator,StandInOnBallEval}.cs`, `D/{IStyleMultiplierSource,StandInStyleSource}.cs`(개인 지시·`MultiplierFullCompliance`), `D/UtilityChooser.cs`(상한 24), `T/OnBallEvalTests.cs` — 검증: 대체 확률 단조성(능력치·거리·압박), 오프사이드 위험(FR-15), 개인 지시 곱, 008 기존 테스트 해시 불변 — 결과: `OnBallEvalTests` 8 + `StyleMultiplierTests`·`UtilityChooserTests`·008 `Actions` 필터 합계 102/102. 플랜과 다른 점: 기술 능력치 대체에 수비 1:1 판정용 `Tackling`도 추가(5종), `DecisionWorld`에 `Tech`·팀 지시 4종·득실차·남은 시간 추가, 지시 억제 판정용 `InstructionWeight(slot, k)`도 인터페이스에 추가

## B. 시야 · 후보 · 점수 (FR-01~04·10~15·20)
- [x] T03 테스트 — 파일: `T/OnBallFixture.cs`, `T/OnBallVisionTests.cs`, `T/OnBallScoringTests.cs`, `T/OnBallCandidateTests.cs` — 검증: AC-01·02·03·08, 후보 상한 24·폴백·경계 드리블·오프사이드 유지·동료 감소·동점 순서 — 실패 확인 — 결과: `OnBallVisionTests` 6·`OnBallScoringTests` 6·`OnBallCandidateTests` 11 (T04와 한 배치). 첫 실행 실패 2건은 테스트 장면 오류(홀드 이득 범위 하한 0 → −0.2로 튜닝 범위 수정, 오프사이드 동료가 시야 밖이던 장면)
- [x] T04 구현 — 파일: `D/{VisionSet,OnBallCandidate,OnBallCandidates,OnBallScorer}.cs` — 검증: T03 통과 — 결과: 통과. 패스는 대상당 008 최선 속도 1개(같은 조준점이면 다른 속도는 xPass로 지배됨) + 스루볼 1개 + 크로스 1개. 드리블 8방향은 원뿔(45°, 4 m) 안 상대가 있으면 돌파(Dribble), 없으면 캐리(Carry). 대체 xT(0~1)는 `xt.scale` 0.3으로 xG 척도에 맞춤 [대체]. FR-03 지점 질의: 008 xPass가 수신 지점 컨트롤을 포함해 따로 질의하지 않음(0회)

## C. 결정자 · 지시 무시 · 의도 · 다이빙 (FR-14·20·30~32)
- [x] T05 테스트 — 파일: `T/OnBallDeciderTests.cs`, `T/InstructionCheckTests.cs` — 검증: AC-04(온볼·오프볼), AC-05(고정 픽스처 1,000회 버릇 5종 방향), AC-06, AC-07, AC-09 — 실패 확인 — 결과: `OnBallDeciderTests` 10·`InstructionCheckTests` 5. 1,000회 수치: 드리블 중독 드리블 14 → 36·패스 965 → 939, 크로스 머신 469 → 879, 중거리 난사 641 → 840, 이타적 슈팅 771 → 285·이기주의 865, Dribbling 1 vs 20 선택 12 vs 28·성공 3 vs 25, 컷백 의도 X 패스 92 → 611, 다이버 넘어지기 158 → 426, 지시 무시(준수 0.2) 899/1000·준수 1 → 0, 오프볼 침투 자제 무시 120·준수 1 → 0
- [x] T06 구현 — 파일: `D/{InstructionCheck,OnBallDecider}.cs`, `D/OffBallDecider.cs`(지시 무시 판정 연결), `D/ScenarioIntentAdapter.cs`·`ME/Scenarios/ScenarioRuntime.cs`(배역 소유자 의도 패킷) — 검증: T05 통과, `IntentTests`·013 `Scenario*` 필터(기대값 바뀌면 목록 → 사용자 보고) — 결과: OnBall·지시·오프볼·의도·Scenario 필터(Slow 제외) 163/163, 013 기대값 변경 없음. 플랜과 다른 점: ① `InstructionCheck` 별도 파일 대신 두 결정자 안에 같은 판정(준수율 1 배수로 다시 낸 점수의 최고 후보 ≠ 선택, 준수율 효과가 없으면 무시 아님 — AC-04 '준수율 1 → 0'을 지키려고) ② 배역 소유자 의도: 캐리·돌파 단계는 단계 목표점 방향 후보 1개를 더하고 일치도는 목표점 근접도(010 FR-92 식) ③ `ScenarioRuntime.NotifyOnBallKick`: 072가 의도와 맞는 패스·슈팅을 내면 실행기가 찼을 때처럼 킥 대기로 둔다(단계 성공 판정 유지)

## D. 단계 연결 (실행)
- [ ] T07 `UtilityDecisionStage` 소유자 분기 — 파일: `ME/Match/Stages/UtilityDecisionStage.cs`, `ME/Match/Stages/StandIn/StandInOnBall.cs`(슛·걷어내기 생성 공개), `T/OnBallStageTests.cs` — 검증: 패스·스루·크로스(뜬 패스)·슈팅·걷어내기·드리블 1:1 판정·홀드·다이브 이벤트 실행, 데드볼 재시작은 대체 경로, 공중볼 온볼 없음, 드리블 실패 틱 태클 중복 없음, 스냅샷 왕복(FormatVersion 5), `SimpleDecisionStage` 해시 불변, Match·Scenario·Goalkeeper·DeadBall 필터 회귀

## E. 텔레메트리 · 헤드리스 · 성능
- [ ] T08 텔레메트리·진단 — 파일: `D/{DecisionTelemetry,IDecisionDiagnostics}.cs`, `T/OnBallDiagnosticsTests.cs` — 검증: FR-40 항목별 시도·지시 무시 집계, 시야 부채꼴·점수 막대 데이터, 꺼짐 0 B
- [ ] T09 헤드리스 경기 테스트 — 파일: `T/OnBallMatchTests.cs` — 검증: AC-11(90분 해시 2회 동일·진단 켬/끔 동일), 100경기 측정값(득점·패스 성공·드리블·슈팅·크로스·소유 지속·지시 무시) 리포트용 기록
- [ ] T10 성능 — 파일: `Assets/_Project/Tests/Performance/OnBallPerfTests.cs`, Verification 벤치 `OnBallBenchWorkloads`(`--spec 072`), `Data/perf/072-onball-decisions.json` — 검증: AC-12 Mono 기준선·GC 0 B(필터 실행). IL2CPP 판정은 T11

## F. 마감
- [ ] T11 검증·리포트 — `determinism-reviewer` 리뷰 → 커밋 → `python3 Tools/devicefarm/farm.py logic --spec 072 --scope auto` → `python3 Tools/devicefarm/farm.py bench --spec 072`(틱 경로 변경) → (058 하이라이트 결과가 바뀌었으면) `farm.py shot --scenario <058 장면>` 육안 승인 → `/verify 072` 리포트 아티팩트 발행 → 상태 갱신(AC-05 헤드리스·AC-10·AC-13 보류 표시)

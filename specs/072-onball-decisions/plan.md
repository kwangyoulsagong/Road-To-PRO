# 072 — 온볼 판단: 후보 · 점수 · 선택 · 지시 · 다이빙 · 플랜

상태: 승인됨
스펙: ./spec.md

## 접근
지금 공 소유자는 `StandInOnBall.Decide`의 대체 경로가 정한다: 보유 시간(`HoldTicks`)이 지나면 슈팅 사거리 안에서 `ShotProb` 굴림 → 아니면 008 평가로 고른 최고 점수 패스(`Pass`, 후보당 노이즈 1회) → 아니면 상대 박스 앞까지 캐리. 배역 소유자는 `ScenarioRuntime.TryDecide`(StepExecutor)가 공 동작을 직접 정한다. 드리블·크로스·홀드·걷어내기·다이빙 후보는 없다.

이 플랜은 `MatchEngine/Runtime/UtilityAI/`에 **온볼 결정자 `OnBallDecider`**를 더하고, 010 단계 5(`UtilityDecisionStage`)의 "공 소유자" 분기를 대체 경로 대신 이 결정자로 보낸다(인플레이·외부 입력 없음일 때만. 데드볼 재시작 킥·골 후·PK는 012 자리라 `StandInOnBall` 그대로). 파이프라인은 010 공통 틀을 그대로 따른다:
**시야 집합(FR-02) → 후보 생성(FR-01·04, 상한 24) → 기본 점수 = 기대 가치 × 성공 확률 − 상실 위험 × 상실 지점 상대 xT(FR-10, 지시 Tempo·Directness·Risk·TimeWasting 보정 FR-11) → 결합 배수(이득 항에만, 003 대체) × 케미(패스만, FR-12) → 평가 노이즈(010 `EvalNoise`) → `UtilityChooser`(정규화 → 의도 보너스 FR-14 → θ_opp → softmax) → 지시 무시 판정(FR-30~32) → 실행(`BallAction` 또는 `Decision.Carry`)**.

성공 확률 공급자는 인터페이스 뒤에 둔다: 패스 = 008 `ActionSystem.EvaluatePass/EvaluateThrough`(실제), 드리블·슈팅·크로스 = **070·071 대체 평가**(`StandInOnBallEval`: 로지스틱 대체식 `[대체]`), ΔxT·상실 지점 xT = 010 `ISpaceEvaluator.Threat`(007 대체), 케미 = `IChemistrySource`(033 대체, 기본 0), 다이빙 판정 = 이벤트만 내고 012 대체 심판은 무시(판정은 012). 연결 때 AC-13으로 재확인.

대안: `StandInOnBall.Pass`에 드리블·슈팅 분기를 덧붙이기 → 시야·결합 배수·의도·지시 무시 판정을 010 선택기와 같은 경로로 설명할 수 없어(점수 분해 NFR) 버렸다.

### 플랜이 스펙에 더하는 대체 (사용자 확인 필요 — 승인 질문에 포함)
1. **드리블 성공 판정 (070 대체)**: AC-06 "성공률도 높다"를 재려면 드리블 결과가 시뮬에 있어야 하는데 070이 없다. 드리블 후보가 선택되면 같은 결정 틱에 **대체 1:1 판정 1회**(결정 RNG, 성공 확률 = 위 대체식)를 굴려 실패면 가장 가까운 수비수가 공을 뺏고(`BallCommands.TakeControl` + `Dribble` 이벤트 실패 플래그), 성공이면 그 수비수의 대체 태클을 1 s 막고 캐리를 이어 간다. 070이 구현되면 통째로 교체된다.
2. **크로스 실행**: `BallActionKind`에 크로스가 없다. 크로스 후보는 박스 안 동료(또는 지점)로 가는 **뜬 패스**(008 `PassModel.LoftMode.LoftedOnly`)로 실행하고 이벤트 `Cross`를 낸다. 크로스 고유 궤적·헤더는 071.
3. **배역 공 소유자도 072가 결정**: 지금은 StepExecutor가 공 동작을 직접 고른다. 스펙 FR-14대로 의도 패킷(다음 패스 대상·단계 행동) 보너스로만 들어가게 바꾸면 013·058 시나리오 성공률과 E2E 고정 장면이 달라질 수 있다. 고정 장면은 배역 Teamwork 20·높은 w_intent 픽스처로 유지(010과 같은 방식), 기대값이 바뀌는 013 테스트는 목록으로 보고한다.
4. **기술 능력치 대체 (002 전)**: `TechnicalAttributes`(008)에 Dribbling·Finishing·Crossing·LongShots 선택 인자를 더한다(기본 50, 기존 생성자·`Average` 호환).

## 모듈 · 파일
경로 접두: `ME/` = `Assets/_Project/MatchEngine/Runtime/`, `D/` = `ME/UtilityAI/`, `T/` = `Assets/_Project/Tests/EditMode/MatchEngine/UtilityAI/`, `P/` = `Assets/_Project/Tests/Performance/`.

| 모듈(asmdef) | 파일 | 역할 |
|---|---|---|
| RoadToPro.MatchEngine | `D/OnBallItem.cs` | 온볼 고려 항목 9종(ShortPass·ProgPass·ThroughBall·Switch·Cross·Carry·Dribble·Shoot·Hold) + 폴백 Clear·Dive (텔레메트리·데이터 번호 고정). 항목 → 결합 배수 질의 `Consideration` 대응(예: ShortPass·ProgPass·Switch → Pass, 원터치면 OneTouchPass 곱, 동료가 박스 안 슈팅 위치면 AssistPass 곱, Dribble 컷인 → CutIn 곱, 먼 슈팅 → LongShot, Hold → HoldBall, Dive → DiveForFoul) |
| 〃 | `D/OnBallCandidate.cs` | 후보 구조체: 항목·대상 슬롯·목표 지점·속도·뜬공 여부·리드·008 `PassOption`·성공 확률·기대 가치·상실 위험·상실 지점 xT·결합 배수·케미·의도 일치도·점수 분해 |
| 〃 | `D/VisionSet.cs` | FR-02: r = 20 + 30s, k = round(3 + 5s) (s = Vision/100). 반경 안 동료를 거리순으로 k명(동점 = 슬롯 순). 시야각 밖 동료는 지각 지연(`dec.onball.vision.delay` [제안]) 전 위치(현재 위치 − 속도 × 지연)로 평가. 시야각 매핑 `[미정]` → `[대체]` 시작값(Vision 1 = 120°, 20 = 220°, 몸 방향 기준). 할당 0(고정 배열 10) |
| 〃 | `D/OnBallCandidates.cs` | FR-01·03·04: 시야 집합 동료 × 패스(땅볼 4속도 + 뜬공 2 — 008 `PassModel.Best`가 속도별 최선 1개, 스루볼은 리드 4종 중 최선 1개) → 항목 분류(거리·전진량·횡 이동량 기준 ShortPass/ProgPass/Switch, 리드 > 0 → ThroughBall), 드리블 8방향(진행 방향 기준 컷인/바깥 구분, 경계 밖·동료 몸 겹침 제외), 슈팅(사거리 `[대체]` 안), 크로스(측면 구역 `[대체]` + 박스 안 동료 1명 이상), 홀드, 캐리(전방 열린 공간). 상한 24 — 넘으면 사전 점수(기대 가치 × 008 로지스틱 xPass)로 자름. 피치 컨트롤 질의(`ISpaceEvaluator.Control`)는 사전 점수 상위 후보에만, 010 질의 카운터와 합산 상한(FR-03). 후보 0 → 홀드(압박 반경 밖) 또는 걷어내기 강제 폴백(FR-04) |
| 〃 | `D/IOnBallEvaluator.cs`, `D/StandInOnBallEval.cs` | 070·071 자리: 드리블 성공 확률(Dribbling·민첩·압박 vs 최근접 수비 Tackling 대체), 슈팅 xG(거리·각도·압박·Finishing/LongShots 로지스틱 대체), 크로스 성공(Crossing·박스 안 동료 수·008 뜬공 xPass), 다이빙 PK 기대 가치(박스 안이면 PK xG 대체 0.76 `[대체]`, 밖이면 프리킥 기대 가치 `[대체]`). 오프사이드 위험(FR-15): 수신자가 오프사이드 위치면 xPass × (1 − 회피 정도), 회피 = 0.3 + 0.6·(Vision + Decisions)/200 `[제안]` |
| 〃 | `D/IChemistrySource.cs`, `D/StandInChemistry.cs` | FR-12: 033 스냅샷 대체(슬롯 쌍 0~1, 기본 0). 배수식 1 + 0.15·chem(033 제안)은 033 소유 — 대체 구현이 같은 식을 쓴다 |
| 〃 | `D/OnBallScorer.cs` | FR-10·11: 이득 = p^α·sign(V)·|V|^β (V = ΔxT 또는 xG, α·β는 Directness로 보간 `[대체]`, 기본 α = β = 1), 손실 = (1 − p)·상실 지점 상대 xT. 결합 배수·케미는 이득에만(010 AC-02와 같은 규칙). Tempo → 보유 시간 효용 감소·원터치 보너스, Risk → 손실 가중, TimeWasting → 리드 중 남은 시간 기반 홀드·코너 캐리 보정 `[대체]` |
| 〃 | `D/InstructionCheck.cs` | FR-30~32: 선택 후보의 실효 지시 가중치 < 1이고, 결합 배수를 **준수율 1로 다시 낸** 점수에서 다른 후보가 최고면 "지시 무시". 재계산은 후보 점수 배열을 한 번 더 도는 것뿐(RNG 없음, 할당 0). 오프볼(010 `OffBallDecider`)도 같은 함수 사용 |
| 〃 | `D/OnBallDecider.cs` | 선수 1명 온볼 결정 파이프라인(위 "접근"). 단계와 테스트 픽스처가 같은 경로. 결과 `OnBallResult{Item, Target, Point, Pass, Dive, IgnoredInstruction, ReleaseIntent}` |
| 〃 | `D/UtilityChooser.cs` (수정) | `MaxCandidates` 16 → 24 (FR-03 상한) |
| 〃 | `D/IStyleMultiplierSource.cs`, `D/StandInStyleSource.cs` (수정) | 선수 개인 지시 가중치(`SetPlayerInstructionWeight(slot, k, w)`, 팀 지시와 곱) + `MultiplierFullCompliance(slot, k)`(FR-32 판정용) |
| 〃 | `D/OffBallDecider.cs` (수정) | 선택 뒤 `InstructionCheck` 호출(FR-31 "침투 자제" 등 오프볼도 같은 규칙) |
| 〃 | `D/DecisionTelemetry.cs`, `D/IDecisionDiagnostics.cs` (수정) | FR-40: 선수별 온볼 항목 9종 + Clear·Dive 시도 횟수, 지시 무시 횟수. 진단 `OnOnBallDecision(tick, slot, vision set, candidates)` — 시야 부채꼴·점수 막대 공급(040이 그림). 꺼지면 0 B |
| 〃 | `D/DecisionParams.cs`, `D/DecisionTuningIds.cs` | 새 튜닝 로드 |
| 〃 | `ME/Actions/TechnicalAttributes.cs` (수정) | 대체 기술 능력치 4개 선택 인자 |
| 〃 | `ME/Match/Stages/UtilityDecisionStage.cs` (수정) | 인플레이·외부 입력 없음·공 소유자 → `OnBallDecider`. 배역이면 의도 패킷(다음 패스 대상·단계 행동)을 넘기고 `TryDecide`의 공 동작은 쓰지 않는다. 결과 실행: 패스·스루·크로스 → `ActionSystem.IntentAction`(→ 008 실행), 슈팅 → 기존 슛 `BallAction`(조준 오차는 대체 그대로, 071), 걷어내기 → Clearance, 드리블 → 대체 1:1 판정 + Carry, 캐리·홀드 → `Decision.Carry`(홀드는 속도 0·볼 보호), 다이브 → 이벤트. 보유 시간 하한(`HoldTicks`) 동안은 결정하지 않고 캐리(지금과 같은 리듬, Tempo로 보정). `RememberPass` 경로 유지 |
| 〃 | `ME/Match/Stages/StandIn/StandInOnBall.cs` (수정) | 슛·걷어내기 `BallAction` 생성 함수를 공개 정적 함수로(072가 재사용). 006 대체 파이프라인(`SimpleDecisionStage`) 동작·해시 불변 |
| 〃 | `ME/Scenarios/ScenarioRuntime.cs`, `D/ScenarioIntentAdapter.cs` (수정) | 배역 소유자에게도 의도 패킷 노출(다음 패스 대상 = 현재 단계 패스 대상 슬롯, 단계 행동 → 온볼 항목). 소유자가 의도 밖 후보를 고르면 기존 `ReleaseOpportunistic` 1건(AC-07) |
| 〃 | `ME/Match/Events/SimEventKind.cs` (수정) | `InstructionIgnored = 901`(A = 지시 고려 항목, B = 선수 슬롯, 결과는 같은 선수의 다음 공 사건으로 025가 묶음 — F0 = 실효 가중치), `DiveAttempt = 902`(A = 접촉 강도 구간 0 없음·1 약함, 012 FR-27 입력). `Dribble`(203) B 비트에 성공/실패 |
| — | `Presentation/Players/Data/043-anim-map.json` | 새 이벤트 2개 none(043 매핑 테스트 규칙) |
| — | `Data/tuning/decision.json` | 아래 "데이터" |
| RoadToPro.Tests.EditMode | `T/OnBall*.cs`, `T/OnBallFixture.cs` | 테스트 전략 표 |
| RoadToPro.Tests.Performance | `P/OnBallPerfTests.cs` | AC-12 Mono 기준선 |
| RoadToPro.Verification | 벤치 드라이버 `OnBallBenchWorkloads` (`--spec 072`) | AC-12 판정(D17) |

의존 방향 확인: 전부 `MatchEngine` 안(→ Core만). UnityEngine 비참조. `.claude/rules/10-csharp.md` 위반 없음.

## 데이터 · 스키마
- `Data/tuning/decision.json`에 `dec.onball.*` 추가(기존 형식 id·default·min·max·unit·tag·source).
  - `[제안]`(스펙 '베스트 케이스' 채택): 후보 상한 24, 드리블 8방향, 시야각 밖 지각 지연 0.3 s, 오프사이드 회피 0.3 + 0.6·s, 패스 속도 10/14/18/22 m/s(008 값 참조, 중복 정의 안 함), 스루 리드 0/2/4/6 m(008 참조).
  - 스펙 확정식: 시야 r = 20 + 30s·k = round(3 + 5s), 케미 1 + 0.15·chem(033 제안), α = β = 1 기본.
  - `[대체]`(스펙 `[미정]` 자리, 017·070·071·003 연결 때 보정): 시야각 120~220°, Directness → β 0.7~1.3·α 1.0~0.8, Tempo 보유 감쇠·원터치 보너스, Risk 손실 가중 0.6~1.4, TimeWasting 계수, 드리블·슈팅·크로스 대체 로지스틱 계수, 슈팅 사거리 30 m, 측면 구역(터치라인 18 m 안·상대 진영 1/3), 다이빙 PK 기대 가치 0.76·프리킥 0.05, 성향 다이빙 가중치(003 전 기본 1.0), 다이빙 후보 조건(접촉 직전 = 최근접 상대 1.5 m 안·접근 중), 드리블 성공 후 태클 차단 1 s, 걷어내기 압박 임계.
- 대체 값은 리포트에 표로 남긴다. 판정은 방향·유의성(이항 p < 0.01)과 스펙 수치(AC-01·02·08)로만.
- 세이브 스키마 변경 없음. 스냅샷: 단계 5 섹션에 온볼 상태(드리블 태클 차단 만료 틱·대상) 추가 → `FormatVersion` 4 → 5, 왕복 테스트.

## 결정론 · 성능
- RNG: `ctx.DecisionRng`, 010 FR-25 순서 그대로 — 소유자 차례에서 ① 집중 저하 굴림 1회(010 공통) ② (의도 단계가 바뀐 경우만) 준수 굴림 1회 ③ 후보 생성 순서대로 노이즈 표본(후보 수만큼) ④ softmax 1회 ⑤ 드리블 선택 시 대체 1:1 판정 1회. 후보 생성 순서 고정: 패스(시야 집합 거리순 × 속도·리드) → 드리블(8방향 고정 순) → 슈팅 → 크로스 → 캐리 → 홀드 → 다이브. 동점 = 생성 순서.
- 지시 무시 재계산·텔레메트리·진단은 RNG를 쓰지 않는다(AC-11).
- 할당 0: 후보 버퍼 24·시야 버퍼 10·점수 분해는 생성 시 고정 배열. Dictionary·LINQ 없음.
- 성능 판정은 IL2CPP 벤치(D17). **위험**: 008 볼 소유자 평가가 이미 IL2CPP 평균 11.2 µs(예산 12, 2026-11-13 완화)인데 스펙 예산은 "008 평가 포함 평균 8 µs". 시야 k ≤ 8로 평가 대상이 10 → 최대 8명으로 줄고, 피치 컨트롤 질의는 상위 후보만이라 줄어드는 몫도 있지만, 드리블·슈팅·크로스가 더해진다. T15 측정에서 넘으면 원인 분해(008 평가 / 후보 생성 / 선택기) 표와 함께 사용자에게 묻는다(예산 완화 또는 소유자 결정 주기 조정 — 임의로 정하지 않음).

## 테스트 전략
픽스처: `OnBallFixture`(MatchContext 없이 `OnBallDecider`에 선수 위치·능력치·지시·버릇·케미·의도 주입) + 헤드리스 `MatchSim`.

| AC | 테스트 / E2E 시나리오 | 종류 | 프로필 |
|---|---|---|---|
| AC-01 | `Vision_Vis1_Radius20_Max3`, `Vision_Vis20_Radius50_Max8`, `Vision_OutsideAngle_DelayedPosition`, `Vision_IntentTargetOutside_NotAdded` | 로직 | — |
| AC-02 | `Score_MultiplierGainOnly_0p2376Minus0p02`, `Score_HigherLossXt_LowerScore` | 로직 | — |
| AC-03 | `Fallback_Surrounded_HoldOrClear_NoNaN`, `Fallback_AllNegative_ValidChoice` | 로직 | — |
| AC-04 | `Ignore_FixedSituation1000_OneEventPerJudgedDecision`, `Ignore_Compliance1_Zero`, `Ignore_OffBallHoldRuns_SameRule` | 로직 | — |
| AC-05 | `Twin_DribbleAddict_Fixture1000` (+ 크로스 머신·중거리 난사·이타적·이기주의 4종 방향) — 헤드리스 1,000경기는 **보류**(스펙 명시: 008·070·071 연결 전 고정 픽스처 대체) | 로직 | — |
| AC-06 | `Twin_Dribbling1vs20_MoreDribbleAndSuccess` (1,000회, 이항 p < 0.01, 070 대체 성공 확률 경유) | 로직 | — |
| AC-07 | `Intent_Cutback_MatchRateHigher`, `Intent_ShotAboveThetaOpp_ReleasesOnce` | 로직 | — |
| AC-08 | `Chem_1vs0_PassScoreRatio1p15`, `Chem_DribbleShotUnaffected` | 로직 | — |
| AC-09 | `Twin_Diver_MoreDiveInBox`, `Dive_EachChoice_OneEvent902` | 로직 | — |
| AC-10 | `e2e/072-onball-overlay` — **보류**(040 디버그 뷰 전, 스펙 명시). 진단 공급 데이터는 `OnBallDiagnosticsTests`로 확인 | E2E | mac-m1-1600p |
| AC-11 | `Determinism_90min_OnBallHashSame`, `Determinism_DiagnosticsOnOff_SameHash` | 결정론 | — |
| AC-12 | `Perf_OwnerDecision_P95`, `Perf_VisionSet`, `Perf_OnBall_ZeroGc` + IL2CPP 벤치 `--spec 072` | 성능 | mac-m1-1600p |
| AC-13 | **보류** — 003·007·033·070·071·012 연결 때 AC-01~09 재실행 | 로직 | — |

추가(회귀 보호): `SimpleDecisionStage_HashUnchanged`(006 대체 파이프라인 불변), 008·010·011·013·058 기존 테스트(`--scope auto` = match 범위). 013 테스트 중 배역 소유자 동작이 바뀌어 기대값이 달라지는 것은 목록으로 보고 후 갱신.

## 버그 방지
- 후보 0·모든 점수 음수: 강제 폴백 + `UtilityChooser` 정규화(ε) → NaN 없음(AC-03).
- 동료 수 감소(퇴장·부상): 시야 집합 k보다 동료가 적어도 정상(`Vision_FewerTeammates_Ok`).
- 오프사이드 위치 동료: 후보 유지·xPass만 감소(`Offside_CandidateKept_RiskApplied`).
- 의도 대상이 시야 밖·퇴장·오프사이드: 일치도 0(`Intent_TargetGone_MatchZero`).
- 금지 지시(가중치 0) + 버릇 2.0 + 준수율 1: 후보 제외(003 FR-31, `Ignore_ForbiddenFullCompliance_Excluded`).
- 공중볼(소유자 없음): 온볼 후보 생성 안 함 — 단계 분기에서 보장(`Stage_NoOwner_NoOnBall`).
- 다이빙·슈팅 동점: 생성 순서(슈팅이 먼저)(`Tie_ShotBeforeDive`).
- 드리블 1:1 실패 뒤 같은 틱 태클 굴림 중복: 실패 처리 틱엔 대체 태클을 돌리지 않음(`Dribble_FailNoDoubleTackle`).
- 피치 경계 드리블 방향: 경계 밖 목표 제외(`Dribble_OutOfPitchDirExcluded`).
- 데드볼 재시작·PK는 072를 타지 않음(`Stage_RestartUsesStandIn`).
- 스냅샷 왕복: 온볼 상태 포함 해시 동일(`Snapshot_RoundTrip_OnBall`).

## 위험 · 미정
- **경기 양상 변화**: 소유자 결정이 바뀌면 득점·패스 성공률·소유 전환 빈도(010 T14a에서 문제 된 "약 2 s마다 소유 변경")가 달라진다. 이번 판정에 평균 득점은 넣지 않고(017) 측정값(득점·패스 성공·드리블·슈팅 수·소유 지속)을 리포트에 남긴다. E2E 고정 경기(`standin.shotProb 0.05` 등)는 072가 대체 경로를 안 쓰게 되면 효과가 없어지므로, 058 하이라이트 장면은 결과가 바뀌면 다시 찍어 육안 확인을 받는다.
- **성능**: 위 "결정론 · 성능" 위험 — 측정 후 사용자 결정.
- `[미정]` 3개(시야각·α β 범위·다이빙 가중치)는 `[대체]` 시작값. 017·003 보정 대상.
- AC-10 문구의 "기준 이미지·smoke"는 2026-11-11 폐지 규칙에 따라 `mac-m1-1600p` 1프로필 육안으로 읽는다. 스펙 "검증 · 증거"의 `logic.png`는 `report.html`로 읽는다(2026-11-12 PNG 폐지).
